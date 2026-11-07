using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SystemCockpit.Services;

public sealed record ShellbagEntry(string Name, string Kind, string KeyPath, string Slot,
    DateTime? KeyWrittenUtc, bool Decoded)
{
    public string WrittenDisplay => KeyWrittenUtc is { } utc ? utc.ToString("yyyy-MM-dd HH:mm") + " UTC" : "Nicht lesbar";
}

/// <summary>
/// Liest Windows-Shellbag-Eintraege (BagMRU) ausschliesslich lesend aus HKCU.
/// Die Namensdekodierung ist Best Effort: nicht dekodierbare Elemente werden
/// ausdruecklich als unbekannt gemeldet, niemals erfunden. Es gibt bewusst
/// keine Loesch- oder Schreibfunktion.
/// </summary>
public static class ShellbagAnalyzer
{
    public const int MaxEntries = 2000;
    private static readonly string[] Roots =
    [
        @"Software\Microsoft\Windows\Shell\BagMRU",
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\BagMRU"
    ];

    public static List<ShellbagEntry> Read()
    {
        var entries = new List<ShellbagEntry>();
        foreach (var root in Roots)
        {
            using var key = Registry.CurrentUser.OpenSubKey(root, writable: false);
            if (key is null) continue;
            Walk(key, root, "", 0, entries);
            if (entries.Count >= MaxEntries) break;
        }
        return entries;
    }

    private static void Walk(RegistryKey key, string keyPath, string parent, int depth, List<ShellbagEntry> entries)
    {
        if (depth > 32 || entries.Count >= MaxEntries) return;
        var written = LastWriteUtc(key);
        foreach (var valueName in key.GetValueNames().Where(n => int.TryParse(n, out _)).OrderBy(n => int.Parse(n)))
        {
            if (entries.Count >= MaxEntries) return;
            if (key.GetValue(valueName) is not byte[] data) continue;
            var (name, kind, decoded) = DecodeItem(data);
            var path = parent.Length == 0 ? name : parent + "\\" + name;
            entries.Add(new(path, kind, keyPath, valueName, written, decoded));
            using var child = key.OpenSubKey(valueName, writable: false);
            if (child is not null) Walk(child, keyPath + "\\" + valueName, path, depth + 1, entries);
        }
    }

    internal static (string Name, string Kind, bool Decoded) DecodeItem(byte[] data)
    {
        if (data.Length < 3) return ("(leeres Element)", "unbekannt", false);
        var type = data[2];
        if ((type & 0x70) == 0x10 && data.Length >= 20)
            return ("{" + new Guid(data.AsSpan(4, 16)) + "}", "Systemordner-GUID", true);
        if ((type & 0x70) == 0x20 && data.Length >= 6 && data[3] is >= (byte)'A' and <= (byte)'Z' && data[4] == ':')
            return ($"{(char)data[3]}:\\", "Laufwerk", true);
        if ((type & 0x70) == 0x30 && data.Length > 14)
        {
            var longName = ReadExtensionLongName(data);
            if (longName is not null) return (longName, type == 0x31 || type == 0x35 ? "Ordner" : "Datei", true);
            var shortEnd = Array.IndexOf(data, (byte)0, 14);
            if (shortEnd > 14)
            {
                var shortName = Encoding.Default.GetString(data, 14, shortEnd - 14);
                if (shortName.Length > 0 && shortName.All(c => !char.IsControl(c)))
                    return (shortName, "Kurzname (Langname nicht dekodierbar)", true);
            }
        }
        return ($"(Typ 0x{type:X2} nicht dekodiert)", "unbekannt", false);
    }

    // BEEF0004-Erweiterungsblock: UTF-16-Langname ab Blockversion 7 bei Offset 46.
    // Aeltere Versionen werden nicht geraten, sondern fallen auf den Kurznamen zurueck.
    private static string? ReadExtensionLongName(byte[] data)
    {
        for (var i = 4; i + 4 <= data.Length; i++)
        {
            if (data[i] != 0x04 || data[i + 1] != 0x00 || data[i + 2] != 0xEF || data[i + 3] != 0xBE) continue;
            var block = i - 4;
            if (block < 0 || block + 2 > data.Length) return null;
            var version = BitConverter.ToUInt16(data, block + 2);
            if (version < 7) return null;
            var nameStart = block + 46;
            if (nameStart + 2 > data.Length) return null;
            var builder = new StringBuilder();
            for (var p = nameStart; p + 1 < data.Length; p += 2)
            {
                var c = BitConverter.ToChar(data, p);
                if (c == '\0') break;
                if (char.IsControl(c)) return null;
                builder.Append(c);
                if (builder.Length > 260) return null;
            }
            return builder.Length > 0 ? builder.ToString() : null;
        }
        return null;
    }

    private static DateTime? LastWriteUtc(RegistryKey key)
    {
        var result = RegQueryInfoKey(key.Handle, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var fileTime);
        return result == 0 && fileTime > 0 ? DateTime.FromFileTimeUtc(fileTime) : null;
    }

    [DllImport("advapi32.dll")]
    private static extern int RegQueryInfoKey(Microsoft.Win32.SafeHandles.SafeRegistryHandle key, StringBuilder? classText,
        IntPtr classLength, IntPtr reserved, IntPtr subKeys, IntPtr maxSubKeyLength, IntPtr maxClassLength,
        IntPtr values, IntPtr maxValueNameLength, IntPtr maxValueLength, IntPtr securityDescriptor, out long lastWriteTime);
}
