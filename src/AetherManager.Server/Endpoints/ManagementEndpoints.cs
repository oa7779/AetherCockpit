using AetherManager.Server.Middleware;
using AetherManager.Server.Services;
using AetherOperations;

namespace AetherManager.Server.Endpoints;

internal static class ManagementEndpoints
{
    public static void MapManagementEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/v1/admin/management").AddEndpointFilter<AdminTokenFilter>()
            .RequireRateLimiting("management");
        admin.MapGet("/actions", (CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return TypedResults.Ok(ManagedActionCatalog.All.Select(action => new ManagedActionDescription(
                    action.Id, action.Name, action.Category, action.ChangesSystem, action.CanAutoRepair, action.RequiredCommands)));
            }).WithSummary("List fixed administrative actions and their risk classification");
        admin.MapGet("/devices", async (IManagementService service, CancellationToken token) =>
            TypedResults.Ok(await service.GetStatusesAsync(token))).WithSummary("Read client liveness and permissions");
        admin.MapPut("/devices/{id:guid}/policy", async (Guid id, ManagementPolicy request,
            IManagementService service, CancellationToken token) =>
            TypedResults.Ok(await service.SetPolicyAsync(id, request, token))).WithSummary("Set device-level management permissions");
        admin.MapPost("/devices/{id:guid}/jobs", async (Guid id, CreateManagedJobRequest request,
            IManagementService service, CancellationToken token) =>
            {
                var job = await service.CreateJobAsync(id, request, token);
                return TypedResults.Created($"/api/v1/admin/management/jobs/{job.Id}", job);
            }).WithSummary("Queue a fixed action with explicit confirmation for changes");
        admin.MapGet("/jobs", async (Guid? deviceId, IManagementService service, CancellationToken token) =>
            TypedResults.Ok(await service.GetJobsAsync(deviceId, token))).WithSummary("List the latest 500 jobs");
        admin.MapGet("/jobs/{id:guid}", async Task<Microsoft.AspNetCore.Http.HttpResults.Results<
            Microsoft.AspNetCore.Http.HttpResults.Ok<ManagedJob>, Microsoft.AspNetCore.Http.HttpResults.NotFound>> (
            Guid id, IManagementService service, CancellationToken token) =>
            {
                var job = await service.GetJobAsync(id, token);
                return job is null ? TypedResults.NotFound() : TypedResults.Ok(job);
            }).WithSummary("Read a queued or completed job");
        admin.MapPost("/jobs/{id:guid}/cancel", async (Guid id, IManagementService service, CancellationToken token) =>
            TypedResults.Ok(await service.CancelAsync(id, token))).WithSummary("Cancel an unclaimed job");
        admin.MapGet("/audit", async (IManagementService service, CancellationToken token) =>
            TypedResults.Ok(await service.GetAuditAsync(token))).WithSummary("Read the latest 500 audit transitions");

        var device = app.MapGroup("/api/v1/management").AddEndpointFilter<DeviceTokenFilter>()
            .RequireRateLimiting("device-management");
        device.MapPost("/heartbeat", async (HeartbeatRequest request, HttpContext context,
            IManagementService service, CancellationToken token) =>
            TypedResults.Ok(await service.HeartbeatAsync(DeviceId(context), request, token)))
            .WithSummary("Report client liveness and local consent");
        device.MapPost("/claim", async Task<Microsoft.AspNetCore.Http.HttpResults.Results<
            Microsoft.AspNetCore.Http.HttpResults.Ok<ManagedJobClaim>, Microsoft.AspNetCore.Http.HttpResults.NoContent>> (
            HttpContext context, IManagementService service, CancellationToken token) =>
            {
                var claim = await service.ClaimAsync(DeviceId(context), token);
                return claim is null ? TypedResults.NoContent() : TypedResults.Ok(claim);
            }).WithSummary("Claim one action once; crashed actions are not redispatched");
        device.MapPost("/jobs/{id:guid}/complete", async (Guid id, CompleteManagedJobRequest request,
            HttpContext context, IManagementService service, CancellationToken token) =>
            TypedResults.Ok(await service.CompleteAsync(DeviceId(context), id, request, token)))
            .WithSummary("Submit a bounded result using the device token and claim capability");
        device.MapPost("/auto-repair", async Task<Microsoft.AspNetCore.Http.HttpResults.Results<
            Microsoft.AspNetCore.Http.HttpResults.Created<ManagedJob>, Microsoft.AspNetCore.Http.HttpResults.NoContent>> (
            AutoRepairRequest request, HttpContext context, IManagementService service, CancellationToken token) =>
            {
                var job = await service.CreateAutoRepairAsync(DeviceId(context), request, token);
                return job is null ? TypedResults.NoContent() :
                    TypedResults.Created($"/api/v1/admin/management/jobs/{job.Id}", job);
            }).WithSummary("Request one evidence-based DNS cache repair per device per day");
    }

    private static Guid DeviceId(HttpContext context) =>
        ((EnrolledDevice)context.Items[DeviceTokenFilter.DeviceContextKey]!).Id;
}
