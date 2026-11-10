using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace SystemCockpit.Services;

public sealed record ProcessMetric(string Name, int Id, double? CpuPercent, double? MemoryMb,
    int? Threads, int? Handles, double? ReadMbps, double? WriteMbps, string Status, long StartTicks = 0)
{
    public ProcessIdentity Identity => new(Id, StartTicks);
    public int? ParentPid { get; init; }
    public double? PrivateMb { get; init; }
    public int? Session { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
    public string CpuDisplay => CpuPercent is { } v ? $"{v:0.0}%" : "--";
    public double CpuLevel => CpuPercent ?? 0;
    public string MemoryDisplay => MemoryMb is { } v ? $"{v:0.0}" : "--";
    public string ReadDisplay => ReadMbps is { } v ? $"{v:0.00}" : "--";
    public string WriteDisplay => WriteMbps is { } v ? $"{v:0.00}" : "--";
}
public sealed record AdapterMetric(string Id, string Name, double? DownloadMbps, double? UploadMbps, long Speed);
public sealed record DashboardSnapshot(DateTimeOffset Timestamp, IReadOnlyList<double?> Cores,
    IReadOnlyList<ProcessMetric> Processes, IReadOnlyList<AdapterMetric> Adapters,
    double CommitGb, double CommitLimitGb, double CacheGb, long Threads, long Handles,
    TimeSpan Uptime, IReadOnlyList<string> Issues);

public sealed class DashboardMetrics
{
    private ProcessorTimes[]? _cores;
    private long _timestamp;
    private Dictionary<int, (long Start, double Cpu, ulong Read, ulong Write)> _processes = [];
    private Dictionary<string, (long Read, long Write)> _adapters = [];

    public DashboardSnapshot Read() => Read(CancellationToken.None);
    public DashboardSnapshot Read(CancellationToken token)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = _timestamp == 0 ? 0 : (double)(now - _timestamp) / Stopwatch.Frequency;
        var issues = new List<string>();
        var cores = ReadCores(issues);
        var performance = new PerformanceInfo { Size = (uint)Marshal.SizeOf<PerformanceInfo>() };
        if (!GetPerformanceInfo(ref performance, performance.Size)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var processes = new List<ProcessMetric>();
        var baseline = new Dictionary<int, (long, double, ulong, ulong)>();
        IReadOnlyDictionary<int, int> parents;
        try { parents = ProcessNative.Parents(); }
        catch (Win32Exception ex) { Trace.TraceError("Prozesseltern: {0}", ex); issues.Add("Prozesseltern nicht lesbar: " + ex.NativeErrorCode); parents = new Dictionary<int, int>(); }
        foreach (var process in ProcessNative.EnumerateProcesses())
        {
            using (process)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var id = process.Id;
                    var name = process.ProcessName;
                    var start = process.StartTime.ToUniversalTime().Ticks;
                    var cpu = process.TotalProcessorTime.TotalSeconds;
                    var memory = process.WorkingSet64 / 1048576d;
                    var hasIo = true;
                    var ioError = "";
                    (ulong ReadBytes, ulong WriteBytes) io = default;
                    try { io = ProcessNative.Io(new(id, start)); }
                    catch (Win32Exception ex) { hasIo = false; ioError = ex.Message; }
                    catch (InvalidOperationException ex) { hasIo = false; ioError = ex.Message; }
                    double? cpuPercent = null, read = null, write = null;
                    if (_processes.TryGetValue(id, out var old) && old.Start == start && elapsed > 0)
                    {
                        if (cpu >= old.Cpu) cpuPercent = CalculateProcessCpu(cpu - old.Cpu, elapsed, cores.Count);
                        if (hasIo && old.Read != ulong.MaxValue && io.ReadBytes >= old.Read && io.WriteBytes >= old.Write)
                        {
                            read = (io.ReadBytes - old.Read) / elapsed / 1048576d;
                            write = (io.WriteBytes - old.Write) / elapsed / 1048576d;
                        }
                    }
                    baseline[id] = (start, cpu, hasIo ? io.ReadBytes : ulong.MaxValue, hasIo ? io.WriteBytes : ulong.MaxValue);
                    processes.Add(new(name, id, cpuPercent, memory, process.Threads.Count, process.HandleCount, read, write,
                        hasIo ? "Lesbar" : $"I/O nicht lesbar: {ioError}", start)
                    {
                        ParentPid = parents.TryGetValue(id, out var parent) ? parent : null,
                        PrivateMb = process.PrivateMemorySize64 / 1048576d, Session = process.SessionId,
                        ObservedAt = DateTimeOffset.UtcNow
                    });
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Protected or exiting processes must remain visibly unknown, not appear idle.
                    processes.Add(new($"PID {process.Id}", process.Id, null, null, null, null, null, null, ex.Message)
                        { ObservedAt = DateTimeOffset.UtcNow });
                }
            }
        }
        _processes = baseline;
        var adapters = new List<AdapterMetric>();
        var adapterBaseline = new Dictionary<string, (long, long)>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(a =>
            a.OperationalStatus == OperationalStatus.Up && a.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var stats = adapter.GetIPStatistics();
                double? down = null, up = null;
                if (_adapters.TryGetValue(adapter.Id, out var old) && elapsed > 0 &&
                    stats.BytesReceived >= old.Read && stats.BytesSent >= old.Write)
                {
                    down = (stats.BytesReceived - old.Read) * 8d / elapsed / 1_000_000;
                    up = (stats.BytesSent - old.Write) * 8d / elapsed / 1_000_000;
                }
                adapterBaseline[adapter.Id] = (stats.BytesReceived, stats.BytesSent);
                adapters.Add(new(adapter.Id, adapter.Name, down, up, adapter.Speed));
            }
            catch (NetworkInformationException ex) { issues.Add($"Adapter {adapter.Name}: {ex.Message}"); }
        }
        _adapters = adapterBaseline;
        _timestamp = now;
        var pageGb = (double)performance.PageSize / 1073741824d;
        return new(DateTimeOffset.Now, cores, processes, adapters,
            performance.CommitTotal * pageGb, performance.CommitLimit * pageGb, performance.SystemCache * pageGb,
            performance.ThreadCount, performance.HandleCount, TimeSpan.FromMilliseconds(Environment.TickCount64), issues);
    }

    public static double CalculateProcessCpu(double cpuSeconds, double elapsedSeconds, int logicalProcessors)
    {
        if (!double.IsFinite(cpuSeconds) || cpuSeconds < 0 || !double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0 || logicalProcessors <= 0)
            throw new ArgumentOutOfRangeException(nameof(cpuSeconds));
        return Math.Clamp(100 * cpuSeconds / elapsedSeconds / logicalProcessors, 0, 100);
    }

    private IReadOnlyList<double?> ReadCores(List<string> issues)
    {
        // NtQuery reports all active processors, unlike Environment.ProcessorCount's process affinity limit.
        var size = Marshal.SizeOf<ProcessorTimes>();
        var buffer = Marshal.AllocHGlobal(size * 4096);
        try
        {
            var status = NtQuerySystemInformation(8, buffer, size * 4096, out var returned);
            if (status != 0 || returned <= 0 || returned % size != 0)
            {
                _cores = null;
                issues.Add($"CPU-Kernzeiten nicht verfuegbar: NTSTATUS 0x{status:X8}");
                return Enumerable.Repeat<double?>(null, Environment.ProcessorCount).ToArray();
            }
            var current = new ProcessorTimes[returned / size];
            var values = new double?[current.Length];
            for (var i = 0; i < current.Length; i++)
            {
                current[i] = Marshal.PtrToStructure<ProcessorTimes>(IntPtr.Add(buffer, i * size));
                if (_cores is { } old && old.Length == current.Length)
                {
                    var total = (current[i].Kernel - old[i].Kernel) + (current[i].User - old[i].User);
                    var idle = current[i].Idle - old[i].Idle;
                    if (total > 0 && idle >= 0) values[i] = NativeMetrics.CalculateCpu((ulong)idle, (ulong)total);
                }
            }
            _cores = current;
            return values;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorTimes { public long Idle, Kernel, User, Dpc, Interrupt; public uint InterruptCount; }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInfo
    {
        public uint Size;
        public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache,
            KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }
    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int informationClass, IntPtr buffer, int length, out int returned);
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(ref PerformanceInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
}
