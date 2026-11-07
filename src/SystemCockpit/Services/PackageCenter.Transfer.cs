using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystemCockpit.Services;

public sealed record PackageTarget(string Id, string Source, string Name, string Version);
public sealed record PackageBundle(int Schema, List<PackageTarget> Packages);

public static partial class PackageCenter
{
    public const int MaxBundleBytes = 128 * 1024;
    private static readonly JsonSerializerOptions BundleOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static bool ValidSource(string source) => source.Length is > 0 and <= 100 &&
        char.IsAsciiLetterOrDigit(source[0]) &&
        source.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    public static ProcessStartInfo ExternalManager(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable) ||
            !new[] { "UniGetUI.exe", "WingetUI.exe" }.Contains(Path.GetFileName(executable), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Vorhandene, vertrauenswuerdige UniGetUI.exe oder WingetUI.exe auswaehlen.");
        return new ProcessStartInfo { FileName = executable, UseShellExecute = false };
    }

    public static string TargetCommandScript(string verb, WingetRow row)
    {
        if (verb is not ("install" or "upgrade" or "uninstall" or "show") || !row.Actionable)
            throw new ArgumentException("Exakte, vollstaendige Paket-ID und bekannte Aktion erforderlich.");
        var source = row.Source.Length == 0 ? "" : ValidSource(row.Source)
            ? " --source " + AdminManagement.Quote(row.Source)
            : throw new ArgumentException("Paketquelle ist unvollstaendig oder nicht eindeutig lesbar.");
        var agreements = verb is "install" or "upgrade" ? " --silent --accept-package-agreements --accept-source-agreements"
            : verb == "uninstall" ? " --accept-source-agreements" : "";
        return "$winget=Get-Command winget.exe -CommandType Application -ErrorAction Stop; " +
            $"& $winget.Source {verb} --id {AdminManagement.Quote(row.Id)} --exact{source}{agreements} --disable-interactivity; exit $LASTEXITCODE";
    }

    public static string SerializeBundle(IEnumerable<WingetRow> rows)
    {
        var bundle = new PackageBundle(1, rows.Select(r => new PackageTarget(r.Id, r.Source, r.Name, r.Version)).ToList());
        ValidateBundle(bundle);
        var json = JsonSerializer.Serialize(bundle, BundleOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxBundleBytes)
            throw new InvalidDataException("Paketliste groesser als 128 KiB.");
        return json;
    }

    public static PackageBundle ParseBundle(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBundleBytes)
            throw new InvalidDataException("Paketliste groesser als 128 KiB.");
        using (var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 }))
            RejectDuplicateProperties(document.RootElement);
        var bundle = JsonSerializer.Deserialize<PackageBundle>(json, BundleOptions)
            ?? throw new InvalidDataException("Paketliste fehlt.");
        ValidateBundle(bundle);
        return bundle;
    }

    public static PackageBundle ReadBundle(string path)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        var buffer = new char[MaxBundleBytes + 1];
        var length = reader.ReadBlock(buffer, 0, buffer.Length);
        if (length > MaxBundleBytes) throw new InvalidDataException("Paketliste groesser als 128 KiB.");
        return ParseBundle(new string(buffer, 0, length));
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Doppeltes JSON-Feld: " + property.Name);
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static void ValidateBundle(PackageBundle bundle)
    {
        if (bundle.Schema != 1 || bundle.Packages is null || bundle.Packages.Count is < 1 or > MaxIds)
            throw new InvalidDataException($"AETHER-Paketliste Schema 1 mit 1..{MaxIds} Paketen erforderlich.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in bundle.Packages)
            if (item is null || item.Id is null || !ValidId(item.Id) || !ids.Add(item.Id) ||
                item.Source is null || !ValidSource(item.Source) ||
                item.Name is null || item.Name.Length > 200 || item.Name.Any(char.IsControl) ||
                item.Version is null || item.Version.Length > 100 || item.Version.Any(char.IsControl))
                throw new InvalidDataException("Paketliste enthaelt doppelte/ungueltige IDs, fehlende Quellen oder ungueltige Metadaten.");
    }
}
