namespace AetherOperations;

public sealed record ManagedAction(string Id, string Name, string Category, bool ChangesSystem,
    bool CanAutoRepair, string Script, string[] RequiredCommands);

public static class ManagedActionCatalog
{
    private static readonly ManagedAction[] Actions =
    [
        Read("inventory", "Strukturiertes Windows-Inventar", "Inventar", InventoryScript,
            "Get-CimInstance", "Get-PnpDevice", "Get-NetAdapter", "Get-WinEvent", "Get-Service"),
        Read("dns-health", "DNS: Standardresolver mit konfigurierten Servern vergleichen", "Netzwerk", DnsHealthScript,
            "Resolve-DnsName", "Get-NetIPConfiguration"),
        new("dns-cache-clear", "DNS-Clientcache leeren", "Netzwerk", true, true,
            "Clear-DnsClientCache -ErrorAction Stop; 'DNS-Cache geleert; erneute Diagnose erforderlich.'", ["Clear-DnsClientCache"]),
        Read("sfc-verify", "SFC /verifyonly – Systemdateien prüfen", "Windows", Native("sfc.exe", "/verifyonly"), "sfc.exe"),
        Read("dism-check", "DISM /CheckHealth – gespeicherten Befund prüfen", "Windows", Native("dism.exe", "/Online /Cleanup-Image /CheckHealth"), "dism.exe"),
        Read("dism-scan", "DISM /ScanHealth – Komponentenspeicher prüfen", "Windows", Native("dism.exe", "/Online /Cleanup-Image /ScanHealth"), "dism.exe"),
        Change("dism-repair", "DISM /RestoreHealth – Komponenten reparieren", "Windows",
            Native("dism.exe", "/Online /Cleanup-Image /RestoreHealth"), "dism.exe"),
        Change("sfc-repair", "SFC /scannow – Systemdateien reparieren", "Windows", Native("sfc.exe", "/scannow"), "sfc.exe"),
        Read("windows-update-search", "Windows Update: angebotene Software/Treiber lesen", "Updates", UpdateSearchScript, "New-Object"),
        Change("windows-update-install", "Ein exakt geprüftes Windows-/Treiberupdate installieren", "Updates",
            "throw 'Typed WindowsUpdateSelection required; use ManagedActionRunner.RunUpdateAsync.'", "New-Object"),
        Read("pnp-inventory", "PnP-Geräte und Problemcodes", "Treiber",
            "Get-PnpDevice -PresentOnly -ErrorAction Stop | Select-Object Status,Class,FriendlyName,Problem,InstanceId | ConvertTo-Json -Depth 4", "Get-PnpDevice"),
        Read("driver-versions", "Installierte signierte Treiber und Versionen", "Treiber",
            "Get-CimInstance Win32_PnPSignedDriver | Select-Object DeviceName,DeviceID,DriverVersion,DriverDate,InfName,IsSigned,Manufacturer | ConvertTo-Json -Depth 4", "Get-CimInstance"),
        Read("pnputil-drivers", "PnPUtil: Treiberpakete aufzählen", "Treiber", Native("pnputil.exe", "/enum-drivers"), "pnputil.exe"),
        Read("storage-status", "Datenträger und Volumes lesen", "Speicher",
            "Get-Disk | Select-Object Number,FriendlyName,OperationalStatus,HealthStatus,PartitionStyle,Size | Format-Table; Get-Volume | Select-Object DriveLetter,FileSystem,HealthStatus,Size,SizeRemaining | Format-Table", "Get-Disk", "Get-Volume"),
        Read("network-status", "Adapter, IP, DNS und Routen lesen", "Netzwerk",
            "Get-NetAdapter -IncludeHidden | Format-Table Name,Status,LinkSpeed; Get-NetIPConfiguration | Format-List; Get-NetRoute | Select-Object -First 200 InterfaceAlias,DestinationPrefix,NextHop,RouteMetric | Format-Table", "Get-NetAdapter", "Get-NetIPConfiguration", "Get-NetRoute"),
        Read("event-errors", "System-/Anwendungsfehler: begrenzter Verlauf", "Ereignisse",
            "foreach($log in @('System','Application')) { try { Get-WinEvent -FilterHashtable @{LogName=$log;Level=1,2,3;StartTime=(Get-Date).AddDays(-7)} -MaxEvents 100 -ErrorAction Stop | Select-Object TimeCreated,Id,ProviderName,LevelDisplayName,Message | Format-List } catch { if($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*'){\"$log : keine passenden Ereignisse\"}else{throw} } }", "Get-WinEvent"),
        Read("defender-status", "Defender-Schutzstatus – keine Änderungen", "Sicherheit",
            "Get-MpComputerStatus | Select-Object AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,AntivirusSignatureVersion,AntivirusSignatureLastUpdated,QuickScanEndTime,FullScanEndTime | Format-List", "Get-MpComputerStatus"),
        Read("bitlocker-status", "BitLocker-Status – keine Wiederherstellungsschlüssel", "Sicherheit",
            "Get-BitLockerVolume | Select-Object MountPoint,VolumeStatus,ProtectionStatus,EncryptionPercentage,EncryptionMethod,LockStatus | Format-Table", "Get-BitLockerVolume"),
        Read("boot-config", "Bootkonfiguration lesen – keine Bearbeitung", "Start", Native("bcdedit.exe", "/enum '{current}'"), "bcdedit.exe"),
        Read("power-status", "Energieschema und verfügbare Schlafzustände", "Energie",
            Native("powercfg.exe", "/getactivescheme") + "\n" + Native("powercfg.exe", "/a"), "powercfg.exe"),
        Read("performance-snapshot", "Prozesse und vorhandener WinSAT-Befund – kein Benchmark", "Leistung",
            "Get-Process | Sort-Object CPU -Descending | Select-Object -First 40 Id,ProcessName,CPU,WorkingSet64 | Format-Table; Get-CimInstance Win32_WinSAT | Select-Object WinSATAssessmentState,CPUScore,MemoryScore,GraphicsScore,DiskScore | Format-List; 'WinSAT-Werte sind gespeicherte Befunde; Status prüfen, keine neue Messung.'", "Get-Process", "Get-CimInstance"),
        Read("etw-sessions", "ETW-Sitzungen und Provider aufzählen – kein Capture", "Analyse",
            Native("logman.exe", "query -ets") + "\n" + Native("logman.exe", "query providers"), "logman.exe"),
        Read("services-status", "Dienste und Startmodi lesen", "Windows",
            "Get-CimInstance Win32_Service | Select-Object Name,State,StartMode,StartName | ConvertTo-Json -Depth 3", "Get-CimInstance"),
        Read("scheduled-tasks", "Geplante Aufgaben lesen", "Start",
            "Get-ScheduledTask | Select-Object TaskName,TaskPath,State | Format-Table", "Get-ScheduledTask"),
        Read("firewall-status", "Firewallprofile und aktivierte Regeln lesen", "Sicherheit",
            "Get-NetFirewallProfile | Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction | Format-Table; Get-NetFirewallRule -Enabled True | Select-Object -First 200 DisplayName,Direction,Action,Profile | Format-Table", "Get-NetFirewallProfile", "Get-NetFirewallRule")
    ];

