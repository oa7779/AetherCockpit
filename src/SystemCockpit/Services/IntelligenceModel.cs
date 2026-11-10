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
    IReadOnlyList<SourceCoverage> Coverage, ProcessSystemSnapshot? Processes = null);
internal sealed record IntelligenceChange(string Id, ChangeKind Kind, string Domain,
    IntelligenceEntity? Before, IntelligenceEntity? After, string Explanation);
internal sealed record TimelineEntry(string Id, DateTimeOffset Occurred, DateTimeOffset Observed, string Category,
    string Title, string? EntityId, SourceMetadata Metadata, string Detail);
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
    internal static void Validate(IntelligenceSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Version != 1 || snapshot.Id == Guid.Empty || snapshot.Name is null || snapshot.Name.Length > 160 ||
            snapshot.Timestamp == default || snapshot.Entities is null || snapshot.Edges is null || snapshot.Coverage is null ||
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
        Enum.IsDefined(metadata.Classification) && metadata.Source.Length <= 32768;
    internal static IReadOnlyList<IntelligenceChange> Compare(IntelligenceSnapshot before, IntelligenceSnapshot after)
    {
        Validate(before); Validate(after);
        var result = new List<IntelligenceChange>();
        var left = before.Entities.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var right = after.Entities.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var coverageBefore = before.Coverage.ToDictionary(c => c.Domain);
        var coverageAfter = after.Coverage.ToDictionary(c => c.Domain);
        foreach (var id in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
        {
            left.TryGetValue(id, out var old); right.TryGetValue(id, out var next);
            var domain = Domain(next ?? old!);
            var complete = coverageBefore.TryGetValue(domain, out var a) && a.Complete &&
                coverageAfter.TryGetValue(domain, out var b) && b.Complete;
            var current = (old is null || old.Metadata.Availability == DataAvailability.Available) &&
                (next is null || next.Metadata.Availability == DataAvailability.Available);
            var kind = !complete && (old is null || next is null) ? ChangeKind.Incomplete : !current ? ChangeKind.NotComparable :
                old is null ? ChangeKind.New : next is null ? ChangeKind.Removed :
                Equal(old, next) ? ChangeKind.Unchanged : ChangeKind.Changed;
            result.Add(new(id, kind, domain, old, next, kind switch
            {
                ChangeKind.Incomplete => "Quelle nicht vollständig; keine sichere Neuanlage oder Entfernung.",
                ChangeKind.NotComparable => "Mindestens ein Zustand fehlt oder ist veraltet.",
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
    private static bool Equal(IntelligenceEntity a, IntelligenceEntity b) => a.Name == b.Name &&
        a.Fields.Count == b.Fields.Count && a.Fields.All(f => b.Fields.TryGetValue(f.Key, out var value) && value == f.Value);

    internal static IReadOnlyList<ReliabilityPattern> Reliability(IEnumerable<TimelineEntry> entries) =>
        entries.Where(e => e.Category is "ApplicationCrash" or "ApplicationHang" or "ServiceFailure" or "WindowsError" or "DeviceError")
            .GroupBy(e => e.Category + "/" + (e.EntityId ?? e.Id), StringComparer.Ordinal)
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
        if (format == ".json") return JsonSerializer.Serialize(new { Schema = "aether.intelligence-report/1", snapshot.Timestamp, Rows = rows }, Json);
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
