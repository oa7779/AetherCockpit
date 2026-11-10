using System.Globalization;
using System.Text.Json;
using AetherOperations;

namespace SystemCockpit.Services;

internal enum IntelligenceKind { Process, File, Service, Startup, Task, Endpoint, Adapter, Software, Driver, Hardware, Sensor, Event, Snapshot, Recording, Watch, Resource, Boot }
internal enum ChangeKind { New, Removed, Changed, Unchanged, NotComparable, Incomplete }
internal sealed record IntelligenceEntity(string Id, IntelligenceKind Kind, string Name,
    IReadOnlyDictionary<string, string> Fields, SourceMetadata Metadata);
internal sealed record IntelligenceEdge(string From, string To, string Relation, SourceMetadata Metadata);
internal sealed record SourceCoverage(string Domain, bool Complete, SourceMetadata Metadata);
internal sealed record IntelligenceSnapshot(int Version, Guid Id, string Name, DateTimeOffset Timestamp,
    IReadOnlyList<IntelligenceEntity> Entities, IReadOnlyList<IntelligenceEdge> Edges,
    IReadOnlyList<SourceCoverage> Coverage, string? HostScope = null);
internal sealed record IntelligenceChange(string Id, ChangeKind Kind, string Domain,
    IntelligenceEntity? Before, IntelligenceEntity? After, string Explanation);
internal sealed record TimelineEntry(string Id, DateTimeOffset Occurred, DateTimeOffset Observed, string Category,
    string Title, string? EntityId, SourceMetadata Metadata, string Detail, WindowsEventReference? Event = null);
internal sealed record WindowsEventReference(string Channel, long RecordId, string Provider, int EventId, string? Subject = null);
internal sealed record ReliabilityPattern(string Key, DateTimeOffset First, DateTimeOffset Last, int Count, string Summary);

