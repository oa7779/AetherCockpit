namespace SystemCockpit.Services;

public sealed record CockpitTask(string Category, string Title, string Destination, string Surface,
    string Effect, string Steps, string Keywords, string? Command = null, string? QuickAction = null)
{
    private bool English => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
    private TaskTranslation? Translation => English && Command is null
        ? QuickAction?.StartsWith("process-id:", StringComparison.Ordinal) == true
            ? new(Title, "READ ONLY / existing process sample", "Select the process. Actions revalidate its identity.", "process pid")
            : QuickAction?.StartsWith("intelligence", StringComparison.Ordinal) == true
                ? new(Title, "READ ONLY / existing evidence", "Inspect the entity, source and observation time.", "system intelligence")
                : TaskCatalogEnglish.Get(Title)
        : null;
    public string CategoryDisplay => English ? TaskCatalogEnglish.Label(Category) : Category switch
    {
        "System Intelligence" => "Systemanalyse",
        "Hardware Intelligence" => "Hardwareübersicht",
        _ => Category
    };
    public string DestinationDisplay => English ? TaskCatalogEnglish.Label(Destination) : Destination switch
    {
        "Auftraege" => "Aufträge",
        "Cleaner" => "Cache-Bereinigung",
        "Operations" => "Wartung",
        "Superadmin" => "Systemverwaltung",
        "Cockpit" => "Systemübersicht",
        "Taskmanager" => "Prozesse / Leistung",
        "SystemIntelligence" => "Systemanalyse",
        "HardwareIntelligence" => "Hardwareübersicht",
        _ => Destination
    };
    public string SurfaceDisplay => English ? TaskCatalogEnglish.Label(Surface) : Surface;
    public string TitleDisplay => Translation?.Title ?? Title;
    public string EffectDisplay => Translation?.Effect ?? Effect;
    public string StepsDisplay => Translation?.Steps ?? Steps;
    public string SearchText => $"{Category} {Title} {Destination} {Effect} {Steps} {Keywords} " +
        $"{CategoryDisplay} {TitleDisplay} {DestinationDisplay} {EffectDisplay} {StepsDisplay} {Translation?.Keywords}";
}

