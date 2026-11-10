using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

internal sealed record TaskCenterPreferences(int Version, IReadOnlyList<string> Favorites, IReadOnlyList<string> Recent)
{
    private static string FilePath => Path.Combine(Path.GetDirectoryName(CockpitSettings.PathName)!, "task-center.json");
    internal static TaskCenterPreferences Empty { get; } = new(1, [], []);
    internal static TaskCenterPreferences Load()
    {
        if (!File.Exists(FilePath)) return Empty;
        if (new FileInfo(FilePath).Length > 65536) throw new InvalidDataException("Befehlspaletten-Einstellungen zu groß.");
        var saved = JsonSerializer.Deserialize<TaskCenterPreferences>(File.ReadAllText(FilePath)) ?? throw new JsonException("Befehlspalette ungültig.");
        if (saved.Version != 1 || saved.Favorites is null || saved.Recent is null || saved.Favorites.Count > 64 || saved.Recent.Count > 12)
            throw new JsonException("Befehlspaletten-Einstellungen ungültig.");
        var known = TaskCatalog.All.Select(t => t.Title).ToHashSet(StringComparer.Ordinal);
        return new(1, saved.Favorites.Where(known.Contains).Distinct().ToArray(), saved.Recent.Where(known.Contains).Distinct().ToArray());
    }
    internal TaskCenterPreferences Toggle(string title) => this with
    {
        Favorites = Favorites.Contains(title) ? Favorites.Where(t => t != title).ToArray() :
            Favorites.Count < 64 ? Favorites.Append(title).ToArray() : throw new InvalidOperationException("Maximal 64 Werkzeugfavoriten.")
    };
    internal TaskCenterPreferences Use(string title) => this with { Recent = new[] { title }.Concat(Recent.Where(t => t != title)).Take(12).ToArray() };
    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this)); File.Move(temp, FilePath, true);
    }
}
