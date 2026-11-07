using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

public sealed class LocalHistory(string directory)
{
    // Serialisiert Limitpruefung und Append gegen parallele Aufzeichnungsaufrufe.
    private readonly object _gate = new();
    public string DirectoryPath { get; } = directory;
    public const long MaxFileBytes = 16 * 1024 * 1024;
    public const int MaxFiles = 7;

    public void Append(LiveSample sample)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, $"metrics-{sample.Timestamp.UtcDateTime:yyyy-MM-dd}.jsonl");
        var line = JsonSerializer.Serialize(new
        {
            schema = 1,
            timestamp = sample.Timestamp,
            sample.CpuPercent,
            sample.MemoryPercent,
            sample.MemoryUsedGb,
            sample.MemoryTotalGb,
            sample.DownloadMbps,
            sample.UploadMbps,
            sample.Sensors
        }) + Environment.NewLine;
        lock (_gate)
        {
            if (new FileInfo(path) is { Exists: true } file &&
                file.Length + System.Text.Encoding.UTF8.GetByteCount(line) > MaxFileBytes)
                throw new IOException("Tageslimit von 16 MiB erreicht. Aufzeichnung ist aus; fuer einen neuen Tag manuell wieder aktivieren.");
            File.AppendAllText(path, line);
            var files = Directory.EnumerateFiles(DirectoryPath, "metrics-*.jsonl")
                .Select(p => new FileInfo(p)).OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList();
            foreach (var expired in files.Skip(MaxFiles)) expired.Delete();
        }
    }
}
