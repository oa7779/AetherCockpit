using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AetherOperations.Platform;

namespace AetherOperations.Intelligence;

public sealed record FabricAnswerClaim(
    [property: JsonRequired] string Text,
    [property: JsonRequired] FabricProof Proof,
    [property: JsonRequired] ImmutableArray<Guid> Evidence,
    [property: JsonRequired] ImmutableArray<Guid> Sources);
public sealed record FabricAssistantAnswer(
    [property: JsonRequired] int Schema,
    [property: JsonRequired] string Language,
    [property: JsonRequired] string Summary,
    [property: JsonRequired] ImmutableArray<FabricAnswerClaim> Findings,
    [property: JsonRequired] ImmutableArray<string> Missing,
    [property: JsonRequired] ImmutableArray<string> Recommendations);
public sealed record FabricFastFinding(
    [property: JsonRequired] string Text,
    [property: JsonRequired] ImmutableArray<Guid> Evidence,
    [property: JsonRequired] ImmutableArray<FabricFastAssistantClaim> Claims = default);
public sealed record FabricFastAssistantClaim(
    [property: JsonRequired] string ClaimType,
    [property: JsonRequired] string Subject,
    [property: JsonRequired] string Property,
    [property: JsonRequired] string Operator,
    [property: JsonRequired] string Value,
    [property: JsonRequired] string Unit,
    [property: JsonRequired] ImmutableArray<Guid> Evidence);
public sealed record FabricFastAssistantAnswer(
    [property: JsonRequired] string Summary,
    [property: JsonRequired] ImmutableArray<FabricFastFinding> Findings,
    [property: JsonRequired] ImmutableArray<string> Recommendations);