    // Return defensive snapshots: mutable record arrays must never alter executable definitions.
    public static IReadOnlyList<ManagedAction> All => Array.AsReadOnly(Actions.Select(Clone).ToArray());
    public static ManagedAction Get(string id) =>
        Clone(Actions.SingleOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException("Unknown managed action.", nameof(id)));
    private static ManagedAction Clone(ManagedAction action) => action with { RequiredCommands = [.. action.RequiredCommands] };
    private static ManagedAction Read(string id, string name, string category, string script, params string[] commands) =>
        new(id, name, category, false, false, script, commands);
    private static ManagedAction Change(string id, string name, string category, string script, params string[] commands) =>
        new(id, name, category, true, false, script, commands);
    private static string Native(string executable, string arguments)
    {
        var invoke = $"& \"$env:SystemRoot\\System32\\{executable}\" {arguments}";
        if (executable == "sfc.exe")
            invoke = "$previous=[Console]::OutputEncoding; try { [Console]::OutputEncoding=[Text.Encoding]::Unicode; " +
                invoke + " | ForEach-Object { [Console]::OutputEncoding=$previous; [Console]::WriteLine($_); [Console]::OutputEncoding=[Text.Encoding]::Unicode } " +
                "} finally { [Console]::OutputEncoding=$previous }";
        return invoke + $"; if($LASTEXITCODE -ne 0){{throw ('{executable} failed: ' + $LASTEXITCODE)}}";
    }

