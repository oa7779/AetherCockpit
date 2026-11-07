using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

public sealed record RegistryTweak(string Id, string Name, RegistryHive Hive, string Key, string Value,
    int OnValue, int OffValue, string Description);
public sealed record TweakState(RegistryTweak Setting, int? Value)
{
    public string Name => Setting.Name;
    public string Status => Value is null ? "Nicht gesetzt / Windows-Standard" :
        Value == Setting.OnValue ? "AN" : Value == Setting.OffValue ? "AUS" : $"Andere Konfiguration: {Value}";
    public string Detail => Setting.Description;
}
public sealed record TweakBackup(string Id, int? Before, int? Applied, int? Pending = null);

public sealed class RegistryTweaks(string backupPath)
{
    public static IReadOnlyList<RegistryTweak> Catalog { get; } =
    [
        new("extensions", "Dateiendungen anzeigen", RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 0, 1, "Explorer; neue Fenster/Explorer-Neustart erforderlich."),
        new("hidden", "Versteckte Dateien anzeigen", RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 1, 2, "Explorer; keine Berechtigungsumgehung."),
        new("system-files", "Geschuetzte Systemdateien anzeigen", RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSuperHidden", 1, 0, "Sensible Dateien werden sichtbar; Anzeige ist keine Loeschfreigabe."),
        new("taskbar", "Taskleistenanimationen", RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 1, 0, "Explorer-Neustart/Abmeldung kann erforderlich sein."),
        new("ads", "Windows-Werbe-ID deaktivieren", RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1, "Nur Werbe-ID, keine komplette Tracking-Sperre."),
        new("suggestions", "Vorgeschlagene Inhalte deaktivieren", RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0, 1, "Versionsabhaengige Windows-Vorschlaege; keine globale Netzsperre."),
        new("tailored", "Personalisierte Diagnose-Erlebnisse deaktivieren", RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1, "Nutzung von Diagnosedaten fuer personalisierte Empfehlungen."),
        new("telemetry", "Optionale Diagnosedaten begrenzen", RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1, 3, "AN = erforderliche Daten (1), AUS = optionale Daten (3). Edition/verwaltete Richtlinien bestimmen die Wirkung; nicht Telemetrie komplett AUS.")
    ];

    public List<TweakState> Read() => Catalog.Select(t => new TweakState(t, ReadValue(t))).ToList();

    public static int? ReadValue(RegistryTweak tweak)
    {
        using var hive = RegistryKey.OpenBaseKey(tweak.Hive, RegistryView.Registry64);
        using var key = hive.OpenSubKey(tweak.Key);
        if (key is null || !key.GetValueNames().Contains(tweak.Value, StringComparer.OrdinalIgnoreCase)) return null;
        if (key.GetValueKind(tweak.Value) != RegistryValueKind.DWord)
            throw new InvalidDataException($"{tweak.Name}: Unerwarteter Registry-Typ, keine automatische Konvertierung.");
        return (int)key.GetValue(tweak.Value)!;
    }

    public void Set(RegistryTweak tweak, int? expected, bool enabled)
    {
        if (!Catalog.Contains(tweak)) throw new ArgumentException("Unbekannte Einstellung.");
        SystemActions.RequireAdmin();
        if (ReadValue(tweak) != expected) throw new InvalidOperationException("Konfiguration hat sich geaendert. Status neu lesen.");
        var target = enabled ? tweak.OnValue : tweak.OffValue;
        var backups = Load();
        var old = backups.SingleOrDefault(b => b.Id == tweak.Id);
        if (old is not null && !Matches(old, expected))
            throw new InvalidOperationException("Gesicherte Einstellung extern geaendert. Wiederherstellung/Status zuerst pruefen.");
        backups.RemoveAll(b => b.Id == tweak.Id);
        backups.Add(PrepareBackup(tweak.Id, old, expected, target));
        ActionJournal.Append("registry-setting-start", new { tweak.Id, target });
        Save(backups);
        using var hive = RegistryKey.OpenBaseKey(tweak.Hive, RegistryView.Registry64);
        using var key = hive.CreateSubKey(tweak.Key, true) ?? throw new IOException("Registry-Schluessel nicht erzeugbar.");
        key.SetValue(tweak.Value, target, RegistryValueKind.DWord);
        if (ReadValue(tweak) != target) throw new IOException("Registry-Aenderung konnte nicht verifiziert werden.");
        backups[^1] = backups[^1] with { Applied = target, Pending = null };
        Save(backups);
        ActionJournal.Append("registry-setting-complete", new { tweak.Id, target });
    }

    public void Restore(RegistryTweak tweak)
    {
        if (!Catalog.Contains(tweak)) throw new ArgumentException("Unbekannte Einstellung.");
        SystemActions.RequireAdmin();
        var backups = Load();
        var backup = backups.SingleOrDefault(b => b.Id == tweak.Id) ?? throw new InvalidOperationException("Keine AETHER-Sicherung fuer diese Einstellung.");
        if (!Matches(backup, ReadValue(tweak)))
            throw new InvalidOperationException("Aktueller Wert stimmt nicht mit AETHER-Aenderung ueberein. Externe Aenderung wird nicht ueberschrieben.");
        ActionJournal.Append("registry-restore-start", new { tweak.Id });
        using var hive = RegistryKey.OpenBaseKey(tweak.Hive, RegistryView.Registry64);
        using var key = hive.OpenSubKey(tweak.Key, true);
        if (backup.Before is { } before)
        {
            if (key is null) throw new IOException("Registry-Schluessel nicht vorhanden.");
            key.SetValue(tweak.Value, before, RegistryValueKind.DWord);
        }
        else key?.DeleteValue(tweak.Value, false);
        if (ReadValue(tweak) != backup.Before) throw new IOException("Wiederherstellung nicht verifiziert.");
        backups.Remove(backup);
        Save(backups);
        ActionJournal.Append("registry-restore-complete", new { tweak.Id });
    }

    internal static bool Matches(TweakBackup backup, int? current) =>
        current == backup.Applied || backup.Pending is { } pending && current == pending;
    internal static TweakBackup PrepareBackup(string id, TweakBackup? old, int? expected, int target) =>
        new(id, old is null ? expected : old.Before, expected, target);

    private List<TweakBackup> Load()
    {
        if (!File.Exists(backupPath)) return [];
        var backups = JsonSerializer.Deserialize<List<TweakBackup>>(File.ReadAllText(backupPath)) ??
            throw new InvalidDataException("Leere Registry-Sicherung.");
        if (backups.Any(b => b is null || !Catalog.Any(t => t.Id == b.Id)) ||
            backups.Select(b => b.Id).Distinct().Count() != backups.Count)
            throw new InvalidDataException("Ungueltige oder doppelte Registry-Sicherungen; keine Aenderung.");
        return backups;
    }
    private void Save(List<TweakBackup> backups)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath + ".tmp", JsonSerializer.Serialize(backups));
        File.Move(backupPath + ".tmp", backupPath, true);
    }
}
