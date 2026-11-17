using AetherManager.Server.Middleware;
using AetherManager.Server.Services;

namespace AetherManager.Server.Endpoints;

internal static class FleetEndpoints
{
    public static void MapFleetEndpoints(this WebApplication app) =>
        app.MapGet("/api/v1/admin/fleet", async (IFleetService service, CancellationToken token) =>
            TypedResults.Ok(await service.GetAsync(token))).AddEndpointFilter<AdminTokenFilter>()
            .RequireRateLimiting("management").WithSummary("Aggregate the actual enrolled fleet, observation freshness and read-only server telemetry");
}
