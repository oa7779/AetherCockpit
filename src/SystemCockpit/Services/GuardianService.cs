using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SystemCockpit.Services;

internal sealed class GuardianServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public GuardianServiceHandle() : base(true) { }
    protected override bool ReleaseHandle() => GuardianService.CloseServiceHandle(handle);
}
internal static class GuardianService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Config
    {
        internal uint Type, Start, Error;
        internal IntPtr Binary, Group;
        internal uint Tag;
        internal IntPtr Dependencies, Account, Display;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Status { internal uint Type, State, Accepted, Win32Exit, SpecificExit, Checkpoint, WaitHint, Pid, Flags; }
    private static Win32Exception Error() => new(Marshal.GetLastWin32Error());
    private static GuardianServiceHandle Open(string name, uint rights)
    {
        if (name.Length is < 1 or > 256 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.')))
            throw new ArgumentException("Nicht unterstützte exakte Dienstidentität.");
        using var manager = OpenSCManager(null, null, 1);
        if (manager.IsInvalid) throw Error();
        var service = OpenService(manager, name, rights);
        if (service.IsInvalid) { var error = Error(); service.Dispose(); throw error; }
        return service;
    }
    internal static GuardianTarget Read(string name)
    {
        using var service = Open(name, 1 | 4 | 8);
        return Read(service, name);
    }
    private static GuardianTarget Read(GuardianServiceHandle service, string name)
    {
        if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<Status>(), out _)) throw Error();
        if (status.State is not (1 or 4)) throw new InvalidOperationException("Dienst befindet sich in einem Übergangs-/unbekannten Zustand.");
        QueryServiceConfig(service, IntPtr.Zero, 0, out var needed);
        if (needed is < 1 or > 65536) throw Error();
        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!QueryServiceConfig(service, buffer, needed, out _)) throw Error();
            var config = Marshal.PtrToStructure<Config>(buffer);
            var binary = Marshal.PtrToStringUni(config.Binary) ?? "";
            var account = Marshal.PtrToStringUni(config.Account) ?? "";
            var dependencies = new List<string>();
            for (var pointer = config.Dependencies; pointer != IntPtr.Zero;)
            {
                var part = Marshal.PtrToStringUni(pointer);
                if (string.IsNullOrEmpty(part)) break;
                dependencies.Add(part); pointer += (part.Length + 1) * 2;
                if (dependencies.Count > 256) throw new InvalidOperationException("Dienstabhängigkeiten überschreiten den Vertrag.");
            }
            if (config.Type != 0x10 || config.Start is 0 or 1 || string.IsNullOrWhiteSpace(binary) ||
                binary.Contains(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase) ||
                binary.Contains("SystemRoot", StringComparison.OrdinalIgnoreCase) ||
                binary.Contains("Aether", StringComparison.OrdinalIgnoreCase) ||
                binary.Contains("SystemCockpit", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Windows-, Treiber-, gemeinsame oder AETHER-Dienste sind in Guardian nicht änderbar.");
            if (!QueryServiceConfig2(service, 12, out var protection, 4, out _)) throw Error();
            if (protection != 0) throw new InvalidOperationException("Windows meldet einen geschützten Dienst.");
            if (!EnumDependentServices(service, 1, IntPtr.Zero, 0, out var dependentBytes, out var count) &&
                Marshal.GetLastWin32Error() != 234) throw Error();
            if (count > 0 || dependentBytes > 0)
                throw new InvalidOperationException("Aktive abhängige Dienste vorhanden; kein kaskadierender Eingriff durch Guardian.");
            var fields = new Dictionary<string, string>
            {
                ["binary"] = binary, ["account"] = account, ["startType"] = config.Start.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["dependencies"] = string.Join(" | ", dependencies), ["processId"] = status.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["protection"] = "Windows LaunchProtected=0; eigener Win32-Prozess; keine aktiven abhängigen Dienste"
            };
            var identity = string.Join("\n", name.ToUpperInvariant(), binary, account, config.Type, config.Start, fields["dependencies"]);
            return new("service", name, EvidenceFile.Hash(identity), status.State == 4 ? "Running" : "Stopped", fields);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    internal static async Task<string> ExecuteAsync(GuardianPlan plan, CancellationToken token)
    {
        var rights = plan.Action switch { "start" => 16u, "stop" => 32u, "restart" => 16u | 32u,
            _ => throw new InvalidOperationException("Nicht erlaubte Dienstaktion.") };
        using var service = Open(plan.Before.Id, 1 | 4 | 8 | rights);
        if (!GuardianCore.Matches(plan.Before, Read(service, plan.Before.Id)))
            throw new InvalidOperationException("Geöffnete Dienstinstanz oder Konfiguration hat sich geändert.");
        async Task Wait(string state)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(25))
            {
                token.ThrowIfCancellationRequested();
                if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<Status>(), out _)) throw Error();
                if (status.State == (state == "Running" ? 4u : 1u)) return;
                await Task.Delay(150, token).ConfigureAwait(false);
            }
            throw new TimeoutException("Dienststatus nicht innerhalb des begrenzten Wartefensters bestätigt.");
        }
        if (plan.Action is "stop" or "restart")
        {
            if (!ControlService(service, 1, out _)) throw Error();
            await Wait("Stopped").ConfigureAwait(false);
        }
        if (plan.Action is "start" or "restart")
        {
            token.ThrowIfCancellationRequested();
            if (!StartService(service, 0, IntPtr.Zero)) throw Error();
            await Wait("Running").ConfigureAwait(false);
        }
        return "Windows-Dienstanforderung ausgeführt; Nachherprüfung folgt.";
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern GuardianServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern GuardianServiceHandle OpenService(GuardianServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig(GuardianServiceHandle service, IntPtr config, uint size, out uint needed);
    [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfig2W", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig2(GuardianServiceHandle service, uint level, out uint value, uint size, out uint needed);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(GuardianServiceHandle service, int info, out Status status, int size, out int needed);
    [DllImport("advapi32.dll", EntryPoint = "EnumDependentServicesW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDependentServices(GuardianServiceHandle service, uint state, IntPtr buffer, uint size, out uint needed, out uint count);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(GuardianServiceHandle service, uint control, out Status status);
    [DllImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(GuardianServiceHandle service, uint count, IntPtr arguments);
}
