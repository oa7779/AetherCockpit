using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherOperations.Intelligence;

public enum AetherModelQualificationStatus { Pending, Qualified, Rejected }

public enum AetherLocalAiCapability
{
    LocalAiUnavailable,
    LocalAiLimited,
    LocalAiReady,
    AdvancedLocalAiPossible
}

public enum AetherLocalAiSetupPhase
{
    CheckingSystem,
    InstallingAether,
    PreparingLocalAi,
    ConfiguringOllama,
    DownloadingModel,
    VerifyingModel,
    ConfiguringLocalAi,
    Completing
}

public sealed record AetherLocalAiSetupProgress(AetherLocalAiSetupPhase Phase, long? CompletedBytes = null, long? TotalBytes = null);

public sealed record AetherModelLayer(string MediaType, string Digest, long SizeBytes);

public sealed record AetherProductModel(
    int CatalogSchema,
    string ModelId,
    string DisplayName,
    string? Publisher,
    string? Variant,
    string? Revision,
    string? Runtime,
    FabricProviderKind? Provider,
    string? Quantization,
    string? LicenseName,
    Uri? LicenseTextUrl,
    bool LicenseAcceptanceRequired,
    Uri? DownloadSource,
    string? ExpectedOllamaDigest,
    string? ExpectedConfigDigest,
    long? ExpectedConfigSizeBytes,
    ImmutableArray<AetherModelLayer> ExpectedLayers,
    long? DownloadSizeBytes,
    long? InstallationSizeBytes,
    long? MinimumRamBytes,
    long? MinimumAvailableRamBytes,
    long? MinimumVramBytes,
    long? MinimumStorageBytes,
    ImmutableArray<string> SupportedRoles,
    int? ContextLimitTokens,
    AetherModelQualificationStatus QualificationStatus,
    DateTimeOffset? QualifiedAt,
    Uri? QualificationEvidence,
    bool ApprovedForAutomaticSelection,
    bool Advanced = false)
{
    public ImmutableArray<string> SupportedLanguages { get; init; } = [];
    public string? ModelFamily { get; init; }
    public decimal? ParameterCountBillions { get; init; }
    public long? PreferredRamBytes { get; init; }
    public string? RequiredGpuVendor { get; init; }
    public int AutomaticSelectionPriority { get; init; }
    public string? FallbackModelId { get; init; }
    public bool? ReasoningTestPassed { get; init; }
    public bool? DigestVerificationPassed { get; init; }
    public bool? GermanTestPassed { get; init; }
    public bool? StructuredOutputTestPassed { get; init; }
    public bool? ToolContractTestPassed { get; init; }
    public bool? OfflineTestPassed { get; init; }
    public bool? ResourceMeasurementPassed { get; init; }
    public Version? MinimumWindowsVersion { get; init; }
    public string? MinimumOllamaVersion { get; init; }
    public long? RecommendedRamBytes { get; init; }
    public long? RequiredModelRamBytes { get; init; }
    public long? RequiredModelVramBytes { get; init; }
    public long? RamSafetyReserveBytes { get; init; }
    public long? VramSafetyReserveBytes { get; init; }
    public long? StorageSafetyReserveBytes { get; init; }
    public int? MaximumParallelRequests { get; init; }
}

public sealed record AetherProductModelCatalogDocument(int Schema, int Version, ImmutableArray<AetherProductModel> Models);

public static class AetherLocalAiStoreContract
{
    public const string EnvironmentVariable = "OLLAMA_MODELS";
    public const string HostEnvironmentVariable = "OLLAMA_HOST";
    public const string OwnershipFileName = "aether-store.json";
    public const string RuntimeLeaseFileName = "aether-ollama-runtime.json";
    public const int AcceptanceTestPort = 11435;
    private const string AcceptanceProfileFileName = "aether-localai-acceptance.json";
    private static readonly Lazy<string?> AcceptanceProfileLocation = new(FindAcceptanceProfilePath);
    private static readonly Lazy<AcceptanceProfile?> Acceptance = new(ReadAcceptanceProfile);

    public static bool IsAcceptanceMode => Acceptance.Value is not null;
    public static string Host => Acceptance.Value?.OllamaHost ?? "127.0.0.1:11434";
    public static string RootPath => Acceptance.Value?.StoreRoot ??
        GetRootPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    public static string CatalogPath => Path.Combine(RootPath, "model-catalog.json");
    public static string ModelStorePath => Path.Combine(RootPath, "models");
    public static string? AcceptanceProfilePath => AcceptanceProfileLocation.Value;

