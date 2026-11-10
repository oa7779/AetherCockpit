namespace SystemCockpit.Services;

public sealed record CockpitTask(string Category, string Title, string Destination, string Surface,
    string Effect, string Steps, string Keywords, string? Command = null, string? QuickAction = null)
{
    private bool English => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
    private TaskTranslation? Translation => English && Command is null
        ? QuickAction?.StartsWith("process-id:", StringComparison.Ordinal) == true
            ? new(Title, "READ ONLY / existing process sample", "Select the process. Actions revalidate its identity.", "process pid")
            : TaskCatalogEnglish.Get(Title)
        : null;
    public string CategoryDisplay => English ? TaskCatalogEnglish.Label(Category) : Category;
    public string DestinationDisplay => English ? TaskCatalogEnglish.Label(Destination) : Destination;
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
            "1. Zeitpunkt und Symptom notieren.\n2. Diagnose und Ereignisse pruefen.\n3. Bericht mit Schwaerzung exportieren.\n4. Erst danach passende Reparatur einzeln freigeben.", "fehler support bericht systemstatus"),
        new("Diagnose", "Abstuerze und Hardwarefehler", "Ereignisse", "Hauptfenster", "LESEND / Windows-Ereignisse",
            "Kanal und Zeitraum eingrenzen. Ereignisdetails und Zeitbezug lesen. Kernel-Power ist allein kein Nachweis fuer ein defektes Netzteil.", "crash whea bugcheck wer bluescreen"),
        new("Diagnose", "Windows gezielt reparieren", "Wartung", "Systemkontrolle", "AENDERND / erst Auswahl und Bestaetigung",
            "Erst Diagnose lesen und Daten sichern. Gefuehrte SFC-/DISM-Aktion auswaehlen; Warnung und exakten Befehl pruefen. Ergebnis/CBS.log lesen; Exit 0 ist kein Gesundheitsbeweis.", "sfc dism restorehealth reparatur"),
        new("Diagnose", "Wartungsplan und Windows-Updates", "Operations", "Hauptfenster", "GEMISCHT / Vorschau, dann Einzelbestaetigungen",
            "Wartungsplan oeffnen, Vorlage waehlen und Aufgaben pruefen. Vor Aenderungen Wiederherstellungspunkt vorsehen. Update-IDs und EULAs pruefen; kein automatischer Neustart.", "assistent windows update neustart restore point"),
        new("Diagnose", "Schutzstatus pruefen", "Sicherheit", "Hauptfenster", "LESEND / kein Schutz wird abgeschaltet",
            "Defender, Firewall, TPM, Secure Boot und BitLocker getrennt lesen. Nicht verfuegbar bedeutet nicht sicher. AETHER ersetzt kein Sicherheitsprodukt.", "defender firewall tpm bitlocker secureboot"),
        new("Diagnose", "Hardware und Software inventarisieren", "Diagnose", "Hauptfenster", "LESEND / Inventar auf eigenen Klick",
            "Rechner-Inventar oeffnen und lokal speichern. Seriennummern, Rechnernamen und Softwareliste vor Weitergabe pruefen.", "inventar export rechner hardware software"),
        new("Software", "Anwendungen und Updates verwalten", "Pakete", "Systemkontrolle", "NETZWERK / Aenderungen nur bestaetigt",
            "WinGet-Pakete oder Updates laden. Quelle, ID und Version pruefen; bei Bedarf Details lesen. Auswahl markieren und Vorschau bestaetigen. Auftraege zeigen jedes Paketergebnis. Weitere Paketmanager bei Bedarf im selbst installierten UniGetUI oeffnen; externe Aktionen bleiben ausserhalb von AETHER.", "winget unigetui wingetui installieren deinstallieren scoop chocolatey"),
        new("Software", "Softwareliste uebertragen", "Pakete", "Systemkontrolle", "DATEI / Import installiert noch nichts",
            "Quellengebundene Pakete als AETHER-Liste exportieren. Auf dem Ziel importieren, Quellen und IDs pruefen, dann Auswahl installieren. Versionen sind Hinweise, keine Versionssperren. Keine Installer oder Lizenzen werden mitgesichert.", "backup restore manifest import export paketliste"),
        new("Software", "Auftraege und Ergebnisse lesen", "Auftraege", "Systemkontrolle", "LESEND / fluechtiger Sitzungsverlauf",
            "Status je Auftrag bzw. Paket lesen. Fehlgeschlagen, abgebrochen und nicht gestartet bleiben unterscheidbar. Sensible Anzeigen und freie Konsolenbefehle werden nicht aufgenommen.", "queue warteschlange verlauf fehler exitcode"),
        new("Netzwerk", "Verbindungsprobleme eingrenzen", "Netzwerk", "Systemkontrolle", "NETZWERK / Tests nur nach Freigabe",
            "Erst Adapter/Gateway pruefen. Dann Router-Ping, Ziel-Ping und DNS getrennt bewerten. Speedtest erzeugt echten Datenverkehr. LAN-Scans nur im berechtigten Netz.", "internet dns ping wlan gateway speedtest"),
        new("Netzwerk", "Pakete filtern und untersuchen", "Netzwerkbeobachtung", "Desktop", "MITSCHNITT / separate Zustimmung erforderlich",
            "Schnittstelle bewusst waehlen und Mitschnitt freigeben. Protokoll, Adresse und Port filtern; Einzelpaketdetails lesen. Filter gelten nur fuer maximal 200 Live-Zeilen, nicht fuer die PCAP-Datei. Warnungen sind Indizien, keine Angriffsnachweise.", "wireshark npcap tcp udp dns syn capture paket"),
        new("Netzwerk", "PCAP in Wireshark analysieren", "Netzwerkbeobachtung", "Desktop", "EXTERN / lokale Datei enthaelt sensible Daten",
            "Mitschnitt zuerst stoppen. Lokale PCAP/PCAPNG-Datei und selbst installiertes Wireshark auswaehlen. AETHER uebergibt nur die Datei; kein Download, Upload oder automatischer Mitschnitt.", "pcap pcapng protokollanalyse wireshark export"),
        new("Monitoring", "Systemlast live beobachten", "Cockpit", "Hauptfenster", "LESEND / vorhandene Live-Messung",
            "CPU, RAM, GPU, Netz und Laufwerke zusammen betrachten. Erster Zaehlerpunkt kann Baseline sein. Fehlende Werte bleiben unbekannt; animierte Datenpfade sind symbolisch.", "dashboard last temperatur auslastung"),
        new("Monitoring", "Sensorverlaeufe untersuchen", "Sensoren", "Hauptfenster", "LESEND / echte Messkanaele",
            "Hardware oder Sensor suchen und Kurven einzeln einblenden. Einheiten nicht vermischen. Luecken bleiben Luecken; Temperaturgrenzen sind keine Messwerte.", "chart graph hotspot vram spannung fan"),
        new("Monitoring", "Messsitzung auswerten und exportieren", "Messauswertung", "Hauptfenster", "LESEND / CSV speichert nur auf Klick",
            "Sitzung neu beginnen, Problem reproduzieren, Statistik anhalten. Nach Hardware/Kanal filtern und sichtbare MIN/MAX/AVG als CSV exportieren. CSV ist Statistik, keine Rohdatenaufzeichnung.", "min max avg durchschnitt csv vergleich"),
        new("Monitoring", "Messlogs und Gaming vergleichen", "Logs / Gaming", "Systemkontrolle", "DATEI / Capture separat freigeben",
            "Vorhandene JSONL-Datei laden und zweite vergleichen. Fuer neue Rohdaten im Cockpit Aufzeichnung aktivieren. PresentMon nur fuer einen bewusst ausgewaehlten Prozess starten; kein synthetischer Belastungstest.", "history fps frametime presentmon logging gaming"),
        new("Monitoring", "Laufwerke und SMART diagnostizieren", "SMART", "Systemkontrolle", "GEMISCHT / Selbsttests nur bestaetigt",
            "Geraete erkennen, Diagnose/Faehigkeiten lesen. SMART-Warnungen genau pruefen und wichtige Daten sichern. Selbsttests erzeugen Last und laufen im Geraet weiter. Keine errechnete Restlebenszeit.", "nvme ssd hdd smart fehler wear gesundheit"),
        new("Monitoring", "Mainboard und Komponenten ansehen", "Mainboard", "Hauptfenster", "LESEND / schematische Darstellung",
            "Bauteile fuer Details anklicken. Live-Status und reale Werte getrennt von symbolischen Lueftern und Datenpfaden lesen. Hardware neu lesen aktualisiert das Inventar.", "platine synthwave board retro"),
        new("Monitoring", "Desktop-Gadgets und Transparenz", "Desktop-Einstellungen", "Desktop", "EINSTELLUNG / kein Systemtuning",
            "Diagrammgruppen, Deckkraft und Positionen festlegen. Tray-Symbol verwaltet Gadgets und App-Lebenszyklus. Autostart nur bei ausdruecklicher Freigabe.", "xbar sidebar gadgets transparenz"),
        new("Bereinigung", "Speicherplatz kontrolliert freigeben", "Cleaner", "Hauptfenster", "LOESCHEND / Vorschau und Bestaetigung",
            "Kategorie waehlen und Vorschau lesen. Pfadfilter verwenden; nur sichtbare Kandidaten gezielt ankreuzen. Versteckte angekreuzte Dateien bleiben in der Gesamtzahl und Loeschbestaetigung enthalten. Loeschen ist endgueltig, kein Papierkorb.", "cache temp cleanup speicherplatz"),
        new("Bereinigung", "Grosse Dateien finden", "Speicheranalyse", "Desktop", "LESEND / nur selbst gewaehlter Ordner",
            "Ordner und Mindestgroesse waehlen. Maximal 100.000 Eintraege, 32 Ebenen und 1.000 groesste Treffer. Grenzen und Lesehinweise pruefen. CSV speichert Dateipfade; vor Weitergabe pruefen. Keine Loeschfunktion und kein vollstaendiger Belegungsplan.", "speicherfresser grosse dateien storage"),
        new("Bereinigung", "Autostart nachvollziehbar verwalten", "Autostart", "Systemkontrolle", "AENDERND / mit lokaler Sicherung",
            "Run-Eintraege einlesen und nach Name/Befehl suchen. Vollstaendigen Befehl und HKCU/HKLM pruefen. Nur bekannten Eintrag deaktivieren; Aktivieren verwendet die Sicherung und verweigert Konflikte.", "startup boot starten ruecknahme"),
        new("Bereinigung", "Prozesse, Dienste und Aufgaben", "Superadmin", "Hauptfenster", "AENDERND / gezielte Verwaltung",
            "Erst aktuellen Zustand lesen. Einzelne, bekannte Aenderung waehlen, Vorschau und Auswirkung pruefen. Keine pauschale Dienstabschaltung oder angebliche RAM-Beschleunigung.", "taskmanager service prozess verwaltung"),
        new("Hilfe", "Grenzen, Einstellungen und Hilfe", "Hilfe", "Hauptfenster", "LESEND / lokale Anleitung",
            "Nach Modul oder Stichwort suchen. Sensorluecken, Datenschutz und Ruecknahmehinweise beachten. Kein Registry-Cleaner, Schutzabschalten, pauschales Debloating oder Leistungsversprechen.", "tuning anleitung hilfe sicherheit"),
        new("Hilfe", "Mess- und Werkzeug-Einstellungen", "Einstellungen", "Systemkontrolle", "EINSTELLUNG / Pfade und Schwellen",
            "Werkzeugpfade und Messintervalle pruefen. Warnschwellen sind eigene Grenzwerte, keine Sensorwerte. Fehlende externe Programme werden nicht automatisch nachinstalliert.", "konfiguration smartctl presentmon schwellen")
    ];

    public static IReadOnlyList<CockpitTask> All { get; } = Workflows.Concat(AdminCommandCatalog.All.Select(command =>
        new CockpitTask("Befehlskatalog", command.Command, "Wartung", "Systemkontrolle", command.RiskBadge,
            command.Description + "\n\n" + command.Impact + "\n\n" +
            "Oeffnet nur den passenden Katalogeintrag. Keine automatische Ausfuehrung. Expertenbefehle bleiben im Einsteigermodus gesperrt.\n" +
            AdminCommandCatalog.Guidance(command.Category).Caution, command.SearchText, command.Command))).ToArray();

    public static IReadOnlyList<CockpitTask> Search(string query, string? category = null)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return All.Where(t => (category is null || t.Category == category) &&
            terms.All(term => t.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
}
