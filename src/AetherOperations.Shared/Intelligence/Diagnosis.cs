using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AetherOperations.Intelligence;

public enum FabricDiagnosticProof { Measured, Derived, Correlated }
public enum FabricHypothesisConfidence { Unconfirmed, Plausible, StronglySupported }
public enum FabricSolutionRisk { ReadOnly, Low, Elevated, Unknown }

public sealed record FabricDiagnosticFinding(
    [property: JsonRequired] string Text,
    [property: JsonRequired] FabricDiagnosticProof Proof,
    [property: JsonRequired] ImmutableArray<Guid> Evidence,
    [property: JsonRequired] string Relevance)
{
    [JsonRequired] public string Id { get; init; } = "";
    [JsonRequired] public string Component { get; init; } = "";
}

public sealed record FabricDiagnosticHypothesis(
    [property: JsonRequired] string Text,
    [property: JsonRequired] ImmutableArray<Guid> Evidence,
    [property: JsonRequired] ImmutableArray<string> MissingOrContradictory,
    [property: JsonRequired] string Reason,
    [property: JsonRequired] FabricHypothesisConfidence Confidence)
{
    [JsonRequired] public string Id { get; init; } = "";
    [JsonRequired] public ImmutableArray<Guid> ContradictingEvidence { get; init; } = [];
}

public sealed record FabricDiagnosticCheck(
    [property: JsonRequired] string Text,
    [property: JsonRequired] int HypothesisIndex,
    [property: JsonRequired] Guid SourceEvidence,
    [property: JsonRequired] string Target,
    [property: JsonRequired] string Timeframe)
{
    [JsonRequired] public string Id { get; init; } = "";
    [JsonRequired] public string Reason { get; init; } = "";
    [JsonRequired] public string ExpectedResult { get; init; } = "";
    [JsonRequired] public string Module { get; init; } = "";
}

public sealed record FabricDiagnosticSolution(
    [property: JsonRequired] string Target,
    [property: JsonRequired] int HypothesisIndex,
    [property: JsonRequired] int CheckIndex,
    [property: JsonRequired] ImmutableArray<Guid> Evidence,
    [property: JsonRequired] string Reason,
    [property: JsonRequired] string Prerequisite,
    [property: JsonRequired] FabricSolutionRisk Risk,
    [property: JsonRequired] string Rollback,
    [property: JsonRequired] bool RequiresApproval)
{
    [JsonRequired] public string Id { get; init; } = "";
}

public sealed record FabricDiagnosticAction(
    [property: JsonRequired] string ActionId,
    [property: JsonRequired] ImmutableArray<Guid> Evidence);

public sealed record FabricDiagnosis(
    [property: JsonRequired] ImmutableArray<FabricDiagnosticFinding> Findings,
    [property: JsonRequired] ImmutableArray<FabricDiagnosticHypothesis> Hypotheses,
    [property: JsonRequired] ImmutableArray<FabricDiagnosticCheck> Checks,
    [property: JsonRequired] ImmutableArray<FabricDiagnosticSolution> Solutions,
    [property: JsonRequired] ImmutableArray<FabricDiagnosticAction> Actions);

public sealed record FabricDiagnosticReport(
    [property: JsonRequired] int Schema,
    [property: JsonRequired] string Language,
    [property: JsonRequired] ImmutableArray<Guid> SystemState,
    [property: JsonRequired] FabricDiagnosis Diagnosis);

public sealed record FabricDiagnosticActionDefinition(string Id, string Label, string Route,
    ImmutableArray<string> Fields);

public static class AetherDiagnosticActions
{
    public static ImmutableArray<FabricDiagnosticActionDefinition> All { get; } =
    [
        new("events.open", "Ereignisanalyse öffnen", "Ereignisse",
            ["systemErrors24h", "maximumEventRepetitions24h", "eventProvider", "eventId", "eventGroup."]),
        new("storage.open", "Hardwareübersicht öffnen", "HardwareIntelligence",
            ["freeBytes", "volumeFreeBytes", "volumeCapacityBytes", "physicalDrive.", "volume."]),
        new("process.open", "Prozessanalyse öffnen", "Taskmanager", ["processCount", "cpuPercent"]),
        new("system-analysis.open", "Systemanalyse öffnen", "SystemIntelligence",
            ["systemErrors24h", "eventProvider", "eventId", "eventGroup.", "cpuPercent", "memoryAvailableBytes"]),
        new("diagnostics.open", "Diagnoseübersicht öffnen", "Diagnose",
            ["systemErrors24h", "eventProvider", "eventId", "eventGroup."]),
        new("guardian.open", "Guardian öffnen", "Guardian", ["guardian."])
    ];

    public static bool Supports(FabricDiagnosticActionDefinition action, FabricEvidence evidence) =>
        SupportsField(action, evidence.Field);