internal static class IntelligenceModel
{
    internal const int MaximumEntities = 30000, MaximumEdges = 60000, MaximumTimeline = 20000;
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = false, MaxDepth = 32 };
    internal static SourceMetadata Metadata(string source, DateTimeOffset time, DataAvailability state = DataAvailability.Available,
        SourceType type = SourceType.Direct, DataClassification privacy = DataClassification.Sensitive) =>
        new(source, type, time, type == SourceType.Derived ? DataConfidence.Unconfirmed : DataConfidence.ProviderReported,
            state, classification: privacy);
    internal static string Domain(IntelligenceEntity entity) =>
        entity.Fields.TryGetValue("domain", out var domain) ? domain : entity.Kind.ToString();
    internal static string DriverGroup(IntelligenceEntity driver) => driver.Fields.GetValueOrDefault("class")?.ToUpperInvariant() switch
    {
        "DISPLAY" => "Grafik", "MEDIA" or "AUDIOENDPOINT" => "Audio", "NET" => "Netzwerk",
        "BLUETOOTH" => "Bluetooth", "SCSIADAPTER" or "HDC" or "DISKDRIVE" or "VOLUME" => "Storage",
        "USB" or "USBDEVICE" => "USB", "SYSTEM" or "COMPUTER" => "Systemgeräte",
        "HIDCLASS" or "KEYBOARD" or "MOUSE" => "Eingabegeräte", "MONITOR" => "Displays", _ => "Sonstige PnP-Geräte"
    };
    internal static IReadOnlyList<IntelligenceEntity> SoftwareProcesses(IntelligenceSnapshot snapshot, string softwareId)
    {
        var files = snapshot.Entities.Where(e => e.Kind == IntelligenceKind.File).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var images = snapshot.Edges.Where(e => e.From == softwareId && files.Contains(e.To) && e.Metadata.Availability == DataAvailability.Available)
            .Select(e => e.To).ToHashSet(StringComparer.Ordinal);
        var processes = snapshot.Edges.Where(e => images.Contains(e.To) && e.Metadata.Availability == DataAvailability.Available)
            .Select(e => e.From).ToHashSet(StringComparer.Ordinal);
        return snapshot.Entities.Where(e => e.Kind == IntelligenceKind.Process && processes.Contains(e.Id)).ToArray();
    }
    internal static void Validate(IntelligenceSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Version != 1 || snapshot.Id == Guid.Empty || snapshot.Name is null || snapshot.Name.Length > 160 ||
            snapshot.Timestamp == default || snapshot.HostScope?.Length > 128 || snapshot.Entities is null || snapshot.Edges is null || snapshot.Coverage is null ||
            snapshot.Entities.Count > MaximumEntities || snapshot.Edges.Count > MaximumEdges || snapshot.Coverage.Count > 128 ||
            snapshot.Entities.Any(e => e is null || string.IsNullOrWhiteSpace(e.Id) || e.Id.Length > 32768 || !Enum.IsDefined(e.Kind) ||
                e.Name is null || e.Name.Length > 32768 || e.Fields is null || e.Fields.Count > 256 ||
                e.Fields.Any(f => f.Key.Length > 256 || f.Value is null || f.Value.Length > 65536) || !Valid(e.Metadata) ||
                e.Metadata.Classification == DataClassification.Secret) ||
            snapshot.Entities.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Entities.Count ||
            snapshot.Coverage.Any(c => c is null || string.IsNullOrWhiteSpace(c.Domain) || c.Domain.Length > 256 || !Valid(c.Metadata) ||
                c.Complete && c.Metadata.Availability != DataAvailability.Available) ||
            snapshot.Coverage.Select(c => c.Domain).Distinct().Count() != snapshot.Coverage.Count)
            throw new JsonException("Ungültiger System-Intelligence-Zustand.");
        var ids = snapshot.Entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        if (snapshot.Edges.Any(e => e is null || e.From is null || e.To is null || !ids.Contains(e.From) || !ids.Contains(e.To) ||
            string.IsNullOrWhiteSpace(e.Relation) || e.Relation.Length > 512 || !Valid(e.Metadata)))
            throw new JsonException("Ungültige Systembeziehung; beide Endpunkte müssen vorhanden sein.");
    }
    internal static bool Valid(SourceMetadata? metadata) => metadata is not null && metadata.Timestamp != default &&
        Enum.IsDefined(metadata.Availability) && Enum.IsDefined(metadata.Confidence) && Enum.IsDefined(metadata.SourceType) &&
        Enum.IsDefined(metadata.Classification) && metadata.Classification != DataClassification.Secret &&
        metadata.Source.Length <= 32768 && metadata.Error?.Length is not > 65536 && metadata.DocumentationLink?.OriginalString.Length is not > 32768;
    internal static IReadOnlyList<IntelligenceChange> Compare(IntelligenceSnapshot before, IntelligenceSnapshot after)
    {
        Validate(before); Validate(after);
        var result = new List<IntelligenceChange>();
        var left = before.Entities.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var right = after.Entities.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var coverageBefore = before.Coverage.ToDictionary(c => c.Domain);
        var coverageAfter = after.Coverage.ToDictionary(c => c.Domain);
        var sameHost = before.HostScope is { Length: > 0 } && string.Equals(before.HostScope, after.HostScope, StringComparison.Ordinal);
        foreach (var id in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
        {
            left.TryGetValue(id, out var old); right.TryGetValue(id, out var next);
            var domain = Domain(next ?? old!);
            var complete = coverageBefore.TryGetValue(domain, out var a) && a.Complete &&
                coverageAfter.TryGetValue(domain, out var b) && b.Complete;
            var current = (old is null || old.Metadata.Availability == DataAvailability.Available) &&
                (next is null || next.Metadata.Availability == DataAvailability.Available);
            var kind = !sameHost ? ChangeKind.NotComparable : !complete && (old is null || next is null) ? ChangeKind.Incomplete : !current ? ChangeKind.NotComparable :
                old is null ? ChangeKind.New : next is null ? ChangeKind.Removed :
                Equal(old, next) ? ChangeKind.Unchanged : ChangeKind.Changed;
            result.Add(new(id, kind, domain, old, next, kind switch
            {
                ChangeKind.Incomplete => "Quelle nicht vollständig; keine sichere Neuanlage oder Entfernung.",
                ChangeKind.NotComparable => !sameHost ? "Rechnerbezug unterschiedlich oder nicht belegt; keine rechnerübergreifende Identitätsgleichheit." : "Mindestens ein Zustand fehlt oder ist veraltet.",
                ChangeKind.Removed => domain == "Process" ? "Nicht mehr beobachtet; kein bestätigtes Prozessende." : "In vollständiger Folgeprobe nicht mehr vorhanden.",
                ChangeKind.New => "Neu beobachtet; erster tatsächlicher Existenzzeitpunkt ist nicht bewiesen.",
                _ => "Beobachteter Zustandsvergleich; keine Ursachenaussage."
            }));
        }
        foreach (var domain in coverageBefore.Keys.Union(coverageAfter.Keys))
            if (!coverageBefore.TryGetValue(domain, out var a) || !a.Complete || !coverageAfter.TryGetValue(domain, out var b) || !b.Complete)
                result.Add(new("coverage:" + domain, ChangeKind.Incomplete, domain, null, null, "Quellenabdeckung fehlt oder ist unvollständig."));
        return result;
    }
    internal static IntelligenceSnapshot Import(string json)
        {
            if (System.Text.Encoding.UTF8.GetByteCount(json) > IntelligenceStore.MaximumFileBytes) throw new JsonException("Snapshot zu groß.");
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Snapshotobjekt fehlt.");
            if (document.RootElement.TryGetProperty("Schema", out var schema) && schema.GetString() == "aether.systemsnapshot/1")
            {
                var old = ProcessSystemSnapshot.Parse(json);
                var entities = old.Processes.Select(p => IntelligenceSources.Entity("process:" + p.Identity, IntelligenceKind.Process, p.Name,
                    "Process", "Expliziter Import aus aether.systemsnapshot/1", p.ObservedAt, new Dictionary<string, string>
                    {
                        ["pid"] = p.Identity.Pid.ToString(CultureInfo.InvariantCulture), ["start"] = p.Identity.StartTicks.ToString(CultureInfo.InvariantCulture),
                        ["cpu"] = p.Cpu?.ToString("R", CultureInfo.InvariantCulture) ?? "Nicht verfügbar",
                        ["ramMiB"] = p.MemoryMiB?.ToString("R", CultureInfo.InvariantCulture) ?? "Nicht verfügbar"
                    }, p.Identity.IsKnown ? DataAvailability.Available : DataAvailability.Unknown)).ToList();
                foreach (var group in old.Relations.Where(r => r.Classification != DataClassification.Secret).GroupBy(r => r.Kind + "/" + r.Key))
                {
                    var row = group.First();
                    entities.Add(IntelligenceSources.Entity("legacy:" + group.Key, IntelligenceKind.Snapshot, row.Key, "Legacy/" + row.Kind,
                        row.Source, row.Timestamp, new Dictionary<string, string> { ["value"] = string.Join(" | ", group.Select(r => r.Value).Distinct()) }, row.Availability));
                }
                foreach (var fact in old.Hardware)
                    entities.Add(IntelligenceSources.Entity("legacy-hardware:" + fact.Key, IntelligenceKind.Hardware, fact.Key, "LegacyHardware",
                        "Alter Prozesssnapshot: nur Feldverfügbarkeit, kein vollständiges Hardwareinventar", fact.Timestamp,
                        new Dictionary<string, string> { ["availability"] = fact.Availability }, Enum.Parse<DataAvailability>(fact.Availability)));
                var domains = entities.Select(Domain).Distinct().Select(d => new SourceCoverage(d, false,
                    Metadata("Altes Schema enthält keinen positiven Vollständigkeitsnachweis", old.Timestamp, DataAvailability.Unknown))).ToArray();
                var migrated = new IntelligenceSnapshot(1, Guid.NewGuid(), "Importierter Prozesssnapshot", old.Timestamp, entities, [], domains);
                Validate(migrated); return migrated;
            }
            var result = JsonSerializer.Deserialize<IntelligenceSnapshot>(json, Json) ?? throw new JsonException("Snapshot fehlt.");
            Validate(result); return result;
        }
    private static bool Equal(IntelligenceEntity a, IntelligenceEntity b) => a.Kind == b.Kind && a.Name == b.Name &&
        a.Fields.Count == b.Fields.Count && a.Fields.All(f => b.Fields.TryGetValue(f.Key, out var value) && value == f.Value);

    internal static IReadOnlyList<ReliabilityPattern> Reliability(IEnumerable<TimelineEntry> entries) =>
        entries.Where(e => e.Category is "ApplicationCrash" or "ApplicationHang" or "ServiceFailure" or "WindowsError" or "DeviceError")
            .GroupBy(e => e.Category + "/" + (e.Event is { } reference ? reference.Channel + "/" + reference.Provider + "/" + reference.EventId +
                "/" + (reference.Subject ?? "Kein strukturiertes Subjekt verfügbar") : e.EntityId ?? e.Id), StringComparer.Ordinal)
            .Select(g => new ReliabilityPattern(g.Key, g.Min(e => e.Occurred), g.Max(e => e.Occurred), g.Count(),
                $"{g.First().Title}: {g.Count()} beobachtete Einträge. Gleiche Quellensignatur, keine Ursachendiagnose."))
            .OrderByDescending(p => p.Last).ToArray();

    internal static string Export(IntelligenceSnapshot snapshot, IReadOnlyList<TimelineEntry> timeline, string format, bool consent)
    {
        Validate(snapshot);
        string Share(string text, DataClassification privacy) =>
            DiagnosticDataPolicy.CanShare(privacy, consent) ? text : "[REDACTED]";
        var rows = new List<string[]>();
        foreach (var entity in snapshot.Entities.Where(e => e.Metadata.Classification != DataClassification.Secret))
            rows.Add([entity.Kind.ToString(), Share(entity.Id + " / " + entity.Name + " / " +
                JsonSerializer.Serialize(entity.Fields), DataClassification.Sensitive),
                entity.Metadata.Timestamp.ToString("O"), entity.Metadata.Availability.ToString(),
                entity.Metadata.Confidence.ToString(), Share(entity.Metadata.Source, DataClassification.Sensitive)]);
        foreach (var item in timeline.Where(e => e.Metadata.Classification != DataClassification.Secret))
            rows.Add(["Timeline", Share(item.Category + " / " + item.Title + " / " + item.Detail + " / " + item.EntityId, DataClassification.Sensitive),
                item.Occurred.ToString("O"), item.Metadata.Availability.ToString(), item.Metadata.Confidence.ToString(),
                Share(item.Metadata.Source, DataClassification.Sensitive)]);
        foreach (var edge in snapshot.Edges.Where(e => e.Metadata.Classification != DataClassification.Secret))
            rows.Add(["Relation", Share(edge.From + " ↔ " + edge.To + " / " + edge.Relation, DataClassification.Sensitive),
                edge.Metadata.Timestamp.ToString("O"), edge.Metadata.Availability.ToString(), edge.Metadata.Confidence.ToString(),
                Share(edge.Metadata.Source, DataClassification.Sensitive)]);
        foreach (var source in snapshot.Coverage.Where(c => c.Metadata.Classification != DataClassification.Secret))
            rows.Add(["Coverage", Share(source.Domain, DataClassification.Sensitive) + " / Complete=" + source.Complete,
                source.Metadata.Timestamp.ToString("O"), source.Metadata.Availability.ToString(), source.Metadata.Confidence.ToString(),
                Share(source.Metadata.Source + " / " + source.Metadata.Error, DataClassification.Sensitive)]);
        return WriteRows(snapshot.Timestamp, rows, format);
    }
    internal static string ExportRecording(DiagnosisRecording recording, string format, bool consent)
    {
        recording.Validate();
        string Share(string text) => consent ? text : "[REDACTED]";
        var rows = new List<string[]>
        {
            new[] { "Recording", $"{recording.Start:O} → {recording.End:O}; {recording.Samples.Count} Proben; Scope={recording.Scope}; " +
                $"Cancelled={recording.Cancelled}; ElapsedMs={recording.ElapsedMilliseconds}; ClockDiscontinuity={recording.ClockDiscontinuity}; " +
                Share(recording.Name), recording.Start.ToString("O"), "Observed", "ProviderReported", "AETHER / gemeinsamer Sampler" }
        };
        foreach (var point in recording.Samples)
            rows.Add(["Sample", consent ? JsonSerializer.Serialize(point) : $"CPU={point.Cpu}; RAM={point.Ram}; Identitäten=[REDACTED]",
                point.Timestamp.ToString("O"), "Observed", "ProviderReported", "Vorhandene gemeinsame Probe; keine Kausalitätsbehauptung"]);
        foreach (var marker in recording.Markers.Where(e => e.Metadata.Classification != DataClassification.Secret))
            rows.Add(["Marker", Share(marker.Title + " / " + marker.Detail), marker.Occurred.ToString("O"),
                marker.Metadata.Availability.ToString(), marker.Metadata.Confidence.ToString(), Share(marker.Metadata.Source)]);
        foreach (var issue in recording.Issues)
            rows.Add(["Issue", Share(issue), recording.End.ToString("O"), "Incomplete", "Unknown", "AETHER"]);
        foreach (var change in Compare(recording.Before, recording.After).Where(c => c.Kind != ChangeKind.Unchanged))
            rows.Add(["Compare", change.Kind + " / " + Share(change.Domain + " / " + change.Id + " / " + change.Explanation),
                recording.End.ToString("O"), change.Kind is ChangeKind.Incomplete or ChangeKind.NotComparable ? "Incomplete" : "Observed",
                "ProviderReported", "Nicht atomarer Vorher-/Nachher-Vergleich"]);
        return WriteRows(recording.End, rows, format);
    }
    private static string WriteRows(DateTimeOffset timestamp, List<string[]> rows, string format)
    {
        if (format == ".json") return JsonSerializer.Serialize(new { Schema = "aether.intelligence-report/1", Timestamp = timestamp, Rows = rows }, Json);
        if (format == ".csv") return "Kind,Value,Timestamp,Availability,Confidence,Source\r\n" +
            string.Join("\r\n", rows.Select(r => string.Join(",", r.Select(HardwareReport.Csv))));
        if (format == ".txt") return string.Join("\n", rows.Select(r => string.Join(" / ", r)));
        throw new ArgumentException("Unbekanntes Berichtsformat.");
    }

    internal static IReadOnlyList<string> Path(string from, string to, IReadOnlyList<IntelligenceEdge> edges)
    {
        var neighbors = edges.Where(e => e.Metadata.Availability == DataAvailability.Available)
            .SelectMany(e => new[] { (e.From, e.To), (From: e.To, To: e.From) }).GroupBy(p => p.From)
            .ToDictionary(g => g.Key, g => g.Select(p => p.To).Distinct().ToArray());
        var queue = new Queue<string>(); queue.Enqueue(from);
        var parents = new Dictionary<string, string?> { [from] = null };
        while (queue.TryDequeue(out var current))
        {
            if (current == to)
            {
                var result = new List<string>();
                for (string? node = to; node is not null; node = parents[node]) result.Add(node);
                result.Reverse(); return result;
            }
            if (!neighbors.TryGetValue(current, out var next)) continue;
            foreach (var node in next) if (parents.TryAdd(node, current)) queue.Enqueue(node);
        }
        return [];
    }
    internal static double? Number(IntelligenceEntity entity, string field) =>
        entity.Fields.TryGetValue(field, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value) ? value : null;
}
