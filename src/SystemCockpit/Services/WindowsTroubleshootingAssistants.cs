namespace SystemCockpit.Services;

public sealed record WindowsTroubleshootingAssistant(string Id, string Name, string Scope, string Script, string NextSteps);

public static class WindowsTroubleshootingAssistants
{
    public static IReadOnlyList<WindowsTroubleshootingAssistant> All { get; } =
    [
        new("system", "Windows-Dateien und Komponenten", "DISM-Komponentenspeicher und Windows-Systemdateien / nur pruefen",
            """
            $ErrorActionPreference='Stop'
            "=== WINDOWS ==="
            Get-CimInstance Win32_OperatingSystem | Format-List Caption,Version,BuildNumber,LastBootUpTime
            "=== DISM / CHECKHEALTH / KEINE REPARATUR ==="
            & "$env:WINDIR\System32\dism.exe" /Online /Cleanup-Image /CheckHealth
            if($LASTEXITCODE -ne 0){throw "DISM CheckHealth Exit $LASTEXITCODE"}
            "=== SFC / VERIFYONLY / KEINE REPARATUR ==="
            & "$env:WINDIR\System32\sfc.exe" /verifyonly
            if($LASTEXITCODE -ne 0){throw "SFC VerifyOnly Exit $LASTEXITCODE"}
            "AUSWERTUNG: SFC-/CBS-Text und DISM-Befund lesen; Prozessende allein ist kein Erfolg."
            """,
            "Lies DISM- und SFC-Endmeldungen. Bei gemeldeter Beschaedigung im Wartungsplan erst DISM /RestoreHealth, danach SFC /scannow als getrennte, jeweils bestaetigte Schritte auswaehlen. Kein Schritt startet automatisch."),
        new("update", "Windows Update und Neustartstatus", "Update-Dienste, letzte Windows-Update-Ereignisse und Neustartindikatoren / lokal",
            """
            $ErrorActionPreference='Stop'
            "=== UPDATE-DIENSTE / NUR LESEN ==="
            Get-Service wuauserv,bits,cryptsvc,TrustedInstaller | Format-Table Name,Status,StartType
            "=== AUSSTEHENDER NEUSTART / INDIKATOREN ==="
            [pscustomobject]@{
                ComponentServicing=(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending')
                WindowsUpdate=(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
                PendingFileRename=(Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\PendingFileRenameOperations')
            } | Format-List
            "=== WINDOWS UPDATE EREIGNISSE / LETZTE 7 TAGE ==="
            $errors=@()
            $events=@(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-WindowsUpdateClient/Operational';StartTime=(Get-Date).AddDays(-7);Level=1,2,3} -MaxEvents 100 -ErrorAction SilentlyContinue -ErrorVariable errors)
            $unexpected=@($errors | Where-Object FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*')
            if($unexpected.Count){throw ($unexpected | Out-String)}
            $events | Format-List TimeCreated,Id,LevelDisplayName,Message
            "TREFFER: $($events.Count) / begrenzter Snapshot, kein vollstaendiger Updateverlauf."
            """,
            "Pruefe Dienstestatus, Neustartindikatoren und konkrete Update-Ereignisse. Ein gestoppter Dienst ist nicht automatisch fehlerhaft. Installiere oder starte nichts automatisch; nutze bei Bedarf den Wartungsplan fuer eine erneute Suche."),
        new("network", "Netzwerk und Internetverbindung", "Lokale Adapter, IP-/DNS-Konfiguration, Standardgateway und DNS-Aufloesung",
            """
            $ErrorActionPreference='Stop'
            "=== ADAPTER / NUR LESEN ==="
            Get-NetAdapter -IncludeHidden | Format-Table Name,InterfaceDescription,Status,LinkSpeed
            "=== IP- UND DNS-KONFIGURATION ==="
            $configs=@(Get-NetIPConfiguration -ErrorAction Stop | Where-Object {$_.NetAdapter.Status -eq 'Up'})
            $configs | Format-List InterfaceAlias,IPv4Address,IPv6Address,IPv4DefaultGateway,DNSServer
            foreach($config in $configs){
                foreach($gateway in @($config.IPv4DefaultGateway | Where-Object {$_.NextHop})){
                    "=== GATEWAY / $($config.InterfaceAlias) / $($gateway.NextHop) ==="
                    Test-Connection -ComputerName $gateway.NextHop -Count 1 -Quiet -TimeoutSeconds 3
                }
                $dns=@($config.DNSServer.ServerAddresses | Where-Object {$_})
                if($dns.Count){
                    "=== DNS-ABFRAGE / $($config.InterfaceAlias) / $($dns[0]) ==="
                    try { Resolve-DnsName -Name 'www.microsoft.com' -Server $dns[0] -DnsOnly -QuickTimeout -ErrorAction Stop | Select-Object -First 4 Name,Type,IPAddress | Format-Table }
                    catch { "DNS-FEHLER: $($_.Exception.Message)" }
                }
            }
            if(!$configs.Count){'KEIN AKTIVER IP-ADAPTER MIT LESBARER KONFIGURATION'}
            "Hinweis: Gateway/DNS-Abfragen pruefen genau die gezeigten lokalen Ziele; kein Scan und keine Konfigurationsaenderung."
            """,
            "Vergleiche Adapterstatus, Adresse, Gateway und DNS-Ausgabe. Ein getrenntes oder virtuelles Interface kann beabsichtigt sein. Bei Fehlern zuerst Kabel/WLAN, Router und DNS-Konfiguration pruefen; AETHER setzt Adapter/IP/DNS nicht automatisch zurueck."),
        new("devices", "Geraete und Treiber", "PnP-Problemcodes und zugeordnete installierte Windows-Treiber",
            """
            $ErrorActionPreference='Stop'
            "=== PNP-PROBLEMGERAETE / NUR LESEN ==="
            $problems=@(Get-PnpDevice -PresentOnly -ErrorAction Stop | Where-Object {$_.Problem -gt 0 -or $_.Status -eq 'Error'})
            if($problems.Count){
                $problems | Select-Object FriendlyName,Class,Status,Problem,InstanceId | Format-List
            }else{'KEINE AKTUELLEN PNP-PROBLEMGERAETE GEMELDET'}
            "=== TREIBERINVENTAR / LETZTE 30 EINTRAEGE ==="
            Get-CimInstance Win32_PnPSignedDriver | Sort-Object DeviceName | Select-Object -First 30 DeviceName,DriverVersion,DriverDate,IsSigned,InfName | Format-Table
            "Ein Treiberdatum ist kein Aktualitaets- oder Sicherheitsnachweis."
            """,
            "Bei einem Problemcode zuerst Geraet und Code im Geraetemanager pruefen und den passenden Treiber ausschliesslich ueber Windows Update oder die offizielle Geraeteherstellerseite beziehen. AETHER ersetzt, entfernt oder installiert keine Treiber automatisch.")
    ];

    public static WindowsTroubleshootingAssistant Get(string id) =>
        All.SingleOrDefault(assistant => assistant.Id == id)
        ?? throw new ArgumentException("Unbekannter Windows-Diagnoseassistent.", nameof(id));
}
