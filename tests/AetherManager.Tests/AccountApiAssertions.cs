using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AetherManager.Server.Models;
using AetherOperations;

namespace AetherManager.Tests;

internal static partial class Runner
{
    private sealed record ApiSession(SessionResponse Session, string Cookie);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, string path,
        string method = "GET", object? body = null, ApiSession? session = null, string? bearer = null,
        string? csrf = null, string? origin = null, bool browser = false)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url + "/api/v1/" + path);
        if (body is not null) request.Content = JsonContent.Create(body, options: WebJson);
        if (session is not null)
        {
            request.Headers.Add("Cookie", session.Cookie);
            if (method != "GET") request.Headers.Add("X-Aether-CSRF", csrf ?? session.Session.CsrfToken);
            request.Headers.Add("Origin", origin ?? url);
        }
        else if (origin is not null) request.Headers.Add("Origin", origin);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (browser) request.Headers.Add("Sec-Fetch-Site", "same-origin");
        if (path == "auth/login") request.Headers.Add("X-Aether-Login", "1");
        return await client.SendAsync(request);
    }
    private static async Task<ApiSession> SignInAsync(HttpClient client, string url, string username, string password)
    {
        using var response = await SendAsync(client, url, "auth/login", "POST", new LoginRequest(username, password), origin: url);
        Check(response.StatusCode == HttpStatusCode.OK, "Actual HTTPS account login succeeds");
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(WebJson))!;
        var header = response.Headers.GetValues("Set-Cookie").Single();
        Check(header.Contains("__Host-AetherSession=") && header.Contains("secure", StringComparison.OrdinalIgnoreCase) &&
            header.Contains("httponly", StringComparison.OrdinalIgnoreCase) && header.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase) &&
            header.Contains("path=/", StringComparison.OrdinalIgnoreCase), "Host-only Secure HttpOnly SameSite Strict session cookie");
        return new(session, header.Split(';')[0]);
    }
    private static async Task VerifyAccountApiAsync(HttpClient client, string url, string root)
    {
        var startCount = _assertions;
        var legacy = (await File.ReadAllTextAsync(Path.Combine(root, "admin-token.txt"))).Trim();
        using (var response = await SendAsync(client, url, "admin/devices", bearer: legacy))
            Check(response.StatusCode == HttpStatusCode.OK, "Legacy administrative tools continue before account bootstrap");
        using (var response = await SendAsync(client, url, "admin/devices", bearer: legacy, browser: true))
            Check(response.StatusCode == HttpStatusCode.Unauthorized, "Browser cannot use indefinite legacy administrator bypass");
        var adminPassword = Secret();
        var readerPassword = Secret();
        var operatorPassword = Secret();
        var bootstrap = new CreateAccountRequest("api-admin", adminPassword, AccountRole.Administrator);
        using (var response = await SendAsync(client, url, "auth/bootstrap", "POST", bootstrap))
            Check(response.StatusCode == HttpStatusCode.Unauthorized, "Unauthenticated bootstrap denied");
        using (var response = await SendAsync(client, url, "auth/bootstrap", "POST", bootstrap, bearer: legacy, browser: true))
            Check(response.StatusCode == HttpStatusCode.Forbidden, "Bootstrap not publicly exposed through browser");
        AccountResponse administrator;
        using (var response = await SendAsync(client, url, "auth/bootstrap", "POST", bootstrap, bearer: legacy))
        {
            Check(response.StatusCode == HttpStatusCode.Created && response.Headers.Location is not null, "Privileged first administrator provisioned once");
            administrator = (await response.Content.ReadFromJsonAsync<AccountResponse>(WebJson))!;
        }
        using (var response = await SendAsync(client, url, "auth/bootstrap", "POST", bootstrap, bearer: legacy))
            Check(response.StatusCode == HttpStatusCode.Conflict, "Bootstrap cannot be replayed even with privileged token");
        using (var response = await SendAsync(client, url, "admin/devices", bearer: legacy))
            Check(response.StatusCode == HttpStatusCode.Unauthorized, "Default legacy admin API disabled after account provisioning; device tokens unchanged");
        using (var response = await SendAsync(client, url, "auth/login", "POST", new LoginRequest("api-admin", adminPassword), origin: "https://foreign.example"))
            Check(response.StatusCode == HttpStatusCode.Forbidden, "Cross-origin login rejected");
        using (var response = await SendAsync(client, url, "auth/login", "POST", new LoginRequest("api-admin", Secret()), origin: url))
            Check(response.StatusCode == HttpStatusCode.Unauthorized, "Incorrect password rejected by HTTPS endpoint");
        var admin = await SignInAsync(client, url, "api-admin", adminPassword);
        using (var response = await SendAsync(client, url, "admin/enrollment-codes", "POST", session: admin, csrf: ""))
            Check(response.StatusCode == HttpStatusCode.Forbidden, "Mutation without CSRF token denied");
        using (var response = await SendAsync(client, url, "admin/enrollment-codes", "POST", session: admin, csrf: Secret()))
            Check(response.StatusCode == HttpStatusCode.Forbidden, "Incorrect CSRF token denied");
        using (var response = await SendAsync(client, url, "admin/enrollment-codes", "POST", session: admin, origin: "https://foreign.example"))
            Check(response.StatusCode == HttpStatusCode.Forbidden, "Correct CSRF capability does not permit cross-origin mutation");
        using (var response = await SendAsync(client, url, "admin/accounts", "POST", new CreateAccountRequest("api-reader", readerPassword, AccountRole.Reader), admin))
            Check(response.StatusCode == HttpStatusCode.Created, "Administrator creates Reader");
        using (var response = await SendAsync(client, url, "admin/accounts", "POST", new CreateAccountRequest("api-operator", operatorPassword, AccountRole.Operator), admin))
            Check(response.StatusCode == HttpStatusCode.Created, "Administrator creates Operator");
        using (var response = await SendAsync(client, url, "admin/accounts", "POST", new { username = "missing-role", password = Secret() }, admin))
            Check(response.StatusCode == HttpStatusCode.BadRequest, "Account role must be supplied explicitly; omitted role cannot default to administrator");
        var reader = await SignInAsync(client, url, "api-reader", readerPassword);
        var op = await SignInAsync(client, url, "api-operator", operatorPassword);
        Guid deviceId;
        string deviceToken;
        using (var response = await SendAsync(client, url, "admin/enrollment-codes", "POST", session: admin))
        {
            var code = (await response.Content.ReadFromJsonAsync<EnrollmentCodeResponse>(WebJson))!;
            using var enrollment = await SendAsync(client, url, "enroll", "POST", new CreateEnrollmentRequest
                { Code = code.Code, MachineName = "api-synthetic", CockpitVersion = "test-only" });
            var enrolled = (await enrollment.Content.ReadFromJsonAsync<EnrollmentResponse>(WebJson))!;
            deviceId = enrolled.DeviceId; deviceToken = enrolled.DeviceToken;
        }
        using (var response = await SendAsync(client, url, $"admin/devices/{deviceId}/approve", "POST", session: admin))
            Check(response.StatusCode == HttpStatusCode.NoContent, "Administrator approves the isolated synthetic client");
        using (var response = await SendAsync(client, url, "management/heartbeat", "POST", new HeartbeatRequest("test-only", false, false), bearer: deviceToken))
            Check(response.StatusCode == HttpStatusCode.OK, "Existing device token workflow survives account bootstrap");
        Guid reportId;
        using (var response = await SendAsync(client, url, "reports", "POST", new CreateReportRequest
            { CapturedAt = DateTimeOffset.UtcNow, Report = JsonSerializer.SerializeToElement(new { fixture = true, summary = "synthetic API report" }) }, bearer: deviceToken))
        {
            Check(response.StatusCode == HttpStatusCode.Created, "Existing report submission still uses device capability");
            reportId = (await response.Content.ReadFromJsonAsync<ReportMetadataResponse>(WebJson))!.ReportId;
        }
        Guid jobId;
        using (var response = await SendAsync(client, url, $"admin/management/devices/{deviceId}/jobs", "POST", new CreateManagedJobRequest("inventory"), admin))
            jobId = (await response.Content.ReadFromJsonAsync<ManagedJob>(WebJson))!.Id;

        var reads = new[] { "admin/devices", $"admin/devices/{deviceId}", "admin/reports", $"admin/reports/{reportId}",
            "admin/management/actions", "admin/management/devices", "admin/management/jobs", $"admin/management/jobs/{jobId}",
            "admin/management/audit", "admin/fleet", "admin/account-audit" };
        foreach (var path in reads)
        {
            using var unauthenticated = await SendAsync(client, url, path);
            Check(unauthenticated.StatusCode == HttpStatusCode.Unauthorized, $"Anonymous denied GET {path}");
            foreach (var signedIn in new[] { admin, op, reader })
            {
                using var response = await SendAsync(client, url, path, session: signedIn);
                Check(response.StatusCode == HttpStatusCode.OK, $"{signedIn.Session.Account.Role} can read {path}");
            }
        }
        foreach (var path in new[] { "admin/accounts", $"admin/accounts/{administrator.Id}", "admin/sessions" })
            foreach (var signedIn in new[] { admin, op, reader })
            {
                using var response = await SendAsync(client, url, path, session: signedIn);
                Check(response.StatusCode == (signedIn == admin ? HttpStatusCode.OK : HttpStatusCode.Forbidden), $"User administration read role {signedIn.Session.Account.Role} {path}");
            }
        var mutations = new (string Method, string Path, object? Body)[]
        {
            ("POST", "admin/enrollment-codes", null),
            ("POST", $"admin/devices/{deviceId}/approve", null),
            ("POST", $"admin/devices/{deviceId}/revoke", null),
            ("PUT", $"admin/management/devices/{deviceId}/policy", new ManagementPolicy()),
            ("POST", $"admin/management/jobs/{jobId}/cancel", null),
            ("POST", "admin/accounts", new CreateAccountRequest("matrix-user", Secret(), AccountRole.Reader)),
            ("PUT", $"admin/accounts/{reader.Session.Account.Id}", new UpdateAccountRequest(AccountRole.Reader, false)),
            ("POST", $"admin/sessions/{reader.Session.SessionId}/revoke", null)
        };
        foreach (var mutation in mutations)
        {
            using var anonymous = await SendAsync(client, url, mutation.Path, mutation.Method, mutation.Body);
            Check(anonymous.StatusCode == HttpStatusCode.Unauthorized, $"Anonymous mutation denied {mutation.Path}");
            using var noCsrf = await SendAsync(client, url, mutation.Path, mutation.Method, mutation.Body, admin, csrf: "");
            Check(noCsrf.StatusCode == HttpStatusCode.Forbidden, $"All mutations enforce CSRF {mutation.Path}");
            foreach (var signedIn in new[] { reader, op })
            {
                using var response = await SendAsync(client, url, mutation.Path, mutation.Method, mutation.Body, signedIn);
                Check(response.StatusCode == HttpStatusCode.Forbidden, $"{signedIn.Session.Account.Role} mutation denied {mutation.Path}");
                Check(response.Content.Headers.ContentType?.MediaType == "application/problem+json", "Authorization errors use Problem Details");
            }
            using (var response = await SendAsync(client, url, $"admin/management/devices/{deviceId}/policy", "PUT", new ManagementPolicy(), admin))
                Check(response.StatusCode == HttpStatusCode.OK, "Administrator can set policy while local consent remains unchanged");
            using (var response = await SendAsync(client, url, $"admin/management/jobs/{jobId}/cancel", "POST", session: admin))
                Check(response.StatusCode == HttpStatusCode.OK, "Administrator can cancel a queued fixture job");
            using (var response = await SendAsync(client, url, "admin/accounts", "POST", new CreateAccountRequest("matrix-user", Secret(), AccountRole.Reader), admin))
                Check(response.StatusCode == HttpStatusCode.Created, "Administrator mutation role matrix permits user provisioning");
        }
        foreach (var signedIn in new[] { op, reader })
        {
            using var diagnostic = await SendAsync(client, url, $"admin/management/devices/{deviceId}/jobs", "POST", new CreateManagedJobRequest("inventory"), signedIn);
            Check(diagnostic.StatusCode == (signedIn == op ? HttpStatusCode.Created : HttpStatusCode.Forbidden), "Only Operator/Admin can queue read-only diagnostics");
            foreach (var action in ManagedActionCatalog.All.Where(x => x.ChangesSystem))
            {
                using var changes = await SendAsync(client, url, $"admin/management/devices/{deviceId}/jobs", "POST", new CreateManagedJobRequest(action.Id, true), signedIn);
                Check(changes.StatusCode == HttpStatusCode.Forbidden, $"{signedIn.Session.Account.Role} cannot queue change {action.Id}");
            }
        }
        using (var response = await SendAsync(client, url, $"admin/accounts/{administrator.Id}", "PUT", new UpdateAccountRequest(AccountRole.Reader, false), admin))
            Check(response.StatusCode == HttpStatusCode.Conflict, "HTTP last administrator demotion prevented");
        using (var response = await SendAsync(client, url, $"admin/accounts/{administrator.Id}", "PUT", new UpdateAccountRequest(AccountRole.Administrator, true), admin))
            Check(response.StatusCode == HttpStatusCode.Conflict, "HTTP last administrator disable prevented");
        using (var response = await SendAsync(client, url, "admin/management/audit", session: admin))
        {
            var audit = (await response.Content.ReadFromJsonAsync<ManagementAuditEntry[]>(WebJson))!;
            Check(audit.Any(x => x.Event.Contains($"actor=account:{administrator.Id}:api-admin")), "Administrative job/approval audit names real local administrator");
            Check(audit.Any(x => x.Event.Contains($"actor=account:{op.Session.Account.Id}:api-operator")), "Diagnostic audit names real Operator");
        }
        using (var response = await SendAsync(client, url, $"admin/sessions/{op.Session.SessionId}/revoke", "POST", session: admin))
            Check(response.StatusCode == HttpStatusCode.NoContent, "Administrator revokes an Operator session");
        using (var response = await SendAsync(client, url, "admin/fleet", session: op, bearer: legacy))
            Check(response.StatusCode == HttpStatusCode.Unauthorized, "Revoked cookie cannot fall back to legacy administrator token");
        using (var response = await SendAsync(client, url, $"admin/accounts/{reader.Session.Account.Id}", "PUT", new UpdateAccountRequest(AccountRole.Operator, false, Secret()), admin))
            Check(response.StatusCode == HttpStatusCode.OK, "Administrator changes role/password");
        using (var response = await SendAsync(client, url, "auth/session", session: reader))
            Check(response.StatusCode == HttpStatusCode.Unauthorized, "Role/password change immediately invalidates prior session");
        using (var response = await SendAsync(client, url, $"admin/devices/{deviceId}/revoke", "POST", session: admin))
            Check(response.StatusCode == HttpStatusCode.NoContent, "Administrator can revoke device and cancel waiting fixture jobs");
        var serialized = await File.ReadAllTextAsync(Path.Combine(root, "accounts.json"));
        Check(new[] { adminPassword, readerPassword, operatorPassword, admin.Cookie, op.Cookie, reader.Cookie }.All(x => !serialized.Contains(x, StringComparison.Ordinal)),
            "Durable account store contains no plaintext passwords or session capabilities");
        using (var response = await SendAsync(client, url, "auth/logout", "POST", session: admin, csrf: ""))
            Check(response.StatusCode == HttpStatusCode.Forbidden, "Logout itself requires CSRF protection");
        using (var response = await SendAsync(client, url, "auth/logout", "POST", session: admin))
            Check(response.StatusCode == HttpStatusCode.NoContent, "Logout revokes current session");
        using (var response = await SendAsync(client, url, "admin/fleet", session: admin))
            Check(response.StatusCode == HttpStatusCode.Unauthorized, "Logged-out session denied");
        var throttled = false;
        for (var attempt = 0; attempt < 21; attempt++)
        {
            using var response = await SendAsync(client, url, "auth/login", "POST", new LoginRequest("unknown-user", Secret()), origin: url);
            if (response.StatusCode != HttpStatusCode.TooManyRequests) continue;
            Check(response.Content.Headers.ContentType?.MediaType == "application/problem+json", "Login rate rejection uses Problem Details");
            throttled = true; break;
        }
        Check(throttled, "Global bounded login limiter rejects excess attempts");
        Console.WriteLine($"PASS {_assertions - startCount} HTTPS account/role/CSRF/session integration assertions.");
    }
}
