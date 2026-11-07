namespace SystemCockpit.Services;

public readonly record struct SensorChannelKey(string Id, string Hardware, string Name, string Type, string Unit);

public static class SensorSemantics
{
    public static SensorChannelKey ChannelKey(SensorReading sensor) =>
        new(sensor.Id, sensor.Hardware, sensor.Name, sensor.Type, sensor.Unit);

    public static (Dictionary<SensorChannelKey, SensorReading> Channels, string Warning) MeasurementChannels(
        IEnumerable<SensorReading> readings)
    {
        var all = readings.ToArray();
        var duplicateIds = all.GroupBy(s => s.Id, StringComparer.Ordinal).Count(g => g.Count() > 1);
        var channels = new Dictionary<SensorChannelKey, SensorReading>();
        var conflicts = 0;
        foreach (var group in all.Where(s => !IsTemperatureLimit(s)).GroupBy(ChannelKey))
        {
            var sensor = group.First();
            if (group.Select(s => s.Value is { } v && double.IsFinite(v) ? v : (double?)null).Distinct().Count() > 1)
            { sensor = sensor with { Value = null }; conflicts++; }
            channels.Add(group.Key, sensor);
        }
        var warning = duplicateIds == 0 ? "" :
            $"{duplicateIds} mehrfach gemeldete Sensor-IDs: unterschiedliche Kanalnamen getrennt; identische Meldungen zusammengefasst." +
            (conflicts == 0 ? "" : $" {conflicts} widerspruechliche Kanaele nicht verfuegbar.");
        return (channels, warning);
    }

    public static bool IsStorage(string id) =>
        id.StartsWith("/nvme/", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("/ssd/", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("/hdd/", StringComparison.OrdinalIgnoreCase);

    public static bool IsTemperatureLimit(string id, string name, string type) =>
        type == "TemperatureLimit" ||
        IsStorage(id) && type == "Temperature" &&
        (name.Equals("Warning Temperature", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("Critical Temperature", StringComparison.OrdinalIgnoreCase));

    public static bool IsTemperatureLimit(SensorReading sensor) =>
        IsTemperatureLimit(sensor.Id, sensor.Name, sensor.Type);

    public static SensorReading Classify(SensorReading sensor) =>
        IsTemperatureLimit(sensor) ? sensor with { Type = "TemperatureLimit" } : sensor;

    public static SensorReading? CpuTemperature(IEnumerable<SensorReading> sensors)
    {
        var cpu = sensors.Where(s => IsCpu(s.Id) && s.Type == "Temperature" &&
            s.Value is { } v && double.IsFinite(v)).ToList();
        return cpu.FirstOrDefault(s => s.Name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase)) ??
            cpu.FirstOrDefault(s => s.Name.Equals("Core (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase)) ??
            cpu.FirstOrDefault(s => s.Name.Equals("Core (Tdie)", StringComparison.OrdinalIgnoreCase)) ??
            cpu.Where(s => s.Name.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase)).MaxBy(s => s.Value);
    }

    public static SensorReading? CpuPower(IEnumerable<SensorReading> sensors) =>
        sensors.FirstOrDefault(s => IsCpu(s.Id) && s.Type == "Power" &&
            s.Name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase) && s.Value is { } v && double.IsFinite(v));

    private static bool IsCpu(string id) => id.StartsWith("/intelcpu/", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("/amdcpu/", StringComparison.OrdinalIgnoreCase);
}
