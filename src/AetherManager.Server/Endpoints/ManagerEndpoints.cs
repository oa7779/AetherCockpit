using AetherManager.Server.Middleware;
using AetherManager.Server.Models;
using AetherManager.Server.Services;

namespace AetherManager.Server.Endpoints;

internal static class ManagerEndpoints
{
    public static void MapManagerEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }))
            .WithName("GetManagerHealth")
            .WithSummary("Check whether the manager server is running")
            .WithDescription("Returns a liveness response without exposing device or report data.")
            .Produces(StatusCodes.Status200OK);

        app.MapPost("/api/v1/enroll", EnrollAsync)
            .RequireRateLimiting("enrollment")
            .WithName("EnrollCockpit")
            .WithSummary("Enroll a cockpit for administrator approval")
            .WithDescription("Consumes a one-time enrollment code and returns a device token. The device remains blocked until approved.")
            .Produces<EnrollmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests);

        app.MapGet("/api/v1/device", GetDeviceStatusAsync)
            .WithName("GetEnrolledDeviceStatus")
            .WithSummary("Check enrollment and approval status")
            .WithDescription("Allows an enrolled device to check whether an administrator has approved it.")
            .Produces<DeviceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        var admin = app.MapGroup("/api/v1/admin")
            .AddEndpointFilter<AdminTokenFilter>();

        admin.MapPost("/enrollment-codes", CreateEnrollmentCodeAsync)
            .WithName("CreateEnrollmentCode")
            .WithSummary("Create a single-use cockpit enrollment code")
            .WithDescription("The code expires after 24 hours and is returned only once.")
            .Produces<EnrollmentCodeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        admin.MapGet("/devices", GetDevicesAsync)
            .WithName("GetDevices")
            .WithSummary("List enrolled cockpits")
            .WithDescription("Returns the server's enrolled devices, including pending approvals.")
            .Produces<DeviceListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        admin.MapGet("/devices/{id:guid}", GetDeviceAsync)
            .WithName("GetDevice")
            .WithSummary("Get one enrolled cockpit")
            .WithDescription("Returns the current approval and last-report state of a device.")
            .Produces<DeviceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        admin.MapPost("/devices/{id:guid}/approve", ApproveDeviceAsync)
            .WithName("ApproveDevice")
            .WithSummary("Approve a pending cockpit")
            .WithDescription("Allows the device's issued token to submit diagnostic reports.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        admin.MapPost("/devices/{id:guid}/revoke", async Task<IResult> (Guid id, IManagementService service,
            CancellationToken token) => await service.SetDeviceApprovalAsync(id, false, token)
                ? TypedResults.NoContent() : TypedResults.NotFound())
            .WithSummary("Block device reporting and further management actions")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        admin.MapGet("/reports", GetReportsAsync)
            .WithName("GetReports")
            .WithSummary("List recent diagnostic reports")
            .WithDescription("Lists a page of at most 500 recent reports, optionally restricted to one device.")
            .Produces<ReportListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        admin.MapGet("/reports/{id:guid}", GetReportAsync)
            .WithName("GetReport")
            .WithSummary("Read a complete diagnostic report")
            .WithDescription("Returns the report payload and its ingestion metadata.")
            .Produces<StoredReportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapPost("/api/v1/reports", AddReportAsync)
            .AddEndpointFilter<DeviceTokenFilter>()
            .RequireRateLimiting("reports")
            .WithName("SubmitReport")
            .WithSummary("Submit a diagnostic report from an approved cockpit")
            .WithDescription("Accepts reports only from enrolled and manually approved devices. Reports are retained for 90 days.")
            .Produces<ReportMetadataResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> EnrollAsync(CreateEnrollmentRequest request, IManagerStore store,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 64 ||
            string.IsNullOrWhiteSpace(request.MachineName) || request.MachineName.Trim().Length > 255 ||
            string.IsNullOrWhiteSpace(request.CockpitVersion) || request.CockpitVersion.Trim().Length > 64)
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid enrollment request", detail: "Code, machine name, and cockpit version are required and must be within their size limits.");

        var result = await store.EnrollAsync(request, cancellationToken);
        if (result is null)
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Enrollment code unavailable", detail: "The code is invalid, expired, or has already been used.");
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Created($"/api/v1/admin/devices/{result.Value.Device.Id}", new EnrollmentResponse(
            result.Value.Device.Id, result.Value.Device.State, result.Value.Token));
    }

    private static async Task<EnrollmentCodeResponse> CreateEnrollmentCodeAsync(IManagerStore store,
        CancellationToken cancellationToken) => await store.CreateEnrollmentCodeAsync(cancellationToken);

    private static async Task<IResult> GetDeviceStatusAsync(HttpRequest request, IManagerStore store,
        CancellationToken cancellationToken)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        var token = AdminTokenFilter.GetBearerToken(request);
        if (token is null)
            return TypedResults.Unauthorized();
        var device = await store.FindDeviceAsync(token, cancellationToken);
        return device is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new DeviceResponse(device.Id, device.MachineName, device.CockpitVersion,
                device.State, device.EnrolledAt, device.LastReportAt));
    }

    private static async Task<DeviceListResponse> GetDevicesAsync(IManagerStore store,
        CancellationToken cancellationToken) => new(await store.GetDevicesAsync(cancellationToken));

    private static async Task<IResult> GetDeviceAsync(Guid id, IManagerStore store,
        CancellationToken cancellationToken)
    {
        var device = (await store.GetDevicesAsync(cancellationToken)).FirstOrDefault(item => item.Id == id);
        return device is null ? TypedResults.NotFound() : TypedResults.Ok(device);
    }

    private static async Task<IResult> ApproveDeviceAsync(Guid id, IManagementService service,
        CancellationToken cancellationToken) =>
        await service.SetDeviceApprovalAsync(id, true, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<IResult> GetReportsAsync(Guid? deviceId, int? limit, int? offset,
        IManagerStore store, CancellationToken cancellationToken)
    {
        var pageSize = limit ?? 100;
        var pageOffset = offset ?? 0;
        if (pageSize is < 1 or > 500 || pageOffset < 0)
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid report page", detail: "Limit must be 1-500 and offset must be zero or greater.");
        var page = await store.GetReportsAsync(deviceId, pageSize, pageOffset, cancellationToken);
        return TypedResults.Ok(new ReportListResponse(page.Reports, page.Offset, page.TotalCount, page.NextOffset));
    }

    private static async Task<IResult> GetReportAsync(Guid id, IManagerStore store,
        CancellationToken cancellationToken)
    {
        var report = await store.GetReportAsync(id, cancellationToken);
        return report is null ? TypedResults.NotFound() : TypedResults.Ok(report);
    }

    private static async Task<IResult> AddReportAsync(CreateReportRequest request, HttpContext context,
        IManagerStore store, CancellationToken cancellationToken)
    {
        if (request.Report.ValueKind != System.Text.Json.JsonValueKind.Object)
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid report", detail: "The report must be a JSON object.");
        if (request.CapturedAt == default)
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid capture time", detail: "Capture time is required.");
        if (request.CapturedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid capture time", detail: "Capture time cannot be more than five minutes in the future.");

        var device = (EnrolledDevice)context.Items[DeviceTokenFilter.DeviceContextKey]!;
        var metadata = await store.AddReportAsync(device.Id, request.CapturedAt, request.Report, cancellationToken);
        return TypedResults.Created($"/api/v1/admin/reports/{metadata.ReportId}", metadata);
    }
}