internal static class AetherIdentityContract
{
    internal static string DisplayValue(string subject, string property, string canonical, FabricWindowsIdentity? windows)
    {
        if (subject == "windowsIdentity" && property == "productName" &&
            canonical.StartsWith("Microsoft Windows ", StringComparison.Ordinal))
            return canonical["Microsoft ".Length..];
        if (subject == "windowsIdentity" && property == "edition" &&
            canonical == "Professional" && windows is not null &&
            Regex.IsMatch(windows.ProductName, @"(?i)\bWindows\s+(?:10|11)\s+Pro$",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            return "Pro";
        if (subject == "windowsIdentity" && property == "architecture" &&
            canonical.Equals("X64", StringComparison.OrdinalIgnoreCase))
            return "64-Bit";
        return canonical;
    }

    internal static bool ContainsValue(string text, string value) =>
        Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(value)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static bool ContainsDisplayValue(string text, FabricFastAssistantClaim claim, string display)
    {
        if (claim.Subject != "windowsIdentity" || display == claim.Value)
            return ContainsValue(text, display);
        return claim.Property switch
        {
            "edition" => Regex.IsMatch(text,
                @"(?i)\b(?:Windows\s+(?:10|11)\s+Pro|(?:Edition|Ausgabe)\s*[:=]\s*Pro)\b",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
            "architecture" => Regex.IsMatch(text,
                @"(?i)(?<![\p{L}\p{N}])64[\s-]?Bit(?![\p{L}\p{N}])",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
            _ => ContainsValue(text, display)
        };
    }
}
public sealed record FabricAssistantResult(FabricAssistantAnswer Answer, string ModelId, string Provider,
    string Routing, string PromptHash, int ContextCharacters, int? InputTokens, int? OutputTokens,
    double Milliseconds, double? GenerationSeconds, double? LoadSeconds, double? FirstTokenSeconds,
    long ProcessBytesBefore, long ProcessBytesAfter, bool RealModel, string Validator)
{
    public double? TokensPerSecond => GenerationSeconds is > 0 && OutputTokens is { } count
        ? count / GenerationSeconds : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FabricDiagnosticReport? DiagnosticReport { get; init; }
    public bool AnswerValidated { get; init; }
    public bool ModelAttempted { get; init; }
    public bool ModelInferenceSucceeded { get; init; }
    public string? AttemptedModelId { get; init; }
    public string? FallbackReason { get; init; }
}

public static class AetherAnswerValidator
{
    private static readonly Regex FastMeasurementPattern = new(
        @"(?<![\d.,])(?<number>\d+(?:[.,]\d+)?)\s*(?<unit>TB|GB|MB|KB|Bytes?|B|Prozent|%)(?=$|[\s.,;:!?])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static FabricAssistantAnswer Parse(string json, ImmutableArray<FabricEvidence> evidence,
        ImmutableArray<FabricCitation> citations)
    {
        var answer = FabricJson.ParseModel<FabricAssistantAnswer>(json, "Antwort-JSON");
        if (answer.Schema != 1)
            throw new FabricContractException("Antwort-Schema", "Schemawert ist nicht freigegeben.", "schema", "1", "abweichender Wert");
        if (answer.Language != "de")
            throw new FabricContractException("Antwort-Schema", "Sprache ist nicht freigegeben.", "language", "de", "abweichender Wert");
        if (answer.Findings.IsDefault || answer.Findings.Length is < 1 or > 16)
            throw new FabricContractException("Antwort-Schema", "Befundliste muss 1 bis 16 Einträge enthalten.",
                "findings", "Array mit 1 bis 16 Einträgen", "ungültige Liste");
        if (answer.Missing.IsDefault || answer.Missing.Length > 24)
            throw new FabricContractException("Antwort-Schema", "Liste fehlender Angaben überschreitet das Limit.",
                "missing", "Array mit höchstens 24 Einträgen", "ungültige Liste");
        if (answer.Recommendations.IsDefault || answer.Recommendations.Length > 8)
            throw new FabricContractException("Antwort-Schema", "Empfehlungsliste überschreitet das Limit.",
                "recommendations", "Array mit höchstens 8 Einträgen", "ungültige Liste");
        ValidateText(answer.Summary, 2400, "summary");
        // A language label alone is not evidence that a provider followed the German prompt.
        if (!System.Text.RegularExpressions.Regex.IsMatch(answer.Summary,
            @"(?i)\b(der|die|das|ist|sind|nicht|liegen|vorhanden|verfügbar|belegt|wurden|wird|zeigt|eine|kein|keine)\b",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new FabricContractException("Antwort-Validierung", "Deutsch-Heuristik nicht erfüllt.", "summary");
        var known = AllowedEvidenceIds(evidence, DateTimeOffset.UtcNow).ToHashSet();
        for (var findingIndex = 0; findingIndex < answer.Findings.Length; findingIndex++)
        {
            var claim = answer.Findings[findingIndex];
            if (claim is null) throw new FabricContractException("Antwort-Schema", "Befund darf nicht null sein.", "findings[]");
            ValidateText(claim.Text, 1600, "findings[].text");
            if (!Enum.IsDefined(claim.Proof))
                throw new FabricContractException("Antwort-Schema", "Proof-Wert ist nicht definiert.",
                    "findings[].proof", "definierter Ganzzahl-Enumwert", "nicht definiert");
            if (claim.Evidence.IsDefault || claim.Evidence.Length > 16)
                throw new FabricContractException("Antwort-Schema", "Evidence-Liste ist ungültig.",
                    "findings[].evidence", "GUID-Array mit höchstens 16 Einträgen", "ungültige Liste");
            if (claim.Sources.IsDefault || claim.Sources.Length > 6)
                throw new FabricContractException("Antwort-Schema", "Quellenliste ist ungültig.",
                    "findings[].sources", "GUID-Array mit höchstens 6 Einträgen", "ungültige Liste");
            for (var evidenceIndex = 0; evidenceIndex < claim.Evidence.Length; evidenceIndex++)
                if (!known.Contains(claim.Evidence[evidenceIndex]))
                    throw new FabricContractException("Belegvalidierung", "EvidenceId unbekannt oder nicht aktuell verfügbar.",
                        $"findings[{findingIndex}].evidence[{evidenceIndex}]", "aktuelle GUID aus allowedEvidenceIds",
                        "unbekannt oder nicht verfügbar");
            for (var sourceIndex = 0; sourceIndex < claim.Sources.Length; sourceIndex++)
            {
                var sourceId = claim.Sources[sourceIndex];
                if (citations.Any(c => c.SourceId == sourceId)) continue;
                var evidenceId = evidence.Any(e => e.Id == sourceId);
                throw new FabricContractException("Belegvalidierung",
                    evidenceId ? "GUID gehört zum Evidence- statt zum Source-ID-Raum." :
                        "Quellen-ID ist nicht im bereitgestellten Quellenkontext enthalten.",
                    $"findings[{findingIndex}].sources[{sourceIndex}]", "GUID aus allowedSourceIds",
                    evidenceId ? "EvidenceId im falschen Identifierraum" : "unbekannte Quellen-ID");
            }
            if (claim.Proof is FabricProof.Observed or FabricProof.Derived &&
                claim.Evidence.IsEmpty && claim.Sources.IsEmpty)
                throw new FabricContractException("Belegvalidierung", "Beobachtung oder Ableitung benötigt mindestens einen Beleg.",
                    $"findings[{findingIndex}].evidence", "mindestens eine aktuelle EvidenceId oder SourceId", "leer");
            if (claim.Evidence.Any(id => evidence.Any(e => e.Id == id && e.Simulated)) &&
                !claim.Text.Contains("simuliert", StringComparison.OrdinalIgnoreCase))
                throw new FabricContractException("Belegvalidierung", "Simulierte Quelle ist nicht als simuliert markiert.",
                    "findings[].text");
        }
        foreach (var text in answer.Missing) ValidateText(text, 800, "missing[]");
        foreach (var text in answer.Recommendations) ValidateText(text, 800, "recommendations[]");
        ValidateWindowsProduct(answer.Summary, evidence);
        ValidateMeasurements(answer.Summary, evidence);
        ValidateIdentity(answer.Summary, evidence);
        foreach (var claim in answer.Findings)
        {
            var cited = evidence.Where(e => claim.Evidence.Contains(e.Id)).ToImmutableArray();
            ValidateWindowsProduct(claim.Text, cited);
            ValidateMeasurements(claim.Text, cited);
            ValidateIdentity(claim.Text, cited);
        }
        foreach (var text in answer.Recommendations)
        {
            ValidateWindowsProduct(text, evidence);
            ValidateMeasurements(text, evidence);
            ValidateIdentity(text, evidence);
        }
        return answer with { Findings = Deduplicate(answer.Findings) };
    }

    public static FabricAssistantAnswer ParseFastAssistant(string json, ImmutableArray<FabricEvidence> evidence,
        ImmutableArray<FabricEvidence> sourceEvidence = default)
    {
        var answer = FabricJson.ParseModel<FabricFastAssistantAnswer>(json, "FastAssistant-JSON");
        return ParseFastAssistant(answer, evidence, sourceEvidence);
    }

    internal static FabricAssistantAnswer ParseFastAssistant(FabricFastAssistantAnswer answer,
        ImmutableArray<FabricEvidence> evidence, ImmutableArray<FabricEvidence> sourceEvidence = default)
    {
        var identitySources = sourceEvidence.IsDefault ? evidence : sourceEvidence;
        var known = AllowedEvidenceIds(evidence, DateTimeOffset.UtcNow).ToHashSet();
        if (answer.Findings.IsDefault ||
            (known.Count == 0 ? !answer.Findings.IsEmpty : answer.Findings.Length is < 1 or > 3))
            throw new FabricContractException("FastAssistant-Schema",
                "Mit aktuellen Belegen sind 1 bis 3 Befunde erforderlich; ohne aktuelle Belege muss die Liste leer sein.",
                "findings", "beleggebundene Befundliste", "ungültige Liste");
        if (answer.Recommendations.IsDefault || answer.Recommendations.Length > 1)
            throw new FabricContractException("FastAssistant-Schema", "Empfehlungsliste überschreitet das Limit.",
                "recommendations", "Array mit höchstens einem Eintrag", "ungültige Liste");
        ValidateText(answer.Summary, 240, "summary");

        var findings = ImmutableArray.CreateBuilder<FabricAnswerClaim>(answer.Findings.Length);
        for (var index = 0; index < answer.Findings.Length; index++)
        {
            var finding = answer.Findings[index];
            if (finding is null)
                throw new FabricContractException("FastAssistant-Schema", "Befund darf nicht null sein.", $"findings[{index}]");
            if (finding.Text is { Length: > 140 } overlongText &&
                AetherSafetyClassifier.Inspect(overlongText).IsEmpty)
            {
                // Data-sparse diagnostics so an over-budget finding text becomes a focused text
                // repair; suspicious texts still take the strict path below.
                var overlongHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(overlongText)));
                throw new FabricContractException("Antwort-Validierung",
                    "Text ist leer, zu groß oder enthält unzulässige Anweisungen.", $"findings[{index}].text",
                    "höchstens 140 Zeichen",
                    overlongText.Length.ToString(CultureInfo.InvariantCulture) + " Zeichen",
                    $"Der Befundtext in findings[{index}] hat {overlongText.Length} Zeichen; höchstens 140 sind erlaubt. " +
                    "Kürze nur diesen Text; jeden Identity-Claimwert weiterhin exakt nennen; claims und evidence unverändert.",
                    new FabricRejectedClaimDiagnostic(index, -1, "Text", "findingText", "length", "Matches",
                        overlongHash, finding.Evidence.IsDefault ? [] : finding.Evidence,
                        [], "höchstens 140 Zeichen", "finding.text-budget")
                    {
                        FindingTextHash = overlongHash
                    },
                    index);
            }
            ValidateText(finding.Text, 140, $"findings[{index}].text");
            if (finding.Evidence.IsDefault || finding.Evidence.IsEmpty || finding.Evidence.Length > 3 ||
                finding.Evidence.Distinct().Count() != finding.Evidence.Length)
                throw new FabricContractException("FastAssistant-Schema", "Befund benötigt 1 bis 3 eindeutige EvidenceIds.",
                    $"findings[{index}].evidence", "GUID-Array mit 1 bis 3 Einträgen", "ungültige Liste");
            foreach (var id in finding.Evidence)
                if (!known.Contains(id))
                    throw new FabricContractException("Belegvalidierung", "EvidenceId unbekannt oder nicht aktuell verfügbar.",
                        $"findings[{index}].evidence", "aktuelle GUID aus allowedEvidenceIds", "unbekannt oder nicht verfügbar",
                        $"findings[{index}] zitiert eine unbekannte EvidenceId. Nur IDs aus allowedEvidenceIds verwenden " +
                        "oder den Befund entfernen; andere Befunde unverändert lassen.",
                        new FabricRejectedClaimDiagnostic(index, -1, "Evidence", "findingEvidence", "id", "Matches",
                            "", [], [], null, "finding.evidence"), index);
            var cited = evidence.Where(item => finding.Evidence.Contains(item.Id)).ToImmutableArray();
            ValidateFastAssistantClaims(finding, index, cited, identitySources, known);
            if (cited.Any(item => item.Simulated) &&
                !finding.Text.Contains("simuliert", StringComparison.OrdinalIgnoreCase))
                throw new FabricContractException("Belegvalidierung", "Simulierte Quelle ist nicht als simuliert markiert.",
                    $"findings[{index}].text");
            ValidateFastAssistantText(finding.Text, cited, evidence, identitySources, finding.Claims,
                $"findings[{index}].text");
            findings.Add(new(finding.Text, FabricProof.Observed, finding.Evidence, []));
        }

        ValidateFastAssistantText(answer.Summary, [], evidence, identitySources, [], "summary");
        foreach (var recommendation in answer.Recommendations)
        {
            ValidateText(recommendation, 120, "recommendations[]");
            ValidateFastAssistantText(recommendation, [], evidence, identitySources, [], "recommendations[]");
        }
        return new(1, "de", answer.Summary, findings.ToImmutable(), [], answer.Recommendations);
    }

    private static void ValidateFastAssistantClaims(FabricFastFinding finding, int findingIndex,
        ImmutableArray<FabricEvidence> citedEvidence, ImmutableArray<FabricEvidence> sourceEvidence,
        HashSet<Guid> knownEvidenceIds)
    {
        if (finding.Claims.IsDefault || finding.Claims.IsEmpty || finding.Claims.Length > 5)
            throw new FabricContractException("FastAssistant-Schema",
                "Jeder Befund benötigt 1 bis 5 strukturierte Claims.",
                $"findings[{findingIndex}].claims", "Array mit 1 bis 5 Claims", "ungültige Liste");

        var claimEvidence = new HashSet<Guid>();
        for (var claimIndex = 0; claimIndex < finding.Claims.Length; claimIndex++)
        {
            var claim = finding.Claims[claimIndex];
            if (claim is null || string.IsNullOrWhiteSpace(claim.ClaimType) ||
                string.IsNullOrWhiteSpace(claim.Subject) || string.IsNullOrWhiteSpace(claim.Property) ||
                string.IsNullOrWhiteSpace(claim.Operator) || string.IsNullOrWhiteSpace(claim.Value) ||
                claim.Value.Length > 100 || claim.Subject.Length > 100 || claim.Property.Length > 64 ||
                claim.Unit is null || claim.Unit.Length > 16)
                throw new FabricContractException("FastAssistant-Schema", "Claimfelder fehlen oder überschreiten ihr Budget.",
                    $"findings[{findingIndex}].claims[{claimIndex}]", "vollständige Claimfelder im Budget",
                    "ungültiger Claim",
                    $"Der Claim findings[{findingIndex}].claims[{claimIndex}] ist unvollständig oder überschreitet " +
                    "sein Feldbudget. Korrigiere ausschließlich diesen Claim oder entferne den betroffenen Befund. " +
                    "Alle übrigen Befunde, Claims und EvidenceIds unverändert lassen.",
                    SchemaClaimDiagnostic(claim, findingIndex, claimIndex), findingIndex);
            if (claim.Evidence.IsDefault || claim.Evidence.Length != 1)
                throw new FabricContractException("FastAssistant-Schema", "Jeder Claim benötigt genau eine EvidenceId.",
                    $"findings[{findingIndex}].claims[{claimIndex}].evidence", "GUID-Array mit genau einem Eintrag",
                    "ungültige Liste",
                    $"Der Claim findings[{findingIndex}].claims[{claimIndex}] benötigt genau eine EvidenceId. " +
                    "Korrigiere ausschließlich diesen Claim oder entferne den betroffenen Befund.",
                    SchemaClaimDiagnostic(claim, findingIndex, claimIndex), findingIndex);
            var evidenceId = claim.Evidence[0];
            claimEvidence.Add(evidenceId);
            if (!knownEvidenceIds.Contains(evidenceId) || !finding.Evidence.Contains(evidenceId))
            {
                ValidateFastAssistantClaim(claim, findingIndex, claimIndex, citedEvidence, sourceEvidence);
                throw new FabricContractException("Belegvalidierung",
                    "Claim-EvidenceId muss aktuell und in findings[].evidence enthalten sein.",
                    $"findings[{findingIndex}].claims[{claimIndex}].evidence",
                    "aktuelle GUID aus findings[].evidence", "unbekannt oder nicht zugeordnet");
            }
            AetherSafetyClassifier.RequireNoSecrets(claim.Value);
            ValidateFastAssistantClaim(claim, findingIndex, claimIndex, citedEvidence, sourceEvidence);
        }

        if (!claimEvidence.SetEquals(finding.Evidence))
            throw new FabricContractException("Belegvalidierung",
                "Befund-EvidenceIds müssen genau den EvidenceIds seiner strukturierten Claims entsprechen.",
                $"findings[{findingIndex}].evidence",
                "Vereinigung der Claims-EvidenceIds", "abweichende EvidenceIds");

        for (var claimIndex = 0; claimIndex < finding.Claims.Length; claimIndex++)
        {
            var claim = finding.Claims[claimIndex];
            if (claim.ClaimType == "Availability" && claim.Subject == "physicalStorageSummary" &&
                claim.Property == "totalCapacity" && claim.Operator == "Equals")
            {
                var negative = Regex.IsMatch(finding.Text,
                    @"(?i)\b(?:nicht verfügbar|nicht angegeben|nicht erfasst|nicht gemeldet|fehlt|unbekannt|nicht bekannt)\b",
                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                var positive = Regex.IsMatch(finding.Text,
                    @"(?i)\b(?:ist|sind)\s+(?:verfügbar|bekannt|vorhanden|angegeben|erfasst|gemeldet)\b",
                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                if (claim.Value == "Unavailable" ? !negative || positive : negative)
                    throw RejectedClaim(claim, findingIndex, claimIndex, ["physicalStorageSummary"], claim.Value,
                        "storage.capacity-availability-text", citedEvidence, sourceEvidence,
                        "Verfügbarkeitsaussage widerspricht dem strukturierten Claim.");
            }
            var claimEvidenceItem = citedEvidence.Single(item => item.Id == claim.Evidence[0]);
            var windows = claimEvidenceItem.Field == "windowsIdentity"
                ? AetherContextManager.ReadWindowsIdentity(claimEvidenceItem, sourceEvidence) : null;
            var display = AetherIdentityContract.DisplayValue(claim.Subject, claim.Property, claim.Value, windows);
            if (claim.ClaimType == "Identity" &&
                !AetherIdentityContract.ContainsValue(finding.Text, claim.Value) &&
                !AetherIdentityContract.ContainsDisplayValue(finding.Text, claim, display))
                throw RejectedClaim(claim, findingIndex, claimIndex,
                    [claimEvidenceItem.Field], claim.Value,
                    "claim.text-identity", citedEvidence, sourceEvidence,
                    "Identitätswert erscheint weder kanonisch noch als registrierter Anzeigewert im Befundtext.",
                    display, finding.Text);
        }
    }

    internal static void ValidateFastAssistantClaim(FabricFastAssistantClaim claim, int findingIndex, int claimIndex,
        ImmutableArray<FabricEvidence> citedEvidence, ImmutableArray<FabricEvidence> sourceEvidence)
    {
        var evidence = citedEvidence.SingleOrDefault(item => item.Id == claim.Evidence[0]);
        var expectedFields = evidence is null ? ImmutableArray<string>.Empty : [evidence.Field];
        string? expectedValue = null;
        var rule = "claim.evidence";
        var valid = false;

        var canonicalVolumeSubject = CanonicalVolumeSubject(claim.Subject);
        var expectedStorageField =
            claim.Subject == "physicalStorageSummary" ||
            claim.Subject.StartsWith("physicalStorageSummary/", StringComparison.Ordinal) ||
            claim.Subject.StartsWith("physicalStorageDevice:", StringComparison.Ordinal)
                ? "physicalStorageSummary"
                : claim.Subject == "volumeSummary" || canonicalVolumeSubject.StartsWith("volume:", StringComparison.Ordinal)
                    ? "volumeSummary"
                    : null;
        if (claim.Subject == "windowsIdentity" && evidence?.Field != "windowsIdentity")
        {
            expectedFields = ["windowsIdentity"];
            var identityEvidence = sourceEvidence.SingleOrDefault(item =>
                item.TargetId == evidence?.TargetId && item.Field == "windowsIdentity" &&
                IsCurrentEvidence(item, DateTimeOffset.UtcNow));
            if (identityEvidence is not null)
            {
                var identity = AetherContextManager.ReadWindowsIdentity(identityEvidence, sourceEvidence);
                expectedValue = WindowsIdentityClaimValue(identity, claim.Property);
            }
            throw RejectedClaim(claim, findingIndex, claimIndex, expectedFields, expectedValue,
                "windows.identity-evidence-binding", citedEvidence, sourceEvidence,
                "Windows-Identität benötigt den zugehörigen Windows-Evidence-Beleg.");
        }
        if (expectedStorageField is not null &&
            (evidence?.Field != expectedStorageField || claim.Subject.Contains('/', StringComparison.Ordinal)))
        {
            expectedFields = [expectedStorageField];
            var target = evidence?.TargetId ?? sourceEvidence.FirstOrDefault(item =>
                item.Id == claim.Evidence[0])?.TargetId;
            var summaryEvidence = sourceEvidence.FirstOrDefault(item =>
                item.TargetId == target && item.Field == expectedStorageField &&
                IsCurrentEvidence(item, DateTimeOffset.UtcNow));
            if (summaryEvidence is not null)
            {
                if (expectedStorageField == "physicalStorageSummary")
                {
                    var summary = AetherContextManager.ReadPhysicalStorageSummary(summaryEvidence, sourceEvidence);
                    if (claim.Subject.StartsWith("physicalStorageDevice:", StringComparison.Ordinal) &&
                        Guid.TryParseExact(claim.Subject["physicalStorageDevice:".Length..], "D", out var deviceId) &&
                        summary.Devices.SingleOrDefault(item => item.EvidenceId == deviceId) is { } device)
                        expectedValue = claim.Property switch
                        {
                            "diskNumber" => device.DiskNumber?.ToString(CultureInfo.InvariantCulture) ??
                                device.DiskNumberAvailability.ToString(),
                            "capacity" => device.CapacityDisplay ?? device.CapacityAvailability.ToString(),
                            "manufacturer" => device.Manufacturer ?? device.ManufacturerAvailability.ToString(),
                            "model" => device.Model ?? device.ModelAvailability.ToString(),
                            "busType" => device.BusType ?? device.BusTypeAvailability.ToString(),
                            "medium" => device.MediaType ?? device.MediaTypeAvailability.ToString(),
                            "healthStatus" => device.HealthStatus ?? device.HealthStatusAvailability.ToString(),
                            _ => null
                        };
                    else
                        expectedValue = claim.Property switch
                        {
                            "deviceCount" => summary.DeviceCount.ToString(CultureInfo.InvariantCulture) + " devices",
                            "totalCapacity" when claim.ClaimType == "Availability" =>
                                summary.TotalCapacityDisplay is null ? "Unavailable" : "Available",
                            "totalCapacity" => summary.TotalCapacityDisplay,
                            "manufacturer" or "manufacturerCount" => $"{summary.ManufacturerUnavailableCount} unavailable",
                            "model" or "modelCount" => $"{summary.ModelUnavailableCount} unavailable",
                            "medium" or "mediumCount" => $"{summary.MediaTypeUnavailableCount} unavailable",
                            _ => null
                        };
                }
                else
                {
                    var summary = AetherContextManager.ReadVolumeSummary(summaryEvidence, sourceEvidence);
                    if (canonicalVolumeSubject.StartsWith("volume:", StringComparison.Ordinal))
                    {
                        var drive = canonicalVolumeSubject["volume:".Length..];
                        var volume = summary.Volumes.SingleOrDefault(item =>
                            string.Equals(item.Drive, drive, StringComparison.OrdinalIgnoreCase));
                        expectedValue = volume is null ? null : claim.Property switch
                        {
                            "capacity" => volume.CapacityDisplay,
                            "used" => volume.UsedDisplay,
                            "free" => volume.FreeDisplay,
                            "drive" => volume.Drive,
                            "fileSystem" => volume.FileSystem,
                            _ => null
                        };
                    }
                    else if (claim.Property == "volumeCount")
                        expectedValue = summary.Volumes.Length.ToString(CultureInfo.InvariantCulture) + " volumes";
                }
                rule = expectedStorageField == "physicalStorageSummary"
                    ? "storage.summary-evidence-binding"
                    : "volume.summary-evidence-binding";
            }
        }

        if (evidence is not null && claim.Subject == "physicalStorageSummary" &&
            evidence.Field == "physicalStorageSummary")
        {
            var summary = AetherContextManager.ReadPhysicalStorageSummary(evidence, sourceEvidence);
            rule = "storage.summary";
            if (claim.ClaimType == "Availability" && claim.Property == "totalCapacity" &&
                claim.Operator == "Equals")
            {
                expectedValue = summary.TotalCapacityDisplay is null ? "Unavailable" : "Available";
                valid = ClaimEquals(claim, expectedValue, "");
                rule = "storage.total-capacity-availability";
            }
            else if (claim.Property == "totalCapacity" &&
                TryDisplayParts(summary.TotalCapacityDisplay, out var value, out var unit))
            {
                expectedValue = value + " " + unit;
                valid = claim.ClaimType == "Measurement" && claim.Operator == "Equals" &&
                    ClaimEquals(claim, value, unit);
                rule = "storage.formatted-total-capacity";
            }
            else if (claim.Property == "deviceCount" && claim.Operator == "Equals")
            {
                expectedValue = summary.DeviceCount.ToString(CultureInfo.InvariantCulture);
                valid = claim.ClaimType == "Count" && ClaimEquals(claim, expectedValue, "devices");
                if (!valid && claim.Value == expectedValue) rule = "storage.count-claim-shape";
            }
            else if (claim.ClaimType == "Availability" &&
                claim.Operator is "UnavailableCount" or "Equals" &&
                (CanonicalStorageDetailProperty(claim.Property) ?? claim.Property) is
                    "manufacturer" or "model" or "medium")
            {
                var canonicalProperty = CanonicalStorageDetailProperty(claim.Property) ?? claim.Property;
                var unavailable = canonicalProperty switch
                {
                    "manufacturer" => summary.ManufacturerUnavailableCount,
                    "model" => summary.ModelUnavailableCount,
                    _ => summary.MediaTypeUnavailableCount
                };
                expectedValue = $"{unavailable} von {summary.DeviceCount} Geräte";
                var valueBacked = claim.Value == unavailable.ToString(CultureInfo.InvariantCulture);
                valid = valueBacked && canonicalProperty == claim.Property &&
                    claim.Operator == "UnavailableCount" && claim.Unit == "devices";
                rule = !valid && valueBacked
                    ? "storage.availability-property-shape"
                    : "storage.availability-count";
            }
            else if (claim.ClaimType == "Count" && claim.Operator == "Equals" &&
                TryGetStorageTypeCount(summary, claim.Property, out var typeCount))
            {
                expectedValue = typeCount.ToString(CultureInfo.InvariantCulture);
                valid = ClaimEquals(claim, expectedValue, "devices");
                rule = "storage.type-count";
            }
            else if (claim.ClaimType == "Status" && claim.Property == "status" &&
                claim.Operator == "Equals" && summary.Status is { } status)
            {
                expectedValue = status;
                valid = ClaimEquals(claim, status, "");
                rule = "storage.summary-status";
            }
        }
        else if (evidence is not null && claim.Subject.StartsWith("physicalStorageDevice:",
            StringComparison.Ordinal) && evidence.Field == "physicalStorageSummary")
        {
            var summary = AetherContextManager.ReadPhysicalStorageSummary(evidence, sourceEvidence);
            var idText = claim.Subject["physicalStorageDevice:".Length..];
            var device = Guid.TryParseExact(idText, "D", out var deviceId)
                ? summary.Devices.SingleOrDefault(item => item.EvidenceId == deviceId)
                : null;
            if (device is not null)
            {
                expectedFields = ["physicalStorageSummary", "physicalDrive"];
                rule = "storage.device-field";
                if (claim.ClaimType == "Availability" && claim.Operator == "Equals")
                {
                    var state = claim.Property switch
                    {
                        "manufacturer" => device.ManufacturerAvailability,
                        "model" => device.ModelAvailability,
                        "busType" => device.BusTypeAvailability,
                        "medium" => device.MediaTypeAvailability,
                        "diskNumber" => device.DiskNumberAvailability,
                        "capacity" => device.CapacityAvailability,
                        "healthStatus" => device.HealthStatusAvailability,
                        _ => (FabricFieldAvailability?)null
                    };
                    if (state is { } availability)
                    {
                        expectedValue = availability.ToString();
                        valid = ClaimEquals(claim, expectedValue, "");
                        rule = "storage.device-availability";
                    }
                }
                else if (claim.ClaimType == "Measurement" && claim.Property == "capacity" &&
                    claim.Operator == "Equals" && device.CapacityAvailability == FabricFieldAvailability.Available &&
                    TryDisplayParts(device.CapacityDisplay, out var value, out var unit))
                {
                    expectedValue = value + " " + unit;
                    valid = ClaimEquals(claim, value, unit);
                    rule = "storage.formatted-device-capacity";
                }
                else if (claim.ClaimType == "Identity" && claim.Operator == "Equals")
                {
                    var actual = claim.Property switch
                    {
                        "manufacturer" when device.ManufacturerAvailability == FabricFieldAvailability.Available =>
                            device.Manufacturer,
                        "model" when device.ModelAvailability == FabricFieldAvailability.Available => device.Model,
                        "busType" when device.BusTypeAvailability == FabricFieldAvailability.Available => device.BusType,
                        "medium" when device.MediaTypeAvailability == FabricFieldAvailability.Available => device.MediaType,
                        "diskNumber" when device.DiskNumberAvailability == FabricFieldAvailability.Available =>
                            device.DiskNumber?.ToString(CultureInfo.InvariantCulture),
                        _ => null
                    };
                    if (actual is not null)
                    {
                        expectedValue = actual;
                        valid = ClaimEquals(claim, actual, "");
                    }
                    else
                    {
                        expectedValue = claim.Property switch
                        {
                            "manufacturer" => device.ManufacturerAvailability.ToString(),
                            "model" => device.ModelAvailability.ToString(),
                            "busType" => device.BusTypeAvailability.ToString(),
                            "medium" => device.MediaTypeAvailability.ToString(),
                            "diskNumber" => device.DiskNumberAvailability.ToString(),
                            _ => null
                        };
                        if (expectedValue is not null) rule = "storage.device-field-availability";
                    }
                }
                else if (claim.ClaimType == "Status" && claim.Property == "healthStatus" &&
                    claim.Operator == "Equals" && device.HealthStatusAvailability == FabricFieldAvailability.Available &&
                    device.HealthStatus is { } health)
                {
                    expectedValue = health;
                    valid = ClaimEquals(claim, health, "");
                }
            }
        }
        else if (evidence is not null && claim.Subject == "volumeSummary" &&
            evidence.Field == "volumeSummary")
        {
            var summary = AetherContextManager.ReadVolumeSummary(evidence, sourceEvidence);
            rule = "volume.summary";
            if (claim.Property == "volumeCount" && claim.Operator == "Equals")
            {
                expectedValue = summary.Volumes.Length.ToString(CultureInfo.InvariantCulture);
                valid = claim.ClaimType == "Count" && ClaimEquals(claim, expectedValue, "volumes");
                if (!valid && claim.Value == expectedValue) rule = "volume.count-claim-shape";
            }
        }
        else if (evidence is not null && claim.Subject.StartsWith("volume:", StringComparison.Ordinal) &&
            evidence.Field == "volumeSummary")
        {
            var summary = AetherContextManager.ReadVolumeSummary(evidence, sourceEvidence);
            var drive = claim.Subject["volume:".Length..];
            var volume = summary.Volumes.SingleOrDefault(item =>
                string.Equals(item.Drive, drive, StringComparison.OrdinalIgnoreCase));
            if (volume is not null)
            {
                rule = "volume.field";
                if (claim.ClaimType == "Identity" && claim.Operator == "Equals")
                {
                    var actual = claim.Property switch
                    {
                        "drive" => volume.Drive,
                        "fileSystem" => volume.FileSystem,
                        _ => null
                    };
                    if (actual is not null)
                    {
                        expectedValue = actual;
                        valid = ClaimEquals(claim, actual, "");
                    }
                }
                else if (claim.ClaimType == "Measurement" && claim.Operator == "Equals")
                {
                    var display = claim.Property switch
                    {
                        "capacity" => volume.CapacityDisplay,
                        "used" => volume.UsedDisplay,
                        "free" => volume.FreeDisplay,
                        _ => null
                    };
                    if (TryDisplayParts(display, out var value, out var unit))
                    {
                        expectedValue = value + " " + unit;
                        valid = ClaimEquals(claim, value, unit);
                        rule = "volume.formatted-measurement";
                    }
                }
            }
        }
        else if (evidence is not null && claim.Subject == "windowsIdentity" &&
            evidence.Field == "windowsIdentity" && claim.ClaimType == "Identity" &&
            claim.Operator == "Equals")
        {
            var identity = AetherContextManager.ReadWindowsIdentity(evidence, sourceEvidence);
            var actual = WindowsIdentityClaimValue(identity, claim.Property);
            if (actual is not null)
            {
                expectedValue = actual;
                valid = ClaimEquals(claim, actual, "");
                rule = "windows.identity";
            }
        }
        else if (evidence is not null && claim.Subject == evidence.Field &&
            !AetherToolBroker.IsPhysicalStorageField(evidence.Field) &&
            !AetherToolBroker.IsVolumeField(evidence.Field) &&
            !AetherToolBroker.IsEventGroupField(evidence.Field))
        {
            rule = "evidence.scalar";
            if (claim.Property == "value" && claim.Operator == "Equals")
            {
                var actual = EvidenceClaimValue(evidence, out var unit, out var claimType);
                if (actual is not null)
                {
                    expectedValue = actual + (unit.Length == 0 ? "" : " " + unit);
                    valid = claim.ClaimType == claimType && ClaimEquals(claim, actual, unit);
                    rule = AetherContextManager.IsFormattedByteField(evidence.Field)
                        ? "measurement.deterministic-byte-format" :
                        "evidence.scalar-value";
                }
            }
        }
        else if (evidence is not null && AetherToolBroker.IsEventGroupField(evidence.Field) &&
            claim.Subject == evidence.Field)
        {
            var group = AetherToolBroker.ValidateEventGroup(evidence.Value!);
            rule = "event-group.field";
            if (claim.Operator == "Equals")
            {
                var actual = claim.Property switch
                {
                    "count" => group.Count.ToString(CultureInfo.InvariantCulture),
                    "eventId" => group.EventId.ToString(CultureInfo.InvariantCulture),
                    "provider" => group.Provider,
                    "log" => group.Log,
                    "level" => group.Level?.ToString(CultureInfo.InvariantCulture),
                    _ => null
                };
                var expectedType = claim.Property is "count" or "eventId" or "level" ? "Count" : "Identity";
                var unit = claim.Property is "count" ? "events" :
                    claim.Property is "eventId" ? "id" : "";
                if (actual is not null)
                {
                    expectedValue = actual + (unit.Length == 0 ? "" : " " + unit);
                    valid = claim.ClaimType == expectedType && ClaimEquals(claim, actual, unit);
                }
            }
        }
        else if (evidence is not null && claim.Subject == "evidence" && claim.Property == "availability" &&
            claim.ClaimType == "Status" && claim.Operator == "Equals")
        {
            expectedValue = evidence.Metadata.Availability.ToString();
            valid = ClaimEquals(claim, expectedValue, "");
            rule = "evidence.availability";
        }

        if (valid) return;
        throw RejectedClaim(claim, findingIndex, claimIndex, expectedFields, expectedValue, rule,
            citedEvidence, sourceEvidence,
            "Strukturierter Claim stimmt nicht exakt mit dem zitierten Evidence-Wert überein.");
    }

    internal static string? WindowsIdentityClaimValue(FabricWindowsIdentity identity, string property) =>
        property switch
        {
            "productName" => identity.ProductName,
            "edition" => identity.Edition,
            "release" => identity.Release,
            "build" => identity.Build,
            "architecture" => identity.Architecture,
            _ => null
        };

    internal static string? EvidenceClaimValue(FabricEvidence evidence, out string unit, out string claimType)
    {
        unit = evidence.Unit;
        claimType = "Identity";
        if (evidence.Value is null) return null;
        if (AetherContextManager.IsFormattedByteField(evidence.Field))
        {
            var display = AetherContextManager.FormatByteEvidence(evidence);
            if (TryDisplayParts(display, out var value, out unit))
            {
                claimType = "Measurement";
                return value;
            }
            return null;
        }
        if (evidence.Unit == "Prozent")
        {
            claimType = "Measurement";
            return AetherContextManager.FormatPercentEvidence(evidence);
        }
        if (evidence.Field is "cpuCores" or "logicalProcessors" or "systemErrors24h" or
            "maximumEventRepetitions24h" or "processCount")
        {
            claimType = "Count";
            return evidence.Value;
        }
        if (evidence.Unit.Length > 0) claimType = "Measurement";
        return evidence.Value;
    }

    internal static bool TryDisplayParts(string? display, out string value, out string unit)
    {
        var match = FastMeasurementPattern.Match(display ?? "");
        value = match.Success ? match.Groups["number"].Value : "";
        unit = match.Success ? match.Groups["unit"].Value : "";
        return match.Success;
    }

    internal static bool ContainsMeasurement(string text) => FastMeasurementPattern.IsMatch(text);

    internal static bool ContainsWindowsIdentityStatement(string text) =>
        new[]
        {
            @"(?i)\bWindows\s+(?:10|11)\b",
            @"(?i)\b(?:Edition|Ausgabe)\s*[:=]\s*(?:Home|Pro|Professional|Enterprise|Education|Workstations|IoT|N|Single\s+Language)\b",
            @"(?i)\b\d{2}H[12]\b",
            @"(?i)\bBuild\s*[:#]?\s*\d{4,6}\b",
            @"(?i)\b(?:x64|x86|arm64)\b"
        }.Any(pattern => Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));

    private static bool ClaimEquals(FabricFastAssistantClaim claim, string value, string unit) =>
        claim.Value.Equals(value, StringComparison.Ordinal) &&
        claim.Unit.Equals(unit, StringComparison.OrdinalIgnoreCase);

    // The prompt presents the availability aggregates under these observation keys; a claim using
    // them refers to the same machine-checked value and is canonicalized deterministically.
    internal static string? CanonicalStorageDetailProperty(string property) => property switch
    {
        "manufacturerCount" or "manufacturerUnavailableCount" => "manufacturer",
        "modelCount" or "modelUnavailableCount" => "model",
        "mediumCount" or "mediumUnavailableCount" or "mediaTypeUnavailableCount" => "medium",
        _ => null
    };

    // Registered aliases of the canonical Windows identity properties; the claimed value must still
    // match the canonical evidence exactly before any canonicalization.
    internal static string? CanonicalWindowsIdentityProperty(string property) => property switch
    {
        "product" or "os" or "operatingSystem" or "operatingSystemProductName" or "name" => "productName",
        "osEdition" or "operatingSystemEdition" => "edition",
        "version" or "osVersion" or "operatingSystemVersion" or "displayVersion" => "release",
        "osBuild" or "buildNumber" or "operatingSystemBuild" => "build",
        "arch" or "osArchitecture" => "architecture",
        _ => null
    };

    private static string CanonicalVolumeSubject(string subject) =>
        subject.StartsWith("volumeSummary/drive:", StringComparison.Ordinal)
            ? "volume:" + subject["volumeSummary/drive:".Length..]
            : subject.StartsWith("volumeSummary/", StringComparison.Ordinal) &&
              subject.Length > "volumeSummary/".Length
                ? "volume:" + subject["volumeSummary/".Length..]
                : subject;

    private static bool TryGetStorageTypeCount(FabricPhysicalStorageSummary summary, string property,
        out int count)
    {
        count = 0;
        var separator = property.IndexOf(':');
        if (separator <= 0 || separator == property.Length - 1) return false;
        var category = property[..separator];
        var name = property[(separator + 1)..];
        var values = category switch
        {
            "busType" => summary.BusTypes,
            "mediaType" => summary.MediaTypes,
            "healthStatus" => summary.HealthStatuses,
            _ => ImmutableArray<FabricStorageTypeCount>.Empty
        };
        var match = values.SingleOrDefault(item => item.Name.Equals(name, StringComparison.Ordinal));
        if (match is null) return false;
        count = match.Count;
        return true;
    }

    private static FabricRejectedClaimDiagnostic SchemaClaimDiagnostic(FabricFastAssistantClaim? claim,
        int findingIndex, int claimIndex) =>
        new(findingIndex, claimIndex, claim?.ClaimType ?? "", claim?.Subject ?? "", claim?.Property ?? "",
            claim?.Operator ?? "",
            claim?.Value is { } value
                ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
                : "",
            claim is { Evidence.IsDefault: false } ? claim.Evidence : [],
            [], null, "claim.schema");

    private static FabricContractException RejectedClaim(FabricFastAssistantClaim claim, int findingIndex,
        int claimIndex, ImmutableArray<string> expectedEvidenceFields, string? expectedValue,
        string validatorRule, ImmutableArray<FabricEvidence> citedEvidence,
        ImmutableArray<FabricEvidence> sourceEvidence, string reason, string? displayValue = null,
        string? findingText = null)
    {
        var diagnostic = new FabricRejectedClaimDiagnostic(findingIndex, claimIndex,
            claim.ClaimType, claim.Subject, claim.Property, claim.Operator,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(claim.Value))),
            claim.Evidence.IsDefault ? [] : claim.Evidence,
            expectedEvidenceFields, expectedValue, validatorRule)
        {
            CanonicalValue = findingText is null ? null : expectedValue,
            DisplayValue = displayValue,
            FindingTextHash = findingText is null ? null :
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(findingText)))
        };
        var canonicalVolumeSubject = CanonicalVolumeSubject(claim.Subject);
        var ids = claim.Evidence.IsDefaultOrEmpty
            ? "keine"
            : string.Join(", ", claim.Evidence.Select(id => id.ToString("D")));
        var targetId = citedEvidence.FirstOrDefault()?.TargetId;
        var expectedEvidenceIds = expectedEvidenceFields.IsEmpty || targetId is null
            ? ImmutableArray<Guid>.Empty
            : sourceEvidence.Where(item => item.TargetId == targetId &&
                    expectedEvidenceFields.Contains(item.Field) && IsCurrentEvidence(item, DateTimeOffset.UtcNow))
                .Select(item => item.Id).Distinct().Order().ToImmutableArray();
        var expectedIdsText = expectedEvidenceIds.IsEmpty
            ? "keine"
            : string.Join(", ", expectedEvidenceIds.Select(id => id.ToString("D")));
        var canonicalClaimShape = claim.Subject.StartsWith("volumeSummary/", StringComparison.Ordinal)
            ? $" Verwende subject={canonicalVolumeSubject} und property={claim.Property}."
            : claim.Subject.StartsWith("physicalStorageSummary/", StringComparison.Ordinal)
                ? $" Verwende subject=physicalStorageSummary und property={claim.Subject["physicalStorageSummary/".Length..]}."
                : claim.Property == "value" && claim.Subject.Contains('/', StringComparison.Ordinal)
                    ? $" Verwende subject={claim.Subject[..claim.Subject.LastIndexOf('/')]} und property={claim.Subject[(claim.Subject.LastIndexOf('/') + 1)..]}."
                    : "";
        var storageIndexInstruction = claim.Subject.StartsWith("physicalStorageDevice:",
                StringComparison.Ordinal) && claim.Property == "diskNumber"
            ? " Interne Feldpfade oder Arraypositionen sind keine Datenträgernummern."
            : "";
        var storageProperty = claim.Subject.StartsWith("volumeSummary/", StringComparison.Ordinal)
            ? claim.Property
            : claim.Subject.Contains('/', StringComparison.Ordinal)
                ? claim.Subject[(claim.Subject.LastIndexOf('/') + 1)..]
                : claim.Property;
        var expectedStorageValue = expectedValue is null
            ? "kein passender Wert"
            : expectedValue.EndsWith(" " + claim.Unit, StringComparison.OrdinalIgnoreCase)
                ? expectedValue
                : expectedValue + (claim.Unit.Length == 0 ? "" : " " + claim.Unit);
        var canonicalMeasurement = TryDisplayParts(expectedValue, out var expectedMeasurementValue,
                out var expectedMeasurementUnit)
            ? $" Kanonischer Claim: value={expectedMeasurementValue}; unit={expectedMeasurementUnit}."
            : "";
        var storageValues = expectedEvidenceFields.Any(field =>
                field is "physicalStorageSummary" or "volumeSummary")
            ? $" Erlaubter Storagewert: {expectedEvidenceFields[0]}.{storageProperty}={expectedStorageValue}."
            : "";
        var correctionTarget = storageValues.Length == 0
            ? "diesen Claim"
            : "diesen Storage-Claim";
        var guidance = $"Feld: {claim.Subject}; Claimtyp: {claim.ClaimType}; Property: {claim.Property}; " +
            $"Operator: {claim.Operator}; Modellwert: {claim.Value}; Einheit: {claim.Unit}; EvidenceId: {ids}; " +
            $"tatsächliche Evidence-Felder: {(citedEvidence.IsEmpty ? "keine" : string.Join(", ", citedEvidence.Select(item => item.Field).Distinct()))}; " +
            $"erwartete Evidence-Felder: {(expectedEvidenceFields.IsEmpty ? "keine" : string.Join(", ", expectedEvidenceFields))}; " +
            $"zulässige EvidenceIds: {expectedIdsText}; " +
            $"erwarteter Wert: {expectedValue ?? "kein aktueller passender Beleg"}; Validatorregel: {validatorRule}." +
            storageValues +
            canonicalMeasurement +
            storageIndexInstruction +
            canonicalClaimShape +
            $" Korrigiere ausschließlich {correctionTarget} oder entferne ihn, wenn keine belegte Aussage möglich ist. " +
            "Alle übrigen Befunde, Claims und EvidenceIds unverändert lassen. Kein neuer Evidence-Scope.";
        return new("FastAssistant-Claim", reason, $"findings[{findingIndex}].claims[{claimIndex}]",
            expectedValue, "Claim nicht gedeckt",
            guidance, diagnostic);
    }

    private static void ValidateFastAssistantText(string text, ImmutableArray<FabricEvidence> citedEvidence,
        ImmutableArray<FabricEvidence> allEvidence, ImmutableArray<FabricEvidence> identitySources,
        ImmutableArray<FabricFastAssistantClaim> claims, string claimField)
    {
        if (!claims.IsDefaultOrEmpty)
            try
            {
                ValidateWindowsClaimCoverage(text, claims);
            }
            catch (FabricContractException error) when (
                error.Field?.StartsWith("windowsIdentity.", StringComparison.Ordinal) == true &&
                error.RejectedClaim is null)
            {
                // Data-sparse diagnostics so an uncovered Windows statement is repairable or, after
                // the single repair is spent, removable together with its finding.
                var coverageProperty = error.Field["windowsIdentity.".Length..];
                var coverageFindingIndex = FindingIndexForField(claimField);
                var coverageTextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
                var coverageDiagnostic = new FabricRejectedClaimDiagnostic(coverageFindingIndex ?? -1, -1,
                    "Identity", "windowsIdentity", coverageProperty, "Matches", coverageTextHash,
                    citedEvidence.Select(item => item.Id).Distinct().ToImmutableArray(),
                    ["windowsIdentity"], null, "windows.claim-coverage")
                {
                    FindingTextHash = coverageTextHash
                };
                var coverageGuidance = $"Windows-Identitätsangabe ohne Claim in {claimField}: abgelehnte Aussage: „{text}“. " +
                    $"Ergänze einen strukturierten Identity-Claim für windowsIdentity.{coverageProperty} " +
                    "oder entferne die Angabe aus diesem Text. " +
                    "Bewahre alle übrigen Befunde samt IDs unverändert; kein neuer Evidence-Scope.";
                throw new FabricContractException(error.Stage, error.Reason, error.Field, error.Expected, error.Actual,
                    coverageGuidance, coverageDiagnostic, coverageFindingIndex);
            }
        try
        {
            ValidateFastAssistantStorageText(text, citedEvidence, identitySources, claims);
            ValidateFastAssistantMeasurements(text, citedEvidence, identitySources, claims);
        }
        catch (FabricContractException error) when (error.Field is "physicalStorageSummary" or "volumeSummary")
        {
            var ids = citedEvidence.Select(item => item.Id).Distinct().Order().ToArray();
            var detected = FastMeasurementPattern.Matches(text).Select(match =>
                $"{match.Groups["number"].Value} {match.Groups["unit"].Value}").ToList();
            var diskNumber = Regex.Match(text,
                @"(?i)\b(?:Datenträger|Festplatte|physical\s+drive|disk|drive|SSD|HDD|NVMe)\s*(?:Nummer|Nr\.?|#)?\s*(?<number>\d{1,3})\b",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (diskNumber.Success) detected.Add("Datenträgernummer " + diskNumber.Groups["number"].Value);
            var allowed = StorageAllowedValues(citedEvidence, identitySources, error.Field);
            var citedSummary = error.Field == "physicalStorageSummary"
                ? citedEvidence.Any(item => item.Field == "physicalStorageSummary")
                : citedEvidence.Any(item => item.Field == "volumeSummary");
            var repairGuidance =
                $"Storagefehler {error.Field} in {claimField}: „{text}“; erkannte Zahl/Einheit: " +
                $"{(detected.Count == 0 ? "keine" : string.Join(", ", detected.Distinct()))}; zitierte IDs/Felder: " +
                $"{(ids.Length == 0 ? "keine" : string.Join(", ", citedEvidence.Select(item => $"{item.Id:D}={item.Field}").Distinct()))}; " +
                $"erlaubte Werte/Einheiten dieser IDs: {allowed}. Interne Feldpfade/Arrayindizes sind keine Datenträgernummern. " +
                $"{(citedSummary ? "Korrigiere nur diesen Storagebefund." : "Entferne nur diesen Storagebefund; erfinde keinen neuen Beleg.")} " +
                "Bewahre alle übrigen Befunde samt IDs unverändert; keine Umrechnung und kein neuer Evidence-Scope.";
            var storageFindingIndex = FindingIndexForField(claimField);
            var storageRejected = error.RejectedClaim;
            if (storageRejected is null && storageFindingIndex is { } storageIndex && error.Actual != "fehlend")
            {
                // Data-sparse diagnostics so a storage wording error on validated claims becomes a
                // focused text repair instead of a full-answer rewrite.
                var storageTextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
                storageRejected = new FabricRejectedClaimDiagnostic(storageIndex, -1, "Measurement",
                    error.Field!, "text", "Matches", storageTextHash,
                    citedEvidence.Select(item => item.Id).Distinct().ToImmutableArray(),
                    citedEvidence.Select(item => item.Field).Distinct().ToImmutableArray(),
                    error.Expected, "storage.text-coverage")
                {
                    FindingTextHash = storageTextHash
                };
            }
            throw new FabricContractException(error.Stage, error.Reason, error.Field, error.Expected, error.Actual,
                repairGuidance, storageRejected, storageFindingIndex);
        }
        catch (FabricContractException error) when (error.Field == "findings[].text" && error.RejectedClaim is null)
        {
            // Data-sparse reconstruction for free-text measurement rejections: only hashes of the
            // model wording are persisted; allowed values are deterministic evidence displays.
            var detected = FastMeasurementPattern.Matches(text).Select(match =>
                $"{match.Groups["number"].Value} {match.Groups["unit"].Value}").Distinct().ToArray();
            // Allowed values are compact deterministic displays only; structured evidence JSON,
            // internal IDs, and raw byte values never enter guidance or diagnostics.
            var allowed = citedEvidence
                .Where(item => IsCurrentEvidence(item, DateTimeOffset.UtcNow))
                .Select(item =>
                    AetherContextManager.IsFormattedByteField(item.Field)
                        ? $"{item.Field}={AetherContextManager.FormatByteEvidence(item)}"
                        : item.Unit == "Prozent"
                            ? $"{item.Field}={AetherContextManager.FormatPercentEvidence(item)} %"
                            : item.Field == "physicalStorageSummary"
                                ? "physicalStorageSummary.totalCapacity=" +
                                  (AetherContextManager.ReadPhysicalStorageSummary(item, identitySources)
                                      .TotalCapacityDisplay ?? "nicht verfügbar")
                                : item.Field == "volumeSummary"
                                    ? "volumeSummary=" + string.Join("|",
                                        AetherContextManager.ReadVolumeSummary(item, identitySources).Volumes
                                            .Select(volume => $"{volume.Drive} Kapazität {volume.CapacityDisplay ?? "-"}, " +
                                                $"frei {volume.FreeDisplay ?? "-"}, belegt {volume.UsedDisplay ?? "-"}"))
                                    : item.Value is { Length: <= 32 } scalarValue && !scalarValue.StartsWith('{')
                                        ? $"{item.Field}={scalarValue}{(item.Unit.Length == 0 ? "" : " " + item.Unit)}"
                                        : null)
                .Where(value => value is not null).Select(value => value!).Distinct().ToArray();
            var findingIndex = FindingIndexForField(claimField);
            var diagnostic = new FabricRejectedClaimDiagnostic(findingIndex ?? -1, -1, "Measurement",
                "findingText", "measurement", "Matches",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("; ", detected)))),
                citedEvidence.Select(item => item.Id).Distinct().ToImmutableArray(),
                citedEvidence.Select(item => item.Field).Distinct().ToImmutableArray(),
                allowed.Length == 0 ? null : string.Join("; ", allowed), "measurement.text-coverage")
            {
                FindingTextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            };
            var repairGuidance = $"Messwertfehler in {claimField}: abgelehnte Aussage: „{text}“; " +
                $"erkannte Zahl/Einheit: {(detected.Length == 0 ? "keine" : string.Join(", ", detected))}; " +
                $"zitierte IDs/Felder: {(citedEvidence.IsEmpty ? "keine" : string.Join(", ", citedEvidence.Select(item => $"{item.Id:D}={item.Field}").Distinct()))}; " +
                $"erlaubte vorformatierte Werte dieser IDs: {(allowed.Length == 0 ? "keine" : string.Join(", ", allowed))}. " +
                "Übernimm Messwerte exakt aus den zitierten Belegen samt passendem Measurement-Claim " +
                "oder entferne die unbelegte Zahl aus diesem Text. Keine Umrechnung. " +
                "Bewahre alle übrigen Befunde samt IDs unverändert; kein neuer Evidence-Scope.";
            throw new FabricContractException(error.Stage, error.Reason, error.Field, error.Expected, error.Actual,
                repairGuidance, diagnostic, findingIndex);
        }