    public static bool SupportsField(FabricDiagnosticActionDefinition action, string field) =>
        action.Fields.Any(allowedField => allowedField.EndsWith(".", StringComparison.Ordinal)
            ? field.StartsWith(allowedField, StringComparison.Ordinal)
            : field.Equals(allowedField, StringComparison.Ordinal));
}

public static class AetherDiagnosisContractValidator
{
    public static FabricDiagnosticReport Parse(string json, ImmutableArray<FabricEvidence> evidence, string? prompt = null)
    {
        try { using var document = JsonDocument.Parse(json); }
        catch (JsonException ex)
        {
            throw Failure($"Diagnoseantwort ist kein einzelnes gültiges JSON-Objekt " +
                $"(Länge {json.Length}, Zeile {ex.LineNumber ?? 0}, Byteposition {ex.BytePositionInLine ?? 0}).", "diagnosis");
        }
        var report = FabricJson.ParseModel<FabricDiagnosticReport>(json, "Diagnose-JSON");
        if (report.Schema != 2)
            throw Failure("Diagnoseschema muss Version 2 verwenden.", "schema");
        if (report.Language != "de")
            throw Failure("Diagnosesprache muss de sein.", "language");
        if (report.Diagnosis is null)
            throw Failure("Diagnoseobjekt fehlt.", "diagnosis");
        if (report.SystemState.IsDefault)
            throw Failure("Systemzustand fehlt.", "systemState");
        if (report.SystemState.Length > 64)
            throw Failure("Systemzustand überschreitet das Limit von 64 Belegen.", "systemState");
        var allowed = AetherAnswerValidator.AllowedEvidenceIds(evidence, DateTimeOffset.UtcNow);
        if (report.SystemState.Any(id => !allowed.Contains(id)))
            throw Failure("Systemzustand enthält unbekannte oder veraltete Belege.", "systemState");
        Validate(report.Diagnosis, evidence);
        if (prompt is not null)
            ValidateFocusedEventGroup(report, evidence, prompt);
        return report with { SystemState = report.SystemState.Distinct().ToImmutableArray() };
    }

    private static void ValidateFocusedEventGroup(FabricDiagnosticReport report,
        ImmutableArray<FabricEvidence> evidence, string prompt)
    {
        if (AetherPromptRegistry.FocusedEventGroup(prompt) is not { } focused)
            return;
        var source = evidence.SingleOrDefault(item => item.Id == focused.EvidenceId &&
            AetherToolBroker.IsEventGroupField(item.Field) &&
            AetherAnswerValidator.IsCurrentEvidence(item, DateTimeOffset.UtcNow));
        if (source is null || source.Metadata.SourceType != SourceType.Direct ||
            !report.SystemState.Contains(source.Id))
            throw Failure("Die konkrete Ereignisdiagnose muss ihren aktuellen direkten Ereignisbeleg im Systemzustand enthalten.",
                "systemState");

        var diagnosis = report.Diagnosis;
        if (diagnosis.Findings.Length != 1 || diagnosis.Hypotheses.Length != 1 ||
            diagnosis.Checks.Length != 1 || diagnosis.Solutions.Length != 1 ||
            diagnosis.Actions.Length != 1)
            throw Failure("Eine konkrete Einzelereignisdiagnose benötigt genau einen Befund, eine Hypothese, eine Prüfung, eine Lösung und eine Navigation.",
                "diagnosis");
        if (diagnosis.Findings[0].Proof != FabricDiagnosticProof.Measured ||
            diagnosis.Findings[0].Evidence.Length != 1 || diagnosis.Findings[0].Evidence[0] != source.Id ||
            !diagnosis.Hypotheses[0].Evidence.Contains(source.Id) ||
            diagnosis.Checks[0].SourceEvidence != source.Id ||
            !diagnosis.Solutions[0].Evidence.Contains(source.Id) ||
            !diagnosis.Actions[0].Evidence.Contains(source.Id))
            throw Failure("Alle Elemente der Einzelereignisdiagnose müssen auf der passenden direkten Ereignisgruppe beruhen.",
                "diagnosis");
    }

