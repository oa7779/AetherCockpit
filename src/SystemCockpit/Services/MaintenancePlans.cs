using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

public sealed class MaintenanceChoice(MaintenanceAction action) : Observable
{
    public MaintenanceAction Action { get; } = action;
    public string Name => Action.Name;
    public string Kind => DesktopUiText.Get(Action.Changes ? "Plan.Change" : "Plan.Diagnostic");
    public string Description => Action.Description;
    private bool _selected;
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
}
public sealed record MaintenanceAction(string Id, string Name, bool Changes, string Description, string Script);
public sealed record AvailableUpdate(string Id, string Title, double SizeMiB, string EulaText);
public sealed class UpdateChoice(AvailableUpdate update) : Observable
{
    public AvailableUpdate Update { get; } = update;
    public string Title => Update.Title;
    public string Id => Update.Id;
    public string Size => $"{Update.SizeMiB:0.0} MiB";
    private bool _selected;
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
}
public sealed record MaintenanceResult(string Action, string State, string Output, DateTimeOffset Started, DateTimeOffset Finished);

public static class MaintenancePlans
{
    public const string RestorePointScript = """
        $ErrorActionPreference='Stop'
        $description='AETHER-'+[guid]::NewGuid().ToString('N')
        Checkpoint-Computer -Description $description -RestorePointType MODIFY_SETTINGS
        if(!(Get-ComputerRestorePoint | Where-Object Description -eq $description)){throw 'Wiederherstellungspunkt nicht bestaetigt; Lauf vor Aenderungen gestoppt'}
        """;
    private static string Native(string exe, string arguments) =>
        (exe == "sfc.exe" ? "sfc" : $"& \"$env:WINDIR\\System32\\{exe}\"") +
        $" {arguments}; if($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 3010){{throw \"{exe} Exit $LASTEXITCODE\"}}; if($LASTEXITCODE -eq 3010){{'NEUSTART ERFORDERLICH / nicht automatisch ausgefuehrt'}}; $global:LASTEXITCODE=0";
    public static IReadOnlyList<MaintenanceAction> Catalog => Definitions.Select(action => action with
    {
        Name = DesktopUiText.Get("Maintenance." + action.Id + ".Name"),
        Description = DesktopUiText.Get("Maintenance." + action.Id + ".Description")
    }).ToArray();

