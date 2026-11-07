using System.Text.RegularExpressions;

namespace SystemCockpit.Services;

public sealed record WingetRow(string Name, string Id, string Version, string Available, string Source)
{
    // Nur eindeutig lesbare IDs sind aktionierbar; abgeschnittene Spalten enden mit U+2026.
    public bool Actionable => PackageCenter.ValidId(Id);
    public string ActionableDisplay => Actionable ? "ja" : "nein / ID unvollstaendig";
}

public static partial class PackageCenter
{
    public const int MaxIds = 100;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 ._+\-\\{}()]{0,199}$")]
    private static partial Regex IdPattern();
    [GeneratedRegex(@"\S+")]
    private static partial Regex HeaderToken();

    public static bool ValidId(string id) =>
        id.Length > 0 && !id.Contains('\u2026') && !id.Contains('\'') && !id.Contains('"') &&
        !id.Contains('`') && !id.Contains('$') && !id.EndsWith('.') && IdPattern().IsMatch(id);

    public static string ListScript => Script("list", null);
    public static string UpgradeListScript => Script("upgrade", null);

    public static string SearchScript(string term)
    {
        var trimmed = term.Trim();
        if (trimmed.Length is < 1 or > 100 || trimmed.Any(char.IsControl) || trimmed.Contains('\'') ||
            trimmed.Contains('"') || trimmed.Contains('`') || trimmed.Contains('$'))
            throw new ArgumentException("Suchbegriff: 1..100 Zeichen ohne Anfuehrungszeichen, Backticks oder $.");
        return Script("search", trimmed);
    }

    private static string Script(string verb, string? argument) =>
        $"& winget.exe {verb}{(argument is null ? "" : " " + AdminManagement.Quote(argument))} --disable-interactivity; " +
        "if($LASTEXITCODE -eq -1978335189){'KEINE PASSENDEN EINTRAEGE'; $global:LASTEXITCODE=0} " +
        "elseif($LASTEXITCODE -ne 0){throw \"WinGet Exit $LASTEXITCODE\"}";

    // Jede ID laeuft einzeln mit --exact; Fehler werden pro ID gemeldet, weitere IDs laufen weiter.
    public static string ActionScript(string verb, IReadOnlyList<string> ids)
    {
        if (verb is not ("install" or "upgrade" or "uninstall"))
            throw new ArgumentException("Nur install, upgrade oder uninstall.");
        if (ids.Count is < 1 or > MaxIds || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count)
            throw new ArgumentException($"1..{MaxIds} eindeutige Paket-IDs erforderlich.");
        foreach (var id in ids)
            if (!ValidId(id)) throw new ArgumentException($"Keine eindeutig lesbare WinGet-ID: {id}");
        var silent = verb == "uninstall" ? "" : " --silent --accept-package-agreements";
        var lines = ids.Select(id =>
            $"& winget.exe {verb} --id {AdminManagement.Quote(id)} --exact{silent} --accept-source-agreements --disable-interactivity; " +
            $"if($LASTEXITCODE -eq -1978335189){{'KEIN PASSENDES UPGRADE / {id.Replace("'", "''")}'}} " +
            $"elseif($LASTEXITCODE -ne 0){{[Console]::Error.WriteLine(\"FEHLER Exit $LASTEXITCODE / {id.Replace("\"", "`\"").Replace("'", "''")}\"); $script:aetherPkgFails++}}");
        return "$script:aetherPkgFails=0\n" + string.Join("\n", lines) +
            "\nif($script:aetherPkgFails -ne 0){exit 1}\n$global:LASTEXITCODE=0";
    }

    // winget-Tabellen sind konsolenbreiten-orientiert: Spaltenstarts kommen aus der Kopfzeile.
    // Zeilen mit Sonderbreitenzeichen koennen verschoben sein; unplausible IDs bleiben nicht aktionierbar.
    public static List<WingetRow> ParseTable(string output)
    {
        var rows = new List<WingetRow>();
        var lines = output.Replace("\r", "").Split('\n')
            .Select(l => new string(l.Where(c => !char.IsControl(c)).ToArray())).ToArray();
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            var divider = lines[i + 1].Trim();
            if (divider.Length < 5 || divider.Any(c => c != '-')) continue;
            var columns = HeaderToken().Matches(lines[i]).ToList();
            var id = columns.FindIndex(m => m.Value is "Id" or "ID");
            var version = columns.FindIndex(m => m.Value is "Version");
            if (id < 0 || version < 0 || columns.Count < 2) continue;
            var available = columns.FindIndex(m => m.Value is "Verfuegbar" or "Verfügbar" or "Available");
            var source = columns.FindIndex(m => m.Value is "Quelle" or "Source");
            for (var j = i + 2; j < lines.Length && lines[j].Trim().Length > 0; j++)
            {
                var line = lines[j];
                if (line.Length <= columns[1].Index || !char.IsWhiteSpace(line[columns[1].Index - 1])) continue;
                string Slice(int column)
                {
                    if (column < 0 || columns[column].Index >= line.Length) return "";
                    var end = column + 1 < columns.Count ? Math.Min(columns[column + 1].Index, line.Length) : line.Length;
                    return line[columns[column].Index..end].Trim();
                }
                var row = new WingetRow(Slice(0), Slice(id), Slice(version), Slice(available), Slice(source));
                if (row.Name.Length > 0 && row.Id.Length > 0) rows.Add(row);
            }
            break;
        }
        return rows;
    }
}
