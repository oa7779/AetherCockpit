using System.Net;
using System.Text;
using System.Text.Json;
using AetherManager.Server.Models;
using AetherManager.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AetherManager.Tests;

internal static partial class Runner
{
    private static async Task VerifyGraphServiceAsync()
    {
        var deviceId = Guid.NewGuid();
        var azureDeviceId = Guid.NewGuid();
        var deploymentId = Guid.NewGuid();
        string? patch = null;
        var requestCount = 0;
        using var handler = new GraphHandler(async (request, token) =>
        {
            requestCount++;
            Check(request.Headers.Authorization?.Scheme == "Bearer" &&
                request.Headers.Authorization.Parameter == "synthetic-token", "Graph request uses app-only bearer token");
            if (request.RequestUri!.AbsolutePath == "/v1.0/deviceManagement/managedDevices" &&
                !request.RequestUri.Query.Contains("skiptoken", StringComparison.OrdinalIgnoreCase))
                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["value"] = new[] { new Dictionary<string, object?>
                    {
                        ["id"] = deviceId, ["deviceName"] = "SYNTHETIC-PC", ["operatingSystem"] = "Windows",
                        ["osVersion"] = "11.0", ["lastSyncDateTime"] = "2026-01-02T03:04:05Z",
                        ["complianceState"] = "compliant", ["managementState"] = "managed", ["azureADDeviceId"] = azureDeviceId
                    } },
                    ["@odata.nextLink"] = "https://graph.microsoft.com/v1.0/deviceManagement/managedDevices?$skiptoken=next"
                }));
            if (request.RequestUri!.AbsolutePath == "/v1.0/deviceManagement/managedDevices")
                return JsonResponse(HttpStatusCode.OK, """{"value":[]}""");
            if (request.RequestUri!.AbsolutePath == "/beta/admin/windows/updates/deployments")
                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    value = new[] { new
                    {
                        id = deploymentId,
                        state = new { requestedValue = "paused", effectiveValue = "paused" },
                        createdDateTime = "2026-01-01T00:00:00Z", lastModifiedDateTime = "2026-01-02T00:00:00Z",
                        content = new { catalogEntry = new { id = "catalog-entry" } }
                    } }
                }));
            if (request.RequestUri!.AbsolutePath == $"/v1.0/deviceManagement/managedDevices/{deviceId:D}/syncDevice" &&
                request.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (request.RequestUri!.AbsolutePath == $"/beta/admin/windows/updates/deployments/{deploymentId:D}" &&
                request.Method == HttpMethod.Patch)
            {
                patch = await request.Content!.ReadAsStringAsync(token);
                return JsonResponse(HttpStatusCode.Accepted, """{"state":{"requestedValue":"none"}}""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/") };
        var graph = new MicrosoftGraphService(client, new FixedGraphTokenProvider(), NullLogger<MicrosoftGraphService>.Instance);
        var devices = await graph.GetDevicesAsync(CancellationToken.None);
        Check(devices.Devices.Length == 1 && devices.Devices[0].Id == deviceId &&
            devices.Devices[0].AzureAdDeviceId == azureDeviceId.ToString(), "Intune device fields map and Graph pagination is followed");
        Check(!devices.HasMore, "Completed Graph pagination is not marked truncated");
        var deployments = await graph.GetDeploymentsAsync(CancellationToken.None);
        Check(deployments.Deployments.Length == 1 && deployments.Deployments[0].RequestedState == "paused" &&
            deployments.Deployments[0].CatalogEntryId == "catalog-entry", "Autopatch deployment state and catalog identity map");
        await graph.SyncDeviceAsync(deviceId, CancellationToken.None);
        await graph.SetDeploymentPausedAsync(deploymentId, true, CancellationToken.None);
        using (var body = JsonDocument.Parse(patch!))
            Check(body.RootElement.GetProperty("@odata.type").GetString() == "#microsoft.graph.windowsUpdates.deployment" &&
                body.RootElement.GetProperty("state").GetProperty("requestedValue").GetString() == "paused",
                "Autopatch pause uses the documented deployment state");
        await graph.SetDeploymentPausedAsync(deploymentId, false, CancellationToken.None);
        using (var body = JsonDocument.Parse(patch!))
            Check(body.RootElement.GetProperty("state").GetProperty("requestedValue").GetString() == "none",
                "Autopatch resume uses the documented requestedValue none");

        var untrustedRequests = 0;
        using var untrustedHandler = new GraphHandler((_, _) =>
        {
            untrustedRequests++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                """{"value":[],"@odata.nextLink":"https://graph.microsoft.example/v1.0/deviceManagement/managedDevices"}"""));
        });
        using var untrustedClient = new HttpClient(untrustedHandler) { BaseAddress = new Uri("https://graph.microsoft.com/") };
        var untrustedGraph = new MicrosoftGraphService(untrustedClient, new FixedGraphTokenProvider(),
            NullLogger<MicrosoftGraphService>.Instance);
        await Reject<GraphIntegrationException>(() => untrustedGraph.GetDevicesAsync(CancellationToken.None));
        Check(untrustedRequests == 1, "Graph pagination refuses untrusted next links before following them");
        Check(requestCount == 6, "Graph tests made only the expected synthetic requests");
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FixedGraphTokenProvider : IGraphTokenProvider
    {
        public GraphIntegrationStatusResponse GetStatus() => new("ready", true, "Synthetic test configuration.");
        public Task<string> GetAccessTokenAsync(CancellationToken token) => Task.FromResult("synthetic-token");
    }

    private sealed class GraphHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