        static void ValidateWindowsClaimCoverage(string text, ImmutableArray<FabricFastAssistantClaim> claims)
        {
            var requirements = new (string Pattern, string Property, string[] ScalarFields)[]
            {
                (@"(?i)\bWindows\s+(?:10|11)\b", "productName", ["operatingSystemProductName", "operatingSystem"]),
                (@"(?i)\b(?:Edition|Ausgabe)\s*[:=]\s*(?:Home|Pro|Professional|Enterprise|Education|Workstations|IoT|N|Single\s+Language)\b",
                    "edition", ["operatingSystemEdition"]),
                (@"(?i)\b\d{2}H[12]\b", "release", ["operatingSystemVersion"]),
                (@"(?i)\bBuild\s*[:#]?\s*\d{4,6}\b", "build", ["operatingSystemBuild"]),
                (@"(?i)\b(?:x64|x86|arm64)\b", "architecture", ["architecture"])
            };
            foreach (var (pattern, property, scalarFields) in requirements)
            {
                if (!Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    continue;
                if (claims.Any(claim => claim.ClaimType == "Identity" && claim.Operator == "Equals" &&
                    ((claim.Subject == "windowsIdentity" && claim.Property == property) ||
                     (scalarFields.Contains(claim.Subject) && claim.Property == "value"))))
                    continue;
                throw new FabricContractException("Belegvalidierung",
                    "Windows-Identitätsangabe benötigt einen zugehörigen strukturierten Claim.",
                    "windowsIdentity." + property);
            }
        }

        try
        {
            ValidateWindowsProduct(text, citedEvidence, identitySources, validateEdition: true);
            ValidateWindowsEdition(text, citedEvidence, identitySources);
            ValidateWindowsRelease(text, citedEvidence, identitySources);
            ValidateIdentity(text, citedEvidence, identitySources);
        }
        catch (FabricContractException error) when (error.Field is
            "windowsProductName" or "windowsEdition" or "operatingSystemVersion" or
            "operatingSystemBuild" or "architecture")
        {
            var expected = WindowsExpectedValue(error.Field, allEvidence, identitySources);
            var ids = WindowsEvidenceIds(error.Field, allEvidence, identitySources);
            var repairGuidance = $"Feld: {error.Field}; Antwortfeld: {claimField}; abgelehnte Aussage: „{text}“; " +
                $"erwarteter Wert: {expected ?? "kein aktueller Einzelbeleg"}; zulässige EvidenceId(s): " +
                (ids.IsEmpty ? "keine" : string.Join(", ", ids.Select(id => id.ToString("D")))) +
                (claimField is "summary" or "recommendations[]"
                    ? ". Verschiebe diese Windows-Identitätsaussage in einen findings[].text-Eintrag mit den genannten IDs, " +
                      "oder entferne sie aus summary beziehungsweise recommendations."
                    : ". Korrigiere ausschließlich diese Aussage; zitiere mindestens eine der genannten IDs " +
                      "oder entferne die unbelegte Identitätsangabe.");
            // Data-sparse diagnostics so unbacked Windows statements become deterministic
            // neutralizations (summary/recommendations) or focused text repairs (findings).
            var windowsFindingIndex = FindingIndexForField(claimField);
            var windowsTextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            var windowsDiagnostic = new FabricRejectedClaimDiagnostic(windowsFindingIndex ?? -1, -1,
                "Identity", "windowsIdentity", error.Field!, "Matches", windowsTextHash,
                citedEvidence.Select(item => item.Id).Distinct().ToImmutableArray(),
                ["windowsIdentity"], expected, "windows.text-coverage")
            {
                FindingTextHash = windowsTextHash
            };
            throw new FabricContractException(error.Stage, error.Reason, error.Field, error.Expected, error.Actual,
                repairGuidance, windowsDiagnostic, windowsFindingIndex);
        }
        catch (FabricContractException error) when (error.Field == "eventGroup" && error.RejectedClaim is null)
        {
            // Data-sparse diagnostics so an unbacked event count is repairable or, after the single
            // repair is spent, removable together with its finding.
            var eventFindingIndex = FindingIndexForField(claimField);
            var eventTextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            var scalarCounts = citedEvidence
                .Where(item => item.Field is "systemErrors24h" or "maximumEventRepetitions24h" &&
                    IsCurrentEvidence(item, DateTimeOffset.UtcNow) && item.Value is { Length: <= 16 })
                .Select(item => $"{item.Field}={item.Value}").Distinct().ToArray();
            var eventDiagnostic = new FabricRejectedClaimDiagnostic(eventFindingIndex ?? -1, -1, "Count",
                "eventGroup", "count", "Matches", eventTextHash,
                citedEvidence.Select(item => item.Id).Distinct().ToImmutableArray(),
                citedEvidence.Select(item => item.Field).Distinct().ToImmutableArray(),
                scalarCounts.Length == 0 ? null : string.Join("; ", scalarCounts), "event.count-coverage")
            {
                FindingTextHash = eventTextHash
            };
            var eventGuidance = $"Ereigniszahl ohne passenden Beleg in {claimField}: abgelehnte Aussage: „{text}“; " +
                $"zitierte IDs/Felder: {(citedEvidence.IsEmpty ? "keine" : string.Join(", ", citedEvidence.Select(item => $"{item.Id:D}={item.Field}").Distinct()))}; " +
                $"belegte Zählwerte: {(scalarCounts.Length == 0 ? "keine" : string.Join(", ", scalarCounts))}. " +
                "Nenne nur belegte Ereigniszahlen samt Provider und Ereignis-ID oder entferne die Zahl aus diesem Text. " +
                "Bewahre alle übrigen Befunde samt IDs unverändert; kein neuer Evidence-Scope.";
            throw new FabricContractException(error.Stage, error.Reason, error.Field, error.Expected, error.Actual,
                eventGuidance, eventDiagnostic, eventFindingIndex);
        }
    }

    private static void ValidateFastAssistantStorageText(string text, ImmutableArray<FabricEvidence> citedEvidence,
        ImmutableArray<FabricEvidence> sourceEvidence, ImmutableArray<FabricFastAssistantClaim> claims)
    {
        var physicalItems = citedEvidence.Where(item => item.Field == "physicalStorageSummary").ToArray();
        var volumeItems = citedEvidence.Where(item => item.Field == "volumeSummary").ToArray();
        if (physicalItems.Length > 1)
            throw StorageError("physicalStorageSummary", "Storagebefund zitiert mehrere physische Summary-Belege.",
            "ein eindeutiger Summary-Beleg", physicalItems.Length.ToString(CultureInfo.InvariantCulture));
        if (volumeItems.Length > 1)
            throw StorageError("volumeSummary", "Storagebefund zitiert mehrere Volume-Summary-Belege.",
            "ein eindeutiger Summary-Beleg", volumeItems.Length.ToString(CultureInfo.InvariantCulture));

        var physicalEvidence = physicalItems.SingleOrDefault();
        var volumeEvidence = volumeItems.SingleOrDefault();
        var physical = physicalEvidence is null ? null :
            AetherContextManager.ReadPhysicalStorageSummary(physicalEvidence, sourceEvidence);
        var volumes = volumeEvidence is null ? null :
            AetherContextManager.ReadVolumeSummary(volumeEvidence, sourceEvidence);

        var fieldPath = Regex.Match(text, @"(?i)\b(?<field>physicalDrive|volume)\.\d+\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (fieldPath.Success)
            throw StorageError(fieldPath.Groups["field"].Value.Equals("volume", StringComparison.OrdinalIgnoreCase)
                ? "volumeSummary" : "physicalStorageSummary",
            "Interner Storage-Feldindex ist keine zitierbare Datenträger- oder Laufwerksnummer.",
            "aggregierter Summary-Wert ohne Feldindex", fieldPath.Value);

        var diskNumber = Regex.Match(text,
            @"(?i)\b(?:Datenträger|Festplatte|physical\s+drive|disk|drive|SSD|HDD|NVMe)\s*(?:Nummer|Nr\.?|#)?\s*(?<number>\d{1,3})\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (diskNumber.Success && !claims.Any(claim =>
            claim.ClaimType == "Identity" && claim.Subject.StartsWith("physicalStorageDevice:",
                StringComparison.Ordinal) && claim.Property == "diskNumber" &&
            claim.Operator == "Equals" && claim.Value == diskNumber.Groups["number"].Value))
            throw StorageError("physicalStorageSummary",
            "Datenträgernummer ist nicht im kanonischen Aggregatbeleg enthalten.",
            "explizit gemessene Datenträgernummer mit passendem Geräte-Claim",
            diskNumber.Groups["number"].Value);

        var hasPhysicalClaim = HasPhysicalStorageTerms(text);
        var hasVolumeClaim = HasVolumeTerms(text);
        if (hasPhysicalClaim && physical is null)
            throw StorageError("physicalStorageSummary",
            "Physische Storageaussage benötigt den zitierten physicalStorageSummary-Beleg.",
            "zitierten physicalStorageSummary-Beleg", "fehlend");
        if (hasVolumeClaim && volumes is null)
            throw StorageError("volumeSummary",
            "Volumeaussage benötigt den zitierten volumeSummary-Beleg.",
            "zitierten volumeSummary-Beleg", "fehlend");
        if (physical is not null && (hasPhysicalClaim || claims.Any(claim =>
            claim.Subject == "physicalStorageSummary" ||
            claim.Subject.StartsWith("physicalStorageDevice:", StringComparison.Ordinal))))
            ValidatePhysicalStorageClaims(text, physical, claims);
        if (volumes is not null && hasVolumeClaim)
            ValidateVolumeClaims(text, volumes);
    }

