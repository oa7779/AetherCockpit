using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SystemCockpit.Services;

internal sealed class ManagerReportingService(Action<string> statusChanged) : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("AETHER Manager device token v1"));
    private static readonly TimeSpan StatusInterval = TimeSpan.FromMinutes(5);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private CockpitSettings _settings = new();
    private CancellationTokenSource? _activeCycle;
    private bool _disposed;

    public bool HasDeviceToken => File.Exists(TokenPath);

    public void Configure(CockpitSettings settings)
    {
        CancellationTokenSource? active;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _settings = settings;
            active = _activeCycle;
        }
        try { active?.Cancel(); }
        catch (ObjectDisposedException) { }
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_wake.CurrentCount == 0) _wake.Release();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _wake.Dispose();
            _disposed = true;
        }
    }

    public async Task<EnrollmentResult> EnrollAsync(string serverUrl, string code, CancellationToken cancellationToken)
    {
        var baseUri = NormalizeServerUrl(serverUrl);
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64)
            throw new ArgumentException(DesktopUiText.Get("ManagerClient.InvalidCode"), nameof(code));

        var version = typeof(ManagerReportingService).Assembly.GetName().Version?.ToString() ?? "unknown";
        using var response = await Http.PostAsJsonAsync(new Uri(baseUri, "api/v1/enroll"),
            new EnrollmentRequest(code.Trim(), Environment.MachineName, version), JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException(DesktopUiText.Get("ManagerClient.EmptyEnrollment"));
        if (result.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(result.DeviceToken) ||
            result.State != "pending")
            throw new InvalidDataException(DesktopUiText.Get("ManagerClient.InvalidEnrollment"));

        await SaveTokenAsync(result.DeviceToken, cancellationToken);
        return new EnrollmentResult(result.DeviceId, result.State);
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var nextReportAt = DateTimeOffset.MinValue;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var settings = CurrentSettings();
                if (!settings.ManagerClientMode || !settings.ManagerReportingEnabled)
                {
                    await _wake.WaitAsync(StatusInterval, stoppingToken);
                    continue;
                }

                using var cycle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                lock (_sync)
                    _activeCycle = cycle;
                var retryAfter = StatusInterval;
                try
                {
                    var token = await LoadTokenAsync(cycle.Token);
                    if (token is null)
                    {
                        statusChanged(DesktopUiText.Get("ManagerClient.NotEnrolled"));
                    }
                    else
                    {
                        var baseUri = NormalizeServerUrl(settings.ManagerServerUrl);
                        var device = await GetDeviceStatusAsync(baseUri, token, cycle.Token);
                        if (device.State != "approved")
                        {
                            statusChanged(NonApprovedStatus(device.State));
                        }
                        else if (DateTimeOffset.UtcNow < nextReportAt)
                        {
                            retryAfter = Min(StatusInterval, nextReportAt - DateTimeOffset.UtcNow);
                            statusChanged(DesktopUiText.Get("ManagerClient.NextReport"));
                        }
                        else
                        {
                            await SubmitDiagnosticReportAsync(baseUri, token, cycle.Token);
                            nextReportAt = DateTimeOffset.UtcNow.AddHours(1);
                            retryAfter = TimeSpan.FromHours(1);
                            statusChanged(DesktopUiText.Format("ManagerClient.ReportSent", DateTimeOffset.Now));
                        }
                    }
                }
                catch (OperationCanceledException) when (
                    cycle.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                {
                    continue;
                }
                catch (OperationCanceledException ex) when (!stoppingToken.IsCancellationRequested)
                {
                    Trace.TraceError("AETHER Zentralverwaltung: {0}", ex);
                    statusChanged(DesktopUiText.Format("ManagerClient.Timeout", ex.Message));
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or
                    InvalidDataException or JsonException or CryptographicException or InvalidOperationException or
                    TimeoutException or System.ComponentModel.Win32Exception)
                {
                    Trace.TraceError("AETHER Zentralverwaltung: {0}", ex);
                    statusChanged(DesktopUiText.Format("ManagerClient.Failure", ex.Message));
                }
                finally
                {
                    lock (_sync)
                        if (ReferenceEquals(_activeCycle, cycle))
                            _activeCycle = null;
                }

                await _wake.WaitAsync(retryAfter, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private CockpitSettings CurrentSettings()
    {
        lock (_sync)
            return _settings;
    }

    internal static string NonApprovedStatus(string? state) => state switch
    {
        "pending" => DesktopUiText.Get("ManagerClient.Pending"),
        "revoked" => DesktopUiText.Get("ManagerClient.Revoked"),
        _ => DesktopUiText.Format("ManagerClient.UnknownState", state ?? DesktopUiText.Get("ManagerClient.MissingState"))
    };

    private static async Task<DeviceStatus> GetDeviceStatusAsync(Uri baseUri, string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "api/v1/device"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DeviceStatus>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException(DesktopUiText.Get("ManagerClient.EmptyStatus"));
    }

    private static async Task SubmitDiagnosticReportAsync(Uri baseUri, string token,
        CancellationToken cancellationToken)
    {
        var snapshotJson = await CommandRunner.PowerShellAsync(DiagnosticAssistants.CollectionScript, cancellationToken);
        var report = DiagnosticAssistants.Analyze(DiagnosticAssistants.Parse(snapshotJson));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/v1/reports"))
        {
            Content = JsonContent.Create(new ReportRequest(report.CapturedAt,
                JsonSerializer.SerializeToElement(report, JsonOptions)), options: JsonOptions)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static Uri NormalizeServerUrl(string serverUrl)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            uri.AbsolutePath != "/" || (!uri.IsLoopback && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException(DesktopUiText.Get("ManagerClient.InvalidUrl"));
        return new Uri(uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/", UriKind.Absolute);
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second) =>
        first < second ? first : second;

    private static string TokenPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AetherCockpit", "Manager", "device-token.bin");

    private static async Task<string?> LoadTokenAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(TokenPath))
            return null;
        var protectedBytes = await File.ReadAllBytesAsync(TokenPath, cancellationToken);
        var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clearBytes);
    }

    private static async Task SaveTokenAsync(string token, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(TokenPath)!;
        Directory.CreateDirectory(directory);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser);
        var temporaryPath = TokenPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
            File.Move(temporaryPath, TokenPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private sealed record EnrollmentRequest(string Code, string MachineName, string CockpitVersion);
    private sealed record EnrollmentResponse(Guid DeviceId, string State, string DeviceToken);
    private sealed record DeviceStatus(Guid Id, string MachineName, string CockpitVersion, string State,
        DateTimeOffset EnrolledAt, DateTimeOffset? LastReportAt);
    private sealed record ReportRequest(DateTimeOffset CapturedAt, JsonElement Report);
}

internal sealed record EnrollmentResult(Guid DeviceId, string State);
