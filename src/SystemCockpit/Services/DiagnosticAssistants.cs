using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

public sealed record DiagnosticEvent(string Log, string TimeCreated, int Id, int? Level, string Provider, string Message);
public sealed record DiagnosticDevice(string Name, string Class, string Status, int? Problem, string InstanceId);
public sealed record DiagnosticAdapter(string Name, string Description, string Status, string LinkSpeed);
public sealed record DiagnosticApplication(string Name, string Version, string Publisher);
public sealed record DiagnosticDriver(string Device, string Version, string Date, bool? IsSigned);
public sealed record WindowsDiagnosticSnapshot(
    string MachineName,
    string Windows,
    string DisplayVersion,
    string Build,
    DiagnosticEvent[] Events,
    DiagnosticDevice[] Devices,
    DiagnosticAdapter[] Adapters,
    DiagnosticApplication[] Applications,
    DiagnosticDriver[] Drivers);
public sealed record DiagnosticFinding(
    string Agent,
    string Severity,
    string Summary,
    string Evidence,
    string Recommendation);
public sealed record DiagnosticAgentReport(DateTimeOffset CapturedAt, WindowsDiagnosticSnapshot Snapshot, DiagnosticFinding[] Findings);

public static class DiagnosticAssistants
{
    public const string CollectionScript = """
        $ErrorActionPreference='Stop'
        $os=Get-CimInstance Win32_OperatingSystem
        $version=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
        $events=@(
            foreach($log in @('System','Application')){
                $errors=@()
                $items=@(Get-WinEvent -FilterHashtable @{LogName=$log;StartTime=(Get-Date).AddDays(-1);Level=1,2,3} -MaxEvents 100 -ErrorAction SilentlyContinue -ErrorVariable errors)
                $unexpected=@($errors | Where-Object FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*')
                if($unexpected.Count){throw "Ereignisprotokoll $log nicht lesbar: $($unexpected | Out-String)"}
                foreach($item in $items){
                    [pscustomobject]@{
                        Log=$log
                        TimeCreated=$item.TimeCreated.ToString('o')
                        Id=$item.Id
                        Level=$item.Level
                        Provider=$item.ProviderName
                        Message=$item.Message
                    }
                }
            }
        )
        $devices=@(Get-PnpDevice -PresentOnly | Select-Object @{n='Name';e={$_.FriendlyName}},Class,Status,Problem,InstanceId)
        $adapters=@(Get-NetAdapter -IncludeHidden | Select-Object @{n='Name';e={$_.Name}},@{n='Description';e={$_.InterfaceDescription}},Status,LinkSpeed)
        $applications=@(
            foreach($path in @(
                'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
                'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
                'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
            )){
                if(Test-Path -LiteralPath $path){
                    foreach($entry in Get-ChildItem -LiteralPath $path){
                        $app=Get-ItemProperty -LiteralPath $entry.PSPath
                        if($app.DisplayName){
                            [pscustomobject]@{Name=[string]$app.DisplayName;Version=[string]$app.DisplayVersion;Publisher=[string]$app.Publisher}
                        }
                    }
                }
            }
        )
        $drivers=@(Get-CimInstance Win32_PnPSignedDriver | Select-Object @{n='Device';e={$_.DeviceName}},@{n='Version';e={$_.DriverVersion}},@{n='Date';e={$_.DriverDate}},IsSigned)
        [pscustomobject]@{
            MachineName=$env:COMPUTERNAME
            Windows=$os.Caption
            DisplayVersion=[string]$version.DisplayVersion
            Build=[string]$version.CurrentBuildNumber+'.'+[string]$version.UBR
            Events=$events
            Devices=$devices
            Adapters=$adapters
            Applications=$applications
            Drivers=$drivers
        } | ConvertTo-Json -Compress -Depth 5
        """;

