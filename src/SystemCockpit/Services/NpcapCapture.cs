using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SystemCockpit.Services;

public sealed record CaptureDevice(string Name, string Description);

internal sealed class NpcapCapture : IDisposable
{
    private IntPtr _library;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FindDevices(out IntPtr devices, StringBuilder error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FreeDevices(IntPtr devices);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr OpenDevice([MarshalAs(UnmanagedType.LPStr)] string name, int snaplen, int promiscuous, int timeout, StringBuilder error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NextPacket(IntPtr device, out IntPtr header, out IntPtr bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CloseDevice(IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DataLink(IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetError(IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetNonBlock(IntPtr device, int enabled, StringBuilder error);
    private readonly FindDevices _find;
    private readonly FreeDevices _free;
    private readonly OpenDevice _open;
    private readonly NextPacket _next;
    private readonly CloseDevice _close;
    private readonly DataLink _link;
    private readonly GetError _error;
    private readonly SetNonBlock _nonblock;
    public const int SnapLength = 128;
    public const long MaxFileBytes = 100_000_000;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(15);
    internal const string OfficialDownloadPage = "https://npcap.com/#download";
    public static string LibraryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Npcap", "wpcap.dll");

    public NpcapCapture()
    {
        if (!File.Exists(LibraryPath))
            throw new FileNotFoundException("Npcap ist nicht installiert. AETHER liefert keinen Capture-Treiber mit; offiziellen Download und Lizenzbedingungen separat pruefen.", LibraryPath);
        _library = LoadLibraryEx(LibraryPath, IntPtr.Zero, 0x00000100 | 0x00000800);
        if (_library == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            T Load<T>(string name) where T : Delegate =>
                Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
            _find = Load<FindDevices>("pcap_findalldevs");
            _free = Load<FreeDevices>("pcap_freealldevs");
            _open = Load<OpenDevice>("pcap_open_live");
            _next = Load<NextPacket>("pcap_next_ex");
            _close = Load<CloseDevice>("pcap_close");
            _link = Load<DataLink>("pcap_datalink");
            _error = Load<GetError>("pcap_geterr");
            _nonblock = Load<SetNonBlock>("pcap_setnonblock");
        }
        catch (EntryPointNotFoundException) { Dispose(); throw; }
    }

    public IReadOnlyList<CaptureDevice> Devices()
    {
        var error = new StringBuilder(512);
        if (_find(out var devices, error) != 0) throw new InvalidOperationException("Npcap: " + error);
        try
        {
            var result = new List<CaptureDevice>();
            for (var cursor = devices; cursor != IntPtr.Zero; cursor = Marshal.ReadIntPtr(cursor))
            {
                var name = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(cursor, IntPtr.Size)) ?? "";
                var description = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(cursor, 2 * IntPtr.Size)) ?? name;
                result.Add(new(name, description));
            }
            return result;
        }
        finally { _free(devices); }
    }

    public string Run(string name, string directory, Action<PacketSummary> receive, CancellationToken token,
        Action<string> started)
    {
        if (!Devices().Any(device => device.Name == name)) throw new ArgumentException("Npcap-Schnittstelle nicht verfuegbar.");
        var error = new StringBuilder(512);
        var handle = _open(name, SnapLength, 0, 250, error);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Npcap: " + error);
        try
        {
            if (_link(handle) != 1)
                throw new NotSupportedException("Diese Schnittstelle liefert keine Ethernet-Frames. Loopback/WLAN-Rohformate werden nicht als Ethernet fehlinterpretiert.");
            if (_nonblock(handle, 1, error) != 0) throw new InvalidOperationException("Npcap nicht abbrechbar: " + error);
            Directory.CreateDirectory(directory);
            Prune(directory, DateTime.UtcNow);
            var path = Path.Combine(directory, $"aether-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.pcap");
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new CaptureFile(stream);
            started(path);
            var elapsed = Stopwatch.StartNew();
            while (!token.IsCancellationRequested && elapsed.Elapsed < MaxDuration)
            {
                var status = _next(handle, out var header, out var bytes);
                if (status == 0) { token.WaitHandle.WaitOne(20); continue; }
                if (status == -2) break;
                if (status != 1) throw new IOException("Npcap: " + Marshal.PtrToStringAnsi(_error(handle)));
                var native = Marshal.PtrToStructure<PacketHeader>(header);
                if (native.Captured > SnapLength || native.Captured > native.Length)
                    throw new InvalidDataException("Npcap meldet eine ungueltige Paketlaenge.");
                var packet = new byte[native.Captured];
                Marshal.Copy(bytes, packet, 0, packet.Length);
                if (!writer.TryWrite(native.Seconds, native.Microseconds, native.Length, packet, elapsed.Elapsed)) break;
                var time = DateTimeOffset.FromUnixTimeSeconds(native.Seconds).AddTicks(native.Microseconds * 10L);
                receive(PacketAnalysis.Parse(packet, checked((int)native.Length), time));
            }
            return path;
        }
        finally { _close(handle); }
    }

    internal static void WriteHeader(BinaryWriter writer)
    {
        writer.Write(0xa1b2c3d4u);
        writer.Write((ushort)2); writer.Write((ushort)4);
        writer.Write(0); writer.Write(0u); writer.Write((uint)SnapLength); writer.Write(1u);
    }

    internal static void Prune(string directory, DateTime now)
    {
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("aether-*.pcap"))
            if (file.LastWriteTimeUtc < now.AddDays(-7)) file.Delete();
        var files = new DirectoryInfo(directory).EnumerateFiles("aether-*.pcap").OrderBy(file => file.LastWriteTimeUtc).ToArray();
        var bytes = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (bytes <= 4 * MaxFileBytes) break;
            bytes -= file.Length;
            file.Delete();
        }
    }

    public void Dispose()
    {
        if (_library == IntPtr.Zero) return;
        NativeLibrary.Free(_library);
        _library = IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PacketHeader { public uint Seconds, Microseconds, Captured, Length; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
}