    public static FabricDiagnosis Validate(FabricDiagnosis diagnosis, ImmutableArray<FabricEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(diagnosis);
        if (diagnosis.Findings.IsDefault || diagnosis.Hypotheses.IsDefault || diagnosis.Checks.IsDefault ||
            diagnosis.Solutions.IsDefault || diagnosis.Actions.IsDefault || diagnosis.Findings.Length > 1 ||
            diagnosis.Hypotheses.Length > 2 || diagnosis.Checks.Length > 2 ||
            diagnosis.Solutions.Length > 2 || diagnosis.Actions.Length > 1)
            throw Failure("Diagnoselisten sind unvollständig oder überschreiten die AETHER-Grenzen 1/2/2/2/1.",
                "diagnosis");

        ImmutableArray<FabricEvidence> Cited(ImmutableArray<Guid> ids, string field)
        {
            if (ids.IsDefaultOrEmpty || ids.Length > 16 || ids.Distinct().Count() != ids.Length)
                throw Failure("Eine eindeutige, nicht leere Belegliste ist erforderlich.", field);
            var matches = evidence.Where(e => ids.Contains(e.Id) && AetherAnswerValidator.IsCurrentEvidence(e, DateTimeOffset.UtcNow)).ToImmutableArray();
            if (matches.Length != ids.Length || matches.Select(e => e.Id).Distinct().Count() != ids.Length)
                throw Failure("Beleg ist unbekannt, doppelt oder nicht aktuell.", field);
            return matches;
        }
        void CheckText(string text, ImmutableArray<FabricEvidence> cited, string field)
        {
            AetherAnswerValidator.ValidateText(text, 800, field);
            AetherAnswerValidator.ValidateWindowsProduct(text, cited);
            AetherAnswerValidator.ValidateMeasurements(text, cited);
            AetherAnswerValidator.ValidateIdentity(text, cited);
            if (cited.Any(e => e.Simulated) && !text.Contains("simuliert", StringComparison.OrdinalIgnoreCase))
                throw Failure("Simulierte Belege müssen gekennzeichnet sein.", field);
        }
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        void CheckId(string id, string field)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 48 || !identifiers.Add(id))
                throw Failure("Diagnose-ID fehlt, ist zu lang oder doppelt.", field);
        }
        static string Normalize(string text) => Regex.Replace(
            Regex.Replace(text.Trim(), @"[\p{P}\p{Z}\s]+", " ",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Trim(),
            @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).ToUpperInvariant();
        static bool SameEvidence(ImmutableArray<Guid> left, ImmutableArray<Guid> right) =>
            left.Length == right.Length && left.All(right.Contains);
        var findings = new List<FabricDiagnosticFinding>();
        foreach (var finding in diagnosis.Findings)
        {
            if (finding is null || !Enum.IsDefined(finding.Proof)) throw Failure("Ungültige Befundstufe.", "diagnosis.findings");
            CheckId(finding.Id, "diagnosis.findings.id");
            var cited = Cited(finding.Evidence, "diagnosis.findings.evidence");
            CheckText(finding.Text, cited, "diagnosis.findings.text");
            CheckText(finding.Relevance, cited, "diagnosis.findings.relevance");
            CheckText(finding.Component, cited, "diagnosis.findings.component");
            if (finding.Proof == FabricDiagnosticProof.Measured &&
                (cited.Length != 1 || cited[0].Metadata.SourceType != SourceType.Direct))
                throw Failure("Messbefund benötigt einen direkten Messbeleg.", "diagnosis.findings.proof");
            if (finding.Proof == FabricDiagnosticProof.Derived && cited.All(e => e.Metadata.SourceType != SourceType.Derived))
                throw Failure("Ableitung benötigt einen deterministisch abgeleiteten Beleg.", "diagnosis.findings.proof");
            if (finding.Proof == FabricDiagnosticProof.Correlated &&
                cited.Select(e => e.Metadata.Source).Distinct(StringComparer.Ordinal).Count() < 2)
                throw Failure("Korrelation benötigt mehrere unabhängige Quellen.", "diagnosis.findings.proof");
            if (findings.Any(previous => previous.Proof == finding.Proof &&
                SameEvidence(previous.Evidence, finding.Evidence) &&
                Normalize(previous.Component) == Normalize(finding.Component) &&
                Normalize(previous.Text) == Normalize(finding.Text)))
                throw Failure("Doppelter Befund mit denselben Belegen und derselben Komponente.", "diagnosis.findings");
            findings.Add(finding);
        }
        var hypotheses = new List<FabricDiagnosticHypothesis>();
        foreach (var hypothesis in diagnosis.Hypotheses)
        {
            if (hypothesis is null)
                throw Failure("Hypothese fehlt.", "diagnosis.hypotheses");
            if (hypothesis.Confidence != FabricHypothesisConfidence.Unconfirmed)
                throw Failure("Modellhypothese muss unbestätigt bleiben.", "diagnosis.hypotheses.confidence");
            if (hypothesis.MissingOrContradictory.IsDefaultOrEmpty || hypothesis.MissingOrContradictory.Length > 8)
                throw Failure("Hypothese benötigt eine bis acht offene Informationen.", "diagnosis.hypotheses.missingOrContradictory");
            if (hypothesis.ContradictingEvidence.IsDefault)
                throw Failure("Liste widersprechender Belege fehlt.", "diagnosis.hypotheses.contradictingEvidence");
            CheckId(hypothesis.Id, "diagnosis.hypotheses.id");
            var cited = Cited(hypothesis.Evidence, "diagnosis.hypotheses.evidence");
            if (!hypothesis.ContradictingEvidence.IsEmpty)
            {
                Cited(hypothesis.ContradictingEvidence, "diagnosis.hypotheses.contradictingEvidence");
                if (hypothesis.ContradictingEvidence.Any(hypothesis.Evidence.Contains))
                    throw Failure("Ein Beleg kann nicht zugleich stützen und widersprechen.", "diagnosis.hypotheses.contradictingEvidence");
            }
            CheckText(hypothesis.Text, cited, "diagnosis.hypotheses.text");
            CheckText(hypothesis.Reason, cited, "diagnosis.hypotheses.reason");
            foreach (var missing in hypothesis.MissingOrContradictory)
                CheckText(missing, cited, "diagnosis.hypotheses.missingOrContradictory");
            if (hypotheses.Any(previous => previous.Confidence == hypothesis.Confidence &&
                SameEvidence(previous.Evidence, hypothesis.Evidence) &&
                SameEvidence(previous.ContradictingEvidence, hypothesis.ContradictingEvidence) &&
                Normalize(previous.Text) == Normalize(hypothesis.Text)))
                throw Failure("Doppelte Hypothese mit denselben stützenden und widersprechenden Belegen.", "diagnosis.hypotheses");
            hypotheses.Add(hypothesis);
        }
        foreach (var check in diagnosis.Checks)
        {
            if (check is null || check.HypothesisIndex < 0 || check.HypothesisIndex >= diagnosis.Hypotheses.Length ||
                !diagnosis.Hypotheses[check.HypothesisIndex].Evidence.Contains(check.SourceEvidence))
                throw Failure("Prüfung benötigt eine Hypothese und einen ihrer Belege.", "diagnosis.checks");
            CheckId(check.Id, "diagnosis.checks.id");
            var cited = Cited([check.SourceEvidence], "diagnosis.checks.sourceEvidence");
            CheckText(check.Text, cited, "diagnosis.checks.text");
            CheckText(check.Target, cited, "diagnosis.checks.target");
            CheckText(check.Timeframe, cited, "diagnosis.checks.timeframe");
            CheckText(check.Reason, cited, "diagnosis.checks.reason");
            CheckText(check.ExpectedResult, cited, "diagnosis.checks.expectedResult");
            if (check.Module.Length > 0 && !AetherDiagnosticActions.All.Any(a => a.Route == check.Module))
                throw Failure("Prüfmodul ist nicht im Navigationskatalog.", "diagnosis.checks.module");
        }
        foreach (var solution in diagnosis.Solutions)
        {
            if (solution is null || solution.HypothesisIndex < 0 || solution.HypothesisIndex >= diagnosis.Hypotheses.Length ||
                solution.CheckIndex < 0 || solution.CheckIndex >= diagnosis.Checks.Length ||
                diagnosis.Checks[solution.CheckIndex].HypothesisIndex != solution.HypothesisIndex ||
                !solution.RequiresApproval || solution.Risk != FabricSolutionRisk.Unknown)
                throw Failure("Lösung benötigt eine zugeordnete Prüfung und Benutzerfreigabe.", "diagnosis.solutions");
            CheckId(solution.Id, "diagnosis.solutions.id");
            var cited = Cited(solution.Evidence, "diagnosis.solutions.evidence");
            if (!solution.Evidence.Contains(diagnosis.Checks[solution.CheckIndex].SourceEvidence))
                throw Failure("Lösung muss den Prüfungsbeleg enthalten.", "diagnosis.solutions.evidence");
            CheckText(solution.Target, cited, "diagnosis.solutions.target");
            CheckText(solution.Reason, cited, "diagnosis.solutions.reason");
            CheckText(solution.Prerequisite, cited, "diagnosis.solutions.prerequisite");
            CheckText(solution.Rollback, cited, "diagnosis.solutions.rollback");
        }
        foreach (var action in diagnosis.Actions)
        {
            if (action is null) throw Failure("Aktion fehlt.", "diagnosis.actions");
            var definition = AetherDiagnosticActions.All.SingleOrDefault(item => item.Id == action.ActionId)
                ?? throw Failure("AETHER-Aktion ist nicht im Katalog.", "diagnosis.actions.actionId");
            var cited = Cited(action.Evidence, "diagnosis.actions.evidence");
            if (!cited.Any(e => AetherDiagnosticActions.Supports(definition, e)))
                throw Failure("AETHER-Aktion passt nicht zum zitierten Beleg.", "diagnosis.actions.evidence");
        }
        return diagnosis;
    }

    private static FabricContractException Failure(string reason, string field) =>
        new("Diagnosevalidierung", reason, field);
}
