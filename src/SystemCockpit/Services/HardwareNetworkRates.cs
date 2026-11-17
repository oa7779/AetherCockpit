using System.Globalization;
using AetherOperations;

namespace SystemCockpit.Services;

internal sealed class HardwareNetworkRates
{
    private readonly Dictionary<(string Id, string Direction), (ulong Bytes, DateTimeOffset Time)> _previous = [];

    internal IReadOnlyList<HardwareComponent> Apply(IReadOnlyList<HardwareComponent> components)
    {
        var active = components.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _previous.Keys.Where(k => !active.Contains(k.Id)).ToArray()) _previous.Remove(key);
        return components.Select(component =>
        {
            var facts = component.Facts.ToList();
            foreach (var direction in new[] { "Rx", "Tx" })
            {
                var key = (component.Id, direction);
                var counter = facts.SingleOrDefault(f => f.Key == direction + " Bytes (kumuliert; keine Rate)");
                if (counter is null) continue;
                var time = counter.Observation.Metadata.Timestamp;
                var rateKey = direction + "-rate";
                var name = direction + " / mittlere Rate seit letzter Abfrage";
                const string source = ".NET NetworkInterface / Bytezählerdifferenz";
                if (!counter.Observation.HasValue || counter.Observation.Metadata.Availability != DataAvailability.Available ||
                    !ulong.TryParse(counter.Observation.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes))
                {
                    _previous.Remove(key);
                    facts.Add(HardwareFact.Missing(rateKey, name, source, time, "Kein aktueller gültiger Bytezähler.", unit: "bit/s"));
                    continue;
                }
                if (_previous.TryGetValue(key, out var old) && bytes >= old.Bytes && time > old.Time)
                {
                    var elapsed = (time - old.Time).TotalSeconds;
                    var rate = (bytes - old.Bytes) * 8d / elapsed;
                    facts.Add(HardwareIntelligenceReader.Derived(rateKey, name, rate.ToString("R", CultureInfo.InvariantCulture),
                        source, time, $"(Bytes neu − Bytes alt) × 8 / {elapsed.ToString("R", CultureInfo.InvariantCulture)} s; " +
                        $"{old.Time:O} bis {time:O}. Mittel über Abfrageintervall, kein Momentanwert.", "bit/s")
                        with { Cadence = HardwareCadence.Dynamic });
                }
                else facts.Add(HardwareFact.Missing(rateKey, name, source, time,
                    "Baseline fehlt, Zähler wurde zurückgesetzt oder Zeitstempel ist nicht aufsteigend.", unit: "bit/s"));
                _previous[key] = (bytes, time);
            }
            return component with { Facts = facts };
        }).ToArray();
    }
}