    private const string UpdateSearchScript = """
        $session=New-Object -ComObject Microsoft.Update.Session
        $session.ClientApplicationID='AETHER read-only update search'
        $searcher=$session.CreateUpdateSearcher()
        $searcher.ServerSelection=0
        $result=$searcher.Search("IsInstalled=0 and IsHidden=0")
        if($result.ResultCode -ne 2){throw "Update search incomplete: $($result.ResultCode)"}
        $offers=@($result.Updates | ForEach-Object {
            $text=[string]$_.EulaText
            $sha=[Security.Cryptography.SHA256]::Create()
            try {$hash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-','')}finally{$sha.Dispose()}
            $manufacturer=$null; $model=$null; $driverDate=$null; $hardwareId=$null
            if($_.Type -eq 2){$manufacturer=$_.DriverManufacturer;$model=$_.DriverModel;$driverDate=$_.DriverVerDate;$hardwareId=$_.DriverHardwareID}
            [pscustomobject]@{updateId=$_.Identity.UpdateID;revision=$_.Identity.RevisionNumber;title=$_.Title;eulaText=$text;eulaHash=$hash;type=$_.Type;driverManufacturer=$manufacturer;driverModel=$model;driverDate=$driverDate;driverHardwareId=$hardwareId;downloaded=$_.IsDownloaded;eulaAccepted=$_.EulaAccepted;rebootBehavior=$_.InstallationBehavior.RebootBehavior;canRequestUserInput=$_.InstallationBehavior.CanRequestUserInput}
        })
        [pscustomobject]@{updates=$offers;source='Configured Windows Update/WSUS';serverSelection=0;resultCode=[int]$result.ResultCode;installedByAether=$false;rebootRequested=$false} | ConvertTo-Json -Depth 6 -Compress
        """;

    private const string DnsHealthScript = """
        $name='www.microsoft.com'
        $defaultSucceeded=$false
        $defaultError=$null
        try { $answers=@(Resolve-DnsName -Name $name -DnsOnly -QuickTimeout -ErrorAction Stop); $defaultSucceeded=@($answers | Where-Object {$_.IPAddress}).Count -gt 0 } catch { $defaultError=$_.Exception.Message }
        $configs=@(Get-NetIPConfiguration -ErrorAction Stop | Where-Object {$_.NetAdapter.Status -eq 'Up' -and ($_.IPv4DefaultGateway -or $_.IPv6DefaultGateway)})
        $servers=@($configs | ForEach-Object {$_.DNSServer.ServerAddresses} | Where-Object {$_} | Sort-Object -Unique)
        $directSucceeded=$false
        $results=@(foreach($server in $servers){
            $ok=$false; $failure=$null
            try { $answers=@(Resolve-DnsName -Name $name -Server $server -DnsOnly -QuickTimeout -ErrorAction Stop); $ok=@($answers | Where-Object {$_.IPAddress}).Count -gt 0 } catch { $failure=$_.Exception.Message }
            if($ok){$directSucceeded=$true}
            [pscustomobject]@{server=$server;succeeded=$ok;error=$failure}
        })
        [pscustomobject]@{name=$name;defaultSucceeded=$defaultSucceeded;defaultError=$defaultError;directSucceeded=$directSucceeded;defaultResolverFailed=(!$defaultSucceeded);configuredResolverSucceeded=$directSucceeded;evidence=[pscustomobject]@{defaultResolverFailed=(!$defaultSucceeded);configuredResolverSucceeded=$directSucceeded;configuredServers=$results};needsRepair=(!$defaultSucceeded -and $directSucceeded);note='Disconnected adapters alone never imply a cache fault.'} | ConvertTo-Json -Depth 5 -Compress
        """;

