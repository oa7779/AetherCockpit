using System.Globalization;
using System.IO;
using System.Text.Json;
using AetherOperations;

namespace SystemCockpit.Services;

internal sealed record SnapshotProcess(ProcessIdentity Identity, string Name, double? Cpu, double? MemoryMiB, DateTimeOffset ObservedAt);
internal sealed record SnapshotHardware(string Key, string Availability, DateTimeOffset Timestamp);
internal sealed record ProcessSystemSnapshot(string Schema, DateTimeOffset Timestamp, IReadOnlyList<SnapshotProcess> Processes,
    IReadOnlyList<ProcessRelation> Relations, IReadOnlyList<SnapshotHardware> Hardware)
{
    internal string LocalJson() => JsonSerializer.Serialize(this with
    {
        Relations = Relations.Where(r => r.Classification != DataClassification.Secret).ToArray()
    }, new JsonSerializerOptions { WriteIndented = true });
    internal static ProcessSystemSnapshot Parse(string json)
    {
        if (json.Length > 32 * 1024 * 1024) throw new JsonException("Snapshot zu groß.");
        var snapshot = JsonSerializer.Deserialize<ProcessSystemSnapshot>(json) ?? throw new JsonException("Snapshot fehlt.");
        if (snapshot.Schema != "aether.systemsnapshot/1" || snapshot.Timestamp == default ||
            snapshot.Processes is null || snapshot.Processes.Count > ProcessHistory.Capacity ||
            snapshot.Relations is null || snapshot.Relations.Count > 20000 || snapshot.Hardware is null || snapshot.Hardware.Count > 65536 ||
            snapshot.Processes.Any(p => p is null || p.Name is null || p.Name.Length > 32768 || p.Identity.Pid < 0 || p.ObservedAt == default ||
                p.Identity.StartTicks < 0 || p.Identity.StartTicks > DateTime.MaxValue.Ticks ||
                p.Cpu is { } cpu && (!double.IsFinite(cpu) || cpu is < 0 or > 100) ||
                p.MemoryMiB is { } memory && (!double.IsFinite(memory) || memory < 0)) ||
            snapshot.Processes.Select(p => p.Identity).Distinct().Count() != snapshot.Processes.Count ||
            snapshot.Relations.Any(r => r is null || r.Kind is null || r.Key is null || r.Value is null || r.Source is null ||
                r.Kind.Length > 256 || r.Key.Length > 32768 || r.Value.Length > 65536 || r.Source.Length > 32768 ||
                r.OwnerPid < 0 || r.Timestamp == default || r.Executable?.Length > 32768 || r.LocalAddress?.Length > 256 ||
                r.LocalPort is < 0 or > 65535 || r.RemotePort is < 0 or > 65535 || r.RemoteAddress?.Length > 256 || r.EndpointState?.Length > 128 ||
                r.GpuEngine?.Length > 512 || r.GpuPercent is { } percent && (!double.IsFinite(percent) || percent < 0) ||
                r.GpuSampleStart is { } sampleStart && sampleStart > r.Timestamp ||
                !Enum.IsDefined(r.Availability) || !Enum.IsDefined(r.Classification)) ||
            snapshot.Hardware.Any(h => h is null || h.Key is null || h.Key.Length > 32768 || h.Availability is null || h.Timestamp == default ||
                !Enum.TryParse<DataAvailability>(h.Availability, out var state) || !Enum.IsDefined(state)) ||
            snapshot.Hardware.Select(h => h.Key).Distinct().Count() != snapshot.Hardware.Count)
            throw new JsonException("Ungültiges Snapshotmodell; keine Ausführung oder Live-Übernahme.");
        return snapshot;
    }
    internal static async Task<ProcessSystemSnapshot> CaptureAsync(IReadOnlyList<ProcessMetric> rows, HardwareInventory? hardware,
        bool synthetic, CancellationToken token)
    {
        var relations = synthetic ? Array.Empty<ProcessRelation>() : await ProcessCorrelation.ReadSystemAsync(token).ConfigureAwait(false);
        return new("aether.systemsnapshot/1", DateTimeOffset.UtcNow,
            rows.Select(p => new SnapshotProcess(p.Identity, p.Name, p.CpuPercent, p.MemoryMb, p.ObservedAt)).ToArray(), relations,
            hardware?.Components.SelectMany(c => c.Facts.Select(f => new SnapshotHardware(c.Id + "/" + f.Key,
                f.Observation.Metadata.Availability.ToString(), f.Observation.Metadata.Timestamp))).ToArray() ?? []);
    }

    internal static string Compare(ProcessSystemSnapshot before, ProcessSystemSnapshot after)
    {
        if (before.Schema != "aether.systemsnapshot/1" || after.Schema != before.Schema) throw new InvalidOperationException("Snapshotformat nicht unterstützt.");
        var lines = new List<string> { $"{before.Timestamp:O} → {after.Timestamp:O}", "Nicht atomare Aufnahmen. Veränderung ist keine Sicherheitsdiagnose." };
        var old = before.Processes.Where(p => p.Identity.IsKnown).ToDictionary(p => p.Identity);
        var next = after.Processes.Where(p => p.Identity.IsKnown).ToDictionary(p => p.Identity);
        foreach (var id in next.Keys.Except(old.Keys)) lines.Add($"Neu beobachtet: {next[id].Name} / {id}");
        foreach (var id in old.Keys.Except(next.Keys)) lines.Add($"Nicht mehr beobachtet (kein verifiziertes Ende): {old[id].Name} / {id}");
        foreach (var id in next.Keys.Intersect(old.Keys))
            if (next[id].MemoryMiB is { } memory && old[id].MemoryMiB is { } previous && Math.Abs(memory - previous) >= 100)
                lines.Add($"RAM-Änderung ≥ 100 MiB: {next[id].Name} / {memory - previous:+0.0;-0.0;0} MiB");
        foreach (var kind in new[] { "Dienst", "Autostart", "Geplante Aufgabe", "TCP", "UDP" })
        {
            if (before.Relations.Concat(after.Relations).Any(r => r.Kind == kind && r.Availability != DataAvailability.Available))
            { lines.Add($"{kind}: Quelle unvollständig; keine sichere Neuanlage-/Entfernungsaussage."); continue; }
            Dictionary<string, string> Values(IReadOnlyList<ProcessRelation> relations) =>
                relations.Where(r => r.Kind == kind).GroupBy(r => r.Key, StringComparer.Ordinal).ToDictionary(g => g.Key,
                    g => string.Join(" | ", g.Select(r => r.Value).Distinct().Order(StringComparer.Ordinal)), StringComparer.Ordinal);
            var left = Values(before.Relations);
            var right = Values(after.Relations);
            foreach (var key in right.Keys.Except(left.Keys)) lines.Add($"{kind} neu beobachtet: {key}");
            foreach (var key in left.Keys.Except(right.Keys)) lines.Add($"{kind} nicht mehr beobachtet: {key}");
            foreach (var key in right.Keys.Intersect(left.Keys).Where(key => right[key] != left[key]))
                lines.Add($"{kind} geändert: {key} / {left[key]} → {right[key]}");
        }
        var oldHardware = before.Hardware.ToDictionary(h => h.Key);
        foreach (var hardware in after.Hardware)
            if (oldHardware.TryGetValue(hardware.Key, out var previous) && previous.Availability != hardware.Availability)
                lines.Add($"Hardwarestatus geändert: {hardware.Key} / {previous.Availability} → {hardware.Availability}");
        if (before.Processes.Any(p => !p.Identity.IsKnown) || after.Processes.Any(p => !p.Identity.IsKnown))
            lines.Add("Prozesse ohne Startidentität werden nicht als sichere neue/beendete Identitäten verglichen.");
        return string.Join("\n", lines);
    }

    internal string Export(string extension, bool consent)
    {
        var cells = new List<ExportCell>();
        var aliases = Processes.Select((p, index) => (p.Identity, Alias: "process-" + (index + 1))).ToDictionary(p => p.Identity, p => p.Alias);
        string Share(string value, DataClassification classification) => DiagnosticDataPolicy.CanShare(classification, consent) ? value : "[REDACTED]";
        foreach (var process in Processes)
        {
            var alias = aliases[process.Identity];
            cells.Add(new(alias, "Identität / Name", Share($"{process.Identity} / {process.Name}", DataClassification.Sensitive), "Sensitive", process.ObservedAt, "Windows-Prozessprobe"));
            cells.Add(new(alias, "CPU %", process.Cpu?.ToString("R", CultureInfo.InvariantCulture) ?? "Unavailable", "Normal", process.ObservedAt, "Abgeleitete CPU-Zeit / logische Kapazität"));
            cells.Add(new(alias, "RAM MiB", process.MemoryMiB?.ToString("R", CultureInfo.InvariantCulture) ?? "Unavailable", "Normal", process.ObservedAt, "WorkingSet64"));
        }
        foreach (var relation in Relations.Where(r => r.Classification != DataClassification.Secret))
            cells.Add(new("relation", "Systembeziehung", Share(relation.Kind + " / " + relation.Key + " / " + relation.Value + " / " + relation.Source, DataClassification.Sensitive),
                "Sensitive", relation.Timestamp, relation.Availability + " / Windows-Systemkorrelation"));
        foreach (var hardware in Hardware)
            cells.Add(new("hardware", "Verfügbarkeit", Share(hardware.Key, DataClassification.Sensitive) + " / " + hardware.Availability,
                "Sensitive", hardware.Timestamp, "Bereits vorhandener M3-Inventarzustand"));
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { Schema, Timestamp, Atomic = false, Rows = cells }, new JsonSerializerOptions { WriteIndented = true });
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return "Component,Field,Value,Classification,Timestamp,Source\r\n" + string.Join("\r\n", cells.Select(c =>
                string.Join(",", new[] { c.Component, c.Field, c.Value, c.Classification, c.Timestamp.ToString("O"), c.Source }.Select(Csv))));
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return Schema + "\n" + Timestamp.ToString("O") + "\n" + string.Join("\n", cells.Select(c => $"{c.Component} / {c.Field}: {c.Value} [{c.Classification}; {c.Timestamp:O}; {c.Source}]"));
        throw new ArgumentException("Exportformat nicht unterstützt.");
    }
    internal static string Csv(string value) => HardwareReport.Csv(value);
    private sealed record ExportCell(string Component, string Field, string Value, string Classification, DateTimeOffset Timestamp, string Source);
}

