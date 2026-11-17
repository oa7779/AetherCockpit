using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AetherOperations.Platform;

namespace AetherOperations.Intelligence;

public enum FabricTrust { BuiltIn, OperatorApproved, Unqualified }
public enum FabricProof { Observed, Derived, Hypothesis, Undecidable }
public enum FabricProgressPhase
{
    Preparing,
    ReadingSystemData,
    CollectingEvidence,
    RetrievingEvidence,
    CheckingHardware,
    CheckingRuntime,
    LoadingModel,
    PreparingModel,
    AnalyzingLocally,
    RunningDeterministicRules,
    ValidatingProposal,
    ValidatingAnswer,
    PreparingResult
}
public enum FabricMode { Interactive, Balanced, Quality, Speed, Background, CpuOnly, LocalOnly, ExternalAllowed, Disabled }
public enum FabricModelRole { FastAssistant, DiagnosticReasoner }
public enum FabricProviderKind { Deterministic, Ollama, OpenAiCompatible, OnnxGenAi, WindowsMl, Cuda, DirectMl, Cpu, Nim, Mock }
public enum FabricStatus { Ready, NotConfigured, NotAcquired, Unhealthy, Busy, Unsupported }
public sealed record FabricModel(string Id, string Revision, string Family, string Quantization, string License,
    Uri Origin, string Digest, ImmutableArray<string> Languages, int DeclaredContextTokens,
    long RequiredRamBytes, long RequiredVramBytes, FabricTrust Trust, bool LicenseAccepted,
    bool StructuredOutput, bool Offline, string ExecutionProvider, string? LocalPath = null)
{
    public int AutomaticSelectionPriority { get; init; }
    public string? FallbackModelId { get; init; }
}
public sealed record FabricProviderInfo(int ApiVersion, string Id, FabricProviderKind Kind, FabricStatus Status,
    string Detail, Uri? Endpoint = null);
public sealed record FabricRequest(int Schema, Guid Id, string Task, string Language, string Prompt,
    string PromptHash, string ModelId, int ContextTokens, int MaximumOutputTokens, int TimeoutSeconds,
    DataClassification Classification, bool ExternalConsent = false);
public sealed record FabricResponse(string ModelId, string Json, int? InputTokens, int? OutputTokens,
    double? GenerationSeconds, double? LoadSeconds, double? FirstTokenSeconds)
{
    public string? DoneReason { get; init; }
}
public sealed record FabricProposal(
    [property: JsonRequired] int Schema,
    [property: JsonRequired] string Intent,
    [property: JsonRequired] string ActionId,
    [property: JsonRequired] ImmutableArray<Guid> Targets,
    [property: JsonRequired] ImmutableDictionary<string, JsonElement> Parameters);
public sealed record FabricObservation(Guid TargetId, bool Simulated, DataAvailability Availability,
    ImmutableDictionary<string, PlatformFact> Facts);
public sealed record FabricEvidence(Guid Id, Guid TargetId, string Field, string? Value, string Unit,
    SourceMetadata Metadata, string Actor, bool Simulated);
public sealed record FabricWindowsIdentity(
    string ProductName, Guid ProductNameEvidenceId,
    string? Edition, Guid? EditionEvidenceId,
    string? Release, Guid? ReleaseEvidenceId,
    string? Build, Guid? BuildEvidenceId,
    string? Architecture, Guid? ArchitectureEvidenceId);
public sealed record FabricPhysicalStorage(int? DiskNumber, string? Model, string? Manufacturer,
    int? BusType, int? MediaType, long? CapacityBytes, string? Firmware, int? HealthStatus);
public sealed record FabricVolume(string Drive, string FileSystem, long CapacityBytes, long UsedBytes, long FreeBytes);
public sealed record FabricStorageTypeCount(int? Code, string Name, int Count);
public enum FabricFieldAvailability { Available, Unavailable, Unknown, NotApplicable }
public sealed record FabricPhysicalStorageDeviceSummary(Guid EvidenceId,
    int? DiskNumber, FabricFieldAvailability DiskNumberAvailability,
    string? Model, FabricFieldAvailability ModelAvailability,
    string? Manufacturer, FabricFieldAvailability ManufacturerAvailability,
    string? BusType, FabricFieldAvailability BusTypeAvailability,
    string? MediaType, FabricFieldAvailability MediaTypeAvailability,
    string? CapacityDisplay, FabricFieldAvailability CapacityAvailability,
    string? HealthStatus, FabricFieldAvailability HealthStatusAvailability);
