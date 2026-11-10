using AetherOperations;

namespace SystemCockpit.Services;

internal static class HardwareSensorCorrelation
{
    internal static IReadOnlyList<HardwareComponent> Apply(IReadOnlyList<HardwareComponent> sensors,
        IReadOnlyList<SensorReading> readings, HardwareInventory inventory, DateTimeOffset time)
    {
        if (readings.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != readings.Count)
            return sensors;
        var result = sensors.ToList();
        foreach (var group in readings.Where(s => !string.IsNullOrWhiteSpace(s.ProviderDeviceId))
                     .GroupBy(s => s.ProviderDeviceId!, StringComparer.Ordinal))
        {
            var id = HardwareIntelligenceReader.Identity("sensor-device", group.Key);
            var pnpIds = group.Select(s => s.PnpDeviceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var parentIds = group.Select(s => s.ProviderParentId).Distinct(StringComparer.Ordinal).ToArray();
            var ambiguous = pnpIds.Length != 1 || parentIds.Length != 1;
            var targets = ambiguous || string.IsNullOrWhiteSpace(pnpIds[0]) ? [] : inventory.Components
                .Where(c => c.Class != HardwareClass.Sensors && c.Facts.Any(f => f.Key == "PNPDeviceID" &&
                    f.Observation.Metadata.Availability == DataAvailability.Available && f.Observation.HasValue &&
                    string.Equals(f.Observation.Value, pnpIds[0], StringComparison.OrdinalIgnoreCase))).ToArray();
            HardwareFact Identity(string key, string caption, string value) => new(key, caption,
                SourceObservation<string>.Present(value, "", HardwareIntelligenceReader.Metadata(
                    "LibreHardwareMonitor IHardware.Identifier / Parent; GenericGpu.DeviceId", time,
                    classification: DataClassification.Sensitive)));
            var facts = new List<HardwareFact> { Identity("provider-device", "Exakte Provider-Gerätekennung", group.Key) };
            if (!ambiguous && pnpIds[0] is { Length: > 0 } pnp) facts.Add(Identity("provider-pnp", "Provider-Gerätekennung für PnP-Abgleich", pnp));
            facts.Add(targets.Length == 1
                ? HardwareIntelligenceReader.Derived("correlation", "Windows-Gerätezuordnung", "Exakter eindeutiger PnP-ID-Abgleich",
                    "LibreHardwareMonitor GenericGpu.DeviceId / CIM PNPDeviceID", time,
                    "OrdinalIgnoreCase-Gleichheit vollständiger verfügbarer Gerätekennungen; keine Namens-, Reihenfolge- oder Herstellerheuristik.")
                : HardwareFact.Missing("correlation", "Windows-Gerätezuordnung", "LibreHardwareMonitor / CIM PNPDeviceID", time,
                    ambiguous ? "Widersprüchliche Providerbeziehungen; keine Zuordnung." :
                    targets.Length > 1 ? "Mehrere passende Windows-Geräte; keine Zuordnung." :
                    "Keine exakte eindeutige Beziehung zu einer geladenen Windows-Geräteinstanz; Providergerät bleibt getrennt."));
            string? parent = targets.Length == 1 ? targets[0].Id :
                !ambiguous && parentIds[0] is { Length: > 0 } providerParent &&
                readings.Any(s => s.ProviderDeviceId == providerParent)
                    ? HardwareIntelligenceReader.Identity("sensor-device", providerParent) : null;
            if (parent == id) parent = null;
            result.Add(new(id, HardwareClass.Sensors, "Providergerät / " + group.First().Hardware, facts, parent));
            foreach (var reading in group)
            {
                var sensorId = HardwareIntelligenceReader.Identity("sensor", reading.Id);
                var index = result.FindIndex(c => c.Id == sensorId);
                if (index >= 0)
                    result[index] = result[index] with { ParentId = ambiguous ? null : id,
                        Facts = result[index].Facts.Append(Identity("provider-device", "Sensor gehört zu Providergerät", group.Key)).ToArray() };
            }
        }
        return result;
    }
}
