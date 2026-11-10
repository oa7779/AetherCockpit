using AetherOperations;

namespace SystemCockpit.Services;

public sealed class HardwareInventoryStore
{
    private readonly Dictionary<HardwareClass, IReadOnlyList<HardwareComponent>> _groups = [];
    private readonly Dictionary<HardwareClass, long> _versions = [];
    public long Version(HardwareClass category) => _versions.GetValueOrDefault(category);
    public bool Contains(HardwareClass category) => _groups.ContainsKey(category);
    public bool NeedsRefresh(HardwareClass category, DateTimeOffset now) =>
        !_groups.TryGetValue(category, out var components) ||
        components.SelectMany(c => c.Facts).Any(f => f.At(now).Observation.Metadata.Availability is
            DataAvailability.Stale or DataAvailability.ProviderError);

    public void Replace(HardwareClass category, IReadOnlyList<HardwareComponent> components, long startedAtVersion)
    {
        if (components.Any(c => c.Class != category) || components.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != components.Count)
            throw new ArgumentException("Ungültige Hardwaregruppe oder doppelte Identität.", nameof(components));
        _groups[category] = Version(category) == startedAtVersion ? components : Stale(components, "Geräteänderung während der Abfrage; erneut aktualisieren.");
    }

    public void Invalidate(IEnumerable<HardwareClass> categories, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        foreach (var category in categories.Distinct())
        {
            _versions[category] = checked(Version(category) + 1);
            if (_groups.TryGetValue(category, out var components)) _groups[category] = Stale(components, reason);
        }
    }

    public void Fail(HardwareClass category, string reason, DataAvailability availability, DateTimeOffset time)
    {
        if (availability is not (DataAvailability.ProviderError or DataAvailability.AccessDenied or DataAvailability.Unsupported))
            throw new ArgumentOutOfRangeException(nameof(availability));
        var old = _groups.GetValueOrDefault(category) ?? [];
        _groups[category] = Stale(old.Where(c => c.Id != "refresh:" + category).ToArray(), reason)
            .Append(new HardwareComponent("refresh:" + category, category, "Abfragefehler",
                [HardwareFact.Missing("refresh", "Letzte Aktualisierung", "AETHER Hardwareprovider", time, reason, availability)])).ToArray();
    }

    private static IReadOnlyList<HardwareComponent> Stale(IReadOnlyList<HardwareComponent> components, string reason) =>
        components.Select(c => c with { Facts = c.Facts.Select(f => f.Observation.HasValue
            ? f with { Observation = f.Observation.AsStale(reason) } : f).ToArray() }).ToArray();

    public HardwareInventory Snapshot(DateTimeOffset now) =>
        new HardwareInventory(now, _groups.OrderBy(g => g.Key).SelectMany(g => g.Value).ToArray()).At(now);
}

public static class HardwareSensorInventory
{
    public static IReadOnlyList<HardwareComponent> Read(IReadOnlyList<SensorReading> readings,
        DateTimeOffset timestamp, bool enabled, string? error)
    {
        if (!enabled || error is not null || readings.Count == 0)
            return [new("sensors:status", HardwareClass.Sensors, "Sensorprovider",
                [HardwareFact.Missing("status", "Sensorzugriff", "Cockpit / LibreHardwareMonitor", timestamp,
                    error is not null ? "Bestehender Sensorprovider meldet einen Fehler; Details im Cockpit." :
                        !enabled ? "Sensorzugriff im Cockpit deaktiviert." : "Provider meldet keine unterstützten Sensoren.",
                    error is null ? DataAvailability.Unavailable : DataAvailability.ProviderError)])];
        if (readings.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != readings.Count)
            return [new("sensors:identity-error", HardwareClass.Sensors, "Sensoridentitäten",
                [HardwareFact.Missing("identity-error", "Sensorzuordnung", "LibreHardwareMonitor / bestehender Cockpit-Sensorpfad",
                    timestamp, "Doppelte Sensoridentitäten; keine willkürliche Zuordnung vorgenommen.", DataAvailability.ProviderError)])];
        return readings.Select(s =>
        {
            var value = s.Value is { } number && double.IsFinite(number) ? number : (double?)null;
            var metadata = value.HasValue
                ? HardwareIntelligenceReader.Metadata("LibreHardwareMonitor / bestehender Cockpit-Sensorpfad", timestamp)
                : new SourceMetadata("LibreHardwareMonitor / bestehender Cockpit-Sensorpfad", SourceType.Direct, timestamp,
                    DataConfidence.Unknown, DataAvailability.Unavailable, error: "Provider meldet keinen endlichen Messwert.");
            var current = value.HasValue
                ? new HardwareFact("current", SensorSemantics.IsTemperatureLimit(s) ? "Temperaturgrenze (keine Messung)" : "Aktuell",
                    SourceObservation<string>.Present(value.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture), s.Unit, metadata),
                    HardwareCadence.Live)
                : HardwareFact.Missing("current", "Aktuell", metadata.Source, timestamp, metadata.Error!, unit: s.Unit)
                    with { Cadence = HardwareCadence.Live };
            HardwareFact Static(string key, string name, string text) => new(key, name,
                SourceObservation<string>.Present(text, "", HardwareIntelligenceReader.Metadata("LibreHardwareMonitor", timestamp)));
            return new HardwareComponent(HardwareIntelligenceReader.Identity("sensor", s.Id), HardwareClass.Sensors,
                $"{s.Type} / {s.Hardware} / {s.Name}",
                [Static("hardware", "Hardware", s.Hardware), Static("name", "Sensor", s.Name), Static("type", "SensorType", s.Type), current,
                 HardwareFact.Missing("history", "Minimum / Maximum / Average", "AETHER Hardwarezentrale", timestamp,
                     "Keine eigene Messhistorie erfasst. Vorhandene Cockpit-Messauswertung bleibt separat; keine erfundenen Statistikwerte.")]);
        }).ToArray();
    }
}
