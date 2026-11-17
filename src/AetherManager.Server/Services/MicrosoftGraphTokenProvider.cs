using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AetherManager.Server.Models;
using Microsoft.Identity.Client;

namespace AetherManager.Server.Services;

internal interface IGraphTokenProvider
{
    GraphIntegrationStatusResponse GetStatus();
    Task<string> GetAccessTokenAsync(CancellationToken token);
}

internal sealed class GraphIntegrationException(int statusCode, string state, string title, string detail)
    : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string State { get; } = state;
    public string Title { get; } = title;
    public string Detail { get; } = detail;
}

internal sealed class MicrosoftGraphTokenProvider(IConfiguration configuration, ILogger<MicrosoftGraphTokenProvider> logger)
    : IGraphTokenProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly string? _tenantId = configuration["AETHER_GRAPH_TENANT_ID"]?.Trim();
    private readonly string? _clientId = configuration["AETHER_GRAPH_CLIENT_ID"]?.Trim();
    private readonly string? _thumbprint = configuration["AETHER_GRAPH_CERT_THUMBPRINT"]?
        .Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
    private IConfidentialClientApplication? _application;
    private X509Certificate2? _certificate;

    public GraphIntegrationStatusResponse GetStatus()
    {
        try
        {
            _ = GetApplication();
            return new("ready", true, "Graph-Konfiguration und Zertifikat sind lokal verfügbar. Tenant-Zustimmung wird beim Graph-Aufruf geprüft.");
        }
        catch (GraphIntegrationException exception)
        {
            return new(exception.State, false, exception.Detail);
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken token)
    {
        try
        {
            var result = await GetApplication()
                .AcquireTokenForClient(["https://graph.microsoft.com/.default"])
                .ExecuteAsync(token);
            return result.AccessToken;
        }
        catch (MsalServiceException exception)
        {
            logger.LogError("Microsoft Graph token request failed with MSAL code {ErrorCode}", exception.ErrorCode);
            throw new GraphIntegrationException(StatusCodes.Status502BadGateway, "authenticationFailed",
                "Microsoft Graph-Anmeldung fehlgeschlagen",
                "Tenant, App-Berechtigungen, Administratorzustimmung und Zertifikatszugriff prüfen.");
        }
        catch (MsalClientException exception)
        {
            logger.LogError("Microsoft Graph client authentication failed with MSAL code {ErrorCode}", exception.ErrorCode);
            throw new GraphIntegrationException(StatusCodes.Status502BadGateway, "authenticationFailed",
                "Microsoft Graph-Anmeldung fehlgeschlagen",
                "Zertifikat, privaten Schlüssel und Dienstkonto prüfen.");
        }
    }

    private IConfidentialClientApplication GetApplication()
    {
        lock (_gate)
        {
            if (_application is not null) return _application;
            if (string.IsNullOrWhiteSpace(_tenantId) || string.IsNullOrWhiteSpace(_clientId) ||
                string.IsNullOrWhiteSpace(_thumbprint))
                throw NotConfigured();
            if (!Guid.TryParse(_tenantId, out _) || !Guid.TryParse(_clientId, out _) ||
                _thumbprint.Length != 40 || _thumbprint.Any(character => !Uri.IsHexDigit(character)))
                throw InvalidConfiguration();
            if (!OperatingSystem.IsWindows())
                throw new GraphIntegrationException(StatusCodes.Status503ServiceUnavailable, "invalidConfiguration",
                    "Microsoft Graph ist nicht verfügbar",
                    "Die Zertifikatsanbindung benötigt den Windows-Zertifikatspeicher LocalMachine\\My.");

            try
            {
                using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);
                var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, _thumbprint, validOnly: false);
                var now = DateTime.UtcNow;
                if (certificates.Count != 1 || !certificates[0].HasPrivateKey ||
                    certificates[0].NotBefore.ToUniversalTime() > now || certificates[0].NotAfter.ToUniversalTime() < now)
                    throw InvalidConfiguration();
                _certificate = certificates[0];
                _application = ConfidentialClientApplicationBuilder.Create(_clientId)
                    .WithAuthority($"https://login.microsoftonline.com/{_tenantId}")
                    .WithCertificate(_certificate)
                    .Build();
                return _application;
            }
            catch (GraphIntegrationException)
            {
                throw;
            }
            catch (Exception exception) when (exception is CryptographicException or UnauthorizedAccessException or
                PlatformNotSupportedException or MsalClientException or ArgumentException)
            {
                logger.LogError(exception, "Microsoft Graph certificate configuration could not be loaded");
                throw InvalidConfiguration();
            }
        }
    }

    private static GraphIntegrationException NotConfigured() =>
        new(StatusCodes.Status503ServiceUnavailable, "notConfigured", "Microsoft Graph ist nicht eingerichtet",
            "AETHER_GRAPH_TENANT_ID, AETHER_GRAPH_CLIENT_ID und AETHER_GRAPH_CERT_THUMBPRINT konfigurieren.");

    private static GraphIntegrationException InvalidConfiguration() =>
        new(StatusCodes.Status503ServiceUnavailable, "invalidConfiguration", "Microsoft Graph ist nicht einsatzbereit",
            "Tenant-ID, App-ID und ein gültiges Zertifikat mit privatem Schlüssel in LocalMachine\\My prüfen.");

    public void Dispose()
    {
        lock (_gate)
        {
            _certificate?.Dispose();
            _certificate = null;
            _application = null;
        }
    }
}