internal sealed record ProcessViewPreferences(int Version, string Filter, bool Tree, bool Active, bool Busy, string Name = "Standard")
{
    private static string FilePath => Path.Combine(Path.GetDirectoryName(CockpitSettings.PathName)!, "process-view.json");
    private sealed record SavedViews(int Version, IReadOnlyList<ProcessViewPreferences> Views);
    internal static void Save(string filter, bool tree, bool active, bool busy, string name = "Standard")
    {
        if (filter.Length > 256 || string.IsNullOrWhiteSpace(name) || name.Length > 80)
            throw new ArgumentException("Suchfilter höchstens 256 Zeichen; Ansichtsname 1–80 Zeichen.");
        var saved = File.Exists(FilePath) ? ReadViews().Where(v => v.Name != name).ToList() : [];
        if (saved.Count >= 16) throw new InvalidOperationException("Maximal 16 gespeicherte Prozessansichten.");
        saved.Add(new(1, filter, tree, active, busy, name));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new SavedViews(2, saved))); File.Move(temp, FilePath, true);
    }
    internal static IReadOnlyList<string> Names() => ReadViews().Select(v => v.Name).ToArray();
    private static IReadOnlyList<ProcessViewPreferences> ReadViews()
    {
        if (new FileInfo(FilePath).Length > 32768) throw new JsonException("Ansichtsdatei zu groß.");
        var saved = JsonSerializer.Deserialize<SavedViews>(File.ReadAllText(FilePath)) ?? throw new JsonException("Ansichten fehlen.");
        if (saved.Version != 2 || saved.Views is null || saved.Views.Count > 16 ||
            saved.Views.Any(v => v is null || v.Version != 1 || v.Filter is null || v.Filter.Length > 256 || string.IsNullOrWhiteSpace(v.Name) || v.Name.Length > 80) ||
            saved.Views.Select(v => v.Name).Distinct().Count() != saved.Views.Count)
            throw new JsonException("Gespeicherte Ansichten ungültig.");
        return saved.Views;
    }
    internal static ProcessViewPreferences Load(string name = "Standard") =>
        ReadViews().SingleOrDefault(v => v.Name == name) ?? throw new InvalidOperationException("Ansicht nicht gefunden.");
}
