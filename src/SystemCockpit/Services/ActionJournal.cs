using System.IO;
using System.Text.Json;
using AetherOperations;

namespace SystemCockpit.Services;

public static class ActionJournal
{
    // Serialisiert Limitpruefung und Append, damit parallele Aktionen weder das
    // Tageslimit unterlaufen noch JSONL-Zeilen ineinander schreiben.
    private static readonly object Gate = new();
    public const long MaxFileBytes = 16 * 1024 * 1024;
    public const int MaxFiles = 30;
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherCockpit", "Actions");
    public static void AppendTechnical(string action, TechnicalEvent entry, object details) =>
        AppendLine(FormatTechnical(action, entry, details));

    public static string FormatTechnical(string action, TechnicalEvent entry, object details) =>
        JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.Now, action, details, technical = entry.JournalDetails()
        }) + Environment.NewLine;
    public static void Append(string action, object details)
    {
        var line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.Now, action, details }) + Environment.NewLine;
        AppendLine(line);
    }

    private static void AppendLine(string line)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, $"actions-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            if ((File.Exists(path) ? new FileInfo(path).Length : 0) + System.Text.Encoding.UTF8.GetByteCount(line) > MaxFileBytes)
                throw new IOException("Aktionsprotokoll voll (16 MiB/Tag). Aktion nicht ohne Protokoll starten.");
            File.AppendAllText(path, line);
            foreach (var expired in Directory.EnumerateFiles(DirectoryPath, "actions-*.jsonl")
                         .OrderByDescending(p => p, StringComparer.Ordinal).Skip(MaxFiles))
                File.Delete(expired);
        }
    }
}
