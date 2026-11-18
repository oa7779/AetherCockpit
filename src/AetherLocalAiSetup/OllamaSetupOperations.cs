using System.ComponentModel;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using AetherOperations.Intelligence;
using Microsoft.Win32;

namespace AetherLocalAiSetup;

internal sealed class OllamaSetupOperations : IAetherLocalAiSetupOperations
{
    private static readonly Uri InstallerUri = new("https://ollama.com/download/OllamaSetup.exe");
    private readonly Uri _defaultApiBase;
    private Uri _apiBase;
    private bool _apiEndpointSelected;
    private static Version MinimumOllamaVersion(AetherProductModel model) =>
        Version.Parse(model.MinimumOllamaVersion ?? "0.5.13");
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5)
    }) { Timeout = Timeout.InfiniteTimeSpan };
    private Process? _managedServer;

    public OllamaSetupOperations() : this(new Uri("http://" + AetherLocalAiStoreContract.Host + "/"))
    {
    }

    internal OllamaSetupOperations(Uri defaultApiBase)
    {
        CompatibleInferenceProvider.ValidateEndpoint(defaultApiBase, false);
        _defaultApiBase = defaultApiBase;
        _apiBase = _defaultApiBase;
    }

    internal Uri ManagedEndpoint => _apiBase;

    public async Task<AetherOllamaRuntimeStatus> InspectRuntimeAsync(AetherProductModel model, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(model);
        var defaultListenerPreserved = _managedServer is not { HasExited: false } &&
            IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == _defaultApiBase.Port);
        SelectManagedEndpoint();
        var executable = AetherOllamaExecutable.Find();
        if (executable is null)
            return new(false, false, false, null, null, true, false, false,
                GetConfiguredModelStorePath(), false, false, false, defaultListenerPreserved);

        var signature = await AetherOllamaExecutable.ReadSignatureAsync(executable, token).ConfigureAwait(false);
        var version = await ReadVersionAsync(executable, token).ConfigureAwait(false);
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port == _apiBase.Port).ToArray();
        var listenerAvailable = listeners.Length > 0;
        var loopbackOnly = listeners.All(endpoint => IPAddress.IsLoopback(endpoint.Address));
        var apiCompatible = false;
        var modelPresent = false;
        if (listenerAvailable && loopbackOnly && signature.Valid)
        {
            try
            {
                using var versionResponse = await Http.GetAsync(new Uri(_apiBase, "api/version"), token).ConfigureAwait(false);
                if (versionResponse.IsSuccessStatusCode)
                {
                    using var versionDocument = await JsonDocument.ParseAsync(
                        await versionResponse.Content.ReadAsStreamAsync(token).ConfigureAwait(false),
                        cancellationToken: token).ConfigureAwait(false);
                    apiCompatible = versionDocument.RootElement.TryGetProperty("version", out var apiVersion) &&
                        apiVersion.ValueKind == JsonValueKind.String &&
                        Version.TryParse(apiVersion.GetString()?.TrimStart('v'), out var parsedVersion) &&
                        parsedVersion >= MinimumOllamaVersion(model);
                }
                if (apiCompatible)
                {
                    using var tagsResponse = await Http.GetAsync(new Uri(_apiBase, "api/tags"), token).ConfigureAwait(false);
                    if (tagsResponse.IsSuccessStatusCode)
                    {
                        using var tags = await JsonDocument.ParseAsync(
                            await tagsResponse.Content.ReadAsStreamAsync(token).ConfigureAwait(false),
                            cancellationToken: token).ConfigureAwait(false);
                        modelPresent = tags.RootElement.GetProperty("models").EnumerateArray()
                            .Any(entry => ReadModelName(entry) == model.ModelId);
                    }
                }
            }
            catch (HttpRequestException)
            {
                apiCompatible = false;
            }
            catch (JsonException)
            {
                apiCompatible = false;
            }
        }

        var storePath = GetConfiguredModelStorePath();
        var ownership = ReadOwnership();
        var storeOwned = ownership is not null &&
            PathEquals(storePath, AetherLocalAiStoreContract.ModelStorePath) &&
            PathEquals(ownership.ModelStorePath, AetherLocalAiStoreContract.ModelStorePath);
        var modelOwned = storeOwned && model.ExpectedOllamaDigest is { } expectedDigest &&
            ownership!.Models.Any(entry => entry.ModelId == model.ModelId &&
                entry.Digest.Equals(expectedDigest, StringComparison.OrdinalIgnoreCase));

        // A running process may have inherited a different environment than this user's current configuration.
        var listenerOwned = _managedServer is { HasExited: false } managed &&
            IsListenerOwnedBy(_apiBase.Port, managed.Id);
        var storeVerified = storeOwned && (!listenerAvailable || listenerOwned);
        return new(true, signature.Valid, apiCompatible, version, executable, loopbackOnly,
            listenerAvailable, storeVerified, storePath, storeOwned, modelPresent, modelOwned,
            defaultListenerPreserved);
    }

    public Task<AetherModelVerification> RecoverAcceptanceStoreOwnershipAsync(
        AetherProductModel model, bool licenseAccepted, CancellationToken token)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var acceptanceProfilePath = AetherLocalAiStoreContract.AcceptanceProfilePath ??
            throw new InvalidOperationException("Ein AETHER-Acceptance-Profil für den kontrollierten Ownership-Recovery fehlt.");
        return AetherAcceptanceStoreOwnershipRecovery.RecoverAsync(
            AetherProductModelCatalog.Current,
            model,
            acceptanceProfilePath,
            Path.Combine(localAppData, "AETHER", "LocalAI.Acceptance"),
            AetherLocalAiStoreContract.RootPath,
            AetherLocalAiStoreContract.ModelStorePath,
            licenseAccepted,
            token);
    }

    public async Task InstallOfficialRuntimeAsync(
        IProgress<AetherLocalAiSetupProgress>? progress, CancellationToken token)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"AetherOllamaSetup-{Guid.NewGuid():N}.exe");
        try
        {
            using (var response = await Http.GetAsync(InstallerUri, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false))
            {
                if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest)
                    throw new InvalidDataException("Der offizielle Ollama-Installer hat auf eine nicht freigegebene Quelle umgeleitet.");
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri is not { } finalUri ||
                    finalUri.Scheme != Uri.UriSchemeHttps || !finalUri.Host.Equals("ollama.com", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Die Installerquelle weicht von der festgelegten offiziellen Ollama-Quelle ab.");

                var total = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    65536, FileOptions.Asynchronous | FileOptions.WriteThrough);
                var buffer = new byte[65536];
                long completed = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    completed = checked(completed + read);
                    progress?.Report(new(AetherLocalAiSetupPhase.ConfiguringOllama, completed, total));
                }
                await output.FlushAsync(token).ConfigureAwait(false);
            }

            var signature = await AetherOllamaExecutable.ReadSignatureAsync(tempPath, token).ConfigureAwait(false);
            if (!signature.Valid)
                throw new InvalidDataException("Der heruntergeladene Installer besitzt keine gültige Ollama Inc.-Authenticode-Signatur.");
            using var installer = Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES",
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new Win32Exception("Der offizielle Ollama-Installer konnte nicht gestartet werden.");
            try
            {
                await installer.WaitForExitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!installer.HasExited) installer.Kill(entireProcessTree: true);
                throw;
            }
            if (installer.ExitCode != 0)
                throw new InvalidOperationException($"Der Ollama-Installer wurde mit Exitcode {installer.ExitCode} beendet.");
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public Task ConfigureManagedStoreAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SelectManagedEndpoint();
        var root = AetherLocalAiStoreContract.RootPath;
        var models = AetherLocalAiStoreContract.ModelStorePath;
        EnsureNoReparsePoints(models);
        if (Directory.Exists(models) && Directory.EnumerateFileSystemEntries(models).Any() &&
            ReadOwnership() is null && !ContainsOnlyRetainedOllamaMetadata(models))
            throw new InvalidOperationException("Der vorgesehene AETHER-Modellstore enthält nicht zugeordnete Daten und wird nicht übernommen.");

        Directory.CreateDirectory(models);
        var owner = ReadOwnership();
        if (owner is not null && !PathEquals(owner.ModelStorePath, models))
            throw new InvalidDataException("Der vorhandene AETHER-Store-Herkunftsnachweis verweist auf einen anderen Pfad.");
        var document = owner ?? new StoreOwnership(1, models, [],
            AetherLocalAiStoreContract.IsAcceptanceMode ? null :
                Environment.GetEnvironmentVariable(AetherLocalAiStoreContract.EnvironmentVariable, EnvironmentVariableTarget.User),
            AetherLocalAiStoreContract.IsAcceptanceMode ? null :
                Environment.GetEnvironmentVariable(AetherLocalAiStoreContract.HostEnvironmentVariable, EnvironmentVariableTarget.User));
        WriteJsonAtomically(Path.Combine(root, AetherLocalAiStoreContract.OwnershipFileName), document);
        Environment.SetEnvironmentVariable(AetherLocalAiStoreContract.EnvironmentVariable, models, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(AetherLocalAiStoreContract.HostEnvironmentVariable,
            _apiBase.Authority, EnvironmentVariableTarget.Process);
        return Task.CompletedTask;
    }

    public async Task StartManagedServerAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SelectManagedEndpoint();
        if (_managedServer is { HasExited: false })
            return;
        var executable = AetherOllamaExecutable.Find() ??
            throw new FileNotFoundException("ollama.exe wurde nach der Runtimeinstallation nicht gefunden.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "serve",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        startInfo.Environment[AetherLocalAiStoreContract.EnvironmentVariable] = AetherLocalAiStoreContract.ModelStorePath;
        startInfo.Environment[AetherLocalAiStoreContract.HostEnvironmentVariable] = _apiBase.Authority;
        startInfo.Environment["OLLAMA_NO_CLOUD"] = "1";
        startInfo.Environment["OLLAMA_NOPRUNE"] = "1";
        var process = Process.Start(startInfo) ??
            throw new Win32Exception("Der verwaltete Ollama-Server konnte nicht gestartet werden.");
        _managedServer = process;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                if (process.HasExited)
                    throw new InvalidOperationException($"Der verwaltete Ollama-Server wurde mit Exitcode {process.ExitCode} beendet.");
                var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                    .Where(endpoint => endpoint.Port == _apiBase.Port).ToArray();
                if (listeners.Length > 0 && listeners.All(endpoint => IPAddress.IsLoopback(endpoint.Address)) &&
                    IsListenerOwnedBy(_apiBase.Port, process.Id))
                {
                    try
                    {
                        using var response = await Http.GetAsync(new Uri(_apiBase, "api/version"), timeout.Token).ConfigureAwait(false);
                        if (response.IsSuccessStatusCode) return;
                    }
                    catch (HttpRequestException) { }
                }
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (token.IsCancellationRequested) throw;
            throw new TimeoutException("Der Ollama-Server wurde innerhalb von 30 Sekunden nicht auf Loopback bereit.");
        }
        throw new TimeoutException("Der Ollama-Server wurde innerhalb von 30 Sekunden nicht auf Loopback bereit.");
    }

    public async Task StopManagedServerAsync()
    {
        var process = _managedServer;
        _managedServer = null;
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                }
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        finally
        {
            process.Dispose();
        }
    }

    public async Task PullModelAsync(AetherProductModel model,
        IProgress<AetherLocalAiSetupProgress>? progress, CancellationToken token)
    {
        EnsureApprovedModel(model);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_apiBase, "api/pull"))
        {
            Content = JsonContent.Create(new { name = model.ModelId, stream = true })
        };
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 8192, leaveOpen: true);
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            if (line.Length > 65536)
                throw new InvalidDataException("Ollama-Fortschrittsmeldung überschreitet das Größenlimit.");
            using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new InvalidDataException("Ollama-Modellabruf fehlgeschlagen: " +
                    (error.GetString() ?? "unbekannter Fehler"));
            var phase = root.TryGetProperty("status", out var status) && status.GetString() == "success"
                ? AetherLocalAiSetupPhase.VerifyingModel
                : AetherLocalAiSetupPhase.DownloadingModel;
            progress?.Report(new(phase, ReadInt64(root, "completed"), ReadInt64(root, "total")));
        }
    }

    public async Task<AetherModelVerification> VerifyModelAsync(AetherProductModel model, CancellationToken token)
    {
        EnsureApprovedModel(model);
        using var response = await Http.GetAsync(new Uri(_apiBase, "api/tags"), token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var tags = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false),
            cancellationToken: token).ConfigureAwait(false);
        var tag = tags.RootElement.GetProperty("models").EnumerateArray()
            .SingleOrDefault(entry => ReadModelName(entry) == model.ModelId);
        if (tag.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("Das Phi-4-Mini-Modell ist nach dem Abruf in /api/tags nicht vorhanden.");
        var digest = tag.GetProperty("digest").GetString()?.Replace("sha256:", "", StringComparison.Ordinal)
            ?? throw new InvalidDataException("Ollama meldet keinen Modelldigest.");
        if (!digest.Equals(model.ExpectedOllamaDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Ollama-Manifestdigest stimmt nicht mit dem AETHER-Katalog überein.");

        var manifestPaths = GetManifestPaths(model);
        string? manifestPath = null;
        foreach (var candidatePath in manifestPaths)
        {
            if (!File.Exists(candidatePath))
                continue;
            EnsureNoReparsePoints(candidatePath);
            await using var candidateStream = new FileStream(candidatePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var candidateDigest = Convert.ToHexString(await SHA256.HashDataAsync(candidateStream, token).ConfigureAwait(false));
            if (candidateDigest.Equals(model.ExpectedOllamaDigest, StringComparison.OrdinalIgnoreCase))
            {
                manifestPath = candidatePath;
                break;
            }
        }
        if (manifestPath is null)
            throw new InvalidDataException("Kein gespeichertes Ollama-Manifest stimmt mit dem versionierten Digest überein.");

        EnsureNoReparsePoints(manifestPath);
        await using var manifestStream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var manifest = await JsonDocument.ParseAsync(manifestStream, cancellationToken: token).ConfigureAwait(false);
        var config = manifest.RootElement.GetProperty("config");
        var configDigest = VerifyBlob(config, model);
        if (!configDigest.Equals(model.ExpectedConfigDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Der gespeicherte Ollama-Konfigurationslayer stimmt nicht mit dem AETHER-Katalog überein.");
        var layers = manifest.RootElement.GetProperty("layers");
        var modelLayers = new List<string>();
        foreach (var layer in layers.EnumerateArray())
            modelLayers.Add(VerifyBlob(layer, model));
        var expected = model.ExpectedLayers.Select(layer => layer.Digest).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (modelLayers.Count != expected.Count ||
            !modelLayers.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expected))
            throw new InvalidDataException("Die gespeicherten Ollama-Modelllayer stimmen nicht vollständig mit dem AETHER-Katalog überein.");
        return new(digest, System.Collections.Immutable.ImmutableArray.CreateRange(modelLayers));
    }

    public async Task<bool> RunFixedHealthCheckAsync(AetherProductModel model, CancellationToken token)
    {
        EnsureApprovedModel(model);
        using var response = await Http.PostAsJsonAsync(new Uri(_apiBase, "api/generate"), new
        {
            model = model.ModelId,
            prompt = "Gib ausschließlich das JSON-Objekt {\"status\":\"ready\"} zurück.",
            format = "json",
            stream = false,
            keep_alive = 0,
            options = new { num_ctx = model.ContextLimitTokens, num_predict = 16, temperature = 0 }
        }, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var result = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false),
            cancellationToken: token).ConfigureAwait(false);
        var root = result.RootElement;
        if (!root.TryGetProperty("model", out var modelId) || modelId.GetString() != model.ModelId ||
            !root.TryGetProperty("done", out var done) || !done.GetBoolean() ||
            !root.TryGetProperty("response", out var text) || text.GetString() is not { Length: > 0 } output)
            return false;
        try
        {
            using var json = JsonDocument.Parse(output);
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("status", out var status) &&
                status.GetString() == "ready";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public Task PersistQualifiedCatalogAsync(AetherProductModel model, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        EnsureApprovedModel(model);
        AetherProductModelCatalog.Validate(AetherProductModelCatalog.Current);
        var owner = ReadOwnership() ??
            throw new InvalidDataException("AETHER-Modellstore besitzt keinen gültigen Herkunftsnachweis.");
        if (!PathEquals(owner.ModelStorePath, AetherLocalAiStoreContract.ModelStorePath))
            throw new InvalidDataException("Der AETHER-Herkunftsnachweis zeigt auf einen anderen Modellstore.");
        var entries = owner.Models.Where(entry => entry.ModelId != model.ModelId)
            .Append(new OwnedModel(model.ModelId, model.ExpectedOllamaDigest!)).ToArray();
        WriteJsonAtomically(Path.Combine(AetherLocalAiStoreContract.RootPath,
            AetherLocalAiStoreContract.OwnershipFileName), owner with { Models = entries });

        var runtimeModel = new FabricModel(model.ModelId, model.Revision!,
            model.Publisher ?? model.DisplayName, model.Quantization!, model.LicenseName!,
            model.DownloadSource!, model.ExpectedOllamaDigest!, model.SupportedLanguages,
            model.ContextLimitTokens!.Value, model.RequiredModelRamBytes!.Value, model.RequiredModelVramBytes!.Value,
            FabricTrust.OperatorApproved, true, model.StructuredOutputTestPassed == true,
            model.OfflineTestPassed == true, FabricProviderKind.Ollama.ToString())
        {
            AutomaticSelectionPriority = model.AutomaticSelectionPriority,
            FallbackModelId = model.FallbackModelId
        };
        var entry = new AetherModelCatalogEntry(runtimeModel, FabricProviderKind.Ollama, _apiBase,
            AetherLocalAiStoreContract.ModelStorePath, true);
        var catalogPath = AetherModelCatalog.DefaultPath;
        var existing = File.Exists(catalogPath)
            ? AetherModelCatalog.Read(catalogPath).Models
            : System.Collections.Immutable.ImmutableArray<AetherModelCatalogEntry>.Empty;
        var catalog = new AetherModelCatalogDocument(AetherModelCatalog.SetupContractSchema,
            AetherModelRegistry.CurrentCatalogVersion,
            existing.Where(existingEntry => existingEntry.Model.Id != model.ModelId).Append(entry).ToImmutableArray());
        AetherModelCatalog.Validate(catalog);
        WriteJsonAtomically(catalogPath, catalog);
        return Task.CompletedTask;
    }

    private static string[] GetManifestPaths(AetherProductModel model)
    {
        var source = model.DownloadSource ??
            throw new InvalidDataException("Der qualifizierte Modellkatalog enthält keine Ollama-Manifestquelle.");
        if (!source.Host.Equals("registry.ollama.ai", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Nur die offizielle Ollama-Modellregistry ist für den verwalteten Setup-Pfad freigegeben.");
        var segments = source.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var manifestIndex = Array.IndexOf(segments, "manifests");
        if (segments.Length < 5 || segments[0] != "v2" || manifestIndex != segments.Length - 2 ||
            segments.Skip(1).Take(manifestIndex - 1).Any(segment =>
                segment.Length is < 1 or > 128 ||
                !segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')) ||
            segments[^1].Length is < 1 or > 128 ||
            !segments[^1].All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
            throw new InvalidDataException("Ollama-Manifestquelle besitzt keinen unterstützten, exakten Modellpfad.");
        var name = Path.Combine(segments[1..^2]);
        return
        [
            Path.Combine(AetherLocalAiStoreContract.ModelStorePath, "manifests-v2", "ollama.com", name, segments[^1]),
            Path.Combine(AetherLocalAiStoreContract.ModelStorePath, "manifests", "registry.ollama.ai", name, segments[^1])
        ];
    }

    private static string VerifyBlob(JsonElement layer, AetherProductModel model)
    {
        var digest = layer.GetProperty("digest").GetString()?.Replace("sha256:", "", StringComparison.Ordinal)
            ?? throw new InvalidDataException("Ollama-Manifest enthält keinen Layer-Digest.");
        var size = layer.GetProperty("size").GetInt64();
        var known = digest.Equals(model.ExpectedConfigDigest, StringComparison.OrdinalIgnoreCase)
            ? model.ExpectedConfigSizeBytes
            : model.ExpectedLayers.FirstOrDefault(entry =>
                entry.Digest.Equals(digest, StringComparison.OrdinalIgnoreCase))?.SizeBytes;
        if (known is null || known != size)
            throw new InvalidDataException("Ein Modell- oder Konfigurationslayer fehlt im versionierten AETHER-Katalog.");
        var path = Path.Combine(AetherLocalAiStoreContract.ModelStorePath, "blobs", "sha256-" + digest);
        EnsureNoReparsePoints(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != size)
            throw new InvalidDataException($"Layergröße stimmt nicht: {digest}.");
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Layerdigest stimmt nicht: {digest}.");
        return digest;
    }

    private static void EnsureApprovedModel(AetherProductModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        AetherProductModelCatalog.Validate(AetherProductModelCatalog.Current);
        var approved = AetherProductModelCatalog.Current.Models.SingleOrDefault(entry => entry.ModelId == model.ModelId);
        if (approved is null || approved != model ||
            approved.QualificationStatus != AetherModelQualificationStatus.Qualified ||
            !approved.ApprovedForAutomaticSelection || approved.ExpectedOllamaDigest is null ||
            approved.DownloadSource is null)
            throw new InvalidDataException("Ollama-Setup unterstützt nur exakt katalogisierte und qualifizierte Modelle.");
    }

    private static async Task<string?> ReadVersionAsync(string executable, CancellationToken token)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "--version",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new Win32Exception("Ollama-Version konnte nicht geprüft werden.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var result = await output.ConfigureAwait(false);
        _ = await error.ConfigureAwait(false);
        return process.ExitCode == 0 && result.Length < 4096 ? result.Trim() : null;
    }

    private static string? GetConfiguredModelStorePath()
    {
        if (AetherLocalAiStoreContract.IsAcceptanceMode &&
            Environment.GetEnvironmentVariable(AetherLocalAiStoreContract.EnvironmentVariable,
                EnvironmentVariableTarget.Process) is null)
            return AetherLocalAiStoreContract.ModelStorePath;
        var configured = Environment.GetEnvironmentVariable(AetherLocalAiStoreContract.EnvironmentVariable,
            EnvironmentVariableTarget.Process) ??
            Environment.GetEnvironmentVariable(AetherLocalAiStoreContract.EnvironmentVariable, EnvironmentVariableTarget.User);
        return configured ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");
    }

    internal static bool ContainsOnlyRetainedOllamaMetadata(string modelStorePath)
    {
        if (!Directory.Exists(modelStorePath)) return true;
        var root = new DirectoryInfo(modelStorePath);
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
            return false;
        var entries = root.EnumerateFileSystemInfos().ToArray();
        if (entries.Length == 0) return true;
        var metadata = entries.SingleOrDefault(entry =>
            entry.Name.Equals("metadata", StringComparison.OrdinalIgnoreCase));
        if (entries.Length != 1 || metadata is not DirectoryInfo metadataDirectory ||
            (metadataDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
            return false;
        var files = metadataDirectory.GetFileSystemInfos();
        if (files.Length == 0) return true;
        foreach (var item in files)
        {
            if (item is not FileInfo file ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                !Regex.IsMatch(file.Name, @"^sha256-[0-9a-f]{64}\.json$",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase) ||
                file.Length is <= 2 or > 65536)
                return false;
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("ollama_version", out var version) ||
                    version.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(version.GetString()) ||
                    !document.RootElement.TryGetProperty("kv", out var values) ||
                    values.ValueKind != JsonValueKind.Object)
                    return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return false;
            }
        }
        return true;
    }

    private void SelectManagedEndpoint()
    {
        if (_apiEndpointSelected) return;
        _apiEndpointSelected = true;
        var defaultBusy = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            .Any(endpoint => endpoint.Port == _defaultApiBase.Port);
        if (defaultBusy) _apiBase = FindFreeLoopbackEndpoint();
    }

    private Uri FindFreeLoopbackEndpoint()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                if (port != AetherLocalAiStoreContract.AcceptanceTestPort)
                    return new Uri($"http://127.0.0.1:{port}/");
            }
            finally
            {
                listener.Stop();
            }
        }
        throw new IOException("Kein freier AETHER-Ollama-Loopback-Endpunkt außerhalb des Acceptance-Ports verfügbar.");
    }

    private static bool IsListenerOwnedBy(int port, int processId)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var size = 0;
            var result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0);
            if (result != 122) throw new Win32Exception((int)result);
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTable(buffer, ref size, false, 2, 5, 0);
                if (result == 122) continue;
                if (result != 0) throw new Win32Exception((int)result);
                var count = Marshal.ReadInt32(buffer);
                var rowSize = Marshal.SizeOf<TcpRow>();
                for (var index = 0; index < count; index++)
                {
                    var row = Marshal.PtrToStructure<TcpRow>(IntPtr.Add(buffer, 4 + index * rowSize));
                    if (row.State == 2 && (ushort)IPAddress.NetworkToHostOrder((short)row.LocalPort) == port &&
                        row.ProcessId == processId)
                        return true;
                }
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        throw new Win32Exception(122, "Die TCP-Prozesszuordnung änderte sich während der Prüfung.");
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(IntPtr tcpTable, ref int size, bool order, int ipVersion,
        int tableClass, int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint ProcessId;
    }

    private static StoreOwnership? ReadOwnership()
    {
        var path = Path.Combine(AetherLocalAiStoreContract.RootPath, AetherLocalAiStoreContract.OwnershipFileName);
        if (!File.Exists(path)) return null;
        EnsureNoReparsePoints(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 65536) throw new InvalidDataException("AETHER-Store-Herkunftsnachweis ist zu groß.");
        return JsonSerializer.Deserialize<StoreOwnership>(stream, FabricJson.Options) is { Schema: 1 } ownership &&
            ownership.ModelStorePath is not null && ownership.Models is not null
            ? ownership
            : throw new InvalidDataException("AETHER-Store-Herkunftsnachweis ist ungültig.");
    }

    private static string? ReadModelName(JsonElement model) =>
        model.TryGetProperty("name", out var name) ? name.GetString() :
        model.TryGetProperty("model", out var modelName) ? modelName.GetString() : null;

    private static long? ReadInt64(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.TryGetInt64(out var result) ? result : null;

    private static bool PathEquals(string? left, string? right) => left is not null && right is not null &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void EnsureNoReparsePoints(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Der verwaltete AETHER-Modellstore darf keine umgeleiteten Pfade enthalten.");
        }
    }

    private static void WriteJsonAtomically<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new IOException("Zielordner konnte nicht bestimmt werden.");
        EnsureNoReparsePoints(path);
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       16384, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, FabricJson.Options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record StoreOwnership(
        [property: JsonPropertyName("schema")] int Schema,
        [property: JsonPropertyName("modelStorePath")] string ModelStorePath,
        [property: JsonPropertyName("models")] OwnedModel[] Models,
        [property: JsonPropertyName("previousModelsEnvironment")] string? PreviousModelsEnvironment = null,
        [property: JsonPropertyName("previousHostEnvironment")] string? PreviousHostEnvironment = null);

    private sealed record OwnedModel(
        [property: JsonPropertyName("modelId")] string ModelId,
        [property: JsonPropertyName("digest")] string Digest);
}
