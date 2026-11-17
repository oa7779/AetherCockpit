using System.Collections.Immutable;
using System.Text.Json;
using AetherOperations.Platform;

namespace AetherOperations.Intelligence;

public sealed class AetherToolBroker(PlatformPrincipal principal)
{
    public static ImmutableArray<string> AllowedActionIds { get; } =
        [PlatformActions.Get("inventory.basic").ActionId];

    public static ImmutableDictionary<string, ImmutableArray<string>> ReadTools { get; } =
        new Dictionary<string, ImmutableArray<string>>
        {
            ["system.status"] = ["freeBytes", "cpuPercent", "systemErrors24h", "maximumEventRepetitions24h", "operatingSystem", "architecture", "memoryTotalBytes", "memoryAvailableBytes",
                "operatingSystemProductName", "operatingSystemEdition", "operatingSystemVersion", "operatingSystemBuild", "operatingSystemKernelVersion",
                "cpuName", "cpuCores", "logicalProcessors", "gpuName", "gpuVramTotalBytes", "gpuVramAvailableBytes",
                "volumeDriveLetter", "volumeFileSystem", "volumeCapacityBytes", "volumeUsedBytes", "volumeFreeBytes", "volumeFreePercent",
                "eventLog", "eventProvider", "eventId", "eventLevel", "eventCategory", "eventCount", "eventFirstSeen", "eventLastSeen", "physicalStorageStatus"],
            ["device.info"] = ["operatingSystem", "architecture"],
            ["storage.status"] = ["freeBytes"],
            ["process.overview"] = ["processCount"],
            ["events.summary"] = ["systemErrors24h", "maximumEventRepetitions24h"],
            ["updates.status"] = ["updateStatus"],
            ["defender.status"] = ["defenderStatus"],
            ["network.status"] = ["activeNetworkAdapters"]
        }.ToImmutableDictionary();

    public async Task<FabricObservation> ReadAsync(string tool, AdminTarget target, IAetherObservationSource source, CancellationToken token)
    {
        if (!ReadTools.TryGetValue(tool, out var fields)) throw new InvalidDataException("Unbekannte Leseaktion; keine freie Toolausführung.");
        PlatformActions.Authorize(principal, target, PlatformActions.Get("inventory.basic"));
        var result = source is IAetherReadToolSource tools
            ? await tools.ReadToolAsync(tool, target, token).ConfigureAwait(false)
            : await source.ReadAsync(target, token).ConfigureAwait(false);
        if (result.TargetId != target.Id || result.Simulated != target.Simulated || result.Facts.Count > 64)
            throw new InvalidDataException("Lesequelle verletzt den Ziel-/Feldvertrag.");
        var selected = result.Facts.Where(p => fields.Contains(p.Key) ||
            tool == "system.status" && (IsPhysicalStorageField(p.Key) || IsEventGroupField(p.Key) || IsVolumeField(p.Key))).ToImmutableDictionary();
        foreach (var (field, fact) in selected)
        {
            if (fact.Value?.Length > (IsEventGroupField(field) ? 512 : 256) || fact.Metadata.Classification == DataClassification.Secret)
                throw new InvalidDataException("Lesequelle enthält unzulässige Felder.");
            if (IsEventGroupField(field) && fact.Value is { } value) ValidateEventGroup(value);
            if (IsVolumeField(field) && fact.Value is { } volume) ValidateVolume(volume);
            AetherSafetyClassifier.RequireNoSecrets((fact.Value ?? "") + " " + fact.Metadata.Source + " " + fact.Metadata.Error);
        }

        return result with { Facts = selected, Availability = selected.IsEmpty && result.Availability == DataAvailability.Available
            ? DataAvailability.Unavailable : result.Availability };
    }
    public static bool IsPhysicalStorageField(string field) =>
        field.StartsWith("physicalDrive.", StringComparison.Ordinal) &&
        int.TryParse(field.AsSpan("physicalDrive.".Length), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var index) && index is >= 0 and < 16 &&
        field == "physicalDrive." + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsEventGroupField(string field) => field is "eventGroup.0" or "eventGroup.1" or "eventGroup.2";

    public static bool IsVolumeField(string field) =>
        field.StartsWith("volume.", StringComparison.Ordinal) &&
        int.TryParse(field.AsSpan("volume.".Length), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var index) && index is >= 0 and < 8 &&
        field == "volume." + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static FabricEventGroup ValidateEventGroup(string value)
    {
        var group = FabricJson.Parse<FabricEventGroup>(value);
        if (group.Log != "System" || string.IsNullOrWhiteSpace(group.Provider) || group.Provider.Length > 160 ||
            group.EventId < 0 || group.Level is not (1 or 2) || group.Count is < 1 or > 128 ||
            group.FirstSeen == default || group.LastSeen == default || group.FirstSeen > group.LastSeen)
            throw new InvalidDataException("Ungültiger Kontext einer Ereignisgruppe.");
        return group;
    }

