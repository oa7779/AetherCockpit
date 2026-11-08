using System.IO;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;

namespace SystemCockpit.Services;

public sealed record SystemAction(string Id, string Label, string Executable, string[] Arguments,
    string Warning, bool Admin, bool Destructive, TimeSpan Timeout)
{
    public override string ToString() => Label;
}

public static class SystemActions
{
    private static string Exe(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);
    private static readonly IReadOnlyList<SystemAction> Definitions =
    [
        new("sfc-verify", "", Exe("sfc.exe"), ["/verifyonly"], "", true, false, TimeSpan.FromMinutes(60)),
        new("sfc-repair", "", Exe("sfc.exe"), ["/scannow"], "", true, true, TimeSpan.FromMinutes(60)),
        new("dism-scan", "", Exe("dism.exe"), ["/Online", "/Cleanup-Image", "/ScanHealth"], "", true, false, TimeSpan.FromMinutes(60)),
        new("dism-repair", "", Exe("dism.exe"), ["/Online", "/Cleanup-Image", "/RestoreHealth"], "", true, true, TimeSpan.FromMinutes(90)),
        new("chkdsk", "", Exe("chkdsk.exe"), [Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "/scan"], "", true, false, TimeSpan.FromMinutes(90)),
        new("firmware", "", Exe("shutdown.exe"), ["/r", "/fw", "/t", "30"], "", true, true, TimeSpan.FromSeconds(15)),
        new("cancel-reboot", "", Exe("shutdown.exe"), ["/a"], "", false, true, TimeSpan.FromSeconds(15)),
        new("arp", "", Exe("netsh.exe"), ["interface", "ip", "delete", "arpcache"], "", true, true, TimeSpan.FromSeconds(30)),
        new("wlan", "", Exe("netsh.exe"), ["wlan", "show", "networks", "mode=bssid"], "", false, false, TimeSpan.FromSeconds(30)),
        new("wlan-profiles", "", Exe("netsh.exe"), ["wlan", "show", "profiles"], "", false, false, TimeSpan.FromSeconds(30)),
        new("neighbors", "", Exe("arp.exe"), ["-a"], "", false, false, TimeSpan.FromSeconds(15)),
        new("bluetooth", "", Exe("pnputil.exe"), ["/enum-devices", "/class", "Bluetooth", "/connected"], "", false, false, TimeSpan.FromSeconds(30))
    ];

    public static IReadOnlyList<SystemAction> Catalog => Definitions.Select(action => action with
    {
        Label = DesktopUiText.Get($"SystemAction.{action.Id}.Name"),
        Warning = DesktopUiText.Get($"SystemAction.{action.Id}.Warning")
    }).ToArray();

    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    public static void RequireAdmin()
    {
        if (!IsAdmin()) throw new UnauthorizedAccessException(DesktopUiText.Get("SystemAction.AdminRequired"));
    }

    public const string CertificateScript = """
        $now=Get-Date
        foreach($store in @('Cert:\LocalMachine\Root','Cert:\CurrentUser\Root','Cert:\LocalMachine\My','Cert:\CurrentUser\My')) {
            foreach($cert in Get-ChildItem $store) {
                $state=if($cert.NotAfter -lt $now){'ABGELAUFEN'}elseif($cert.NotAfter -lt $now.AddDays(30)){'BALD ABLAUFEND'}else{'DATUM OK'}
                "$store | $state | $($cert.NotAfter.ToString('yyyy-MM-dd')) | $($cert.Subject) | $($cert.Thumbprint)"
            }
        }
        'Keine pauschale Root-CA-Freigabe: Ablaufdatum ist kein vollstaendiger Vertrauensbeweis. Keine Online-Revocation-Abfrage.'
        """;
    public const string StabilityScript = """
        $events=@()
        try {
            $events=@(Get-WinEvent -FilterHashtable @{LogName='System';Id=41,18,19,1001,6008;StartTime=(Get-Date).AddDays(-30)} -MaxEvents 100)
        } catch { if($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*'){throw} }
        if($events.Count -eq 0){'Keine passenden Kernel-Power/WHEA/Bugcheck-Ereignisse in 30 Tagen.'}
        foreach($event in $events) { "$($event.TimeCreated) | $($event.ProviderName) | ID $($event.Id)`n$($event.Message)`n" }
        """;

