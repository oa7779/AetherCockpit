namespace SystemCockpit.Services;

internal sealed record TaskTranslation(string Title, string Effect, string Steps, string Keywords);

internal static class TaskCatalogEnglish
{
    private static readonly IReadOnlyDictionary<string, TaskTranslation> Workflows =
        new Dictionary<string, TaskTranslation>(StringComparer.Ordinal)
        {
            ["Prozesse anzeigen"] = new("Show processes", "READ ONLY / shared Windows sample", "Open the list or tree. PID and creation time form the identity. Context actions revalidate the target.", "process tree"),
            ["Prozess suchen"] = new("Find a process", "READ ONLY / focus search", "Filter by name, PID or state. Search never performs a process action.", "search filter pid"),
            ["Hardwarebericht erstellen"] = new("Create hardware report", "FILE / export existing observations", "Read the required classes first, then use the privacy-aware export. No automatic full inventory.", "hardware report export"),
            ["Netzwerkverbindungen anzeigen"] = new("Show network connections", "READ ONLY / owner-PID endpoints", "Read TCP and UDP endpoints on request. Owner PID is time-bound, not a byte rate or permanent identity.", "socket tcp udp listener"),
            ["Dienste anzeigen"] = new("Show services", "READ ONLY / open service area", "Load service status deliberately. Changes still need preview and confirmation.", "service startup"),
            ["Systemsnapshot aufnehmen"] = new("Capture system snapshot", "READ ONLY / local session snapshot", "Capture processes, services, startup entries and endpoints with separate source times. Not atomic. Export separately.", "snapshot capture"),
            ["Snapshots vergleichen"] = new("Compare snapshots", "READ ONLY / compare two local states", "Capture or load two snapshots. Compare identities without deriving a security diagnosis.", "snapshot compare"),
            ["Dateispeicherort öffnen"] = new("Open file location", "LOCAL / selected process file", "Select a process first. Revalidate its identity and path before selecting the file in Explorer.", "file explorer path"),
            ["Windows-Probleme eingrenzen"] = new("Troubleshoot Windows problems", "READ ONLY / review the current status",
                "1. Note the time and symptoms.\n2. Review diagnostics and events.\n3. Export a redacted report.\n4. Only then approve an appropriate repair individually.", "error support report system status"),
            ["Abstuerze und Hardwarefehler"] = new("Crashes and hardware errors", "READ ONLY / Windows events",
                "Narrow down the channel and time period. Read event details and timing. Kernel-Power alone does not prove a faulty power supply.", "crash whea bugcheck bluescreen"),
            ["Windows gezielt reparieren"] = new("Repair Windows selectively", "CHANGES / select and confirm first",
                "Review diagnostics and back up data first. Select a guided SFC or DISM action; check the warning and exact command. Read the result and CBS.log; exit code 0 does not prove system health.", "sfc dism restorehealth repair"),
            ["Wartungsplan und Windows-Updates"] = new("Maintenance plans and Windows updates", "MIXED / preview, then individual confirmations",
                "Open maintenance plans, choose a template and review its tasks. Plan a restore point before changes. Check update IDs and EULAs; no automatic restart.", "assistant windows update restart restore point"),
            ["Schutzstatus pruefen"] = new("Check protection status", "READ ONLY / no protection is disabled",
                "Review Defender, Firewall, TPM, Secure Boot and BitLocker separately. Unavailable does not mean safe. AETHER is not a replacement for security software.", "defender firewall tpm bitlocker secure boot"),
            ["Hardware und Software inventarisieren"] = new("Inventory hardware and software", "READ ONLY / inventory on request",
                "Open the computer inventory and save it locally. Check serial numbers, computer names and software lists before sharing.", "inventory export computer hardware software"),
            ["Anwendungen und Updates verwalten"] = new("Manage apps and updates", "NETWORK / changes only after confirmation",
                "Load WinGet packages or updates. Check source, ID and version; read details if needed. Mark the selection and confirm the preview. Jobs show each package result. Other package managers may be opened in a separately installed UniGetUI; external actions remain outside AETHER.", "winget unigetui install uninstall scoop chocolatey"),
            ["Softwareliste uebertragen"] = new("Transfer a software list", "FILE / import does not install anything",
                "Export source-bound packages as an AETHER list. Import on the destination, check sources and IDs, then install the selection. Versions are hints, not version locks. Installers and licenses are not backed up.", "backup restore manifest import export package list"),
            ["Auftraege und Ergebnisse lesen"] = new("Review jobs and results", "READ ONLY / temporary session history",
                "Review each job or package status. Failed, cancelled and not started remain distinct. Sensitive displays and free console commands are excluded.", "queue history error exit code"),
            ["Verbindungsprobleme eingrenzen"] = new("Troubleshoot connection problems", "NETWORK / tests require approval",
                "First check the adapter and gateway. Then assess router ping, destination ping and DNS separately. The speed test generates real traffic. Scan only networks you are authorized to test.", "internet dns ping wifi gateway speed test"),
            ["Pakete filtern und untersuchen"] = new("Filter and inspect packets", "CAPTURE / separate consent required",
                "Choose the interface and approve capture. Filter by protocol, address and port; inspect packet details. Filters apply only to at most 200 live rows, not the PCAP file. Alerts are indicators, not proof of an attack.", "wireshark npcap tcp udp dns syn capture packet"),
            ["PCAP in Wireshark analysieren"] = new("Analyze PCAP in Wireshark", "EXTERNAL / local file contains sensitive data",
                "Stop capture first. Select a local PCAP or PCAPNG file and a separately installed Wireshark. AETHER passes only the file; no download, upload or automatic capture.", "pcap pcapng protocol analysis wireshark export"),
            ["Systemlast live beobachten"] = new("Monitor system load live", "READ ONLY / existing live measurements",
                "Review CPU, RAM, GPU, network and drives together. The first counter point may be a baseline. Missing values remain unknown; animated data paths are symbolic.", "dashboard load temperature utilization"),
            ["Sensorverlaeufe untersuchen"] = new("Inspect sensor history", "READ ONLY / actual sensor channels",
                "Search for hardware or sensors and show individual graphs. Do not mix units. Gaps remain gaps; temperature limits are not measurements.", "chart graph hotspot vram voltage fan"),
            ["Messsitzung auswerten und exportieren"] = new("Analyze and export a measurement session", "READ ONLY / CSV saved only on request",
                "Start a new session, reproduce the problem and pause statistics. Filter by hardware or channel and export visible MIN/MAX/AVG as CSV. CSV contains statistics, not raw recording.", "min max avg average csv comparison"),
            ["Messlogs und Gaming vergleichen"] = new("Compare measurement logs and gaming", "FILE / capture needs separate approval",
                "Load an existing JSONL file and compare a second. Enable cockpit recording for new raw data. Start PresentMon only for a deliberately selected process; this is not a synthetic stress test.", "history fps frametime presentmon logging gaming"),
            ["Laufwerke und SMART diagnostizieren"] = new("Diagnose drives and SMART", "MIXED / self-tests only after confirmation",
                "Detect devices and read diagnostics and capabilities. Review SMART warnings carefully and back up important data. Self-tests create load and continue on the device. No estimated remaining lifetime is calculated.", "nvme ssd hdd smart failure wear health"),
            ["Mainboard und Komponenten ansehen"] = new("View mainboard and components", "READ ONLY / schematic illustration",
                "Select components for details. Distinguish live status and actual readings from symbolic fans and data paths. Refresh hardware to update the inventory.", "board hardware components"),
            ["Desktop-Gadgets und Transparenz"] = new("Desktop gadgets and opacity", "SETTING / no system tuning",
                "Set graph groups, opacity and positions. The tray icon manages gadgets and the app lifecycle. Autostart requires explicit approval.", "xbar sidebar gadgets opacity"),
            ["Speicherplatz kontrolliert freigeben"] = new("Free disk space with control", "DELETES / preview and confirmation",
                "Choose a category and inspect the preview. Use the path filter and select only intended candidates. Hidden checked files remain included in the total and confirmation. Deletion is permanent; there is no recycle bin.", "cache temp cleanup disk space"),
            ["Grosse Dateien finden"] = new("Find large files", "READ ONLY / only the selected folder",
                "Choose a folder and minimum size. At most 100,000 entries, 32 levels and 1,000 largest results. Check limits and read notices. CSV contains paths; review before sharing. There is no deletion function or complete disk usage map.", "large files storage"),
            ["Autostart nachvollziehbar verwalten"] = new("Manage startup entries transparently", "CHANGES / with a local backup",
                "Read Run entries and search by name or command. Check the full command and HKCU/HKLM. Disable only a known entry; enabling uses the backup and rejects conflicts.", "startup boot restore"),
            ["Prozesse, Dienste und Aufgaben"] = new("Processes, services and tasks", "CHANGES / targeted administration",
                "Read the current state first. Choose one known change and review the preview and impact. No blanket disabling of services or claimed RAM acceleration.", "task manager service process administration"),
            ["Grenzen, Einstellungen und Hilfe"] = new("Limits, settings and help", "READ ONLY / local guide",
                "Search by module or keyword. Consider sensor gaps, privacy and recovery guidance. No registry cleaner, protection disabling, blanket debloating or performance promises.", "tuning guide help safety"),
            ["Mess- und Werkzeug-Einstellungen"] = new("Measurement and tool settings", "SETTING / paths and thresholds",
                "Check tool paths and measurement intervals. Warning thresholds are user-defined limits, not sensor readings. Missing external programs are not installed automatically.", "configuration smartctl presentmon thresholds")
        };

