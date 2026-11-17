using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

public sealed record HistoryPoint(DateTimeOffset Timestamp, double Value);
public sealed record HistorySeries(string Id, string Label, string Unit, List<HistoryPoint> Points)
{
    public override string ToString() => Label;
    public string Summary => DesktopUiText.Format("History.Summary", Points.Count, Points.Min(p => p.Value),
        Points.Max(p => p.Value), Points.Average(p => p.Value), Unit);
}
public sealed record HistoryReport(List<HistorySeries> Series, List<string> Issues);

public static class HistoryReader
{
    public static HistoryReport Read(string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > LocalHistory.MaxFileBytes)
            throw new InvalidDataException(DesktopUiText.Get("History.FileTooLarge"));
        var series = new Dictionary<string, HistorySeries>();
        var issues = new List<string>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            token.ThrowIfCancellationRequested();
            if (++lineNumber > 43200) throw new InvalidDataException(DesktopUiText.Get("History.TooManyPoints"));
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Length > 1024 * 1024) throw new InvalidDataException(DesktopUiText.Get("History.LineTooLarge"));
            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                if (root.GetProperty("schema").GetInt32() != 1) throw new InvalidDataException(DesktopUiText.Get("History.UnknownSchema"));
                var time = root.GetProperty("timestamp").GetDateTimeOffset();
                Add("native/cpu", DesktopUiText.Get("History.TotalCpu"), "%", root.GetProperty("CpuPercent"), time);
                Add("native/ram", DesktopUiText.Get("History.MemoryUsage"), "%", root.GetProperty("MemoryPercent"), time);
                Add("native/down", "Download", "Mbit/s", root.GetProperty("DownloadMbps"), time);
                Add("native/up", "Upload", "Mbit/s", root.GetProperty("UploadMbps"), time);
                var readings = new List<SensorReading>();
                foreach (var sensor in root.GetProperty("Sensors").EnumerateArray())
                {
                    var id = sensor.GetProperty("Id").GetString() ?? throw new InvalidDataException(DesktopUiText.Get("History.MissingSensorId"));
                    var name = sensor.GetProperty("Name").GetString() ?? "";
                    var type = sensor.TryGetProperty("Type", out var sensorType) ? sensorType.GetString() ?? "" : "";
                    readings.Add(new(id, sensor.GetProperty("Hardware").GetString() ?? "", name, type,
                        Number(sensor.GetProperty("Value")), sensor.GetProperty("Unit").GetString() ?? ""));
                }
                var (channels, warning) = SensorSemantics.MeasurementChannels(readings);
                if (warning.Length > 0 && !issues.Contains(warning))
                {
                    if (issues.Count >= 100) throw new InvalidDataException(DesktopUiText.Get("History.TooManyNotices"));
                    issues.Add(warning);
                }
                foreach (var (key, sensor) in channels)
                    AddNumber("sensor/" + JsonSerializer.Serialize(key), $"{sensor.Hardware} / {sensor.Name}",
                        sensor.Unit, sensor.Value, time);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or KeyNotFoundException or FormatException or InvalidOperationException)
            {
                if (issues.Count < 100) issues.Add(DesktopUiText.Format("History.LineError", lineNumber, ex.Message));
                else throw new InvalidDataException(DesktopUiText.Get("History.TooManyDamagedLines"));
            }
        }
        foreach (var item in series.Values) item.Points.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return new(series.Values.OrderBy(s => s.Label).ToList(), issues);

        void Add(string id, string label, string unit, JsonElement value, DateTimeOffset time)
        {
            AddNumber(id, label, unit, Number(value), time);
        }

        void AddNumber(string id, string label, string unit, double? value, DateTimeOffset time)
        {
            if (value is not { } number) return;
            if (!series.TryGetValue(id, out var item))
            {
                if (series.Count >= 2048) throw new InvalidDataException(DesktopUiText.Get("History.TooManySeries"));
                series[id] = item = new(id, label, unit, []);
            }
            item.Points.Add(new(time, number));
        }
    }

    private static double? Number(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (!value.TryGetDouble(out var number) || !double.IsFinite(number))
            throw new InvalidDataException(DesktopUiText.Get("History.NonFiniteValue"));
        return number;
    }

    public static string Compare(HistoryReport first, HistoryReport second)
    {
        var matching = first.Series.Join(second.Series, a => a.Id, b => b.Id, (a, b) =>
            $"{a.Label}: AVG {a.Points.Average(p => p.Value):0.##} -> {b.Points.Average(p => p.Value):0.##} {a.Unit} " +
            $"| MAX {a.Points.Max(p => p.Value):0.##} -> {b.Points.Max(p => p.Value):0.##} {a.Unit}").ToList();
        return matching.Count == 0 ? DesktopUiText.Get("History.NoMatchingSeries") : string.Join(Environment.NewLine, matching);
    }
}