public sealed record FabricPhysicalStorageSummary(Guid? StatusEvidenceId, ImmutableArray<Guid> DeviceEvidenceIds,
    string? Status, int DeviceCount, long? TotalCapacityBytes, string? TotalCapacityDisplay,
    ImmutableArray<FabricStorageTypeCount> BusTypes, ImmutableArray<FabricStorageTypeCount> MediaTypes,
    ImmutableArray<FabricStorageTypeCount> HealthStatuses, int ManufacturerUnavailableCount,
    int ModelUnavailableCount, int MediaTypeUnavailableCount,
    ImmutableArray<FabricPhysicalStorageDeviceSummary> Devices);
public sealed record FabricVolumeSummaryEntry(ImmutableArray<Guid> SourceEvidenceIds,
    string Drive, string FileSystem, long CapacityBytes, long UsedBytes, long FreeBytes,
    string CapacityDisplay, string UsedDisplay, string FreeDisplay);
public sealed record FabricVolumeSummary(ImmutableArray<FabricVolumeSummaryEntry> Volumes);
public sealed record FabricEventGroup(
    [property: JsonRequired] string Log, [property: JsonRequired] string Provider,
    [property: JsonRequired] int EventId, [property: JsonRequired] byte? Level,
    [property: JsonRequired] string? Category, [property: JsonRequired] DateTimeOffset FirstSeen,
    [property: JsonRequired] DateTimeOffset LastSeen, [property: JsonRequired] int Count);
public sealed record FabricFinding(string Agent, Guid? Target, FabricProof Proof, string Explanation,
    ImmutableArray<Guid> Evidence, ImmutableArray<string> Missing);
public sealed record FabricKnowledge(Guid Id, string Title, string Version, string Origin, string Text, string Digest,
    DateTimeOffset Captured, DateTimeOffset Expires, FabricTrust Trust, DataClassification Classification,
    string Actor, Guid? Target, int ChunkingVersion = 1, bool Deleted = false);
public sealed record FabricCitation(Guid SourceId, string Version, string Digest, string Excerpt, double SearchScore);
public sealed record FabricPlan(int Schema, Guid Id, string Intent, ImmutableArray<Guid> Targets,
    string ActionId, string ActionHash, string RunbookId, string RunbookHash, string PromptHash, ImmutableArray<FabricFinding> Findings,
    ImmutableArray<FabricCitation> Citations, ImmutableArray<string> Assumptions, ImmutableArray<string> Missing,
    int Risk, ExecutionPolicy Policy, string Verification, string Rollback)
{
    [JsonIgnore] public string Hash => PlatformJson.Hash(this);
}
public sealed record FabricInvestigation(Guid Id, string Actor, string ModelId, string PromptHash,
    ImmutableArray<FabricObservation> Observations, ImmutableArray<FabricEvidence> Evidence,
    FabricPlan Plan, string Explanation, ImmutableArray<string> Issues, DateTimeOffset Created)
{
    public FabricAssistantResult? Assistant { get; init; }
}
public sealed record FabricPlanBinding(FabricPlan Plan, Guid Job, string PreviewHash);
public sealed record FabricMetric(Guid RequestId, string Provider, string Model, string Task, double Milliseconds,
    int? InputTokens, int? OutputTokens, long ProcessBytesBefore, long ProcessBytesAfter,
    string Result, bool Synthetic);
public sealed record FabricRoleMetric(string Model, FabricModelRole Role, bool Validated, double Milliseconds);
public sealed record FabricAudit(int Sequence, DateTimeOffset At, string Actor, string Event, string SubjectHash,
    string PreviousHash, string Hash);
public sealed record FabricHardware(DateTimeOffset At, long? TotalRamBytes, long? AvailableRamBytes,
    string Cpu, int LogicalProcessors, string? Gpu, long? TotalVramBytes, long? AvailableVramBytes,
    double? GpuPercent, double? TemperatureC, double? CpuPercent, long ProcessBytes,
    string Cuda, string DirectMl, string WindowsMl, string Npu, string PageFile, DateTimeOffset? VramMeasuredAt = null);