    public static string GetRootPath(string localAppDataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataPath);
        return Path.Combine(Path.GetFullPath(localAppDataPath), "AETHER", "LocalAI");
    }

    private static AcceptanceProfile? ReadAcceptanceProfile()
    {
        var profilePath = AcceptanceProfileLocation.Value;
        if (profilePath is null) return null;
        using var stream = new FileStream(profilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 4096)
            throw new InvalidDataException("AETHER-Akzeptanzprofil überschreitet das Größenlimit.");
        var profile = JsonSerializer.Deserialize<AcceptanceProfile>(stream)
            ?? throw new InvalidDataException("AETHER-Akzeptanzprofil ist leer.");
        var localAppData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var acceptanceRoot = Path.Combine(localAppData, "AETHER", "LocalAI.Acceptance");
        var storeRoot = Path.GetFullPath(profile.StoreRoot);
        var profileId = Path.GetFileName(storeRoot);
        if (profile.Schema != 1 ||
            !string.Equals(Path.GetDirectoryName(storeRoot), acceptanceRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            profileId.Length is < 8 or > 64 ||
            !profileId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') ||
            !Uri.TryCreate("http://" + profile.OllamaHost + "/", UriKind.Absolute, out var endpoint) ||
            !IPAddress.TryParse(endpoint.Host, out var address) || !address.Equals(IPAddress.Loopback) ||
            endpoint.Port != AcceptanceTestPort || endpoint.AbsolutePath != "/" ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new InvalidDataException("AETHER-Akzeptanzprofil muss einen isolierten lokalen Store und Loopback-Ollama festlegen.");
        return new(profile.Schema, storeRoot, profile.OllamaHost);
    }

    private static string? FindAcceptanceProfilePath()
    {
        var directory = Path.GetFullPath(AppContext.BaseDirectory);
        for (var depth = 0; depth < 3; depth++)
        {
            var profilePath = Path.Combine(directory, AcceptanceProfileFileName);
            if (File.Exists(profilePath)) return profilePath;

            var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
            if (parent is null || string.Equals(parent, directory, StringComparison.Ordinal))
                break;
            directory = parent;
        }
        return null;
    }

    private sealed record AcceptanceProfile(int Schema, string StoreRoot, string OllamaHost);
}

public static class AetherAcceptanceStoreOwnershipRecovery
{
    private const string ModelId = "nemotron-3-nano:4b";
    private const string ExpectedHost = "127.0.0.1:11435";
    private const long ExpectedParameterCount = 3_973_556_832;
    private const string RegistryManifestPath = "v2/library/nemotron-3-nano/manifests/4b";
    private const string ModelManifestMediaType = "application/vnd.docker.distribution.manifest.v2+json";
    private const string ManifestListMediaType = "application/vnd.ollama.manifest.list.v2+json";

    public static async Task<AetherModelVerification> RecoverAsync(
        AetherProductModelCatalogDocument catalog,
        AetherProductModel model,
        string acceptanceProfilePath,
        string acceptanceRootPath,
        string storeRootPath,
        string modelStorePath,
        bool licenseAccepted,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(model);
        AetherProductModelCatalog.Validate(catalog);
        if (!licenseAccepted || !model.LicenseAcceptanceRequired)
            throw new UnauthorizedAccessException("Die NVIDIA-Modelllizenz muss ausdrücklich angenommen sein.");
        if (model.ModelId != ModelId || model.Publisher != "NVIDIA" || model.Revision != "4b" ||
            model.ModelFamily != "Nemotron 3 Nano" || model.ParameterCountBillions != 3.97m ||
            model.Quantization != "Q4_K_M" || model.LicenseName != "NVIDIA Open Model License" ||
            model.LicenseTextUrl?.AbsoluteUri !=
                "https://www.nvidia.com/en-us/agreements/enterprise-software/nvidia-open-model-license/" ||
            model.DownloadSource?.Host != "registry.ollama.ai" ||
            model.DownloadSource.AbsolutePath != "/" + RegistryManifestPath ||
            model.QualificationStatus != AetherModelQualificationStatus.Pending ||
            model.ApprovedForAutomaticSelection || model.ExpectedOllamaDigest is null ||
            model.ExpectedConfigDigest is null || model.ExpectedConfigSizeBytes is null ||
            model.ExpectedLayers.Length != 3 || model.DownloadSizeBytes is null ||
            model.ExpectedLayers.Sum(layer => layer.SizeBytes) + model.ExpectedConfigSizeBytes != model.DownloadSizeBytes)
            throw new InvalidDataException("Der Recovery-Pfad akzeptiert ausschließlich den katalogisierten, noch nicht qualifizierten Nemotron-3-Nano-4B-Kandidaten.");

        var catalogModel = catalog.Models.SingleOrDefault(entry => entry.ModelId == ModelId);
        if (catalogModel is null || catalogModel.QualificationStatus != AetherModelQualificationStatus.Pending ||
            catalogModel.ApprovedForAutomaticSelection || catalogModel.ExpectedOllamaDigest != model.ExpectedOllamaDigest ||
            catalogModel.ExpectedConfigDigest != model.ExpectedConfigDigest ||
            !catalogModel.ExpectedLayers.SequenceEqual(model.ExpectedLayers))
            throw new InvalidDataException("Der Recovery-Kandidat stimmt nicht mit dem unveränderten Pending-Katalogeintrag überein.");

        var fullAcceptanceRoot = Path.GetFullPath(acceptanceRootPath);
        var fullStoreRoot = Path.GetFullPath(storeRootPath);
        var fullModelStore = Path.GetFullPath(modelStorePath);
        var fullProfilePath = Path.GetFullPath(acceptanceProfilePath);
        var expectedParent = Path.Combine(fullAcceptanceRoot, Path.GetFileName(fullStoreRoot));
        if (!PathEquals(fullStoreRoot, expectedParent) ||
            !PathEquals(Path.GetDirectoryName(fullStoreRoot), fullAcceptanceRoot) ||
            !PathEquals(fullModelStore, Path.Combine(fullStoreRoot, "models")) ||
            Path.GetFileName(fullProfilePath) != "aether-localai-acceptance.json" ||
            !Directory.Exists(fullStoreRoot) || !Directory.Exists(fullModelStore))
            throw new InvalidDataException("Der Zielpfad ist kein vorhandener, isolierter AETHER-Acceptance-Store.");
        var storeName = Path.GetFileName(fullStoreRoot);
        if (storeName.Length is < 8 or > 64 ||
            !storeName.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            throw new InvalidDataException("Die Acceptance-Store-ID ist ungültig.");

        EnsureNoReparsePoints(fullAcceptanceRoot);
        EnsureNoReparsePoints(fullStoreRoot);
        EnsureNoReparsePoints(fullModelStore);
        EnsureNoReparsePoints(fullProfilePath);
        var profile = await ReadProfileAsync(fullProfilePath, token).ConfigureAwait(false);
        if (profile.Schema != 1 || profile.OllamaHost != ExpectedHost ||
            !PathEquals(profile.StoreRoot, fullStoreRoot))
            throw new InvalidDataException("Das Acceptance-Profil bestätigt weder diesen Store noch den isolierten Ollama-Port 11435.");

        var ownershipPath = Path.Combine(fullStoreRoot, AetherLocalAiStoreContract.OwnershipFileName);
        EnsureNoReparsePoints(ownershipPath);
        var owner = await ReadOwnershipAsync(ownershipPath, token).ConfigureAwait(false);
        if (owner.Schema != 1 || !PathEquals(owner.ModelStorePath, fullModelStore))
            throw new InvalidDataException("Der vorhandene Ownership-Nachweis zeigt nicht exakt auf den Acceptance-Store.");
        if (owner.Models.Length > 1 ||
            owner.Models.Any(entry => entry.ModelId != ModelId ||
                !entry.Digest.Equals(model.ExpectedOllamaDigest, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Der Store enthält Ownership-Einträge, die durch diesen Recovery-Nachweis nicht erklärt werden.");

        var verification = await VerifyStoredModelAsync(model, fullModelStore, token).ConfigureAwait(false);
        if (owner.Models.Length == 0)
        {
            var updated = owner with
            {
                Models = [new OwnedModel(ModelId, model.ExpectedOllamaDigest)]
            };
            await WriteOwnershipAtomicallyAsync(ownershipPath, updated, token).ConfigureAwait(false);
        }
        return verification;
    }

    private static async Task<AetherModelVerification> VerifyStoredModelAsync(
        AetherProductModel model, string modelStorePath, CancellationToken token)
    {
        var blobsPath = Path.Combine(modelStorePath, "blobs");
        var registryManifestPath = Path.Combine(modelStorePath, "manifests", "registry.ollama.ai",
            "library", "nemotron-3-nano", "4b");
        var manifestListPath = Path.Combine(modelStorePath, "manifests-v2", "ollama.com",
            "library", "nemotron-3-nano", "4b");
        EnsureNoReparsePoints(registryManifestPath);
        EnsureNoReparsePoints(manifestListPath);

        var registryManifestBytes = await ReadBoundedAsync(registryManifestPath, 65536, token).ConfigureAwait(false);
        using var registryManifest = JsonDocument.Parse(registryManifestBytes);
        var root = registryManifest.RootElement;
        if (ReadInt32(root, "schemaVersion") != 2 || ReadString(root, "runner") != "llamacpp" ||
            ReadString(root, "format") != "gguf")
            throw new InvalidDataException("Das gespeicherte Registry-Manifest identifiziert nicht das erwartete Ollama-GGUF-Modell.");

        var childManifestDigest = NormalizeDigest(model.ExpectedOllamaDigest!);
        var childManifestPath = Path.Combine(blobsPath, "sha256-" + childManifestDigest);
        var childManifestBytes = await ReadBoundedAsync(childManifestPath, 65536, token).ConfigureAwait(false);
        var actualManifestDigest = Hash(childManifestBytes);
        if (!actualManifestDigest.Equals(childManifestDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Der Nemotron-Manifestblob stimmt nicht mit dem katalogisierten Digest überein.");

        using var childManifest = JsonDocument.Parse(childManifestBytes);
        var childRoot = childManifest.RootElement;
        if (ReadInt32(childRoot, "schemaVersion") != 2)
            throw new InvalidDataException("Das gespeicherte Nemotron-Manifest hat ein unbekanntes Schema.");

        await VerifyDescriptorAsync(childRoot.GetProperty("config"), "application/vnd.docker.container.image.v1+json",
            model.ExpectedConfigDigest!, model.ExpectedConfigSizeBytes!.Value, blobsPath, token).ConfigureAwait(false);
        var childLayers = childRoot.GetProperty("layers").EnumerateArray().ToArray();
        await VerifyExpectedLayersAsync(childLayers, model, blobsPath, token).ConfigureAwait(false);

        var wrapperConfig = root.GetProperty("config");
        EnsureSameDescriptor(wrapperConfig, childRoot.GetProperty("config"));
        var wrapperLayers = root.GetProperty("layers").EnumerateArray().ToArray();
        if (wrapperLayers.Length != model.ExpectedLayers.Length + 2)
            throw new InvalidDataException("Das Registry-Tag enthält unerwartete oder fehlende Layer.");
        var expectedLayerDigests = model.ExpectedLayers.Select(layer => layer.Digest)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenExpectedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonElement listDescriptor = default;
        JsonElement childDescriptor = default;
        foreach (var descriptor in wrapperLayers)
        {
            var mediaType = ReadString(descriptor, "mediaType");
            if (mediaType == ManifestListMediaType)
            {
                if (listDescriptor.ValueKind != JsonValueKind.Undefined)
                    throw new InvalidDataException("Das Registry-Tag enthält mehrere Manifestlisten.");
                listDescriptor = descriptor;
            }
            else if (mediaType == ModelManifestMediaType)
            {
                if (childDescriptor.ValueKind != JsonValueKind.Undefined)
                    throw new InvalidDataException("Das Registry-Tag enthält mehrere Modellmanifeste.");
                childDescriptor = descriptor;
            }
            else
            {
                var digest = ReadDigest(descriptor);
                var expected = model.ExpectedLayers.SingleOrDefault(layer =>
                    layer.MediaType == mediaType && layer.Digest.Equals(digest, StringComparison.OrdinalIgnoreCase));
                if (expected is null || !seenExpectedLayers.Add(digest))
                    throw new InvalidDataException("Das Registry-Tag enthält einen unbekannten, doppelten oder unerwarteten Modelllayer.");
                await VerifyDescriptorAsync(descriptor, expected.MediaType, expected.Digest,
                    expected.SizeBytes, blobsPath, token).ConfigureAwait(false);
            }
        }
        if (!seenExpectedLayers.SetEquals(expectedLayerDigests) ||
            listDescriptor.ValueKind == JsonValueKind.Undefined ||
            childDescriptor.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("Das Registry-Tag verweist nicht vollständig auf die erwarteten Nemotron-Layer.");

        var listDigest = ReadDigest(listDescriptor);
        var listSize = ReadInt64(listDescriptor, "size");
        var manifestListBytes = await ReadBoundedAsync(manifestListPath, 65536, token).ConfigureAwait(false);
        await VerifyDescriptorAsync(listDescriptor, ManifestListMediaType, listDigest, listSize,
            blobsPath, token).ConfigureAwait(false);
        var listBlob = await ReadBoundedAsync(Path.Combine(blobsPath, "sha256-" + listDigest), 65536, token)
            .ConfigureAwait(false);
        if (!manifestListBytes.AsSpan().SequenceEqual(listBlob))
            throw new InvalidDataException("Die Registry-Tagdatei stimmt nicht mit ihrem Manifestlisten-Blob überein.");
        using var manifestList = JsonDocument.Parse(manifestListBytes);
        if (ReadInt32(manifestList.RootElement, "schemaVersion") != 2 ||
            !manifestList.RootElement.GetProperty("manifests").EnumerateArray().Any(entry =>
                ReadString(entry, "digest") == "sha256:" + childManifestDigest &&
                ReadString(entry, "runner") == "llamacpp" && ReadString(entry, "format") == "gguf"))
            throw new InvalidDataException("Die offizielle Tagauflösung bindet den Nemotron-4B-Digest nicht an llamacpp/GGUF.");

        if (!ReadDigest(childDescriptor).Equals(childManifestDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Der Registry-Tag verweist nicht exakt auf den geprüften Nemotron-Manifestblob.");
        await VerifyDescriptorAsync(childDescriptor, ModelManifestMediaType, childManifestDigest,
            childManifestBytes.LongLength, blobsPath, token).ConfigureAwait(false);
        var storedChild = await ReadBoundedAsync(childManifestPath, 65536, token).ConfigureAwait(false);
        if (!storedChild.AsSpan().SequenceEqual(childManifestBytes))
            throw new InvalidDataException("Der Registry-Tag verweist nicht exakt auf den geprüften Nemotron-Manifestblob.");

        var expectedBlobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sha256-" + NormalizeDigest(model.ExpectedConfigDigest!)
        };
        foreach (var descriptor in wrapperLayers)
            expectedBlobs.Add("sha256-" + ReadDigest(descriptor));
        var actualBlobs = Directory.EnumerateFiles(blobsPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actualBlobs.SetEquals(expectedBlobs) ||
            Directory.EnumerateDirectories(blobsPath, "*", SearchOption.TopDirectoryOnly).Any())
            throw new InvalidDataException("Der dedizierte Nemotron-Acceptance-Store enthält unerklärte Modellblobs.");

        VerifyModelIdentity(model, modelStorePath);
        VerifyLicense(model, blobsPath);
        var verifiedLayers = ImmutableArray.CreateRange(model.ExpectedLayers.Select(layer => layer.Digest));
        return new(model.ExpectedOllamaDigest!, verifiedLayers);
    }

    private static async Task VerifyExpectedLayersAsync(
        JsonElement[] descriptors, AetherProductModel model, string blobsPath, CancellationToken token)
    {
        if (descriptors.Length != model.ExpectedLayers.Length)
            throw new InvalidDataException("Das Nemotron-Manifest enthält nicht exakt die katalogisierten Modelllayer.");
        var verified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in descriptors)
        {
            var mediaType = ReadString(descriptor, "mediaType");
            var digest = ReadDigest(descriptor);
            var expected = model.ExpectedLayers.SingleOrDefault(layer =>
                layer.MediaType == mediaType && layer.Digest.Equals(digest, StringComparison.OrdinalIgnoreCase));
            if (expected is null || !verified.Add(digest))
                throw new InvalidDataException("Ein Nemotron-Manifestlayer ist unbekannt oder doppelt.");
            await VerifyDescriptorAsync(descriptor, expected.MediaType, expected.Digest,
                expected.SizeBytes, blobsPath, token).ConfigureAwait(false);
        }
        if (model.ExpectedLayers.Any(layer => layer.MediaType == "application/vnd.ollama.image.license") == false ||
            !verified.SetEquals(model.ExpectedLayers.Select(layer => layer.Digest)))
            throw new InvalidDataException("Das Nemotron-Manifest enthält nicht alle katalogisierten Layer einschließlich Lizenz.");
    }

    private static async Task VerifyDescriptorAsync(
        JsonElement descriptor, string mediaType, string digest, long size, string blobsPath, CancellationToken token)
    {
        if (ReadString(descriptor, "mediaType") != mediaType ||
            !ReadDigest(descriptor).Equals(NormalizeDigest(digest), StringComparison.OrdinalIgnoreCase) ||
            ReadInt64(descriptor, "size") != size || size <= 0)
            throw new InvalidDataException("Ein Registry-Manifestlayer widerspricht dem versionierten AETHER-Modellvertrag.");
        var path = Path.Combine(blobsPath, "sha256-" + NormalizeDigest(digest));
        EnsureNoReparsePoints(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != size)
            throw new InvalidDataException($"Layergröße stimmt nicht: {digest}.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
        if (!actual.Equals(NormalizeDigest(digest), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Layergröße oder SHA-256 stimmt nicht: {digest}.");
    }

    private static void EnsureSameDescriptor(JsonElement left, JsonElement right)
    {
        if (ReadString(left, "mediaType") != ReadString(right, "mediaType") ||
            !ReadDigest(left).Equals(ReadDigest(right), StringComparison.OrdinalIgnoreCase) ||
            ReadInt64(left, "size") != ReadInt64(right, "size"))
            throw new InvalidDataException("Die Tag- und Modellmanifeste widersprechen sich beim Konfigurationslayer.");
    }

    private static void VerifyModelIdentity(AetherProductModel model, string modelStorePath)
    {
        var modelLayer = model.ExpectedLayers.Single(layer => layer.MediaType == "application/vnd.ollama.image.model");
        var metadataPath = Path.Combine(modelStorePath, "metadata", "sha256-" + modelLayer.Digest + ".json");
        EnsureNoReparsePoints(metadataPath);
        var info = new FileInfo(metadataPath);
        if (!info.Exists || info.Length is <= 0 or > 65536)
            throw new InvalidDataException("Die lokalen Ollama-Metadaten zur Modellidentität fehlen oder sind ungültig.");
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(metadataPath));
        var values = metadata.RootElement.GetProperty("kv");
        if (values.GetProperty("general.architecture").GetString() != "nemotron_h" ||
            values.GetProperty("general.parameter_count").GetInt64() != ExpectedParameterCount)
            throw new InvalidDataException("Die lokalen Modellmetadaten identifizieren nicht Nemotron 3 Nano 4B.");
    }

    private static void VerifyLicense(AetherProductModel model, string blobsPath)
    {
        var licenseLayer = model.ExpectedLayers.Single(layer =>
            layer.MediaType == "application/vnd.ollama.image.license");
        var path = Path.Combine(blobsPath, "sha256-" + licenseLayer.Digest);
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        if (!text.StartsWith("NVIDIA Open Model License Agreement", StringComparison.Ordinal) ||
            !text.Contains("NVIDIA Corporation", StringComparison.Ordinal))
            throw new InvalidDataException("Der gespeicherte Lizenzlayer ist kein NVIDIA Open Model License-Text.");
    }

    private static async Task<AcceptanceProfile> ReadProfileAsync(string path, CancellationToken token)
    {
        var bytes = await ReadBoundedAsync(path, 4096, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<AcceptanceProfile>(bytes) ??
            throw new InvalidDataException("Das AETHER-Acceptance-Profil ist leer oder ungültig.");
    }

    private static async Task<OwnershipDocument> ReadOwnershipAsync(string path, CancellationToken token)
    {
        var bytes = await ReadBoundedAsync(path, 65536, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<OwnershipDocument>(bytes, FabricJson.Options) ??
            throw new InvalidDataException("Der AETHER-Ownership-Nachweis ist leer oder ungültig.");
    }

    private static async Task WriteOwnershipAtomicallyAsync(
        string path, OwnershipDocument owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             16384, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, owner, FabricJson.Options, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, long maximumBytes, CancellationToken token)
    {
        EnsureNoReparsePoints(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > maximumBytes)
            throw new InvalidDataException($"Erwartete Storedatei fehlt oder überschreitet das Größenlimit: {path}.");
        return await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
    }

    private static string ReadString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
        property.GetString() is { } result
            ? result
            : throw new InvalidDataException($"Registry-Metadatum {name} fehlt oder ist ungültig.");

    private static long ReadInt64(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.TryGetInt64(out var result)
            ? result
            : throw new InvalidDataException($"Registry-Metadatum {name} fehlt oder ist ungültig.");

    private static int ReadInt32(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.TryGetInt32(out var result)
            ? result
            : throw new InvalidDataException($"Registry-Metadatum {name} fehlt oder ist ungültig.");

    private static string ReadDigest(JsonElement descriptor) =>
        NormalizeDigest(ReadString(descriptor, "digest").Replace("sha256:", "", StringComparison.Ordinal));

    private static string NormalizeDigest(string digest) =>
        digest.Length == 64 && digest.All(char.IsAsciiHexDigit)
            ? digest.ToLowerInvariant()
            : throw new InvalidDataException("Ein Modellmanifest enthält einen ungültigen SHA-256-Digest.");

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool PathEquals(string? left, string? right) => left is not null && right is not null &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void EnsureNoReparsePoints(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Der AETHER-Acceptance-Store darf keine umgeleiteten Pfade enthalten.");
        }
    }

    private sealed record AcceptanceProfile(
        [property: JsonPropertyName("schema")] int Schema,
        [property: JsonPropertyName("storeRoot")] string StoreRoot,
        [property: JsonPropertyName("ollamaHost")] string OllamaHost);

    private sealed record OwnershipDocument(
        [property: JsonPropertyName("schema")] int Schema,
        [property: JsonPropertyName("modelStorePath")] string ModelStorePath,
        [property: JsonPropertyName("models")] OwnedModel[] Models,
        [property: JsonPropertyName("previousModelsEnvironment")] string? PreviousModelsEnvironment = null,
        [property: JsonPropertyName("previousHostEnvironment")] string? PreviousHostEnvironment = null);

    private sealed record OwnedModel(
        [property: JsonPropertyName("modelId")] string ModelId,
        [property: JsonPropertyName("digest")] string Digest);
}

public sealed record AetherSetupGpu(string Name, long? DedicatedVramBytes, long? AvailableVramBytes, string? DriverVersion);

public sealed record AetherSetupHardware(
    Version? WindowsVersion,
    bool Is64BitOperatingSystem,
    string? CpuName,
    int? CpuCores,
    int? LogicalProcessors,
    long? InstalledRamBytes,
    long? AvailableRamBytes,
    ImmutableArray<AetherSetupGpu> Gpus,
    long? FreeModelStoreBytes);

public static class AetherSetupGpuMeasurements
{
    public static ImmutableArray<AetherSetupGpu> ApplyNvidiaSmi(
        ImmutableArray<AetherSetupGpu> gpus, string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (gpus.IsDefault || gpus.IsEmpty)
            throw new InvalidDataException("NVIDIA-SMI-Messwerte können keinem GPU-Inventar zugeordnet werden.");

        var rows = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rows.Length == 0)
            throw new InvalidDataException("NVIDIA-SMI hat keine GPU-Messwerte geliefert.");

        var updated = gpus.ToBuilder();
        var matched = new HashSet<int>();
        foreach (var row in rows)
        {
            var columns = row.Split(',');
            if (columns.Length != 4)
                throw new InvalidDataException("NVIDIA-SMI hat eine ungültige Messzeile geliefert.");
            var name = columns[0].Trim();
            var indices = Enumerable.Range(0, gpus.Length)
                .Where(index => string.Equals(gpus[index].Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (indices.Length != 1 ||
                !gpus[indices[0]].Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                !matched.Add(indices[0]) ||
                !long.TryParse(columns[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var totalMib) ||
                !long.TryParse(columns[2].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var freeMib) ||
                totalMib <= 0 || freeMib < 0 || freeMib > totalMib ||
                string.IsNullOrWhiteSpace(columns[3]))
                throw new InvalidDataException("NVIDIA-SMI-Messwerte lassen sich nicht eindeutig dem GPU-Inventar zuordnen.");
            updated[indices[0]] = gpus[indices[0]] with
            {
                DedicatedVramBytes = checked(totalMib * 1024 * 1024),
                AvailableVramBytes = checked(freeMib * 1024 * 1024),
                DriverVersion = columns[3].Trim()
            };
        }
        for (var index = 0; index < gpus.Length; index++)
        {
            if (gpus[index].Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) && !matched.Contains(index))
                throw new InvalidDataException("Für mindestens eine NVIDIA-GPU fehlen dedizierte VRAM-Messwerte.");
        }
        return updated.ToImmutable();
    }
}

public sealed record AetherOllamaRuntimeStatus(
    bool Installed,
    bool OfficialSignatureValid,
    bool ApiCompatible,
    string? Version,
    string? ExecutablePath,
    bool LoopbackOnly,
    bool ListenerAvailable,
    bool StoreConfigurationVerified,
    string? ModelStorePath,
    bool ModelStoreOwned,
    bool ModelPresent,
    bool ModelOwned = false,
    bool UserOllamaListenerPreserved = false);

public sealed record AetherModelVerification(string ManifestDigest, ImmutableArray<string> LayerDigests);

public interface IAetherLocalAiSetupOperations
{
    Task<AetherOllamaRuntimeStatus> InspectRuntimeAsync(AetherProductModel model, CancellationToken token);
    Task<AetherModelVerification> RecoverAcceptanceStoreOwnershipAsync(
        AetherProductModel model, bool licenseAccepted, CancellationToken token);
    Task InstallOfficialRuntimeAsync(IProgress<AetherLocalAiSetupProgress>? progress, CancellationToken token);
    Task ConfigureManagedStoreAsync(CancellationToken token);
    Task StartManagedServerAsync(CancellationToken token);
    Task StopManagedServerAsync();
    Task PullModelAsync(AetherProductModel model, IProgress<AetherLocalAiSetupProgress>? progress, CancellationToken token);
    Task<AetherModelVerification> VerifyModelAsync(AetherProductModel model, CancellationToken token);
    Task<bool> RunFixedHealthCheckAsync(AetherProductModel model, CancellationToken token);
    Task PersistQualifiedCatalogAsync(AetherProductModel model, CancellationToken token);
}

public sealed class AetherLocalAiStoreOwnershipRecoveryWorkflow(IAetherLocalAiSetupOperations operations)
{
    public Task<AetherModelVerification> RunAsync(AetherProductModelCatalogDocument catalog,
        AetherProductModel model, bool licenseAccepted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(model);
        AetherProductModelCatalog.Validate(catalog);
        var catalogModel = catalog.Models.SingleOrDefault(entry => entry.ModelId == model.ModelId);
        if (catalogModel != model || model.ModelId != "nemotron-3-nano:4b" ||
            model.QualificationStatus != AetherModelQualificationStatus.Pending ||
            model.ApprovedForAutomaticSelection)
            throw new InvalidOperationException("Ownership-Recovery ist ausschließlich für den exakten, weiterhin Pending geführten Nemotron-4B-Katalogeintrag zulässig.");
        if (!licenseAccepted || !model.LicenseAcceptanceRequired)
            throw new UnauthorizedAccessException("Die NVIDIA-Modelllizenz muss ausdrücklich angenommen sein.");
        token.ThrowIfCancellationRequested();
        return operations.RecoverAcceptanceStoreOwnershipAsync(model, licenseAccepted, token);
    }
}

public sealed class AetherLocalAiSetupWorkflow(IAetherLocalAiSetupOperations operations)
{
    public async Task RunAsync(AetherProductModelCatalogDocument catalog, AetherProductModel model,
        AetherSetupHardware hardware, bool licenseAccepted, bool allowManagedStoreConfiguration,
        IProgress<AetherLocalAiSetupProgress>? progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(model);
        AetherProductModelCatalog.Validate(catalog);
        if (model.LicenseAcceptanceRequired && !licenseAccepted)
            throw new UnauthorizedAccessException("Die Modelllizenz wurde nicht angenommen.");
        if (model.QualificationStatus != AetherModelQualificationStatus.Qualified ||
            !model.ApprovedForAutomaticSelection || model.ExpectedOllamaDigest is null ||
            model.MinimumRamBytes is null || model.MinimumAvailableRamBytes is null ||
            model.MinimumVramBytes is null || model.MinimumStorageBytes is null)
            throw new InvalidOperationException("Das Modell ist nicht vollständig qualifiziert und kann nicht installiert oder freigegeben werden.");
        if (!catalog.Models.Any(entry => entry.ModelId == model.ModelId && entry == model))
            throw new InvalidDataException("Das Modell gehört nicht exakt zum übergebenen Produktkatalog.");

        progress?.Report(new(AetherLocalAiSetupPhase.CheckingSystem));
        var capability = AetherSetupCapabilityGate.Evaluate(catalog, hardware);
        if (!capability.CanInstall || capability.ModelId != model.ModelId)
            throw new InvalidOperationException("Die gemessenen Systemressourcen erfüllen den freigegebenen Modellvertrag nicht.");
        token.ThrowIfCancellationRequested();

        progress?.Report(new(AetherLocalAiSetupPhase.PreparingLocalAi));
        var runtime = await operations.InspectRuntimeAsync(model, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!runtime.Installed)
        {
            if (!allowManagedStoreConfiguration)
                throw new UnauthorizedAccessException("Die Einrichtung des AETHER-Modellstores wurde nicht bestätigt.");
            progress?.Report(new(AetherLocalAiSetupPhase.ConfiguringOllama));
            await operations.ConfigureManagedStoreAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await operations.InstallOfficialRuntimeAsync(progress, token).ConfigureAwait(false);
            runtime = await operations.InspectRuntimeAsync(model, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        if (!runtime.Installed || !runtime.OfficialSignatureValid || string.IsNullOrWhiteSpace(runtime.Version) ||
            string.IsNullOrWhiteSpace(runtime.ExecutablePath))
            throw new InvalidDataException("Die offizielle Ollama-Installation, Version oder Installationsquelle konnte nicht bestätigt werden.");
        if (runtime.ListenerAvailable && !runtime.LoopbackOnly)
            throw new InvalidOperationException($"Ollama muss ausschließlich an {AetherLocalAiStoreContract.Host} lauschen.");
        if (runtime.ModelPresent && !runtime.ModelOwned)
            throw new InvalidOperationException("Ein vorhandenes Fremdmodell wird nicht in den AETHER-Katalog übernommen.");
        var correctStore = string.Equals(runtime.ModelStorePath, AetherLocalAiStoreContract.ModelStorePath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        if (!correctStore || !runtime.ModelStoreOwned || !runtime.StoreConfigurationVerified)
        {
            if (!allowManagedStoreConfiguration)
                throw new UnauthorizedAccessException("Die Konfiguration des verwalteten AETHER-Modellstores wurde nicht bestätigt.");
            if (runtime.ListenerAvailable)
                throw new InvalidOperationException("Ollama verwendet noch den bisherigen Prozesszustand. Bitte Ollama beenden und die Einrichtung erneut starten.");
            progress?.Report(new(AetherLocalAiSetupPhase.ConfiguringOllama));
            await operations.ConfigureManagedStoreAsync(token).ConfigureAwait(false);
            runtime = await operations.InspectRuntimeAsync(model, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!runtime.Installed || !runtime.OfficialSignatureValid ||
                !runtime.StoreConfigurationVerified ||
                !string.Equals(runtime.ModelStorePath, AetherLocalAiStoreContract.ModelStorePath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Ollama ist nach der Konfiguration nicht für den geprüften AETHER-Modellstore eingerichtet.");
            if (runtime.ModelPresent && !runtime.ModelOwned)
                throw new InvalidDataException("Ein Modell im AETHER-Modellstore besitzt keinen AETHER-Herkunftsnachweis und wird nicht übernommen.");
        }
        var managedServerStarted = false;
        try
        {
            if (!runtime.ListenerAvailable)
            {
                managedServerStarted = true;
                await operations.StartManagedServerAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                runtime = await operations.InspectRuntimeAsync(model, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!runtime.ListenerAvailable || !runtime.LoopbackOnly || !runtime.StoreConfigurationVerified ||
                    !runtime.ApiCompatible)
                    throw new InvalidDataException("Der verwaltete Ollama-Server konnte nicht auf dem geprüften Loopback-Store gestartet werden.");
            }
            if (!runtime.ApiCompatible || !runtime.LoopbackOnly || !runtime.StoreConfigurationVerified)
                throw new InvalidDataException("Ollama-Version, Loopback-Listener oder effektiver AETHER-Modellstore sind nicht kompatibel.");
            token.ThrowIfCancellationRequested();

            if (!runtime.ModelPresent)
            {
                progress?.Report(new(AetherLocalAiSetupPhase.DownloadingModel));
                await operations.PullModelAsync(model, progress, token).ConfigureAwait(false);
            }
            progress?.Report(new(AetherLocalAiSetupPhase.VerifyingModel));
            var verification = await operations.VerifyModelAsync(model, token).ConfigureAwait(false);
            if (!string.Equals(verification.ManifestDigest, model.ExpectedOllamaDigest, StringComparison.OrdinalIgnoreCase) ||
                !verification.LayerDigests.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(model.ExpectedLayers.Select(layer => layer.Digest)))
                throw new InvalidDataException("Ollama-Manifest oder Modelllayer stimmen nicht mit dem versionierten AETHER-Katalog überein.");
            token.ThrowIfCancellationRequested();
            if (!await operations.RunFixedHealthCheckAsync(model, token).ConfigureAwait(false))
                throw new InvalidDataException("Der festgelegte lokale Modell-Healthcheck ist fehlgeschlagen.");
            token.ThrowIfCancellationRequested();
            progress?.Report(new(AetherLocalAiSetupPhase.ConfiguringLocalAi));
            await operations.PersistQualifiedCatalogAsync(model, token).ConfigureAwait(false);
            progress?.Report(new(AetherLocalAiSetupPhase.Completing));
        }
        catch (Exception setupError)
        {
            if (managedServerStarted)
            {
                try
                {
                    await operations.StopManagedServerAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException(
                        "Die Einrichtung ist fehlgeschlagen und der verwaltete Ollama-Server konnte nicht beendet werden.",
                        setupError, cleanupError);
                }
            }
            throw;
        }
        if (managedServerStarted)
            await operations.StopManagedServerAsync().ConfigureAwait(false);
    }
}

public sealed record AetherLocalAiCapabilityResult(
    AetherLocalAiCapability Capability,
    string? ModelId,
    ImmutableArray<string> Reasons)
{
    public string Label => Capability switch
    {
        AetherLocalAiCapability.LocalAiUnavailable => "Lokale KI ist auf diesem System nicht verfügbar.",
        AetherLocalAiCapability.LocalAiLimited => "Lokal eingeschränkt",
        AetherLocalAiCapability.LocalAiReady => "Lokale KI bereit",
        AetherLocalAiCapability.AdvancedLocalAiPossible => "Erweiterte lokale KI möglich",
        _ => throw new InvalidOperationException("Unbekannter lokaler KI-Fähigkeitsstatus.")
    };

    public bool CanInstall => Capability is AetherLocalAiCapability.LocalAiReady or AetherLocalAiCapability.AdvancedLocalAiPossible;
}

public static class AetherProductModelCatalog
{
    public const int CurrentSchema = 1;
    public const int CurrentVersion = 3;
    private static readonly Uri PhiLicense = new("https://huggingface.co/microsoft/Phi-4-mini-instruct/blob/main/LICENSE");
    private static readonly Uri PhiManifest = new("https://registry.ollama.ai/v2/library/phi4-mini/manifests/3.8b");
    private static readonly Uri PhiQualification = new("https://github.com/oa7779/AetherCockpit/blob/rc/grosse-entfesselung/docs/INTELLIGENCE-MODEL-INSTALLATION.md");
    private static readonly Uri NemotronLicense = new("https://www.nvidia.com/en-us/agreements/enterprise-software/nvidia-open-model-license/");
    private static readonly Uri NemotronManifest = new("https://registry.ollama.ai/v2/library/nemotron-3-nano/manifests/4b");
    private const long Mib = 1024L * 1024;
    private const long Gib = 1024L * 1024 * 1024;
    private const long PhiModelRamBytes = 600 * Mib;
    private const long PhiModelVramBytes = 3171 * Mib;
    private const long PhiPayloadBytes = 2491876774;
    private const long ReferenceSystemRamBytes = 68719476736;

    public static AetherProductModelCatalogDocument Current { get; } = new(CurrentSchema, CurrentVersion,
    [
        new(1, "phi4-mini:3.8b", "Phi-4 Mini 3.8B", "Microsoft", "3.8B", "3.8b", "Ollama",
            FabricProviderKind.Ollama, "Q4_K_M", "MIT", PhiLicense, true, PhiManifest,
            "78fad5d182a7c33065e153a5f8ba210754207ba9d91973f57dffa7f487363753",
            "8c2539a423c4df2d9d2609fba56e7bad554696b66ed0e280c56e0ef4a32498fa",
            411,
            [
                new("application/vnd.ollama.image.model", "3c168af1dea0a414299c7d9077e100ac763370e5a98b3c53801a958a47f0a5db", 2491874624),
                new("application/vnd.ollama.image.template", "813f53fdc6e58d35bb1c3853c93266380e9ca918a993e8eab193e8ede5d3a603", 655),
                new("application/vnd.ollama.image.license", "fa8235e5b48faca34e3ca98cf4f694ef08bd216d28b58071a1f85b1d50cb814d", 1084)
            ],
            PhiPayloadBytes, PhiPayloadBytes, 17 * Gib, (8 * Gib) + PhiModelRamBytes,
            PhiModelVramBytes + (2 * Gib), 3 * PhiPayloadBytes,
            ["Lokaler Assistent", "Zusammenfassung", "Beleggestützte Analyse"],
            8192, AetherModelQualificationStatus.Qualified, new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero),
            PhiQualification, true)
        {
            SupportedLanguages = ["de", "en"],
            ModelFamily = "Phi-4",
            ParameterCountBillions = 3.8m,
            RequiredGpuVendor = "NVIDIA",
            AutomaticSelectionPriority = 50,
            FallbackModelId = "aether.deterministic",
            MinimumWindowsVersion = new Version(10, 0, 19045),
            MinimumOllamaVersion = "0.5.13",
            RecommendedRamBytes = ReferenceSystemRamBytes,
            RequiredModelRamBytes = PhiModelRamBytes,
            RequiredModelVramBytes = PhiModelVramBytes,
            RamSafetyReserveBytes = 8 * Gib,
            VramSafetyReserveBytes = 2 * Gib,
            StorageSafetyReserveBytes = 2 * PhiPayloadBytes,
            MaximumParallelRequests = 1,
            DigestVerificationPassed = true,
            GermanTestPassed = true,
            StructuredOutputTestPassed = true,
            ToolContractTestPassed = true,
            OfflineTestPassed = true,
            ResourceMeasurementPassed = true
        },
        new(1, "nemotron-3-nano:4b", "Nemotron 3 Nano 4B", "NVIDIA", "3.97B", "4b", "Ollama",
            FabricProviderKind.Ollama, "Q4_K_M", "NVIDIA Open Model License", NemotronLicense, true, NemotronManifest,
            "d94b6dfae26d340fd16599d8cd5301945bb42b6bd7a7c70f2078559562f60cbe",
            "1c309ae5e93ede03f48f4bdbfd1b50d1780f2c3bcdc82f4569e20f0c33878db9",
            499,
            [
                new("application/vnd.ollama.image.model", "527db2cf6c705d8fabb95693d038d9c06b4a2b0b8b0a4bbdbd01212d37242970", 2837586496),
                new("application/vnd.ollama.image.license", "355e036064fa9b3a96ce0cdbb69abe54f5c7ce0b6aa2c7d5f8ec8580b011c20e", 10124),
                new("application/vnd.ollama.image.params", "12e88b2a8727339b5a4a8b3e2d0d637ac1c61085b1072e77865f0c25d6e468eb", 28)
            ],
            2837597147, 2837597147, null, null, null, null,
            [], null, AetherModelQualificationStatus.Rejected, null, null, false)
        {
            SupportedLanguages = ["de", "en", "es", "fr", "it", "ja"],
            ModelFamily = "Nemotron 3 Nano",
            ParameterCountBillions = 3.97m,
            RequiredGpuVendor = "NVIDIA",
            AutomaticSelectionPriority = 100,
            FallbackModelId = "phi4-mini:3.8b"
        },
        new(1, "nemotron-3-nano:30b", "Nemotron 3 Nano 30B", "NVIDIA", "30B", null, null, null,
            null, "NVIDIA Nemotron Open Model License", NemotronLicense, true, null, null, null, null,
            [], null, null, null, null, null, null, [], null, AetherModelQualificationStatus.Pending, null, null, false)
    ]);

    static AetherProductModelCatalog() => Validate(Current);

    public static void Validate(AetherProductModelCatalogDocument catalog)
    {
        if (catalog.Schema != CurrentSchema || catalog.Version != CurrentVersion ||
            catalog.Models.IsDefaultOrEmpty || catalog.Models.Length > 16 ||
            catalog.Models.Any(model => model is null || model.CatalogSchema != catalog.Schema ||
                string.IsNullOrWhiteSpace(model.ModelId) || model.ModelId.Length > 256 ||
                !model.ModelId.All(character => char.IsAsciiLetterOrDigit(character) || "._:/-".Contains(character)) ||
                string.IsNullOrWhiteSpace(model.DisplayName) || model.DisplayName.Length > 160 ||
                model.ExpectedLayers.IsDefault || model.SupportedRoles.IsDefault ||
                model.SupportedLanguages.IsDefault ||
                model.ParameterCountBillions is <= 0 ||
                model.PreferredRamBytes is <= 0 ||
                model.AutomaticSelectionPriority is < 0 or > 1000 ||
                model.FallbackModelId is { } fallbackId &&
                    (fallbackId.Length > 256 || !fallbackId.All(character => char.IsAsciiLetterOrDigit(character) || "._:/-".Contains(character))) ||
                model.RequiredGpuVendor is { } gpuVendor &&
                    (gpuVendor.Length > 64 || string.IsNullOrWhiteSpace(gpuVendor) ||
                     gpuVendor.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not (' ' or '-' or '_'))) ||
                model.ExpectedLayers.Any(layer => layer is null || !IsDigest(layer.Digest) || layer.SizeBytes <= 0 ||
                    string.IsNullOrWhiteSpace(layer.MediaType)) ||
                model.DownloadSizeBytes is <= 0 || model.InstallationSizeBytes is <= 0 ||
                model.MinimumRamBytes is <= 0 || model.MinimumAvailableRamBytes is <= 0 ||
                model.MinimumVramBytes is < 0 || model.MinimumStorageBytes is <= 0 ||
                model.RecommendedRamBytes is <= 0 || model.RequiredModelRamBytes is <= 0 ||
                model.RequiredModelVramBytes is < 0 || model.RamSafetyReserveBytes is < 0 ||
                model.VramSafetyReserveBytes is < 0 || model.StorageSafetyReserveBytes is < 0 ||
                model.MaximumParallelRequests is < 1 or > 4 ||
                model.MinimumOllamaVersion is not null && !Version.TryParse(model.MinimumOllamaVersion, out _) ||
                model.MinimumWindowsVersion is not null &&
                    (model.MinimumWindowsVersion.Major != 10 || model.MinimumWindowsVersion.Minor != 0 ||
                     model.MinimumWindowsVersion.Build < 19045) ||
                model.ContextLimitTokens is <= 0 ||
                model.ExpectedOllamaDigest is not null && !IsDigest(model.ExpectedOllamaDigest) ||
                model.ExpectedConfigDigest is not null && !IsDigest(model.ExpectedConfigDigest) ||
                (model.ExpectedConfigDigest is null) != (model.ExpectedConfigSizeBytes is null) ||
                model.ExpectedConfigSizeBytes is <= 0 ||
                model.QualificationStatus == AetherModelQualificationStatus.Qualified &&
                    (model.QualifiedAt is null || model.QualificationEvidence is null ||
                     string.IsNullOrWhiteSpace(model.Revision) || string.IsNullOrWhiteSpace(model.Publisher) ||
                     model.MinimumRamBytes is null || model.MinimumAvailableRamBytes is null || model.MinimumVramBytes is null ||
                     model.MinimumStorageBytes is null || model.DownloadSizeBytes is null || model.InstallationSizeBytes is null ||
                     model.MinimumWindowsVersion is null || model.MinimumOllamaVersion is null ||
                     model.RecommendedRamBytes is null || model.RequiredModelRamBytes is null ||
                     model.RequiredModelVramBytes is null || model.RamSafetyReserveBytes is null ||
                     model.VramSafetyReserveBytes is null || model.StorageSafetyReserveBytes is null ||
                     model.MaximumParallelRequests is null ||
                     string.IsNullOrWhiteSpace(model.ModelFamily) || model.ParameterCountBillions is null ||
                     model.AutomaticSelectionPriority <= 0 || string.IsNullOrWhiteSpace(model.FallbackModelId) ||
                     model.ExpectedOllamaDigest is null || model.ExpectedLayers.IsEmpty ||
                     model.ExpectedConfigDigest is null || model.ExpectedConfigSizeBytes is null ||
                     model.DownloadSource is null || model.Runtime is null || model.Provider is null ||
                     model.Quantization is null || model.LicenseName is null || model.LicenseTextUrl is null ||
                     model.SupportedRoles.IsEmpty || model.ContextLimitTokens is null ||
                     !model.SupportedLanguages.Contains("de", StringComparer.Ordinal) ||
                     model.DigestVerificationPassed != true || model.GermanTestPassed != true ||
                     model.StructuredOutputTestPassed != true || model.ToolContractTestPassed != true ||
                     model.OfflineTestPassed != true || model.ResourceMeasurementPassed != true) ||
                model.QualificationStatus == AetherModelQualificationStatus.Qualified &&
                    (!Meets(model.MinimumAvailableRamBytes, model.RequiredModelRamBytes, model.RamSafetyReserveBytes) ||
                     !Meets(model.MinimumVramBytes, model.RequiredModelVramBytes, model.VramSafetyReserveBytes) ||
                     !Meets(model.MinimumStorageBytes, model.InstallationSizeBytes, model.StorageSafetyReserveBytes) ||
                    model.MinimumRamBytes < model.MinimumAvailableRamBytes ||
                     model.RecommendedRamBytes < model.MinimumRamBytes ||
                     model.MaximumParallelRequests != 1) ||
                model.ApprovedForAutomaticSelection &&
                    (model.QualificationStatus != AetherModelQualificationStatus.Qualified ||
                     !model.LicenseAcceptanceRequired || model.LicenseName is null || model.LicenseTextUrl is null ||
                     model.Runtime is null || model.Provider is null)))
            throw new InvalidDataException("AETHER-Modellkatalog ist unvollständig, ungültig oder inkonsistent.");
        if (catalog.Models.Select(model => model.ModelId).Distinct(StringComparer.Ordinal).Count() != catalog.Models.Length)
            throw new InvalidDataException("AETHER-Modellkatalog enthält doppelte Modell-IDs.");
        foreach (var model in catalog.Models)
        {
            if (model.LicenseTextUrl is { } license && !IsSafeHttps(license) ||
                model.DownloadSource is { } source && !IsSafeHttps(source) ||
                model.QualificationEvidence is { } evidence && !IsSafeHttps(evidence))
                throw new InvalidDataException("AETHER-Modellkatalog enthält eine unsichere Quelle.");
            if (model.DownloadSizeBytes is { } downloadBytes &&
                model.ExpectedLayers.Sum(layer => layer.SizeBytes) + (model.ExpectedConfigSizeBytes ?? 0) != downloadBytes)
                throw new InvalidDataException("AETHER-Modellkatalog-Payloadgröße stimmt nicht mit den bestätigten Schichten überein.");
            if (model.QualificationStatus != AetherModelQualificationStatus.Qualified &&
                model.ApprovedForAutomaticSelection)
                throw new InvalidDataException("Nicht qualifizierte Modelle dürfen nicht automatisch ausgewählt werden.");
        }
    }

    private static bool IsSafeHttps(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    private static bool Meets(long? minimum, long? baseline, long? reserve) =>
        minimum is >= 0 && baseline is >= 0 && reserve is >= 0 &&
        baseline.Value <= long.MaxValue - reserve.Value &&
        minimum.Value >= baseline.Value + reserve.Value;

    private static bool IsDigest(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);
}

public static class AetherSetupCapabilityGate
{
    private static readonly Version MinimumWindowsVersion = new(10, 0, 19045);

    public static AetherLocalAiCapabilityResult Evaluate(AetherProductModelCatalogDocument catalog, AetherSetupHardware hardware)
    {
        AetherProductModelCatalog.Validate(catalog);
        ArgumentNullException.ThrowIfNull(hardware);
        if (!hardware.Is64BitOperatingSystem)
            return Result(AetherLocalAiCapability.LocalAiUnavailable, "Das Betriebssystem ist nicht x64.");
        if (hardware.WindowsVersion is null)
            return Result(AetherLocalAiCapability.LocalAiLimited, "Die Windows-Version konnte nicht bestätigt werden.");
        if (hardware.WindowsVersion < MinimumWindowsVersion)
            return Result(AetherLocalAiCapability.LocalAiUnavailable, "Windows 10 22H2 oder neuer wird benötigt.");
        if (string.IsNullOrWhiteSpace(hardware.CpuName) || hardware.LogicalProcessors is null or <= 0)
            return Result(AetherLocalAiCapability.LocalAiLimited, "CPU-Modell oder logische Prozessoren sind nicht verfügbar.");
        if (hardware.InstalledRamBytes is null or <= 0 || hardware.AvailableRamBytes is null or < 0)
            return Result(AetherLocalAiCapability.LocalAiLimited, "Installierter oder verfügbarer Arbeitsspeicher ist nicht vollständig messbar.");
        if (hardware.Gpus.IsDefault || hardware.Gpus.Any(gpu => string.IsNullOrWhiteSpace(gpu.Name)))
            return Result(AetherLocalAiCapability.LocalAiLimited, "GPU-Inventar ist nicht vollständig verfügbar.");
        if (hardware.FreeModelStoreBytes is null or < 0)
            return Result(AetherLocalAiCapability.LocalAiLimited, "Verfügbarer Speicherplatz des AETHER-Modellstores ist nicht messbar.");

        var qualified = catalog.Models.Where(model => model.QualificationStatus == AetherModelQualificationStatus.Qualified &&
                model.ApprovedForAutomaticSelection)
            .OrderByDescending(model => model.AutomaticSelectionPriority)
            .ThenBy(model => model.ModelId, StringComparer.Ordinal).ToArray();
        if (qualified.Length == 0)
            return Result(AetherLocalAiCapability.LocalAiLimited, "Kein Modell besitzt einen vollständigen, freigegebenen Qualifikationsnachweis.");

        var missingRequirements = new List<string>();
        var failedRequirements = new List<string>();
        foreach (var model in qualified)
        {
            if (model.MinimumWindowsVersion is { } modelWindows && hardware.WindowsVersion < modelWindows)
            {
                failedRequirements.Add($"{model.DisplayName}: Windows-Version unter dem Katalogminimum.");
                continue;
            }
            if (model.MinimumRamBytes is null || model.MinimumVramBytes is null || model.MinimumStorageBytes is null)
            {
                missingRequirements.Add($"{model.DisplayName}: Mindestressourcen fehlen.");
                continue;
            }
            if (model.MinimumAvailableRamBytes is null)
            {
                missingRequirements.Add($"{model.DisplayName}: Mindestwert für verfügbaren Arbeitsspeicher fehlt.");
                continue;
            }
            if (hardware.InstalledRamBytes < model.MinimumRamBytes || hardware.AvailableRamBytes < model.MinimumAvailableRamBytes)
            {
                failedRequirements.Add($"{model.DisplayName}: Arbeitsspeicher unter dem Katalogminimum.");
                continue;
            }
            if (hardware.FreeModelStoreBytes < model.MinimumStorageBytes)
            {
                failedRequirements.Add($"{model.DisplayName}: freier Modellstore-Speicher unter dem Katalogminimum.");
                continue;
            }
            if (model.MinimumVramBytes > 0)
            {
                var compatibleGpus = model.RequiredGpuVendor is { } requiredVendor
                    ? hardware.Gpus.Where(gpu => gpu.Name.Contains(requiredVendor, StringComparison.OrdinalIgnoreCase))
                    : hardware.Gpus.AsEnumerable();
                if (!compatibleGpus.Any())
                {
                    failedRequirements.Add($"{model.DisplayName}: erforderlicher GPU-Hersteller {model.RequiredGpuVendor} ist nicht vorhanden.");
                    continue;
                }
                var matchingGpu = compatibleGpus.FirstOrDefault(gpu => gpu.DedicatedVramBytes is not null &&
                    gpu.AvailableVramBytes is not null && !string.IsNullOrWhiteSpace(gpu.DriverVersion));
                if (matchingGpu is null)
                {
                    missingRequirements.Add($"{model.DisplayName}: dedizierter VRAM oder Treiber ist nicht verfügbar.");
                    continue;
                }
                if (matchingGpu.DedicatedVramBytes < model.MinimumVramBytes ||
                    matchingGpu.AvailableVramBytes < model.MinimumVramBytes)
                {
                    failedRequirements.Add($"{model.DisplayName}: dedizierter VRAM unter dem Katalogminimum.");
                    continue;
                }
            }
            return ResultForModel(model.Advanced ? AetherLocalAiCapability.AdvancedLocalAiPossible :
                AetherLocalAiCapability.LocalAiReady, model.ModelId);
        }
        if (missingRequirements.Count > 0)
            return ResultMany(AetherLocalAiCapability.LocalAiLimited, missingRequirements);
        return ResultMany(AetherLocalAiCapability.LocalAiUnavailable, failedRequirements);

        static AetherLocalAiCapabilityResult Result(AetherLocalAiCapability capability, string reason) =>
            new(capability, null, [reason]);
        static AetherLocalAiCapabilityResult ResultMany(AetherLocalAiCapability capability, IEnumerable<string> details) =>
            new(capability, null, [.. details]);
        static AetherLocalAiCapabilityResult ResultForModel(AetherLocalAiCapability capability, string modelId) =>
            new(capability, modelId, []);
    }
}