    private static IReadOnlyList<MaintenanceAction> Definitions { get; } =
    [
        new("inventory", "System- und Sicherheitsinventar", false, "Hardware/Windows/Defender lesen; keine Schutzabschaltung.",
            """
            Get-CimInstance Win32_ComputerSystem | Format-List Manufacturer,Model,TotalPhysicalMemory
            Get-CimInstance Win32_OperatingSystem | Format-List Caption,Version,LastBootUpTime
            Get-CimInstance Win32_Processor | Format-List Name,NumberOfCores,NumberOfLogicalProcessors
            Get-Volume | Format-Table DriveLetter,FileSystem,HealthStatus,Size,SizeRemaining
            Get-PhysicalDisk | Format-Table FriendlyName,MediaType,HealthStatus,OperationalStatus
            Get-MpComputerStatus | Format-List AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,AntivirusSignatureLastUpdated
            foreach($query in @(
                @{Name='TPM';Script={Get-Tpm | Format-List TpmPresent,TpmReady}},
                @{Name='Secure Boot';Script={Confirm-SecureBootUEFI}},
                @{Name='BitLocker';Script={Get-BitLockerVolume | Format-Table MountPoint,VolumeStatus,ProtectionStatus}}
            )){
                try { $query.Name; & $query.Script } catch { "WARN / $($query.Name) nicht lesbar: $($_.Exception.Message)" }
            }
            $pending=(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
            "Neustart-Richtlinienindikator: $pending / kein vollstaendiger Neustartnachweis / kein automatischer Neustart"
            """),
        new("events", "Fehler / Warnungen lesen", false, "Bis 500 System-/Application-Ereignisse je Kanal, sieben Tage.",
            """
            foreach($log in @('System','Application')){
                $errors=@(); $events=@(Get-WinEvent -FilterHashtable @{LogName=$log;StartTime=(Get-Date).AddDays(-7);Level=1,2,3} -MaxEvents 500 -ErrorAction SilentlyContinue -ErrorVariable errors)
                $unexpected=@($errors | Where-Object FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*')
                if($unexpected.Count){throw ($unexpected | Out-String)}
                $events | Format-List TimeCreated,Id,ProviderName,Message
                "KANAL $log / $($events.Count) Treffer / begrenzter Snapshot"
            }
            """),
        new("startup", "Autostart / Dienste / Aufgaben", false, "Inventar; keine Eintraege ausschalten.",
            "Get-CimInstance Win32_StartupCommand | Format-List Name,Command,Location; Get-CimInstance Win32_Service | Where-Object StartMode -eq 'Auto' | Format-Table Name,State,StartMode; Get-ScheduledTask | Format-Table TaskPath,TaskName,State"),
        new("componentAnalysis", "Komponentenspeicher analysieren", false, "DISM-Analyse; kann eigene Windows-Logs schreiben.", Native("dism.exe", "/Online /Cleanup-Image /AnalyzeComponentStore")),
        new("diskScan", "NTFS-Systemvolume online pruefen", true, "CHKDSK /scan kann Onlinekorrekturen ausloesen; kein /r oder Offline-Neustart.",
            "if((Get-Volume -DriveLetter $env:SystemDrive.Substring(0,1)).FileSystem -ne 'NTFS'){throw 'Systemvolume ist nicht NTFS'}; " + Native("chkdsk.exe", "$env:SystemDrive /scan")),
        new("repairImage", "Windows-Abbild reparieren", true, "DISM RestoreHealth; Downloads moeglich; nicht gewaltsam abbrechen.", Native("dism.exe", "/Online /Cleanup-Image /RestoreHealth")),
        new("sfc", "Systemdateien pruefen / reparieren", true, "SFC-Befund bleibt PRUEFEN; Exit 0 ist kein Gesundheitssiegel.", Native("sfc.exe", "/scannow") + "; 'PRUEFEN / Textbefund und CBS.log lesen'"),
        new("cleanup", "Temp-Bereinigung mit Dateivorschau", true, "Oeffnet den AETHER-Cleaner. Dateien einzeln ankreuzen; keine unbeaufsichtigte Loeschung.", ""),
        new("componentCleanup", "Komponentenspeicher bereinigen", true, "Kein ResetBase; keine einfache Ruecknahme.", Native("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup")),
        new("optimize", "Lokale Laufwerke mediengerecht optimieren", true, "Windows defrag /O entscheidet SSD-TRIM/HDD-Optimierung; kein SSD-Zwangsdefrag.",
            "$volumes=@(Get-Volume | Where-Object {$_.DriveType -eq 'Fixed' -and $_.DriveLetter -and $_.FileSystem -in @('NTFS','ReFS')}); if(!$volumes.Count){throw 'Keine geeigneten Volumes'}; foreach($v in $volumes){ " + Native("defrag.exe", "($v.DriveLetter+':') /O /U /V") + " }"),
        new("defenderUpdate", "Defender-Signaturen aktualisieren", true, "Downloads; kein deaktivierter Defender wird automatisch aktiviert.", "Update-MpSignature"),
        new("defenderQuick", "Defender-Schnellscan", true, "Windows kann Bedrohungen gemaess bestehender Richtlinie behandeln.", DefenderScan("Quick")),
        new("defenderFull", "Defender-Vollscan", true, "Kann Stunden dauern; bestehende Defender-Richtlinie gilt.", DefenderScan("Full")),
        new("windowsSearch", "Windows-Updates suchen", false, "Microsoft/WSUS-Quelle laut Windows-Richtlinie; keine Treiberupdates.", "UPDATEAUSWAHL"),
        new("windowsInstall", "Angekreuzte Windows-Updates installieren", true, "Explizite Update-ID + Revision, EULA vor Installation bestaetigen; kein automatischer Neustart.", "UPDATEINSTALLATION"),
        new("wingetList", "App-Updateangebot anzeigen", false, "Kein --all / --force; Paket-IDs aus Ausgabe verwenden.", "& winget.exe upgrade --disable-interactivity; if($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189){throw \"WinGet Exit $LASTEXITCODE\"}; if($LASTEXITCODE -eq -1978335189){'KEINE PASSENDEN UPGRADES'}; $global:LASTEXITCODE=0"),
        new("wingetInstall", "Exakte App-IDs aktualisieren", true, "Eine ID je Zeile; Quell-/Paketlizenzen werden durch Bestaetigung akzeptiert; Fremdinstaller koennen UI zeigen.", "APPINSTALLATION")
    ];
    public static IReadOnlyList<string> Template(string name) => name switch
    {
        "Nur Diagnose" => ["inventory", "events", "startup", "componentAnalysis"],
        "Windows reparieren" => ["inventory", "repairImage", "sfc"],
        "Updates vorbereiten" => ["inventory", "windowsSearch", "wingetList"],
        "Sicherheitswartung" => ["inventory", "defenderUpdate", "defenderQuick"],
        _ => throw new ArgumentException("Unbekannte Wartungsvorlage.")
    };
    public static string Build(MaintenanceAction action, IReadOnlyList<AvailableUpdate> updates, string packages)
    {
        if (!Catalog.Contains(action)) throw new ArgumentException("Unbekannte Wartungsaktion.");
        if (action.Id == "cleanup") throw new ArgumentException("Cleaner nur ueber explizite Dateivorschau.");
        if (action.Id == "windowsSearch") return SearchUpdatesScript;
        if (action.Id == "windowsInstall")
        {
            if (updates.Count == 0 || updates.Any(u => !ValidUpdateId(u.Id))) throw new ArgumentException("Windows-Updates erst suchen und ankreuzen.");
            var ids = string.Join(",", updates.Select(u => AdminManagement.Quote(u.Id)));
            return """
                $session=New-Object -ComObject Microsoft.Update.Session
                $search=$session.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0 and Type='Software'")
                if($search.ResultCode -ne 2){throw 'Windows-Updatesuche unvollstaendig'}
                $selection=New-Object -ComObject Microsoft.Update.UpdateColl
                """ + $"\nforeach($id in @({ids})){{" + """
                $matches=@($search.Updates | Where-Object {($_.Identity.UpdateID+':'+$_.Identity.RevisionNumber) -eq $id})
                if($matches.Count -ne 1){throw "Update nicht mehr eindeutig verfuegbar: $id"}
                $update=$matches[0]
                """ + "\n$licenses=" + AdminManagement.Quote(JsonSerializer.Serialize(updates.ToDictionary(u => u.Id, u => u.EulaText))) + """ 
                 | ConvertFrom-Json
                if($update.EulaText -cne $licenses.PSObject.Properties[$id].Value){throw 'Update-Lizenz seit Vorschau geaendert'}
                if(!$update.EulaAccepted){$update.AcceptEula()}
                [void]$selection.Add($update)
                }
                $downloader=$session.CreateUpdateDownloader(); $downloader.Updates=$selection
                $download=$downloader.Download()
                if($download.ResultCode -ne 2){throw 'Update-Download nicht vollstaendig erfolgreich'}
                for($i=0;$i -lt $selection.Count;$i++){if($download.GetUpdateResult($i).ResultCode -ne 2){throw 'Einzelupdate-Download fehlgeschlagen'}}
                $installer=$session.CreateUpdateInstaller(); $installer.Updates=$selection
                $installation=$installer.Install()
                if($installation.ResultCode -ne 2){throw 'Installation nicht vollstaendig erfolgreich; Teilaenderungen moeglich'}
                for($i=0;$i -lt $selection.Count;$i++){if($installation.GetUpdateResult($i).ResultCode -ne 2){throw 'Einzelupdate-Installation fehlgeschlagen'}}
                "Updates installiert / Neustart erforderlich: $($installation.RebootRequired) / KEIN automatischer Neustart"
                """;
        }
        if (action.Id == "wingetInstall")
        {
            var ids = packages.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
            if (ids.Length is < 1 or > 100 || ids.Any(id => !System.Text.RegularExpressions.Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9_.+-]{0,199}$")))
                throw new ArgumentException("1..100 exakte WinGet-IDs, eine je Zeile; keine Optionen oder freien Befehle.");
            return string.Join("\n", ids.Select(id => $"& winget.exe upgrade --id {AdminManagement.Quote(id)} --exact --silent --accept-source-agreements --accept-package-agreements --disable-interactivity; if($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189){{throw \"WinGet Exit $LASTEXITCODE\"}}; if($LASTEXITCODE -eq -1978335189){{'KEIN PASSENDES UPGRADE / ID: {id}'}}; $global:LASTEXITCODE=0"));
        }
        return action.Script;
    }
    internal static bool ValidUpdateId(string id)
    {
        var parts = id.Split(':');
        return parts.Length == 2 && Guid.TryParse(parts[0], out _) && int.TryParse(parts[1], out var revision) && revision >= 0;
    }
    public const string SearchUpdatesScript = """
        $session=New-Object -ComObject Microsoft.Update.Session
        $result=$session.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0 and Type='Software'")
        if($result.ResultCode -ne 2){throw 'Windows-Updatesuche unvollstaendig'}
        $updates=@($result.Updates | ForEach-Object { @{Id=($_.Identity.UpdateID+':'+$_.Identity.RevisionNumber);Title=$_.Title;SizeMiB=[math]::Round($_.MaxDownloadSize/1MB,1);EulaText=[string]$_.EulaText} })
        ConvertTo-Json -InputObject @($updates) -Compress -Depth 4
        """;
    private static string DefenderScan(string type) => $$"""
        $before=Get-MpComputerStatus
        if(!$before.AMServiceEnabled -or !$before.AntivirusEnabled){throw 'Defender nicht aktiv; keine Schutzkonfiguration geaendert'}
        $requested=Get-Date
        Start-MpScan -ScanType {{type}}Scan
        $after=Get-MpComputerStatus
        if($null -eq $after.{{type}}ScanEndTime -or $after.{{type}}ScanEndTime -lt $requested){throw 'Angeforderter Scanabschluss nicht bestaetigt'}
        if(@(Get-MpThreat | Where-Object IsActive).Count){throw 'Aktive Bedrohungen gemeldet; Windows-Sicherheit pruefen'}
        "Scanabschluss bestaetigt / $($after.{{type}}ScanEndTime)"
        """;
}
