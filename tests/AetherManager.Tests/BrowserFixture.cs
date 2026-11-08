using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using AetherManager.Server.Models;
using AetherManager.Server.Services;
using AetherOperations;

namespace AetherManager.Tests;

internal static partial class Runner
{
    private static async Task SeedFleetAsync(string root, JsonManagerStore store, ManagementService management)
    {
        var token = CancellationToken.None;
        for (var index = 0; index < 48; index++)
        {
            var code = await store.CreateEnrollmentCodeAsync(token);
            var enrolled = (await store.EnrollAsync(new CreateEnrollmentRequest
            {
                Code = code.Code, MachineName = $"FIXTURE-PC-{index + 1:00}", CockpitVersion = "synthetic-fixture"
            }, token))!.Value;
            if (index % 8 == 0) continue;
            await management.SetDeviceApprovalAsync(enrolled.Device.Id, true, token);
            if (index % 3 != 2)
                await management.HeartbeatAsync(enrolled.Device.Id, new("synthetic-fixture", false, false), token);
            if (index % 4 == 1)
                await store.AddReportAsync(enrolled.Device.Id, DateTimeOffset.UtcNow,
                    JsonSerializer.SerializeToElement(new { fixture = true, summary = "Synthetic report; no machine collection or Windows action", sequence = index + 1 }), token);
            if (index % 8 == 7) await management.SetDeviceApprovalAsync(enrolled.Device.Id, false, token);
        }
        var path = Path.Combine(root, "management.json");
        var state = JsonSerializer.Deserialize<ManagementService.State>(await File.ReadAllTextAsync(path), WebJson)!;
        for (var index = 0; index < state.Devices.Count; index++)
            if (index % 3 == 1 && state.Devices[index].LastSeenAt is not null)
                state.Devices[index] = state.Devices[index] with { LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-15) };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, WebJson));
    }

    private static async Task<int> ServeFixtureAsync(string[] args)
    {
        if (args[0] != "--serve-fixture" || args.Length != 1 && (args.Length != 3 || args[1] != "--seconds") ||
            args.Length == 3 && (!int.TryParse(args[2], out var parsed) || parsed is < 30 or > 1800))
        {
            Console.Error.WriteLine("Usage: AetherManager.Tests.dll --serve-fixture [--seconds 30..1800]");
            return 2;
        }
        var seconds = args.Length == 3 ? int.Parse(args[2]) : 600;
        var root = Path.Combine(Directory.GetCurrentDirectory(), "tests", ".manager-test-runs", "browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Process? process = null;
        Task<string>? stdout = null, stderr = null;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var data = Path.Combine(root, "data");
            var store = new JsonManagerStore(data);
            await store.InitializeAsync(cancellation.Token);
            var management = new ManagementService(data, store);
            await SeedFleetAsync(data, store, management);
            var password = Secret();
            using (var accounts = new LocalAccountService(data, TimeProvider.System))
            {
                await accounts.InitializeAsync(cancellation.Token);
                await accounts.BootstrapAsync(new("fixture-admin", password, AccountRole.Administrator), cancellation.Token);
                await accounts.CreateAsync(new("fixture-operator", password, AccountRole.Operator), "fixture", cancellation.Token);
                await accounts.CreateAsync(new("fixture-reader", password, AccountRole.Reader), "fixture", cancellation.Token);
            }
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var certificatePassword = Secret();
            var certificatePath = Path.Combine(root, "fixture.pfx");
            await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pfx, certificatePassword), cancellation.Token);
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SystemCockpit.slnx"))) directory = directory.Parent;
            var repository = directory?.FullName ?? throw new DirectoryNotFoundException("Fixture repository root missing.");
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var url = $"https://localhost:{port}";
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(repository, "src", "AetherManager.Server", "bin", "Release", "net10.0", "AetherManager.Server.dll"));
            start.ArgumentList.Add("--contentRoot"); start.ArgumentList.Add(Path.Combine(repository, "src", "AetherManager.Server"));
            foreach (var name in start.Environment.Keys.Where(x => x.StartsWith("AETHER_MANAGER_", StringComparison.OrdinalIgnoreCase) ||
                x.StartsWith("ASPNETCORE_Kestrel__", StringComparison.OrdinalIgnoreCase) || x.StartsWith("Kestrel__", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(name);
            start.Environment["AETHER_MANAGER_URLS"] = url;
            start.Environment["AETHER_MANAGER_DATA"] = data;
            start.Environment["AETHER_MANAGER_LEGACY_ADMIN_API"] = "false";
            start.Environment["ASPNETCORE_ALLOWEDHOSTS"] = "localhost";
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            start.Environment["DOTNET_ENVIRONMENT"] = "Production";
            start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Path"] = certificatePath;
            start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Password"] = certificatePassword;
            process = Process.Start(start) ?? throw new InvalidOperationException("Fixture server failed to start.");
            stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
            using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (message, cert, _, _) =>
                message.RequestUri?.Host == "localhost" && cert?.Thumbprint == certificate.Thumbprint };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
            var ready = false;
            for (var attempt = 0; attempt < 40 && !process.HasExited; attempt++)
            {
                try { using var response = await client.GetAsync(url + "/health", cancellation.Token); ready = response.IsSuccessStatusCode; }
                catch (HttpRequestException) { }
                if (ready) break;
                await Task.Delay(250, cancellation.Token);
            }
            if (!ready) throw new InvalidOperationException("Isolated fixture server not ready.");
            var credentialsPath = Path.Combine(root, "browser-credentials.json");
            await File.WriteAllTextAsync(credentialsPath, JsonSerializer.Serialize(new
            {
                url, password, administrator = "fixture-admin", @operator = "fixture-operator", reader = "fixture-reader",
                fixtureDeviceCount = 48, expiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds)
            }, WebJson), cancellation.Token);
            Console.WriteLine("FIXTURE_READY " + credentialsPath);
            Console.Out.Flush();
            var until = DateTimeOffset.UtcNow.AddSeconds(seconds);
            while (DateTimeOffset.UtcNow < until && !process.HasExited)
                await Task.Delay(1000, cancellation.Token);
            if (process.HasExited && process.ExitCode != 0) throw new InvalidOperationException("Fixture server exited unexpectedly.");
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 0; }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally
        {
            Console.CancelKeyPress -= cancel;
            if (process is not null)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                if (stdout is not null && stderr is not null) await Task.WhenAll(stdout, stderr);
                process.Dispose();
            }
            Directory.Delete(root, true);
        }
    }
}
