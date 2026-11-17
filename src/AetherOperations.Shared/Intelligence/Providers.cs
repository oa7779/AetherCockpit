using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AetherOperations.Platform;

namespace AetherOperations.Intelligence;

public sealed class AetherModelRegistry
{
    public const int CurrentCatalogVersion = 1;
    public const int MaximumModels = 16;
    public Guid DiagnosticId { get; } = Guid.NewGuid();
    public static ImmutableArray<FabricModelCandidate> Candidates { get; } =
    [
        new("phi-4-mini", "Phi-4 Mini", "Phi", "Deutsch, Interaktion, Zusammenfassung, RAG",
            "Nicht installiert; konkrete Variante, Revision, Lizenz und Hash bewusst freigeben."),
        new("phi-4", "Phi-4", "Phi", "Lokale Analyse, Berichte, Planprüfung",
            "Nicht installiert; konkrete Variante, Revision, Lizenz und Hash bewusst freigeben."),
        new("nemotron-3-nano:4b", "Nemotron 3 Nano 4B", "Nemotron", "Für den aktuellen AETHER-Katalog nach wiederkehrenden deutschen Schreibfehlern und unklarer Rückwegformulierung abgelehnt.",
            "Ollama-Tag und Q4_K_M-Manifest geprüft; keine automatische Auswahl."),
        new("nemotron-3-nano-30b-a3b", "Nemotron 3 Nano 30B-A3B", "Nemotron", "Agentische Planung und Korrelation; Deutsch separat prüfen",
            "Nicht installiert; konkrete Variante, Revision, Lizenz und Hash bewusst freigeben.")
    ];
    public static FabricModel Fallback { get; } = new("aether.deterministic", "1", "Deterministic", "Nicht anwendbar",
        "AETHER-Projektlizenz", new("https://github.com/oa7779/AetherCockpit"), PlatformJson.TextHash("aether.deterministic/v1"),
        ["de", "en"], 0, 0, 0, FabricTrust.BuiltIn, true, true, true, "Deterministisch; kein Sprachmodell");
    private readonly Dictionary<string, (FabricModel Model, IAetherInferenceProvider Provider)> _models = [];
    private readonly HashSet<string> _automatic = new(StringComparer.Ordinal);
    public AetherModelRegistry() => _models.Add(Fallback.Id, (Fallback, new DeterministicInferenceProvider()));
    public ImmutableArray<(FabricModel Model, IAetherInferenceProvider Provider)> Entries
    { get { lock (_models) return _models.Values.ToImmutableArray(); } }
    public ImmutableArray<(FabricModel Model, IAetherInferenceProvider Provider)> AutomaticEntries
    {
        get { lock (_models) return _automatic.Select(id => _models[id]).ToImmutableArray(); }
    }
    public bool IsAutomatic(string id) { lock (_models) return _automatic.Contains(id); }
    public void ReplaceAutomaticProvider(string id, IAetherInferenceProvider expected, CompatibleInferenceProvider replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.Info.Kind != FabricProviderKind.Ollama || !replacement.IsLoopback ||
            replacement.Info.Status != FabricStatus.Ready)
            throw new InvalidDataException("Der automatische Ersatzprovider muss ein betriebsbereiter lokaler Ollama-Provider sein.");
        lock (_models)
        {
            if (!_automatic.Contains(id) || !_models.TryGetValue(id, out var current) ||
                !ReferenceEquals(current.Provider, expected))
                throw new InvalidOperationException("Der automatische Modellprovider hat sich geändert oder ist nicht freigegeben.");
            if (current.Provider is not CompatibleInferenceProvider previous ||
                previous.Info.Id != replacement.Info.Id || previous.RuntimePolicy != replacement.RuntimePolicy)
                throw new InvalidDataException("Identität und Runtime-Richtlinie des automatischen Providers müssen unverändert bleiben.");
            _models[id] = (current.Model, replacement);
        }
    }
    public static void Validate(FabricModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.Id) || model.Id.Length > 256 || !model.Id.All(c => char.IsAsciiLetterOrDigit(c) || "._:/-".Contains(c)) ||
            string.IsNullOrWhiteSpace(model.Revision) ||
            model.Revision.Length > 160 || string.IsNullOrWhiteSpace(model.License) || model.License.Length > 512 ||
            model.Origin is null || model.Origin.Scheme != "https" || model.Origin.UserInfo.Length != 0 || model.Origin.Query.Length != 0 ||
            model.Digest is null || model.Digest.Length != 64 || !model.Digest.All(char.IsAsciiHexDigit) ||
            model.Languages.IsDefaultOrEmpty || model.Languages.Length > 32 || model.Languages.Any(l => l.Length is < 2 or > 12 || !l.All(c => char.IsAsciiLetter(c) || c == '-')) ||
            model.DeclaredContextTokens is < 0 or > 1048576 || model.RequiredRamBytes < 0 || model.RequiredVramBytes < 0 ||
            !Enum.IsDefined(model.Trust) || !model.LicenseAccepted ||
            model.Id != Fallback.Id && model.Trust != FabricTrust.OperatorApproved ||
            model.Trust is not (FabricTrust.BuiltIn or FabricTrust.OperatorApproved) ||
            model.AutomaticSelectionPriority is < 0 or > 1000 ||
            model.FallbackModelId is { } fallbackId &&
                (fallbackId == model.Id || fallbackId.Length > 256 ||
                 !fallbackId.All(c => char.IsAsciiLetterOrDigit(c) || "._:/-".Contains(c))) ||
            string.IsNullOrWhiteSpace(model.ExecutionProvider))
            throw new InvalidDataException("Modell benötigt belegte Identität, Digest, Sprachgrenzen und ausdrückliche Lizenz-/Vertrauensfreigabe.");
    }
    public void Register(FabricModel model, IAetherInferenceProvider provider)
    {
        Validate(model);
        if (model.Id == Fallback.Id || provider.Info.ApiVersion != 1 || provider.Info.Status != FabricStatus.Ready)
            throw new InvalidDataException("Provider nicht betriebsbereit oder reservierte Modellidentität.");
        if (model.LocalPath is { } path)
        {
            if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("Absoluter Modellpfad erforderlich.");
            PlatformStore.CheckPath(path);
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            if (!hash.Equals(model.Digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Modellhash stimmt nicht.");
        }
        lock (_models)
        {
            if (_models.Count >= MaximumModels || !_models.TryAdd(model.Id, (model, provider)))
                throw new InvalidOperationException("Modellregistry voll oder Identität bereits registriert.");
        }
    }
    public void RegisterFromCatalog(int catalogVersion, FabricModel model, IAetherInferenceProvider provider,
        bool hardwareQualified)
    {
        if (catalogVersion != CurrentCatalogVersion)
            throw new InvalidDataException("Modellkatalogversion wird von dieser AETHER-Version nicht unterstützt.");
        Validate(model);
        if (!hardwareQualified || !model.Offline || !model.StructuredOutput ||
            !model.Languages.Contains("de", StringComparer.Ordinal) || model.RequiredRamBytes <= 0 ||
            model.DeclaredContextTokens < 4096 || model.Trust != FabricTrust.OperatorApproved ||
            !model.LicenseAccepted || provider.Info.Kind != FabricProviderKind.Ollama ||
            provider.Info.Endpoint is not { } endpoint ||
            !System.Net.IPAddress.TryParse(endpoint.Host, out var address) || !System.Net.IPAddress.IsLoopback(address))
            throw new InvalidDataException("Modell erfüllt den AETHER-Katalogvertrag für lokale automatische Nutzung nicht.");
        Register(model, provider);
        lock (_models) _automatic.Add(model.Id);
    }
    public static ImmutableArray<FabricProviderInfo> Capabilities =>
        Enum.GetValues<FabricProviderKind>().Select(kind => new FabricProviderInfo(1, kind.ToString(), kind,
            kind == FabricProviderKind.Deterministic ? FabricStatus.Ready : FabricStatus.NotConfigured,
            kind == FabricProviderKind.Deterministic ? "Modellloser geprüfter Pfad" :
            kind is FabricProviderKind.Ollama or FabricProviderKind.OpenAiCompatible or FabricProviderKind.Nim
                ? "HTTP-Adapter vorhanden; Modell, Endpunkt und Vertrauen ausdrücklich konfigurieren"
                : "Vertragsgrenze; keine native Inferenz im erhöhten Cockpit geladen")).ToImmutableArray();
}

public sealed record AetherModelCatalogEntry(
    [property: JsonRequired] FabricModel Model,
    [property: JsonRequired] FabricProviderKind Provider,
    [property: JsonRequired] Uri Endpoint,
    [property: JsonRequired] string ModelStorePath,
    [property: JsonRequired] bool HardwareQualified);
public sealed record AetherModelCatalogDocument(
    [property: JsonRequired] int Schema,
    [property: JsonRequired] int CatalogVersion,
    [property: JsonRequired] ImmutableArray<AetherModelCatalogEntry> Models)
{
    public int KeepAliveSeconds { get; init; } = (int)AetherModelRuntimePolicy.Default.KeepAlive.TotalSeconds;
}

public static class AetherModelCatalog
{
    public const int SetupContractSchema = 1;
    public const int MaximumCatalogBytes = 65536;
    public const int MaximumModels = AetherModelRegistry.MaximumModels - 1;
    public const int MaximumKeepAliveSeconds = AetherModelRuntimePolicy.DefaultKeepAliveSeconds;
    public static string DefaultPath => AetherLocalAiStoreContract.CatalogPath;
    public static string ModelStorePath => AetherLocalAiStoreContract.ModelStorePath;

    public static AetherModelCatalogDocument Read(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("Der lokale AETHER-Modellkatalog darf nicht aus einem Netzwerkpfad geladen werden.");
        path = Path.GetFullPath(path);
        PlatformStore.CheckPath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumCatalogBytes) throw new InvalidDataException("AETHER-Modellkatalog überschreitet das Größenlimit.");
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaximumCatalogBytes)
                throw new InvalidDataException("AETHER-Modellkatalog überschreitet das Größenlimit.");
            buffer.Write(chunk, 0, read);
        }
        var json = new System.Text.UTF8Encoding(false, true).GetString(buffer.ToArray());
        var catalog = FabricJson.Parse<AetherModelCatalogDocument>(json);
        Validate(catalog);
        return catalog;
    }

    public static void Register(AetherModelCatalogDocument catalog, AetherModelRegistry registry,
        ICollection<CompatibleInferenceProvider> providers)
    {
        Validate(catalog);
        foreach (var entry in catalog.Models)
        {
            if (entry.Provider != FabricProviderKind.Ollama ||
                !string.Equals(Path.GetFullPath(entry.ModelStorePath), Path.GetFullPath(ModelStorePath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("AETHER-Katalog verweist nicht auf den festgelegten lokalen Modellstore und Ollama-Provider.");
            var provider = new CompatibleInferenceProvider("aether.ollama.local", entry.Provider, entry.Endpoint,
                runtimePolicy: new(TimeSpan.FromSeconds(catalog.KeepAliveSeconds)));
            try
            {
                registry.RegisterFromCatalog(catalog.CatalogVersion, entry.Model, provider, entry.HardwareQualified);
                providers.Add(provider);
            }
            catch
            {
                provider.Dispose();
                throw;
            }
        }
    }

    public static void Validate(AetherModelCatalogDocument catalog)
    {
        if (catalog.Schema != SetupContractSchema || catalog.CatalogVersion != AetherModelRegistry.CurrentCatalogVersion ||
            catalog.KeepAliveSeconds is < 1 or > MaximumKeepAliveSeconds ||
            catalog.Models.IsDefault || catalog.Models.Length > MaximumModels ||
            catalog.Models.Any(entry => entry is null || entry.Model is null || entry.Endpoint is null ||
                string.IsNullOrWhiteSpace(entry.ModelStorePath)) ||
            catalog.Models.Select(entry => entry.Model.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Models.Length)
            throw new InvalidDataException("AETHER-Modellkatalog oder Setup-Vertrag ist ungültig oder nicht kompatibel.");
        foreach (var entry in catalog.Models)
        {
            AetherModelRegistry.Validate(entry.Model);
            CompatibleInferenceProvider.ValidateEndpoint(entry.Endpoint, false);
            if (entry.Model.Id == AetherModelRegistry.Fallback.Id ||
                entry.Provider != FabricProviderKind.Ollama || !entry.HardwareQualified ||
                !string.Equals(Path.GetFullPath(entry.ModelStorePath), Path.GetFullPath(ModelStorePath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                !entry.Model.Offline || !entry.Model.StructuredOutput ||
                !entry.Model.Languages.Contains("de", StringComparer.Ordinal) || entry.Model.RequiredRamBytes <= 0 ||
                entry.Model.DeclaredContextTokens < 4096 || entry.Model.Trust != FabricTrust.OperatorApproved ||
                !entry.Model.LicenseAccepted || entry.Model.LocalPath is not null)
                throw new InvalidDataException("AETHER-Katalogeintrag ist nicht für den automatischen lokalen Betrieb qualifiziert.");
            PlatformStore.CheckPath(entry.ModelStorePath);
        }
    }
}

public sealed class DeterministicInferenceProvider : IAetherInferenceProvider
{
    public FabricProviderInfo Info { get; } = new(1, "aether.deterministic", FabricProviderKind.Deterministic,
        FabricStatus.Ready, "Kein Sprachmodell; feste lesende Aktionszuordnung");
    public Task<FabricResponse> InferAsync(FabricModel model, FabricRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(request.Prompt);
        var targets = document.RootElement.GetProperty("targets").EnumerateArray().Select(e => e.GetGuid()).ToImmutableArray();
        var proposal = new FabricProposal(1, request.Task, AetherToolBroker.AllowedActionIds[0], targets,
            ImmutableDictionary<string, JsonElement>.Empty);
        return Task.FromResult(new FabricResponse(model.Id, JsonSerializer.Serialize(proposal, FabricJson.Options),
            null, null, null, null, null));
    }
}

public sealed record FabricModelInventory(bool Reachable, bool Found, string? ActualDigest, bool? Resident,
    long? ResidentModelBytes = null, long? ResidentModelVramBytes = null);

public sealed class CompatibleInferenceProvider : IAetherInferenceProvider, IDisposable
{
    private readonly HttpClient _client;
    public FabricProviderInfo Info { get; }
    public AetherModelRuntimePolicy RuntimePolicy { get; }
    public FabricModelInventory? LastInventory { get; private set; }
    public CompatibleInferenceProvider(string id, FabricProviderKind kind, Uri endpoint, bool explicitExternalConfiguration = false,
        AetherModelRuntimePolicy? runtimePolicy = null)
    {
        ValidateEndpoint(endpoint, explicitExternalConfiguration);
        if (kind is not (FabricProviderKind.Ollama or FabricProviderKind.OpenAiCompatible or FabricProviderKind.Nim))
            throw new ArgumentException("Nicht unterstütztes HTTP-Inferenzprotokoll.");
        RuntimePolicy = runtimePolicy ?? AetherModelRuntimePolicy.Default;
        _client = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false })
        { Timeout = Timeout.InfiniteTimeSpan };
        Info = new(1, id, kind, FabricStatus.Ready, "Konfiguriert; Modellgesundheit erst nach expliziter Prüfung", endpoint);
    }
    public static void ValidateEndpoint(Uri endpoint, bool external)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0 || endpoint.AbsolutePath != "/" ||
            endpoint.Scheme is not ("http" or "https") ||
            (!IPAddress.TryParse(endpoint.Host, out var address) || !IPAddress.IsLoopback(address)) &&
                (!external || endpoint.Scheme != "https"))
            throw new ArgumentException("Explizite Loopback-IP oder ausdrücklich konfigurierter HTTPS-Endpunkt ohne Umleitung/Geheimnisse erforderlich.");
    }
    public bool IsLoopback => IPAddress.TryParse(Info.Endpoint!.Host, out var address) && IPAddress.IsLoopback(address);
    public async Task<string> ReadOllamaVersionAsync(CancellationToken token)
    {
        using var document = await SendAsync(new(HttpMethod.Get, new Uri(Info.Endpoint!, "api/version")), token).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(version.GetString()))
            throw new InvalidDataException("Ollama-Versionsantwort ist ungültig.");
        return version.GetString()!;
    }
    private async Task<JsonDocument> SendAsync(HttpRequestMessage message, CancellationToken token)
    {
        using (message)
        using (var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 1024 * 1024) throw new InvalidDataException("Providerantwort zu groß.");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(bytes, token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > 1024 * 1024) throw new InvalidDataException("Providerantwort überschreitet das Budget.");
                buffer.Write(bytes, 0, read);
            }
            return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        }
    }
    public async Task CheckModelAsync(FabricModel model, CancellationToken token)
    {
        try { await CheckModelCoreAsync(model, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or IndexOutOfRangeException)
        { throw new InvalidDataException("Providerinventar verletzt den erwarteten Metadatenvertrag.", ex); }
    }
    private async Task CheckModelCoreAsync(FabricModel model, CancellationToken token)
    {
        AetherModelRegistry.Validate(model);
        LastInventory = null;
        using var response = await SendAsync(new(HttpMethod.Get, new Uri(Info.Endpoint!,
            Info.Kind == FabricProviderKind.Ollama ? "api/tags" : "v1/models")), token).ConfigureAwait(false);
        if (Info.Kind == FabricProviderKind.Ollama)
        {
            var matches = response.RootElement.GetProperty("models").EnumerateArray()
                .Where(item => item.GetProperty("name").GetString() == model.Id).ToArray();
            var actualDigest = matches.Length == 1
                ? matches[0].GetProperty("digest").GetString()?.Replace("sha256:", "", StringComparison.Ordinal) : null;
            LastInventory = new(true, matches.Length > 0, actualDigest, null);
            if (matches.Length == 0)
                throw new InvalidDataException("Modell auf diesem Ollama-Server nicht sichtbar; Modellstore und Serverkonfiguration prüfen.");
            if (matches.Length != 1 || !string.Equals(
                actualDigest, model.Digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Ollama-Modelldigest widerspricht dem freigegebenen Manifest; Integrität nicht bestätigt.");
        }
        else if (!response.RootElement.GetProperty("data").EnumerateArray().Any(item => item.GetProperty("id").GetString() == model.Id))
            throw new InvalidDataException("Freigegebene Modell-ID wird vom Endpunkt nicht angeboten; kein Modellwechsel.");
    }
    public async Task<FabricResponse> InferAsync(FabricModel model, FabricRequest request, CancellationToken token)
    {
        try { return await InferCoreAsync(model, request, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or IndexOutOfRangeException)
        { throw new InvalidDataException("Providerantwort verletzt den erwarteten Inferenzvertrag.", ex); }
    }
    private async Task<FabricResponse> InferCoreAsync(FabricModel model, FabricRequest request, CancellationToken token)
    {
        if (!IsLoopback && (!request.ExternalConsent || request.Classification != DataClassification.Normal || model.Offline))
            throw new UnauthorizedAccessException("Externe Übertragung nicht erlaubt; sensible Daten bleiben lokal.");
        AetherSafetyClassifier.RequireNoSecrets(request.Prompt);
        await CheckModelAsync(model, token).ConfigureAwait(false);
        object body = Info.Kind == FabricProviderKind.Ollama
            ? new { model = model.Id, stream = false, format = AetherPromptRegistry.OllamaFormatFor(request.Task, request.Prompt), prompt = request.Prompt,
                system = AetherPromptRegistry.InstructionFor(request.Task, request.Prompt),
                options = new { num_ctx = request.ContextTokens, num_predict = request.MaximumOutputTokens, temperature = 0 },
                keep_alive = RuntimePolicy.OllamaKeepAliveValue }
            : new { model = model.Id, stream = false, max_tokens = request.MaximumOutputTokens,
                messages = new[] { new { role = "system", content = AetherPromptRegistry.InstructionFor(request.Task, request.Prompt) },
                    new { role = "user", content = request.Prompt } }, response_format = new { type = "json_object" } };
        using var document = await SendAsync(new(HttpMethod.Post, new Uri(Info.Endpoint!,
            Info.Kind == FabricProviderKind.Ollama ? "api/generate" : "v1/chat/completions"))
            { Content = JsonContent.Create(body) }, token).ConfigureAwait(false);
        var root = document.RootElement;
        if (Info.Kind == FabricProviderKind.Ollama)
        {
            if (root.GetProperty("model").GetString() != model.Id || !root.GetProperty("done").GetBoolean())
                throw new InvalidDataException("Provider meldet ein anderes Modell oder unvollständiges Ergebnis.");
            int? Count(string key) => root.TryGetProperty(key, out var value) ? value.GetInt32() : null;
            double? Seconds(string key) => root.TryGetProperty(key, out var value) ? value.GetDouble() / 1e9 : null;
            return new(model.Id, root.GetProperty("response").GetString() ?? throw new InvalidDataException("Modellantwort fehlt."),
                Count("prompt_eval_count"), Count("eval_count"), Seconds("eval_duration"), Seconds("load_duration"), null)
            {
                DoneReason = root.TryGetProperty("done_reason", out var reason) && reason.ValueKind != JsonValueKind.Null
                    ? reason.GetString() : null
            };
        }
        var usage = root.TryGetProperty("usage", out var u) ? u : default;
        if (root.GetProperty("model").GetString() != model.Id) throw new InvalidDataException("Provider hat die Modell-ID gewechselt.");
        var choices = root.GetProperty("choices");
        if (choices.GetArrayLength() != 1 || choices[0].GetProperty("finish_reason").GetString() != "stop")
            throw new InvalidDataException("Provider meldet keine einzelne vollständig abgeschlossene Antwort.");
        return new(model.Id, choices[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidDataException("Strukturierte Modellantwort fehlt."),
            usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("prompt_tokens", out var input) ? input.GetInt32() : null,
            usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("completion_tokens", out var output) ? output.GetInt32() : null,
            null, null, null);
    }
    public async Task UnloadAsync(FabricModel model, CancellationToken token)
    {
        try { await UnloadCoreAsync(model, token).ConfigureAwait(false); }
        catch (Exception ex) when (IsContractError(ex))
        { throw new InvalidDataException("Provider verletzt den Entladevertrag.", ex); }
    }
    private async Task UnloadCoreAsync(FabricModel model, CancellationToken token)
    {
        if (Info.Kind != FabricProviderKind.Ollama) throw new NotSupportedException("Dieser Endpunkt besitzt keinen qualifizierten Entladevertrag.");
        await CheckModelAsync(model, token).ConfigureAwait(false);
        using var document = await SendAsync(new(HttpMethod.Post, new Uri(Info.Endpoint!, "api/generate"))
            { Content = JsonContent.Create(new { model = model.Id, keep_alive = 0, stream = false }) }, token).ConfigureAwait(false);
        if (!document.RootElement.GetProperty("done").GetBoolean() ||
            document.RootElement.GetProperty("model").GetString() != model.Id)
            throw new InvalidDataException("Entladen nicht bestätigt.");
        if (await IsLoadedAsync(model, token).ConfigureAwait(false))
            throw new InvalidDataException("Modell laut Server weiterhin geladen; gemeinsam genutzten Zustand beachten.");
    }
    public async Task LoadAsync(FabricModel model, CancellationToken token)
    {
        try { await LoadCoreAsync(model, token).ConfigureAwait(false); }
        catch (Exception ex) when (IsContractError(ex))
        { throw new InvalidDataException("Provider verletzt den Ladevertrag.", ex); }
    }
    private async Task LoadCoreAsync(FabricModel model, CancellationToken token)
    {
        if (Info.Kind != FabricProviderKind.Ollama)
            throw new NotSupportedException("Dieser Endpunkt verwaltet das Laden selbst; kein separater qualifizierter Ladevertrag.");
        await CheckModelAsync(model, token).ConfigureAwait(false);
        using var document = await SendAsync(new(HttpMethod.Post, new Uri(Info.Endpoint!, "api/generate"))
            { Content = JsonContent.Create(new { model = model.Id, keep_alive = RuntimePolicy.OllamaKeepAliveValue, stream = false }) }, token).ConfigureAwait(false);
        if (!document.RootElement.GetProperty("done").GetBoolean() ||
            document.RootElement.GetProperty("model").GetString() != model.Id ||
            !await IsLoadedAsync(model, token).ConfigureAwait(false))
            throw new InvalidDataException("Server bestätigt kein geladenes freigegebenes Modell.");
    }

    public async Task<bool> IsLoadedAsync(FabricModel model, CancellationToken token)
    {
        try { return await IsLoadedCoreAsync(model, token).ConfigureAwait(false); }
        catch (Exception ex) when (IsContractError(ex))
        { throw new InvalidDataException("Provider verletzt den Modellstatusvertrag.", ex); }
    }
    private async Task<bool> IsLoadedCoreAsync(FabricModel model, CancellationToken token)
    {
        if (Info.Kind != FabricProviderKind.Ollama)
            throw new NotSupportedException("Für diesen Endpunkt ist kein qualifizierter Resident-Modellstatus verfügbar.");
        using var document = await SendAsync(new(HttpMethod.Get, new Uri(Info.Endpoint!, "api/ps")), token).ConfigureAwait(false);
        var matches = document.RootElement.GetProperty("models").EnumerateArray().Where(item =>
            item.GetProperty("name").GetString() == model.Id &&
            item.GetProperty("digest").GetString()?.Replace("sha256:", "", StringComparison.Ordinal)
                .Equals(model.Digest, StringComparison.OrdinalIgnoreCase) == true).ToArray();
        if (matches.Length > 1)
            throw new InvalidDataException("Ollama meldet dieselbe Modelldigest mehrfach als resident.");
        long? ReadBytes(string name)
        {
            if (!matches.Any() || !matches[0].TryGetProperty(name, out var value)) return null;
            if (value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var bytes) || bytes < 0)
                throw new InvalidDataException($"Ollama-Residentmetadaten {name} sind ungültig.");
            return bytes;
        }
        var resident = matches.Length == 1;
        if (LastInventory is { } inventory)
            LastInventory = inventory with
            {
                Resident = resident,
                ResidentModelBytes = ReadBytes("size"),
                ResidentModelVramBytes = ReadBytes("size_vram")
            };
        return resident;
    }
    private static bool IsContractError(Exception ex) =>
        ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or IndexOutOfRangeException;
    public void Dispose() => _client.Dispose();
}

public sealed record AetherModelRuntimePolicy(TimeSpan KeepAlive)
{
    public const int DefaultKeepAliveSeconds = 5 * 60;
    public static AetherModelRuntimePolicy Default { get; } = new(TimeSpan.FromSeconds(DefaultKeepAliveSeconds));

    public string OllamaKeepAliveValue
    {
        get
        {
            if (KeepAlive < TimeSpan.Zero) throw new InvalidOperationException("Keep-Alive darf nicht negativ sein.");
            if (KeepAlive == TimeSpan.Zero) return "0";
            if (KeepAlive.TotalHours >= 1 && KeepAlive.TotalMinutes % 60 == 0)
                return $"{(long)KeepAlive.TotalHours}h";
            if (KeepAlive.TotalMinutes >= 1 && KeepAlive.TotalSeconds % 60 == 0)
                return $"{(long)KeepAlive.TotalMinutes}m";
            return $"{(long)KeepAlive.TotalSeconds}s";
        }
    }
}