    public static FabricVolume ValidateVolume(string value)
    {
        var volume = FabricJson.Parse<FabricVolume>(value);
        if (volume.Drive is not { Length: 3 } || !char.IsAsciiLetter(volume.Drive[0]) ||
            volume.Drive[1] != ':' || volume.Drive[2] != '\\' ||
            string.IsNullOrWhiteSpace(volume.FileSystem) || volume.FileSystem.Length > 32 ||
            volume.CapacityBytes <= 0 || volume.FreeBytes < 0 || volume.UsedBytes < 0 ||
            volume.FreeBytes > volume.CapacityBytes || volume.UsedBytes != volume.CapacityBytes - volume.FreeBytes)
            throw new InvalidDataException("Ungültiger lokaler Volumebeleg.");
        return volume;
    }

    public FabricProposal Parse(string json, ImmutableArray<AdminTarget> selected)
    {
        var proposal = FabricJson.ParseModel<FabricProposal>(json, "Vorschlags-JSON");
        if (proposal.Schema != 1)
            throw new FabricContractException("Vorschlags-Broker", "Schemawert ist nicht freigegeben.",
                "schema", "1", "abweichender Wert");
        if (proposal.Intent is not ("inventory-review" or "undecidable"))
            throw new FabricContractException("Vorschlags-Broker", "Absicht ist nicht freigegeben.",
                "intent", "inventory-review oder undecidable", "nicht freigegeben");
        if (!AllowedActionIds.Contains(proposal.ActionId, StringComparer.Ordinal))
            throw new FabricContractException("Vorschlags-Broker", "ActionId ist nicht in der erlaubten Aktionsliste.",
                "actionId", string.Join(", ", AllowedActionIds), "nicht freigegeben");
        if (proposal.Targets.IsDefaultOrEmpty || proposal.Targets.Length > TargetInventory.MaximumTargets ||
            proposal.Targets.Length != selected.Length || proposal.Targets.Distinct().Count() != proposal.Targets.Length)
            throw new FabricContractException("Vorschlags-Broker", "Zielmenge ist leer, doppelt, zu groß oder unvollständig.",
                "targets", "nicht leere Liste der ausgewählten GUIDs", "ungültige Zielmenge");
        if (proposal.Targets.Any(id => !selected.Any(target => target.Id == id)))
            throw new FabricContractException("Vorschlags-Broker", "TargetId ist nicht ausgewählt.",
                "targets", "GUID aus der ausgewählten Zielmenge", "unbekannte TargetId");
        if (proposal.Parameters is null || proposal.Parameters.Count != 0)
            throw new FabricContractException("Vorschlags-Broker", "Parameter müssen ein leeres Objekt sein.",
                "parameters", "leeres Objekt", "nicht leer");
        foreach (var target in selected.Where(t => proposal.Targets.Contains(t.Id)))
        {
            try { PlatformActions.Authorize(principal, target, PlatformActions.Get(proposal.ActionId)); }
            catch (UnauthorizedAccessException)
            {
                throw new FabricContractException("Policy", "Die Aktion ist für mindestens ein Ziel blockiert.",
                    "targets", "ausdrücklich autorisierte Ziele", "blockiert");
            }
        }
        return proposal;
    }
}

public sealed class AetherPlanner(PlatformPrincipal principal)
{
    public FabricPlan Create(FabricProposal proposal, ImmutableArray<FabricFinding> findings, ImmutableArray<FabricCitation> citations)
    {
        var action = PlatformActions.Get(proposal.ActionId); var runbook = PlatformActions.Runbook(action.ActionId);
        return new(1, Guid.NewGuid(), proposal.Intent, proposal.Targets, action.ActionId, action.Hash,
            runbook.Id, runbook.Hash, AetherPromptRegistry.Hash, findings, citations,
            ["Zeitliche Nachbarschaft beweist keine Ursache; eine Testmarke repariert keine Auslastung."],
            findings.SelectMany(f => f.Missing).Distinct().ToImmutableArray(), action.Safety.Risk,
            new(Workers: 4, WaveSize: 10, CanaryCount: 1), action.Plan.Verify, action.Plan.Rollback);
    }
    public FabricPlan Marker(FabricInvestigation investigation, AdminTarget target)
    {
        if (investigation.Plan.Intent != "inventory-review" || !target.Simulated ||
            !investigation.Plan.Targets.Contains(target.Id) ||
            !investigation.Observations.Any(o => o.TargetId == target.Id && o.Availability == DataAvailability.Available))
            throw new UnauthorizedAccessException("Testmarke nur auf explizitem, erreichbarem Simulationsziel der Untersuchung.");
        var action = PlatformActions.Get("test.marker");
        PlatformActions.Authorize(principal, target, action);
        var runbook = PlatformActions.Runbook(action.ActionId);
        return investigation.Plan with { Id = Guid.NewGuid(), Targets = [target.Id], ActionId = action.ActionId,
            ActionHash = action.Hash, RunbookId = runbook.Id, RunbookHash = runbook.Hash, Risk = action.Safety.Risk,
            Verification = action.Plan.Verify, Rollback = action.Plan.Rollback,
            Assumptions = ["SIMULIERTER Ablaufnachweis; keine Reparatur und keine Änderung am realen Rechner."] };
    }
}

