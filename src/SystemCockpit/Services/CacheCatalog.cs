using System.IO;

namespace SystemCockpit.Services;

public sealed record CacheTarget(string Name, string Path, int Level, string Risk)
{
    public override string ToString() => $"{Name} | {Risk}";
}

public static class CacheCatalog
{
    // Transparente Bewertung statt Magie-Score: Stufe aus dem Katalog-Level,
    // dazu die realen Summen der aktuellen Vorschau und der Katalog-Hinweis.
    public static string Assess(CacheTarget target, int files, long bytes)
    {
        var grade = target.Level switch
        {
            0 => "A / risikoarm",
            1 => "B / App-Cache, Programm vorher schliessen",
            2 => "C / Diagnosedaten gehen verloren",
            _ => "D / systemnah, bewusst entscheiden"
        };
        return $"Bereinigungsbewertung {grade} | {files} Dateien | {bytes / 1048576d:0.00} MiB | {target.Risk}";
    }

    public static IReadOnlyList<CacheTarget> Discover()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var targets = new List<CacheTarget>
        {
            new("Benutzer Temp", Path.Combine(local, "Temp"), 0, "Niedrig / alte temporaere Dateien"),
            new("CrashDumps", Path.Combine(local, "CrashDumps"), 2, "Diagnosedaten gehen verloren"),
            new("Windows Temp", Path.Combine(windows, "Temp"), 2, "Admin / temporaere Dateien"),
            new("WER ReportArchive", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows", "WER", "ReportArchive"), 3, "Admin / archivierte Crashdiagnosen"),
            new("Windows Prefetch", Path.Combine(windows, "Prefetch"), 3, "Kein Leistungsgewinn; Startoptimierung wird neu aufgebaut")
        };
        foreach (var browser in new[]
        {
            ("Chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            ("Edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
            ("Opera", Path.Combine(roaming, "Opera Software", "Opera Stable"))
        })
        {
            if (!Directory.Exists(browser.Item2)) continue;
            if ((File.GetAttributes(browser.Item2) & FileAttributes.ReparsePoint) != 0) continue;
            var profiles = browser.Item1 == "Opera" ? [browser.Item2]
                : Directory.GetDirectories(browser.Item2).Where(p => Path.GetFileName(p) == "Default" || Path.GetFileName(p).StartsWith("Profile ")).ToArray();
            foreach (var profile in profiles)
                foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache" })
                    targets.Add(new($"{browser.Item1} / {Path.GetFileName(profile)} / {cache}", Path.Combine(profile, cache),
                        1, "Browser schliessen; Cache wird neu geladen"));
        }
        var firefox = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefox) && (File.GetAttributes(firefox) & FileAttributes.ReparsePoint) == 0)
            foreach (var profile in Directory.GetDirectories(firefox))
                targets.Add(new($"Firefox / {Path.GetFileName(profile)}", Path.Combine(profile, "cache2"), 1, "Nur Cache; keine Lesezeichen oder Cookies"));
        foreach (var app in new[] { "discord", "Microsoft\\Teams", "Spotify", "Zoom", "Code", "Adobe\\Common" })
            foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache" })
                targets.Add(new($"{app} / {cache}", Path.Combine(roaming, app, cache), 1, "App schliessen; keine Offline-Downloads"));
        targets.Add(new("Teams / WebView Cache", Path.Combine(local, "Packages", "MSTeams_8wekyb3d8bbwe",
            "LocalCache", "Microsoft", "MSTeams", "EBWebView", "Default", "Cache"), 1, "Teams schliessen"));
        using var steam = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (steam?.GetValue("SteamPath") is string steamPath && System.IO.Path.IsPathFullyQualified(steamPath))
            targets.Add(new("Steam / htmlcache", System.IO.Path.Combine(steamPath, "config", "htmlcache"), 1, "Steam schliessen; keine Spiele/Downloads"));
        return targets.Where(t => Directory.Exists(t.Path)).DistinctBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
