using AetherClient.Service;
using AetherManager.Server.Models;
using AetherManager.Server.Services;
using AetherOperations;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AetherManager.Tests;

internal static partial class Runner
{
    private static int _assertions;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0) return await ServeFixtureAsync(args);
        var root = Path.Combine(Directory.GetCurrentDirectory(), "tests", ".manager-test-runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var phase = "management";
        try
        {
            var token = CancellationToken.None;
            var store = new JsonManagerStore(root);
            await store.InitializeAsync(token);
            var code = await store.CreateEnrollmentCodeAsync(token);
            var enrollment = await store.EnrollAsync(new CreateEnrollmentRequest
                { Code = code.Code, MachineName = "synthetic-device", CockpitVersion = "test" }, token);
            Check(enrollment is not null, "Enrollment accepted");
            var id = enrollment!.Value.Device.Id;
            var management = new ManagementService(root, store);
            await Reject<InvalidOperationException>(() => management.CreateJobAsync(id, new("inventory"), token));
            Check(await management.SetDeviceApprovalAsync(id, true, token), "Device manually approved");
            var initial = await management.HeartbeatAsync(id, new("test", false, false), token);
            Check(!initial.Policy.AllowChanges && !initial.LocalChangesAllowed, "Changes off by default");
            await Reject<ArgumentException>(() => management.SetPolicyAsync(id, new(false, true), token));
            await Reject<ArgumentException>(() => management.HeartbeatAsync(id, new("test", false, true), token));
            await Reject<ArgumentException>(() => management.CreateJobAsync(id, new("not-a-command"), token));
            await Reject<InvalidOperationException>(() => management.CreateJobAsync(id, new("dns-cache-clear", true), token));
            var diagnostic = await management.CreateJobAsync(id, new("inventory"), token);
            var claim = await management.ClaimAsync(id, token);
            Check(claim?.Job.Id == diagnostic.Id, "Claim returns fixed diagnostic");
            Check(await management.ClaimAsync(id, token) is null, "Running job not redispatched");
            var reopened = new ManagementService(root, store);
            Check(await reopened.ClaimAsync(id, token) is null, "Running claim persists across restart");
            await Reject<InvalidOperationException>(() => reopened.CompleteAsync(id, diagnostic.Id,
                new(new string('x', 64), 0, "fake", false), token));
            await Reject<ArgumentException>(() => reopened.CompleteAsync(id, diagnostic.Id,
                new(claim!.ClaimToken, 0, new string('x', 65537), false), token));
            var completion = new CompleteManagedJobRequest(claim!.ClaimToken, 0, "synthetic output only", false);
            Check((await reopened.CompleteAsync(id, diagnostic.Id, completion, token)).State == "completed", "Completion recorded");
            Check((await reopened.CompleteAsync(id, diagnostic.Id, completion, token)).Output == completion.Output, "Completion is idempotent");
            await Reject<InvalidOperationException>(() => reopened.CancelAsync(diagnostic.Id, token));
            await reopened.SetPolicyAsync(id, new(true, true), token);
            await Reject<InvalidOperationException>(() => reopened.CreateJobAsync(id, new("dns-cache-clear", true), token));
            await reopened.HeartbeatAsync(id, new("test", true, true), token);
            var updateId = Guid.NewGuid();
            var update = new WindowsUpdateSelection(updateId, 1, new string('A', 64), true);
            await Reject<InvalidOperationException>(() => reopened.CreateJobAsync(id, new("windows-update-install", true, null, update), token));
            var search = await reopened.CreateJobAsync(id, new("windows-update-search"), token);
            var searchClaim = await reopened.ClaimAsync(id, token);
            Check(searchClaim?.Job.Id == search.Id, "Update search claimed without running Windows Update");
            var offers = System.Text.Json.JsonSerializer.Serialize(new { updates = new[] {
                new { updateId, revision = 1, eulaHash = update.EulaHash, title = "Synthetic update", eulaText = "Synthetic license" } } });
            await reopened.CompleteAsync(id, search.Id, new(searchClaim!.ClaimToken, 0, offers, false), token);
            await Reject<InvalidOperationException>(() => reopened.CreateJobAsync(id,
                new("windows-update-install", true, search.Id, update with { EulaHash = new string('B', 64) }), token));
            await Reject<ArgumentException>(() => reopened.CreateJobAsync(id,
                new("windows-update-install", true, search.Id, update with { AcceptEula = false }), token));
            await Reject<ArgumentException>(() => reopened.CreateJobAsync(id, new("inventory", false, search.Id, update), token));
            var approvedUpdate = await reopened.CreateJobAsync(id, new("windows-update-install", true, search.Id, update), token);
            Check(approvedUpdate.Update == update && !approvedUpdate.Automatic, "Exactly reviewed update bound to manual job");
            await reopened.CancelAsync(approvedUpdate.Id, token);
            await Reject<InvalidOperationException>(() => reopened.CreateJobAsync(id, new("dns-cache-clear"), token));
            await Reject<InvalidOperationException>(() => reopened.CreateAutoRepairAsync(id, new(false, true), token));
            var auto = await reopened.CreateAutoRepairAsync(id, new(true, true), token);
            Check(auto?.ActionId == "dns-cache-clear" && auto.Automatic, "Narrow automatic repair queued with evidence");
            Check(await reopened.CreateAutoRepairAsync(id, new(true, true), token) is null, "Automatic repair cooldown persists");
            await reopened.SetPolicyAsync(id, new(), token);
            Check(await reopened.ClaimAsync(id, token) is null, "Revoked policy prevents queued changes");
            Check((await reopened.CancelAsync(auto!.Id, token)).State == "cancelled", "Queued repair cancelled");
            var queued = await reopened.CreateJobAsync(id, new("inventory"), token);
            Check(await reopened.SetDeviceApprovalAsync(id, false, token), "Device revoked");
            Check((await reopened.GetJobAsync(queued.Id, token))?.State == "cancelled", "Revocation cancels queued jobs");
            await Reject<InvalidOperationException>(() => reopened.ClaimAsync(id, token));
            Check(!await reopened.SetDeviceApprovalAsync(id, true, token), "Revoked device requires new enrollment");
            Check((await reopened.GetAuditAsync(token)).Length > 10, "Administrative transitions audited");
            Check(ManagedActionCatalog.All.Where(action => action.CanAutoRepair).Select(action => action.Id)
                .SequenceEqual(["dns-cache-clear"]), "Only DNS cache action eligible for automatic repair");
            Check(new ClientConfiguration("https://aether.example.net:5187/", id, "").ServerUri().Scheme == "https", "HTTPS target accepted");
            foreach (var url in new[] { "http://aether.example.net/", "https://user:pass@example.net/", "https://example.net/path", "https://example.net/?q=x" })
                await Reject<InvalidDataException>(() => Task.FromResult(new ClientConfiguration(url, id, "").ServerUri()));
            phase = "https-account-api";
            await VerifyTlsAsync(root);
            phase = "synthetic-graph";
            await VerifyGraphServiceAsync();
            phase = "local-account";
            await VerifyAccountsAsync(Path.Combine(root, "account-unit"));
            phase = "fleet";
            await VerifyFleetAsync(Path.Combine(root, "fleet-unit"));
            Console.WriteLine($"PASS {_assertions} management assertions; no Windows action executed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL at {DateTimeOffset.Now:O}; phase={phase}; fixture={root}; HRESULT=0x{ex.HResult:X8}");
            if (phase == "local-account")
                Console.Error.WriteLine("Account store: " + Path.Combine(root, "account-unit", "accounts.json"));
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        _assertions++;
    }

    private static async Task VerifyTlsAsync(string root)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var certificatePath = Path.Combine(root, "tls-fixture.pfx");
        await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pfx, password));
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SystemCockpit.slnx"))) directory = directory.Parent;
        var repository = directory?.FullName ?? throw new DirectoryNotFoundException("Test repository root missing.");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(repository, "src", "AetherManager.Server", "bin", "Release", "net10.0", "AetherManager.Server.dll"));
        start.ArgumentList.Add("--contentRoot");
        start.ArgumentList.Add(Path.Combine(repository, "src", "AetherManager.Server"));
        foreach (var name in start.Environment.Keys.Where(name => name.StartsWith("AETHER_MANAGER_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AETHER_GRAPH_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("ASPNETCORE_Kestrel__", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Kestrel__", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(name);
        start.Environment["AETHER_MANAGER_URLS"] = $"https://localhost:{port}";
        start.Environment["AETHER_MANAGER_DATA"] = Path.Combine(root, "tls-server-data");
        start.Environment["AETHER_MANAGER_CERT_THUMBPRINT"] = "";
        start.Environment["AETHER_MANAGER_LEGACY_ADMIN_API"] = "false";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_ALLOWEDHOSTS"] = "localhost";
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Path"] = certificatePath;
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Password"] = password;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("TLS fixture server failed to start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var handler = new HttpClientHandler
            {
                UseCookies = false,
                ServerCertificateCustomValidationCallback = (message, cert, _, _) =>
                    message.RequestUri?.Host == "localhost" && cert?.Thumbprint == certificate.Thumbprint
            };
            using var pinned = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var url = $"https://localhost:{port}";
            var ready = false;
            for (var attempt = 0; attempt < 40 && !process.HasExited; attempt++)
            {
                try { using var health = await pinned.GetAsync(url + "/health"); ready = health.IsSuccessStatusCode; }
                catch (HttpRequestException) { }
                if (ready) break;
                await Task.Delay(250);
            }
            if (!ready) throw new InvalidOperationException("TLS fixture did not become ready. " + (process.HasExited ? await stderr : ""));
            Check(ready, "Actual HTTPS endpoint responds using the isolated certificate");
            using var standard = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            await Reject<HttpRequestException>(async () => { using var response = await standard.GetAsync(url + "/health"); });
            Check((await pinned.GetStringAsync(url + "/")).Contains("Zentralverwaltung"), "Web administration works over HTTPS");
            using var unauthorized = await pinned.GetAsync(url + "/api/v1/admin/management/actions");
            Check(unauthorized.StatusCode == HttpStatusCode.Unauthorized, "TLS management endpoint still requires authentication");
            await VerifyAccountApiAsync(pinned, url, Path.Combine(root, "tls-server-data"));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
    }

    private static async Task Reject<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { _assertions++; return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
