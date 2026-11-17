using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AetherManager.Server.Middleware;
using AetherManager.Server.Models;
using AetherManager.Server.Services;
using AetherOperations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AetherManager.Tests;

internal static partial class Runner
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private static async Task VerifyAccountsAsync(string root)
    {
        var startCount = _assertions;
        Directory.CreateDirectory(root);
        var clock = new FixtureClock(DateTimeOffset.UtcNow);
        using var accounts = new LocalAccountService(root, clock);
        await accounts.InitializeAsync(CancellationToken.None);
        var password = Secret();
        var admin = await accounts.BootstrapAsync(new("admin-unit", password, AccountRole.Administrator), CancellationToken.None);
        await Reject<InvalidOperationException>(() => accounts.BootstrapAsync(new("again", Secret(), AccountRole.Administrator), CancellationToken.None));
        await Reject<ArgumentException>(() => accounts.CreateAsync(new("bad", "short", AccountRole.Reader), "unit-admin", CancellationToken.None));
        await Reject<ArgumentException>(() => accounts.CreateAsync(new("bad space", Secret(), AccountRole.Reader), "unit-admin", CancellationToken.None));
        await Reject<ArgumentException>(() => accounts.CreateAsync(new("bad", Secret(), (AccountRole)99), "unit-admin", CancellationToken.None));
        var reader = await accounts.CreateAsync(new("reader-unit", password, AccountRole.Reader), "unit-admin", CancellationToken.None);
        await Reject<InvalidOperationException>(() => accounts.CreateAsync(new("READER-unit", Secret(), AccountRole.Reader), "unit-admin", CancellationToken.None));
        var state = JsonSerializer.Deserialize<LocalAccountService.State>(await File.ReadAllTextAsync(Path.Combine(root, "accounts.json")), WebJson)!;
        Check(state.Accounts.Select(x => x.PasswordHash).Distinct().Count() == 2, "Equal passwords have distinct salted KDF hashes");
        Check(state.Accounts.All(x => !x.PasswordHash.Contains(password, StringComparison.Ordinal)), "No plaintext password in credential records");
        var login = await accounts.LoginAsync(new("ADMIN-unit", password), CancellationToken.None);
        Check(login is not null && login.Value.Response.Account.Id == admin.Id, "Local login is case-insensitive and returns actual actor");
        Check(login!.Value.Response.ExpiresAt == clock.GetUtcNow().AddMinutes(30), "Absolute session expires in 30 minutes UTC");
        Check(await accounts.ValidateAsync(login.Value.Cookie, CancellationToken.None) is not null, "Opaque session capability validates");
        Check(await accounts.ValidateAsync(Secret(), CancellationToken.None) is null, "Unknown session capability rejected");
        Check(!await File.ReadAllTextAsync(Path.Combine(root, "accounts.json")).ContinueWith(x =>
            x.Result.Contains(login.Value.Cookie, StringComparison.Ordinal) || x.Result.Contains(login.Value.Response.CsrfToken, StringComparison.Ordinal)),
            "Session and CSRF capabilities are not persisted");
        clock.Advance(TimeSpan.FromMinutes(30));
        Check(await accounts.ValidateAsync(login.Value.Cookie, CancellationToken.None) is null, "Session invalid at exact absolute expiry");
        var readerLogin = (await accounts.LoginAsync(new("reader-unit", password), CancellationToken.None))!.Value;
        await accounts.UpdateAsync(reader.Id, new(AccountRole.Operator, false), "unit-admin", CancellationToken.None);
        Check(await accounts.ValidateAsync(readerLogin.Cookie, CancellationToken.None) is null, "Role change revokes all prior sessions");
        var second = (await accounts.LoginAsync(new("reader-unit", password), CancellationToken.None))!.Value;
        await accounts.RevokeAsync(second.Response.SessionId, "unit-admin", CancellationToken.None);
        Check(await accounts.ValidateAsync(second.Cookie, CancellationToken.None) is null, "Explicit session revocation is effective");
        await Reject<InvalidOperationException>(() => accounts.UpdateAsync(admin.Id, new(AccountRole.Reader, false), "unit-admin", CancellationToken.None));
        await Reject<InvalidOperationException>(() => accounts.UpdateAsync(admin.Id, new(AccountRole.Administrator, true), "unit-admin", CancellationToken.None));
        var changedPassword = Secret();
        await accounts.UpdateAsync(reader.Id, new(AccountRole.Reader, false, changedPassword), "unit-admin", CancellationToken.None);
        Check(await accounts.LoginAsync(new("reader-unit", password), CancellationToken.None) is null, "Password reset invalidates old password");
        Check(await accounts.LoginAsync(new("reader-unit", changedPassword), CancellationToken.None) is not null, "Password reset accepts new credential");
        await accounts.UpdateAsync(reader.Id, new(AccountRole.Reader, true), "unit-admin", CancellationToken.None);
        Check(await accounts.LoginAsync(new("reader-unit", changedPassword), CancellationToken.None) is null, "Disabled account cannot log in");
        for (var i = 0; i < 5; i++)
            Check(await accounts.LoginAsync(new("admin-unit", Secret()), CancellationToken.None) is null, "Incorrect password rejected");
        Check(await accounts.LoginAsync(new("admin-unit", password), CancellationToken.None) is null, "Five failed attempts lock account for ten minutes");
        clock.Advance(TimeSpan.FromMinutes(10));
        Check(await accounts.LoginAsync(new("admin-unit", password), CancellationToken.None) is not null, "Account unlocks after bounded lockout");
        using (var concurrent = new LocalAccountService(root, clock))
            await Reject<IOException>(() => concurrent.InitializeAsync(CancellationToken.None));
        accounts.Dispose();
        using var reopened = new LocalAccountService(root, clock);
        await reopened.InitializeAsync(CancellationToken.None);
        Check(await reopened.ValidateAsync(login.Value.Cookie, CancellationToken.None) is null, "Restart invalidates sessions");
        await Reject<InvalidOperationException>(() => reopened.BootstrapAsync(new("again", Secret(), AccountRole.Administrator), CancellationToken.None));
        Check((await reopened.GetAuditAsync(CancellationToken.None)).Any(x => x.Actor == "unit-admin" && x.Event.Contains("passwordReset=True")), "Account audit includes actual modifying actor without password");
        var filterStore = new JsonManagerStore(root);
        await filterStore.InitializeAsync(CancellationToken.None);
        var filter = new AdminTokenFilter(filterStore, reopened);
        var active = (await reopened.LoginAsync(new("admin-unit", password), CancellationToken.None))!.Value;
        DefaultHttpContext Context(string? cookie = null, bool browser = false, string? path = null)
        {
            var http = new DefaultHttpContext();
            http.Request.Scheme = "https";
            http.Request.Path = path ?? "/api/v1/admin/fleet";
            http.Request.Method = "GET";
            if (cookie is not null) http.Request.Headers.Cookie = $"{AdminTokenFilter.CookieName}={cookie}";
            if (browser) http.Request.Headers["Sec-Fetch-Site"] = "same-origin";
            return http;
        }
        async Task<object?> Invoke(DefaultHttpContext http) =>
            await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), _ => ValueTask.FromResult<object?>("allowed"));
        var authenticated = Context(active.Cookie, true);
        Check(await Invoke(authenticated) is string, "Central endpoint filter accepts current HTTPS session");
        Check(authenticated.Items[AdminTokenFilter.ActorKey]?.ToString() == $"account:{admin.Id}:admin-unit", "Central filter records actual actor");
        clock.Advance(TimeSpan.FromMinutes(30));
        Check(await Invoke(Context(active.Cookie, true)) is IStatusCodeHttpResult { StatusCode: 401 }, "Expired session rejected by actual endpoint filter");
        var oldFlag = Environment.GetEnvironmentVariable("AETHER_MANAGER_LEGACY_ADMIN_API");
        try
        {
            var privileged = await filterStore.GetAdminTokenAsync(CancellationToken.None);
            Environment.SetEnvironmentVariable("AETHER_MANAGER_LEGACY_ADMIN_API", "false");
            var cli = Context(); cli.Request.Headers.Authorization = "Bearer " + privileged;
            Check(await Invoke(cli) is IStatusCodeHttpResult { StatusCode: 401 }, "Default post-bootstrap token workflow is explicitly disabled");
            Environment.SetEnvironmentVariable("AETHER_MANAGER_LEGACY_ADMIN_API", "true");
            Check(await Invoke(cli) is string && cli.Items[AdminTokenFilter.ActorKey]?.ToString() == "legacy-admin-api", "Explicit legacy compatibility enables only administrative API tools and audits the capability actor");
            var browserToken = Context(browser: true); browserToken.Request.Headers.Authorization = "Bearer " + privileged;
            Check(await Invoke(browserToken) is IStatusCodeHttpResult { StatusCode: 401 }, "Compatibility flag never enables browser token bypass");
            var expiredWithToken = Context(active.Cookie); expiredWithToken.Request.Headers.Authorization = "Bearer " + privileged;
            Check(await Invoke(expiredWithToken) is IStatusCodeHttpResult { StatusCode: 401 }, "Expired cookie never falls back to compatible admin token");
            var usersWithToken = Context(path: "/api/v1/admin/accounts"); usersWithToken.Request.Headers.Authorization = "Bearer " + privileged;
            Check(await Invoke(usersWithToken) is IStatusCodeHttpResult { StatusCode: 401 }, "Legacy token cannot provision later local users even in compatibility mode");
        }
        finally { Environment.SetEnvironmentVariable("AETHER_MANAGER_LEGACY_ADMIN_API", oldFlag); }
        for (var index = 0; index < 5; index++)
            Check(await reopened.LoginAsync(new("admin-unit", password), CancellationToken.None) is not null, "Bounded per-account session creation");
        await Reject<InvalidOperationException>(() => reopened.LoginAsync(new("admin-unit", password), CancellationToken.None));
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await Reject<OperationCanceledException>(() => reopened.GetAccountsAsync(cancelled.Token));
        }
        var additionalAdmin = await reopened.CreateAsync(new("additional-admin", Secret(), AccountRole.Administrator), "unit-admin", CancellationToken.None);
        Check((await reopened.UpdateAsync(admin.Id, new(AccountRole.Reader, false), "unit-admin", CancellationToken.None)).Role == AccountRole.Reader,
            "Admin demotion allowed when another enabled administrator exists");
        await Reject<InvalidOperationException>(() => reopened.UpdateAsync(additionalAdmin.Id, new(AccountRole.Administrator, true), "unit-admin", CancellationToken.None));

        var paths = new[]
        {
            ("POST", "/api/v1/admin/enrollment-codes"),
            ("POST", "/api/v1/admin/devices/" + Guid.NewGuid() + "/approve"),
            ("POST", "/api/v1/admin/devices/" + Guid.NewGuid() + "/revoke"),
            ("PUT", "/api/v1/admin/management/devices/" + Guid.NewGuid() + "/policy"),
            ("POST", "/api/v1/admin/management/jobs/" + Guid.NewGuid() + "/cancel"),
            ("POST", "/api/v1/admin/accounts"),
            ("PUT", "/api/v1/admin/accounts/" + Guid.NewGuid()),
            ("POST", "/api/v1/admin/sessions/" + Guid.NewGuid() + "/revoke")
        };
        foreach (var (method, path) in paths)
            foreach (var role in Enum.GetValues<AccountRole>())
                Check(AdminTokenFilter.CanAccess(role, method, path) == (role == AccountRole.Administrator), $"Role matrix {role} {method} {path}");
        foreach (var action in ManagedActionCatalog.All)
            foreach (var role in Enum.GetValues<AccountRole>())
                Check(AdminTokenFilter.CanAccess(role, "POST", "/api/v1/admin/management/devices/" + Guid.NewGuid() + "/jobs", new(action.Id)) ==
                    (role == AccountRole.Administrator || role == AccountRole.Operator && !action.ChangesSystem),
                    $"Catalog authorization {role} {action.Id}");
        Check(!AdminTokenFilter.CanAccess(AccountRole.Operator, "POST", "/api/v1/admin/management/devices/" + Guid.NewGuid() + "/jobs", new("inventory", true)),
            "Operator cannot submit change confirmation");
        Check(!AdminTokenFilter.CanAccess(AccountRole.Operator, "DELETE", "/api/v1/admin/fleet"), "Unknown mutations denied by default");
        Check(!AdminTokenFilter.CanAccess((AccountRole)99, "GET", "/api/v1/admin/fleet"), "Unknown role rejected");
        Console.WriteLine($"PASS {_assertions - startCount} local-account and authorization assertions.");
    }

    private static async Task VerifyFleetAsync(string root)
    {
        var startCount = _assertions;
        Directory.CreateDirectory(root);
        var store = new JsonManagerStore(root);
        await store.InitializeAsync(CancellationToken.None);
        var management = new ManagementService(root, store);
        await SeedFleetAsync(root, store, management);
        var clock = new FixtureClock(DateTimeOffset.UtcNow);
        using var monitoring = new ServerMonitoringService(clock, NullLogger<ServerMonitoringService>.Instance);
        Check(monitoring.GetSnapshot().ProcessCpuPercent is null, "Unsampled CPU is unavailable, not a fake reading");
        monitoring.Sample(); clock.Advance(TimeSpan.FromSeconds(5)); monitoring.Sample();
        var service = new FleetService(store, management, monitoring, clock);
        var fleet = await service.GetAsync(CancellationToken.None);
        Check(fleet.Devices.Length == 48, "48 synthetic enrolled installations in aggregate fixture");
        Check(fleet.Devices.Select(x => x.Device.Id).Distinct().Count() == 48, "Fixture devices have distinct real enrollment records");
        Check(fleet.Devices.Any(x => x.Liveness == "fresh") && fleet.Devices.Any(x => x.Liveness == "stale") &&
            fleet.Devices.Any(x => x.Liveness == "never-observed"), "Fresh, stale and unobserved heartbeat states distinct");
        Check(fleet.Devices.All(x => x.Liveness != "offline"), "Missing heartbeat is never inferred offline");
        Check(fleet.Devices.Any(x => x.LatestReport is not null) && fleet.Devices.Any(x => x.LatestReport is null), "Actual retained report metadata or explicit unavailable");
        Check(fleet.Server.ProcessWorkingSetBytes > 0 && fleet.Server.ProcessCpuPercent is >= 0 and <= 100, "Server process telemetry is sampled from the real process");
        Check(fleet.Server.HostProcessCount > 0 && (fleet.Server.HostCpuPercent is >= 0 and <= 100 || fleet.Server.HostCpuPercent is null),
            "Host process count and CPU shape are actual samples or unavailable");
        Check(fleet.Server.HostUptimeSeconds > 0 && fleet.Server.ProcessUptimeSeconds >= 0, "Actual host and process uptime shape");
        Check(fleet.Server.TemperatureStatus.StartsWith("Unavailable"), "Unavailable temperature provider explicit");
        Check(fleet.Server.Disks.All(x => x.TotalBytes > 0 && x.AvailableBytes >= 0 || x.UnavailableReason is not null), "Disk measurements valid or explicitly unavailable");
        Check(FleetService.Freshness(null, clock.GetUtcNow(), 120) == "never-observed", "No heartbeat not offline");
        Check(FleetService.Freshness(clock.GetUtcNow().AddSeconds(-120), clock.GetUtcNow(), 120) == "fresh", "Freshness includes exact threshold");
        Check(FleetService.Freshness(clock.GetUtcNow().AddSeconds(-121), clock.GetUtcNow(), 120) == "stale", "Stale boundary explicit");
        Console.WriteLine($"PASS {_assertions - startCount} fleet/telemetry assertions (48 synthetic enrollments).");
    }

    private sealed class FixtureClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        private long _stamp;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _stamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan value) { _now += value; _stamp += value.Ticks; }
    }
}