public sealed class FabricContractException : Exception
{
    public string Stage { get; }
    public string? Field { get; }
    public string Reason { get; }
    public string? Expected { get; }
    public string? Actual { get; }
    public string? RepairGuidance { get; }
    public FabricRejectedClaimDiagnostic? RejectedClaim { get; }
    public int? RepairTargetFindingIndex { get; }
    public string Diagnostic => $"{Stage} abgelehnt: {Reason}" +
        (Field is null ? "" : $" Feld `{Field}`") +
        (Expected is null ? "" : $"; erwartet {Expected}") +
        (Actual is null ? "" : $"; erhalten {Actual}") + ".";

    public FabricContractException(string stage, string reason, string? field = null,
        string? expected = null, string? actual = null, string? repairGuidance = null,
        FabricRejectedClaimDiagnostic? rejectedClaim = null, int? repairTargetFindingIndex = null)
        : base($"{stage}: {reason}")
    {
        Stage = stage;
        Reason = reason;
        Field = SafeField(field);
        Expected = expected;
        Actual = actual;
        RepairGuidance = repairGuidance;
        RejectedClaim = rejectedClaim;
        RepairTargetFindingIndex = repairTargetFindingIndex;
    }

    private static string? SafeField(string? field)
    {
        if (field is null) return null;
        var safe = new string(field.Take(96).Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '[' or ']').ToArray());
        return safe.Length == 0 ? "unbekannt" : safe;
    }
}

public sealed record FabricRejectedClaimDiagnostic(int FindingIndex, int ClaimIndex, string ClaimType,
    string Subject, string Property, string Operator, string ModelValueHash,
    ImmutableArray<Guid> EvidenceIds, ImmutableArray<string> ExpectedEvidenceFields,
    string? ExpectedValue, string ValidatorRule)
{
    public string? CanonicalValue { get; init; }
    public string? DisplayValue { get; init; }
    public string? FindingTextHash { get; init; }
}

public sealed class FabricResourceException(string message) : Exception(message) { }

public sealed record FabricProfileStage(string Model, int RequestedContextTokens, int Concurrency,
    int? ActualInputTokens, double Milliseconds, double? TokensPerSecond, long WorkingSetDelta,
    string Result, bool Synthetic);
