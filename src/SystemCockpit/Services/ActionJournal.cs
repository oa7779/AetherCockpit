using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

public static class ActionJournal
{
    // Serialisiert Limitpruefung und Append, damit parallele Aktionen weder das
    // Tageslimit unterlaufen noch JSONL-Zeilen ineinander schreiben.
    private static readonly object Gate = new();
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherCockpit", "Actions");
    public static void Append(string action, object details)
    {
        var line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.Now, action, details }) + Environment.NewLine;
        lock (Gate)
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, $"actions-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            if (File.Exists(path) && new FileInfo(path).Length + System.Text.Encoding.UTF8.GetByteCount(line) > 16 * 1024 * 1024)
                throw new IOException("Aktionsprotokoll voll (16 MiB/Tag). Aktion nicht ohne Protokoll starten.");
            File.AppendAllText(path, line);
            foreach (var expired in Directory.EnumerateFiles(DirectoryPath, "actions-*.jsonl")
                         .OrderByDescending(p => p, StringComparer.Ordinal).Skip(30))
                File.Delete(expired);
        }
    }
}
