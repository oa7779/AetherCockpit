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
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
    options.Configure(builder.Configuration.GetSection("Kestrel"));
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
});
var dataRoot = Environment.GetEnvironmentVariable("AETHER_MANAGER_DATA");
if (string.IsNullOrWhiteSpace(dataRoot))
{
    dataRoot = WindowsServiceHelpers.IsWindowsService()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AetherCockpit", "Manager")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherCockpit", "Manager");
}
builder.Services.AddSingleton<IManagerStore>(_ => new JsonManagerStore(dataRoot));
builder.Services.AddSingleton<AdminTokenFilter>();
builder.Services.AddSingleton<DeviceTokenFilter>();
builder.Services.AddHostedService<ReportRetentionWorker>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.MapOpenApi();
await app.Services.GetRequiredService<IManagerStore>().InitializeAsync(app.Lifetime.ApplicationStopping);
app.MapManagerEndpoints();
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
