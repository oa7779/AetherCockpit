using AetherOperations;

namespace AetherManager.Server.Models;

/// <summary>Actual server process/host measurements; null means unavailable, never a simulated sensor.</summary>
public sealed record ServerSnapshotResponse(DateTimeOffset SampledAt, double? HostCpuPercent, double? ProcessCpuPercent,
    int? HostProcessCount, long? ProcessWorkingSetBytes,
    long? HostMemoryTotalBytes, long? HostMemoryAvailableBytes, double HostUptimeSeconds, double? ProcessUptimeSeconds,
    DiskSnapshotResponse[] Disks, string CpuScope, string? UnavailableReason, string TemperatureStatus);
/// <summary>Read-only capacity/free-space snapshot of an actual mounted fixed volume.</summary>
public sealed record DiskSnapshotResponse(string Name, long? TotalBytes, long? AvailableBytes, string? UnavailableReason);
/// <summary>One real enrolled installation, observation freshness, policy and latest retained report metadata.</summary>
public sealed record FleetDeviceResponse(DeviceResponse Device, ManagedDeviceStatus? Management, string Liveness,
    string ReportFreshness, ReportMetadataResponse? LatestReport);
/// <summary>Aggregated enrolled fleet and server snapshot; layout is logical, not physical network topology.</summary>
public sealed record FleetResponse(DateTimeOffset GeneratedAt, int HeartbeatFreshSeconds, int ReportFreshSeconds,
    FleetDeviceResponse[] Devices, ServerSnapshotResponse Server);
