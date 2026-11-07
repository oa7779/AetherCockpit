namespace SystemCockpit.Services;

internal static class BoardComponentReadings
{
    internal sealed record Metric(string Name, double? Value, string Unit, double Scale)
    {
        internal string Display => Value is { } value && double.IsFinite(value) ? $"{value:0.##} {Unit}" : "--";
        internal double Level => Value is { } value && double.IsFinite(value) ? Math.Clamp(value / Scale * 100, 0, 100) : double.NaN;
    }

    internal static IReadOnlyList<Metric> Metrics(string component, BoardFeedFrame? frame)
    {
        if (frame is null) return [];
        var channels = SensorSemantics.MeasurementChannels(frame.Sensors ?? []).Channels.Values;
        var selected = channels.Where(s => Family(s.Id) == component)
            .OrderBy(s => s.Hardware, StringComparer.Ordinal).ThenBy(s => s.Type, StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Id, StringComparer.Ordinal);
        var result = selected.Select(s => new Metric(s.Hardware + " / " + s.Name, s.Value, s.Unit, Scale(s))).ToList();
        if (component is "cpu" or "ram" or "gpu")
        {
            var text = component == "cpu" ? frame.Cpu : component == "ram" ? frame.Ram : frame.Gpu;
            result.Insert(0, new("Auslastung", double.TryParse(text, out var value) && double.IsFinite(value) ? value : null, "%", 100));
        }
        if (component == "network")
        {
            result.Insert(0, new("Download", double.TryParse(frame.Download, out var down) && double.IsFinite(down) ? down : null, "Mbit/s", 1000));
            result.Insert(1, new("Upload", frame.Upload, "Mbit/s", 1000));
        }
        return result;
    }

    private static string Family(string id) =>
        id.StartsWith("/intelcpu/", StringComparison.OrdinalIgnoreCase) || id.StartsWith("/amdcpu/", StringComparison.OrdinalIgnoreCase) ? "cpu" :
        id.StartsWith("/gpu-", StringComparison.OrdinalIgnoreCase) ? "gpu" :
        id.StartsWith("/ram/", StringComparison.OrdinalIgnoreCase) ? "ram" :
        SensorSemantics.IsStorage(id) ? "storage" :
        id.StartsWith("/nic/", StringComparison.OrdinalIgnoreCase) ? "network" :
        id.StartsWith("/mainboard/", StringComparison.OrdinalIgnoreCase) || id.StartsWith("/lpc/", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("/superio/", StringComparison.OrdinalIgnoreCase) ? "controller" : "other";

    private static double Scale(SensorReading sensor)
    {
        var reference = sensor.Type switch { "Load" or "Control" or "Level" => 100d,
            "Temperature" => 120, "Clock" => 6000, "Fan" => 4000, "Voltage" => 12, "Power" => 500, _ => 1 };
        var value = sensor.Value is { } v && double.IsFinite(v) ? Math.Abs(v) : 0;
        return Math.Max(reference, Math.Ceiling(value * 1.25 / reference) * reference);
    }

    internal static string Describe(string component, BoardFeedFrame? frame)
    {
        if (frame is null) return "LAST -- / TEMP --";
        var sensors = SensorSemantics.MeasurementChannels(frame.Sensors ?? []).Channels.Values;
        IEnumerable<SensorReading> selected = component switch
        {
            "cpu" => sensors.Where(s => s.Id.StartsWith("/intelcpu/", StringComparison.OrdinalIgnoreCase) ||
                s.Id.StartsWith("/amdcpu/", StringComparison.OrdinalIgnoreCase)),
            "gpu" => CockpitModel.SelectGpu(sensors),
            "ram" => sensors.Where(s => s.Id.StartsWith("/ram/", StringComparison.OrdinalIgnoreCase)),
            "storage" => sensors.Where(s => SensorSemantics.IsStorage(s.Id)),
            "controller" => sensors.Where(s => s.Id.StartsWith("/mainboard/", StringComparison.OrdinalIgnoreCase)),
            _ => []
        };
        var measured = selected.Where(s => !SensorSemantics.IsTemperatureLimit(s)).ToArray();
        var temperature = measured.Where(s => s.Type == "Temperature" && s.Value is { } v && double.IsFinite(v))
            .MaxBy(s => s.Value);
        var load = component switch
        {
            "cpu" => frame.Cpu + " %",
            "gpu" => frame.Gpu + " %",
            "ram" => frame.Ram + " %",
            _ => "--"
        };
        var temp = component == "cpu" ? frame.Temperature : temperature?.Display ?? "--";
        if (component == "network")
            return $"RX {frame.Download} Mbit/s\nTX {(frame.Upload is { } upload && double.IsFinite(upload) ? upload.ToString("0.0") : "--")} Mbit/s";
        if (component is "usb" or "sound" or "monitor" or "other") return "LAST -- / TEMP --";
        if (component == "storage")
        {
            var activity = measured.Where(s => s.Type == "Load" && s.Value is { } value && double.IsFinite(value))
                .MaxBy(s => s.Value);
            load = activity?.Display ?? "--";
        }
        return $"LAST {load}" + (component == "storage" && load != "--" ? " (Max.)" : "") +
            $"\nTEMP {temp}" + (component is "gpu" or "ram" or "storage" or "controller" &&
                temperature is not null ? " (Max.)" : "");
    }
}
