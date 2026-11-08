using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AetherManager.Server.Models;

namespace AetherManager.Server.Services;

internal interface IServerMonitoringService { ServerSnapshotResponse GetSnapshot(); }

internal sealed class ServerMonitoringService(TimeProvider clock, ILogger<ServerMonitoringService> logger)
    : BackgroundService, IServerMonitoringService
{
    private ServerSnapshotResponse _snapshot = new(clock.GetUtcNow(), null, null, null, null, null, null,
        Environment.TickCount64 / 1000d, null, [], "Host CPU: Windows processor group; server process: normalized across logical processors",
        "Awaiting first sample", "Unavailable: no temperature sensor provider");
    private TimeSpan? _previousCpu;
    private long? _previousStamp;
    private (ulong Idle, ulong Kernel, ulong User)? _previousHostCpu;
    public ServerSnapshotResponse GetSnapshot() => Volatile.Read(ref _snapshot);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
        try
        {
            do { Sample(); } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal void Sample()
    {
        var now = clock.GetUtcNow();
        double? cpu = null, hostCpu = null, processUptime = null;
        int? processCount = null;
        long? workingSet = null, total = null, available = null;
        string? reason = null;
        try
        {
            using var process = Process.GetCurrentProcess();
            var currentCpu = process.TotalProcessorTime;
            var stamp = clock.GetTimestamp();
            if (_previousCpu is { } previous && _previousStamp is { } previousStamp)
            {
                var elapsed = clock.GetElapsedTime(previousStamp, stamp).TotalSeconds;
                if (elapsed > 0) cpu = Math.Clamp((currentCpu - previous).TotalSeconds / elapsed / Environment.ProcessorCount * 100, 0, 100);
            }
            _previousCpu = currentCpu;
            _previousStamp = stamp;
            workingSet = process.WorkingSet64;
            processUptime = Math.Max(0, (now - new DateTimeOffset(process.StartTime.ToUniversalTime())).TotalSeconds);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            reason = "Process measurements unavailable";
            logger.LogWarning(ex, "Server process measurement unavailable");
        }
        Process[]? processes = null;
        try { processes = Process.GetProcesses(); processCount = processes.Length; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        { reason = "Host process count unavailable"; logger.LogWarning(ex, "Host process enumeration unavailable"); }
        finally { if (processes is not null) foreach (var process in processes) process.Dispose(); }
        if (OperatingSystem.IsWindows())
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                var current = (Idle: idle.Value, Kernel: kernel.Value, User: user.Value);
                if (_previousHostCpu is { } previous && current.Idle >= previous.Idle &&
                    current.Kernel >= previous.Kernel && current.User >= previous.User)
                {
                    var elapsed = (current.Kernel - previous.Kernel) + (current.User - previous.User);
                    var idleElapsed = current.Idle - previous.Idle;
                    if (elapsed > 0 && idleElapsed <= elapsed) hostCpu = (elapsed - idleElapsed) * 100d / elapsed;
                }
                _previousHostCpu = current;
            }
            else reason = "Host CPU measurement unavailable";
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref memory))
            {
                total = checked((long)memory.TotalPhysical);
                available = checked((long)memory.AvailablePhysical);
            }
            else reason = "Host memory measurement unavailable";
        }
        else reason = "Host memory provider only available on Windows";
        var disks = new List<DiskSnapshotResponse>();
        try
        {
            foreach (var disk in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed).Take(16))
            {
                try { disks.Add(new(disk.Name, disk.TotalSize, disk.AvailableFreeSpace, null)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { disks.Add(new(disk.Name, null, null, "Volume capacity unavailable")); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { reason = "Volume enumeration unavailable"; logger.LogWarning(ex, "Server volume enumeration unavailable"); }
        Volatile.Write(ref _snapshot, new(now, hostCpu, cpu, processCount, workingSet, total, available, Environment.TickCount64 / 1000d,
            processUptime, disks.ToArray(), "Host CPU: Windows processor group; server process: normalized across logical processors", reason,
            "Unavailable: no temperature sensor provider"));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint Low, High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);
}
