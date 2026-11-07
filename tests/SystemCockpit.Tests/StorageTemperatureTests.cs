using System.IO;
using SystemCockpit;
using SystemCockpit.Services;

internal static class StorageTemperatureTests
{
    public static int Run()
    {
        var count = 0;
        void Check(bool valid, string message) { count++; if (!valid) throw new InvalidOperationException(message); }
        List<SensorReading> raw =
        [
            new("/nvme/0/temperature/0", "Lexar SSD EQ790 2TB", "Composite Temperature", "Temperature", 35, "C"),
            new("/nvme/0/temperature/1", "Lexar SSD EQ790 2TB", "Temperature 1", "Temperature", 36, "C"),
            new("/nvme/0/temperature/10", "Lexar SSD EQ790 2TB", "Warning Temperature", "Temperature", 99, "C"),
            new("/nvme/0/temperature/11", "Lexar SSD EQ790 2TB", "Critical Temperature", "Temperature", 109, "C")
        ];
        var classified = raw.Select(SensorSemantics.Classify).ToList();
        Check(classified.Count(s => s.Type == "TemperatureLimit") == 2 && classified[^1].Value == 109,
            "Keep exact NVMe limit value but classify it separately from measured temperature");
        Check(classified[^1].Display.Contains("Grenzwert") && classified[0].Display == "35 C",
            "Raw matrix explicitly labels limit versus current value");
        foreach (var readings in new[] { raw, classified })
        {
            var checks = SensorDiagnostics.Evaluate(readings, new());
            Check(!checks.Any(c => c.Name.StartsWith("Storage Temperatur") && c.State == "warning"),
                "109/99 C limits must never cause an overtemperature warning, including legacy samples");
            Check(checks.Count(c => c.Name.StartsWith("Storage Temperatur")) == 2 &&
                checks.Any(c => c.Name.Contains("Composite Temperature")),
                "Actual storage measurement channels retain name and source");
            var stats = new SessionStatistics();
            var sample = new LiveSample(DateTimeOffset.Now, 20, 30, 10, 32, 1, 2, [], readings);
            stats.Add(sample);
            Check(!stats.Snapshot().Any(s => s.Name is "Warning Temperature" or "Critical Temperature"),
                "Thresholds excluded from measured MIN/MAX/AVG");
            var model = new CockpitModel();
            model.Apply(sample, true);
            model.ApplyDashboard(new(sample.Timestamp, [20], [], [], 10, 32, 1, 0, 0, TimeSpan.Zero, []), sample);
            Check(model.SensorGraphs.Count == 2 && model.Alerts.All(c => !c.Name.StartsWith("Storage Temperatur")),
                "Thresholds excluded from live graphs and systempulse alerts, measurements retained");
            var folder = Path.Combine(Path.GetTempPath(), "Aether-storage-temperature-" + Guid.NewGuid().ToString("N"));
            try
            {
                new LocalHistory(folder).Append(sample);
                var report = HistoryReader.Read(Directory.GetFiles(folder).Single(), CancellationToken.None);
                Check(report.Series.All(s => !s.Label.Contains("Warning Temperature") && !s.Label.Contains("Critical Temperature")) &&
                    report.Series.Any(s => s.Label.Contains("Composite Temperature")),
                    "New and legacy history logs must not plot static temperature thresholds");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }
        var hot = classified.Select(s => s.Name == "Composite Temperature" ? s with { Value = 109 } : s).ToList();
        Check(SensorDiagnostics.Evaluate(hot, new()).Any(c => c.Name.Contains("Composite Temperature") &&
            c.State == "warning" && c.Value.StartsWith("109")),
            "A real 109 C measurement must still warn; do not hide high temperatures by numeric cutoff");
        Check(!SensorSemantics.IsTemperatureLimit(new("/gpu-nvidia/0/temperature/1", "GPU", "GPU Hot Spot", "Temperature", 109, "C")),
            "High GPU hotspot measurement is not a storage limit");
        return count;
    }
}