    public static string SecretScript(string kind, string profile)
    {
        return kind switch
        {
            "bitlocker" => """
                $found=$false
                foreach($volume in Get-BitLockerVolume) {
                    foreach($protector in $volume.KeyProtector) {
                        if($protector.KeyProtectorType -eq 'RecoveryPassword') {
                            $found=$true
                            "$($volume.MountPoint) | $($protector.KeyProtectorId) | $($protector.RecoveryPassword)"
                        }
                    }
                }
                if(-not $found){throw 'Keine RecoveryPassword-Protektoren gemeldet. Keine Schluessel werden erfunden.'}
                """,
            "windows-key" => """
                $key=(Get-CimInstance SoftwareLicensingService).OA3xOriginalProductKey
                if([string]::IsNullOrWhiteSpace($key)){throw 'Kein OEM-Firmware-Produktschluessel vorhanden. Digitale Lizenz wird nicht als Schluessel erfunden.'}
                $key
                """,
            "wifi" => string.IsNullOrWhiteSpace(profile) || profile.Length > 128
                ? throw new ArgumentException("Gespeicherten WLAN-Profilnamen eingeben.")
                : $"& \"$env:SystemRoot\\System32\\netsh.exe\" wlan show profile name='{profile.Replace("'", "''")}' key=clear; if($LASTEXITCODE -ne 0){{throw 'WLAN-Profil konnte nicht gelesen werden.'}}",
            _ => throw new ArgumentException("Unbekannte Schluesselabfrage.")
        };
    }
}

public sealed record StartupEntry(string Hive, string Name, string Command, bool Enabled)
{
    public override string ToString() => $"{(Enabled ? "AN" : "AUS")} | {Hive} | {Name}";
}

public sealed class StartupManager(string backupDirectory)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private string BackupPath => Path.Combine(backupDirectory, "startup-backups.json");

    private List<StartupEntry> Backups() => !File.Exists(BackupPath) ? []
        : JsonSerializer.Deserialize<List<StartupEntry>>(File.ReadAllText(BackupPath))
            ?? throw new InvalidDataException("Ungueltige Autostart-Sicherung.");

    public List<StartupEntry> Read()
    {
        var entries = new List<StartupEntry>();
        foreach (var hive in new[] { "HKCU", "HKLM" })
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(RunKey);
            if (key is null) continue;
            foreach (var name in key.GetValueNames())
                if (key.GetValueKind(name) == RegistryValueKind.String && key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string command)
                    entries.Add(new(hive, name, command, true));
        }
        entries.AddRange(Backups().Select(e => e with { Enabled = false }));
        return entries;
    }

    public void SetEnabled(StartupEntry entry, bool enabled)
    {
        if (entry.Hive is not ("HKCU" or "HKLM") || entry.Name.Length == 0)
            throw new ArgumentException("Unbekannter Autostarteintrag.");
        if (entry.Hive == "HKLM") SystemActions.RequireAdmin();
        var backups = Backups();
        using var baseKey = RegistryKey.OpenBaseKey(entry.Hive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(RunKey, writable: true)
            ?? throw new IOException("Autostart-Registrierung nicht vorhanden.");
        if (!enabled)
        {
            if (!entry.Enabled || key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string current ||
                current != entry.Command || key.GetValueKind(entry.Name) != RegistryValueKind.String)
                throw new IOException("Autostarteintrag wurde geaendert. Neu einlesen.");
            if (backups.Any(b => b.Hive == entry.Hive && b.Name == entry.Name)) throw new IOException("Sicherung existiert bereits. Nicht ueberschreiben.");
            backups.Add(entry with { Enabled = false });
            Save(backups);
            key.DeleteValue(entry.Name, throwOnMissingValue: true);
        }
        else
        {
            var backup = backups.SingleOrDefault(b => b.Hive == entry.Hive && b.Name == entry.Name && b.Command == entry.Command)
                ?? throw new IOException("Keine passende lokale Sicherung.");
            if (key.GetValueNames().Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                throw new IOException("Aktiver Autostarteintrag existiert. Konflikt nicht ueberschreiben.");
            key.SetValue(entry.Name, backup.Command, RegistryValueKind.String);
            backups.Remove(backup);
            Save(backups);
        }
    }
    private void Save(List<StartupEntry> entries)
    {
        Directory.CreateDirectory(backupDirectory);
        var temporary = BackupPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries));
        File.Move(temporary, BackupPath, true);
    }
}
