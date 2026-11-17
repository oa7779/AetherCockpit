using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using AetherOperations.Platform;

namespace AetherOperations.Intelligence;

public sealed record FabricAgent(string Id, string Purpose);
public sealed class AetherAgentRegistry
{
    public ImmutableArray<FabricAgent> Agents { get; } =
    [
        new("Orchestrator", "Belegabdeckung und begrenzte Koordination"), new("Intent", "Absicht und fehlende Angaben"),
        new("Inventory", "Explizite Ziel- und Bestandsdaten"), new("Windows", "Belegte Betriebssystemdaten"),
        new("Network", "Netzwerkbelege, keine selbstständigen Scans"), new("Security", "Schutzbelege, keine Schutzänderung"),
        new("Event", "Aggregierte Fehlerereignisse"), new("Performance", "Freiplatz und CPU-Probe"),
        new("Deployment", "Paketbelege und offene Voraussetzungen"), new("Guardian", "Keine automatische Guardian-Abnahme"),
        new("ScriptReview", "Statische Musterprüfung ohne Ausführung"), new("Policy", "Hostrolle und Zielgrenzen"),
        new("Report", "Beleggestützte deutsche Zusammenfassung"), new("Critic", "Fehlende, veraltete und widersprüchliche Belege")
    ];
    public static bool IsSystemSummary(string input) => AetherSafetyClassifier.Inspect(input).IsEmpty &&
        (input.Contains("systemstatus", StringComparison.OrdinalIgnoreCase) || input.Contains("systemzustand", StringComparison.OrdinalIgnoreCase)) &&
        (input.Contains("zusammen", StringComparison.OrdinalIgnoreCase) || input.Contains("status", StringComparison.OrdinalIgnoreCase));
    public static FabricModelRole RoleFor(string input) =>
        IsSystemSummary(input) ? FabricModelRole.FastAssistant : FabricModelRole.DiagnosticReasoner;
    public static string Intent(string input) => IsSystemSummary(input) ? "inventory-review" : AetherSafetyClassifier.Inspect(input).IsEmpty &&
        new[] { "prüf", "pruef", "check", "invent", "analyse", "untersuch" }.Any(s => input.Contains(s, StringComparison.OrdinalIgnoreCase)) &&
        new[] { "speicher", "storage", "cpu", "fehler", "error", "ereignis", "event", "laufwerk", "smart", "nvme",
            "invent", "testgruppe", "test group" }.Any(s => input.Contains(s, StringComparison.OrdinalIgnoreCase))
            ? "inventory-review" : "undecidable";
    public async Task<ImmutableArray<FabricFinding>> AnalyzeAsync(string input, ImmutableArray<FabricEvidence> evidence,
        ImmutableArray<AdminTarget> targets, PlatformPrincipal principal, CancellationToken token)
    {
        var findings = new ConcurrentBag<FabricFinding>();
        await Parallel.ForEachAsync(Agents, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
            (agent, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                foreach (var finding in Analyze(agent.Id, input, evidence, targets, principal)) findings.Add(finding);
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
        return findings.OrderBy(f => f.Agent).ThenBy(f => f.Target).ToImmutableArray();
    }
    private static ImmutableArray<FabricFinding> Analyze(string agent, string input, ImmutableArray<FabricEvidence> evidence,
        ImmutableArray<AdminTarget> targets, PlatformPrincipal principal)
    {
        FabricFinding Note(FabricProof proof, string text, ImmutableArray<string> missing = default) =>
            new(agent, null, proof, text, [], missing.IsDefault ? [] : missing);
        var available = evidence.Where(e => e.Metadata.Availability == DataAvailability.Available &&
            e.Metadata.Timestamp <= DateTimeOffset.UtcNow && DateTimeOffset.UtcNow - e.Metadata.Timestamp < TimeSpan.FromMinutes(10)).ToImmutableArray();
        if (agent is "Performance" or "Event" or "Windows" or "Inventory")
        {
            var fields = agent switch { "Performance" => new[] { "freeBytes", "cpuPercent" }, "Event" => ["systemErrors24h", "maximumEventRepetitions24h"],
                "Windows" => ["operatingSystem"], _ => ["freeBytes", "architecture"] };
            return targets.SelectMany(target => fields.Select(field =>
            {
                var matching = available.Where(e => e.TargetId == target.Id && e.Field == field).ToArray();
                var fact = matching.FirstOrDefault();
                if (fact is null || field == "cpuPercent" && DateTimeOffset.UtcNow - fact.Metadata.Timestamp > TimeSpan.FromSeconds(60))
                    return new FabricFinding(agent, target.Id, FabricProof.Undecidable, field + ": kein ausreichend aktueller Beleg.", [], [field]);
                if (matching.Select(f => f.Value).Distinct().Count() > 1)
                    return new FabricFinding(agent, target.Id, FabricProof.Undecidable, field + ": widersprüchliche Quellen.", matching.Select(f => f.Id).ToImmutableArray(), ["Konflikt auflösen"]);
                var numeric = decimal.TryParse(fact.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value);
                var detail = field switch
                {
                    "freeBytes" when numeric && value < 20m * 1024 * 1024 * 1024 => "Weniger als die Prüfgrenze von 20 GiB frei; Ursache nicht belegt.",
                    "cpuPercent" when numeric && value >= 80 => "CPU-Probe über der Prüfgrenze von 80 Prozent; kein Nachweis dauerhafter Last oder ihrer Ursache.",
                    "systemErrors24h" when numeric && value > 1 => "Mehrere Systemfehler im begrenzten Zeitfenster; Anzahl allein beweist keine Ursache.",
                    "maximumEventRepetitions24h" when numeric && value > 1 => "Wiederkehrende Provider-/Ereignis-ID innerhalb der begrenzten Probe; Ursache und Vollständigkeit nicht belegt.",
                    _ => field + ": " + fact.Value
                };
                return new FabricFinding(agent, target.Id, FabricProof.Observed,
                    (fact.Simulated ? "SIMULIERT: " : "") + detail, [fact.Id], []);
            })).ToImmutableArray();
        }
        return agent switch
        {
            "Intent" => [Note(Intent(input) == "inventory-review" ? FabricProof.Derived : FabricProof.Undecidable,
                Intent(input) == "inventory-review" ? "Lesende Gruppenuntersuchung erkannt; Zielgruppe wird vom Host fixiert." : "Absicht nicht qualifiziert; keine Ausführung.")],
            "Orchestrator" => [Note(FabricProof.Derived, $"{targets.Length} explizite Ziele; {available.Length} aktuelle Feldbelege; höchstens vier Agenten gleichzeitig.")],
            "Policy" => [Note(FabricProof.Derived, $"Hostrolle {principal.Role}; {targets.Count(t => principal.Scope.Contains(t.Id))} Ziele im authentifizierten Geltungsbereich. Keine Modellfreigabe.")],
            "ScriptReview" => AetherSafetyClassifier.ReviewScript(input).Select(s => Note(FabricProof.Derived, s)).ToImmutableArray(),
            "Report" => [Note(FabricProof.Derived, $"{available.Select(e => e.TargetId).Distinct().Count()} Ziele mit aktuellen Teilbelegen. Ursachen bleiben ohne zusätzliche Quellen offen.")],
            "Critic" => [
                Note(available.IsEmpty ? FabricProof.Undecidable : FabricProof.Derived,
                    $"{evidence.Length - available.Length} fehlende/veraltete Feldbelege; keine Ursachenfreigabe durch Modellkonsens.",
                    evidence.Where(e => !available.Contains(e)).Select(e => $"{e.TargetId}: {e.Field}").ToImmutableArray()),
                Note(available.GroupBy(e => (e.TargetId, e.Field)).Any(g => g.Select(e => e.Value).Distinct().Count() > 1)
                    ? FabricProof.Undecidable : FabricProof.Derived,
                    available.GroupBy(e => (e.TargetId, e.Field)).Any(g => g.Select(e => e.Value).Distinct().Count() > 1)
                        ? "Widersprüchliche Quellen: keine eindeutige Aussage." : "Keine widersprüchlichen aktuellen Feldwerte erkannt; dies ist kein Vollständigkeitsnachweis.")],
            "Guardian" => [Note(FabricProof.Undecidable, "Guardian wird nicht automatisch gestartet; Profilfreigaben bleiben unverändert.", ["Manuelle Profilabnahme"])],
            "Network" => [Note(FabricProof.Undecidable, "Keine qualifizierte Netzwerkaufnahme in dieser Untersuchung.", ["Netzwerkbelege"])],
            "Security" => [Note(FabricProof.Undecidable, "Keine aktuelle Schutzbewertung in diesem Schnitt.", ["Schutzbelege"])],
            "Deployment" => [Note(FabricProof.Undecidable, "Kein freigegebenes Paket oder Installationsauftrag vorhanden.", ["Paketvertrag"])],
            _ => throw new InvalidOperationException("Agent nicht registriert.")
        };
    }
}

public sealed class AetherIntelligenceHost
{
    private readonly PlatformPrincipal _principal;
    private readonly IAetherObservationSource _source;
    private readonly AetherEvidenceStore _store;
    private readonly AetherRetrievalEngine _retrieval;
    private readonly AetherModelRouter _router;
    private readonly AetherInferenceScheduler _scheduler;
    private readonly AetherIntelligenceAudit _audit;
    private readonly AetherModelRegistry _models;
    public Guid RegistryDiagnosticId => _models.DiagnosticId;
    public Guid RouterRegistryDiagnosticId => _router.RegistryDiagnosticId;
    public AetherAgentRegistry Agents { get; } = new();
    public AetherConversationStore Conversations { get; } = new();
    public AetherIntelligenceHost(PlatformPrincipal principal, IAetherObservationSource source, AetherEvidenceStore store,
        AetherRetrievalEngine retrieval, AetherModelRegistry models, AetherInferenceScheduler scheduler,
        AetherModelTelemetry telemetry, AetherIntelligenceAudit audit)
    {
        _principal = principal; _source = source; _store = store; _retrieval = retrieval; _models = models;
        _scheduler = scheduler; _router = new(models, telemetry); _audit = audit;
    }
    public async Task<FabricInvestigation> InvestigateAsync(string input, ImmutableArray<AdminTarget> selected,
        FabricMode mode, CancellationToken token, string? preferredModel = null,
        IProgress<FabricProgressPhase>? progress = null, FabricRunDiagnostics? diagnostics = null)
    {
        TargetInventory.Validate(new PlatformInventory(1, selected, []));
        if (selected.IsEmpty || input.Length > 8192) throw new ArgumentException("Explizite begrenzte Anfrage und Zielmenge erforderlich.");
        AetherSafetyClassifier.RequireNoSecrets(input);
        var issues = AetherSafetyClassifier.Inspect(input).ToBuilder();
        if (mode == FabricMode.Disabled) throw new InvalidOperationException("Intelligence ist ausgeschaltet.");
        progress?.Report(FabricProgressPhase.Preparing);
        var id = Guid.NewGuid();
        var observedFields = AetherAgentRegistry.IsSystemSummary(input) || selected.Length == 1
            ? AetherToolBroker.ReadTools["system.status"]
            : ["freeBytes", "cpuPercent", "systemErrors24h", "maximumEventRepetitions24h", "operatingSystem", "architecture"];
        _audit.Record("Untersuchung gestartet", PlatformJson.Hash(new { id, Targets = selected.Select(t => t.Id), AetherPromptRegistry.Hash }));
        var observations = new ConcurrentBag<FabricObservation>();
        progress?.Report(FabricProgressPhase.ReadingSystemData);
        await Parallel.ForEachAsync(selected, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
            async (target, ct) =>
            {
                try
                {
                    PlatformActions.Authorize(_principal, target, PlatformActions.Get("inventory.basic"));
                    if (AetherAgentRegistry.Intent(input) == "undecidable")
                    { observations.Add(new(target.Id, target.Simulated, DataAvailability.Unknown, ImmutableDictionary<string, PlatformFact>.Empty)); return; }
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    var observation = await new AetherToolBroker(_principal).ReadAsync("system.status", target, _source, timeout.Token).ConfigureAwait(false);
                    if (observation.TargetId != target.Id || observation.Simulated != target.Simulated || observation.Facts.Count > 64)
                        throw new InvalidDataException("Beobachtungsquelle verletzt Ziel-/Feldvertrag.");
                    var fields = observation.Facts.Where(pair => observedFields.Contains(pair.Key) ||
                            AetherToolBroker.IsPhysicalStorageField(pair.Key) || AetherToolBroker.IsEventGroupField(pair.Key) ||
                            AetherToolBroker.IsVolumeField(pair.Key))
                        .ToImmutableDictionary();
                    foreach (var (field, fact) in fields)
                    {
                        if (fact.Value?.Length > (AetherToolBroker.IsEventGroupField(field) ? 512 : 256) ||
                            fact.Metadata.Classification == DataClassification.Secret)
                            throw new InvalidDataException("Unzulässiges Beobachtungsfeld.");
                        if (AetherToolBroker.IsEventGroupField(field) && fact.Value is { } value)
                            AetherToolBroker.ValidateEventGroup(value);
                        if (AetherToolBroker.IsVolumeField(field) && fact.Value is { } volume)
                            AetherToolBroker.ValidateVolume(volume);
                        AetherSafetyClassifier.RequireNoSecrets((fact.Value ?? "") + " " + fact.Metadata.Source + " " + fact.Metadata.Error);
                    }
                    observations.Add(observation with { Facts = fields });
                }
                catch (UnauthorizedAccessException) { observations.Add(new(target.Id, target.Simulated, DataAvailability.AccessDenied, ImmutableDictionary<string, PlatformFact>.Empty)); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException && !ct.IsCancellationRequested)
                {
                    System.Diagnostics.Trace.TraceError("AETHER Intelligence-Quelle: {0}", ex);
                    observations.Add(new(target.Id, target.Simulated, DataAvailability.ProviderError, ImmutableDictionary<string, PlatformFact>.Empty));
                }
            }).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var ordered = observations.OrderBy(o => o.TargetId).ToImmutableArray();
        progress?.Report(FabricProgressPhase.CollectingEvidence);
        var evidence = ImmutableArray.CreateBuilder<FabricEvidence>();
        foreach (var observation in ordered)
        foreach (var field in observedFields.Concat(observation.Facts.Keys
            .Where(field => AetherToolBroker.IsPhysicalStorageField(field) || AetherToolBroker.IsEventGroupField(field) ||
                AetherToolBroker.IsVolumeField(field)).Order(StringComparer.Ordinal)))
        {
            observation.Facts.TryGetValue(field, out var fact);
            if (fact is not null && (fact.Value?.Length > (AetherToolBroker.IsEventGroupField(field) ? 512 : 256) ||
                fact.Metadata.Classification == DataClassification.Secret))
                throw new InvalidDataException("Belegfeld zu groß oder geheim; keine Modellübernahme.");
            evidence.Add(new(Guid.NewGuid(), observation.TargetId, field, fact?.Value, field switch
            { "freeBytes" or "memoryTotalBytes" or "memoryAvailableBytes" or "gpuVramTotalBytes" or "gpuVramAvailableBytes" or "volumeCapacityBytes" or "volumeUsedBytes" or "volumeFreeBytes" => "Bytes",
                "cpuPercent" or "volumeFreePercent" => "Prozent", "systemErrors24h" => "Begrenzte Anzahl", _ => "" },
                fact?.Metadata ?? new SourceMetadata("Keine qualifizierte Feldquelle", SourceType.Unconfirmed, DateTimeOffset.UtcNow,
                    DataConfidence.Unknown, observation.Availability == DataAvailability.AccessDenied ? DataAvailability.AccessDenied : DataAvailability.Unavailable,
                    classification: DataClassification.Sensitive), _principal.Id, observation.Simulated));
        }
        var facts = evidence.ToImmutable();
        var role = AetherAgentRegistry.RoleFor(input);
        var findings = await Agents.AnalyzeAsync(input, facts, selected, _principal, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var allowed = selected.Where(t => _principal.Scope.Contains(t.Id)).ToImmutableArray();
        if (allowed.IsEmpty) throw new UnauthorizedAccessException("Kein Ziel im authentifizierten Geltungsbereich.");
        progress?.Report(FabricProgressPhase.RetrievingEvidence);
        var citations = _retrieval.Search(input, _principal.Id, allowed.Select(t => t.Id).ToImmutableArray(), token: token);
        token.ThrowIfCancellationRequested();
        if (citations.IsEmpty) issues.Add("Keine passende freigegebene Wissensquelle; keine Quelle erfunden.");
        var modelCitations = ImmutableArray<FabricCitation>.Empty;
        var context = new AetherContextManager().Build(input, allowed.Select(t => t.Id).ToImmutableArray(), modelCitations, facts);
        token.ThrowIfCancellationRequested();
        progress?.Report(FabricProgressPhase.PreparingModel);
        var routed = _router.Route("de", mode, DataClassification.Sensitive, preferredModel,
            minimumContextTokens: 4096, role: role);
        issues.Add(routed.Reason);
        var request = new FabricRequest(1, id, AetherAgentRegistry.Intent(input), "de", context, AetherPromptRegistry.Hash,
            routed.Model.Id, routed.Model.Id == AetherModelRegistry.Fallback.Id ? 0 : Math.Min(8192, routed.Model.DeclaredContextTokens),
            512, 30, DataClassification.Sensitive);
        FabricProposal proposal; string modelId;
        var modelAttempted = false;
        var modelInferenceSucceeded = false;
        string? modelFallbackReason = null;
        var proposalStage = "Modellprovider";
        try
        {
            progress?.Report(routed.Model.Id == AetherModelRegistry.Fallback.Id
                ? FabricProgressPhase.RunningDeterministicRules : FabricProgressPhase.AnalyzingLocally);
            modelAttempted = routed.Model.Id != AetherModelRegistry.Fallback.Id;
            FabricResponse response;
            // Bounded retries after transient local-runtime server errors; identical request and scope.
            var transientDelays = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3) };
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    response = attempt == 0
                        ? await _scheduler.RunAsync(routed.Model, routed.Provider, request, mode, token).ConfigureAwait(false)
                        : await _scheduler.RetryWithoutRefreshAsync(routed.Model, routed.Provider, request, mode, token).ConfigureAwait(false);
                    break;
                }
                catch (HttpRequestException transient) when (attempt < transientDelays.Length &&
                    transient.StatusCode is { } transientStatus && (int)transientStatus >= 500 &&
                    routed.Provider is CompatibleInferenceProvider { IsLoopback: true })
                {
                    diagnostics?.Update(d => d.InferenceRetriedAfterTransientError = true);
                    await Task.Delay(transientDelays[attempt], token).ConfigureAwait(false);
                }
            }
            modelInferenceSucceeded = modelAttempted;
            token.ThrowIfCancellationRequested();
            proposalStage = "Vorschlags-JSON";
            progress?.Report(FabricProgressPhase.ValidatingProposal);
            proposal = new AetherToolBroker(_principal).Parse(response.Json, allowed);
            token.ThrowIfCancellationRequested();
            modelId = response.ModelId;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or
            FabricContractException or FabricResourceException ||
            ex is OperationCanceledException && !token.IsCancellationRequested)
        {
            if (routed.Model.Id == AetherModelRegistry.Fallback.Id) throw;
            var detail = proposalStage == "Vorschlags-JSON" ? ex switch
            {
                FabricContractException contract => contract.Diagnostic,
                JsonException => "Vorschlags-JSON abgelehnt: JSON-Schema ungültig.",
                InvalidDataException => "Vorschlags-Broker abgelehnt: Brokervertrag verletzt.",
                UnauthorizedAccessException => "Policy blockiert den Modellvorschlag.",
                InvalidOperationException => "Vorschlags-Broker oder Ressourcenprüfung hat die Anfrage blockiert.",
                _ => "Vorschlags-JSON abgelehnt."
            } : ex switch
            {
                FabricResourceException resource => resource.Message,
                FabricContractException contract => contract.Diagnostic,
                HttpRequestException { StatusCode: null } => "Modellprovider nicht erreichbar.",
                HttpRequestException http => $"Providerantwort ungültig: HTTP {(int)http.StatusCode!.Value}.",
                JsonException => "Providerantwort ungültig: Provider-JSON syntaktisch ungültig.",
                InvalidDataException => "Providerantwort ungültig: Providervertrag verletzt.",
                UnauthorizedAccessException => "Policy blockiert die Provideranfrage.",
                OperationCanceledException => "Modellprovider-Zeitlimit überschritten.",
                _ => "Modellanfrage durch Ressourcen- oder Modellvertrag blockiert."
            };
            issues.Add(detail + "; regelbasierter Rückfall, keine gültige Modellantwort behauptet.");
            modelFallbackReason = proposalStage == "Vorschlags-JSON" ? "ValidationFailed" : "InferenceFailed";
            diagnostics?.Update(d =>
            {
                d.FallbackReason = modelFallbackReason;
                d.FallbackDetail = detail;
                d.InferenceStarted = true;
                d.InferenceSucceeded = proposalStage == "Vorschlags-JSON";
                d.ExceptionType = ex.GetType().FullName;
                d.ExceptionDetail = detail;
            });
            System.Diagnostics.Trace.TraceWarning("AETHER Intelligence FallbackReason=ProposalFailed: {0}", detail);
            _audit.Record("Modellfallback: ProposalFailed", PlatformJson.Hash(new { proposalStage, detail }));
            var fallback = _models.Entries.Single(e => e.Model.Id == AetherModelRegistry.Fallback.Id);
            progress?.Report(FabricProgressPhase.RunningDeterministicRules);
            var response = await _scheduler.RunAsync(fallback.Model, fallback.Provider, request with
                { ModelId = fallback.Model.Id, ContextTokens = 0 }, mode, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            progress?.Report(FabricProgressPhase.ValidatingProposal);
            proposal = new AetherToolBroker(_principal).Parse(response.Json, allowed); modelId = fallback.Model.Id;
        }
        token.ThrowIfCancellationRequested();
        // A model cannot promote an unqualified host intent.
        if (AetherAgentRegistry.Intent(input) == "undecidable") proposal = proposal with { Intent = "undecidable" };
        var plan = new AetherPlanner(_principal).Create(proposal, findings, citations);
        token.ThrowIfCancellationRequested();
        var explanation = string.Join(Environment.NewLine, findings.Where(f => f.Agent is "Performance" or "Event" or "Critic")
            .Select(f => $"{f.Agent} · {f.Target?.ToString() ?? "Gruppe"} · {f.Proof switch
            { FabricProof.Observed => "Durch Quelle belegt", FabricProof.Derived => "Aus Belegen abgeleitet",
                FabricProof.Hypothesis => "Hypothese", _ => "Nicht entscheidbar" }}: {f.Explanation}"));
        if (role == FabricModelRole.FastAssistant && modelId != AetherModelRegistry.Fallback.Id)
        {
            facts = AetherContextManager.WithCanonicalWindowsIdentity(facts);
            facts = AetherContextManager.WithCanonicalStorageSummaries(facts);
        }
        FabricAssistantResult assistant;
        string? attemptedAnswerModelId = null;
        FabricProviderKind? attemptedAnswerProviderKind = null;
        try
        {
            var actual = _models.Entries.Single(e => e.Model.Id == modelId);
            attemptedAnswerModelId = actual.Model.Id;
            attemptedAnswerProviderKind = actual.Provider.Info.Kind;
            assistant = await new AetherAssistant(_scheduler).AnswerAsync(input, allowed.Select(t => t.Id).ToImmutableArray(),
                facts, modelCitations, actual.Model, actual.Provider, routed.Reason, mode, token, progress, diagnostics).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or
            FabricContractException or FabricResourceException ||
            ex is OperationCanceledException && !token.IsCancellationRequested)
        {
            if (modelId == AetherModelRegistry.Fallback.Id) throw;
            var outputTruncated = ex is FabricContractException { Stage: "OutputTruncated" };
            var detail = ex switch
            {
                FabricContractException { Stage: "OutputTruncated" } =>
                    "Die lokale Modellantwort erreichte das Ausgabelimit und war unvollständig (done_reason=length).",
                FabricContractException contract => contract.Diagnostic,
                FabricResourceException resource => resource.Message,
                HttpRequestException { StatusCode: null } => "Modellprovider nicht erreichbar.",
                HttpRequestException http => $"Providerantwort ungültig: HTTP {(int)http.StatusCode!.Value}.",
                JsonException => "Antwort-JSON syntaktisch ungültig.",
                InvalidDataException => "Antwort-Validierung: Schema oder Belegvertrag ungültig.",
                InvalidOperationException => "Antwort-Validierung durch Modellvertrag blockiert.",
                _ => "Modellantwort nicht validiert."
            };
            issues.Add(detail + "; regelbasierte Beleganzeige, keine gültige Modellantwort behauptet.");
            var fallbackReason = outputTruncated ? "OutputTruncated" :
                diagnostics?.InferenceSucceeded == true ? "ValidationFailed" : "InferenceFailed";
            diagnostics?.Update(d =>
            {
                d.FallbackReason = fallbackReason;
                d.FallbackDetail = detail;
                if (d.InferenceSucceeded == true) d.ValidatorSucceeded = false;
                else d.InferenceSucceeded = false;
                d.ExceptionType = ex.GetType().FullName;
                d.ExceptionDetail = detail;
            });
            System.Diagnostics.Trace.TraceWarning("AETHER Intelligence FallbackReason=AnswerFailed: {0}", detail);
            _audit.Record("Modellfallback: AnswerFailed", PlatformJson.Hash(new { detail }));
            var fallback = _models.Entries.Single(e => e.Model.Id == AetherModelRegistry.Fallback.Id);
            assistant = await new AetherAssistant(_scheduler).AnswerAsync(input, allowed.Select(t => t.Id).ToImmutableArray(),
                facts, citations, fallback.Model, fallback.Provider, "Antwortvalidierung fehlgeschlagen; modellloser Rückfall.", mode, token,
                progress).ConfigureAwait(false);
            modelId = fallback.Model.Id;
            modelFallbackReason = fallbackReason;
        }
        token.ThrowIfCancellationRequested();
        if (!assistant.AnswerValidated)
        {
            var answerInferenceAttempted = diagnostics?.InferenceTask is "assistant-report" or "diagnosis-report";
            var answerInferenceSucceeded = answerInferenceAttempted && diagnostics?.InferenceSucceeded == true;
            assistant = assistant with
            {
                RealModel = assistant.RealModel || answerInferenceSucceeded &&
                    attemptedAnswerProviderKind is not (FabricProviderKind.Mock or FabricProviderKind.Deterministic),
                ModelAttempted = answerInferenceAttempted,
                ModelInferenceSucceeded = answerInferenceSucceeded,
                AttemptedModelId = answerInferenceAttempted ? attemptedAnswerModelId : null,
                FallbackReason = modelFallbackReason ?? assistant.FallbackReason
            };
        }
        diagnostics?.Update(d =>
        {
            d.FinalProvider = assistant.Provider;
            d.RealModel = assistant.RealModel;
            if (!assistant.RealModel && d.FallbackReason is null)
                d.FallbackReason = "AutomaticSelectionDenied";
        });
        progress?.Report(FabricProgressPhase.PreparingResult);
        var investigation = new FabricInvestigation(id, _principal.Id, modelId, AetherPromptRegistry.Hash, ordered, facts, plan,
            explanation, issues.ToImmutable(), DateTimeOffset.UtcNow) { Assistant = assistant };
        token.ThrowIfCancellationRequested();
        _store.Save("investigation", id, investigation);
        _audit.Record("Untersuchung und Plan gespeichert", plan.Hash);
        return investigation;
    }
    public Task<FabricInvestigation> SummarizeAsync(string input, AdminTarget local, FabricMode mode,
        CancellationToken token, string? preferredModel = null, IProgress<FabricProgressPhase>? progress = null,
        FabricRunDiagnostics? diagnostics = null)
    {
        if (local.Simulated || local.Transport != TransportKind.Local || !AetherAgentRegistry.IsSystemSummary(input))
            throw new InvalidOperationException("Systemzusammenfassung benötigt den expliziten lokalen Rechner und eine lesende Statusanfrage.");
        return InvestigateAsync(input, [local], mode, token, preferredModel, progress, diagnostics);
    }
}