    public static WindowsDiagnosticSnapshot Parse(string json)
    {
        var snapshot = JsonSerializer.Deserialize<WindowsDiagnosticSnapshot>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.MachineName) ||
            snapshot.Events is null || snapshot.Events.Any(e => e is null) ||
            snapshot.Devices is null || snapshot.Devices.Any(d => d is null) ||
            snapshot.Adapters is null || snapshot.Adapters.Any(a => a is null) ||
            snapshot.Applications is null || snapshot.Applications.Any(a => a is null) ||
            snapshot.Drivers is null || snapshot.Drivers.Any(d => d is null))
            throw new InvalidDataException("Windows-Diagnose lieferte keinen vollstaendigen Snapshot; Befund bleibt unbekannt.");
        return snapshot;
    }

    public static DiagnosticAgentReport Analyze(WindowsDiagnosticSnapshot snapshot, DateTimeOffset? capturedAt = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var findings = new List<DiagnosticFinding>();

        foreach (var group in snapshot.Events
                     .Where(e => e.Level is 1 or 2 || e.Id is 1000 or 1001)
                     .GroupBy(e => (e.Log, e.Provider, e.Id))
                     .OrderByDescending(g => g.Max(e => e.Level ?? 0)))
        {
            var sample = group.OrderByDescending(e => e.TimeCreated, StringComparer.Ordinal).First();
            var severity = group.Any(e => e.Level == 1) ? "KRITISCH" :
                group.Count() >= 5 ? "HOCH" : "MITTEL";
            findings.Add(new(
                group.Key.Log == "Application" ? "Anwendungsereignisse" : "Windows-Ereignisse",
                severity,
                $"{group.Key.Provider} / Ereignis {group.Key.Id}: {group.Count()} Treffer in 24 Stunden",
                sample.Message,
                "Ereignisdetails und betroffene Anwendung/Geraet pruefen. Keine automatische Reparatur abgeleitet."));
        }

        foreach (var device in snapshot.Devices.Where(d => d.Problem is > 0 ||
                     string.Equals(d.Status, "Error", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(d.Status, "Degraded", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(d.Status, "Unknown", StringComparison.OrdinalIgnoreCase) ||
                     string.IsNullOrWhiteSpace(d.Status)))
        {
            var statusUnknown = string.IsNullOrWhiteSpace(device.Status) ||
                string.Equals(device.Status, "Unknown", StringComparison.OrdinalIgnoreCase);
            var severity = device.Problem is > 0 || string.Equals(device.Status, "Error", StringComparison.OrdinalIgnoreCase)
                ? "HOCH" : statusUnknown ? "UNBEKANNT" : "MITTEL";
            findings.Add(new("Treiber und Geraete", severity,
                $"{device.Name}: Status {device.Status ?? "nicht gemeldet"}, Problemcode {device.Problem?.ToString() ?? "nicht gemeldet"}",
                $"{device.Class} / {device.InstanceId}",
                statusUnknown
                    ? "Windows lieferte keinen Geraetestatus; daraus wird kein fehlerfreier Zustand abgeleitet."
                    : "Geraetemanager, Hardwareverbindung und passenden Herstellertreiber pruefen. Treiber werden nicht automatisch ersetzt."));
        }

        if (snapshot.Adapters.Length == 0)
            findings.Add(new("Verbindungen", "UNBEKANNT", "Windows meldet keine Netzwerkadapter.",
                "Die Adapterabfrage lieferte keine Eintraege.", "Verbindungsstatus ist nicht verfuegbar; daraus wird kein Fehler oder Reparaturschritt abgeleitet."));
        else if (snapshot.Adapters.Any(a => string.IsNullOrWhiteSpace(a.Status)))
            findings.Add(new("Verbindungen", "UNBEKANNT", "Mindestens ein Netzwerkadapter hat keinen lesbaren Status.",
                string.Join("; ", snapshot.Adapters.Select(a => $"{a.Name}: {a.Status ?? "nicht gemeldet"}")),
                "Adapterdaten pruefen; fehlende Statuswerte werden nicht als fehlerfrei gewertet."));
        else if (!snapshot.Adapters.Any(a => string.Equals(a.Status, "Up", StringComparison.OrdinalIgnoreCase)))
            findings.Add(new("Verbindungen", "HINWEIS", "Kein Netzwerkadapter meldet aktuell Status Up.",
                string.Join("; ", snapshot.Adapters.Select(a => $"{a.Name}: {a.Status}")),
                "WLAN-/LAN-Verbindung und Adapterzustand pruefen. Deaktivierte, virtuelle und getrennte Adapter koennen beabsichtigt sein."));

        findings.Add(new("Anwendungsinventar", "INFORMATION",
            snapshot.Applications.Length == 0
                ? "Installierte Anwendungen konnten nicht ausgewiesen werden."
                : $"{snapshot.Applications.Length} installierte Anwendungen erfasst.",
            "Quelle: Windows-Deinstallationsregistrierung; keine Installer gestartet.",
            "Versionsinventar ist keine Update- oder Sicherheitsbewertung."));

        findings.Add(new("Treiberinventar", "INFORMATION",
            snapshot.Drivers.Length == 0
                ? "Treiberinventar ist nicht verfuegbar."
                : $"{snapshot.Drivers.Length} Windows-PnP-Treiber erfasst.",
            "Quelle: Win32_PnPSignedDriver; keine Treiberupdates gesucht.",
            "Ein Versionsdatum allein beweist weder Aktualitaet noch Fehlerfreiheit."));

        if (!snapshot.Events.Any(e => e.Level is 1 or 2 || e.Id is 1000 or 1001))
            findings.Insert(0, new("Windows-Ereignisse", "INFORMATION",
                "Keine kritischen oder wiederholt auftretenden System-/Anwendungsfehler in der begrenzten Abfrage erkannt.",
                "System- und Application-Protokoll / letzte 24 Stunden / maximal 100 Ereignisse je Kanal.",
                "Dies ist kein vollstaendiger Systemgesundheitsnachweis."));

        return new(capturedAt ?? DateTimeOffset.Now, snapshot, findings.ToArray());
    }
}
