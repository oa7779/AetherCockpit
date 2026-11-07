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
    public static IReadOnlyList<ManagedActionTool> All { get; } =
        ManagedActionCatalog.All.Select(a => new ManagedActionTool(a, Risk(a))).ToArray();

    public static IReadOnlyList<OptionalMicrosoftTool> OptionalTools { get; } =
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

    public static string Risk(ManagedAction action) => action.Id switch
    {
        "dns-cache-clear" => "ÄNDERND: leert nur lokalen DNS-Cache; kein Adapterreset. DNS-Erfolg danach erneut prüfen.",
        "sfc-repair" => "ÄNDERND: ersetzt Windows-Systemdateien; vorher sichern, CBS-Endbefund prüfen. Abbruch ist kein Rollback.",
        "dism-repair" => "ÄNDERND: repariert Komponentenspeicher; kann konfigurierte Updatequelle kontaktieren, vorher sichern. Abbruch ist kein Rollback.",
        "dns-health" => "NETZWERK: löst www.microsoft.com über Standardresolver und konfigurierte DNS-Server aktiver Gateway-Adapter auf; kein Scan/Reset.",
        "windows-update-search" => "NETZWERK: COM-Suche bei konfigurierter Microsoft-/WSUS-Quelle; zeigt Identität/Revision, installiert nichts, keine EULA-Annahme, kein Neustart.",
        "inventory" => "LESEND: begrenztes Inventar einschließlich App-/Geräte-/Prozessdaten und Ereignissen; enthält sensible Daten. Fehlende Bereiche bleiben unbekannt.",
        "bitlocker-status" => "LESEND: Volume-/Verschlüsselungsstatus, keine Schlüssel; Modul und Edition müssen unterstützt sein.",
        "defender-status" => "LESEND: Defender-Modul kann fehlen; passiver/deaktivierter Status ist kein allgemeiner Sicherheitsnachweis.",
        "etw-sessions" => "LESEND: logman listet Sitzungen/Provider; kein Start eines Captures, Ausgabe kann gekürzt werden.",
        "power-status" => "LESEND: keine Änderung am Energieschema; Schlafzustände hängen von Firmware/Treibern ab.",
        "boot-config" => "LESEND: BCDEdit liest aktuellen Boot-Eintrag; keine Bootänderung, Rechte erforderlich.",
        "performance-snapshot" => "LESEND: Prozesse und gespeicherter WinSAT-Befund; kein Benchmark, ungültige/veraltete Werte nicht als aktuelle Leistung interpretieren.",
        "dism-scan" or "sfc-verify" => "PRÜFEND: erhöhte Rechte, hohe I/O-Last und Diagnoseprotokolle möglich; keine Reparatur. Endbefund prüfen.",
        "event-errors" => "LESEND: maximal 100 Ereignisse je Kanal/7 Tage; sensible Meldungen. Keine Treffer beweisen keine Fehlerfreiheit.",
        "pnputil-drivers" => "LESEND: nur /enum-drivers; Umfang/Optionen hängen von Windows-Build ab, keine Treiberinstallation.",
        "driver-versions" or "pnp-inventory" => "LESEND: Geräte-/Treiberdaten; Datum ist kein Aktualitätsnachweis. Installation nur separat mit exakter geprüfter Auswahl.",
        "storage-status" => "LESEND: Windows-Storage-Modul; HealthStatus ist kein vollständiger SMART-/Hardwaretest.",
        _ => "LESEND: lokale Windows-Abfrage; sensible Daten möglich, Module/Edition/Rechte versionsabhängig. Fehlende Befehle werden als Fehler gemeldet."
    };
}
