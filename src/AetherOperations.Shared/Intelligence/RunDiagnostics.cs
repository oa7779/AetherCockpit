using System.Text.Json.Serialization;
using System.Text.Json;

namespace AetherOperations.Intelligence;

public sealed class FabricRunDiagnostics
{
    private readonly object _gate = new();
    [JsonIgnore] public Action<FabricRunDiagnostics>? Changed { get; set; }
    public Guid RunId { get; init; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? BuildVersion { get; set; }
    public string? ExecutablePath { get; set; }
    public int ProcessId { get; set; }
    public string? ProcessUserSid { get; set; }
    public string? LocalAppDataPath { get; set; }
    public string? PrimaryDiagnosticPath { get; set; }
    public string? DiagnosticPath { get; set; }
    public string? DiagnosticWriteError { get; set; }
    public string? Stage { get; set; }
    public bool? Elevated { get; set; }
    public bool? AcceptanceMode { get; set; }
    public string? CatalogSource { get; set; }
    public bool ProfileFound { get; set; }
    public string? ProfilePath { get; set; }
    public bool CatalogFound { get; set; }
    public string? CatalogPath { get; set; }
    public string? StoreRoot { get; set; }
    public string? ModelStore { get; set; }
    public int? CatalogSchema { get; set; }
    public int? CatalogVersion { get; set; }
    public string? CatalogModelId { get; set; }
    public string? PromptVersion { get; set; }
    public string? AnswerType { get; set; }
    public string? QualificationStatus { get; set; }
    public bool? ApprovedForAutomaticSelection { get; set; }
    public DateTimeOffset? HardwareSnapshotAt { get; set; }
    public long? TotalRamBytes { get; set; }
    public long? AvailableRamBytes { get; set; }
    public long? TotalVramBytes { get; set; }
    public long? AvailableVramBytes { get; set; }
    public long? FreeModelStorageBytes { get; set; }
    public string? Capability { get; set; }
    public bool? AvailableRamAccepted { get; set; }
    public bool? AvailableVramAccepted { get; set; }
    public string? SelectedModelId { get; set; }
    public string? IntendedProvider { get; set; }
    public string? OllamaEndpoint { get; set; }
    public bool? EndpointReachable { get; set; }
    public string? RuntimeExecutablePath { get; set; }
    public bool? RuntimeStartedByAether { get; set; }
    public bool? RuntimeStoreVerified { get; set; }
    public string? RuntimeModelStorePath { get; set; }
    public string? RuntimeHost { get; set; }
    public DateTimeOffset? RuntimeStartedAt { get; set; }
    public int? RuntimeProcessId { get; set; }
    public string? ApiVersion { get; set; }
    public bool? ModelFound { get; set; }
    public string? ActualDigest { get; set; }
    public string? ExpectedDigest { get; set; }
    public bool? Resident { get; set; }
    public long? ResidentModelBytes { get; set; }
    public long? ResidentModelVramBytes { get; set; }
    public bool EnsureReadyStarted { get; set; }
    public bool? EnsureReadySucceeded { get; set; }
    public bool LoadStarted { get; set; }
    public bool? LoadSucceeded { get; set; }
    public DateTimeOffset? ModelLoadStartedAt { get; set; }
    public DateTimeOffset? ModelLoadCompletedAt { get; set; }
    public double? ModelLoadDurationMilliseconds { get; set; }
    public string? RequestedModelRole { get; set; }
    public string? InferenceTask { get; set; }
    public bool InferenceStarted { get; set; }
    public bool? InferenceSucceeded { get; set; }
    public bool? InferenceRetriedAfterTransientError { get; set; }
    public DateTimeOffset? InferenceStartedAt { get; set; }
    public DateTimeOffset? InferenceCompletedAt { get; set; }
    public double? AssistantElapsedMilliseconds { get; set; }
    public long? ProcessWorkingSetBeforeBytes { get; set; }
    public long? ProcessWorkingSetAfterBytes { get; set; }
    public bool ValidatorStarted { get; set; }
    public bool? ValidatorSucceeded { get; set; }
    public string[]? AcceptedClaimTypes { get; set; }
    public string[]? AcceptedClaimCategories { get; set; }
    public int? AcceptedStorageAvailabilityClaims { get; set; }
    public int? AcceptedStorageMeasurementClaims { get; set; }
    public bool RepairRetryStarted { get; set; }
    public bool? ClaimEvidenceRebound { get; set; }
    public bool? ClaimShapeCanonicalized { get; set; }
    public bool? CanonicalIdentityFindingAdded { get; set; }
    public bool? RejectedFindingRemoved { get; set; }
    public string? InitialValidationReason { get; set; }
    public FabricRejectedClaimDiagnostic? InitialRejectedClaim { get; set; }
    public FabricRejectedClaimDiagnostic? RepairRejectedClaim { get; set; }
    public int? MaximumOutputTokens { get; set; }
    public int? InitialInputTokens { get; set; }
    public int? InitialOutputTokens { get; set; }
    public string? InitialDoneReason { get; set; }
    public FabricResponseShape? InitialResponseShape { get; set; }
    public double? InitialGenerationSeconds { get; set; }
    public double? InitialLoadSeconds { get; set; }
    public double? InitialFirstTokenSeconds { get; set; }
    public int? RepairInputTokens { get; set; }
    public int? RepairOutputTokens { get; set; }
    public string? RepairDoneReason { get; set; }
    public FabricResponseShape? RepairResponseShape { get; set; }
    public double? RepairGenerationSeconds { get; set; }
    public double? RepairLoadSeconds { get; set; }
    public double? RepairFirstTokenSeconds { get; set; }
    public string? FinalProvider { get; set; }
    public bool? RealModel { get; set; }
    public string? FallbackReason { get; set; }
    public string? FallbackDetail { get; set; }
    public string? ExceptionType { get; set; }
    public string? ExceptionDetail { get; set; }

    public void Update(Action<FabricRunDiagnostics> update)
    {
        lock (_gate)
        {
            update(this);
            Changed?.Invoke(this);
        }

    }
}

public sealed record FabricResponseShape(int Characters, string? FirstCharacter, string? LastCharacter,
    bool CompleteJson, bool ObjectRoot, string[] KnownRootProperties, int UnknownRootPropertyCount)
{
    public static FabricResponseShape Inspect(string text)
    {
        var trimmed = text.AsSpan().Trim();
        // Only JSON punctuation is retained, never response text or arbitrary property names.
        static string? Boundary(ReadOnlySpan<char> value, bool first) =>
            value.IsEmpty ? null : "{}[]\"".Contains(first ? value[0] : value[^1])
                ? (first ? value[0] : value[^1]).ToString() : "other";
        var start = Boundary(trimmed, true);
        var end = Boundary(trimmed, false);
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            var isObject = document.RootElement.ValueKind == JsonValueKind.Object;
            var properties = isObject ? document.RootElement.EnumerateObject().Select(p => p.Name).ToArray() : [];
            var allowed = new[] { "summary", "findings", "recommendations", "schema", "language",
                "systemState", "diagnosis", "missing" };
            return new(text.Length, start, end, true, isObject,
                properties.Where(p => allowed.Contains(p, StringComparer.Ordinal)).ToArray(),
                properties.Count(p => !allowed.Contains(p, StringComparer.Ordinal)));
        }
        catch (JsonException)
        {
            return new(text.Length, start, end, false, false, [], 0);
        }
    }
}
