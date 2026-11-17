using AetherManager.Server.Middleware;
using AetherManager.Server.Models;
using AetherManager.Server.Services;

namespace AetherManager.Server.Endpoints;

internal static class GraphEndpoints
{
    public static void MapGraphEndpoints(this WebApplication app)
    {
        var cloud = app.MapGroup("/api/v1/admin/cloud").AddEndpointFilter<AdminTokenFilter>()
            .RequireRateLimiting("cloud");
        cloud.MapGet("/status", (IMicrosoftGraphService graph) =>
            TypedResults.Ok(graph.GetStatus())).WithSummary("Read Microsoft Graph configuration status");
        cloud.MapGet("/devices", async (IMicrosoftGraphService graph, CancellationToken token) =>
            TypedResults.Ok(await graph.GetDevicesAsync(token))).WithSummary("Read Intune managed devices");
        cloud.MapGet("/deployments", async (IMicrosoftGraphService graph, CancellationToken token) =>
            TypedResults.Ok(await graph.GetDeploymentsAsync(token))).WithSummary("Read Windows Autopatch deployments");
        cloud.MapPost("/devices/{id:guid}/sync", async (Guid id, HttpContext context,
            IMicrosoftGraphService graph, IAccountService accounts, CancellationToken token) =>
        {
            await accounts.RecordAdministrativeEventAsync(AdminTokenFilter.Actor(context),
                $"Microsoft Graph Intune sync requested for device {id:D}", token);
            await graph.SyncDeviceAsync(id, token);
            return TypedResults.NoContent();
        }).WithSummary("Request one explicitly confirmed Intune device sync");
        cloud.MapPost("/deployments/{id:guid}/state", async (Guid id, GraphDeploymentStateRequest request,
            HttpContext context, IMicrosoftGraphService graph, IAccountService accounts, CancellationToken token) =>
        {
            var state = request.Paused ? "pause" : "resume";
            await accounts.RecordAdministrativeEventAsync(AdminTokenFilter.Actor(context),
                $"Microsoft Graph Autopatch deployment {id:D} requested {state}", token);
            await graph.SetDeploymentPausedAsync(id, request.Paused, token);
            return TypedResults.Ok(new GraphActionResponse(
                request.Paused ? "pauseRequested" : "resumeRequested",
                "Microsoft Graph hat die Zustandsänderung angenommen."));
        }).WithSummary("Request one explicitly confirmed Autopatch deployment state change");
    }
}
