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
    string Recommendation)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string AgentDisplay => DesktopUiText.Get(Agent switch
    {
        "Anwendungsereignisse" => "Diagnostic.Agent.ApplicationEvents",
        "Windows-Ereignisse" => "Diagnostic.Agent.WindowsEvents",
        "Treiber und Geraete" => "Diagnostic.Agent.Devices",
        "Verbindungen" => "Diagnostic.Agent.Connections",
        "Anwendungsinventar" => "Diagnostic.Agent.Applications",
        "Treiberinventar" => "Diagnostic.Agent.Drivers",
        _ => throw new InvalidOperationException($"Unknown diagnostic agent code '{Agent}'.")
    });

    [System.Text.Json.Serialization.JsonIgnore]
    public string SeverityDisplay => DesktopUiText.Get("Diagnostic.Level." + Severity);
}
public sealed record DiagnosticAgentReport(DateTimeOffset CapturedAt, WindowsDiagnosticSnapshot Snapshot, DiagnosticFinding[] Findings);

public static class DiagnosticAssistants
{
    public static string CollectionScript => "$aetherEventLogError = '" +
        DesktopUiText.Get("Diagnostic.EventReadFailure").Replace("'", "''", StringComparison.Ordinal) + "'\n" + """
        $ErrorActionPreference='Stop'
        $os=Get-CimInstance Win32_OperatingSystem
        $version=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
        $events=@(
            foreach($log in @('System','Application')){
                $errors=@()
                $items=@(Get-WinEvent -FilterHashtable @{LogName=$log;StartTime=(Get-Date).AddDays(-1);Level=1,2,3} -MaxEvents 100 -ErrorAction SilentlyContinue -ErrorVariable errors)
                $unexpected=@($errors | Where-Object FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*')
                if($unexpected.Count){throw ($aetherEventLogError -f $log,($unexpected | Out-String))}
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
            throw new InvalidDataException(DesktopUiText.Get("Diagnostic.Incomplete"));
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
                DesktopUiText.Format("Diagnostic.EventSummary", group.Key.Provider, group.Key.Id, group.Count()),
                sample.Message,
                DesktopUiText.Get("Diagnostic.EventAdvice")));
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
                DesktopUiText.Format("Diagnostic.DeviceSummary", device.Name,
                    device.Status ?? DesktopUiText.Get("Diagnostic.NotReported"),
                    device.Problem?.ToString() ?? DesktopUiText.Get("Diagnostic.NotReported")),
                $"{device.Class} / {device.InstanceId}",
                statusUnknown
                    ? DesktopUiText.Get("Diagnostic.UnknownDeviceAdvice")
                    : DesktopUiText.Get("Diagnostic.DeviceAdvice")));
        }

        if (snapshot.Adapters.Length == 0)
            findings.Add(new("Verbindungen", "UNBEKANNT", DesktopUiText.Get("Diagnostic.NoAdapters"),
                DesktopUiText.Get("Diagnostic.NoAdapterEvidence"), DesktopUiText.Get("Diagnostic.NoAdapterAdvice")));
        else if (snapshot.Adapters.Any(a => string.IsNullOrWhiteSpace(a.Status)))
            findings.Add(new("Verbindungen", "UNBEKANNT", DesktopUiText.Get("Diagnostic.UnknownAdapter"),
                string.Join("; ", snapshot.Adapters.Select(a => $"{a.Name}: {a.Status ?? DesktopUiText.Get("Diagnostic.NotReported")}")),
                DesktopUiText.Get("Diagnostic.UnknownAdapterAdvice")));
        else if (!snapshot.Adapters.Any(a => string.Equals(a.Status, "Up", StringComparison.OrdinalIgnoreCase)))
            findings.Add(new("Verbindungen", "HINWEIS", DesktopUiText.Get("Diagnostic.NoUpAdapter"),
                string.Join("; ", snapshot.Adapters.Select(a => $"{a.Name}: {a.Status}")),
                DesktopUiText.Get("Diagnostic.NoUpAdapterAdvice")));

        findings.Add(new("Anwendungsinventar", "INFORMATION",
            snapshot.Applications.Length == 0
                ? DesktopUiText.Get("Diagnostic.NoApplications")
                : DesktopUiText.Format("Diagnostic.ApplicationCount", snapshot.Applications.Length),
            DesktopUiText.Get("Diagnostic.ApplicationSource"),
            DesktopUiText.Get("Diagnostic.ApplicationAdvice")));

        findings.Add(new("Treiberinventar", "INFORMATION",
            snapshot.Drivers.Length == 0
                ? DesktopUiText.Get("Diagnostic.NoDrivers")
                : DesktopUiText.Format("Diagnostic.DriverCount", snapshot.Drivers.Length),
            DesktopUiText.Get("Diagnostic.DriverSource"),
            DesktopUiText.Get("Diagnostic.DriverAdvice")));

        if (!snapshot.Events.Any(e => e.Level is 1 or 2 || e.Id is 1000 or 1001))
            findings.Insert(0, new("Windows-Ereignisse", "INFORMATION",
                DesktopUiText.Get("Diagnostic.NoErrors"),
                DesktopUiText.Get("Diagnostic.NoErrorEvidence"),
                DesktopUiText.Get("Diagnostic.NoErrorAdvice")));

        return new(capturedAt ?? DateTimeOffset.Now, snapshot, findings.ToArray());
    }
}
