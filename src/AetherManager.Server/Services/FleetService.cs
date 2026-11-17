using AetherManager.Server.Models;

namespace AetherManager.Server.Services;

internal interface IFleetService { Task<FleetResponse> GetAsync(CancellationToken token); }

internal sealed class FleetService(IManagerStore store, IManagementService management, IServerMonitoringService monitoring,
    TimeProvider clock) : IFleetService
{
    public async Task<FleetResponse> GetAsync(CancellationToken token)
    {
        var devices = await store.GetDevicesAsync(token);
        var statuses = (await management.GetStatusesAsync(token)).ToDictionary(x => x.DeviceId);
        var reports = (await store.GetLatestReportsAsync(token)).ToDictionary(x => x.DeviceId);
        var now = clock.GetUtcNow();
        return new(now, 120, 3600, devices.Select(device =>
        {
            statuses.TryGetValue(device.Id, out var status);
            reports.TryGetValue(device.Id, out var report);
            return new FleetDeviceResponse(device, status, Freshness(status?.LastSeenAt, now, 120),
                Freshness(report?.ReceivedAt, now, 3600), report);
        }).ToArray(), monitoring.GetSnapshot());
    }

    internal static string Freshness(DateTimeOffset? at, DateTimeOffset now, int seconds) =>
        at is null ? "never-observed" : at <= now && now - at <= TimeSpan.FromSeconds(seconds) ? "fresh" : "stale";
}