public sealed class AetherPolicyEngine(PlatformPrincipal principal)
{
    public ImmutableArray<AdminTarget> Validate(FabricPlan plan, ImmutableArray<AdminTarget> inventory)
    {
        var action = PlatformActions.Get(plan.ActionId); var runbook = PlatformActions.Runbook(plan.ActionId);
        if (plan.Schema != 1 || plan.Id == Guid.Empty || plan.Intent != "inventory-review" ||
            plan.Targets.IsDefaultOrEmpty || plan.Targets.Distinct().Count() != plan.Targets.Length ||
            plan.ActionHash != action.Hash || plan.Risk != action.Safety.Risk ||
            plan.RunbookId != runbook.Id || plan.RunbookHash != runbook.Hash || plan.PromptHash != AetherPromptRegistry.Hash)
            throw new UnauthorizedAccessException("Planversion, Absicht, Aktion, Risiko oder Hashbindung ungültig.");
        PlatformActions.Validate(plan.Policy, plan.Targets.Length, plan.Risk > 0);
        var targets = plan.Targets.Select(id => inventory.SingleOrDefault(t => t.Id == id)
            ?? throw new UnauthorizedAccessException("Planziel fehlt im expliziten Hostinventar.")).ToImmutableArray();
        foreach (var target in targets)
        {
            PlatformActions.Authorize(principal, target, action);
            if (plan.ActionId == "test.marker" && !target.Simulated)
                throw new UnauthorizedAccessException("Intelligence-Schnitt gibt nur simulierte Schreibaktionen frei.");
        }
        return targets;
    }
}