    private static void ValidateFastAssistantMeasurements(string text, ImmutableArray<FabricEvidence> citedEvidence,
        ImmutableArray<FabricEvidence> sourceEvidence,
        ImmutableArray<FabricFastAssistantClaim> claims = default)
    {
        foreach (Match match in FastMeasurementPattern.Matches(text))
        {
            var segment = MeasurementSegment(text, match);
            var physical = HasPhysicalStorageTerms(segment);
            var volume = HasVolumeTerms(segment);
            var memory = Regex.IsMatch(segment, @"(?i)\b(?:RAM|Arbeitsspeicher|VRAM|Grafikspeicher)\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!claims.IsDefaultOrEmpty && claims.Any(claim =>
                    claim.ClaimType == "Measurement" && claim.Operator == "Equals" &&
                    ClaimMatchesMeasurement(match, claim) &&
                    (physical
                        ? claim.Subject == "physicalStorageSummary" ||
                          claim.Subject.StartsWith("physicalStorageDevice:", StringComparison.Ordinal)
                        : volume
                            ? claim.Subject.StartsWith("volume:", StringComparison.Ordinal) ||
                              claim.Subject == "volumeSummary"
                            : claim.Subject is not ("physicalStorageSummary" or "volumeSummary") &&
                              !claim.Subject.StartsWith("physicalStorageDevice:", StringComparison.Ordinal) &&
                              !claim.Subject.StartsWith("volume:", StringComparison.Ordinal))))
                continue;
            var physicalEvidence = citedEvidence.FirstOrDefault(item => item.Field == "physicalStorageSummary");
            var volumeEvidence = citedEvidence.FirstOrDefault(item => item.Field == "volumeSummary");
            if (memory && !physical && !volume)
            {
            ValidateMeasurements(match.Value, citedEvidence);
            continue;
            }
            if (!physical && !volume)
            {
            if (physicalEvidence is not null && volumeEvidence is not null)
                throw StorageError("physicalStorageSummary",
                    "Messwert ist ohne Storage-Kategorie mehrdeutig; physische Kapazität und Volumeplatz sind getrennt.",
                    "eindeutige Kategorie im Befund", match.Value);
            physical = physicalEvidence is not null;
            volume = volumeEvidence is not null;
            }
            if (physical && volume)
            throw StorageError("volumeSummary",
                "Physische Kapazität und Volumeplatz dürfen nicht im selben Messwert vermischt werden.",
                "getrennte physische beziehungsweise Volume-Aussage", match.Value);
            if (physical)
            {
            if (physicalEvidence is null)
                throw StorageError("physicalStorageSummary",
                    "Physischer Messwert benötigt den zitierten physicalStorageSummary-Beleg.",
                    "physicalStorageSummary", match.Value);
            var summary = AetherContextManager.ReadPhysicalStorageSummary(physicalEvidence, sourceEvidence);
            // The physical summary exposes exactly one measured aggregate (total capacity). The
            // value must match it exactly; free/used wordings would misattribute the number.
            var misattributed = Regex.IsMatch(segment,
                @"(?i)\b(?:frei\w*|belegt\w*|verfügbar\w*|genutzt\w*|free|used)\b",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (misattributed ||
                summary.TotalCapacityDisplay is null ||
                !MeasurementMatches(match, summary.TotalCapacityDisplay))
                throw StorageError("physicalStorageSummary",
                    "Physische Kapazität stimmt nicht exakt mit dem vorformatierten Summarywert überein.",
                    summary.TotalCapacityDisplay ?? "keine physische Gesamtkapazität verfügbar", match.Value);
            continue;
            }
            if (volume)
            {
            if (volumeEvidence is null)
                throw StorageError("volumeSummary",
                    "Volume-Messwert benötigt den zitierten volumeSummary-Beleg.",
                    "volumeSummary", match.Value);
            var summary = AetherContextManager.ReadVolumeSummary(volumeEvidence, sourceEvidence);
            var driveMatches = Regex.Matches(segment, @"(?i)\b(?<drive>[A-Z]):(?:\\)?",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var free = Regex.IsMatch(segment, @"(?i)\b(?:frei|freier|freie|free)\b",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var used = Regex.IsMatch(segment, @"(?i)\b(?:belegt|belegte|belegter|used)\b",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var capacity = Regex.IsMatch(segment, @"(?i)\b(?:kapazität|gesamtkapazität|gesamt|capacity)\b",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (driveMatches.Count != 1 || (free ? 1 : 0) + (used ? 1 : 0) + (capacity ? 1 : 0) != 1)
                throw StorageError("volumeSummary",
                    "Volume-Messwert benötigt genau einen Laufwerksbuchstaben und genau eine Messwertart.",
                    "Volume-Laufwerk sowie frei, belegt oder Kapazität", match.Value);
            var drive = driveMatches[0].Groups["drive"].Value + ":\\";
            var item = summary.Volumes.SingleOrDefault(value =>
                string.Equals(value.Drive, drive, StringComparison.OrdinalIgnoreCase));
            var allowed = item is null ? null : free ? item.FreeDisplay : used ? item.UsedDisplay : item.CapacityDisplay;
            if (allowed is null || !MeasurementMatches(match, allowed))
                throw StorageError("volumeSummary",
                    "Volume-Messwert oder Einheit stimmt nicht exakt mit dem zitierten Volume-Beleg überein.",
                    allowed ?? "kein passender Laufwerksbeleg", match.Value);
            continue;
            }
            ValidateMeasurements(match.Value, citedEvidence);
        }
    }

    private static bool ClaimMatchesMeasurement(Match measurement, FabricFastAssistantClaim claim) =>
        decimal.TryParse(measurement.Groups["number"].Value.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var actual) &&
        decimal.TryParse(claim.Value.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var expected) &&
        actual == expected &&
        measurement.Groups["unit"].Value.Equals(claim.Unit, StringComparison.OrdinalIgnoreCase);

    private static void ValidatePhysicalStorageClaims(string text, FabricPhysicalStorageSummary summary,
        ImmutableArray<FabricFastAssistantClaim> claims)
    {
        foreach (Match match in Regex.Matches(text,
                 @"(?i)\b(?<count>\d+)\s+(?:physische(?:n|r)?\s+)?(?:Datenträger|Speichergeräte|Gerät(?:e|en)?|Disks|physical\s+drives)\b",
                 RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            if (!int.TryParse(match.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            count != summary.DeviceCount)
            throw StorageError("physicalStorageSummary", "Geräteanzahl widerspricht dem physischen Summary-Beleg.",
                summary.DeviceCount.ToString(CultureInfo.InvariantCulture), match.Groups["count"].Value);
        foreach (Match match in Regex.Matches(text,
                 @"(?i)\b(?:Geräteanzahl|Datenträgeranzahl|Anzahl\s+(?:der\s+)?(?:physischen\s+)?(?:Datenträger|Geräte))\s*[:=]?\s*(?<count>\d+)\b",
                 RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            if (!int.TryParse(match.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            count != summary.DeviceCount)
            throw StorageError("physicalStorageSummary", "Geräteanzahl widerspricht dem physischen Summary-Beleg.",
                summary.DeviceCount.ToString(CultureInfo.InvariantCulture), match.Groups["count"].Value);

        var numberAndType = Regex.Matches(text,
            @"(?i)\b(?<count>\d+)\s+(?<type>NVMe|USB|SCSI|SATA|SAS|SSD|HDD|SCM)\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var typeAndNumber = Regex.Matches(text,
            @"(?i)\b(?<type>NVMe|USB|SCSI|SATA|SAS|SSD|HDD|SCM)(?:[- ](?:Geräte|Devices))?\s*[:=]\s*(?<count>\d+)\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        foreach (var match in numberAndType.Cast<Match>().Concat(typeAndNumber.Cast<Match>()))
        {
            var countText = match.Groups["count"].Value;
            var typeText = match.Groups["type"].Value;
            var count = int.Parse(countText, CultureInfo.InvariantCulture);
            var groups = typeText.ToUpperInvariant() is "SSD" or "HDD" or "SCM"
            ? summary.MediaTypes : summary.BusTypes;
            var expected = groups.FirstOrDefault(item =>
            item.Name.Equals(typeText, StringComparison.OrdinalIgnoreCase))?.Count;
            if (expected != count)
            throw StorageError("physicalStorageSummary", "Storage-Typanzahl widerspricht dem Aggregatbeleg.",
                expected?.ToString(CultureInfo.InvariantCulture) ?? "Typ nicht belegt", match.Value);
        }

        ValidateStorageTypeNames(text, summary.BusTypes.Concat(summary.MediaTypes)
            .Concat(summary.HealthStatuses).Where(item => !item.Name.Equals("Nicht gemeldet", StringComparison.Ordinal)),
            "physicalStorageSummary");
        ValidateUnavailableDetail(text, "Hersteller", "manufacturer", summary.ManufacturerUnavailableCount,
            summary.DeviceCount, claims);
        ValidateUnavailableDetail(text, "Modell", "model", summary.ModelUnavailableCount,
            summary.DeviceCount, claims);
        ValidateUnavailableDetail(text, "Medium", "medium", summary.MediaTypeUnavailableCount,
            summary.DeviceCount, claims);
        if (Regex.IsMatch(text, @"(?i)\bHealthStatus\s*[:=]\s*\d+\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw StorageError("physicalStorageSummary", "Windows-Storagecodes sind kein freigegebener Modellmesswert.",
            "aggregierter Healthstatus", "roher Statuscode");
    }

    private static void ValidateVolumeClaims(string text, FabricVolumeSummary summary)
    {
        foreach (Match match in Regex.Matches(text, @"(?i)\b(?<count>\d+)\s+Volumes?\b",
                 RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            if (!int.TryParse(match.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            count != summary.Volumes.Length)
            throw StorageError("volumeSummary", "Volumeanzahl widerspricht dem Volume-Summary-Beleg.",
                summary.Volumes.Length.ToString(CultureInfo.InvariantCulture), match.Groups["count"].Value);
        foreach (Match match in Regex.Matches(text,
                 @"(?i)\bVolumeanzahl\s*[:=]?\s*(?<count>\d+)\b",
                 RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            if (!int.TryParse(match.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            count != summary.Volumes.Length)
            throw StorageError("volumeSummary", "Volumeanzahl widerspricht dem Volume-Summary-Beleg.",
                summary.Volumes.Length.ToString(CultureInfo.InvariantCulture), match.Groups["count"].Value);
    }

    private static void ValidateStorageTypeNames(string text, IEnumerable<FabricStorageTypeCount> types, string field)
    {
        foreach (var name in new[] { "NVMe", "USB", "SCSI", "SATA", "SAS", "SSD", "HDD", "SCM",
                 "gesund", "warnung", "ungesund" })
        {
            if (!Regex.IsMatch(text, $@"(?i)\b{Regex.Escape(name)}\b",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            continue;
            if (!types.Any(item => item.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            throw StorageError(field, "Storage-Typ oder -Status ist im kanonischen Summary nicht enthalten.",
                "gemessener Aggregattyp oder Status", name);
        }
    }

    private static void ValidateUnavailableDetail(string text, string label, string property, int unavailableCount,
        int deviceCount, ImmutableArray<FabricFastAssistantClaim> claims)
    {
        foreach (var segment in Regex.Split(text, @"(?i)(?<!\d),(?!\d)|[;!?]|\.(?!\d)|\b(?:aber|jedoch|während)\b",
                     RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            ValidateUnavailableDetailSegment(segment, label, property, unavailableCount, deviceCount, claims);
    }

    private static void ValidateUnavailableDetailSegment(string text, string label, string property, int unavailableCount,
        int deviceCount, ImmutableArray<FabricFastAssistantClaim> claims)
    {
        var mention = Regex.IsMatch(text, $@"(?i)\b{Regex.Escape(label)}\w*\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!mention) return;
        var unavailable = Regex.IsMatch(text,
            @"(?i)\b(?:nicht verfügbar|nicht angegeben|nicht spezifiziert|nicht erfasst|nicht gemeldet|nicht zugeordnet|fehlt|fehlen|unbekannt|nicht bekannt)\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var summaryClaim = claims.Any(claim => claim.ClaimType == "Availability" &&
            claim.Subject == "physicalStorageSummary" && claim.Property == property &&
            claim.Operator == "UnavailableCount" && claim.Unit == "devices" &&
            claim.Value == unavailableCount.ToString(CultureInfo.InvariantCulture));
        var deviceClaims = claims.Where(claim =>
            claim.Subject.StartsWith("physicalStorageDevice:", StringComparison.Ordinal) &&
            claim.Property == property).ToArray();
        var supportedPositive = deviceClaims.Any(claim =>
            claim.ClaimType == "Availability" && claim.Operator == "Equals" && claim.Value == "Available" ||
            claim.ClaimType == "Identity" && claim.Operator == "Equals" &&
            text.Contains(claim.Value, StringComparison.OrdinalIgnoreCase));
        if (!supportedPositive && Regex.IsMatch(text,
            $@"(?i)\b{Regex.Escape(label)}\w*\s+(?:(?:ist|sind|war|waren)\s+)?(?:verfügbar|bekannt|vorhanden|angegeben|erfasst|gemeldet|zugeordnet)\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw StorageError("physicalStorageSummary",
                $"{label}verfügbarkeit ist nicht positiv belegt.",
                "positiver Verfügbarkeits- oder Identitätsbeleg", label + " wird als verfügbar behauptet");
        if (unavailable)
        {
                if (unavailableCount > 0 && summaryClaim)
                {
                    if (Regex.IsMatch(text, @"(?i)\b(?:USB|NVMe|SCSI|SATA|SAS|SSD|HDD|SCM)\b",
                        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) &&
                        !deviceClaims.Any(claim => claim.ClaimType == "Availability" &&
                            claim.Operator == "Equals" && claim.Value is "Unavailable" or "Unknown"))
                        throw StorageError("physicalStorageSummary",
                            $"{label}verfügbarkeit ist im Aggregatbeleg nicht einem einzelnen Bustyp oder Medium zugeordnet.",
                            "aggregierte Anzahl ohne Gerätezuordnung oder Claim für ein konkretes Gerät",
                            label + " einer Gerätegruppe zugeordnet");
                    var ratio = Regex.Match(text, @"(?i)\b(?<missing>\d+)\s+von\s+(?<total>\d+)\b",
                        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                    if (ratio.Success &&
                        (!int.TryParse(ratio.Groups["missing"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var missing) ||
                         !int.TryParse(ratio.Groups["total"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var total) ||
                         missing != unavailableCount || total != deviceCount))
                        throw StorageError("physicalStorageSummary",
                            $"{label}verfügbarkeit widerspricht dem kanonischen Summary.",
                            $"{unavailableCount} von {deviceCount}", $"{ratio.Groups["missing"].Value} von {ratio.Groups["total"].Value}");
                    return;
                }
                if (deviceClaims.Any(claim => claim.ClaimType == "Availability" &&
                    claim.Operator == "Equals" && claim.Value is "Unavailable" or "Unknown"))
                    return;
                throw StorageError("physicalStorageSummary",
                    $"{label}angabe ist nicht durch die Verfügbarkeitszählung des Summarys gedeckt.",
                    $"{unavailableCount} von {deviceCount} nicht verfügbar", label + " wird behauptet");
        }
        if (supportedPositive) return;
        if (!unavailable || unavailableCount == 0 || !summaryClaim)
            throw StorageError("physicalStorageSummary",
            $"{label}angabe ist nicht durch die Verfügbarkeitszählung des Summarys gedeckt.",
            $"{unavailableCount} von {deviceCount} nicht verfügbar", label + " wird behauptet");
    }

    private static int? FindingIndexForField(string field)
    {
        const string prefix = "findings[";
        if (!field.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var end = field.IndexOf(']', prefix.Length);
        return end > prefix.Length &&
            int.TryParse(field.AsSpan(prefix.Length, end - prefix.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out var index)
                ? index
                : null;
    }

    private static string StorageAllowedValues(ImmutableArray<FabricEvidence> citedEvidence,
        ImmutableArray<FabricEvidence> sourceEvidence, string field)
    {
        var allowed = new List<string>();
        var applicableField = field == "physicalStorageSummary" ? "physicalStorageSummary" : "volumeSummary";
        foreach (var item in citedEvidence.Where(item => item.Field == applicableField))
        {
            if (item.Field == "physicalStorageSummary")
            {
            var summary = AetherContextManager.ReadPhysicalStorageSummary(item, sourceEvidence);
            var diskNumbers = summary.Devices.Where(device =>
                    device.DiskNumberAvailability == FabricFieldAvailability.Available && device.DiskNumber is not null)
                .Select(device => device.DiskNumber!.Value.ToString(CultureInfo.InvariantCulture)).ToArray();
            allowed.Add($"physicalStorageSummary: Geräte={summary.DeviceCount}; Gesamtkapazität={summary.TotalCapacityDisplay ?? "nicht verfügbar"}; " +
                $"Bustypen={string.Join(", ", summary.BusTypes.Select(value => $"{value.Name}×{value.Count}"))}; " +
                $"Medientypen={string.Join(", ", summary.MediaTypes.Select(value => $"{value.Name}×{value.Count}"))}; " +
                $"Health={string.Join(", ", summary.HealthStatuses.Select(value => $"{value.Name}×{value.Count}"))}; " +
                $"Hersteller/Modell/Medium nicht verfügbar={summary.ManufacturerUnavailableCount}/{summary.ModelUnavailableCount}/{summary.MediaTypeUnavailableCount} von {summary.DeviceCount}; " +
                $"Datenträgernummern={(diskNumbers.Length == 0 ? "keine verfügbar" : string.Join(", ", diskNumbers))}");
            }
            else if (item.Field == "volumeSummary")
            {
            var summary = AetherContextManager.ReadVolumeSummary(item, sourceEvidence);
            allowed.Add("volumeSummary: " + string.Join("; ", summary.Volumes.Select(volume =>
                $"{volume.Drive} {volume.FileSystem}: Kapazität {volume.CapacityDisplay}, belegt {volume.UsedDisplay}, frei {volume.FreeDisplay}")));
            }
        }
        if (allowed.Count > 0) return string.Join(" | ", allowed);
        return field == "physicalStorageSummary"
            ? "keine: keine zitierte physicalStorageSummary-EvidenceId"
            : "keine: keine zitierte volumeSummary-EvidenceId";
    }

    private static string MeasurementSegment(string text, Match match)
    {
        static bool IsBoundary(string value, int index) =>
            value[index] is ';' or '!' or '?' or '\r' or '\n' ||
            value[index] == '.' && (index + 1 == value.Length || char.IsWhiteSpace(value[index + 1]));

        var start = 0;
        for (var index = match.Index - 1; index >= 0; index--)
            if (IsBoundary(text, index)) { start = index + 1; break; }
        var end = text.Length;
        for (var index = match.Index + match.Length; index < text.Length; index++)
            if (IsBoundary(text, index)) { end = index; break; }
        return text[start..end];
    }

    private static bool MeasurementMatches(Match claim, string display)
    {
        var expected = FastMeasurementPattern.Match(display);
        if (!expected.Success ||
            !decimal.TryParse(claim.Groups["number"].Value.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var claimed) ||
            !decimal.TryParse(expected.Groups["number"].Value.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var measured))
            return false;
        return claimed == measured &&
            claim.Groups["unit"].Value.Equals(expected.Groups["unit"].Value, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasPhysicalStorageTerms(string text) =>
        Regex.IsMatch(text,
            @"(?i)\b(?:physisch\w*|Datenträger\w*|Festplatte\w*|Speicherinventar\w*|Speichergerät\w*|SSD|HDD|NVMe|USB|Hersteller\w*|Medientyp\w*|Healthstatus\w*)\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static bool HasVolumeTerms(string text) =>
        Regex.IsMatch(text,
            @"(?i)\b(?:Volume\w*|Laufwerk\w*|Dateisystem\w*)\b|(?i)\b[A-Z]:\\?",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ||
        (!Regex.IsMatch(text, @"(?i)\b(?:RAM|Arbeitsspeicher|VRAM|Grafikspeicher)\b",
             RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) &&
         Regex.IsMatch(text, @"(?i)\b(?:frei\w*|free)\b",
             RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));

    private static FabricContractException StorageError(string field, string reason, string expected, string actual) =>
        new("Belegvalidierung", reason, field, expected, actual);

    internal static void ValidateWindowsEdition(string text, ImmutableArray<FabricEvidence> evidence,
        ImmutableArray<FabricEvidence> identitySources = default)
    {
        var identity = CitedWindowsIdentity(evidence, identitySources.IsDefault ? evidence : identitySources);
        var edition = identity?.Edition ?? evidence.FirstOrDefault(e => e.Field == "operatingSystemEdition" &&
            IsCurrentEvidence(e, DateTimeOffset.UtcNow))?.Value;
        foreach (Match match in Regex.Matches(text,
                     @"(?i)\b(?:Edition|Ausgabe)\s*[:=]\s*(?<edition>Home|Pro|Professional|Enterprise|Education|Workstations|IoT|N|Single\s+Language)\b",
                     RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            if (edition is null || (!string.Equals(edition, match.Groups["edition"].Value, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(AetherIdentityContract.DisplayValue("windowsIdentity", "edition", edition, identity),
                    match.Groups["edition"].Value, StringComparison.OrdinalIgnoreCase)))
                throw new FabricContractException("Belegvalidierung", "Windows-Edition ist nicht durch den zitierten Beleg gedeckt.",
                    "windowsEdition", edition, match.Groups["edition"].Value);
    }

    internal static void ValidateWindowsProduct(string text, ImmutableArray<FabricEvidence> evidence,
        ImmutableArray<FabricEvidence> identitySources = default, bool validateEdition = false)
    {
        var sources = identitySources.IsDefault ? evidence : identitySources;
        var identity = CitedWindowsIdentity(evidence, sources);
        var product = identity?.ProductName ?? evidence.FirstOrDefault(e => e.Field == "operatingSystemProductName" &&
            IsCurrentEvidence(e, DateTimeOffset.UtcNow))?.Value ??
            evidence.FirstOrDefault(e => e.Field == "operatingSystem" && IsCurrentEvidence(e, DateTimeOffset.UtcNow))?.Value;
        foreach (Match match in Regex.Matches(text,
                     @"(?i)\bWindows\s+(?<version>10|11)(?:\s+(?<edition>Home|Pro|Professional|Enterprise|Education|Workstations|IoT|N|Single\s+Language))?\b",
                     RegexOptions.CultureInvariant,
                     TimeSpan.FromMilliseconds(100)))
        {
            var productMatches = product?.Contains($"Windows {match.Groups["version"].Value}",
                StringComparison.OrdinalIgnoreCase) == true;
            var edition = match.Groups["edition"].Value;
            var editionMatches = !validateEdition || edition.Length == 0 ||
                product?.Contains(edition, StringComparison.OrdinalIgnoreCase) == true ||
                identity?.Edition is { } recordedEdition &&
                string.Equals(recordedEdition, edition, StringComparison.OrdinalIgnoreCase);
            if (!productMatches || !editionMatches)
                throw new FabricContractException("Belegvalidierung", "Windows-Produktname widerspricht dem Produktbeleg.",
                    "windowsProductName", product, match.Value);
        }
    }

    internal static void ValidateWindowsRelease(string text, ImmutableArray<FabricEvidence> evidence,
        ImmutableArray<FabricEvidence> identitySources = default)
    {
        var identity = CitedWindowsIdentity(evidence, identitySources.IsDefault ? evidence : identitySources);
        var release = identity?.Release ?? evidence.FirstOrDefault(e => e.Field == "operatingSystemVersion" &&
            IsCurrentEvidence(e, DateTimeOffset.UtcNow))?.Value;
        foreach (Match match in Regex.Matches(text, @"(?i)\b\d{2}H[12]\b",
                     RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            if (release is null || !string.Equals(release, match.Value, StringComparison.OrdinalIgnoreCase))
                throw new FabricContractException("Belegvalidierung", "Windows-Release ist nicht durch den zitierten Beleg gedeckt.",
                    "operatingSystemVersion", release, match.Value);
    }

    internal static void ValidateMeasurements(string text, ImmutableArray<FabricEvidence> evidence)
    {
        var physical = Regex.IsMatch(text, @"(?i)\b(?:physisch\w*|SSD|HDD|NVMe|Festplatte)\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        foreach (Match match in Regex.Matches(text, @"(?<![\d.,])(?<number>\d+(?:[.,]\d+)?)\s*(?<unit>TB|GB|MB|KB|Bytes?|B|Prozent|%)(?=$|[\s.,;:!?])",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            if (!decimal.TryParse(match.Groups["number"].Value.Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint,
                    System.Globalization.CultureInfo.InvariantCulture, out var claimed))
                throw new FabricContractException("Belegvalidierung", "Messwert ist ungültig.", "findings[].text");
            var unit = match.Groups["unit"].Value.ToUpperInvariant();
            var matches = evidence.Any(e =>
            {
                if (!IsCurrentEvidence(e, DateTimeOffset.UtcNow)) return false;
                decimal? raw = null;
                var bytes = e.Unit == "Bytes";
                if (physical && !AetherToolBroker.IsPhysicalStorageField(e.Field)) return false;
                if (AetherToolBroker.IsPhysicalStorageField(e.Field))
                {
                    var disk = JsonSerializer.Deserialize<FabricPhysicalStorage>(e.Value!, FabricJson.Options);
                    raw = disk?.CapacityBytes;
                    bytes = true;
                }
                else if (AetherToolBroker.IsVolumeField(e.Field))
                {
                    var volume = AetherToolBroker.ValidateVolume(e.Value!);
                    raw = Regex.IsMatch(text, @"(?i)\bfrei\w*\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
                        ? volume.FreeBytes : Regex.IsMatch(text, @"(?i)\bbelegt\w*\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
                            ? volume.UsedBytes : Regex.IsMatch(text, @"(?i)\b(?:kapazität|gesamt\w*)\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
                                ? volume.CapacityBytes : null;
                    bytes = true;
                }
                else if ((!physical || e.Field is not ("freeBytes" or "volumeFreeBytes" or "volumeCapacityBytes" or "volumeUsedBytes")) &&
                         decimal.TryParse(e.Value, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    raw = parsed;
                if (raw is null) return false;
                if (unit is "%" or "PROZENT")
                    return !bytes && e.Unit == "Prozent" && Math.Abs(raw.Value - claimed) <= 0.05m;
                if (!bytes) return false;
                if (unit is "B" or "BYTE" or "BYTES") return raw == claimed;
                var exponent = unit switch { "TB" => 4, "GB" => 3, "MB" => 2, "KB" => 1, _ => 0 };
                var binary = raw.Value / (decimal)Math.Pow(1024, exponent);
                var decimalValue = raw.Value / (decimal)Math.Pow(1000, exponent);
                return Math.Abs(binary - claimed) <= 0.05m || Math.Abs(decimalValue - claimed) <= 0.05m;
            });
            if (!matches)
                throw new FabricContractException("Belegvalidierung", "Messwert oder Einheit ist durch die zitierten Belege nicht gedeckt.", "findings[].text");
        }
    }

    internal static void ValidateIdentity(string text, ImmutableArray<FabricEvidence> evidence,
        ImmutableArray<FabricEvidence> identitySources = default)
    {
        var current = evidence.Where(e => IsCurrentEvidence(e, DateTimeOffset.UtcNow)).ToArray();
        var identity = CitedWindowsIdentity(evidence, identitySources.IsDefault ? evidence : identitySources);
        foreach (Match match in Regex.Matches(text, @"(?i)\bBuild\s*[:#]?\s*(\d{4,6})\b",
                     RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            if (identity?.Build != match.Groups[1].Value &&
                !current.Any(e => e.Field == "operatingSystemBuild" && e.Value == match.Groups[1].Value))
                throw new FabricContractException("Belegvalidierung", "Windows-Build ist nicht durch den zitierten Beleg gedeckt.",
                    "operatingSystemBuild",
                    identity?.Build ?? current.FirstOrDefault(e => e.Field == "operatingSystemBuild")?.Value ??
                    "zitiertes operatingSystemBuild", match.Groups[1].Value);
        foreach (Match match in Regex.Matches(text, @"(?i)\b(?:x64|x86|arm64)\b",
                     RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            if (!string.Equals(identity?.Architecture, match.Value, StringComparison.OrdinalIgnoreCase) &&
                !current.Any(e => e.Field == "architecture" && string.Equals(e.Value, match.Value, StringComparison.OrdinalIgnoreCase)))
                throw new FabricContractException("Belegvalidierung", "Architektur ist nicht durch den zitierten Beleg gedeckt.",
                    "architecture", identity?.Architecture, match.Value);
        foreach (Match match in Regex.Matches(text, @"(?i)\b(?:Datenträger|Disk)\s*[:#]?\s*(\d{1,3})\b",
                     RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            var number = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (!current.Where(e => AetherToolBroker.IsPhysicalStorageField(e.Field))
                .Select(e => JsonSerializer.Deserialize<FabricPhysicalStorage>(e.Value!, FabricJson.Options))
                .Any(disk => disk?.DiskNumber == number))
                throw new FabricContractException("Belegvalidierung", "Datenträgernummer ist nicht durch den zitierten Gerätebeleg gedeckt.", "physicalDrive");
        }
        foreach (Match match in Regex.Matches(text,
                     @"(?i)\b(\d+)\s+(?:Systemfehler|Fehlerereignisse|Ereignisse|Events)\b",
                     RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            if (!int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var count))
                throw new FabricContractException("Belegvalidierung", "Ereignisanzahl ist ungültig.", "eventGroup");
            // A cited current event-count scalar backs the stated count exactly; otherwise a
            // matching event group with provider and event ID is required.
            var scalarBacked = current.Any(e =>
                e.Field is "systemErrors24h" or "maximumEventRepetitions24h" &&
                int.TryParse(e.Value, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var backedCount) &&
                backedCount == count);
            if (scalarBacked) continue;
            var groups = current.Where(e => AetherToolBroker.IsEventGroupField(e.Field))
                .Select(e => AetherToolBroker.ValidateEventGroup(e.Value!));
            if (!groups.Any(group => group.Count == count &&
                text.Contains(group.Provider, StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(text, $@"(?i)\b(?:Event(?:-?ID)?|Ereignis(?:-?ID)?)\s*[:#]?\s*{group.EventId}\b",
                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))))
                throw new FabricContractException("Belegvalidierung",
                    "Ereignisanzahl benötigt eine passende Gruppe mit Provider und Ereignis-ID.", "eventGroup");
        }
    }

    private static FabricWindowsIdentity? CitedWindowsIdentity(ImmutableArray<FabricEvidence> evidence,
        ImmutableArray<FabricEvidence> identitySources)
    {
        var item = evidence.FirstOrDefault(e => e.Field == "windowsIdentity" &&
            IsCurrentEvidence(e, DateTimeOffset.UtcNow));
        return item is null ? null : AetherContextManager.ReadWindowsIdentity(item, identitySources);
    }

    private static string? WindowsExpectedValue(string? field, ImmutableArray<FabricEvidence> allEvidence,
        ImmutableArray<FabricEvidence> identitySources)
    {
        var canonical = allEvidence.FirstOrDefault(item => item.Field == "windowsIdentity" &&
            IsCurrentEvidence(item, DateTimeOffset.UtcNow));
        var identity = canonical is null ? null : AetherContextManager.ReadWindowsIdentity(canonical, identitySources);
        if (identity is not null)
            return field switch
            {
                "windowsProductName" => identity.ProductName,
                "windowsEdition" => identity.Edition,
                "operatingSystemVersion" => identity.Release,
                "operatingSystemBuild" => identity.Build,
                "architecture" => identity.Architecture,
                _ => null
            };
        var sourceField = field switch
        {
            "windowsProductName" => "operatingSystemProductName",
            "windowsEdition" => "operatingSystemEdition",
            "operatingSystemVersion" => "operatingSystemVersion",
            "operatingSystemBuild" => "operatingSystemBuild",
            "architecture" => "architecture",
            _ => ""
        };
        return allEvidence.FirstOrDefault(item => item.Field == sourceField &&
            IsCurrentEvidence(item, DateTimeOffset.UtcNow))?.Value;
    }

    private static ImmutableArray<Guid> WindowsEvidenceIds(string? field, ImmutableArray<FabricEvidence> evidence,
        ImmutableArray<FabricEvidence> identitySources)
    {
        bool HasField(FabricEvidence item)
        {
            var identity = AetherContextManager.ReadWindowsIdentity(item, identitySources);
            return field switch
            {
                "windowsProductName" => !string.IsNullOrWhiteSpace(identity.ProductName),
                "windowsEdition" => !string.IsNullOrWhiteSpace(identity.Edition),
                "operatingSystemVersion" => !string.IsNullOrWhiteSpace(identity.Release),
                "operatingSystemBuild" => !string.IsNullOrWhiteSpace(identity.Build),
                "architecture" => !string.IsNullOrWhiteSpace(identity.Architecture),
                _ => false
            };
        }
        var canonical = evidence.Where(item => item.Field == "windowsIdentity" &&
            IsCurrentEvidence(item, DateTimeOffset.UtcNow) && HasField(item)).Select(item => item.Id);
        var rawField = field switch
        {
            "windowsProductName" => "operatingSystemProductName",
            "windowsEdition" => "operatingSystemEdition",
            "operatingSystemVersion" => "operatingSystemVersion",
            "operatingSystemBuild" => "operatingSystemBuild",
            "architecture" => "architecture",
            _ => ""
        };
        var raw = evidence.Where(item => item.Field == rawField && IsCurrentEvidence(item, DateTimeOffset.UtcNow))
            .Select(item => item.Id);
        return canonical.Concat(raw).Distinct().Order().ToImmutableArray();
    }

    private static ImmutableArray<FabricAnswerClaim> Deduplicate(ImmutableArray<FabricAnswerClaim> claims)
    {
        static string Normalize(string value)
        {
            var punctuation = Regex.Replace(value.Trim(), @"[.!?]+$", "",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            return Regex.Replace(punctuation, @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        var result = new List<FabricAnswerClaim>();
        foreach (var claim in claims)
        {
            var normalized = Normalize(claim.Text);
            var index = result.FindIndex(existing => existing.Proof == claim.Proof &&
                string.Equals(Normalize(existing.Text), normalized, StringComparison.OrdinalIgnoreCase));
            if (index < 0) { result.Add(claim); continue; }
            var combinedEvidence = result[index].Evidence.Concat(claim.Evidence).Distinct().ToImmutableArray();
            var combinedSources = result[index].Sources.Concat(claim.Sources).Distinct().ToImmutableArray();
            if (combinedEvidence.Length > 16 || combinedSources.Length > 6)
                throw new FabricContractException("Belegvalidierung", "Zusammengeführte Aussage überschreitet das Beleglimit.", "findings");
            result[index] = result[index] with { Evidence = combinedEvidence, Sources = combinedSources };
        }
        return result.ToImmutableArray();
    }

    public static bool IsCurrentEvidence(FabricEvidence evidence, DateTimeOffset now) =>
        evidence.Metadata.Availability == DataAvailability.Available &&
        evidence.Value is not null && evidence.Metadata.Timestamp <= now &&
        now - evidence.Metadata.Timestamp < (evidence.Field == "cpuPercent" ? TimeSpan.FromSeconds(60) : TimeSpan.FromMinutes(10));

    internal static ImmutableArray<Guid> AllowedEvidenceIds(ImmutableArray<FabricEvidence> evidence, DateTimeOffset now) =>
        evidence.Where(e => IsCurrentEvidence(e, now))
            .Select(e => e.Id).Distinct().Order().ToImmutableArray();

    internal static void ValidateText(string? text, int maximum, string field)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || !AetherSafetyClassifier.Inspect(text).IsEmpty)
            throw new FabricContractException("Antwort-Validierung", "Text ist leer, zu groß oder enthält unzulässige Anweisungen.", field);
    }

    public static FabricAssistantAnswer Deterministic(ImmutableArray<FabricEvidence> evidence)
    {
        var now = DateTimeOffset.UtcNow;
        var available = evidence.Where(e => e.Metadata.Availability == DataAvailability.Available &&
            e.Value is not null && e.Metadata.Timestamp <= now && now - e.Metadata.Timestamp < TimeSpan.FromMinutes(10)).Take(16).ToArray();
        return new(1, "de", "Dies ist eine regelbasierte Zusammenfassung der verfügbaren Quellen.",
            available.Length == 0 ? [new("Es liegen keine aktuellen verfügbaren Messbelege vor.", FabricProof.Undecidable, [], [])] :
                available.Select(e => new FabricAnswerClaim(
                    $"{(e.Simulated ? "SIMULIERT · " : "")}{e.Field}: {e.Value} {e.Unit}", FabricProof.Observed, [e.Id], [])).ToImmutableArray(),
            evidence.Where(e => !available.Contains(e)).Select(e => e.Field + ": nicht verfügbar oder nicht in dieser begrenzten Übersicht.").Distinct().Take(24).ToImmutableArray(),
            ["Ursachen nur anhand zusätzlicher passender Belege prüfen; keine Systemänderung ausgeführt."]);
    }
}

public sealed class AetherAssistant(AetherInferenceScheduler scheduler)
{
    public async Task<FabricAssistantResult> AnswerAsync(string input, ImmutableArray<Guid> targets,
        ImmutableArray<FabricEvidence> evidence, ImmutableArray<FabricCitation> citations, FabricModel model,
        IAetherInferenceProvider provider, string routing, FabricMode mode, CancellationToken token,
        IProgress<FabricProgressPhase>? progress = null, FabricRunDiagnostics? diagnostics = null)
    {
        var watch = Stopwatch.StartNew(); var before = Environment.WorkingSet;
        token.ThrowIfCancellationRequested();
        var sourceEvidence = AetherContextManager.VisibleEvidence(targets, evidence);
        var visibleEvidence = sourceEvidence;
        var contextManager = new AetherContextManager();
        var role = AetherAgentRegistry.RoleFor(input);
        string context;
        if (role == FabricModelRole.FastAssistant)
        {
            sourceEvidence = AetherContextManager.WithCanonicalWindowsIdentity(sourceEvidence);
            sourceEvidence = AetherContextManager.WithCanonicalStorageSummaries(sourceEvidence);
            visibleEvidence = AetherContextManager.FastAssistantEvidenceScope(sourceEvidence);
            visibleEvidence = AetherContextManager.FocusFastAssistantScope(input, visibleEvidence);
            context = contextManager.BuildFastAssistant(input, targets, sourceEvidence, visibleEvidence);
        }
        else
        {
            context = contextManager.Build(input, targets, citations, evidence);
            if (AetherPromptRegistry.FocusedEventGroup(context) is { } focused)
            {
                visibleEvidence = visibleEvidence.Where(item => item.Id == focused.EvidenceId &&
                    AetherAnswerValidator.IsCurrentEvidence(item, DateTimeOffset.UtcNow)).ToImmutableArray();
                context = contextManager.Build(input, targets, citations, visibleEvidence);
            }
        }
        token.ThrowIfCancellationRequested();
        var task = role == FabricModelRole.FastAssistant ? "assistant-report" : "diagnosis-report";
        var promptHash = AetherPromptRegistry.HashFor(task, context);
        if (model.Id == AetherModelRegistry.Fallback.Id)
        {
            token.ThrowIfCancellationRequested();
            return new(AetherAnswerValidator.Deterministic(evidence), model.Id, provider.Info.Id, routing,
                promptHash, context.Length, null, null, watch.Elapsed.TotalMilliseconds,
                null, null, null, before, Environment.WorkingSet, false, "Feste Quellenzuordnung; kein Sprachmodell");
        }
        var maximumOutputTokens = role == FabricModelRole.FastAssistant ? 1536 : 2048;
        promptHash = AetherPromptRegistry.HashFor(task, context);
        var inferenceSucceeded = false;
        var validationStarted = false;
        var validatingRepair = false;
        try
        {
            diagnostics?.Update(d =>
            {
                d.RequestedModelRole = role.ToString(); d.InferenceTask = task;
                d.PromptVersion = AetherPromptRegistry.Version;
                d.AnswerType = task == "assistant-report" ? nameof(FabricFastAssistantAnswer) : nameof(FabricDiagnosticReport);
                d.InferenceStartedAt = DateTimeOffset.UtcNow;
                d.ProcessWorkingSetBeforeBytes = before;
            });
            var request = new FabricRequest(1, Guid.NewGuid(), task, "de", context,
                promptHash, model.Id, Math.Min(8192, model.DeclaredContextTokens),
                maximumOutputTokens, 60, DataClassification.Sensitive);
            async Task<FabricResponse> InferWithTransientRetryAsync(FabricRequest inferenceRequest, bool refreshHardware)
            {
                // Bounded retries after transient local-runtime server errors; identical request and scope.
                var transientDelays = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3) };
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        return attempt == 0 && refreshHardware
                            ? await scheduler.RunAsync(model, provider, inferenceRequest, mode, token).ConfigureAwait(false)
                            : await scheduler.RetryWithoutRefreshAsync(model, provider, inferenceRequest, mode, token).ConfigureAwait(false);
                    }
                    catch (HttpRequestException transient) when (attempt < transientDelays.Length &&
                        transient.StatusCode is { } transientStatus && (int)transientStatus >= 500 &&
                        provider is CompatibleInferenceProvider { IsLoopback: true })
                    {
                        diagnostics?.Update(d => d.InferenceRetriedAfterTransientError = true);
                        await Task.Delay(transientDelays[attempt], token).ConfigureAwait(false);
                    }
                }
            }
            progress?.Report(FabricProgressPhase.AnalyzingLocally);
            diagnostics?.Update(d => { d.InferenceStarted = true; d.MaximumOutputTokens = request.MaximumOutputTokens; });
            var response = await InferWithTransientRetryAsync(request, refreshHardware: true).ConfigureAwait(false);
            inferenceSucceeded = true;
            diagnostics?.Update(d =>
            {
                d.InferenceSucceeded = true; d.InitialInputTokens = response.InputTokens;
                d.InitialOutputTokens = response.OutputTokens; d.InitialGenerationSeconds = response.GenerationSeconds;
                d.InitialLoadSeconds = response.LoadSeconds; d.InitialFirstTokenSeconds = response.FirstTokenSeconds;
                d.InitialDoneReason = response.DoneReason;
                d.InitialResponseShape = FabricResponseShape.Inspect(response.Json);
            });
            token.ThrowIfCancellationRequested();
            progress?.Report(FabricProgressPhase.ValidatingAnswer);
            validationStarted = true;
            diagnostics?.Update(d => d.ValidatorStarted = true);
            FabricAssistantAnswer? answer = null;
            FabricDiagnosticReport? report = null;
            FabricFastAssistantAnswer? initialFastCandidate = null;
            FabricFastAssistantAnswer? repairFastCandidate = null;
            FabricFastAssistantAnswer? acceptedFastCandidate = null;
            void ValidateResponse(string json)
            {
                if (task == "assistant-report")
                {
                    var candidate = FabricJson.ParseModel<FabricFastAssistantAnswer>(json, "FastAssistant-JSON");
                    candidate = CanonicalizeClaimShapes(candidate, visibleEvidence, sourceEvidence, diagnostics);
                    if (!validatingRepair)
                    {
                        candidate = AddRequestedIdentityFindings(candidate, input, visibleEvidence, sourceEvidence,
                            diagnostics);
                    }
                    if (validatingRepair) repairFastCandidate = candidate;
                    else initialFastCandidate = candidate;
                    answer = AetherAnswerValidator.ParseFastAssistant(candidate, visibleEvidence, sourceEvidence);
                    acceptedFastCandidate = candidate;
                }
                else
                    report = AetherDiagnosisContractValidator.Parse(json, visibleEvidence, context);
            }
            var initialOutputTruncated = IsOutputTruncated(response.DoneReason);
            FabricContractException? initialValidationError = null;
            if (initialOutputTruncated)
            {
                diagnostics?.Update(d => { d.ValidatorSucceeded = false; d.RepairRetryStarted = true; });
            }
            else
            {
                try
                {
                    ValidateResponse(response.Json);
                }
                catch (FabricContractException ex)
                {
                    initialValidationError = ex;
                }
            }
            var deterministicStorageField = initialValidationError?.Field switch
            {
                "physicalStorageSummary" or "volumeSummary" when initialValidationError.Actual == "fehlend" =>
                    initialValidationError.Field,
                "physicalDrive" => "physicalStorageSummary",
                _ => null
            };
            if (!initialOutputTruncated && task == "assistant-report" && initialFastCandidate is not null &&
                deterministicStorageField is { } deterministicStorageTarget)
            {
                var storageIndexes = StorageFindingIndexes(initialFastCandidate, deterministicStorageTarget)
                    .Concat(initialValidationError!.RepairTargetFindingIndex is { } storageIndex ? [storageIndex] : [])
                    .Distinct().Order().ToArray();
                initialFastCandidate = RemoveStorageFindings(initialFastCandidate, storageIndexes,
                    deterministicStorageTarget, visibleEvidence, sourceEvidence);
                try
                {
                    answer = AetherAnswerValidator.ParseFastAssistant(
                        initialFastCandidate, visibleEvidence, sourceEvidence);
                    acceptedFastCandidate = initialFastCandidate;
                    initialValidationError = null;
                }
                catch (FabricContractException error)
                {
                    initialValidationError = error;
                }
            }
            if (initialOutputTruncated || initialValidationError is not null)
            {
                token.ThrowIfCancellationRequested();
                diagnostics?.Update(d =>
                {
                    d.ValidatorSucceeded = false;
                    d.RepairRetryStarted = false;
                    d.InitialValidationReason = initialOutputTruncated
                        ? "OutputTruncated"
                        : initialValidationError!.Diagnostic;
                    d.InitialRejectedClaim = initialValidationError?.RejectedClaim;
                });
                var repairContext = JsonNode.Parse(context)!.AsObject();
                var rejectedClaim = initialValidationError?.RejectedClaim;
                var rejectedFindingIndex = initialValidationError?.RepairTargetFindingIndex;
                var focusedClaimRepair = !initialOutputTruncated && task == "assistant-report" &&
                    initialFastCandidate is not null && rejectedClaim is not null &&
                    rejectedClaim.FindingIndex >= 0 && rejectedClaim.FindingIndex < initialFastCandidate.Findings.Length &&
                    rejectedClaim.ClaimIndex >= 0 &&
                    rejectedClaim.ClaimIndex < initialFastCandidate.Findings[rejectedClaim.FindingIndex].Claims.Length;
                var storageField = initialValidationError?.Field is "physicalStorageSummary" or "volumeSummary"
                    ? initialValidationError.Field
                    : null;
                var storageRemovalCandidates = !initialOutputTruncated && task == "assistant-report" &&
                    initialFastCandidate is not null && rejectedFindingIndex is { } storageFindingIndex &&
                    storageFindingIndex >= 0 && storageFindingIndex < initialFastCandidate.Findings.Length &&
                    storageField is not null && initialValidationError?.Actual == "fehlend"
                        ? StorageFindingIndexes(initialFastCandidate, storageField)
                            .Append(storageFindingIndex).Distinct().Order().ToArray()
                        : [];
                var focusedStorageRemoval = storageRemovalCandidates.Length > 0 &&
                    storageRemovalCandidates.Length <= initialFastCandidate!.Findings.Length;
                var storageRemovalIndexes = focusedStorageRemoval ? storageRemovalCandidates : [];
                var deterministicStorageRemoval = focusedStorageRemoval &&
                    initialValidationError?.Actual == "fehlend";
                var focusedTextRepair = !focusedClaimRepair && !focusedStorageRemoval &&
                    !initialOutputTruncated && task == "assistant-report" &&
                    initialFastCandidate is not null &&
                    rejectedClaim is { ClaimIndex: < 0, FindingIndex: >= 0 } &&
                    rejectedClaim.FindingIndex < initialFastCandidate.Findings.Length;
                if (focusedClaimRepair)
                {
                    var targetFinding = initialFastCandidate!.Findings[rejectedClaim!.FindingIndex];
                    repairContext["repairTarget"] = JsonSerializer.SerializeToNode(new
                    {
                        kind = "claim",
                        rejectedClaim.FindingIndex,
                        rejectedClaim.ClaimIndex,
                        rejectedClaim.ValidatorRule,
                        findingText = targetFinding.Text,
                        claim = targetFinding.Claims[rejectedClaim.ClaimIndex],
                        findingClaims = targetFinding.Claims
                    }, FabricJson.Options);
                    if (rejectedClaim.ValidatorRule == "claim.text-identity")
                    {
                        var neededIds = targetFinding.Claims.SelectMany(claim => claim.Evidence)
                            .Select(id => id.ToString("D")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var allowed = repairContext["allowedEvidenceIds"]!.AsArray()
                            .Where(id => id is not null && neededIds.Contains(id.GetValue<string>()))
                            .Select(id => id!.DeepClone()).ToArray();
                        var observations = repairContext["observations"]!.AsArray()
                            .Where(item => item?["id"] is JsonValue id && neededIds.Contains(id.GetValue<string>()))
                            .Select(item => item!.DeepClone()).ToArray();
                        repairContext["allowedEvidenceIds"] = new JsonArray(allowed);
                        repairContext["observations"] = new JsonArray(observations);
                    }
                }
                else if (focusedStorageRemoval)
                {
                    repairContext["repairTarget"] = JsonSerializer.SerializeToNode(new
                    {
                        kind = "removeStorageFindings",
                        findingIndexes = storageRemovalIndexes
                    }, FabricJson.Options);
                }
                else if (focusedTextRepair)
                {
                    var textFinding = initialFastCandidate!.Findings[rejectedClaim!.FindingIndex];
                    repairContext["repairTarget"] = JsonSerializer.SerializeToNode(new
                    {
                        kind = "findingText",
                        rejectedClaim.FindingIndex,
                        rejectedClaim.ValidatorRule,
                        findingText = textFinding.Text,
                        findingClaims = textFinding.Claims
                    }, FabricJson.Options);
                }
                else if (!initialOutputTruncated && task == "assistant-report" && initialFastCandidate is not null)
                    repairContext["previousAnswer"] =
                        JsonSerializer.SerializeToNode(initialFastCandidate, FabricJson.Options);
                var repairGuidance = initialValidationError?.RepairGuidance ?? initialValidationError?.Diagnostic ?? "";
                var repairMeasurementValue = "";
                var repairMeasurementUnit = "";
                var canonicalMeasurementRepair = focusedClaimRepair &&
                    (rejectedClaim!.ValidatorRule is "measurement.deterministic-byte-format" or
                        "storage.formatted-total-capacity" or "storage.formatted-device-capacity" or
                        "volume.formatted-measurement") &&
                    AetherAnswerValidator.TryDisplayParts(rejectedClaim.ExpectedValue,
                        out repairMeasurementValue, out repairMeasurementUnit);
                var scalarUnit = "";
                var scalarType = "";
                var scalarEvidence = focusedClaimRepair && rejectedClaim!.ValidatorRule == "evidence.scalar-value"
                    ? visibleEvidence.SingleOrDefault(item =>
                        item.Id == rejectedClaim.EvidenceIds[0] && item.Field == rejectedClaim.Subject)
                    : null;
                var scalarValue = scalarEvidence is null ? null :
                    AetherAnswerValidator.EvidenceClaimValue(scalarEvidence, out scalarUnit, out scalarType);
                var canonicalCountRepair = scalarType == "Count" && scalarValue is not null;
                if (!canonicalCountRepair && focusedClaimRepair &&
                    (rejectedClaim!.ValidatorRule == "storage.summary" && rejectedClaim.Property == "deviceCount" ||
                     rejectedClaim.ValidatorRule == "volume.summary" && rejectedClaim.Property == "volumeCount") &&
                    rejectedClaim.ExpectedValue is { Length: > 0 } canonicalStorageCount)
                {
                    scalarValue = canonicalStorageCount;
                    scalarUnit = rejectedClaim.Property == "deviceCount" ? "devices" : "volumes";
                    canonicalCountRepair = true;
                }
                repairContext["repairInstruction"] = initialOutputTruncated && task == "assistant-report"
                    ? "Vorige Antwort abgeschnitten (done_reason=length). Gleicher Evidence-Scope; vollständiges JSON nach kompaktem Schema."
                    : initialOutputTruncated
                    ? "Vorige Antwort abgeschnitten (done_reason=length). Gleicher Evidence-Scope und Aktionskatalog; vollständiges JSON."
                    : focusedStorageRemoval
                        ? $"Abgelehnt: {repairGuidance}. Entferne alle repairTarget-Storagebefunde; findings muss leer sein. Kein neuer Evidence-Scope."
                        : canonicalMeasurementRepair
                        ? $"Nur repairTarget korrigieren: ClaimType=Measurement, Operator=Equals, value={repairMeasurementValue}, unit={repairMeasurementUnit}; EvidenceId unverändert, andere Befunde unverändert. JSON."
                        : canonicalCountRepair
                        ? $"Nur repairTarget korrigieren: claimType=Count, operator=Equals, value={scalarValue}, unit={scalarUnit}; EvidenceId und andere Befunde unverändert. JSON."
                        : focusedClaimRepair && rejectedClaim!.ValidatorRule == "claim.text-identity"
                        ? $"Der Befundtext muss {rejectedClaim.ExpectedValue} exakt enthalten. Andere Aussagen aus findingClaims beibehalten; nur den Zielclaim ausgeben. JSON."
                        : focusedClaimRepair
                        ? $"Abgelehnt: {repairGuidance}. Korrigiere repairTarget; gib den Befund als findings[0] aus. JSON."
                        : focusedTextRepair
                        ? $"Abgelehnt: {repairGuidance}. Gib genau den repairTarget-Befund als findings[0] mit korrigiertem text aus; claims und evidence exakt unverändert übernehmen. JSON."
                        : $"Abgelehnt: {repairGuidance}. Nur diesen Fehler beheben; Rest und Evidence-Scope unverändert." +
                          (initialFastCandidate is null ? "" : " previousAnswer als Ausgangspunkt nutzen.") + " JSON.";
                var repairPrompt = repairContext.ToJsonString(FabricJson.Options);
                var repairRequest = request with
                {
                    Id = Guid.NewGuid(),
                    Prompt = repairPrompt,
                    PromptHash = AetherPromptRegistry.HashFor(task, repairPrompt)
                };
                if (deterministicStorageRemoval)
                {
                    var cleanedAnswer = RemoveStorageFindings(initialFastCandidate!, storageRemovalIndexes,
                        storageField!, visibleEvidence, sourceEvidence);
                    answer = AetherAnswerValidator.ParseFastAssistant(cleanedAnswer, visibleEvidence, sourceEvidence);
                    acceptedFastCandidate = cleanedAnswer;
                }
                else
                {
                progress?.Report(FabricProgressPhase.AnalyzingLocally);
                diagnostics?.Update(d => { d.RepairRetryStarted = true; d.InferenceSucceeded = false; });
                response = await InferWithTransientRetryAsync(repairRequest, refreshHardware: false).ConfigureAwait(false);
                inferenceSucceeded = true;
                request = repairRequest;
                context = repairPrompt;
                diagnostics?.Update(d =>
                {
                    d.InferenceSucceeded = true; d.RepairInputTokens = response.InputTokens;
                    d.RepairOutputTokens = response.OutputTokens; d.RepairGenerationSeconds = response.GenerationSeconds;
                    d.RepairLoadSeconds = response.LoadSeconds; d.RepairFirstTokenSeconds = response.FirstTokenSeconds;
                    d.RepairDoneReason = response.DoneReason;
                    d.RepairResponseShape = FabricResponseShape.Inspect(response.Json);
                });
                token.ThrowIfCancellationRequested();
                if (IsOutputTruncated(response.DoneReason))
                    throw OutputTruncatedException();
                progress?.Report(FabricProgressPhase.ValidatingAnswer);
                validatingRepair = true;
                void AcceptRepairedCandidate()
                {
                    var fallbackInserted = false;
                    void EnsureFallbackFinding()
                    {
                        // If every model finding had to be removed, a canonical deterministic
                        // finding keeps the validated answer evidence-bound instead of falling back.
                        if (!repairFastCandidate!.Findings.IsEmpty || fallbackInserted) return;
                        if ((CreateCanonicalWindowsFinding(visibleEvidence, sourceEvidence) ??
                             CreateCanonicalStorageFinding("physicalStorageSummary", visibleEvidence, sourceEvidence))
                                is { } fallbackFinding)
                        {
                            fallbackInserted = true;
                            repairFastCandidate = repairFastCandidate with { Findings = [fallbackFinding] };
                        }
                    }
                    EnsureFallbackFinding();
                    repairFastCandidate = CanonicalizeClaimShapes(repairFastCandidate!,
                        visibleEvidence, sourceEvidence, diagnostics);
                    while (true)
                    {
                        try
                        {
                            answer = AetherAnswerValidator.ParseFastAssistant(repairFastCandidate, visibleEvidence, sourceEvidence);
                            break;
                        }
                        catch (FabricContractException mergeError) when (
                            mergeError is { Field: "physicalStorageSummary" or "volumeSummary", Actual: "fehlend" })
                        {
                            // Same deterministic handling as for initial answers: uncited storage
                            // statements that surfaced only after the focused repair are removed.
                            var mergeIndexes = StorageFindingIndexes(repairFastCandidate, mergeError.Field!)
                                .Concat(mergeError.RepairTargetFindingIndex is { } mergeIndex ? [mergeIndex] : [])
                                .Distinct().Order().ToArray();
                            repairFastCandidate = RemoveStorageFindings(repairFastCandidate, mergeIndexes,
                                mergeError.Field!, visibleEvidence, sourceEvidence);
                        }
                        catch (FabricContractException mergeError) when (
                            mergeError.RejectedClaim is { FindingIndex: >= 0 } mergeRejected &&
                            mergeRejected.FindingIndex < repairFastCandidate.Findings.Length)
                        {
                            // The single repair is spent; a remaining unbacked finding that cannot
                            // be canonicalized deterministically is removed so that only validated
                            // findings stay visible.
                            diagnostics?.Update(d => d.RejectedFindingRemoved = true);
                            repairFastCandidate = repairFastCandidate with
                            {
                                Findings = repairFastCandidate.Findings
                                    .Where((_, index) => index != mergeRejected.FindingIndex).ToImmutableArray()
                            };
                            EnsureFallbackFinding();
                            repairFastCandidate = CanonicalizeClaimShapes(repairFastCandidate,
                                visibleEvidence, sourceEvidence, diagnostics);
                        }
                    }
                    acceptedFastCandidate = repairFastCandidate;
                }
                if (focusedClaimRepair)
                {
                    repairFastCandidate = FabricJson.ParseModel<FabricFastAssistantAnswer>(
                        response.Json, "FastAssistant-JSON");
                    if (repairFastCandidate.Findings.IsDefaultOrEmpty ||
                        repairFastCandidate.Findings.Length != 1 ||
                        repairFastCandidate.Findings[0].Evidence.IsDefaultOrEmpty ||
                        repairFastCandidate.Findings[0].Evidence.Length != 1 ||
                        repairFastCandidate.Findings[0] is not { Claims.Length: 1 } repairedFinding)
                        throw RepairContractError("Repair muss genau einen Zielbefund mit einem Claim liefern.",
                            rejectedClaim!);
                    var originalFinding = initialFastCandidate!.Findings[rejectedClaim!.FindingIndex];
                    var originalClaim = originalFinding.Claims[rejectedClaim.ClaimIndex];
                    if (rejectedClaim.ValidatorRule == "claim.text-identity" &&
                        SerializeClaimSequence([originalClaim]) != SerializeClaimSequence(repairedFinding.Claims))
                        throw RepairContractError("Textreparatur muss den beanstandeten Identitätsclaim unverändert erhalten.",
                            rejectedClaim);
                    if (rejectedClaim.ValidatorRule is not ("claim.text-identity" or
                            "measurement.deterministic-byte-format" or "storage.formatted-total-capacity" or
                            "storage.formatted-device-capacity" or "volume.formatted-measurement") &&
                        repairedFinding.Text == originalFinding.Text &&
                        SerializeClaimSequence(repairedFinding.Claims) == SerializeClaimSequence([originalClaim]))
                    {
                        // The model returned the rejected claim unchanged: it cannot back the
                        // assertion. The affected finding is removed deterministically so only
                        // evidence-backed findings remain.
                        diagnostics?.Update(d => d.RejectedFindingRemoved = true);
                        repairFastCandidate = initialFastCandidate with
                        {
                            Findings = initialFastCandidate.Findings
                                .Where((_, findingIndex) => findingIndex != rejectedClaim.FindingIndex)
                                .ToImmutableArray()
                        };
                        AcceptRepairedCandidate();
                    }
                    else
                    {
                    var claimEvidence = rejectedClaim.ValidatorRule is
                        "measurement.deterministic-byte-format" or "storage.formatted-total-capacity" or
                        "storage.formatted-device-capacity" or "volume.formatted-measurement"
                            ? originalFinding.Claims[rejectedClaim.ClaimIndex].Evidence
                            : repairedFinding.Evidence;
                    // The model must not invent IDs: unknown repaired evidence falls back to the
                    // original claim evidence and is then rebound deterministically if misbound.
                    if (!claimEvidence.All(id => visibleEvidence.Any(item => item.Id == id)))
                        claimEvidence = originalFinding.Claims[rejectedClaim.ClaimIndex].Evidence;
                    var repairedClaim = repairedFinding.Claims[0] with { Evidence = claimEvidence };
                    if ((rejectedClaim!.ValidatorRule is "measurement.deterministic-byte-format" or
                            "storage.formatted-total-capacity" or "storage.formatted-device-capacity" or
                            "volume.formatted-measurement") &&
                        AetherAnswerValidator.TryDisplayParts(rejectedClaim.ExpectedValue,
                            out var expectedMeasurementValue, out var expectedMeasurementUnit))
                    {
                        repairedClaim = repairedClaim with
                        {
                            ClaimType = "Measurement",
                            Subject = rejectedClaim.Subject,
                            Property = rejectedClaim.Property,
                            Operator = "Equals",
                            Value = expectedMeasurementValue,
                            Unit = expectedMeasurementUnit
                        };
                        if (CanonicalMeasurementText(rejectedClaim,
                                expectedMeasurementValue + " " + expectedMeasurementUnit) is { } canonicalText)
                            repairedFinding = repairedFinding with { Text = canonicalText };
                    }
                    repairedFinding = repairedFinding with
                    {
                        Evidence = claimEvidence,
                        Claims = [repairedClaim]
                    };
                    var claims = originalFinding.Claims.SetItem(rejectedClaim.ClaimIndex,
                        repairedFinding.Claims[0]);
                    var mergedFinding = originalFinding with
                    {
                        Text = repairedFinding.Text,
                        Claims = claims,
                        Evidence = claims.SelectMany(claim => claim.Evidence).Distinct().ToImmutableArray()
                    };
                    var mergedFindings = initialFastCandidate.Findings
                        .SetItem(rejectedClaim.FindingIndex, mergedFinding);
                    EnsureRepairPreservesOtherClaims(initialFastCandidate,
                        initialFastCandidate with { Findings = mergedFindings }, rejectedClaim);
                    mergedFindings = NormalizeDeterministicMeasurementFindings(mergedFindings,
                        visibleEvidence, sourceEvidence);
                    repairFastCandidate = initialFastCandidate with
                    {
                        Findings = mergedFindings
                    };
                    AcceptRepairedCandidate();
                    }
                }
                else if (focusedTextRepair)
                {
                    repairFastCandidate = FabricJson.ParseModel<FabricFastAssistantAnswer>(
                        response.Json, "FastAssistant-JSON");
                    var originalTextFinding = initialFastCandidate!.Findings[rejectedClaim!.FindingIndex];
                    if (repairFastCandidate.Findings.IsDefaultOrEmpty ||
                        repairFastCandidate.Findings.Length != 1 ||
                        repairFastCandidate.Findings[0] is not { } rewrittenFinding ||
                        SerializeClaimSequence(rewrittenFinding.Claims) !=
                            SerializeClaimSequence(originalTextFinding.Claims))
                        throw RepairContractError(
                            "Textreparatur muss genau den Zielbefund mit unveränderten Claims liefern.",
                            rejectedClaim);
                    if (rewrittenFinding.Text == originalTextFinding.Text)
                    {
                        // The model returned the finding text unchanged: the statement cannot be
                        // backed. The finding is removed deterministically.
                        diagnostics?.Update(d => d.RejectedFindingRemoved = true);
                        repairFastCandidate = initialFastCandidate with
                        {
                            Findings = initialFastCandidate.Findings
                                .Where((_, findingIndex) => findingIndex != rejectedClaim.FindingIndex)
                                .ToImmutableArray()
                        };
                    }
                    else
                        repairFastCandidate = initialFastCandidate with
                        {
                            Findings = initialFastCandidate.Findings.SetItem(rejectedClaim.FindingIndex,
                                originalTextFinding with { Text = rewrittenFinding.Text })
                        };
                    AcceptRepairedCandidate();
                }
                else if (focusedStorageRemoval)
                {
                    repairFastCandidate = FabricJson.ParseModel<FabricFastAssistantAnswer>(
                        response.Json, "FastAssistant-JSON");
                    if (repairFastCandidate.Findings.IsDefault || !repairFastCandidate.Findings.IsEmpty)
                        throw new FabricContractException("FastAssistant-Repair",
                            "Storage-Repair muss ausschließlich den beanstandeten Befund entfernen.",
                            $"findings[{rejectedFindingIndex!.Value}]", "leeres findings-Array",
                            "Storagebefund weiterhin vorhanden");
                    repairFastCandidate = initialFastCandidate! with
                    {
                        Findings = initialFastCandidate.Findings
                            .Where((_, index) => !storageRemovalIndexes.Contains(index))
                            .ToImmutableArray()
                    };
                    if (repairFastCandidate.Findings.IsEmpty &&
                        CreateCanonicalStorageFinding(initialValidationError!.Field!, visibleEvidence, sourceEvidence) is { } safeFinding)
                        repairFastCandidate = repairFastCandidate with { Findings = [safeFinding] };
                    repairFastCandidate = RemoveUncitedStorageText(repairFastCandidate);
                    answer = AetherAnswerValidator.ParseFastAssistant(repairFastCandidate, visibleEvidence, sourceEvidence);
                    acceptedFastCandidate = repairFastCandidate;
                }
                else
                {
                    ValidateResponse(response.Json);
                    if (initialValidationError?.RejectedClaim is { } failedClaim &&
                        task == "assistant-report" && initialFastCandidate is not null &&
                        repairFastCandidate is not null)
                        EnsureRepairPreservesOtherClaims(initialFastCandidate, repairFastCandidate, failedClaim);
                }
            }
            }
            if (task == "assistant-report" && acceptedFastCandidate is not null)
            {
                var completedCandidate = AddRequestedStorageFinding(acceptedFastCandidate, input,
                    visibleEvidence, sourceEvidence);
                if (!ReferenceEquals(completedCandidate, acceptedFastCandidate))
                {
                    answer = AetherAnswerValidator.ParseFastAssistant(completedCandidate,
                        visibleEvidence, sourceEvidence);
                    acceptedFastCandidate = completedCandidate;
                }
            }
            diagnostics?.Update(d =>
            {
                d.ValidatorSucceeded = true;
                if (acceptedFastCandidate is not null)
                {
                    var claims = acceptedFastCandidate.Findings.SelectMany(finding => finding.Claims).ToArray();
                    d.AcceptedClaimTypes = claims.Select(claim => claim.ClaimType).Distinct().Order().ToArray();
                    d.AcceptedClaimCategories = claims.Select(claim => claim.Subject switch
                    {
                        "windowsIdentity" when claim.ClaimType == "Identity" => "WindowsIdentity",
                        "cpuName" when claim.ClaimType == "Identity" => "CpuIdentity",
                        "gpuName" when claim.ClaimType == "Identity" => "GpuIdentity",
                        "memoryTotalBytes" or "memoryAvailableBytes" when claim.ClaimType == "Measurement" => "RamMeasurement",
                        "gpuVramTotalBytes" or "gpuVramAvailableBytes" when claim.ClaimType == "Measurement" => "VramMeasurement",
                        _ => null
                    }).Where(category => category is not null).Select(category => category!).Distinct().Order().ToArray();
                    d.AcceptedStorageAvailabilityClaims = claims.Count(claim => claim.ClaimType == "Availability" &&
                        (claim.Subject == "physicalStorageSummary" ||
                         claim.Subject.StartsWith("physicalStorageDevice:", StringComparison.Ordinal)));
                    d.AcceptedStorageMeasurementClaims = claims.Count(claim => claim.ClaimType == "Measurement" &&
                        (claim.Subject == "physicalStorageSummary" ||
                         claim.Subject.StartsWith("physicalStorageDevice:", StringComparison.Ordinal) ||
                         claim.Subject.StartsWith("volume:", StringComparison.Ordinal)));
                }
            });
            var workingSetAfter = Environment.WorkingSet;
            diagnostics?.Update(d =>
            {
                d.InferenceCompletedAt = DateTimeOffset.UtcNow;
                d.AssistantElapsedMilliseconds = watch.Elapsed.TotalMilliseconds;
                d.ProcessWorkingSetAfterBytes = workingSetAfter;
            });
            scheduler.RecordRoleOutcome(model.Id, role, true, watch.Elapsed.TotalMilliseconds);
            token.ThrowIfCancellationRequested();
            return new(answer ?? AetherAnswerValidator.Deterministic(evidence), model.Id, provider.Info.Id, routing, request.PromptHash, context.Length,
                response.InputTokens, response.OutputTokens, watch.Elapsed.TotalMilliseconds, response.GenerationSeconds,
                response.LoadSeconds, response.FirstTokenSeconds, before, Environment.WorkingSet,
                provider.Info.Kind is not (FabricProviderKind.Mock or FabricProviderKind.Deterministic),
                task == "assistant-report"
                    ? "FastAssistant-Antwortvertrag und Belegzuordnung geprüft; Inhalt anhand der Quellen prüfen"
                    : "Diagnosevertrag und Belegzuordnung geprüft; freie Modellinterpretationen bleiben prüfpflichtig")
            {
                DiagnosticReport = report, AnswerValidated = true, ModelAttempted = true,
                ModelInferenceSucceeded = true, AttemptedModelId = model.Id
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or
            InvalidOperationException or UnauthorizedAccessException or FabricContractException or FabricResourceException ||
            ex is OperationCanceledException && !token.IsCancellationRequested)
        {
            diagnostics?.Update(d =>
            {
                d.InferenceSucceeded = inferenceSucceeded;
                d.RealModel = inferenceSucceeded &&
                    provider.Info.Kind is not (FabricProviderKind.Mock or FabricProviderKind.Deterministic);
                if (validationStarted) d.ValidatorSucceeded = false;
                if (ex is FabricContractException { Stage: "OutputTruncated" } truncated)
                {
                    d.FallbackReason = "OutputTruncated";
                    d.FallbackDetail = truncated.Diagnostic;
                }
                if (ex is FabricContractException contractError)
                {
                    if (d.InitialValidationReason is null)
                    {
                        d.InitialValidationReason = contractError.Diagnostic;
                        d.InitialRejectedClaim = contractError.RejectedClaim;
                    }
                    if (validatingRepair) d.RepairRejectedClaim = contractError.RejectedClaim;
                }
                d.InferenceCompletedAt = DateTimeOffset.UtcNow;
                d.AssistantElapsedMilliseconds = watch.Elapsed.TotalMilliseconds;
                d.ProcessWorkingSetAfterBytes = Environment.WorkingSet;
            });
            if (inferenceSucceeded && validationStarted)
                scheduler.RecordRoleOutcome(model.Id, role, false, watch.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    private static Guid? FindUniqueClaimEvidence(FabricFastAssistantClaim claim,
        FabricRejectedClaimDiagnostic rejectedClaim, ImmutableArray<FabricEvidence> visibleEvidence,
        ImmutableArray<FabricEvidence> sourceEvidence)
    {
        // An unknown cited ID cannot constrain the target; the unique validating candidate across
        // the expected evidence fields (or the claim's own subject field) binds deterministically.
        var original = visibleEvidence.SingleOrDefault(item => item.Id == claim.Evidence[0]);
        ImmutableArray<string> candidateFields = rejectedClaim.ExpectedEvidenceFields.IsDefaultOrEmpty
            ? [claim.Subject]
            : rejectedClaim.ExpectedEvidenceFields;
        candidateFields = candidateFields.Add(claim.Subject).Distinct(StringComparer.Ordinal).ToImmutableArray();
        var matches = new List<Guid>();
        foreach (var candidate in visibleEvidence.Where(item =>
                     (original is null || item.TargetId == original.TargetId) &&
                     candidateFields.Contains(item.Field) &&
                     AetherAnswerValidator.IsCurrentEvidence(item, DateTimeOffset.UtcNow)))
        {
            try
            {
                AetherAnswerValidator.ValidateFastAssistantClaim(claim with { Evidence = [candidate.Id] },
                    rejectedClaim.FindingIndex, rejectedClaim.ClaimIndex, [candidate], sourceEvidence);
                matches.Add(candidate.Id);
            }
            catch (FabricContractException) { }
        }
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string? IdentityTextLabel(FabricFastAssistantClaim claim) => (claim.Subject, claim.Property) switch
    {
        ("windowsIdentity", "productName") => "Windows",
        ("windowsIdentity", "edition") => "Windows-Edition",
        ("windowsIdentity", "release") => "Windows-Version",
        ("windowsIdentity", "build") => "Windows-Build",
        ("windowsIdentity", "architecture") => "Windows-Architektur",
        ("cpuName", "value") => "CPU",
        ("gpuName", "value") => "GPU",
        _ => null
    };

    private static FabricFastFinding? CanonicalIdentityFinding(FabricFastFinding finding, int rejectedClaimIndex,
        ImmutableArray<FabricEvidence> visibleEvidence, ImmutableArray<FabricEvidence> sourceEvidence)
    {
        if (finding.Claims.Length <= 1 || rejectedClaimIndex < 0 || rejectedClaimIndex >= finding.Claims.Length)
            return null;
        var claims = ImmutableArray.CreateBuilder<FabricFastAssistantClaim>();
        foreach (var claim in finding.Claims.Where((_, index) => index != rejectedClaimIndex))
        {
            if (claim.ClaimType != "Identity" || claim.Operator != "Equals" || claim.Unit.Length != 0 ||
                string.IsNullOrWhiteSpace(claim.Value) || claim.Evidence.Length != 1 ||
                IdentityTextLabel(claim) is null)
                continue;
            var citedEvidence = visibleEvidence.Where(item => item.Id == claim.Evidence[0]).ToImmutableArray();
            try
            {
                AetherAnswerValidator.ValidateFastAssistantClaim(claim, 0, 0, citedEvidence, sourceEvidence);
            }
            catch (FabricContractException)
            {
                continue;
            }
            claims.Add(claim);
        }

        if (claims.Count == 0) return null;
        var retainedClaims = claims.ToImmutable();
        var text = string.Join("; ", retainedClaims.Select(claim =>
            $"{IdentityTextLabel(claim)}: {claim.Value}")) + ".";
        if (text.Length > 140) return null;
        return new FabricFastFinding(text,
            retainedClaims.SelectMany(claim => claim.Evidence).Distinct().ToImmutableArray(), retainedClaims);
    }

    private static FabricFastFinding? CanonicalIdentityTextFinding(FabricFastFinding finding,
        ImmutableArray<FabricEvidence> visibleEvidence, ImmutableArray<FabricEvidence> sourceEvidence)
    {
        if (finding.Claims.IsDefaultOrEmpty) return null;
        foreach (var claim in finding.Claims)
        {
            if (claim.ClaimType != "Identity" || claim.Operator != "Equals" || claim.Unit.Length != 0 ||
                string.IsNullOrWhiteSpace(claim.Value) || claim.Evidence.Length != 1 ||
                IdentityTextLabel(claim) is null)
                return null;
            var citedEvidence = visibleEvidence.Where(item => item.Id == claim.Evidence[0]).ToImmutableArray();
            try { AetherAnswerValidator.ValidateFastAssistantClaim(claim, 0, 0, citedEvidence, sourceEvidence); }
            catch (FabricContractException) { return null; }
        }

        var text = string.Join("; ", finding.Claims.Select(claim =>
            $"{IdentityTextLabel(claim)}: {claim.Value}")) + ".";
        if (text.Length > 140) return null;
        return new FabricFastFinding(text,
            finding.Claims.SelectMany(claim => claim.Evidence).Distinct().ToImmutableArray(), finding.Claims);
    }

    private static string? CanonicalMeasurementText(FabricRejectedClaimDiagnostic claim, string display)
    {
        if (claim.ValidatorRule == "measurement.deterministic-byte-format")
            return claim.ExpectedEvidenceFields.FirstOrDefault() switch
            {
                "memoryTotalBytes" => $"Arbeitsspeicher gesamt: {display}.",
                "memoryAvailableBytes" => $"Arbeitsspeicher verfügbar: {display}.",
                "gpuVramTotalBytes" => $"Grafikspeicher gesamt: {display}.",
                "gpuVramAvailableBytes" => $"Grafikspeicher verfügbar: {display}.",
                _ => null
            };
        if (claim.ValidatorRule == "storage.formatted-total-capacity")
            return $"Die physische Gesamtkapazität beträgt {display}.";
        if (claim.ValidatorRule == "volume.formatted-measurement" &&
            claim.Subject.StartsWith("volume:", StringComparison.Ordinal))
        {
            var detail = claim.Property switch
            {
                "free" => "ist " + display + " frei",
                "used" => "sind " + display + " belegt",
                "capacity" => "hat eine Kapazität von " + display,
                _ => null
            };
            return detail is null ? null : $"Auf {claim.Subject["volume:".Length..]} {detail}.";
        }
        return null;
    }

    internal static FabricFastAssistantAnswer CanonicalizeClaimShapes(FabricFastAssistantAnswer candidate,
        ImmutableArray<FabricEvidence> visibleEvidence, ImmutableArray<FabricEvidence> sourceEvidence,
        FabricRunDiagnostics? diagnostics)
    {
        // Deterministic shape canonicalization: claim type, unit, registered property aliases, or a
        // missing identity value appended to the finding text are rewritten deterministically; every
        // value was already verified to exactly match the cited evidence before these rules fire.
        while (true)
        {
            FabricRejectedClaimDiagnostic? rejected;
            try
            {
                AetherAnswerValidator.ParseFastAssistant(candidate, visibleEvidence, sourceEvidence);
                return candidate;
            }
            catch (FabricContractException error)
            {
                rejected = error.RejectedClaim;
            }
            if (rejected is { ValidatorRule: "measurement.text-coverage" or "windows.text-coverage", FindingIndex: < 0 })
            {
                // Numbers and Windows identity statements in the summary or in recommendations can
                // never cite evidence; they are neutralized deterministically instead of spending
                // the single model repair.
                Func<string, bool> offending = rejected.ValidatorRule == "measurement.text-coverage"
                    ? AetherAnswerValidator.ContainsMeasurement
                    : AetherAnswerValidator.ContainsWindowsIdentityStatement;
                var summaryNeutralized = offending(candidate.Summary);
                var cleanRecommendations = candidate.Recommendations
                    .Where(text => !offending(text)).ToImmutableArray();
                if (!summaryNeutralized && cleanRecommendations.Length == candidate.Recommendations.Length)
                    return candidate;
                candidate = candidate with
                {
                    Summary = summaryNeutralized
                        ? "Der aktuelle Systemstatus wurde ausgewertet."
                        : candidate.Summary,
                    Recommendations = cleanRecommendations
                };
                diagnostics?.Update(d => d.ClaimShapeCanonicalized = true);
                continue;
            }
            if (rejected is null ||
                rejected.FindingIndex < 0 || rejected.FindingIndex >= candidate.Findings.Length)
                return candidate;
            var finding = candidate.Findings[rejected.FindingIndex];
            if (finding.Claims.IsDefault || rejected.ClaimIndex < 0 || rejected.ClaimIndex >= finding.Claims.Length)
                return candidate;
            var claim = finding.Claims[rejected.ClaimIndex];
            var repairedClaim = rejected.ValidatorRule switch
            {
                "storage.count-claim-shape" when rejected.ExpectedValue is { } deviceCountValue &&
                    claim.Value == deviceCountValue &&
                    !(claim.ClaimType == "Count" && claim.Unit.Equals("devices", StringComparison.OrdinalIgnoreCase)) =>
                    claim with { ClaimType = "Count", Unit = "devices" },
                "volume.count-claim-shape" when rejected.ExpectedValue is { } volumeCountValue &&
                    claim.Value == volumeCountValue &&
                    !(claim.ClaimType == "Count" && claim.Unit.Equals("volumes", StringComparison.OrdinalIgnoreCase)) =>
                    claim with { ClaimType = "Count", Unit = "volumes" },
                "storage.availability-property-shape" =>
                    claim with
                    {
                        Property = AetherAnswerValidator.CanonicalStorageDetailProperty(claim.Property) ?? claim.Property,
                        Operator = "UnavailableCount",
                        Unit = "devices"
                    },
                "evidence.scalar-value" when claim.Operator == "Equals" && claim.Property == "value" &&
                    visibleEvidence.SingleOrDefault(item =>
                        item.Id == claim.Evidence[0] && item.Field == claim.Subject) is { } scalarEvidence &&
                    AetherAnswerValidator.EvidenceClaimValue(scalarEvidence,
                        out var scalarUnit, out var scalarType) is { } scalarValue &&
                    ScalarValueMatches(claim.Value, scalarValue) &&
                    !(claim.ClaimType == scalarType && claim.Value == scalarValue &&
                      claim.Unit.Equals(scalarUnit, StringComparison.OrdinalIgnoreCase)) =>
                    claim with { ClaimType = scalarType, Value = scalarValue, Unit = scalarUnit },
                "measurement.deterministic-byte-format" or "storage.formatted-total-capacity" or
                "storage.formatted-device-capacity" or "volume.formatted-measurement" when
                    AetherAnswerValidator.TryDisplayParts(rejected.ExpectedValue,
                        out var displayValue, out var displayUnit) &&
                    ScalarValueMatches(claim.Value, displayValue) &&
                    !(claim.ClaimType == "Measurement" && claim.Operator == "Equals" &&
                      claim.Value == displayValue && claim.Unit.Equals(displayUnit, StringComparison.OrdinalIgnoreCase)) =>
                    claim with { ClaimType = "Measurement", Operator = "Equals", Value = displayValue, Unit = displayUnit },
                "claim.evidence" when claim.Operator == "Equals" && claim.Property == "value" &&
                    visibleEvidence.Where(item => item.Field == claim.Subject &&
                            AetherAnswerValidator.IsCurrentEvidence(item, DateTimeOffset.UtcNow))
                        .ToArray() is { Length: 1 } scalarCandidates &&
                    AetherAnswerValidator.EvidenceClaimValue(scalarCandidates[0],
                        out var fieldUnit, out var fieldType) is { } fieldValue &&
                    ScalarValueMatches(claim.Value, fieldValue) =>
                    claim with
                    {
                        ClaimType = fieldType,
                        Value = fieldValue,
                        Unit = fieldUnit,
                        Evidence = [scalarCandidates[0].Id]
                    },
                "claim.evidence" when CanonicalizeScalarIdentityClaim(claim, visibleEvidence) is { } scalarIdentityClaim =>
                    scalarIdentityClaim,
                "claim.schema" when claim is { ClaimType: "Identity", Operator: "Equals" } &&
                    (string.IsNullOrWhiteSpace(claim.Property) || claim.Property is "value" or "name") &&
                    visibleEvidence.Where(item => item.Field == claim.Subject &&
                            AetherAnswerValidator.IsCurrentEvidence(item, DateTimeOffset.UtcNow))
                        .ToArray() is { Length: 1 } identityCandidates &&
                    AetherAnswerValidator.EvidenceClaimValue(identityCandidates[0],
                        out var identityUnit, out var identityType) is { Length: > 0 and <= 100 } identityFieldValue &&
                    identityType == "Identity" &&
                    (string.IsNullOrWhiteSpace(claim.Value) || claim.Value == identityFieldValue) =>
                    // The canonical value is machine-backed; the subsequent revalidation still
                    // requires the finding text to state it.
                    claim with
                    {
                        Property = "value",
                        Value = identityFieldValue,
                        Unit = identityUnit,
                        Evidence = [identityCandidates[0].Id]
                    },
                "windows.identity" or "claim.evidence" when CanonicalizeWindowsIdentityPropertyAlias(
                    claim, visibleEvidence, sourceEvidence) is { } canonicalWindowsClaim =>
                    canonicalWindowsClaim,
                "volume.summary-evidence-binding" when
                    claim.Subject.StartsWith("volumeSummary/", StringComparison.Ordinal) &&
                    AetherAnswerValidator.TryDisplayParts(rejected.ExpectedValue,
                        out var volumeValue, out var volumeUnit) &&
                    claim.Value == volumeValue && claim.Unit.Equals(volumeUnit, StringComparison.OrdinalIgnoreCase) &&
                    visibleEvidence.Any(item => item.Id == claim.Evidence[0] && item.Field == "volumeSummary") =>
                    claim with
                    {
                        Subject = claim.Subject.StartsWith("volumeSummary/drive:", StringComparison.Ordinal)
                            ? "volume:" + claim.Subject["volumeSummary/drive:".Length..]
                            : "volume:" + claim.Subject["volumeSummary/".Length..]
                    },
                _ when (rejected.ValidatorRule.EndsWith("-evidence-binding", StringComparison.Ordinal) ||
                        rejected.ValidatorRule == "claim.evidence") &&
                    claim.Evidence.Length == 1 &&
                    FindUniqueClaimEvidence(claim, rejected, visibleEvidence, sourceEvidence) is { } reboundEvidence &&
                    reboundEvidence != claim.Evidence[0] =>
                    claim with { Evidence = [reboundEvidence] },
                _ => null
            };
            var repairedClaims = repairedClaim is null
                ? finding.Claims
                : finding.Claims.SetItem(rejected.ClaimIndex, repairedClaim);
            var repairedFinding = repairedClaim is not null
                ? finding with
                {
                    Claims = repairedClaims,
                    Evidence = repairedClaims.SelectMany(item => item.Evidence).Distinct().ToImmutableArray()
                }
                : rejected.ValidatorRule == "claim.text-identity" &&
                  finding.Claims.Length > 1 &&
                  finding.Claims.Any(item => item.Subject is "cpuName" or "gpuName") &&
                  CanonicalIdentityTextFinding(finding, visibleEvidence, sourceEvidence) is { } canonicalIdentityText
                    ? canonicalIdentityText
                : rejected.ValidatorRule == "claim.text-identity" &&
                  rejected.ExpectedValue is { Length: > 0 } identityValue && claim.Value == identityValue &&
                  IdentityTextLabel(claim) is { } label &&
                  $"{finding.Text.TrimEnd()} {label}: {identityValue}." is { Length: <= 140 } expandedText
                    ? finding with { Text = expandedText }
                    : null;
            if (repairedFinding is null &&
                rejected.ValidatorRule is ("claim.evidence" or "windows.identity" or "claim.schema") &&
                claim.Subject is ("windowsIdentity" or "cpuName" or "gpuName") &&
                CanonicalIdentityFinding(finding, rejected.ClaimIndex, visibleEvidence, sourceEvidence) is { } safeIdentityFinding)
                repairedFinding = safeIdentityFinding;
            if (repairedFinding is null) return candidate;
            candidate = candidate with
            {
                Findings = candidate.Findings.SetItem(rejected.FindingIndex, repairedFinding)
            };
            var evidenceRebound = repairedClaim is not null &&
                !repairedClaim.Evidence.SequenceEqual(claim.Evidence);
            diagnostics?.Update(d =>
            {
                d.ClaimShapeCanonicalized = true;
                if (evidenceRebound) d.ClaimEvidenceRebound = true;
            });
        }
    }

    private static FabricFastAssistantClaim? CanonicalizeScalarIdentityClaim(
        FabricFastAssistantClaim claim, ImmutableArray<FabricEvidence> visibleEvidence)
    {
        if (claim is not { ClaimType: "Identity", Subject: "cpuName" or "gpuName", Operator: "Equals" } ||
            claim.Property is not ("name" or "value") || claim.Unit.Length != 0 ||
            claim.Evidence.Length != 1)
            return null;

        var candidates = visibleEvidence.Where(item => item.Field == claim.Subject &&
            AetherAnswerValidator.IsCurrentEvidence(item, DateTimeOffset.UtcNow)).ToArray();
        if (candidates.Length != 1 ||
            AetherAnswerValidator.EvidenceClaimValue(candidates[0], out var unit, out var type) is not { } value ||
            type != "Identity" || unit.Length != 0 || claim.Value != value)
            return null;

        return claim with { Property = "value", Evidence = [candidates[0].Id] };
    }

    private static FabricFastAssistantClaim? CanonicalizeWindowsIdentityPropertyAlias(
        FabricFastAssistantClaim claim, ImmutableArray<FabricEvidence> visibleEvidence,
        ImmutableArray<FabricEvidence> sourceEvidence)
    {
        if (claim is not { ClaimType: "Identity", Subject: "windowsIdentity", Operator: "Equals" } ||
            claim.Evidence.Length != 1 || claim.Unit.Length != 0 ||
            AetherAnswerValidator.CanonicalWindowsIdentityProperty(claim.Property) is not { } property ||
            property == claim.Property)
            return null;

        var evidence = visibleEvidence.SingleOrDefault(item => item.Id == claim.Evidence[0] &&
            item.Field == "windowsIdentity" && AetherAnswerValidator.IsCurrentEvidence(item, DateTimeOffset.UtcNow));
        if (evidence is null) return null;

        var identity = AetherContextManager.ReadWindowsIdentity(evidence, sourceEvidence);
        return AetherAnswerValidator.WindowsIdentityClaimValue(identity, property) is { } canonicalValue &&
            claim.Value == canonicalValue
                ? claim with { Property = property }
                : null;
    }

    private static ImmutableArray<FabricFastFinding> NormalizeDeterministicMeasurementFindings(
        ImmutableArray<FabricFastFinding> findings, ImmutableArray<FabricEvidence> evidence,
        ImmutableArray<FabricEvidence> sourceEvidence)
    {
        var result = findings.ToBuilder();
        for (var index = 0; index < findings.Length; index++)
        {
            var finding = findings[index];
            if (finding.Claims.IsDefaultOrEmpty || finding.Claims.Length != 1 ||
                finding.Claims[0].Evidence.IsDefaultOrEmpty || finding.Claims[0].Evidence.Length != 1 ||
                !finding.Evidence.SequenceEqual(finding.Claims[0].Evidence))
                continue;
            var claim = finding.Claims[0];
            var cited = evidence.Where(item => item.Id == claim.Evidence[0]).ToImmutableArray();
            try
            {
                AetherAnswerValidator.ValidateFastAssistantClaim(claim, index, 0, cited, sourceEvidence);
            }
            catch (FabricContractException error)
            {
                var rejected = error.RejectedClaim;
                if (rejected is null ||
                    rejected.ValidatorRule is not ("measurement.deterministic-byte-format" or
                        "storage.formatted-total-capacity" or "volume.formatted-measurement") ||
                    !AetherAnswerValidator.TryDisplayParts(rejected.ExpectedValue,
                        out var expectedValue, out var expectedUnit) ||
                    CanonicalMeasurementText(rejected, expectedValue + " " + expectedUnit) is not { } text)
                    continue;
                var canonicalClaim = claim with
                {
                    ClaimType = "Measurement",
                    Subject = rejected.Subject,
                    Property = rejected.Property,
                    Operator = "Equals",
                    Value = expectedValue,
                    Unit = expectedUnit
                };
                result[index] = finding with { Text = text, Claims = [canonicalClaim] };
            }
        }
        return result.ToImmutable();
    }

    // Same numeric value in a different canonical formatting (decimal separator); never a conversion.
    private static bool ScalarValueMatches(string claimValue, string canonicalValue) =>
        claimValue.Equals(canonicalValue, StringComparison.Ordinal) ||
        decimal.TryParse(claimValue.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var claimed) &&
        decimal.TryParse(canonicalValue.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var canonical) &&
        claimed == canonical;

    private static FabricFastFinding? CreateCanonicalWindowsFinding(
        ImmutableArray<FabricEvidence> visibleEvidence, ImmutableArray<FabricEvidence> sourceEvidence)
    {
        var identityEvidence = visibleEvidence.FirstOrDefault(item => item.Field == "windowsIdentity" &&
            AetherAnswerValidator.IsCurrentEvidence(item, DateTimeOffset.UtcNow));
        if (identityEvidence is null) return null;
        FabricWindowsIdentity identity;
        try { identity = AetherContextManager.ReadWindowsIdentity(identityEvidence, sourceEvidence); }
        catch (FabricContractException) { return null; }
        var display = AetherIdentityContract.DisplayValue("windowsIdentity", "productName",
            identity.ProductName, identity);
        var text = $"{display} ist belegt.";
        return text.Length > 140 ? null : new(text, [identityEvidence.Id],
            [new("Identity", "windowsIdentity", "productName", "Equals", identity.ProductName, "",
                [identityEvidence.Id])]);
    }

    internal static FabricFastAssistantAnswer AddRequestedIdentityFindings(
        FabricFastAssistantAnswer answer, string input, ImmutableArray<FabricEvidence> visibleEvidence,
        ImmutableArray<FabricEvidence> sourceEvidence, FabricRunDiagnostics? diagnostics)
    {
        if (!Regex.IsMatch(input, @"(?i)\b(?:name|namen|bezeichnung|bezeichnungen)\b",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            return answer;
        try { AetherAnswerValidator.ParseFastAssistant(answer, visibleEvidence, sourceEvidence); }
        catch (FabricContractException) { return answer; }

        var findings = answer.Findings.ToBuilder();
        var added = false;
        foreach (var (subject, label, pattern) in new[]
                 {
                     ("cpuName", "CPU", @"(?i)\b(?:cpu|prozessor)\w*\b"),
                     ("gpuName", "GPU", @"(?i)\b(?:gpu|grafikkarte)\w*\b")
                 })
        {
            if (!Regex.IsMatch(input, pattern, RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)) ||
                findings.SelectMany(finding => finding.Claims).Any(claim =>
                    claim.ClaimType == "Identity" && claim.Subject == subject) ||
                findings.Count >= 4)
                continue;

            var candidates = visibleEvidence.Where(item => item.Field == subject &&
                AetherAnswerValidator.IsCurrentEvidence(item, DateTimeOffset.UtcNow)).ToArray();
            if (candidates.Length != 1 ||
                AetherAnswerValidator.EvidenceClaimValue(candidates[0], out var unit, out var type) is not { } value ||
                type != "Identity" || unit.Length != 0 || value.Length > 100)
                continue;

            var evidence = candidates[0];
            var text = $"{(evidence.Simulated ? "SIMULIERT: " : "")}{label}: {value}.";
            if (text.Length > 140) continue;
            findings.Add(new FabricFastFinding(text, [evidence.Id],
                [new FabricFastAssistantClaim("Identity", subject, "value", "Equals", value, "", [evidence.Id])]));
            added = true;
        }

        if (!added) return answer;
        diagnostics?.Update(d => d.CanonicalIdentityFindingAdded = true);
        return answer with { Findings = findings.ToImmutable() };
    }

    internal static FabricFastAssistantAnswer AddRequestedStorageFinding(
        FabricFastAssistantAnswer answer, string input, ImmutableArray<FabricEvidence> visibleEvidence,
        ImmutableArray<FabricEvidence> sourceEvidence)
    {
        if (!Regex.IsMatch(input, @"(?i)\bdatenträger\w*\b", RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)) ||
            !Regex.IsMatch(input, @"(?i)\b(?:gesamt)?kapazität\w*\b", RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)))
            return answer;

        try { AetherAnswerValidator.ParseFastAssistant(answer, visibleEvidence, sourceEvidence); }
        catch (FabricContractException) { return answer; }

        var existingClaims = answer.Findings.SelectMany(finding => finding.Claims).ToArray();
        if (existingClaims.Any(claim => claim.ClaimType == "Availability" &&
                claim.Subject == "physicalStorageSummary" && claim.Property == "totalCapacity") &&
            existingClaims.Any(claim => claim.ClaimType == "Measurement" &&
                claim.Subject == "physicalStorageSummary" && claim.Property == "totalCapacity"))
            return answer;

        if (CreateCanonicalStorageFinding("physicalStorageSummary", visibleEvidence, sourceEvidence) is not { } finding)
            return answer;
        if (answer.Findings.Length < 3)
            return answer with { Findings = answer.Findings.Add(finding) };

        for (var index = 0; index < answer.Findings.Length; index++)
        {
            var existing = answer.Findings[index];
            var claims = existing.Claims.AddRange(finding.Claims);
            var evidence = existing.Evidence.AddRange(finding.Evidence).Distinct().ToImmutableArray();
            var text = existing.Text.TrimEnd() + " " + finding.Text;
            if (claims.Length > 5 || evidence.Length > 3 || text.Length > 140) continue;

            var combined = answer with
            {
                Findings = answer.Findings.SetItem(index, new FabricFastFinding(text, evidence, claims))
            };
            try
            {
                AetherAnswerValidator.ParseFastAssistant(combined, visibleEvidence, sourceEvidence);
                return combined;
            }
            catch (FabricContractException) { }
        }
        return answer;
    }

    private static FabricFastFinding? CreateCanonicalStorageFinding(string field,
        ImmutableArray<FabricEvidence> visibleEvidence, ImmutableArray<FabricEvidence> sourceEvidence)
    {
        var summaryField = field == "physicalStorageSummary" ? "physicalStorageSummary" : "volumeSummary";
        var summaryEvidence = visibleEvidence.SingleOrDefault(item => item.Field == summaryField);
        if (summaryEvidence is null) return null;
        if (summaryField == "physicalStorageSummary")
        {
            var summary = AetherContextManager.ReadPhysicalStorageSummary(summaryEvidence, sourceEvidence);
            var count = summary.DeviceCount.ToString(CultureInfo.InvariantCulture);
            var deviceWord = summary.DeviceCount == 1 ? "Gerät" : "Geräte";
            var capacity = "";
            var unit = "";
            var available = summary.TotalCapacityDisplay is { } display &&
                AetherAnswerValidator.TryDisplayParts(display, out capacity, out unit);
            var claims = ImmutableArray.CreateBuilder<FabricFastAssistantClaim>();
            claims.Add(new("Count", summaryField, "deviceCount", "Equals", count, "devices", [summaryEvidence.Id]));
            claims.Add(new("Availability", summaryField, "totalCapacity", "Equals",
                available ? "Available" : "Unavailable", "", [summaryEvidence.Id]));
            var text = $"Physisches Speicherinventar: {count} {deviceWord}; die Gesamtkapazität ist " +
                (available ? $"{summary.TotalCapacityDisplay}." : "nicht verfügbar.");
            if (available)
                claims.Add(new("Measurement", summaryField, "totalCapacity", "Equals",
                    capacity, unit, [summaryEvidence.Id]));
            return new(text, [summaryEvidence.Id], claims.ToImmutable());
        }
        var volumeCount = AetherContextManager.ReadVolumeSummary(summaryEvidence, sourceEvidence).Volumes.Length;
        var volumeValue = volumeCount.ToString(CultureInfo.InvariantCulture);
        return new($"Das inventarisierte System umfasst {volumeValue} Volumes.", [summaryEvidence.Id],
            [new("Count", summaryField, "volumeCount", "Equals", volumeValue, "volumes", [summaryEvidence.Id])]);
    }

    private static FabricFastAssistantAnswer RemoveStorageFindings(FabricFastAssistantAnswer answer,
        int[] indexes, string field, ImmutableArray<FabricEvidence> visibleEvidence,
        ImmutableArray<FabricEvidence> sourceEvidence)
    {
        var findings = answer.Findings.Where((_, index) => !indexes.Contains(index)).ToImmutableArray();
        if (findings.IsEmpty && CreateCanonicalStorageFinding(field, visibleEvidence, sourceEvidence) is { } safeFinding)
            findings = [safeFinding];
        findings = NormalizeDeterministicMeasurementFindings(findings, visibleEvidence, sourceEvidence);
        return RemoveUncitedStorageText(answer with { Findings = findings });
    }

    private static FabricFastAssistantAnswer RemoveUncitedStorageText(FabricFastAssistantAnswer answer) =>
        answer with
        {
            Summary = AetherAnswerValidator.HasPhysicalStorageTerms(answer.Summary) ||
                AetherAnswerValidator.HasVolumeTerms(answer.Summary)
                    ? "Der aktuelle Systemstatus wurde ausgewertet."
                    : answer.Summary,
            Recommendations = answer.Recommendations.Where(text =>
                !AetherAnswerValidator.HasPhysicalStorageTerms(text) &&
                !AetherAnswerValidator.HasVolumeTerms(text)).ToImmutableArray()
        };

    private static IEnumerable<int> StorageFindingIndexes(FabricFastAssistantAnswer answer, string field)
    {
        var physical = field == "physicalStorageSummary";
        return answer.Findings.Select((finding, index) => (finding, index))
            .Where(item => item.finding.Claims.Any(claim => physical
                    ? claim.Subject == "physicalStorageSummary" ||
                  claim.Subject.StartsWith("physicalStorageSummary", StringComparison.Ordinal) ||
                      claim.Subject.StartsWith("physicalStorageDevice:", StringComparison.Ordinal)
                : claim.Subject.StartsWith("volumeSummary", StringComparison.Ordinal) ||
                      claim.Subject.StartsWith("volume:", StringComparison.Ordinal) ||
                  claim.Subject.StartsWith("volumeSummary\\", StringComparison.Ordinal)) ||
                (physical
                    ? AetherAnswerValidator.HasPhysicalStorageTerms(item.finding.Text)
                    : AetherAnswerValidator.HasVolumeTerms(item.finding.Text)))
            .Select(item => item.index);
    }

    private static void EnsureRepairPreservesOtherClaims(FabricFastAssistantAnswer initial,
        FabricFastAssistantAnswer repaired, FabricRejectedClaimDiagnostic rejectedClaim)
    {
        var index = rejectedClaim.FindingIndex;
        // Rejected summary or recommendation texts (no finding target) are repaired as free text;
        // the full answer is still completely revalidated afterwards.
        if (index < 0 && rejectedClaim.ClaimIndex < 0) return;
        if (index < 0 || index >= initial.Findings.Length ||
            initial.Summary != repaired.Summary ||
            !initial.Recommendations.SequenceEqual(repaired.Recommendations, StringComparer.Ordinal))
            throw RepairContractError("Repair hat neben dem beanstandeten Claim weitere Antwortteile verändert.",
                rejectedClaim);

        var removed = repaired.Findings.Length == initial.Findings.Length - 1;
        if (!removed && repaired.Findings.Length != initial.Findings.Length)
            throw RepairContractError("Repair hat die Anzahl nicht betroffener Befunde verändert.", rejectedClaim);

        for (var originalIndex = 0; originalIndex < initial.Findings.Length; originalIndex++)
        {
            if (originalIndex == index)
            {
                if (removed) continue;
                var before = initial.Findings[originalIndex];
                var after = repaired.Findings[originalIndex];
                // Finding-text rules (ClaimIndex < 0) require unchanged claims and a changed text;
                // claim rules require exactly the rejected claim or, for text-identity, the text to change.
                if (rejectedClaim.ClaimIndex < 0
                        ? SerializeClaimSequence(before.Claims) != SerializeClaimSequence(after.Claims) ||
                          !before.Evidence.SequenceEqual(after.Evidence) ||
                          before.Text == after.Text
                        : rejectedClaim.ClaimIndex >= before.Claims.Length ||
                          before.Claims.Length != after.Claims.Length ||
                          SerializeClaimSequence(before.Claims.Where((_, claimIndex) =>
                                  claimIndex != rejectedClaim.ClaimIndex)) !=
                          SerializeClaimSequence(after.Claims.Where((_, claimIndex) =>
                                  claimIndex != rejectedClaim.ClaimIndex)) ||
                          SerializeFastAssistantFinding(before with { Text = "" }) ==
                          SerializeFastAssistantFinding(after with { Text = "" }) &&
                          (rejectedClaim.ValidatorRule != "claim.text-identity" || before.Text == after.Text))
                    throw RepairContractError("Repair hat nicht ausschließlich den beanstandeten Claim korrigiert.",
                        rejectedClaim);
                continue;
            }

            var repairedIndex = removed && originalIndex > index ? originalIndex - 1 : originalIndex;
            if (SerializeFastAssistantFinding(initial.Findings[originalIndex]) !=
                SerializeFastAssistantFinding(repaired.Findings[repairedIndex]))
                throw RepairContractError("Repair hat einen anderen Befund oder dessen EvidenceIds verändert.",
                    rejectedClaim);
        }
    }

    private static string SerializeFastAssistantFinding(FabricFastFinding finding) =>
        JsonSerializer.Serialize(finding, FabricJson.Options);

    private static string SerializeClaimSequence(IEnumerable<FabricFastAssistantClaim> claims) =>
        JsonSerializer.Serialize(claims, FabricJson.Options);

    private static FabricContractException RepairContractError(string reason,
        FabricRejectedClaimDiagnostic rejectedClaim) =>
        new("FastAssistant-Repair", reason, $"findings[{rejectedClaim.FindingIndex}]",
            "nur der beanstandete Claim korrigiert", "andere Antwortteile verändert",
            "Korrigiere ausschließlich den beanstandeten Claim oder entferne den betroffenen Befund. " +
            "Alle übrigen Befunde, Claims und EvidenceIds unverändert lassen. Kein neuer Evidence-Scope.",
            rejectedClaim);

    private static bool IsOutputTruncated(string? doneReason) =>
        string.Equals(doneReason, "length", StringComparison.OrdinalIgnoreCase);

    private static FabricContractException OutputTruncatedException() =>
        new("OutputTruncated", "Modellantwort erreichte das Ausgabelimit und war unvollständig.",
            "done_reason", "vollständige Ausgabe", "length");
}

public sealed record FabricModelCandidate(string Id, string Name, string Family, string IntendedRole,
    string AcquisitionStatus);
public enum FabricModelLoadState { NotInstalled, Registered, Checking, Verified, Loading, Ready, Error, ProviderMissing, Unsupported }
public enum FabricModelOperation { None, Checking, Loading, Unloading }
public sealed record FabricModelRuntime(string ModelId, FabricModelLoadState State, string Detail, DateTimeOffset At)
{
    public FabricModelOperation Operation { get; init; }
}

public sealed class AetherModelLifecycle
{
    private readonly Dictionary<string, FabricModelRuntime> _states = [];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _modelGates = new(StringComparer.Ordinal);
    public FabricModelRuntime Status(string id)
    {
        lock (_states) return _states.GetValueOrDefault(id) ??
            new(id, FabricModelLoadState.NotInstalled, "Kein Server-/Ladenachweis vorhanden.", DateTimeOffset.UtcNow);
    }
    private void Set(string id, FabricModelLoadState state, string detail, FabricModelOperation operation = FabricModelOperation.None)
    { lock (_states) _states[id] = new(id, state, detail, DateTimeOffset.UtcNow) { Operation = operation }; }
    public void Registered(FabricModel model) =>
        Set(model.Id, FabricModelLoadState.Registered,
            "Manifest importiert und registriert; Servermodell und Digest noch nicht geprüft.");
    public async Task CheckAsync(FabricModel model, CompatibleInferenceProvider provider, CancellationToken token)
    {
        Set(model.Id, FabricModelLoadState.Checking, "Modellidentität und Serverstatus werden geprüft.", FabricModelOperation.Checking);
        try
        {
            await provider.CheckModelAsync(model, token).ConfigureAwait(false);
            var loaded = provider.Info.Kind == FabricProviderKind.Ollama &&
                await provider.IsLoadedAsync(model, token).ConfigureAwait(false);
            Set(model.Id, loaded ? FabricModelLoadState.Ready : FabricModelLoadState.Verified,
                loaded ? "Server meldet das freigegebene Modell aktuell geladen; Zustand kann sich ändern." :
                    "Servermodell angeboten; kein aktueller Resident-Ladenachweis.", FabricModelOperation.Checking);
        }
        catch (HttpRequestException ex)
        { Set(model.Id, FabricModelLoadState.ProviderMissing, "Lokaler Modellprovider ist nicht erreichbar: " + ex.GetType().Name, FabricModelOperation.Checking); throw; }
        catch (Exception ex) { Set(model.Id, FabricModelLoadState.Error, "Server-/Identitätsprüfung nicht bestätigt: " + ex.Message, FabricModelOperation.Checking); throw; }
    }
    public async Task LoadAsync(FabricModel model, CompatibleInferenceProvider provider, AetherResourceGovernor governor,
        CancellationToken token)
    {
        var request = new FabricRequest(1, Guid.NewGuid(), "inventory-review", "de",
            AetherContextManager.ProfilePrompt(256), AetherPromptRegistry.Hash, model.Id,
            Math.Min(4096, model.DeclaredContextTokens), 512, 30, DataClassification.Normal);
        governor.Check(model, request, 1, FabricMode.LocalOnly);
        Set(model.Id, FabricModelLoadState.Loading, "Explizites Laden angefordert.", FabricModelOperation.Loading);
        try
        {
            await provider.LoadAsync(model, token).ConfigureAwait(false);
            Set(model.Id, FabricModelLoadState.Ready, "Server bestätigt Laden; gemeinsam genutzter Zustand kann sich ändern.", FabricModelOperation.Loading);
        }
        catch { Set(model.Id, FabricModelLoadState.Error, "Laden nicht bestätigt.", FabricModelOperation.Loading); throw; }
    }
    public async Task EnsureReadyAsync(FabricModel model, CompatibleInferenceProvider provider,
        AetherResourceGovernor governor, CancellationToken token, IProgress<FabricProgressPhase>? progress = null,
        FabricRunDiagnostics? diagnostics = null)
    {
        if (provider.Info.Kind != FabricProviderKind.Ollama || !provider.IsLoopback)
            throw new NotSupportedException("Automatische lokale Modellverwaltung erfordert einen Ollama-Loopback-Provider.");
        var gate = _modelGates.GetOrAdd(model.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            diagnostics?.Update(d => d.EnsureReadyStarted = true);
            if (!governor.CanRefreshHardware)
                throw new FabricResourceException("Eine aktuelle lokale Hardwaremessung ist nicht verfügbar; das Modell wird nicht geladen.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            progress?.Report(FabricProgressPhase.CheckingHardware);
            await governor.RefreshAsync(model, deadline.Token).ConfigureAwait(false);
            var request = new FabricRequest(1, Guid.NewGuid(), "inventory-review", "de",
                AetherContextManager.ProfilePrompt(256), AetherPromptRegistry.Hash, model.Id,
                Math.Min(4096, model.DeclaredContextTokens), 512, 60, DataClassification.Sensitive);
            governor.Check(model, request, 1, FabricMode.LocalOnly);
            progress?.Report(FabricProgressPhase.CheckingRuntime);
            if (diagnostics is not null)
            {
                try
                {
                    var version = await provider.ReadOllamaVersionAsync(deadline.Token).ConfigureAwait(false);
                    diagnostics.Update(d => { d.ApiVersion = version; d.EndpointReachable = true; });
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException)
                {
                    diagnostics.Update(d =>
                    {
                        d.ApiVersion = "Nicht verfügbar"; d.ExceptionType = ex.GetType().FullName;
                        d.ExceptionDetail = "Ollama-Versionsabfrage fehlgeschlagen.";
                    });
                }
            }
            await CheckAsync(model, provider, deadline.Token).ConfigureAwait(false);
            diagnostics?.Update(d =>
            {
                d.EndpointReachable = provider.LastInventory?.Reachable;
                d.ModelFound = provider.LastInventory?.Found;
                d.ActualDigest = provider.LastInventory?.ActualDigest;
                d.Resident = provider.LastInventory?.Resident;
                d.ResidentModelBytes = provider.LastInventory?.ResidentModelBytes;
                d.ResidentModelVramBytes = provider.LastInventory?.ResidentModelVramBytes;
            });
            if (Status(model.Id).State == FabricModelLoadState.Ready)
            {
                diagnostics?.Update(d => d.EnsureReadySucceeded = true);
                return;
            }
            progress?.Report(FabricProgressPhase.LoadingModel);
            var loadWatch = Stopwatch.StartNew();
            var loadStartedAt = DateTimeOffset.UtcNow;
            diagnostics?.Update(d => { d.LoadStarted = true; d.ModelLoadStartedAt = loadStartedAt; });
            try
            {
                await LoadAsync(model, provider, governor, deadline.Token).ConfigureAwait(false);
                diagnostics?.Update(d =>
                {
                    d.LoadSucceeded = true; d.EnsureReadySucceeded = true;
                    d.Resident = provider.LastInventory?.Resident;
                    d.ResidentModelBytes = provider.LastInventory?.ResidentModelBytes;
                    d.ResidentModelVramBytes = provider.LastInventory?.ResidentModelVramBytes;
                });
            }
            catch
            {
                diagnostics?.Update(d => d.LoadSucceeded = false);
                throw;
            }
            finally
            {
                loadWatch.Stop();
                diagnostics?.Update(d =>
                {
                    d.ModelLoadCompletedAt = DateTimeOffset.UtcNow;
                    d.ModelLoadDurationMilliseconds = loadWatch.Elapsed.TotalMilliseconds;
                });
            }
        }
        finally { gate.Release(); }
    }
    public async Task UnloadAsync(FabricModel model, CompatibleInferenceProvider provider, CancellationToken token)
    {
        try
        {
            await provider.UnloadAsync(model, token).ConfigureAwait(false);
            Set(model.Id, FabricModelLoadState.Verified, "Server bestätigt Entladen; Modell bleibt installiert.", FabricModelOperation.Unloading);
        }
        catch { Set(model.Id, FabricModelLoadState.Error, "Entladen nicht bestätigt.", FabricModelOperation.Unloading); throw; }
    }
}
