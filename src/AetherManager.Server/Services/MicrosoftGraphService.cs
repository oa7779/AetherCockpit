using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AetherManager.Server.Models;

namespace AetherManager.Server.Services;

internal interface IMicrosoftGraphService
{
    GraphIntegrationStatusResponse GetStatus();
    Task<GraphDeviceListResponse> GetDevicesAsync(CancellationToken token);
    Task<GraphDeploymentListResponse> GetDeploymentsAsync(CancellationToken token);
    Task SyncDeviceAsync(Guid id, CancellationToken token);
    Task SetDeploymentPausedAsync(Guid id, bool paused, CancellationToken token);
}

internal sealed class MicrosoftGraphService(
    HttpClient client,
    IGraphTokenProvider tokenProvider,
    ILogger<MicrosoftGraphService> logger) : IMicrosoftGraphService
{
    private const int PageLimit = 5;
    private const int ResponseLimit = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public GraphIntegrationStatusResponse GetStatus() => tokenProvider.GetStatus();

    public async Task<GraphDeviceListResponse> GetDevicesAsync(CancellationToken token)
    {
        var (items, hasMore) = await GetCollectionAsync(
            "v1.0/deviceManagement/managedDevices?$select=id,deviceName,operatingSystem,osVersion,lastSyncDateTime,complianceState,managementState,azureADDeviceId&$top=100",
            token);
        return new(items.Select(ParseDevice).ToArray(), hasMore);
    }

    public async Task<GraphDeploymentListResponse> GetDeploymentsAsync(CancellationToken token)
    {
        var (items, hasMore) = await GetCollectionAsync("beta/admin/windows/updates/deployments?$top=100", token);
        return new(items.Select(ParseDeployment).ToArray(), hasMore);
    }

    public async Task SyncDeviceAsync(Guid id, CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"v1.0/deviceManagement/managedDevices/{id:D}/syncDevice", null, token);
    }

    public async Task SetDeploymentPausedAsync(Guid id, bool paused, CancellationToken token)
    {
        var body = new DeploymentStatePatch(
            "#microsoft.graph.windowsUpdates.deployment",
            new("#microsoft.graph.windowsUpdates.deploymentState", paused ? "paused" : "none"));
        using var response = await SendAsync(HttpMethod.Patch,
            $"beta/admin/windows/updates/deployments/{id:D}", body, token);
    }

    private async Task<(List<JsonElement> Items, bool HasMore)> GetCollectionAsync(string path, CancellationToken token)
    {
        var items = new List<JsonElement>();
        string? next = path;
        for (var page = 0; page < PageLimit && next is not null; page++)
        {
            using var response = await SendAsync(HttpMethod.Get, next, null, token);
            using var document = await ReadJsonAsync(response, token);
            if (!document.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
                throw InvalidGraphResponse();
            foreach (var item in value.EnumerateArray())
            {
                if (items.Count >= PageLimit * 100) return (items, true);
                items.Add(item.Clone());
            }

            next = document.RootElement.TryGetProperty("@odata.nextLink", out var nextLink) &&
                nextLink.ValueKind == JsonValueKind.String ? nextLink.GetString() : null;
            if (next is not null && !IsTrustedNextLink(next))
                throw new GraphIntegrationException(StatusCodes.Status502BadGateway, "invalidGraphResponse",
                    "Microsoft Graph lieferte eine ungültige Folgeseite",
                    "Die unerwartete Graph-Weiterleitungsadresse wurde nicht aufgerufen.");
            if (next is not null && page == PageLimit - 1) return (items, true);
        }
        return (items, false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken token)
    {
        var accessToken = await tokenProvider.GetAccessTokenAsync(token);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.IsSuccessStatusCode) return response;
            logger.LogWarning("Microsoft Graph {Method} request returned HTTP {StatusCode}",
                method.Method, (int)response.StatusCode);
            response.Dispose();
            throw new GraphIntegrationException(StatusCodes.Status502BadGateway, "graphRequestFailed",
                "Microsoft Graph hat die Anfrage abgewiesen",
                "Graph-Berechtigungen, Administratorzustimmung, Intune-Lizenz und Autopatch-Voraussetzungen prüfen.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Microsoft Graph request could not be completed");
            throw new GraphIntegrationException(StatusCodes.Status502BadGateway, "graphUnavailable",
                "Microsoft Graph ist nicht erreichbar", "Netzwerkzugriff auf login.microsoftonline.com und graph.microsoft.com prüfen.");
        }
        catch (OperationCanceledException exception) when (!token.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Microsoft Graph request timed out");
            throw new GraphIntegrationException(StatusCodes.Status504GatewayTimeout, "graphTimeout",
                "Microsoft Graph antwortet nicht rechtzeitig", "Verbindung prüfen und die Anfrage später erneut ausführen.");
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        var total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, ResponseLimit + 1 - total)), token)) > 0)
        {
            total += read;
            if (total > ResponseLimit) throw InvalidGraphResponse();
            buffer.Write(chunk, 0, read);
        }
        var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
        try { return JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException) { throw InvalidGraphResponse(); }
    }

    private static GraphDeviceResponse ParseDevice(JsonElement item) =>
        new(ReadGuid(item, "id"), ReadString(item, "deviceName"),
            ReadString(item, "operatingSystem"), ReadString(item, "osVersion"),
            ReadDate(item, "lastSyncDateTime"), ReadString(item, "complianceState"),
            ReadString(item, "managementState"), ReadString(item, "azureADDeviceId"));

    private static GraphDeploymentResponse ParseDeployment(JsonElement item)
    {
        var state = item.TryGetProperty("state", out var value) && value.ValueKind == JsonValueKind.Object
            ? value : default;
        var content = item.TryGetProperty("content", out var contentValue) && contentValue.ValueKind == JsonValueKind.Object
            ? contentValue : default;
        var entry = content.ValueKind == JsonValueKind.Object &&
            content.TryGetProperty("catalogEntry", out var entryValue) && entryValue.ValueKind == JsonValueKind.Object
                ? entryValue : default;
        return new(ReadGuid(item, "id"), ReadString(state, "requestedValue"), ReadString(state, "effectiveValue"),
            ReadString(entry, "id"), ReadDate(item, "createdDateTime"), ReadDate(item, "lastModifiedDateTime"));
    }

    private static Guid ReadGuid(JsonElement item, string property)
    {
        var value = ReadString(item, property);
        return Guid.TryParse(value, out var id) ? id : throw InvalidGraphResponse();
    }

    private static string? ReadString(JsonElement item, string property) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? ReadDate(JsonElement item, string property)
    {
        var value = ReadString(item, property);
        if (value is null) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date : throw InvalidGraphResponse();
    }

    private static bool IsTrustedNextLink(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("graph.microsoft.com", StringComparison.OrdinalIgnoreCase) &&
        uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.AbsolutePath.StartsWith("/v1.0/", StringComparison.Ordinal) ||
         uri.AbsolutePath.StartsWith("/beta/", StringComparison.Ordinal));

    private static GraphIntegrationException InvalidGraphResponse() =>
        new(StatusCodes.Status502BadGateway, "invalidGraphResponse", "Microsoft Graph lieferte ungültige Daten",
            "Die Antwort wurde nicht als Verwaltungsdaten übernommen.");

    private sealed record DeploymentStatePatch(
        [property: JsonPropertyName("@odata.type")] string ODataType,
        DeploymentState State);

    private sealed record DeploymentState(
        [property: JsonPropertyName("@odata.type")] string ODataType,
        string RequestedValue);
}
