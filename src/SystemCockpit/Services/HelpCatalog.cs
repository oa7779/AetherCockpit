namespace SystemCockpit.Services;

public sealed record HelpTopic(string Module, string Title, string Body, string Keywords)
{
    public string SearchText => $"{Module} {Title} {Body} {Keywords}";
}

/// <summary>Lokale Modulhilfe mit Bedienabläufen und relevanten Grenzen.</summary>
public static class HelpCatalog
{
    public static IReadOnlyList<HelpTopic> All { get; } = Build();

    public static List<HelpTopic> Search(string query) => All
        .Where(t => t.SearchText.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    private static IReadOnlyList<HelpTopic> Build() =>
    [
        new("Arbeitsplatz", "Funktion finden / Strg+K",
            "FUNKTION FINDEN oder Strg+K öffnet die Arbeitsplatzsuche. Arbeitsbereich und Suchbegriffe kombinieren, " +
            "Wirkung und Schritte lesen und den passenden Bereich öffnen. Die Suche startet keinen Auftrag und schaltet den Expertenmodus nicht ein.\n" +
            "AUSGABE EIN/AUS schafft Platz für Tabellen. Aufträge zeigt bis zu 200 Einträge dieser Fenstersitzung im RAM; " +
            "freie Befehle und sensible Anzeigen bleiben ausgenommen. Wartungsberichte und Aktionsjournal werden getrennt geführt.",
            "suche arbeitsplatz navigation auftraege warteschlange professionell"),
        new("Pakete", "Quellen, Paketlisten und Aufträge",
            "WinGet-Pakete oder Updates laden und exakte ID und Quelle prüfen. Die Bestätigung zeigt alle Ziele; Pakete laufen danach nacheinander. " +
            "Einzelfehler, Abbruch und nicht gestartete Schritte stehen unter Aufträge. Nach dem Lauf die Liste neu laden.\n" +
            "Export verwendet Auswahl oder geladene Liste mit vollständigen IDs und Quellen; ausgeschlossene Einträge werden genannt. " +
            "Import liest das AETHER-Format mit höchstens 100 Paketen und 128 KiB als Wunschliste. Er installiert nichts. " +
            "Versionen sind Hinweise, keine Pins; Quellen müssen eingerichtet sein.\n" +
            "Ein installiertes UniGetUI/WingetUI lässt sich separat öffnen. Seine Einstellungen, Freigaben und Aktionen bleiben außerhalb von AETHER.",
            "winget unigetui wingetui scoop chocolatey export import paketliste details quelle"),
        new("Netzwerk", "Live-Filter und PCAP-Übergabe",
            "Live-Analyse, Erfassung und Warnungen sind getrennt. Text, Protokoll, Port, SYN und DNS-Fehler gemeinsam filtern. " +
            "Bei ungültigem Filter bleibt der bisherige aktiv. Die Ansicht enthält höchstens 200 Pakete, keine Gesamtstatistik.\n" +
            "CSV speichert sichtbare Metadaten einschließlich IP-Adressen. Die PCAP-Datei bleibt ungefiltert. " +
            "Mitschnitt zuerst stoppen, dann die lokale Datei nach Bestätigung an installiertes Wireshark übergeben. " +
            "128 aufgenommene Bytes erlauben keine vollständige Protokollanalyse oder TLS-Entschlüsselung.",
            "pcap pcapng wireshark filter tcp udp dns syn metadata"),
        new("Cleaner", "Große Dateien finden und Auswahl filtern",
            "Ordner und Mindestgröße wählen, dann den lesenden Scan starten. Höchstens 100.000 Einträge, 32 Ebenen und 1000 größte Treffer; " +
            "Links/Junctions werden nicht verfolgt. Fehler und Limits stehen im Ergebnis. CSV enthält Dateipfade; Explorer markieren öffnet nicht die Datei.\n" +
            "Der Cache-Cleaner ist ein eigener Ablauf. Sein Pfadfilter ändert nur die Ansicht: versteckte angekreuzte Dateien bleiben ausgewählt. " +
            "Keine ankreuzen leert alles. Erst Vorschau und Gesamtbestätigung erlauben endgültiges Löschen ohne Papierkorb.",
            "speicherfresser grosse dateien filter cleanup temp autostart"),
        new("Mainboard", "Virtuelles Mainboard / 1986",
            "Cockpit -> Mainboard / 1986 liest Windows-PnP-Geräte und RAM-Bänke. HARDWARE NEU LESEN aktualisiert die Momentaufnahme. " +
            "Bauteil oder Beschriftung anklicken zeigt die erfassten Geräte mit Status und verfügbarer Eltern-ID.\n" +
            "Die Platine mit CPU-Kühler, RAM, GPU und Anschlüssen ist schematisch. Datenpfade und Lüftertempo sind dekorativ, " +
            "keine elektrische Topologie oder Drehzahlmessung. Laufwerke erscheinen einzeln; weitere Schnittstellen bleiben in Gruppendetails.\n" +
            "Instrumente wechseln etwa alle sechs Sekunden durch zuordenbare Messkanäle und nennen Hardware und Einheit. " +
            "Skalen sind Anzeigereferenzen, Gruppenmaxima gekennzeichnet; fehlende Werte bleiben --. Der Internetzustand stammt von Windows.\n" +
            "Der Animationsschalter steuert Datenfluss, Lüfter, Raster und Binärberge. Monitorabschaltung und Pixelpflege weiter nutzen; " +
            "Bewegung allein verhindert OLED-Einbrennen nicht verlässlich.",
            "mainboard motherboard platine retro 1986 hardware bus usb sata ram gpu controller schaubild luefter binaer ssd"),
        new("Mainboard", "Bildschirmschoner / AetherMainboard.scr",
            "Im Setup den optionalen Schoner installieren und in der Windows-Liste AETHER Mainframe 1986 auswählen. " +
            "Wartezeit und Anmeldeseite bei Reaktivierung dort festlegen. Portable SCR, DLLs und Laufzeit zusammen behalten.\n" +
            "/s startet Vollbild, /c zeigt einen Hinweis auf fehlende eigene Einstellungen, /p <Handle> die eingebettete Vorschau. " +
            "Maus, Tastatur oder Monitorwechsel beendet Vollbild. " +
            "Jeder Monitor zeigt die vollständige Szene; nur eindeutig interne Laufwerke erscheinen auf der Schonerplatine.\n" +
            "Der Schoner läuft ohne UAC und initialisiert keine Sensoren. Live-Werte kommen lokal aus AETHER im selben Konto und derselben Sitzung. " +
            "Ohne Verbindung zeigt er eine Hardware-Momentaufnahme mit KEINE LIVE-MESSWERTE.\n" +
            "Der Bildschirmschoner verwendet und verwaltet keine Musikdateien. Windows-Sperre und Pixelpflege bleiben zuständig.",
            "bildschirmschoner screensaver scr mainboard monitor multimonitor vorschau"),
        new("Erste Schritte", "AETHER vollständig deinstallieren",
            "Benötigte Daten vorher außerhalb der AETHER-Ordner sichern. Alte Installationen zuerst mit aktuellem Setup aktualisieren, " +
            "dann unter Installierte Apps deinstallieren. Ein erkannter Restordner kann nach Bestätigung wiederhergestellt werden.\n" +
            "Der Uninstaller entfernt Autostart und Prozesse dieser Installation, den Installationsordner und %LOCALAPPDATA%\\AetherCockpit " +
            "des ausführenden Kontos einschließlich Einstellungen, Logs, Mitschnitten und Backups. Andere Profile und externe Dateien bleiben bestehen. " +
            "Portable Kopien behalten ihre Programmordner, teilen aber die entfernten Kontodaten. Ein Update erhält die Daten.",
            "deinstallation uninstall entfernen loeschen daten backup"),
        new("Erste Schritte", "Erststart: EULA und Rechner-Inventar",
            "Beim ersten Start führt der Assistent durch Funktionsübersicht, EULA und Rechner-Inventar. " +
            "Ohne Zustimmung beendet sich AETHER; die Zustimmung wird im ausführenden Konto lokal gespeichert.\n" +
            "Das Inventar liest Windows-Hardwaredaten und installierte Software aus der Registry. Es kann als Text gespeichert " +
            "und später unter DIAGNOSE -> RECHNER-INVENTAR erneut geöffnet werden. Namen und Seriennummern vor Weitergabe prüfen.",
            "erststart eula lizenz zustimmung inventar inventarisierung hardware software assistent"),
        new("Erste Schritte", "AETHER in zwei Minuten",
            "1. Mit UAC starten; AETHER initialisiert die Hardware-Sensoren.\n" +
            "2. Im Cockpit Live-Werte, unter Taskmanager Prozesse und CPU-Kerne ansehen.\n" +
            "3. Sensoren, Laufwerke und Messauswertung für Details öffnen.\n" +
            "4. Diagnose, Sicherheit und Ereignisse für Windows-Befunde nutzen.\n" +
            "5. Änderungen in Cleaner, Superadmin oder Operations erst nach Vorschau freigeben.\n" +
            "Ausnahme: Die freie Expertenkonsole startet eingegebene Befehle mit Enter direkt.",
            "start einfuehrung ueberblick grundlagen uac administrator"),
        new("Erste Schritte", "Desktop-Gadgets, Transparenz und Tray",
            "Das Tray-Symbol öffnet Desktop / Transparenz / Parameter. Diagrammgruppen wählen und Hauptfenster, xBar und Gadgets " +
            "getrennt auf 20–100 Prozent Deckkraft einstellen. Gadgets am Titel verschieben; sie liegen hinter normalen Fenstern auf der Explorer-Ebene.\n" +
            "X blendet das Hauptfenster nur aus. Im Tray Gadgets minimieren, wiederherstellen oder schließen; " +
            "AETHER vollständig beenden beendet auch die Hintergrundarbeit. Anmeldestart verlangt eine genehmigte erhöhte Aufgabe " +
            "und zuvor manuell bestätigte EULA.",
            "desktop gadget transparenz deckkraft opacity tray autostart taskleiste uhr minimieren"),
        new("Netzwerk", "Paketmitschnitt und Warnungen",
            "Tray -> Netzwerküberwachung. TCP-Verbindungszählung ist getrennt aktivierbar. Mitschnitt benötigt separat " +
            "installiertes und lizenziertes Npcap; AETHER bietet nur die offizielle Downloadseite an.\n" +
            "Ethernet-kompatible Schnittstelle wählen und Aufnahme bestätigen. Höchstens 128 Bytes je Paket, " +
            "100.000.000 Bytes (100 MB) je PCAP-Datei und 15 Minuten. Eigene Captures werden nach sieben Tagen beim nächsten Aufbewahrungslauf entfernt.\n" +
            "SYN-Port-, DNS-Fehler- und TCP-Schwellen erzeugen Tray-Hinweise und lokale Logs, keine Blockierung. " +
            "Auch gekürzte Pakete können vertrauliche Daten enthalten. Automatischer Anmeldemitschnitt braucht eine eigene Zustimmung und Autostart.",
            "netzwerk npcap wireshark pcap paketmitschnitt capture dns tcp syn alarm datenschutz"),
        new("Erste Schritte", "Skins: Retro-Neon, Aero, Candy, Fluent",
            "Systemkontrolle -> EINSTELLUNGEN bietet Retro-Neon, Vista Aero Glas, Candy Glossy und Windows 11 26H2 Fluent. " +
            "Die Namen bezeichnen Darstellungsvarianten, keine Windows-Funktions- oder Kompatibilitätszusage.\n" +
            "Skins ändern Hintergründe, Panels, Leisten und Knöpfe; Neon-Akzente bleiben erhalten. " +
            "Die Auswahl wirkt sofort auf offene Fenster und wird gespeichert. Der Erststart-Assistent nutzt immer das Retro-Neon-Original.",
            "skin theme design aero vista candy glossy fluent windows 11 oberflaeche optik"),
        new("Erste Schritte", "Navigation: Zurück, Vor, Hauptebene",
            "Die Navigationsleiste folgt dem lokalen Besuchsverlauf: ZURÜCK, VOR, COCKPIT und HILFE. " +
            "Maus-Daumentasten und Alt+Pfeil links/rechts führen dieselben Schritte aus.\n" +
            "Die erweiterte Systemkontrolle hat einen eigenen Modulverlauf und einen HAUPTFENSTER-Knopf. " +
            "Die Positionszeile zeigt den aktuellen Bereich. Bis zu 100 Besuche bleiben im RAM; ein neuer Besuch verwirft den Vorwärtszweig.",
            "navigation zurueck vor verlauf maus daumentaste alt pfeil brotkrumen hauptebene"),
        new("Erste Schritte", "Einsteiger- und Expertenmodus",
            "Unter WARTUNG schaltet EXPERTENMODUS den freien Konsolenstart und vollständigen Katalog frei. " +
            "Enter führt dort auch ändernde Befehle sofort aus. Experte ist die gespeicherte Standardvorgabe.\n" +
            "Im Einsteigermodus zeigt der Katalog nur lesende Befehle; der freie Start ist gesperrt. " +
            "Geführte Aktionen behalten in beiden Modi ihre Bestätigungen. Der Modus ist keine zusätzliche Windows-Rechtegrenze.",
            "einsteiger experte modus sicherheit sperre konsole risiko"),
        new("Erste Schritte", "Risikoanzeige verstehen",
            "Die Katalogkennzeichnung beschreibt die Wirkung: LESEND in Grün liest Zustände, NETZWERK in Gelb kann Ziele kontaktieren, " +
            "ÄNDERND in Orange verändert das System bei Ausführung.\n" +
            "Externe Netzwerkziele können deine öffentliche IP-Adresse sehen.\n" +
            "SINNVOLL und VORSICHT erklären Anlass und Grenzen. Vor dem Start Ziel, Parameter und Wirkung lesen. " +
            "Die Einstufung ist kein Gesamturteil über die Sicherheit des Rechners.",
            "risiko stufen farben lesend aendernd netzwerk badge"),
        new("Cockpit", "Live-Instrumente und AETHER xBar",
            "Live-Werte werden ungefähr alle zwei Sekunden gelesen; Cockpitkurven halten 90 Punkte im RAM. " +
            "Die xBar zeigt sechs wählbare Gruppen: CPU, RAM, GPU, Temperaturen, Netzwerk und Laufwerksbelegung.\n" +
            "Im Zahnrad Monitor, Seite und PIN/FLOAT wählen. FLOAT speichert freie Positionen; Auto-hide gilt nur angeheftet am Einzelmonitor. " +
            "Die aktuelle xBar ist eine Diagrammleiste, kein Launcher. Fehlende Werte bleiben nicht verfügbar. " +
            "Exklusives Gaming-Vollbild kann xBar und Overlay verdecken.",
            "xbar sidebar diagramme graphen tortendiagramm belegt frei laufwerke widgets pin float monitor overlay gaming"),
        new("Taskmanager", "Prozesse und CPU-Kerne",
            "Die Prozesstabelle lässt sich durchsuchen und sortieren. CPU-Anteil ist auf die gesamte Maschine und das Messintervall bezogen; " +
            "alle logischen Kerne besitzen eigene Graphen.\n" +
            "Prozessdetails lesen Threads, Pfad und Speicher mit PID-/Startzeitprüfung. Geschützte oder beendete Prozesse können unlesbar sein. " +
            "Priorität und Beenden werden in Superadmin verwaltet. Ein beendeter Prozess lässt sich nicht automatisch zurücknehmen.",
            "prozesse cpu kerne threads beenden details"),
        new("Sensoren", "Sensor-Diagramme, Detailmatrix und Grenzwerte",
            "LIVE-DIAGRAMME gruppiert gemeldete Kanäle nach Hardware und Einheit. Legendenhaken steuern Reihen dieser Sitzung; " +
            "der Filter gilt auch für DETAILMATRIX / GRENZWERTE. Das Zeitfenster hält 180 Messzyklen im RAM.\n" +
            "Fehlende oder widersprüchliche Werte erzeugen Lücken. NVMe Warning/Critical Temperature sind Grenzen, keine Ist-Temperaturen, " +
            "und bleiben aus Kurven und Temperaturwarnungen ausgeschlossen.\n" +
            "Bei fehlenden CPU-Werten den Providerbericht prüfen. HVCI kann den klassischen Treiber blockieren; " +
            "separat installiertes PawnIO kann den Zugriff ermöglichen. AETHER installiert es nicht und schaltet Kernisolierung nicht ab.",
            "sensoren diagramme kurven verlauf matrix nvme grenzwert temperatur filter pawnio hvci kernisolierung treiber"),
        new("Laufwerke", "SMART und Selbsttests",
            "LAUFWERKE zeigt Windows Storage Health. Im SMART-Modul Geräte erkennen und den smartctl-Bericht mit Fähigkeiten lesen. " +
            "Rohdaten können als JSON exportiert werden.\n" +
            "Selbsttests nur bei Unterstützung bestätigen. Sie laufen im Laufwerk weiter, auch nach App-Abbruch; abort stoppt sie gesondert. " +
            "Conveyance, Selective, AAM und APM sind ATA-spezifisch. NVMe percentage_used bezeichnet verbrauchte Ausdauer, keine Restlaufzeit.",
            "smart ssd festplatte selbsttest short long smartctl gesundheit"),
        new("Messauswertung", "Aufzeichnen, Vergleichen, FPS",
            "MIN/MAX/AVG verwendet gültige Messpunkte. Aufzeichnung ist anfangs aus und speichert JSONL unter History: " +
            "höchstens sieben Tagesdateien mit je 16 MiB, keine garantierte Sieben-Kalendertage-Frist.\n" +
            "Für Gaming eine Messsitzung aufzeichnen, stoppen und unter LOGS / GAMING auswerten oder mit einem zweiten Log vergleichen. " +
            "PresentMon-Capture benötigt Prozesswahl und Freigabe. AVG FPS, P99 und 1%-low bleiben je Prozess/Swapchain getrennt; " +
            "die erfassten Frameintervalle sind nicht zwingend tatsächlich am Monitor ausgegebene FPS.",
            "aufzeichnung log vergleich fps presentmon gaming min max avg csv"),
        new("Diagnose", "Windows-Status, Speicherdiagnose und Export",
            "DIAGNOSE aktualisiert lesende Windows-Befunde ungefähr alle 60 Sekunden. Speicherdiagnose-Ergebnisse liest vorhandene " +
            "Windows-Ereignisse. Einen neuen RAM-Test selbst mit mdsched.exe planen; er benötigt einen Neustart.\n" +
            "Diagnoseexport verwendet aktuelle Modellwerte mit Vorschau. Die Musterschwärzung ersetzt Rechner-/Benutzernamen, " +
            "Benutzerpfade, IPv4 und MAC. Sie ist keine vollständige Anonymisierung und gilt nicht automatisch für andere Berichte. " +
            "Den zu speichernden Text vor Weitergabe prüfen.",
            "diagnose export bericht schwaerzung redaktion speicherdiagnose mdsched ram support"),
        new("Diagnose", "Windows-Assistenten und Hintergrunddiagnose",
            "Windows-Diagnose & Problemlösung öffnet Helfer für Systemdateien, Updates, Netzwerk und Geräte/Treiber. " +
            "Jeder startet einzeln und speichert einen lokalen Bericht. Reparaturplan vorbereiten wählt DISM/SFC-Schritte, führt sie nicht aus.\n" +
            "Hintergrunddiagnose in den Desktop-Einstellungen aktivieren: erster Lauf sofort, danach 30 Minuten Pause. " +
            "Der letzte Bericht liegt unter Operations\\Reports\\background-latest.json. Neue Befunde melden sich im Tray; " +
            "Klick öffnet die Assistenten. Anmeldestart braucht separat genehmigten Autostart. Automatische Reparaturen gibt es nicht.",
            "assistent diagnose windows hintergrund ueberwachung reparatur update geraete treiber"),
        new("Sicherheit", "Schutzstatus lesen",
            "Defender, Firewall, BitLocker, Secure Boot, TPM und Updatezustand werden getrennt gelesen. " +
            "Ein grüner Wert bestätigt nur die jeweilige Meldung; unbekannte Daten sind kein positiver Befund.\n" +
            "Get-HotFix beweist keine vollständige Aktualität, Defender bewertet kein alternatives Antivirusprodukt. " +
            "Zertifikatsketten werden offline geprüft. AETHER schaltet keine Schutzfunktionen ab.",
            "defender firewall bitlocker secure boot tpm schutz zertifikat"),
        new("Cleaner", "Cache mit Vorschau bereinigen",
            "1. Stufe und Kategorie wählen. 2. TEMP-DATEIEN PRÜFEN erstellt Vorschau, Anzahl, MiB und Risiko A–D. " +
            "3. Kandidaten unter LÖSCHEN? ankreuzen. 4. Gesamtmenge bestätigen; gelöscht wird endgültig ohne Papierkorb.\n" +
            "Nur Dateien älter als sieben Tage. Links, Hardlinks, gesperrte, schreibgeschützte oder geänderte Dateien werden nicht gelöscht. " +
            "Browser/Apps vorher schließen. Cookies, Lesezeichen, Downloads und Offline-Medien gehören nicht zu den Cachekategorien.",
            "cleaner aufraeumen cache temp loeschen bewertung score vorschau"),
        new("Ereignisse", "Ereignisanzeige nutzen",
            "Kanal, Zeitraum von 1–30 Tagen und Limit bis 2000 wählen, dann nach Text und Stufe filtern. " +
            "Einzelereignisse haben getrennte Volltext- und XML-Details.\n" +
            "Der Cockpitmonitor liest System/Application nach jeweils ungefähr 15 Sekunden erneut, höchstens 200 je Kanal aus 24 Stunden. " +
            "Zähler beschreiben diesen begrenzten Snapshot; Zugriffsfehler und keine Treffer bleiben unterscheidbar.",
            "ereignisse eventlog kanal fehler warnung filter"),
        new("Superadmin", "Statusbasierte Verwaltung",
            "Für Dienste, Aufgaben, Features, Adapter, Firewall oder Benutzer zunächst STATUS LESEN. " +
            "Dann Ziel und Aktion wählen, Vorschau prüfen und einzeln bestätigen.\n" +
            "Viele Aktionen unterstützen Rücknahme der letzten reversiblen Sitzungsaktion. Extern geänderte Ziele werden nicht blind " +
            "überschrieben. Prozessbeenden und bereits ausgeführte Dienst-/Aufgabenwirkungen sind nicht automatisch reversibel; " +
            "Fehler oder Abbruch können Teilergebnisse hinterlassen.",
            "superadmin dienste aufgaben firewall benutzer ruecknahme workbench"),
        new("Operations", "Wartungspläne und Updates",
            "17 Wartungskategorien mit vier Planvorlagen: Aufgaben wählen, Vorschau lesen, Lauf bestätigen und Änderungen einzeln freigeben. " +
            "Ein Wiederherstellungspunkt ist standardmäßig Pflicht; scheitert er, beginnt keine Wartungsänderung.\n" +
            "Updates verwenden GUID und Revision mit erneuter EULA-Prüfung. WinGet-Pläne benötigen exakte IDs. " +
            "STOP hält nach dem laufenden Schritt an, nicht gewaltsam DISM/SFC. Live-/HTML-Berichte bleiben lokal; " +
            "es gibt keine automatische Löschfrist für Planberichte.",
            "operations wartung plan update eula wiederherstellungspunkt protokoll"),
        new("Pakete", "Paket-Center (WinGet)",
            "INSTALLIERTE PAKETE oder VERFÜGBARE UPDATES laden, alternativ im Katalog suchen. " +
            "ID und Quelle prüfen und aktionierbare Zeilen markieren; Strg/Umschalt wählt mehrere.\n" +
            "Installieren, Aktualisieren oder Deinstallieren zeigt die vollständige Vorschau vor dem Start. " +
            "ALLE AKTUALISIEREN nutzt die geladene Liste, nicht upgrade --all. Abgeschnittene IDs werden nicht verwendet. " +
            "Herstellerinstaller und Lizenzbedingungen gelten weiter.",
            "pakete winget installieren aktualisieren deinstallieren updates unigetui software"),
        new("Netzwerk", "Scan, Ping und Downloadtest",
            "Ping, Router-Ping und Captive-Portal-Probe prüfen Ziele nach Freigabe. LAN-Scan arbeitet nur in privaten IPv4-Netzen " +
            "von /24 bis /30. ARP/MAC und importierte OUI-CSV unterstützen die Zuordnung; fehlende Antworten schließen Geräte nicht aus.\n" +
            "SPEEDTEST lädt 1–200 MiB über eine HTTPS-Verbindung von speed.cloudflare.com. " +
            "Datenvolumen wird belastet und die eigene IP am Messziel sichtbar. Ergebnis ist Download-Anwendungsdurchsatz, kein Uploadtest.",
            "netzwerk lan scan ping speedtest durchsatz wlan arp oui"),
        new("Spuren", "Shellbag-Analyse",
            "SHELLBAGS LESEN zeigt Explorer-Ordnerspuren aus HKCU BagMRU, auch für inzwischen gelöschte Ordner. " +
            "Namensdekodierung kann unvollständig sein; unbekannte Einträge bleiben gekennzeichnet.\n" +
            "Limit sind 2000 Einträge. Der Zeitstempel gehört zum Registry-Schlüssel, nicht zwingend zum letzten Ordnerbesuch. " +
            "AETHER bietet in diesem Modul keine Löschfunktion.",
            "shellbags spuren registry ordner forensik bagmru"),
        new("Schluessel", "Sensible Schlüssel anzeigen",
            "WLAN-, BitLocker-Recovery- und OEM-Firmwarekey werden nur nach Klick und Warndialog gelesen. " +
            "Eine digitale Lizenz erzeugt keinen Firmwarekey.\n" +
            "Die Anzeige wird bei Tabwechsel und Schließen geleert und hat keine AETHER-Speicher-/Exportfunktion. " +
            "Vorher Bildschirmfreigabe und Aufnahme beenden. Anzeigeleeren verhindert weder Bildschirmaufnahme noch " +
            "Speicherforensik oder Systemprotokollierung.",
            "schluessel wlan passwort bitlocker recovery key oem lizenz"),
        new("Konsole", "CRT-Konsole und Befehlskatalog",
            "Katalogbefehl in die Eingabe übernehmen, Parameter und Platzhalter prüfen. ENTER startet direkt, auch bei Änderungen. " +
            "Im Einsteigermodus ist der freie Start gesperrt.\n" +
            "Jeder Lauf hat eine neue nichtinteraktive PowerShell-5.1-Sitzung. Variablen und cd gelten nicht beim nächsten Start. " +
            "Zeitlimit 1–1440 Minuten, Vorgabe 120. Abbruch beendet den eigenen Prozessbaum, macht Änderungen nicht rückgängig. " +
            "Bis zu 100 Befehle bleiben im RAM; Modulwechsel löscht freie Eingabe, Verlauf und Ausgabe.",
            "konsole terminal powershell katalog enter verlauf zeitlimit"),
        new("Daten & Grenzen", "Übertragung und Systemänderungen",
            "AETHER hat keine allgemeine App-Telemetrie. Einzelplatz nimmt keinen Managerkontakt auf; " +
            "Serverberichte verlangen Anmeldung, manuelle Freigabe und getrenntes Opt-in. Netzwerkwerkzeuge, Updates und freie Befehle " +
            "können dennoch Ziele kontaktieren.\n" +
            "Einstellungen, Berichte und Backups liegen unter %LOCALAPPDATA%\\AetherCockpit. " +
            "Vor Reparaturen sichern; Wiederherstellungspunkte ersetzen kein Backup. Der Uninstaller entfernt auch die Kontodaten. " +
            "Fehlende Werte bleiben nicht verfügbar, Exitcode 0 beweist keine Fehlerfreiheit.",
            "datenschutz telemetrie cloud grenzen appdata sicherheit daten"),
        new("Daten & Grenzen", "Einzelplatz und zentrale Verwaltung",
            "System Control -> Einstellungen -> Zentrale Verwaltung: Einzelplatz ist Standard. " +
            "Für Server-Client URL speichern, Einmalcode zur Anmeldung verwenden, Gerät manuell freigeben lassen und " +
            "Berichtsübertragung getrennt aktivieren.\n" +
            "Der Client sendet dann beim ersten möglichen Lauf und stündlich Diagnoseinventar mit Ereignissen und Befunden. " +
            "Das Token ist per DPAPI für das aktuelle Konto geschützt. Zurückschalten stoppt Anfragen und deaktiviert Übertragung. " +
            "Der Server bietet eine HTTPS-Webverwaltung. Der optional und separat bestätigte Windows-Clientdienst " +
            "meldet Heartbeats und Berichte auch ohne geöffnetes Cockpit und führt feste Verwaltungsaufträge aus. " +
            "Systemänderungen brauchen lokale und zentrale Freigaben; die DNS-Cache-Autoreparatur zusätzlich beide Automatik-Opt-ins. " +
            "Der Desktop-Einzelplatzschalter stoppt nicht einen separat installierten Dienst.",
            "server client manager zentrale verwaltung enrollment anmeldung opt-in dpapi bericht dienst heartbeat"),
        new("Konsole", "Windows-Werkzeugassistenten",
            "Der Werkzeugbereich enthält feste Diagnose- und Reparaturaktionen mit Verfügbarkeitsprüfung und " +
            "Konsolenausgabe. Veränderungen werden bestätigt, fehlende Werkzeuge nicht nachinstalliert. " +
            "Sysinternals und ADK sind optionale lokale Werkzeuge mit offiziellen Bezugslinks.\n" +
            "Remote-Updates benötigen eine vollständige aktuelle Suche, eine einzelne GUID/Revision und die Zustimmung " +
            "zum Lizenztext. Identität und Lizenz werden vor Ausführung erneut geprüft; kein automatischer Neustart.",
            "werkzeuge assistenten diagnose reparatur sysinternals adk treiber update versionierung")
    ];
}