public interface IAetherInferenceProvider
{
    FabricProviderInfo Info { get; }
    Task<FabricResponse> InferAsync(FabricModel model, FabricRequest request, CancellationToken token);
}
public interface IAetherObservationSource
{
    Task<FabricObservation> ReadAsync(AdminTarget target, CancellationToken token);
}
public interface IAetherReadToolSource : IAetherObservationSource
{
    Task<FabricObservation> ReadToolAsync(string tool, AdminTarget target, CancellationToken token);
}
public interface IAetherStateProtection
{
    byte[] Protect(byte[] clear);
    byte[] Unprotect(byte[] cipher);
}
public interface IAetherEmbeddingProvider
{
    string Id { get; }
    bool Offline { get; }
    float[] Embed(string text);
}
public static class FabricJson
{
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32 };
        options.MakeReadOnly(true);
        return options;
    }
    public static T Parse<T>(string text)
    {
        if (text.Length > 16 * 1024 * 1024) throw new InvalidDataException("Intelligence-JSON überschreitet das Budget.");
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
        CheckDuplicates(document.RootElement);
        return JsonSerializer.Deserialize<T>(text, Options) ?? throw new InvalidDataException("Intelligence-Objekt fehlt.");
    }

    public static T ParseModel<T>(string text, string stage)
    {
        var json = ExtractSingleModelObject(text, stage);
        using var document = ParseModelDocument(json, stage);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new FabricContractException(stage, "Der JSON-Root muss ein Objekt sein.", "root", "Objekt", ActualType(document.RootElement));
        CheckModelDuplicates(document.RootElement, stage);
        ValidateModelShape(document.RootElement, Options.GetTypeInfo(typeof(T)), stage, "");
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ??
                throw new FabricContractException(stage, "JSON-Objekt fehlt.", "root", "Objekt", "null");
        }
        catch (JsonException ex)
        {
            throw new FabricContractException(stage, "JSON-Schema ungültig.", SafePath(ex.Path), "gültiger C#-Vertrag", "Schemafehler");
        }
    }

    public static string DescribeModelContract<T>(T example)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(example, Options));
        var typeInfo = Options.GetTypeInfo(typeof(T));
        return string.Join("; ", document.RootElement.EnumerateObject()
            .Select(property =>
            {
                var contract = typeInfo.Properties.Single(item => item.Name == property.Name);
                return $"{property.Name}: {DescribeType(contract.PropertyType)}";
            }));
    }

    internal static JsonObject CreateJsonSchema<T>() => CreateJsonSchema(Options.GetTypeInfo(typeof(T)));

    private static JsonObject CreateJsonSchema(JsonTypeInfo typeInfo)
    {
        var type = Nullable.GetUnderlyingType(typeInfo.Type) ?? typeInfo.Type;
        if (type == typeof(JsonElement)) return new JsonObject();
        if (type == typeof(string)) return new JsonObject { ["type"] = "string" };
        if (type == typeof(Guid)) return new JsonObject { ["type"] = "string", ["format"] = "uuid" };
        if (type == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (type == typeof(int) || type == typeof(long)) return new JsonObject { ["type"] = "integer" };
        if (type == typeof(double) || type == typeof(float)) return new JsonObject { ["type"] = "number" };
        if (type.IsEnum)
        {
            var values = new JsonArray();
            foreach (var value in Enum.GetValues(type))
                values.Add(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
            return new JsonObject { ["type"] = "integer", ["enum"] = values };
        }
        if (typeInfo.Kind == JsonTypeInfoKind.Object)
        {
            var properties = new JsonObject();
            var required = new JsonArray();
            foreach (var property in typeInfo.Properties.Where(property => !property.IsExtensionData))
            {
                properties[property.Name] = CreateJsonSchema(Options.GetTypeInfo(property.PropertyType));
                if (property.IsRequired) required.Add(property.Name);
            }
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required,
                ["additionalProperties"] = false
            };
        }
        if (typeInfo.Kind == JsonTypeInfoKind.Enumerable && typeInfo.ElementType is { } elementType)
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = CreateJsonSchema(Options.GetTypeInfo(elementType))
            };
        if (typeInfo.Kind == JsonTypeInfoKind.Dictionary && typeInfo.ElementType is { } valueType)
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = CreateJsonSchema(Options.GetTypeInfo(valueType))
            };
        throw new InvalidOperationException("JSON-Schema für diesen typisierten Modellvertrag ist nicht unterstützt.");
    }

    private static string ExtractSingleModelObject(string text, string stage)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FabricContractException(stage, "Kein JSON-Objekt gefunden.");
        if (text.Length > 65536)
            throw new FabricContractException(stage, "Providerantwort überschreitet das JSON-Budget.");

        var content = text;
        var outerPrefix = "";
        var outerSuffix = "";
        var firstFence = content.IndexOf("```", StringComparison.Ordinal);
        if (firstFence >= 0)
        {
            var closingFence = content.IndexOf("```", firstFence + 3, StringComparison.Ordinal);
            if (closingFence < 0 || content.IndexOf("```", closingFence + 3, StringComparison.Ordinal) >= 0)
                throw new FabricContractException(stage, "Markdown-Codefence ist unvollständig oder mehrdeutig.");
            var headerEnd = content.IndexOfAny(['\r', '\n'], firstFence + 3);
            if (headerEnd < 0)
                throw new FabricContractException(stage, "Markdown-Codefence ist unvollständig.");
            var header = content[(firstFence + 3)..headerEnd].Trim();
            if (header.Length > 0 && !header.Equals("json", StringComparison.OrdinalIgnoreCase))
                throw new FabricContractException(stage, "Markdown-Codefence kennzeichnet kein JSON.");
            var bodyStart = headerEnd + (content[headerEnd] == '\r' && headerEnd + 1 < content.Length &&
                content[headerEnd + 1] == '\n' ? 2 : 1);
            outerPrefix = content[..firstFence];
            outerSuffix = content[(closingFence + 3)..];
            content = content[bodyStart..closingFence];
        }

        var candidates = new List<(int Start, int Length, JsonValueKind Kind)>();
        var malformed = false;
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] is not ('{' or '[')) continue;
            if (!TryFindContainerEnd(content, index, out var end))
            {
                malformed = true;
                break;
            }
            var candidate = content[index..(end + 1)];
            try
            {
                using var parsed = JsonDocument.Parse(candidate, new JsonDocumentOptions { MaxDepth = 32 });
                candidates.Add((index, end + 1 - index, parsed.RootElement.ValueKind));
            }
            catch (JsonException) { malformed = true; }
            index = end;
        }

        if (candidates.Count > 1)
            throw new FabricContractException(stage, "Mehrere JSON-Werte machen die Antwort mehrdeutig.");
        if (malformed)
            throw new FabricContractException(stage, "JSON ist syntaktisch ungültig oder unvollständig.");
        if (candidates.Count == 0)
        {
            try
            {
                using var scalar = JsonDocument.Parse(content.Trim(), new JsonDocumentOptions { MaxDepth = 32 });
                throw new FabricContractException(stage, "Der JSON-Root muss ein Objekt sein.", "root", "Objekt", ActualType(scalar.RootElement));
            }
            catch (JsonException)
            {
                throw new FabricContractException(stage, "Kein JSON-Objekt gefunden.");
            }
        }

        var value = candidates[0];
        if (value.Kind != JsonValueKind.Object)
            throw new FabricContractException(stage, "Der JSON-Root muss ein Objekt sein.", "root", "Objekt", value.Kind.ToString());
        var prefix = outerPrefix + content[..value.Start];
        var suffix = content[(value.Start + value.Length)..] + outerSuffix;
        if (prefix.Trim().Length + suffix.Trim().Length > 512)
            throw new FabricContractException(stage, "Erklärungstext außerhalb des JSON überschreitet das erlaubte Budget.");
        return content.Substring(value.Start, value.Length);
    }

    private static JsonDocument ParseModelDocument(string json, string stage)
    {
        try { return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException ex)
        {
            throw new FabricContractException(stage, "JSON ist syntaktisch ungültig.",
                expected: "gültiges JSON", actual: $"Zeile {ex.LineNumber}, Byte {ex.BytePositionInLine}");
        }
    }

    private static bool TryFindContainerEnd(string text, int start, out int end)
    {
        var stack = new Stack<char>();
        var inString = false;
        var escaped = false;
        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') inString = false;
                continue;
            }
            if (character == '"') { inString = true; continue; }
            if (character is '{' or '[') { stack.Push(character); continue; }
            if (character is not ('}' or ']')) continue;
            if (stack.Count == 0) { end = index; return true; }
            var open = stack.Pop();
            if (open == '{' && character != '}' || open == '[' && character != ']')
            { end = index; return true; }
            if (stack.Count == 0) { end = index; return true; }
        }
        end = -1;
        return false;
    }

    private static void ValidateModelShape(JsonElement element, JsonTypeInfo typeInfo, string stage, string path)
    {
        var type = Nullable.GetUnderlyingType(typeInfo.Type) ?? typeInfo.Type;
        if (type == typeof(JsonElement)) return;
        if (element.ValueKind == JsonValueKind.Null)
            throw new FabricContractException(stage, "JSON-Feld darf nicht null sein.", path,
                DescribeType(type), "null");

        if (typeInfo.Kind == JsonTypeInfoKind.Object)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw TypeError(stage, path, "Objekt", ActualType(element));
            var properties = typeInfo.Properties.Where(property => !property.IsExtensionData).ToArray();
            foreach (var item in element.EnumerateObject())
            {
                var property = properties.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, item.Name, StringComparison.Ordinal));
                if (property is null)
                    throw new FabricContractException(stage, "Unbekanntes Feld.", JoinPath(path, item.Name),
                        "bekanntes Feld", "unbekannt");
                ValidateModelShape(item.Value, Options.GetTypeInfo(property.PropertyType), stage,
                    JoinPath(path, property.Name));
            }
            foreach (var property in properties.Where(property => property.IsRequired))
                if (!element.EnumerateObject().Any(item => string.Equals(item.Name, property.Name,
                    StringComparison.Ordinal)))
                    throw new FabricContractException(stage, "Erforderliches Feld fehlt.", JoinPath(path, property.Name));
            return;
        }

        if (typeInfo.Kind == JsonTypeInfoKind.Enumerable)
        {
            if (element.ValueKind != JsonValueKind.Array)
                throw TypeError(stage, path, DescribeType(type), ActualType(element));
            var elementType = typeInfo.ElementType ?? throw new FabricContractException(stage, "C#-Arrayvertrag ist nicht verfügbar.", path);
            var index = 0;
            foreach (var item in element.EnumerateArray())
                ValidateModelShape(item, Options.GetTypeInfo(elementType), stage, JoinPath(path, $"[{index++}]"));
            return;
        }

        if (typeInfo.Kind == JsonTypeInfoKind.Dictionary)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw TypeError(stage, path, "Objekt", ActualType(element));
            if (typeInfo.KeyType != typeof(string) || typeInfo.ElementType is null)
                throw new FabricContractException(stage, "C#-Dictionaryvertrag ist nicht unterstützt.", path);
            foreach (var item in element.EnumerateObject())
                ValidateModelShape(item.Value, Options.GetTypeInfo(typeInfo.ElementType), stage, JoinPath(path, item.Name));
            return;
        }

        var valid = type == typeof(string) ? element.ValueKind == JsonValueKind.String :
            type == typeof(Guid) ? element.ValueKind == JsonValueKind.String && element.TryGetGuid(out _) :
            type == typeof(bool) ? element.ValueKind is JsonValueKind.True or JsonValueKind.False :
            type == typeof(int) || type.IsEnum ? element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out _) :
            type == typeof(long) ? element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out _) :
            type == typeof(double) ? element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out _) :
            type == typeof(float) ? element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out _) :
            true;
        if (type == typeof(Guid) && element.ValueKind == JsonValueKind.String && !element.TryGetGuid(out _))
            throw new FabricContractException(stage, "GUID-Zeichenfolge ist syntaktisch ungültig.",
                path, "syntaktisch gültige GUID", "ungültige GUID-Zeichenfolge");
        if (!valid)
            throw TypeError(stage, path, DescribeType(type), ActualType(element));
    }

    private static void CheckModelDuplicates(JsonElement element, string stage, string path = "")
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                var propertyPath = JoinPath(path, property.Name);
                if (!names.Add(property.Name))
                    throw new FabricContractException(stage, "Doppeltes JSON-Feld.", propertyPath);
                CheckModelDuplicates(property.Value, stage, propertyPath);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var child in element.EnumerateArray())
                CheckModelDuplicates(child, stage, JoinPath(path, $"[{index++}]"));
        }
    }

    private static FabricContractException TypeError(string stage, string? field, string expected, string actual) =>
        new(stage, "Feldtyp ist falsch.", field, expected, actual);

    private static string DescribeType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(Guid)) return "GUID-Zeichenfolge";
        if (type == typeof(string)) return "Zeichenfolge";
        if (type == typeof(int)) return "Ganzzahl";
        if (type.IsEnum)
            return "Ganzzahl-Enum(" + string.Join(", ", Enum.GetValues(type)
                .Cast<object>().Select(value => $"{Enum.GetName(type, value)}={Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)}")) + ")";
        if (type == typeof(bool)) return "Boolesch";
        var info = Options.GetTypeInfo(type);
        if (info.Kind == JsonTypeInfoKind.Enumerable && info.ElementType is { } itemType)
            return $"Array<{DescribeType(itemType)}>";
        if (info.Kind == JsonTypeInfoKind.Dictionary) return "Objekt";
        if (info.Kind == JsonTypeInfoKind.Object)
            return "Objekt{" + string.Join(", ", info.Properties.Where(property => !property.IsExtensionData)
                .Select(property => $"{property.Name}: {DescribeType(property.PropertyType)}")) + "}";
        if (type == typeof(JsonElement)) return "JSON-Wert";
        return "Objekt";
    }

    private static string ActualType(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => "Boolesch",
        JsonValueKind.Number when element.TryGetInt32(out _) => "Ganzzahl",
        _ => element.ValueKind.ToString()
    };

    private static string JoinPath(string path, string field) => path.Length == 0 ? field :
        field.Length > 0 && field[0] == '[' ? path + field : path + "." + field;

    private static string? SafePath(string? path)
    {
        if (path is null) return null;
        var safe = new string(path.Take(96).Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '[' or ']').ToArray());
        return safe.Length == 0 ? null : safe;
    }

    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Doppelter JSON-Schlüssel.");
                CheckDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) CheckDuplicates(child);
    }
}
