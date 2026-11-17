using System.Globalization;
using System.Text.Json;
using AetherOperations;

namespace SystemCockpit.Services;

internal static class GuardianWorkflows
{
    internal static GuardianArtifact PackageManifest(PackageCenter.ObservedPackages observed, GuardianContext context)
    {
        if (observed.Truncated || observed.Kind is not ("list" or "upgrade") || observed.Rows.Count == 0)
            throw new InvalidOperationException("Vollständige vorhandene WinGet-Liste erforderlich; leere oder abgeschnittene Ausgabe nicht als Paketnachweis übernehmen.");
        var exact = observed.Rows.Where(r => r.Actionable && PackageCenter.ValidSource(r.Source)).ToArray();
        if (exact.Length == 0 || exact.GroupBy(r => r.Source + ":" + r.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1))
            throw new InvalidOperationException("Keine eindeutigen quellengebundenen Paketidentitäten.");
        var items = exact.Select(r => new SoftwareRecoveryItem("winget:" + r.Source + ":" + r.Id, r.Name, r.Version,
            "Von dieser WinGet-Tabellenquelle nicht gemeldet", "Installationskontext nicht von dieser Quelle gemeldet",
            r.Source, "WinGet list/upgrade / explizite ID und Quelle; keine Zuordnung über Namen oder Ordner",
            RollbackCapability.ExternalSourceRequired, [])
        {
            PackageId = r.Id, AvailableVersion = string.IsNullOrWhiteSpace(r.Available) ? "Nicht gemeldet" : r.Available,
            RecoverySource = r.Source + " / " + r.Id + " / exakte Wiederverfügbarkeit und Architektur vor Installation neu prüfen",
            Restriction = "Zeitbezogener Tabellenbefund, keine Garantie älterer Versionen oder Lizenz. Keine Zuordnung zu Uninstall-Einträgen.",
            Verification = "Vor manueller Installation ID, Quelle, Architektur und Kontext explizit lesen; danach ID, Quelle und installierte Version erneut prüfen"
        }).ToArray();
        return GuardianVault.Artifact(RecoveryKind.SoftwareManifest, "Quellengebundenes WinGet-Manifest", context,
            $"Vorhandene manuell gelesene WinGet-Quelle vom {observed.Observed:O}; {items.Length} eindeutige Einträge, {observed.Rows.Count - items.Length} nicht ausreichend belegte Zeilen ausgeschlossen. Keine neue Suche oder Installation.",
            new Dictionary<string, string> { ["observed"] = observed.Observed.ToString("O"), ["query"] = observed.Kind }) with { Software = items };
    }
    internal static GuardianArtifact RecoveryPoint(IntelligenceSnapshot snapshot, GuardianContext context, string name)
    {
        IntelligenceModel.Validate(snapshot);
        return GuardianVault.Artifact(RecoveryKind.RecoveryPoint, name, context,
            "Enthält: beobachtete Systementitäten, Quellenabdeckung und belegte Beziehungen. " +
            "Kann dokumentieren: Software, Treiber, Dienste und Startzustände. Enthält nicht: persönliche Dateien, Installationsmedien, Lizenzen, " +
            "vollständige Konfigurationen oder ein Systemimage. Kein Windows-Wiederherstellungspunkt. Externe Wiederherstellungsquellen bleiben erforderlich.",
            new Dictionary<string, string> { ["scope"] = "Systemzustandsdokumentation", ["recovery"] = "DocumentedOnly" }, snapshot);
    }
    internal static GuardianArtifact SoftwareManifest(IntelligenceSnapshot snapshot, GuardianContext context)
    {
        IntelligenceModel.Validate(snapshot);
        var software = snapshot.Entities.Where(e => e.Kind == IntelligenceKind.Software).Select(e => new SoftwareRecoveryItem(
            e.Id, e.Name, e.Fields.GetValueOrDefault("version") ?? "Nicht separat verfügbar",
            e.Fields.GetValueOrDefault("architecture") ?? "Nicht belegt",
            e.Id.StartsWith("software:HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) ? "Benutzerregistrierung" : "Aus Quellidentität lesen",
            e.Metadata.Source, IntelligenceModel.Domain(e) == "Softwarepaket" ? "AppX/MSIX-Manifest; Wiederinstallationsquelle nicht gesichert" :
                "Klassischer Installer unbekannt; keine WinGet-Zuordnung",
            RollbackCapability.ExternalSourceRequired,
            snapshot.Edges.Where(edge => edge.From == e.Id || edge.To == e.Id).Select(edge => edge.From == e.Id ? edge.To : edge.From).Distinct().ToArray()))
            .Select(item => PackageEvidence(item, snapshot.Entities.Single(e => e.Id == item.Identity))).ToArray();
        return GuardianVault.Artifact(RecoveryKind.SoftwareManifest, "Software-Wiederherstellungsmanifest", context,
            "Belegte Paket-/Registrierungsidentitäten und Beziehungen. Keine geratenen WinGet-IDs; keine Installationsmedien oder Lizenzsicherung.",
            new Dictionary<string, string> { ["schema"] = "aether.software-recovery/1", ["sourceCoverage"] = JsonSerializer.Serialize(
                snapshot.Coverage.Where(c => c.Domain is "Software" or "Softwarepaket"), IntelligenceModel.Json) }) with { Software = software };
    }
    private static SoftwareRecoveryItem PackageEvidence(SoftwareRecoveryItem item, IntelligenceEntity entity)
    {
        if (IntelligenceModel.Domain(entity) != "Softwarepaket") return item;
        var fullName = entity.Fields.GetValueOrDefault("value") ?? "";
        var parts = fullName.Split('_');
        if (parts.Length != 5 || !Version.TryParse(parts[1], out _) || parts[2] is not ("x86" or "x64" or "arm" or "arm64" or "neutral") ||
            parts[0].Length == 0 || parts[4].Length == 0 || fullName.Contains('|'))
            return item with { Restriction = "Paketfullname nicht eindeutig strukturiert; Version und Architektur nicht geraten." };
        return item with
        {
            PackageId = fullName, Version = parts[1], Architecture = parts[2],
            Context = "Get-AppxPackage im aktuellen Benutzerkontext; keine AllUsers-Abdeckung",
            Source = "Lokale Windows-AppX-Paketregistrierung / exaktes Manifest",
            RecoverySource = "Originales signiertes MSIX/AppX-Paket oder belegte externe Store-/Verwaltungsquelle erforderlich; nicht gesichert",
            Restriction = "Paketfamilie ist keine Store-Produktkennung. Keine externe Quelle oder verfügbare Version behauptet.",
            Verification = "PackageFullName, Familie, Version, Architektur, Benutzerregistrierung und Manifest-Executable erneut lesen"
        };
    }
    internal static GuardianArtifact BeginObservation(IntelligenceSnapshot snapshot, GuardianContext context) =>
        GuardianVault.Artifact(RecoveryKind.InstallationObservation, "Installation beobachten", context,
            "Beobachtungsfenster geöffnet. Benutzer startet Installation selbst. Noch keine Nachherprüfung.",
            new Dictionary<string, string> { ["state"] = "Observing", ["start"] = snapshot.Timestamp.ToString("O") }, snapshot, pinned: true);
    internal static GuardianArtifact CompleteObservation(GuardianArtifact before, IntelligenceSnapshot after, GuardianContext context)
    {
        before.Validate(); IntelligenceModel.Validate(after);
        if (before.Kind != RecoveryKind.InstallationObservation || before.Fields.GetValueOrDefault("state") != "Observing" ||
            before.Snapshot is null || before.Context != context)
            throw new InvalidOperationException("Kein passendes offenes Beobachtungsfenster für diesen Kontext.");
        var changes = IntelligenceModel.Compare(before.Snapshot, after);
        return GuardianVault.Artifact(RecoveryKind.InstallationObservation, "Beobachtungszeitraum abgeschlossen", context,
            "Während des Beobachtungszeitraums festgestellte Änderungen; kein Beweis, dass ein Installer sie verursacht hat.",
            new Dictionary<string, string>
            {
                ["state"] = "Completed", ["before"] = before.Id.ToString("D"),
                ["comparison"] = JsonSerializer.Serialize(changes.GroupBy(c => c.Kind).ToDictionary(g => g.Key.ToString(), g => g.Count())),
                ["start"] = before.Snapshot.Timestamp.ToString("O"), ["end"] = after.Timestamp.ToString("O")
            }, after, [before.Id]);
    }
    internal static IReadOnlyList<IntelligenceChange> ObservationChanges(GuardianArtifact before, GuardianArtifact after) =>
        before.Snapshot is null || after.Snapshot is null ? throw new InvalidOperationException("Vergleichszustände fehlen.") :
            IntelligenceModel.Compare(before.Snapshot, after.Snapshot);
    internal static GuardianArtifact DriverState(IntelligenceSnapshot snapshot, GuardianContext context)
    {
        IntelligenceModel.Validate(snapshot);
        var ids = snapshot.Entities.Where(e => e.Kind is IntelligenceKind.Driver or IntelligenceKind.Hardware or IntelligenceKind.Service or IntelligenceKind.Event)
            .Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var state = snapshot with { Entities = snapshot.Entities.Where(e => ids.Contains(e.Id)).ToArray(),
            Edges = snapshot.Edges.Where(e => ids.Contains(e.From) && ids.Contains(e.To)).ToArray() };
        return GuardianVault.Artifact(RecoveryKind.DriverState, "Driver Guard — belegter Treiberzustand", context,
            "Geräteinstanz, Provider, Version, INF, Signaturmeldung, Treiberdienst und PnP-Status aus gemeinsamen Quellen. " +
            "Geladener Dienst ist keine vollständige Hardwarefunktionsbestätigung. Alte Version allein belegt keinen Rückweg.",
            new Dictionary<string, string> { ["rollback"] = "Unknown", ["action"] = "ReadOnly" }, state);
    }
    internal static GuardianArtifact VerifyDrivers(GuardianArtifact before, IntelligenceSnapshot actual, GuardianContext context)
    {
        before.Validate(); IntelligenceModel.Validate(actual);
        if (before.Kind != RecoveryKind.DriverState || before.Context != context || before.Snapshot is null ||
            actual.HostScope != context.Host || before.Snapshot.HostScope != context.Host)
            throw new InvalidOperationException("Driver-Guard-Vorherzustand oder Host-/Benutzerkontext passt nicht.");
        var after = DriverState(actual, context);
        var comparisons = IntelligenceModel.Compare(before.Snapshot, after.Snapshot!);
        var checks = before.Snapshot.Entities.Where(e => e.Kind == IntelligenceKind.Driver).Select(driver =>
        {
            var current = after.Snapshot!.Entities.SingleOrDefault(e => e.Id == driver.Id);
            var device = current?.Fields.GetValueOrDefault("deviceId");
            var pnp = after.Snapshot.Entities.SingleOrDefault(e => e.Kind == IntelligenceKind.Hardware &&
                string.Equals(e.Fields.GetValueOrDefault("deviceId"), device, StringComparison.OrdinalIgnoreCase));
            var service = current?.Fields.GetValueOrDefault("service");
            var serviceRow = after.Snapshot.Entities.SingleOrDefault(e => e.Kind == IntelligenceKind.Service &&
                string.Equals(e.Fields.GetValueOrDefault("key"), service, StringComparison.OrdinalIgnoreCase));
            return new GuardianDriverCheck(driver.Id, device ?? "Nicht mehr belegt; keine Namenszuordnung zu neuen Instanzen",
                driver.Fields, current?.Fields, pnp?.Fields.GetValueOrDefault("status") ?? "Nicht lesbar",
                pnp?.Fields.GetValueOrDefault("problem") ?? "Nicht lesbar", serviceRow?.Fields.GetValueOrDefault("state") ?? "Nicht belegt",
                "Nur lesender PnP-/Dienststatus; keine universelle Hardwarefunktionsbestätigung",
                "Externe geprüfte Treiberquelle erforderlich; alte Versionsnummer ist kein Rückweg");
        }).ToArray();
        return after with
        {
            Name = "Driver Guard — Vorher/Nachher geprüft", Dependencies = [before.Id],
            Summary = "Exakte Geräteinstanzen, aktive Treiberregistrierung, Signaturmeldung, PnP-Fehler, Dienststatus und Ereignisse verglichen. Neue Geräteinstanzen werden ohne belegte Identitätskante nicht zusammengeführt. Keine Treiberinstallation.",
            DriverChecks = checks, Fields = new Dictionary<string, string>(after.Fields)
            {
                ["before"] = before.Id.ToString("D"), ["comparison"] = JsonSerializer.Serialize(
                    comparisons.GroupBy(c => c.Kind).ToDictionary(g => g.Key.ToString(), g => g.Count()), IntelligenceModel.Json),
                ["events"] = "Windows-Ereignisse im jeweiligen gespeicherten Zustand; ohne Geräteidentitätskante nur zeitlicher Kontext, kein Treiberursachennachweis."
            }
        };
    }
    internal static GuardianArtifact CrashCase(TimelineEntry crash, IntelligenceSnapshot snapshot, GuardianContext context,
        IReadOnlyList<TimelineEntry> timeline, IReadOnlyList<GuardianArtifact> artifacts,
        ProcessHistory? history = null, IReadOnlyList<DiagnosisRecording>? recordings = null)
    {
        if (crash.Category is not ("ApplicationCrash" or "ApplicationHang") || crash.Event is null ||
            !IntelligenceModel.Valid(crash.Metadata))
            throw new InvalidOperationException("Nur ein qualifizierter Windows-Crash-/Hang-Eintrag kann einen Fall eröffnen.");
        var matching = snapshot.Entities.SingleOrDefault(e => e.Id == crash.EntityId && e.Kind == IntelligenceKind.Event);
        var processIds = snapshot.Edges.Where(e => matching is not null && e.To == matching.Id &&
            e.Metadata.Availability == DataAvailability.Available).Select(e => e.From).ToHashSet();
        var processes = snapshot.Entities.Where(e => e.Kind == IntelligenceKind.Process && processIds.Contains(e.Id)).ToArray();
        var related = processes.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var processEdges = snapshot.Edges.Where(e => (related.Contains(e.From) || related.Contains(e.To)) &&
            e.Metadata.Availability == DataAvailability.Available).ToArray();
        var neighbours = processEdges.SelectMany(e => new[] { e.From, e.To }).ToHashSet(StringComparer.Ordinal);
        var fileIds = snapshot.Entities.Where(e => e.Kind == IntelligenceKind.File && neighbours.Contains(e.Id)).Select(e => e.Id).ToHashSet();
        var softwareEdges = snapshot.Edges.Where(e => fileIds.Contains(e.To) && e.Metadata.Availability == DataAvailability.Available &&
            snapshot.Entities.Any(s => s.Id == e.From && s.Kind == IntelligenceKind.Software)).ToArray();
        neighbours.UnionWith(softwareEdges.Select(e => e.From));
        var nearby = artifacts.Where(a => a.Context == context && a.Kind == RecoveryKind.Transaction &&
            Math.Abs((a.Created - crash.Occurred).TotalMinutes) <= 5).OrderByDescending(a => a.Created).Take(64).ToArray();
        var similar = artifacts.Where(a => a.Context == context && a.Kind == RecoveryKind.CrashCase &&
            a.Fields.GetValueOrDefault("eventPattern") == CrashPattern(crash)).OrderByDescending(a => a.Created).Take(64).ToArray();
        var recorderEvidence = (recordings ?? []).Where(r => r.Before.HostScope == context.Host && r.After.HostScope == context.Host &&
            r.Start <= crash.Occurred && r.End >= crash.Occurred).OrderByDescending(r => r.Start).Take(8).ToArray();
        var traces = artifacts.Where(a => a.Context == context && a.TraceResult is { } trace && a.TraceAnalysis is { } analysis &&
            trace.CaptureBegan <= crash.Occurred && trace.CaptureEnded >= crash.Occurred &&
            analysis.Facts.Any(f => related.Contains("process:" + f.Identity))).Take(8).ToArray();
        return GuardianVault.Artifact(RecoveryKind.CrashCase, "Crash Lab — " + crash.Category, context,
            "Windows meldet einen Fehler/Hang. Technisch gleiche Signaturen und zeitliche Nähe sind kein Ursachenbeweis. " +
            "Nicht beobachtete Prozesshistorie wird nicht rekonstruiert. Keine Malwarebewertung.",
            new Dictionary<string, string>
            {
                ["event"] = JsonSerializer.Serialize(crash, IntelligenceModel.Json),
                ["processIdentity"] = processes.Length == 1 ? processes[0].Id : "Keine eindeutig belegte Prozessinstanz",
                ["qualifiedContext"] = JsonSerializer.Serialize(snapshot.Entities.Where(e => neighbours.Contains(e.Id)).Take(128), IntelligenceModel.Json),
                ["qualifiedEdges"] = JsonSerializer.Serialize(processEdges.Concat(softwareEdges).Take(256), IntelligenceModel.Json),
                ["processHistory"] = JsonSerializer.Serialize(history?.Rows.Where(p => related.Contains("process:" + p.Key)).Take(8)
                    .Select(p => new { Identity = p.Key, Current = p.Value.Current, Samples = p.Value.Samples.TakeLast(32).ToArray() }).ToArray(), IntelligenceModel.Json),
                ["recordings"] = string.Join(" | ", recorderEvidence.Select(r => r.Id)),
                ["traces"] = string.Join(" | ", traces.Select(a => a.Id)),
                ["traceBounds"] = "Nur gleiche belegte Prozessinstanz im tatsächlichen Aufnahmeintervall; ergänzender Kontext, keine Crashursache. Nicht beobachtete Fälle bleiben ohne Trace.",
                ["similarCases"] = string.Join(" | ", similar.Select(a => a.Id)),
                ["eventPattern"] = CrashPattern(crash),
                ["nearbyTimeline"] = JsonSerializer.Serialize(timeline.Where(t => Math.Abs((t.Occurred - crash.Occurred).TotalMinutes) <= 5).Take(64), IntelligenceModel.Json),
                ["nearbyTransactions"] = string.Join(" | ", nearby.Select(a => a.Id)),
                ["contextBounds"] = "Bis 128 belegte Kontextentitäten / 256 Kanten / 8 Prozessinstanzen mit je 32 Proben / 8 passende Recorderkopien / 64 benachbarte Transaktionen / 64 frühere Musterfälle. Vollständiger beobachteter Snapshot bleibt enthalten. Fehlende unbeobachtete Quellen bleiben fehlend.",
                ["dump"] = "Ausgeschaltet. Kein Dump angelegt. Separater identitätsgebundener Opt-in-Vertrag erforderlich."
            }, snapshot, nearby.Select(a => a.Id).Concat(similar.Select(a => a.Id)).Concat(traces.Select(a => a.Id)).Distinct().ToArray()) with { Recordings = recorderEvidence };
    }
    private static string CrashPattern(TimelineEntry crash) => EvidenceFile.Hash(JsonSerializer.Serialize(
        new { crash.Event!.Channel, crash.Event.Provider, crash.Event.EventId, crash.Event.Subject }, IntelligenceModel.Json));
    internal static async Task<GuardianArtifact> ReadCrashFilesAsync(GuardianArtifact crash, CancellationToken token)
    {
        if (crash.Kind != RecoveryKind.CrashCase || crash.Snapshot is null) throw new InvalidOperationException("Crash Case mit Zustandsquelle erforderlich.");
        var process = crash.Fields.GetValueOrDefault("processIdentity");
        var files = crash.Snapshot.Edges.Where(e => e.From == process && e.Metadata.Availability == DataAvailability.Available)
            .Select(e => e.To).ToHashSet(StringComparer.Ordinal);
        var paths = crash.Snapshot.Entities.Where(e => e.Kind == IntelligenceKind.File && files.Contains(e.Id))
            .Select(e => e.Fields.GetValueOrDefault("path")).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var facts = new Dictionary<string, string>(crash.Fields);
        facts["fileEvidenceObserved"] = DateTimeOffset.UtcNow.ToString("O");
        foreach (var (path, index) in paths.Take(8).Select((p, i) => (p, i)))
        {
            token.ThrowIfCancellationRequested();
            try { facts["fileEvidence" + index] = await Task.Run(() => ProcessFileInfo.Describe(path, token), token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or NotSupportedException or System.ComponentModel.Win32Exception or ArgumentException)
            {
                System.Diagnostics.Trace.TraceError("Guardian Crash-Dateiquelle: {0}", ex);
                facts["fileEvidence" + index] = path + ": aktuell nicht lesbar / " + ex.Message;
            }
        }
        facts["fileEvidenceScope"] = $"{Math.Min(paths.Length, 8)} von {paths.Length} belegten aktuellen lokalen Dateien geprüft; ohne eindeutige Prozessinstanz keine Dateiidentität geraten. Keine Aussage über die Datei zur Fehlerzeit.";
        return crash with { Fields = facts };
    }
    internal static GuardianArtifact MaintenanceState(GuardianArtifact maintenance, string state, GuardianContext context,
        IntelligenceSnapshot? actual = null)
    {
        if (maintenance.Kind != RecoveryKind.Maintenance || maintenance.Context != context || !maintenance.Pinned ||
            state is not ("Paused" or "Cancelled"))
            throw new InvalidOperationException("Wartungsvorgang oder Kontext passt nicht.");
        if (actual is not null) IntelligenceModel.Validate(actual);
        var fields = new Dictionary<string, string>(maintenance.Fields)
        {
            ["state"] = state,
            ["verification"] = actual is null ? "Keine neue Systemprüfung; erfolgte Schritte bleiben erfolgt." :
                JsonSerializer.Serialize(IntelligenceModel.Compare(maintenance.Snapshot ?? actual, actual)
                    .GroupBy(c => c.Kind).ToDictionary(g => g.Key.ToString(), g => g.Count()))
        };
        return maintenance with { Fields = fields, Pinned = state == "Paused",
            Summary = "Workflowstatus geändert; keine gespeicherte Systemaktion wiederholt. Schritte einzeln mit dem Transaktionsplaner prüfen." };
    }
    internal static IntelligenceSnapshot Project(IntelligenceSnapshot snapshot, IReadOnlyList<GuardianArtifact> artifacts)
    {
        if (artifacts.Count == 0) return snapshot;
        artifacts = artifacts.Where(a => a.Context.Host == snapshot.HostScope).Take(256).ToArray();
        var entities = snapshot.Entities.ToList(); var edges = snapshot.Edges.ToList();
        foreach (var artifact in artifacts.Take(256))
        {
            var id = "guardian:" + artifact.Id.ToString("D");
            entities.Add(new(id, IntelligenceKind.Guardian, artifact.Name, new Dictionary<string, string>
            {
                ["domain"] = "Guardian", ["type"] = artifact.Kind.ToString(), ["state"] = artifact.Transaction?.State.ToString() ??
                    artifact.Fields.GetValueOrDefault("state") ?? "Documented", ["summary"] = artifact.Summary,
                ["created"] = artifact.Created.ToString("O", CultureInfo.InvariantCulture)
            }, artifact.Metadata));
            if (artifact.TraceResult is { } trace)
            {
                var fields = new Dictionary<string, string>(entities[^1].Fields)
                {
                    ["traceProfile"] = trace.Profile, ["captureBegan"] = trace.CaptureBegan.ToString("O"),
                    ["captureEnded"] = trace.CaptureEnded.ToString("O"), ["traceCounts"] = artifact.Fields.GetValueOrDefault("traceCounts") ?? "",
                    ["traceLimits"] = "Eigene beobachtete Intervalle; CPU-Samplehäufigkeit kein CPU-Prozentwert. Kein vollständiger Boot-/Login-/Dienstverlauf."
                };
                entities[^1] = entities[^1] with { Fields = fields };
            }
        }
        var ids = entities.Select(e => e.Id).ToHashSet();
        foreach (var artifact in artifacts.Take(256))
        {
            var id = "guardian:" + artifact.Id.ToString("D");
            foreach (var dependency in artifact.Dependencies)
            {
                var target = "guardian:" + dependency.ToString("D");
                if (ids.Contains(target)) edges.Add(new(id, target, "Guardian-Vorgang / gespeicherte Recovery-Abhängigkeit, keine Kausalbehauptung", artifact.Metadata));
            }
            if (artifact.Transaction?.Plan.Before is { } before)
            {
                var candidates = snapshot.Entities.Where(e => before.Kind == "service" && e.Kind == IntelligenceKind.Service &&
                    string.Equals(e.Fields.GetValueOrDefault("key"), before.Id, StringComparison.OrdinalIgnoreCase) ||
                    before.Kind == "task" && e.Kind == IntelligenceKind.Task &&
                    string.Equals(e.Fields.GetValueOrDefault("key"), before.Id, StringComparison.OrdinalIgnoreCase) ||
                    before.Kind == "process" && e.Kind == IntelligenceKind.Process && e.Id == "process:" + before.Id).ToArray();
                if (candidates.Length == 1)
                    edges.Add(new(id, candidates[0].Id, "Guardian-Vorgang betrifft exakten Windows-Namen; Konfiguration zeitbezogen im Journal", artifact.Metadata));
            }
            if (artifact.Kind == RecoveryKind.DriverState && artifact.Snapshot is { } driverSnapshot)
                foreach (var driver in driverSnapshot.Entities.Where(e => e.Kind == IntelligenceKind.Driver && ids.Contains(e.Id)).Take(64))
                    edges.Add(new(id, driver.Id, "Dokumentierter Geräteinstanzbezug; zeitbezogener Treiberinhalt, keine Kausalaussage", artifact.Metadata));
            GuardianTraceProjection.Project(artifact, entities, edges, ids, Math.Min(128, Math.Max(0, 4096 - entities.Count(e => e.Fields.GetValueOrDefault("domain") == "DeepTrace"))));
        }
        return snapshot with { Entities = entities.ToArray(), Edges = edges.ToArray(),
            Coverage = snapshot.Coverage.Where(c => c.Domain is not ("Guardian" or "DeepTrace")).Append(new SourceCoverage("Guardian", false,
                IntelligenceModel.Metadata("Begrenzte lokale Guardian-Historie; keine vollständige Systemhistorie", snapshot.Timestamp)))
                .Append(new SourceCoverage("DeepTrace", false, IntelligenceModel.Metadata(
                    "Begrenzte eigene ETL-Intervalle; kein vollständiger Systemverlauf. Projektion höchstens 128 Fakten je Trace / 4096 insgesamt.", snapshot.Timestamp))).ToArray() };
    }
    internal static async Task<GuardianArtifact> UpdatesAsync(GuardianContext context, CancellationToken token)
    {
        var result = await AdminShell.RunAsync(UpdateScript, TimeSpan.FromSeconds(35), token).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.OutputTruncated) throw new InvalidOperationException("Updatehistorie nicht vollständig lesbar: " + result.Error);
        using var document = JsonDocument.Parse(result.Output);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("History", out var history) ||
            history.ValueKind != JsonValueKind.Array || history.GetArrayLength() > 200 ||
            !document.RootElement.TryGetProperty("RebootRequired", out var reboot) || reboot.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new JsonException("Ungültige Windows-Updatehistorie.");
        return GuardianVault.Artifact(RecoveryKind.UpdateHistory, "Update Intelligence", context,
            "Windows Update COM / höchstens 200 Historieneinträge. Keine Suche, Installation, Änderung der Updatepolitik oder konkurrierende Orchestrierung. " +
            "Softwareangebote nur mit belegter Paketquelle im vorhandenen Paketbereich; keine geratenen WinGet-Zuordnungen.",
            new Dictionary<string, string> { ["history"] = result.Output, ["rebootRequired"] = reboot.ToString() });
    }
    internal const string UpdateScript = """
        $ErrorActionPreference='Stop'
        $session=New-Object -ComObject Microsoft.Update.Session
        $searcher=$session.CreateUpdateSearcher()
        $total=$searcher.GetTotalHistoryCount(); $count=[Math]::Min(200,$total)
        $history=@(if($count -gt 0){$searcher.QueryHistory(0,$count) | ForEach-Object {
            @{Id=[string]$_.UpdateIdentity.UpdateID;Revision=[int]$_.UpdateIdentity.RevisionNumber;
              Title=[string]$_.Title;Date=$_.Date.ToUniversalTime().ToString('O');Result=[int]$_.ResultCode;
              HResult=[int]$_.HResult;Operation=[int]$_.Operation}
        }})
        $info=New-Object -ComObject Microsoft.Update.SystemInfo
        @{History=$history;Total=$total;Complete=($total -le 200);RebootRequired=[bool]$info.RebootRequired} | ConvertTo-Json -Depth 6 -Compress
        """;
}