    private const string InventoryScript = """
        $errors=[Collections.Generic.List[string]]::new()
        function Snapshot([string]$label,[scriptblock]$read){
            try { & $read } catch { $errors.Add($label + ': ' + $_.Exception.Message); $null }
        }
        $os=Snapshot 'os' { Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,LastBootUpTime,OSArchitecture }
        $pnp=@(Snapshot 'pnp' { Get-PnpDevice -PresentOnly | Select-Object -First 500 Status,Class,FriendlyName,Problem,InstanceId })
        $network=@(Snapshot 'network' { Get-NetAdapter -IncludeHidden | Select-Object -First 100 Name,InterfaceDescription,Status,LinkSpeed,MacAddress })
        $ip=@(Snapshot 'ip' { Get-NetIPConfiguration | Select-Object InterfaceAlias,IPv4Address,IPv6Address,IPv4DefaultGateway,IPv6DefaultGateway,DNSServer })
        $drivers=@(Snapshot 'drivers' { Get-CimInstance Win32_PnPSignedDriver | Select-Object -First 500 DeviceName,DriverVersion,DriverDate,IsSigned,InfName,DeviceID })
        $apps=@(Snapshot 'apps' { foreach($path in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')){ Get-ItemProperty $path -ErrorAction SilentlyContinue | Where-Object DisplayName | Select-Object DisplayName,DisplayVersion,Publisher } })
        $events=@(foreach($log in @('System','Application')){
            Snapshot ('events:'+$log) { try { Get-WinEvent -FilterHashtable @{LogName=$log;Level=1,2,3;StartTime=(Get-Date).AddDays(-7)} -MaxEvents 50 -ErrorAction Stop | Select-Object LogName,TimeCreated,Id,Level,ProviderName,Message } catch { if($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*'){throw} } }
        })
        $processes=@(Snapshot 'processes' { Get-Process | Select-Object -First 300 Id,ProcessName,CPU,WorkingSet64 })
        $services=@(Snapshot 'services' { Get-CimInstance Win32_Service | Select-Object -First 500 Name,State,StartMode })
        $sessionKey=Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager'
        $reboot=[pscustomobject]@{componentServicing=(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending');windowsUpdate=(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired');pendingFileRename=($null -ne $sessionKey.PendingFileRenameOperations)}
        $update=Snapshot 'update' { [pscustomobject]@{services=@(Get-Service wuauserv,bits,cryptsvc,TrustedInstaller | Select-Object Name,Status,StartType);lastResults=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install' -ErrorAction SilentlyContinue | Select-Object LastSuccessTime,LastError} }
        [pscustomobject]@{schemaVersion=1;collectedAt=(Get-Date).ToUniversalTime().ToString('o');machineName=$env:COMPUTERNAME;os=$os;build=$os.BuildNumber;pnp=$pnp;network=$network;ip=$ip;drivers=$drivers;apps=@($apps | Select-Object -First 500);events=$events;processes=$processes;services=$services;reboot=$reboot;update=$update;errors=@($errors);bounded=$true} | ConvertTo-Json -Depth 8 -Compress
        """;
}
