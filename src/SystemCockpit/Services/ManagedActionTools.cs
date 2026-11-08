using AetherOperations;

namespace SystemCockpit.Services;

public sealed record ManagedActionTool(ManagedAction Action, string Risk)
{
    public string Name => $"{Action.Category} / {Action.Name}";
}

public sealed record OptionalMicrosoftTool(string Name, string Executable, string DownloadUrl, string Risk)
{
    public string Display => $"{Name} – {Risk}";
}

public static class ManagedActionTools
{
    public static IReadOnlyList<ManagedActionTool> All =>
        ManagedActionCatalog.All.Select(a => new ManagedActionTool(a with
        {
            Name = DesktopUiText.Get($"Managed.{a.Id}.Name"),
            Category = DesktopUiText.Get($"Managed.Category.{a.Category}")
        }, Risk(a))).ToArray();

    private static readonly IReadOnlyList<OptionalMicrosoftTool> OptionalDefinitions =
    [
        new("Process Explorer", "procexp64.exe", "https://learn.microsoft.com/sysinternals/downloads/process-explorer",
            "Prozesse/DLLs; sensible Daten, extern auch Prozessbeendigung möglich"),
        new("Process Monitor", "Procmon64.exe", "https://learn.microsoft.com/sysinternals/downloads/procmon",
            "Datei/Registry/Prozessmitschnitt; hohe Last und sensible Pfade, Treiber und EULA"),
        new("Autoruns", "Autoruns64.exe", "https://learn.microsoft.com/sysinternals/downloads/autoruns",
            "Autostarts; extern Deaktivierung/Löschung möglich"),
        new("TCPView", "Tcpview64.exe", "https://learn.microsoft.com/sysinternals/downloads/tcpview",
            "Verbindungen; sensible Ziele, extern Verbindungen schließbar"),
        new("RAMMap", "RAMMap64.exe", "https://learn.microsoft.com/sysinternals/downloads/rammap",
            "Speicheranalyse; extern Cacheleerung möglich"),
        new("VMMap", "VMMap64.exe", "https://learn.microsoft.com/sysinternals/downloads/vmmap",
            "Prozessspeicheranalyse; Administratorrechte und sensible Daten"),
        new("Sigcheck", "sigcheck64.exe", "https://learn.microsoft.com/sysinternals/downloads/sigcheck",
            "Signaturen; optionale VirusTotal-Abfragen übertragen Daten, nicht automatisch aktiviert"),
        new("Handle", "handle64.exe", "https://learn.microsoft.com/sysinternals/downloads/handle",
            "Offene Handles; Schließen kann Datenverlust verursachen"),
        new("Windows Performance Recorder (ADK)", "wpr.exe", "https://learn.microsoft.com/windows-hardware/test/wpt/",
            "ETW-Aufzeichnung braucht bewusste Freigabe, erzeugt sensible Trace-Dateien"),
        new("Windows Performance Analyzer (ADK)", "wpa.exe", "https://learn.microsoft.com/windows-hardware/test/wpt/",
            "Lokale Trace-Analyse; separate optionale Installation"),
        new("Windows ADK / DISM-Werkzeuge", "dism.exe", "https://learn.microsoft.com/windows-hardware/get-started/adk-install",
            "Edition/Version prüfen; ADK ist nicht automatisch installiert")
    ];

    public static IReadOnlyList<OptionalMicrosoftTool> OptionalTools => OptionalDefinitions.Select(tool => tool with
    {
        Name = tool.Executable == "dism.exe" ? DesktopUiText.Get("OptionalTool.AdkName") : tool.Name,
        Risk = DesktopUiText.Get($"OptionalTool.{tool.Executable}.Risk")
    }).ToArray();

    public static string Risk(ManagedAction action) => DesktopUiText.Get("Managed.Risk." + (action.Id switch
    {
        "dns-cache-clear" => "DnsClear",
        "sfc-repair" => "SfcRepair",
        "dism-repair" => "DismRepair",
        "dns-health" => "DnsHealth",
        "windows-update-search" => "UpdateSearch",
        "inventory" => "Inventory",
        "bitlocker-status" => "BitLocker",
        "defender-status" => "Defender",
        "etw-sessions" => "Etw",
        "power-status" => "Power",
        "boot-config" => "Boot",
        "performance-snapshot" => "Performance",
        "dism-scan" or "sfc-verify" => "Check",
        "event-errors" => "Events",
        "pnputil-drivers" => "DriverPackages",
        "driver-versions" or "pnp-inventory" => "Drivers",
        "storage-status" => "Storage",
        _ => "Read"
    }));
}
