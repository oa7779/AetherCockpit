using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AetherManager.Server.Endpoints;
using AetherManager.Server.Middleware;
using AetherManager.Server.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "AETHER Management Server");
var urls = Environment.GetEnvironmentVariable("AETHER_MANAGER_URLS") ?? "http://127.0.0.1:5187";
var configuredUrls = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        ? uri : throw new InvalidOperationException("AETHER_MANAGER_URLS enthaelt eine ungueltige URL."))
    .ToArray();
if (configuredUrls.Length == 0 || configuredUrls.Any(uri =>
        !uri.IsLoopback && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("Nicht-loopback Listenadressen erfordern HTTPS. HTTP ist nur auf localhost/127.0.0.1 erlaubt.");

builder.WebHost.UseUrls(urls);
if (configuredUrls.Any(uri => !uri.IsLoopback) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_ALLOWEDHOSTS")))
    throw new InvalidOperationException("Netzwerkbetrieb erfordert ASPNETCORE_ALLOWEDHOSTS mit den konkreten Servernamen.");
if (configuredUrls.Any(uri => !uri.IsLoopback) &&
    Environment.GetEnvironmentVariable("ASPNETCORE_ALLOWEDHOSTS")!.Split(';').Any(host => host.Contains('*')))
    throw new InvalidOperationException("Fuer die Fernverwaltung sind Hostfilter-Wildcards nicht erlaubt.");
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
    options.Configure(builder.Configuration.GetSection("Kestrel"));
    var thumbprint = Environment.GetEnvironmentVariable("AETHER_MANAGER_CERT_THUMBPRINT");
    if (!string.IsNullOrWhiteSpace(thumbprint))
    {
        using var store = new System.Security.Cryptography.X509Certificates.X509Store(
            System.Security.Cryptography.X509Certificates.StoreName.My,
            System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
        store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
        var certificates = store.Certificates.Find(
            System.Security.Cryptography.X509Certificates.X509FindType.FindByThumbprint, thumbprint, validOnly: true);
        if (certificates.Count != 1 || !certificates[0].HasPrivateKey)
            throw new InvalidOperationException("Ein gueltiges LocalMachine-Serverzertifikat mit privatem Schluessel ist erforderlich.");
        options.ConfigureHttpsDefaults(https => https.ServerCertificate = certificates[0]);
    }
});
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.AllowDuplicateProperties = false;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
        await TypedResults.Problem(statusCode: 429, title: "Rate limit reached; try again later").ExecuteAsync(context.HttpContext);
    // A single bounded limiter prevents arbitrary IP/user keys from growing a login partition table.
    options.AddFixedWindowLimiter("login", options =>
    {
        options.PermitLimit = 20;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueLimit = 0;
        options.AutoReplenishment = true;
    });
    options.AddPolicy("enrollment", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("reports", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("management", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("cloud", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("device-management", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1200,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
var dataRoot = Environment.GetEnvironmentVariable("AETHER_MANAGER_DATA");
if (string.IsNullOrWhiteSpace(dataRoot))
{
    dataRoot = WindowsServiceHelpers.IsWindowsService()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AetherCockpit", "Manager")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherCockpit", "Manager");
}
builder.Services.AddSingleton<IManagerStore>(_ => new JsonManagerStore(dataRoot));
builder.Services.AddSingleton<IManagementService>(services =>
    new ManagementService(dataRoot, services.GetRequiredService<IManagerStore>(), services.GetRequiredService<IHttpContextAccessor>()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAccountService>(services => new LocalAccountService(dataRoot, services.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<ServerMonitoringService>();
builder.Services.AddSingleton<IServerMonitoringService>(services => services.GetRequiredService<ServerMonitoringService>());
builder.Services.AddHostedService(services => services.GetRequiredService<ServerMonitoringService>());
builder.Services.AddSingleton<IFleetService, FleetService>();
builder.Services.AddSingleton<AdminTokenFilter>();
builder.Services.AddSingleton<DeviceTokenFilter>();
builder.Services.AddSingleton<IGraphTokenProvider, MicrosoftGraphTokenProvider>();
builder.Services.AddHttpClient<IMicrosoftGraphService, MicrosoftGraphService>(client =>
{
    client.BaseAddress = new Uri("https://graph.microsoft.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<ReportRetentionWorker>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapOpenApi();
await app.Services.GetRequiredService<IManagerStore>().InitializeAsync(app.Lifetime.ApplicationStopping);
await app.Services.GetRequiredService<IAccountService>().InitializeAsync(app.Lifetime.ApplicationStopping);
app.MapManagerEndpoints();
app.MapManagementEndpoints();
app.MapAccountEndpoints();
app.MapFleetEndpoints();
app.MapGraphEndpoints();
app.Lifetime.ApplicationStarted.Register(() =>
{
    var store = app.Services.GetRequiredService<IManagerStore>();
    app.Logger.LogInformation("AETHER Manager hoert auf {Urls}; Datenordner: {DataRoot}", urls, dataRoot);
    app.Logger.LogInformation("Administrator-Token liegt in {Path}; Erstgeraete-Code liegt in {CodePath}",
        Path.Combine(dataRoot, "admin-token.txt"), Path.Combine(dataRoot, "enrollment-code.txt"));
});
await app.RunAsync();

/// <summary>Exposes the top-level program for integration testing.</summary>
public partial class Program;
