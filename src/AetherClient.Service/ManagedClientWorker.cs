using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using AetherOperations;

namespace AetherClient.Service;

internal sealed class ManagedClientWorker(ILogger<ManagedClientWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _pendingPath = Path.Combine(ClientConfiguration.Root, "pending-result.json");
    private DateTimeOffset _nextReport;
    private DateTimeOffset _nextDnsCheck;
    private static string Version => Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = await ClientConfiguration.LoadAsync(stoppingToken);
                var status = await SendAsync<ManagedDeviceStatus>(config, "management/heartbeat",
                    new HeartbeatRequest(Version, config.AllowChanges, config.AllowAutoRepair), stoppingToken)
                    ?? throw new InvalidDataException("The server did not return client status.");
                if (File.Exists(_pendingPath))
                {
                    var pending = JsonSerializer.Deserialize<PendingResult>(await File.ReadAllBytesAsync(_pendingPath, stoppingToken), Json)
                        ?? throw new InvalidDataException("Invalid pending result; no further jobs will be started.");
                    if (pending.DeviceId != config.DeviceId)
                        throw new InvalidDataException("Pending job belongs to another device. Restore its configuration before retrying.");
                    var result = pending.Result ?? new CompleteManagedJobRequest(pending.Claim.ClaimToken, -1,
                        "Client stopped after claiming the job. Execution state is unknown; the action was not restarted.", false);
                    await SendAsync<ManagedJob>(config, $"management/jobs/{pending.Claim.Job.Id}/complete", result, stoppingToken);
                    File.Delete(_pendingPath);
                }
                var claim = await SendAsync<ManagedJobClaim>(config, "management/claim", null, stoppingToken);
                if (claim is not null)
                {
                    var pending = new PendingResult(config.DeviceId, claim, null);
                    await SavePendingAsync(pending, stoppingToken);
                    var action = ManagedActionCatalog.Get(claim.Job.ActionId);
                    ManagedActionResult result;
                    var latestConfig = await ClientConfiguration.LoadAsync(stoppingToken);
                    var latestStatus = await SendAsync<ManagedDeviceStatus>(latestConfig, "management/heartbeat",
                        new HeartbeatRequest(Version, latestConfig.AllowChanges, latestConfig.AllowAutoRepair), stoppingToken)
                        ?? throw new InvalidDataException("Missing execution permission status.");
                    if (latestConfig.DeviceId != config.DeviceId || latestConfig.ServerUrl != config.ServerUrl ||
                        action.ChangesSystem && (!latestConfig.AllowChanges || !latestStatus.Policy.AllowChanges) ||
                        claim.Job.Automatic && (!action.CanAutoRepair || action.Id != "dns-cache-clear" ||
                            !latestConfig.AllowAutoRepair || !latestStatus.Policy.AllowAutoRepair || !await DnsHealthAsync(stoppingToken)))
                        result = new ManagedActionResult(-1, "Execution declined: local/server permission or DNS repair evidence no longer applies.", false);
                    else
                        result = claim.Job.ActionId == "windows-update-install"
                            ? await ManagedActionRunner.RunUpdateAsync(claim.Job.Update ??
                                throw new InvalidDataException("An exact approved update identity is missing."), stoppingToken)
                            : await ManagedActionRunner.RunAsync(action, stoppingToken);
                    var completion = new CompleteManagedJobRequest(claim.ClaimToken, result.ExitCode, result.Output, result.Truncated);
                    pending = pending with { Result = completion };
                    await SavePendingAsync(pending, stoppingToken);
                    await SendAsync<ManagedJob>(config, $"management/jobs/{claim.Job.Id}/complete", completion, stoppingToken);
                    File.Delete(_pendingPath);
                    logger.LogInformation("Managed action {Action} finished with exit code {ExitCode}", action.Id, result.ExitCode);
                }
                await ObserveAsync("Inventory report", async () =>
                {
                    if (DateTimeOffset.UtcNow < _nextReport) return;
                    var inventory = await ManagedActionRunner.RunAsync(ManagedActionCatalog.Get("inventory"), stoppingToken, 8 * 1024 * 1024);
                    if (inventory.ExitCode != 0 || inventory.Truncated)
                        throw new InvalidDataException($"Inventory incomplete: exit={inventory.ExitCode}, truncated={inventory.Truncated}. {inventory.Output}");
                    using var report = JsonDocument.Parse(inventory.Output);
                    await SendAsync<JsonElement>(config, "reports",
                        new { capturedAt = DateTimeOffset.UtcNow, report = report.RootElement }, stoppingToken);
                    _nextReport = DateTimeOffset.UtcNow.AddMinutes(30);
                }, stoppingToken);
                await ObserveAsync("DNS assistant", async () =>
                {
                    if (!config.AllowChanges || !config.AllowAutoRepair || !status.Policy.AllowChanges ||
                        !status.Policy.AllowAutoRepair || DateTimeOffset.UtcNow < _nextDnsCheck) return;
                    var health = await DnsHealthAsync(stoppingToken);
                    _nextDnsCheck = DateTimeOffset.UtcNow.AddMinutes(30);
                    if (health)
                        await SendAsync<ManagedJob>(config, "management/auto-repair",
                            new AutoRepairRequest(true, true), stoppingToken);
                }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                logger.LogError(ex, "Managed client cycle failed. No action is silently retried; pending results are retained.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task ObserveAsync(string name, Func<Task> observe, CancellationToken token)
    {
        try { await observe(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            logger.LogError(ex, "{Observation} unavailable. Other diagnostic and managed job stages remain independent.", name);
        }
    }

    private static bool IsRecoverable(Exception ex) => ex is HttpRequestException or IOException or JsonException or
        System.Security.Cryptography.CryptographicException or InvalidOperationException or ArgumentException or
        UnauthorizedAccessException or System.ComponentModel.Win32Exception or OperationCanceledException or KeyNotFoundException;

    private static async Task<bool> DnsHealthAsync(CancellationToken token)
    {
        var result = await ManagedActionRunner.RunAsync(ManagedActionCatalog.Get("dns-health"), token);
        if (result.ExitCode != 0 || result.Truncated)
            throw new InvalidDataException("DNS assistant did not produce a complete result.");
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.GetProperty("needsRepair").GetBoolean();
    }

    private async Task<T?> SendAsync<T>(ClientConfiguration config, string path, object? body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(config.ServerUri(), "api/v1/" + path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Token());
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await _http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        return response.StatusCode == HttpStatusCode.NoContent ? default :
            await response.Content.ReadFromJsonAsync<T>(Json, token);
    }

    private async Task SavePendingAsync(PendingResult pending, CancellationToken token)
    {
        var temporary = _pendingPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(pending, Json), token);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _pendingPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public override void Dispose() { _http.Dispose(); base.Dispose(); }
    private sealed record PendingResult(Guid DeviceId, ManagedJobClaim Claim, CompleteManagedJobRequest? Result);
}
