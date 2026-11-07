using System.ComponentModel;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace SystemCockpit.Services;

public sealed class NativeMetrics
{
    private ulong _idle, _kernel, _user;
    private bool _hasCpuBaseline;
    private long _networkTimestamp;
    private Dictionary<string, (long Received, long Sent)> _network = [];

    public LiveSample Read()
    {
        var cpu = ReadCpu();
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var (download, upload) = ReadNetwork();
        var volumes = new List<Check>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            try
            {
                if (!drive.IsReady)
                {
                    volumes.Add(new(drive.Name, "Nicht bereit", "unknown", "Windows meldet keinen Zugriff."));
                    continue;
                }
                var freePercent = 100d * drive.AvailableFreeSpace / drive.TotalSize;
                volumes.Add(new(drive.Name, $"{drive.AvailableFreeSpace / 1073741824d:0.0} GB frei",
                    freePercent < 10 ? "warning" : "ok",
                    $"{drive.TotalSize / 1073741824d:0.0} GB gesamt | {freePercent:0.0}% frei", freePercent));
            }
            catch (IOException ex) { volumes.Add(new(drive.Name, "Lesefehler", "unknown", ex.Message)); }
            catch (UnauthorizedAccessException ex) { volumes.Add(new(drive.Name, "Zugriff verweigert", "unknown", ex.Message)); }
        }
        return new(DateTimeOffset.Now, cpu, memory.MemoryLoad,
            (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824d,
            memory.TotalPhysical / 1073741824d, download, upload, volumes, []);
    }

    private double? ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        double? result = null;
        if (_hasCpuBaseline)
        {
            var total = (kernel - _kernel) + (user - _user);
            if (total > 0) result = CalculateCpu(idle - _idle, total);
        }
        (_idle, _kernel, _user, _hasCpuBaseline) = (idle, kernel, user, true);
        return result;
    }

    public static double CalculateCpu(ulong idleDelta, ulong totalDelta) =>
        totalDelta == 0 ? throw new ArgumentOutOfRangeException(nameof(totalDelta))
        : Math.Clamp(100d * (1d - (double)idleDelta / totalDelta), 0, 100);

    private (double? Download, double? Upload) ReadNetwork()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var elapsed = _networkTimestamp == 0 ? 0
            : (double)(now - _networkTimestamp) / System.Diagnostics.Stopwatch.Frequency;
        var current = new Dictionary<string, (long Received, long Sent)>();
        long received = 0, sent = 0;
        var matched = false;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n =>
                     n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            var stats = adapter.GetIPStatistics();
            current[adapter.Id] = (stats.BytesReceived, stats.BytesSent);
            if (_network.TryGetValue(adapter.Id, out var previous) &&
                stats.BytesReceived >= previous.Received && stats.BytesSent >= previous.Sent)
            {
                received += stats.BytesReceived - previous.Received;
                sent += stats.BytesSent - previous.Sent;
                matched = true;
            }
        }
        _network = current;
        _networkTimestamp = now;
        return matched && elapsed > 0
            ? (received * 8d / elapsed / 1_000_000, sent * 8d / elapsed / 1_000_000)
            : (null, null);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}
