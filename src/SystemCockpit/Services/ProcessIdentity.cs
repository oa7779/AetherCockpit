using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SystemCockpit.Services;

public readonly record struct ProcessIdentity(int Pid, long StartTicks)
{
    public bool IsKnown => Pid > 0 && StartTicks > 0;
    public override string ToString() => $"{Pid}@{StartTicks}";
}

internal sealed class ProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public ProcessHandle() : base(true) { }
    protected override bool ReleaseHandle() => ProcessNative.CloseHandle(handle);
}

internal static class ProcessNative
{
    internal static IEnumerable<Process> EnumerateProcesses()
    {
        var processes = Process.GetProcesses();
        try { foreach (var process in processes) yield return process; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    internal const uint Query = 0x1000, Synchronize = 0x100000, Terminate = 1;
    internal static ProcessHandle Open(ProcessIdentity identity, bool terminate = false)
    {
        if (!identity.IsKnown) throw new InvalidOperationException("Prozessidentität fehlt; keine Aktion möglich.");
        var handle = OpenProcess(Query | Synchronize | (terminate ? Terminate : 0), false, identity.Pid);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        try
        {
            if (Identity(handle, identity.Pid) != identity) throw new InvalidOperationException("PID wurde wiederverwendet. Ziel nicht verändert.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal static ProcessIdentity Identity(ProcessHandle handle, int pid)
    {
        if (!GetProcessTimes(handle, out var created, out _, out _, out _)) throw Error();
        return new(pid, DateTime.FromFileTimeUtc(created).Ticks);
    }

    internal static bool Exited(ProcessHandle handle) => WaitForSingleObject(handle, 0) switch
    {
        0 => true, 258 => false, _ => throw Error()
    };

    internal static string Path(ProcessHandle handle)
    {
        var buffer = new StringBuilder(32768);
        var length = buffer.Capacity;
        if (!QueryFullProcessImageName(handle, 0, buffer, ref length)) throw Error();
        return buffer.ToString();
    }

    internal static string Protection(ProcessHandle handle)
    {
        if (!IsProcessCritical(handle, out var critical)) throw Error();
        if (critical) return "Windows meldet einen kritischen Prozess.";
        if (!GetProcessInformation(handle, 7, out var level, 4)) throw Error();
        return level == 0xFFFFFFFE ? "" : $"Windows-Prozessschutz aktiv (Level 0x{level:X8}).";
    }

    internal static string Architecture(ProcessHandle handle)
    {
        if (!IsWow64Process2(handle, out var process, out var native)) throw Error();
        return (process == 0 ? native : process) switch
        {
            0x8664 => "x64", 0x14c => "x86", 0xaa64 => "ARM64", 0x1c4 => "ARM32",
            var value => $"Maschinencode 0x{value:X4}"
        };
    }

    internal static (string User, bool Elevated, uint Integrity) Token(ProcessHandle handle)
    {
        if (!OpenProcessToken(handle, 8, out var token)) throw Error();
        using (token)
        {
            IntPtr Read(int kind)
            {
                GetTokenInformation(token, kind, IntPtr.Zero, 0, out var size);
                if (size is < 4 or > 1048576) throw Error();
                var buffer = Marshal.AllocHGlobal(size);
                if (GetTokenInformation(token, kind, buffer, size, out _)) return buffer;
                var error = Error(); Marshal.FreeHGlobal(buffer); throw error;
            }
            var user = Read(1);
            var elevation = IntPtr.Zero;
            var integrity = IntPtr.Zero;
            try
            {
                var sid = new System.Security.Principal.SecurityIdentifier(Marshal.ReadIntPtr(user));
                elevation = Read(20);
                integrity = Read(25);
                var integritySid = Marshal.ReadIntPtr(integrity);
                var count = Marshal.ReadByte(GetSidSubAuthorityCount(integritySid));
                var rid = unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(integritySid, (uint)(count - 1))));
                return (sid.Value, Marshal.ReadInt32(elevation) != 0, rid);
            }
            finally
            {
                Marshal.FreeHGlobal(user);
                if (elevation != IntPtr.Zero) Marshal.FreeHGlobal(elevation);
                if (integrity != IntPtr.Zero) Marshal.FreeHGlobal(integrity);
            }
        }
    }

    internal static IReadOnlyDictionary<int, int> Parents()
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw Error();
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        var result = new Dictionary<int, int>();
        if (!Process32First(snapshot, ref entry)) throw Error();
        do { result[(int)entry.Pid] = (int)entry.Parent; entry.Size = (uint)Marshal.SizeOf<ProcessEntry>(); }
        while (Process32Next(snapshot, ref entry));
        if (Marshal.GetLastWin32Error() != 18) throw Error();
        return result;
    }

    internal static (ulong Read, ulong Write) Io(ProcessIdentity identity)
    {
        using var handle = Open(identity);
        if (!GetProcessIoCounters(handle, out var io)) throw Error();
        return (io.ReadBytes, io.WriteBytes);
    }

    internal static Win32Exception Error() => new(Marshal.GetLastWin32Error());
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Pid;
        public UIntPtr Heap;
        public uint Module, Threads, Parent;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern ProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(ProcessHandle handle, out long created, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(ProcessHandle handle, uint timeout);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(ProcessHandle handle, uint flags, StringBuilder path, ref int length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsProcessCritical(ProcessHandle handle, out bool critical);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessInformation(ProcessHandle handle, int info, out uint data, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsWow64Process2(ProcessHandle handle, out ushort process, out ushort native);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool TerminateProcess(ProcessHandle handle, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern ProcessHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32First(ProcessHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32Next(ProcessHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessIoCounters(ProcessHandle process, out IoCounters counters);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(ProcessHandle handle, uint access, out ProcessHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(ProcessHandle token, int kind, IntPtr data, int size, out int returned);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
}