    internal static TaskTranslation Get(string title) => Workflows.TryGetValue(title, out var value)
        ? value : throw new InvalidOperationException($"Missing English workflow translation for '{title}'.");

    internal static string Label(string value) => value switch
    {
        "Prozesse" => "Processes", "Taskmanager" => "Process center", "HardwareIntelligence" => "Hardware Intelligence",
        "Diagnose" => "Diagnostics", "Netzwerk" => "Network", "Monitoring" => "Monitoring",
        "Bereinigung" => "Cleanup", "Hilfe" => "Help", "Software" => "Software",
        "Befehlskatalog" => "Command catalog", "Hauptfenster" => "Main window",
        "Systemkontrolle" => "System Control", "Desktop" => "Desktop",
        "Ereignisse" => "Events", "Wartung" => "Maintenance", "Sicherheit" => "Security",
        "Pakete" => "Packages", "Auftraege" => "Jobs", "Netzwerkbeobachtung" => "Network monitor",
        "Sensoren" => "Sensors", "Messauswertung" => "Measurements", "Desktop-Einstellungen" => "Desktop settings",
        "Speicheranalyse" => "Storage analysis", "Einstellungen" => "Settings",
        "Cockpit" or "Operations" or "Logs / Gaming" or "SMART" or "Mainboard" or "Cleaner" or "Autostart" or "Superadmin" => value,
        _ => throw new InvalidOperationException($"Missing English task label for '{value}'.")
    };
}