public sealed class AetherApprovalEngine(PlatformEngine engine, AetherPolicyEngine policy, AetherIntelligenceAudit audit, AetherEvidenceStore store)
{
    private readonly Dictionary<Guid, (string Plan, string Preview)> _bindings = [];
    public async Task<PlatformJob> PreviewAsync(FabricPlan plan, ImmutableArray<AdminTarget> inventory, CancellationToken token)
    {
        var targets = policy.Validate(plan, inventory);
        audit.Record("Vorschau angefordert", plan.Hash);
        var preview = await engine.PreviewAsync(plan.ActionId, targets, plan.Policy, token).ConfigureAwait(false);
        store.Save("plan", preview.Preview.Id, new FabricPlanBinding(plan, preview.Preview.Id, preview.PreviewHash));
        lock (_bindings) _bindings.Add(preview.Preview.Id, (plan.Hash, preview.PreviewHash));
        audit.Record("Vorschau gebunden", PlatformJson.TextHash(plan.Hash + preview.PreviewHash));
        return preview;
    }
    private string Require(FabricPlan plan, Guid job)
    {
        lock (_bindings)
            return _bindings.TryGetValue(job, out var binding) && binding.Plan == plan.Hash ? binding.Preview
                : throw new UnauthorizedAccessException("Plan wurde geändert oder Bindung nach Neustart fehlt; neue Vorschau erforderlich.");
    }
    public PlatformJob Approve(FabricPlan plan, Guid job, bool personallyConfirmed)
    {
        if (!personallyConfirmed) throw new UnauthorizedAccessException("Persönliche ausdrückliche Freigabe fehlt.");
        var hash = Require(plan, job);
        audit.Record("Persönliche Freigabe angefordert", PlatformJson.TextHash(plan.Hash + hash));
        return engine.Approve(job, hash, TimeSpan.FromMinutes(10));
    }
    public async Task<PlatformJob> ExecuteAsync(FabricPlan plan, Guid job, CancellationToken token)
    {
        Require(plan, job);
        audit.Record("Deterministische Ausführung angefordert", plan.Hash);
        var result = await engine.RunAsync(job, token).ConfigureAwait(false);
        audit.Record("Deterministische Ausführung: " + result.State, plan.Hash);
        return result;
    }
    public async Task<PlatformJob> RollbackAsync(FabricPlan plan, Guid job, bool personallyConfirmed, CancellationToken token)
    {
        if (!personallyConfirmed) throw new UnauthorizedAccessException("Rückweg muss persönlich bestätigt werden.");
        var hash = Require(plan, job);
        audit.Record("Eigener Rückweg angefordert", plan.Hash);
        var result = await engine.RollbackAsync(job, hash, token).ConfigureAwait(false);
        audit.Record("Rückweg: " + result.State, plan.Hash);
        return result;
    }
    public PlatformJob Revoke(Guid job) => engine.Revoke(job);
    public void Pause() => engine.RequestPause();
    public async Task<PlatformJob> ReconcileAsync(FabricPlan plan, Guid job, ImmutableArray<AdminTarget> inventory, CancellationToken token)
    {
        policy.Validate(plan, inventory);
        var binding = store.Load<FabricPlanBinding>("plan").SingleOrDefault(b => b.Job == job)
            ?? throw new InvalidDataException("Dauerhafte Planbindung fehlt.");
        if (binding.Plan.Hash != plan.Hash) throw new UnauthorizedAccessException("Gespeicherte Planbindung widerspricht dem gewählten Plan.");
        audit.Record("Manuelle Nachprüfung angefordert", plan.Hash);
        var current = engine.Inspect(job);
        if (current.PreviewHash != binding.PreviewHash) throw new InvalidDataException("Vorschau stimmt nicht mit dauerhafter Planbindung überein.");
        var result = current.State is PlatformJobState.Running or PlatformJobState.NeedsVerification
            ? await engine.ReconcileAsync(job, token).ConfigureAwait(false)
            : current.State is PlatformJobState.Approved or PlatformJobState.AwaitingApproval or PlatformJobState.Paused
                ? engine.Revoke(job) : current;
        lock (_bindings) _bindings[job] = (plan.Hash, binding.PreviewHash);
        audit.Record("Nachprüfung; keine automatische Fortsetzung", plan.Hash);
        return result;
    }
}

public sealed class AetherVerificationEngine
{
    public ImmutableArray<string> Verify(PlatformJob job)
    {
        PlatformStore.Validate(job);
        var result = job.Preview.Targets.Select(t =>
        {
            var execution = job.Results.SingleOrDefault(r => r.TargetId == t.Target.Id);
            return $"Ziel {t.Target.Id}: " + (execution is null ? "Noch nicht ausgeführt" :
                execution.State is TargetResultState.Succeeded or TargetResultState.RolledBack
                    ? $"Vom deterministischen Adapter verifiziert: {execution.State}" : $"Nicht erfolgreich bestätigt: {execution.State}");
        }).ToImmutableArray();
        return result;
    }
}

public sealed class AetherIntelligenceAudit(AetherEvidenceStore store, string actor)
{
    private static readonly Guid Id = new("db43a33f-6498-4f20-8ea0-e30a8ad636a1");
    private readonly object _gate = new();
    public ImmutableArray<FabricAudit> Entries
    {
        get { lock (_gate) { var entries = store.Load<ImmutableArray<FabricAudit>>("audit").SingleOrDefault(); Validate(entries.IsDefault ? [] : entries); return entries.IsDefault ? [] : entries; } }
    }
    public void Record(string kind, string subjectHash)
    {
        lock (_gate)
        {
            var entries = Entries;
            if (entries.Length >= 2048 || kind.Length > 128 || subjectHash.Length != 64 || !subjectHash.All(char.IsAsciiHexDigit))
                throw new InvalidDataException("Auditbudget oder Hashvertrag verletzt.");
            var entry = new FabricAudit(entries.Length, DateTimeOffset.UtcNow, actor, kind, subjectHash, entries.LastOrDefault()?.Hash ?? "", "");
            store.Save("audit", Id, entries.Add(entry with { Hash = Hash(entry) }));
        }
    }
    private static string Hash(FabricAudit entry) => PlatformJson.Hash(entry with { Hash = "" });
    private void Validate(ImmutableArray<FabricAudit> entries)
    {
        for (var i = 0; i < entries.Length; i++)
            if (entries[i].Sequence != i || entries[i].Actor != actor || entries[i].Hash != Hash(entries[i]) ||
                entries[i].PreviousHash != (i == 0 ? "" : entries[i - 1].Hash))
                throw new InvalidDataException("Intelligence-Auditkette ungültig.");
    }
}