public static class TaskCatalog
{
    private static IReadOnlyList<CockpitTask> Workflows { get; } =
    [
        new("Guardian", "Änderungsjournal öffnen", "Guardian", "Hauptfenster", "LESEND / lokale Transaktionen", "Quellen, Nachherprüfung und Fehler lesen.", "journal change", QuickAction: "Change Journal"),
        new("Guardian", "Wiederherstellungsarchiv öffnen", "Guardian", "Hauptfenster", "LOKAL / sensible Zustandsdokumentation", "Kein Dateibackup oder Systemimage.", "vault recovery", QuickAction: "Recovery Vault"),
        new("Guardian", "Wiederherstellungsstatus prüfen", "Guardian", "Hauptfenster", "LESEND / definierte Kategorien", "Kontext, Integrität und Umfang prüfen; kein Prozentscore.", "readiness backup", QuickAction: "Recovery Readiness"),
        new("Guardian", "Zustandsaufnahme speichern", "Guardian", "Hauptfenster", "LOKAL / bewusst auslösen", "Quellen lesen und Umfang vor Nutzung prüfen.", "recovery point", QuickAction: "Recovery Vault"),
        new("Guardian", "Installationsvergleich starten", "Guardian", "Hauptfenster", "LESEND / Vorher und Nachher", "Vorherzustand speichern, Installation selbst auslösen und danach vergleichen.", "installation observer", QuickAction: "Installation Observer"),
        new("Guardian", "Updateverlauf öffnen", "Guardian", "Hauptfenster", "LESEND / Windows Update COM", "Historie und belegte Neustartinformation, keine neue Updateorchestrierung.", "update", QuickAction: "Update Intelligence"),
        new("Guardian", "Treiberstatus öffnen", "Guardian", "Hauptfenster", "LESEND / Geräteidentität und Treiberzustand", "Keine Treiberinstallation oder erfundener Rückweg.", "driver treiber", QuickAction: "Driver Guard"),
        new("Guardian", "Absturz-/Hangdiagnose öffnen", "Guardian", "Hauptfenster", "LESEND / qualifizierter Fehlerfall", "Ereignis, beobachteten Kontext und zeitliche Nähe getrennt beurteilen.", "crash hang", QuickAction: "Crash Lab"),
        new("Guardian", "ETW-Tiefendiagnose öffnen", "Guardian", "Hauptfenster", "OPT-IN / sensible lokale ETL", "Profil, Dauer und Datenschutz prüfen; Produktionsaufnahme bleibt gesperrt.", "etw trace", QuickAction: "Deep Trace"),
        new("Guardian", "Rückwegplanung öffnen", "Guardian", "Hauptfenster", "LESEND / Rückwege separat bestätigen", "Rückweg ist eine neue identitätsgeprüfte Transaktion.", "rollback", QuickAction: "Rollback Center"),
        new("Guardian", "Wartungsablauf öffnen", "Guardian", "Hauptfenster", "LOKAL / unterbrechbarer manueller Ablauf", "Nach Neustart Kontext und Realität erneut lesen; nichts automatisch wiederholen.", "maintenance wartung", QuickAction: "Maintenance Window"),
        new("Guardian", "Wiederherstellungs- und Supportpakete öffnen", "Guardian", "Hauptfenster", "DATEI / Vorschau und Datenschutzfreigabe", "Private DPAPI-Sicherung oder streng redigiertes Supportpaket.", "rescue support package", QuickAction: "Rescue Package"),
        new("System Intelligence", "Systemveränderungen verstehen", "SystemIntelligence", "Hauptfenster", "LESEND / zeitbezogene Evidenz",
            "Timeline, Inventare, Baselines und belegte Beziehungen gemeinsam prüfen. Zeitnachbarn sind keine Ursachen.", "timeline software treiber driver boot startup reliability compare map"),
        new("System Intelligence", "Warum ist mein PC gerade langsam?", "SystemIntelligence", "Hauptfenster", "LESEND / gemeinsame Ressourcenprobe",
            "Größte beobachtete Beiträge mit Quellenzeit lesen. Keine automatische Ursachenbehauptung.", "langsam slow cpu ram io diagnose", QuickAction: "Diagnose"),
        new("System Intelligence", "Diagnose aufzeichnen", "SystemIntelligence", "Hauptfenster", "LOKAL / explizit starten und stoppen",
            "Profil und Dauer wählen; bestehende Proben verwenden. Keine ETW-Sitzung und kein Paketmitschnitt.", "recorder recording aufzeichnung", QuickAction: "Recorder"),
        new("System Intelligence", "System Watch einrichten", "SystemIntelligence", "Hauptfenster", "LOKAL / sichere Beobachtungsregeln",
            "Auslöser, Schwelle, Dauer und Cooldown festlegen. Nur Hinweis, Timeline, Snapshot oder Diagnosemarker.", "watch regel überwachen", QuickAction: "Watch"),
        new("Prozesse", "Prozesse anzeigen", "Taskmanager", "Hauptfenster", "LESEND / gemeinsame Windows-Prozessprobe",
            "Prozessliste oder Baum öffnen. PID und Startzeit bilden die Identität. Kontextmenüaktionen prüfen das Ziel erneut.", "prozess prozessbaum process"),
        new("Prozesse", "Prozess suchen", "Taskmanager", "Hauptfenster", "LESEND / Suchfeld fokussieren",
            "Name, PID oder Zustand eingeben. Keine Prozessaktion wird durch die Suche ausgeführt.", "suchen filter pid", QuickAction: "process-search"),
        new("Prozesse", "Hardwarebericht erstellen", "HardwareIntelligence", "Hauptfenster", "DATEI / vorhandene Hardwareklassen exportieren",
            "Zuerst gewünschte Klassen lesen. Export mit Datenschutzdialog aufrufen; keine automatische vollständige Hardwareerfassung.", "hardware export quelle bericht", QuickAction: "hardware-report"),
        new("Prozesse", "Netzwerkverbindungen anzeigen", "Taskmanager", "Hauptfenster", "LESEND / Owner-PID-Endpunkte",
            "TCP- und UDP-Endpunkte auf Anforderung lesen. Owner-PID ist zeitbezogen, keine Datenrate und keine dauerhafte Prozessidentität.", "socket tcp udp listener ports", QuickAction: "connections"),
        new("Prozesse", "Dienste anzeigen", "Superadmin", "Hauptfenster", "LESEND / Dienstbereich öffnen",
            "Dienstbereich öffnen und Status bewusst laden. Änderungen benötigen weiterhin Vorschau und Bestätigung.", "service dienst starttyp", QuickAction: "services"),
        new("Prozesse", "Systemsnapshot aufnehmen", "Taskmanager", "Hauptfenster", "LESEND / lokaler Sitzungssnapshot",
            "Gemeinsame Prozessprobe, Dienste, Autostarts und Verbindungen mit eigenen Quellenzeiten aufnehmen. Nicht atomar; Export separat.", "snapshot zustand vergleich", QuickAction: "snapshot"),
        new("Prozesse", "Snapshots vergleichen", "Taskmanager", "Hauptfenster", "LESEND / zwei lokale Zustände",
            "Zwei Snapshots aufnehmen oder lokal laden. Veränderungen nach Identität vergleichen; keine Sicherheitsdiagnose daraus ableiten.", "compare differenz starttyp", QuickAction: "compare"),
        new("Prozesse", "Dateispeicherort öffnen", "Taskmanager", "Hauptfenster", "LOKAL / ausgewählte Prozessdatei",
            "Zuerst einen Prozess auswählen. Identität und Pfad werden erneut gelesen; Explorer markiert die Datei.", "datei explorer pfad", QuickAction: "file-location"),
        new("Diagnose", "Windows-Probleme eingrenzen", "Diagnose", "Hauptfenster", "LESEND / vorhandenen Status ansehen",
            "1. Zeitpunkt und Symptom notieren.\n2. Diagnose und Ereignisse prüfen.\n3. Bericht mit Schwärzung exportieren.\n4. Erst danach passende Reparatur einzeln freigeben.", "fehler support bericht systemstatus"),
        new("Diagnose", "Abstürze und Hardwarefehler", "Ereignisse", "Hauptfenster", "LESEND / Windows-Ereignisse",
            "Kanal und Zeitraum eingrenzen. Ereignisdetails und Zeitbezug lesen. Kernel-Power ist allein kein Nachweis für ein defektes Netzteil.", "crash whea bugcheck wer bluescreen"),
        new("Diagnose", "Windows gezielt reparieren", "Wartung", "Systemkontrolle", "ÄNDERND / erst Auswahl und Bestätigung",
            "Erst Diagnose lesen und Daten sichern. Geführte SFC-/DISM-Aktion auswählen; Warnung und exakten Befehl prüfen. Ergebnis/CBS.log lesen; Exit 0 ist kein Gesundheitsbeweis.", "sfc dism restorehealth reparatur"),
        new("Diagnose", "Wartungsplan und Windows-Updates", "Operations", "Hauptfenster", "GEMISCHT / Vorschau, dann Einzelbestätigungen",
            "Wartungsplan öffnen, Vorlage wählen und Aufgaben prüfen. Vor Änderungen Wiederherstellungspunkt vorsehen. Update-IDs und EULAs prüfen; kein automatischer Neustart.", "assistent windows update neustart restore point"),
        new("Diagnose", "Schutzstatus prüfen", "Sicherheit", "Hauptfenster", "LESEND / kein Schutz wird abgeschaltet",
            "Defender, Firewall, TPM, Secure Boot und BitLocker getrennt lesen. Nicht verfügbar bedeutet nicht sicher. AETHER ersetzt kein Sicherheitsprodukt.", "defender firewall tpm bitlocker secureboot"),
        new("Diagnose", "Hardware und Software inventarisieren", "Diagnose", "Hauptfenster", "LESEND / Inventar auf eigenen Klick",
            "Rechner-Inventar öffnen und lokal speichern. Seriennummern, Rechnernamen und Softwareliste vor Weitergabe prüfen.", "inventar export rechner hardware software"),
        new("Software", "Anwendungen und Updates verwalten", "Pakete", "Systemkontrolle", "NETZWERK / Änderungen nur bestätigt",
            "WinGet-Pakete oder Updates laden. Quelle, ID und Version prüfen; bei Bedarf Details lesen. Auswahl markieren und Vorschau bestätigen. Aufträge zeigen jedes Paketergebnis. Weitere Paketmanager bei Bedarf im selbst installierten UniGetUI öffnen; externe Aktionen bleiben außerhalb von AETHER.", "winget unigetui wingetui installieren deinstallieren scoop chocolatey"),
        new("Software", "Softwareliste übertragen", "Pakete", "Systemkontrolle", "DATEI / Import installiert noch nichts",
            "Quellengebundene Pakete als AETHER-Liste exportieren. Auf dem Ziel importieren, Quellen und IDs prüfen, dann Auswahl installieren. Versionen sind Hinweise, keine Versionssperren. Keine Installer oder Lizenzen werden mitgesichert.", "backup restore manifest import export paketliste"),
        new("Software", "Aufträge und Ergebnisse lesen", "Auftraege", "Systemkontrolle", "LESEND / flüchtiger Sitzungsverlauf",
            "Status je Auftrag bzw. Paket lesen. Fehlgeschlagen, abgebrochen und nicht gestartet bleiben unterscheidbar. Sensible Anzeigen und freie Konsolenbefehle werden nicht aufgenommen.", "queue warteschlange verlauf fehler exitcode"),
        new("Netzwerk", "Verbindungsprobleme eingrenzen", "Netzwerk", "Systemkontrolle", "NETZWERK / Tests nur nach Freigabe",
            "Erst Adapter/Gateway prüfen. Dann Router-Ping, Ziel-Ping und DNS getrennt bewerten. Speedtest erzeugt echten Datenverkehr. LAN-Scans nur im berechtigten Netz.", "internet dns ping wlan gateway speedtest"),
        new("Netzwerk", "Pakete filtern und untersuchen", "Netzwerkbeobachtung", "Desktop", "MITSCHNITT / separate Zustimmung erforderlich",
            "Schnittstelle bewusst wählen und Mitschnitt freigeben. Protokoll, Adresse und Port filtern; Einzelpaketdetails lesen. Filter gelten nur für maximal 200 Live-Zeilen, nicht für die PCAP-Datei. Warnungen sind Indizien, keine Angriffsnachweise.", "wireshark npcap tcp udp dns syn capture paket"),
        new("Netzwerk", "PCAP in Wireshark analysieren", "Netzwerkbeobachtung", "Desktop", "EXTERN / lokale Datei enthaelt sensible Daten",
            "Mitschnitt zuerst stoppen. Lokale PCAP/PCAPNG-Datei und selbst installiertes Wireshark auswählen. AETHER übergibt nur die Datei; kein Download, Upload oder automatischer Mitschnitt.", "pcap pcapng protokollanalyse wireshark export"),
        new("Monitoring", "Systemlast live beobachten", "Cockpit", "Hauptfenster", "LESEND / vorhandene Live-Messung",
            "CPU, RAM, GPU, Netz und Laufwerke zusammen betrachten. Erster Zählerpunkt kann Baseline sein. Fehlende Werte bleiben unbekannt; animierte Datenpfade sind symbolisch.", "dashboard last temperatur auslastung"),
        new("Monitoring", "Sensorverläufe untersuchen", "Sensoren", "Hauptfenster", "LESEND / echte Messkanäle",
            "Hardware oder Sensor suchen und Kurven einzeln einblenden. Einheiten nicht vermischen. Lücken bleiben Lücken; Temperaturgrenzen sind keine Messwerte.", "chart graph hotspot vram spannung fan"),
        new("Monitoring", "Messsitzung auswerten und exportieren", "Messauswertung", "Hauptfenster", "LESEND / CSV speichert nur auf Klick",
            "Sitzung neu beginnen, Problem reproduzieren, Statistik anhalten. Nach Hardware/Kanal filtern und sichtbare MIN/MAX/AVG als CSV exportieren. CSV ist Statistik, keine Rohdatenaufzeichnung.", "min max avg durchschnitt csv vergleich"),
        new("Monitoring", "Messlogs und Gaming vergleichen", "Logs / Gaming", "Systemkontrolle", "DATEI / Capture separat freigeben",
            "Vorhandene JSONL-Datei laden und zweite vergleichen. Für neue Rohdaten im Cockpit Aufzeichnung aktivieren. PresentMon nur für einen bewusst ausgewählten Prozess starten; kein synthetischer Belastungstest.", "history fps frametime presentmon logging gaming"),
        new("Monitoring", "Laufwerke und SMART diagnostizieren", "SMART", "Systemkontrolle", "GEMISCHT / Selbsttests nur bestätigt",
            "Geräte erkennen, Diagnose/Fähigkeiten lesen. SMART-Warnungen genau prüfen und wichtige Daten sichern. Selbsttests erzeugen Last und laufen im Gerät weiter. Keine errechnete Restlebenszeit.", "nvme ssd hdd smart fehler wear gesundheit"),
        new("Monitoring", "Mainboard und Komponenten ansehen", "Mainboard", "Hauptfenster", "LESEND / schematische Darstellung",
            "Bauteile für Details anklicken. Live-Status und reale Werte getrennt von symbolischen Lüftern und Datenpfaden lesen. Hardware neu lesen aktualisiert das Inventar.", "platine synthwave board retro"),
        new("Monitoring", "Desktop-Gadgets und Transparenz", "Desktop-Einstellungen", "Desktop", "EINSTELLUNG / kein Systemtuning",
            "Diagrammgruppen, Deckkraft und Positionen festlegen. Tray-Symbol verwaltet Gadgets und App-Lebenszyklus. Autostart nur bei ausdrücklicher Freigabe.", "xbar sidebar gadgets transparenz"),
        new("Bereinigung", "Speicherplatz kontrolliert freigeben", "Cleaner", "Hauptfenster", "LÖSCHEND / Vorschau und Bestätigung",
            "Kategorie wählen und Vorschau lesen. Pfadfilter verwenden; nur sichtbare Kandidaten gezielt ankreuzen. Versteckte angekreuzte Dateien bleiben in der Gesamtzahl und Löschbestätigung enthalten. Löschen ist endgültig, kein Papierkorb.", "cache temp cleanup speicherplatz"),
        new("Bereinigung", "Grosse Dateien finden", "Speicheranalyse", "Desktop", "LESEND / nur selbst gewählter Ordner",
            "Ordner und Mindestgröße wählen. Maximal 100.000 Einträge, 32 Ebenen und 1.000 größte Treffer. Grenzen und Lesehinweise prüfen. CSV speichert Dateipfade; vor Weitergabe prüfen. Keine Löschfunktion und kein vollständiger Belegungsplan.", "speicherfresser grosse dateien storage"),
        new("Bereinigung", "Autostart nachvollziehbar verwalten", "Autostart", "Systemkontrolle", "ÄNDERND / mit lokaler Sicherung",
            "Run-Einträge einlesen und nach Name/Befehl suchen. Vollständigen Befehl und HKCU/HKLM prüfen. Nur bekannten Eintrag deaktivieren; Aktivieren verwendet die Sicherung und verweigert Konflikte.", "startup boot starten rücknahme"),
        new("Bereinigung", "Prozesse, Dienste und Aufgaben", "Superadmin", "Hauptfenster", "ÄNDERND / gezielte Verwaltung",
            "Erst aktuellen Zustand lesen. Einzelne, bekannte Änderung wählen, Vorschau und Auswirkung prüfen. Keine pauschale Dienstabschaltung oder angebliche RAM-Beschleunigung.", "taskmanager service prozess verwaltung"),
        new("Hilfe", "Grenzen, Einstellungen und Hilfe", "Hilfe", "Hauptfenster", "LESEND / lokale Anleitung",
            "Nach Modul oder Stichwort suchen. Sensorlücken, Datenschutz und Rücknahmehinweise beachten. Kein Registry-Cleaner, Schutzabschalten, pauschales Debloating oder Leistungsversprechen.", "tuning anleitung hilfe sicherheit"),
        new("Hilfe", "Mess- und Werkzeug-Einstellungen", "Einstellungen", "Systemkontrolle", "EINSTELLUNG / Pfade und Schwellen",
            "Werkzeugpfade und Messintervalle prüfen. Warnschwellen sind eigene Grenzwerte, keine Sensorwerte. Fehlende externe Programme werden nicht automatisch nachinstalliert.", "konfiguration smartctl presentmon schwellen")
    ];

    public static IReadOnlyList<CockpitTask> All { get; } = Workflows.Concat(AdminCommandCatalog.All.Select(command =>
        new CockpitTask("Befehlskatalog", command.Command, "Wartung", "Systemkontrolle", command.RiskBadge,
            command.Description + "\n\n" + command.Impact + "\n\n" +
            "Öffnet nur den passenden Katalogeintrag. Keine automatische Ausführung. Expertenbefehle bleiben im Einsteigermodus gesperrt.\n" +
            AdminCommandCatalog.Guidance(command.Category).Caution, command.SearchText, command.Command))).ToArray();

    public static IReadOnlyList<CockpitTask> Search(string query, string? category = null)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return All.Where(t => (category is null || t.Category == category) &&
            terms.All(term => t.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
}
