using System.Globalization;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace SystemCockpit.Services;

public sealed record FrameSummary(string Process, string Swapchain, int Frames, double AverageFps,
    double P99FrameMs, double OnePercentLowFps)
{
    public override string ToString() => $"{Process} | {Swapchain} | {Frames} Frames | " +
        $"AVG {AverageFps:0.0} FPS | P99 {P99FrameMs:0.00} ms | 1%-low {OnePercentLowFps:0.0} FPS";
}

public static class FrameTelemetry
{
    public static string DetectExecutable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe");
        return File.Exists(path) ? path : "";
    }
    public static string[] CaptureArguments(string processName, int seconds, string output, string session)
    {
        if (string.IsNullOrWhiteSpace(processName) || processName.Length > 260 ||
            Path.GetFileName(processName) != processName || !processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Exakten Prozessnamen ohne Pfad eingeben, z.B. Spiel.exe.");
        if (seconds is < 5 or > 600) throw new ArgumentException("Capture-Dauer 5 bis 600 Sekunden.");
        if (!Path.IsPathFullyQualified(output)) throw new ArgumentException("Absoluter CSV-Pfad erforderlich.");
        return ["--process_name", processName, "--timed", seconds.ToString(CultureInfo.InvariantCulture),
            "--terminate_after_timed", "--output_file", output, "--session_name", session, "--no_console_stats",
            "--v2_metrics", "--no_track_input"];
    }

    public static IReadOnlyList<FrameSummary> Read(string path)
    {
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Frame-CSV groesser als 64 MiB.");
        using var parser = new TextFieldParser(path) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException("Frame-CSV ist leer.");
        int Index(params string[] names) => Array.FindIndex(headers, h => names.Contains(h, StringComparer.OrdinalIgnoreCase));
        var frameIndex = Index("CPUFrameTime", "FrameTime", "MsBetweenPresents", "msBetweenPresents");
        var processIndex = Index("Application", "ProcessName");
        var swapIndex = Index("SwapChainAddress");
        if (frameIndex < 0 || processIndex < 0) throw new InvalidDataException("PresentMon-Framezeit-/Application-Spalten fehlen.");
        var groups = new Dictionary<(string Process, string Swapchain), List<double>>();
        var count = 0;
        while (!parser.EndOfData)
        {
            if (++count > 1_000_000) throw new InvalidDataException("Mehr als 1 Million Frames.");
            var row = parser.ReadFields() ?? throw new InvalidDataException("Ungueltige CSV-Zeile.");
            if (row.Length != headers.Length) throw new InvalidDataException($"CSV-Spaltenanzahl Zeile {count + 1} ungueltig.");
            if (!double.TryParse(row[frameIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) ||
                !double.IsFinite(ms) || ms < 0) throw new InvalidDataException($"Ungueltige Framezeit Zeile {count + 1}.");
            if (ms == 0) continue;
            var key = (row[processIndex], swapIndex >= 0 ? row[swapIndex] : "nicht gemeldet");
            if (!groups.TryGetValue(key, out var values))
            {
                if (groups.Count >= 256) throw new InvalidDataException("Mehr als 256 Swapchains.");
                groups[key] = values = [];
            }
            values.Add(ms);
        }
        return groups.Select(pair =>
        {
            var sorted = pair.Value.OrderDescending().ToArray();
            var slowCount = Math.Max(1, (int)Math.Ceiling(sorted.Length * .01));
            var ascending = sorted.Reverse().ToArray();
            var p99 = ascending[Math.Clamp((int)Math.Ceiling(ascending.Length * .99) - 1, 0, ascending.Length - 1)];
            return new FrameSummary(pair.Key.Process, pair.Key.Swapchain, sorted.Length,
                1000 / sorted.Average(), p99, 1000 / sorted.Take(slowCount).Average());
        }).OrderByDescending(s => s.Frames).ToList();
    }
}
