using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SystemCockpit.Services;

public sealed class ManagedDevice(string host, bool https = true) : Observable
{
    public string Host { get; } = ValidateHost(host);
    public bool Https { get; } = https;
    public string Transport => Https ? "WinRM HTTPS / 5986" : "WinRM HTTP / 5985";
    private bool _selected;
    private string _status = "Nicht abgefragt";
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public static string ValidateHost(string host)
    {
        host = host.Trim();
        if (!Regex.IsMatch(host, @"^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?$") ||
            host.Contains("..", StringComparison.Ordinal) || host.Split('.').Any(p => p.Length > 63))
            throw new ArgumentException("DNS-/Windows-Hostname oder IPv4 eingeben; keine URL, Portliste, Wildcards oder Befehle.");
        return host;
    }
}
public sealed record SavedDevice(string Host, bool Https);
public static class ManagedDevices
{
    public static string DiagnosticScript(ManagedDevice target)
    {
        var args = $"-ComputerName {AdminManagement.Quote(target.Host)} -Authentication Negotiate -Port {(target.Https ? 5986 : 5985)} " + (target.Https ? "-UseSSL " : "");
        return "$ErrorActionPreference='Stop'; Test-WSMan " + args + " | Format-List; Invoke-Command " + args + """
            -SessionOption (New-PSSessionOption -OpenTimeout 15000 -OperationTimeout 60000) -ScriptBlock {
                $ErrorActionPreference='Stop'
                "TARGET / $env:COMPUTERNAME / Konto $([Security.Principal.WindowsIdentity]::GetCurrent().Name)"
                Get-CimInstance Win32_ComputerSystem | Format-List Manufacturer,Model,TotalPhysicalMemory
                Get-CimInstance Win32_OperatingSystem | Format-List Caption,Version,LastBootUpTime
                Get-Volume | Format-Table DriveLetter,HealthStatus,Size,SizeRemaining
                Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 30 Name,Id,CPU,WorkingSet64 | Format-Table
                Get-Service | Where-Object Status -eq 'Stopped' | Select-Object -First 100 Name,DisplayName,StartType | Format-Table
                "GESTOPPTE DIENSTE SIND NICHT PAUSCHAL FEHLER"
                foreach($log in @('System','Application')){
                    $errors=@(); $events=@(Get-WinEvent -FilterHashtable @{LogName=$log;StartTime=(Get-Date).AddDays(-1);Level=1,2,3} -MaxEvents 50 -ErrorAction SilentlyContinue -ErrorVariable errors)
                    if(@($errors | Where-Object FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*').Count){throw ($errors | Out-String)}
                    $events | Format-List TimeCreated,Id,ProviderName,Message
                }
            }
            """;
    }
    public static List<ManagedDevice> Load(string path)
    {
        if (!File.Exists(path)) return [];
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.EnumerateArray().Any(item =>
            item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("Host", out var host) || host.ValueKind != JsonValueKind.String ||
            !item.TryGetProperty("Https", out var tls) || tls.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
            throw new InvalidDataException("Ziele benoetigen Host und explizite HTTPS-/HTTP-Auswahl; keine unsicheren Standardwerte.");
        var saved = JsonSerializer.Deserialize<List<SavedDevice>>(json) ?? throw new InvalidDataException("Leere Geraetekonfiguration.");
        if (saved.Count > 500 || saved.Any(d => d is null) || saved.Select(d => d.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count() != saved.Count)
            throw new InvalidDataException("Ungueltige/doppelte Geraeteziele; keine Verbindung gestartet.");
        return saved.Select(d => new ManagedDevice(d.Host, d.Https)).ToList();
    }
    public static void Save(string path, IEnumerable<ManagedDevice> devices)
    {
        var saved = devices.Select(d => new SavedDevice(d.Host, d.Https)).ToList();
        if (saved.Count > 500) throw new ArgumentException("Maximal 500 explizite Ziele.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(saved));
        File.Move(path + ".tmp", path, true);
    }
    public const string CloudReadinessScript = """
        "LOCAL ONLY / keine Graph-Anmeldung, keine Cloud-Abfrage"
        Get-CimInstance Win32_OperatingSystem | Format-List Caption,Version
        Get-Service WinRM | Format-List Name,Status,StartType
        "WinRM-Konfiguration wird nicht veraendert; kein TrustedHosts-Wildcard/Basic/SkipCACheck"
        $office='HKLM:\SOFTWARE\Microsoft\Office\ClickToRun\Configuration'
        if(Test-Path -LiteralPath $office){Get-ItemProperty -LiteralPath $office | Select-Object ProductReleaseIds,VersionToReport,UpdateChannel | Format-List}else{'Office Click-to-Run nicht lokal gefunden'}
        Get-Module -ListAvailable Microsoft.Graph.Authentication,ExchangeOnlineManagement,MicrosoftTeams | Select-Object Name,Version | Format-Table
        "M365 / Entra / Intune / Copilot: nicht verbunden. Tenant, App-Registrierung, Rollen/Lizenzen und freigegebene Scopes erforderlich."
        "Lokale Softwarepraesenz beweist keine Cloud-Lizenz oder Copilot-Verfuegbarkeit. Keine automatische Modulinstallation."
        """;
}
