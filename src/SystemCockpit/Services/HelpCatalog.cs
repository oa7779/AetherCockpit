namespace SystemCockpit.Services;

public sealed record HelpTopic(string Module, string Title, string Body, string Keywords)
{
    public string SearchText => $"{Module} {Title} {Body} {Keywords}";
}

/// <summary>
/// Durchsuchbare, lokal eingebettete Hilfe: je Modul ein Thema mit Schritt-fuer-Schritt-
/// Anleitung, Sicherheitshinweisen und ehrlichen Grenzen. Keine Online-Hilfe, keine Telemetrie.
/// </summary>
public static class HelpCatalog
{
    public static IReadOnlyList<HelpTopic> All { get; } = Build();

    public static List<HelpTopic> Search(string query) => All
        .Where(t => t.SearchText.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    private static IReadOnlyList<HelpTopic> Build() =>
    [
        new("Arbeitsplatz", "Funktion finden / Strg+K",
            "FUNKTION FINDEN links oder Strg+K im Hauptfenster und in der Systemkontrolle. " +
            "Arbeitsbereich und mehrere Suchbegriffe kombinieren. Die Suche enthaelt Aufgabenablaeufe und den vollstaendigen Befehlskatalog.\n" +
            "Wirkung und Schritte lesen, dann Bereich oeffnen. Hier startet keine Reparatur, Installation oder Erfassung. " +
            "Katalogziele oeffnen nur den Eintrag; Expertenmodus wird nicht automatisch aktiviert. Die freie Expertenkonsole startet dort weiterhin mit Enter direkt.\n" +
            "AUSGABE EIN/AUS in der Systemkontrolle schafft Platz fuer Tabellen; bei einem Auftrag wird die Konsole sichtbar. " +
            "Auftraege zeigt bis zu 200 Eintraege dieser Fenstersitzung, keine sensiblen Anzeigen oder freien Befehle. " +
            "Bestehende Wartungsplanberichte und das Aktionsjournal bleiben getrennt. Kein globaler Scheduler.",
            "suche arbeitsplatz navigation auftraege warteschlange semi professionell"),
        new("Pakete", "Quellen, Paketlisten und Auftraege",
            "Installierte WinGet-Pakete/Updates laden oder suchen. Paketdetails liest die exakte ID und gemeldete Quelle. " +
            "Aenderungen zeigen alle IDs/Quellen in einer scrollbaren Vorschau; erst nach Zustimmung starten Pakete nacheinander. " +
            "Einzelfehler, Abbruch und nicht gestartete Folgeschritte stehen unter Auftraege. Nach dem Lauf Liste neu laden.\n" +
            "Paketliste exportiert Auswahl, sonst geladene Liste; nur vollstaendige IDs mit Quelle. Ausgeschlossene Eintraege werden genannt. " +
            "Import liest ausschliesslich das AETHER-Format (100 Pakete/128 KiB) als Wunschliste und installiert nichts. " +
            "Versionen sind Hinweise, keine Pins. Quellen muessen eingerichtet sein; keine Installer, Einstellungen oder Lizenzen gesichert.\n" +
            "Weitere Manager optional im bewusst gestarteten installierten UniGetUI/WingetUI. Keine native Scoop-/Chocolatey-Verwaltung. " +
            "Externe Aktionen werden nicht in AETHER aufgezeichnet; externe Einstellungen/Freigaben gelten.",
            "winget unigetui wingetui scoop chocolatey export import paketliste details quelle"),
        new("Netzwerk", "Live-Filter und Wireshark-Uebergabe",
            "Live-Analyse, Erfassung/Alarme und Warnungen sind getrennt. Oeffnen startet keinen Mitschnitt. " +
            "Text, Protokoll, Port, SYN und DNS-Fehler gemeinsam filtern. Ungueltiger Filter wird sichtbar abgewiesen; bisheriger Filter bleibt aktiv. " +
            "Nur bis zu 200 Live-Zeilen, keine Wireshark-Displayfilter und keine Gesamtstatistik. Paket markieren fuer Metadaten und Analysegrenzen.\n" +
            "CSV speichert nur sichtbare Metadaten, inklusive IP-Adressen. PCAP bleibt ungefiltert. Mitschnitt zuerst stoppen, " +
            "dann Datei bewusst an selbst installiertes Wireshark uebergeben. Kein Download/Upload, kein automatisches Capture. " +
            "128-Byte-Capture erlaubt keine vollstaendige Protokoll-/TLS-Analyse. Schwellenalarme sind Indizien, kein Angriffsnachweis.",
            "pcap pcapng wireshark filter tcp udp dns syn metadata"),
        new("Cleaner", "Grosse Dateien finden und gefiltert auswaehlen",
            "Grosse Dateien finden ist rein lesend, kein Loeschmodul. Ordner und Mindestgroesse selbst waehlen; beim Oeffnen startet kein Scan. " +
            "Maximal 100.000 Eintraege, 32 Ebenen und 1.000 groesste Treffer. Keine Links/Junctions; Fehler/Limits sind sichtbar, keine vollstaendige Belegungsanalyse. " +
            "CSV enthaelt Dateipfade; vor Weitergabe pruefen. Explorer markieren oeffnet nur Windows, nicht die Datei.\n" +
            "Der Cache-Cleaner bleibt separat: Pfadfilter schraenkt nur die Ansicht ein. Sichtbare ankreuzen wirkt auf sichtbare Kandidaten; " +
            "bereits versteckte angekreuzte Dateien bleiben Teil der gesamten Auswahl und Loeschbestaetigung. Keine ankreuzen leert alles. " +
            "Loeschen erst nach Vorschau und Bestaetigung, endgueltig ohne Papierkorb.",
            "speicherfresser grosse dateien filter cleanup temp wise autostart"),
        new("Mainboard", "Virtuelles Mainboard / 1986",
            "Links unter Cockpit -> Mainboard / 1986. Beim ersten Oeffnen werden nur vorhandene Windows-PnP-Geraete " +
            "und gemeldete RAM-Baenke lokal gelesen. HARDWARE NEU LESEN aktualisiert die Momentaufnahme, auch nach USB-Aenderungen.\n" +
            "Die isometrische Zeichnung zeigt stilisierten CPU-Kuehler, RAM-Riegel, GPU, Chipsatz und Anschluesse statt Gruppenkacheln. " +
            "Typbezeichnung und Windows-Status stehen an Hinweislinien; Bauteil/Beschriftung anklicken zeigt alle Geraete mit Eltern-ID. " +
            "Fehlende Gruppen bleiben unbestueckt. Bauformen und Bestueckung sind schematisch, keine physische Stueckliste.\n" +
            "Animierte Pfade illustrieren schematischen Datenfluss, keine gemessenen Wege, Richtungen oder Datenraten. " +
            "Langsam drehende Luefter sind keine Drehzahlmessung. Das Raster laeuft zum Horizont; sieben Binaerzeilen enthalten AETHER und Olaf Weins. " +
            "Cyan-/Magenta-Neon mit Glow: Binaerberge rahmen den Horizont links und rechts ein, die Perspektivmitte bleibt offen. " +
            "Feste Fluchtlinien und gerade zum Horizont laufende Querlinien verhindern ein Taumeln der Strasse. " +
            "Himmelsfarben/Verlaufspunkte wandern langsam; die Szene verschiebt sich sanft und Legendenpfade pulsieren farbig hin/zurueck. " +
            "Beschriftungen bleiben scharf und deckend vor den Linien. Animationsschalter und Scanner-Animation sind gekoppelt.\n" +
            "An den Komponenten wechseln Beschreibung und Rundinstrument alle sechs Sekunden. Alle zuordenbaren Messkanaele " +
            "(auch MHz, RPM, Temperatur, Leistung/Spannung) rotieren; Instrument nennt Hardware/Kanal. Skala ist eine " +
            "Anzeigereferenz, keine gemessene Grenze. Fehlende/widerspruechliche Werte bleiben --. " +
            "Das reduziert statische Darstellung, garantiert aber keinen Einbrennschutz; Monitor-Pixelpflege und Abschaltung nutzen.\n" +
            "Auf der Platine erscheinen ALLE erkannten DiskDrive-/CDROM-Geraete einzeln mit Typname und Status, auch baugleiche SSDs. " +
            "Laufwerk anklicken zeigt genau dieses Geraet. Keine Mengenbegrenzung; unbekannte Bauformen bleiben schematisch.\n" +
            "Internet ist der von Windows gemeldete Zustand, kein eigener Online-Test. Keine Sensorwerte werden erfunden.",
            "mainboard motherboard platine retro 1986 hardware bus usb sata ram gpu controller schaubild luefter fans binaer ssd"),
        new("Mainboard", "Bildschirmschoner / AetherMainboard.scr",
            "Optional im Setup mitinstallieren oder aus dem vollstaendig entpackten Portable-Ordner verwenden. " +
            "Die SCR-Datei nicht allein verschieben: sie teilt DLLs und Laufzeit mit AETHER. Das optionale Setup legt " +
            "einen kleinen nativen Starter im Windows-Systemordner ab; AETHER Mainframe 1986 steht dann in der Windows-Liste. Dort selbst den Bildschirmschoner " +
            "und 'Anmeldeseite bei Reaktivierung' in Windows festlegen; das Setup aktiviert nichts automatisch.\n" +
            "Jeder angeschlossene Bildschirm zeigt die obere Mainboard-Szene ohne die untere Geraeteuebersicht. " +
            "Die Vektorszene skaliert auf die Monitorflaeche einschliesslich 4K; bei breiten Monitoren werden Himmel/Boden erweitert, nicht die Platine verzerrt. " +
            "Nur intern erkannte Laufwerke, keine USB-Sticks/USB-Gehaeuse/SD/optischen Medien; unbekannte Bustypen bleiben ausgeschlossen. " +
            "Die normale Cockpit-Inventarliste bleibt vollstaendig. Lange Namen werden lesbar gekuerzt statt von Linien durchzogen. " +
            "Live-Auslastung und verfuegbare Temperaturen stehen direkt an den Komponenten; Gruppenmaxima sind bezeichnet. " +
            "Fehlende Sensoren bleiben --; Temperaturgrenzen sind keine Messwerte. Maus/Tastatur beendet den Vollbildmodus; " +
            "die Windows-Vorschau wird im Vorschaufenster eingebettet. /c bietet nur Musik an/aus, /s startet, /p <Handle> ist die Vorschau. " +
            "Vorhandene externe WAV/Lautstaerke bleiben; fehlt eine Zuordnung, wird beim Speichern mit Musik an einmalig nach einer lokalen WAV gefragt. Vorschau bleibt stumm.\n" +
            "Start ohne UAC, keine Sensorinitialisierung. Live-Werte werden nur lesend lokal aus laufendem AETHER desselben " +
            "Windows-Kontos und derselben Sitzung uebernommen. Ohne Verbindung: echte Hardware-Momentaufnahme, deutlich KEINE LIVE-MESSWERTE. " +
            "Bewegung folgt Windows-Animationen und bei Verbindung dem AETHER-Schalter; Richtungen und Lueftertempo sind symbolisch. " +
            "Nicht als eigene Sperrsoftware verwenden; Schutz bleibt Aufgabe der Windows-Sperre.",
            "bildschirmschoner screensaver scr mainboard monitor multimonitor vorschau"),
        new("Erste Schritte", "AETHER vollstaendig deinstallieren",
            "Alte Installationen zuerst mit dem aktuellen Setup aktualisieren, danach unter Windows -> Installierte Apps deinstallieren. " +
            "Fehlen nach einer alten Deinstallation Uninstaller/Installationseintrag, bietet das Setup ab Beta 20 bei erkannten " +
            "AETHER-Produktdateien eine bestaetigte Wiederherstellung an. Zuerst laufendes AETHER im Tray beenden. " +
            "Nur einen ausschliesslich fuer AETHER bestimmten Zielordner bestaetigen; dabei bleiben Kontodaten erhalten. " +
            "Ab Beta 19 werden nach ausdruecklicher Bestaetigung Autostart-Aufgabe, installierte App/Tray/Gadgets/SCR beendet und " +
            "Installationsordner sowie %LOCALAPPDATA%\\AetherCockpit des ausfuehrenden Windows-Kontos geloescht, " +
            "einschliesslich Einstellungen, Musikauswahl, Logs und Backups. Benoetigte Daten vorher extern sichern! " +
            "Externe Musikdateien und andere portable Programmordner bleiben erhalten; diese teilen jedoch dieselben Kontoeinstellungen. " +
            "Andere Profile bleiben unberuehrt. Bei anderem UAC-Konto gilt das Administratorkonto. Updates erhalten weiterhin Nutzerdaten.",
            "deinstallation uninstall entfernen loeschen daten backup"),
        new("Erste Schritte", "Erststart: EULA und Rechner-Inventar",
            "Beim allerersten Start fuehrt ein kurzer Assistent durch drei Schritte: Funktionsuebersicht, " +
            "Lizenzvereinbarung (EULA) mit Pflicht-Zustimmung und das Rechner-Inventar.\n" +
            "Ohne Zustimmung zur EULA wird das Programm beendet. Die Zustimmung wird einmalig lokal gespeichert " +
            "(%LOCALAPPDATA%\\AetherCockpit), der Assistent erscheint danach nicht erneut.\n" +
            "Das Inventar erfasst lesend Hardware (ueber die Windows-Abfrage) und installierte Software (Registry) " +
            "und laesst sich als Textdatei speichern. Spaeter jederzeit ueber DIAGNOSE -> RECHNER-INVENTAR erreichbar.",
            "erststart eula lizenz zustimmung inventar inventarisierung hardware software erfassen assistent willkommen"),
        new("Erste Schritte", "AETHER in 2 Minuten",
            "1. AETHER startet mit Administratorrechten (UAC) und initialisiert die Hardware-Sensoren automatisch.\n" +
            "2. COCKPIT zeigt die Live-Instrumente; TASKMANAGER Prozesse und alle CPU-Kerne.\n" +
            "3. Beobachten und Auswerten: SENSOREN, LAUFWERKE, MESSAUSWERTUNG.\n" +
            "4. Pruefen: DIAGNOSE und SICHERHEIT (automatische lesende 60s-Abfragen).\n" +
            "5. Eingreifen: CLEANER, OPERATIONS, SUPERADMIN und die ERWEITERTE SYSTEMKONTROLLE.\n" +
            "Grundregel: Lesen passiert automatisch, Aendern niemals - jede aendernde Aktion verlangt deine ausdrueckliche Bestaetigung.",
            "start einfuehrung ueberblick grundlagen uac administrator"),
        new("Erste Schritte", "Desktop-Gadgets, Transparenz und Tray",
            "Das AETHER-Symbol bei der Uhr oeffnet Desktop / Transparenz / Parameter. " +
            "Dort lassen sich Hauptfenster, xBar und Gadgets getrennt auf 20-100 Prozent Deckkraft stellen.\n" +
            "Gadgets liegen hinter normalen Apps auf der Desktop-Ebene; am Titel ziehen, am eigenen Regler die Deckkraft einstellen. " +
            "Diagrammgruppen waehlst du im Setup oder spaeter in diesem Dialog.\n" +
            "X am Hauptfenster blendet es nur aus. Im Tray: Gadgets minimieren/wiederherstellen, Gadgets schliessen oder AETHER vollstaendig beenden. " +
            "Der optionale installierte Anmeldestart braucht ausdrueckliche Zustimmung zur erhoehten Aufgabe und eine vorher manuell bestaetigte EULA.",
            "desktop gadget rainmeter transparenz deckkraft opacity tray autostart taskleiste uhr minimieren"),
        new("Netzwerk", "Paketmitschnitt und regelbasierte Warnungen",
            "Tray -> Netzwerkueberwachung. TCP-Verbindungszaehlung ist separat aktivierbar, ohne Paketmitschnitt. " +
            "Fuer Pakete zuerst Npcap separat installieren und lizenzieren; AETHER installiert keinen Treiber.\n" +
            "Schnittstellen lesen, Ethernet-Schnittstelle waehlen, Mitschnitt bestaetigen. " +
            "Maximal 128 Bytes/Paket, 100 MiB oder 15 Minuten; lokale PCAP-Dateien unter AetherCockpit/Captures. " +
            "Sieben Tage Aufbewahrung beim naechsten lokalen Bereinigungslauf. Auch gekuerzte Pakete koennen vertrauliche Daten enthalten.\n" +
            "Viele SYN-Zielports, DNS-Fehler oder TCP-Verbindungen loesen konfigurierbare Hinweise aus: Tray und lokales Protokoll, ohne Ton oder Blockierung. " +
            "Keine Wireshark-Vollanalyse, Entschluesselung oder Angriffsgarantie. Automatische Mitschnitte brauchen eigene Zustimmung und installierten Autostart.",
            "netzwerk npcap wireshark pcap paketmitschnitt capture dns tcp syn alarm datenschutz"),
        new("Erste Schritte", "Skins: Retro-Neon, Aero, Candy, Fluent",
            "Unter Systemkontrolle > EINSTELLUNGEN > Skin stehen vier Oberflaechen bereit:\n" +
            "RETRO-NEON (Original und Standard), VISTA AERO GLAS (dunkles Glas in Blau), CANDY GLOSSY (dunkle " +
            "Bonbon-Optik mit Glanzlichtern) und WINDOWS 11 26H2 FLUENT (flach, dezent, Mica-artig).\n" +
            "Skins stylen Hintergruende, Panels, Leisten und Knoepfe; die Neon-Akzentfarben der Inhalte bleiben " +
            "in allen Skins erhalten - deshalb sind alle Skins dunkle Varianten.\n" +
            "Die Auswahl wirkt SOFORT auf alle offenen Fenster und wird automatisch gespeichert. " +
            "Der Erststart-Assistent laeuft immer im Original-Design.",
            "skin theme design aero vista candy glossy fluent windows 11 oberflaeche optik"),
        new("Erste Schritte", "Navigation: Zurueck, Vor, Hauptebene",
            "Ueber der Inhaltsflaeche sitzt die Navigationsleiste: ZURUECK und VOR folgen deinem Besuchsverlauf wie im " +
            "Browser, COCKPIT springt direkt zur Hauptebene, HILFE oeffnet dieses Hilfe-Center.\n" +
            "Ueberall funktionieren ausserdem die Maus-Daumentasten (Zurueck/Vor) und Alt+Pfeil links/rechts.\n" +
            "Die erweiterte Systemkontrolle hat ihre eigene Leiste mit HAUPTFENSTER-Knopf; ihr Verlauf bewegt sich " +
            "durch die Module (SMART, Pakete, Wartung, ...).\n" +
            "Die Brotkrumen-Zeile zeigt immer die aktuelle Position, z.B. HAUPTEBENE > SUPERADMIN.",
            "navigation zurueck vor verlauf maus daumentaste alt pfeil brotkrumen hauptebene untermenue"),
        new("Erste Schritte", "Einsteiger- und Expertenmodus",
            "In der erweiterten Systemkontrolle unter WARTUNG gibt es den Schalter EXPERTENMODUS.\n" +
            "AUS (Einsteiger): Der Befehlskatalog zeigt nur lesende, sichere Befehle; der freie Konsolenstart ist gesperrt.\n" +
            "AN (Experte): Voller Katalog inklusive aendernder Befehle; Enter in der Konsole fuehrt direkt aus.\n" +
            "Der Modus wird gespeichert. Gefuehrte Aktionen (Wartung, Cleaner, Operations) behalten in beiden Modi ihre Bestaetigungsdialoge.",
            "einsteiger experte modus sicherheit sperre konsole risiko"),
        new("Erste Schritte", "Risikoanzeige verstehen",
            "Jeder Katalogbefehl traegt eine farbige Risikostufe:\n" +
            "RISIKO: LESEND (gruen) - zeigt nur Daten an, veraendert nichts.\n" +
            "RISIKO: NETZWERK (gelb) - kann externe Dienste kontaktieren; deine IP wird dort sichtbar.\n" +
            "RISIKO: AENDERND (orange) - veraendert das System sofort bei Ausfuehrung.\n" +
            "Der Detailbereich ergaenzt SINNVOLL (wann der Befehl hilft) und VORSICHT (was zu beachten ist).",
            "risiko stufen farben lesend aendernd netzwerk badge"),
        new("Cockpit", "Live-Instrumente und AETHER xBar",
            "Das Cockpit misst etwa alle 2 Sekunden CPU, RAM, GPU und Netzwerk; Kurven halten 90 Punkte im RAM.\n" +
            "AETHER xBar (Diagnose-Tab) ist eine reine Diagrammleiste ohne Launcher-, Datei- oder Messwertlisten. " +
            "CPU, RAM und GPU zeigen Instrumente mit Verlauf; CPU-/GPU-/SSD-Temperaturen und Download/Upload haben Live-Kurven. " +
            "Laufwerke zeigen je ein Tortendiagramm fuer belegt (lila) und frei (tuerkis).\n" +
            "In den xBar-Optionen waehlst du Monitor, linke/rechte Seite, angeheftet oder frei schwebend und (nur am Einzelmonitor) Auto-hide. " +
            "Monitor, Position und die sechs einzeln schaltbaren Diagrammgruppen bleiben lokal gespeichert. " +
            "Im FLOAT-Modus kannst du die Leiste am Titel frei verschieben, ohne automatisches Andocken.\n" +
            "Warnschwellen aenderst du in der Systemkontrolle unter EINSTELLUNGEN. Exklusives Vollbild kann die Leiste verdecken.\n" +
            "Nicht verfuegbare Sensoren zeigen 'Nicht verfuegbar' - niemals erfundene Werte.",
            "xbar sidebar diagramme graphen tortendiagramm belegt frei laufwerke widgets auto-hide pin float monitor overlay temperatur"),
        new("Taskmanager", "Prozesse und CPU-Kerne",
            "Live-Prozesstabelle mit Suche und Sortierung; CPU-Anteil ist auf die gesamte Maschine normiert, auch bei Gaming-Last.\n" +
            "Alle logischen CPU-Kerne haben eigene Graphen.\n" +
            "PROZESSDETAILS liest Threads, Pfad und Speicher eines gewaehlten Prozesses (nur lesend; geschuetzte Prozesse liefern keine Details).\n" +
            "Prozesse beendest du bewusst ueber den Katalogbefehl Stop-Process oder den Windows-Task-Manager.",
            "prozesse cpu kerne threads beenden details"),
        new("Sensoren", "Sensor-Diagramme, Detailmatrix und Grenzwerte",
            "LIVE-DIAGRAMME zeigt nach Hardware und Einheit gruppierte Verlaufskurven. Die Farblegende schaltet einzelne Sensoren fuer diese Sitzung ein/aus. " +
            "Jedes Diagramm besitzt eine eigene dynamische Y-Skala; X zeigt echte Messzeiten. Letzte 180 Messzyklen (bei 2s etwa 6 Minuten), nur im Arbeitsspeicher.\n" +
            "Der Filter wirkt auf Diagramme und Tabelle. DETAILMATRIX / GRENZWERTE zeigt weiterhin alle Hardware-Sensoren (LibreHardwareMonitor). " +
            "'Nicht verfuegbar' bedeutet: kein Messwert - nicht 0. Ausfaelle unterbrechen die Kurve, kein Verbinden ueber fehlende Messpunkte.\n" +
            "NVMe 'Warning/Critical Temperature' sind vom Laufwerk gemeldete GRENZWERTE, keine Ist-Temperaturen; " +
            "AETHER kennzeichnet sie und nimmt sie aus Warnungen und Verlaeufen aus.\n" +
            "Fehlende CPU-Temperatur/Leistung kann am Provider liegen; AETHER erfindet keinen Ersatzwert.\n" +
            "Fehlen CPU-Werte bei aktiver Kernisolierung (HVCI): Der klassische WinRing0-Treiber ist von Windows blockiert. " +
            "Loesung ohne Schutzabschaltung: signierten PawnIO-Treiber installieren (https://pawnio.eu), AETHER neu starten. " +
            "Der Providerbericht (Cockpit, 'CPU-/Sensorproviderbericht lesen') nennt den Kernelzugriffs-Status in der ersten Zeile.",
            "sensoren diagramme kurven verlauf matrix nvme grenzwert temperatur filter pawnio hvci kernisolierung winring0 treiber"),
        new("Laufwerke", "SMART und Selbsttests",
            "LAUFWERKE zeigt Windows Storage Health; das SMART-Modul (Systemkontrolle) liest den vollstaendigen smartctl-Bericht.\n" +
            "1. SMART-Geraete erkennen. 2. Diagnose lesen. 3. Bei Bedarf Selbsttest (short/long) bestaetigen.\n" +
            "Ein gestarteter Selbsttest laeuft im Laufwerk weiter, auch wenn AETHER beendet wird; 'abort' stoppt ihn explizit.\n" +
            "NVMe percentage_used ist verbrauchte Ausdauer, keine Restlaufzeit-Prognose.",
            "smart ssd festplatte selbsttest short long smartctl gesundheit"),
        new("Messauswertung", "Aufzeichnen, Vergleichen, FPS",
            "MIN/MAX/AVG entstehen live aus gueltigen Messpunkten. Aufzeichnung (JSONL, 7 Tage x 16 MiB) ist standardmaessig AUS.\n" +
            "Workflow Gaming: Aufzeichnung starten, spielen, stoppen, dann in LOGS / GAMING den Verlauf auswerten oder zwei Logs vergleichen.\n" +
            "PresentMon-Capture misst echte Frame-Zeiten eines Prozesses: AVG FPS, P99, 1%-low - pro Swapchain, ohne Vermischung.",
            "aufzeichnung log vergleich fps presentmon gaming min max avg csv"),
        new("Diagnose", "Windows-Status, Speicherdiagnose, Export",
            "DIAGNOSE fasst lesende Windows-Pruefungen zusammen (alle 60s automatisch aktualisiert).\n" +
            "SPEICHERDIAGNOSE-ERGEBNISSE liest vorhandene Resultate der Windows-Speicherdiagnose aus dem System-Ereignislog. " +
            "Einen neuen RAM-Test planst du selbst mit mdsched.exe (Neustart erforderlich); AETHER startet ihn nicht automatisch.\n" +
            "DIAGNOSEBERICHT EXPORTIEREN erstellt einen Textbericht mit Vorschau. Die Schwaerzung ersetzt Rechnername, " +
            "Benutzername, IPv4- und MAC-Adressen durch Platzhalter, bevor du speicherst - fuer Foren oder Support geeignet.\n" +
            "Schwaerzung ist Mustererkennung, keine Garantie: Vorschau vor dem Teilen selbst pruefen.",
            "diagnose export bericht schwaerzung redaktion speicherdiagnose mdsched ram support"),
        new("Sicherheit", "Schutzstatus lesen",
            "Defender, Firewall, BitLocker, Secure Boot, TPM und Updates werden lesend geprueft.\n" +
            "'Unbekannt' ist niemals ein Freifahrtschein; ein gruener Einzelwert ist kein Gesamturteil.\n" +
            "AETHER schaltet keine Schutzfunktionen ab.",
            "defender firewall bitlocker secure boot tpm schutz"),
        new("Cleaner", "Sicher aufraeumen in 4 Schritten",
            "1. Stufe (Schnell/Standard/Tief/Experte) und Kategorie waehlen. 2. TEMP-DATEIEN PRUEFEN erstellt eine Vorschau " +
            "mit Bereinigungsbewertung (Stufe A-D, Dateien, MiB). 3. Dateien in der Spalte LOESCHEN? ankreuzen - " +
            "Zeilenmarkierung allein loescht NIE. 4. Loeschen bestaetigen; endgueltig, ohne Papierkorb.\n" +
            "Nur Dateien aelter als 7 Tage; Junctions, Hardlinks, gesperrte und veraenderte Dateien werden uebersprungen. " +
            "Browser/Apps vorher schliessen. Keine Cookies, Lesezeichen, Downloads oder Offline-Medien.",
            "cleaner aufraeumen cache temp loeschen bewertung score vorschau"),
        new("Ereignisse", "Ereignisanzeige nutzen",
            "Kanal und Zeitraum (1..30 Tage) waehlen, Eintraege filtern, Details direkt eingebettet lesen.\n" +
            "Der Eventmonitor im Cockpit liest System/Application automatisch alle 15 Sekunden (maximal 400 Eintraege).",
            "ereignisse eventlog kanal fehler warnung filter"),
        new("Superadmin", "Statusbasierte Verwaltung",
            "Bereiche wie Dienste, Aufgaben, Firewall, Pakete, Benutzer: erst STATUS LESEN, dann Zeile waehlen, " +
            "Aktion waehlen, Vorschau pruefen, einzeln bestaetigen.\n" +
            "Nach jeder Aenderung wird der Status neu gelesen; viele Aktionen bieten eine gepruefte RUECKNAHME. " +
            "Extern veraenderte Ziele werden nicht blind zurueckgenommen.",
            "superadmin dienste aufgaben firewall benutzer ruecknahme workbench"),
        new("Operations", "Wartungsplaene und Updates",
            "17 native Wartungskategorien mit Vorlagen: Plan zusammenstellen, Vorschau lesen, Wiederherstellungspunkt-Gating " +
            "beachten, jede Kategorie einzeln bestaetigen. Live-/HTML-Protokolle bleiben erhalten.\n" +
            "Windows-Updates laufen per GUID+Revision mit frischer EULA-Pruefung; WinGet-Plaene nur mit exakten IDs.\n" +
            "STOP bedeutet: nach dem aktuellen Schritt geordnet stoppen.",
            "operations wartung plan update eula wiederherstellungspunkt protokoll"),
        new("Pakete", "Paket-Center (WinGet)",
            "Direkt erreichbar ueber die linke Leiste: SK PAKETE / WINGET.\n" +
            "1. INSTALLIERTE PAKETE oder VERFUEGBARE UPDATES laden, oder per Suchfeld im Katalog suchen.\n" +
            "2. Zeilen markieren (Strg/Umschalt fuer mehrere) - die Spalte AKTIONIERBAR muss 'ja' zeigen.\n" +
            "3. Installieren/Aktualisieren/Deinstallieren klicken; der Dialog zeigt die vollstaendige ID-Liste vor dem Start.\n" +
            "ALLE AKTUALISIEREN nutzt exakt die zuletzt geladene Update-Liste - nie ein blindes 'upgrade --all'.\n" +
            "Eintraege mit abgeschnittener ID (...) sind nicht aktionierbar; das verhindert Aktionen auf geratene IDs.",
            "pakete winget installieren aktualisieren deinstallieren updates unigetui software"),
        new("Netzwerk", "Scan, Ping, Speedtest",
            "Ping/Router-Ping/Captive-Portal pruefen gezielt nach Freigabe. LAN-Scan nur in privaten RFC1918-Netzen (/24../30), " +
            "MAC/Hersteller per ARP und optionaler OUI-CSV.\n" +
            "SPEEDTEST laedt nach Freigabe 1..200 MiB von speed.cloudflare.com und misst den Download-Durchsatz einer " +
            "HTTPS-Verbindung. Dein Datenvolumen wird belastet und deine IP ist dem Messziel sichtbar. Kein Upload-Test.",
            "netzwerk lan scan ping speedtest durchsatz wlan arp oui"),
        new("Spuren", "Shellbag-Analyse",
            "Windows merkt sich Ansichtseinstellungen besuchter Ordner in der Registry (BagMRU) - auch fuer geloeschte Ordner.\n" +
            "SHELLBAGS LESEN zeigt diese Spuren nur lesend; nicht dekodierbare Eintraege werden ehrlich als unbekannt markiert.\n" +
            "AETHER bietet bewusst keine Shellbag-Loeschung an; der Zeitstempel gehoert zum Registry-Schluessel.",
            "shellbags spuren registry ordner forensik bagmru"),
        new("Schluessel", "Sensible Schluessel anzeigen",
            "WLAN-Profile, BitLocker-Recovery und OEM-Windows-Key werden nur auf ausdruecklichen Klick nach Warndialog gelesen.\n" +
            "Ausgabe verschwindet beim Tabwechsel und wird nie gespeichert oder exportiert.\n" +
            "Vorher Bildschirmfreigabe/-aufnahme beenden; Schluessel niemals teilen.",
            "schluessel wlan passwort bitlocker recovery key oem lizenz"),
        new("Konsole", "CRT-Konsole und Befehlskatalog",
            "Befehl im Katalog anklicken (wird in die Eingabe kopiert), Platzhalter wie 'PAKET-ID' ersetzen, Enter startet direkt.\n" +
            "Pfeil hoch/runter blaettert durch den fluechtigen Verlauf (max. 100). Jede Ausfuehrung ist eine frische, " +
            "nichtinteraktive PowerShell-5.1-Sitzung: cd/Variablen gelten nicht fuer den naechsten Start.\n" +
            "Zeitlimit 1..1440 Minuten; Abbruch beendet den Prozessbaum, macht aber nichts rueckgaengig.\n" +
            "Im Einsteiger-Modus ist der freie Start gesperrt.",
            "konsole terminal powershell katalog enter verlauf zeitlimit"),
        new("Daten & Grenzen", "Uebertragung und Systemaenderungen",
            "Keine Telemetrie oder unabgesprochenen Hintergrunddienste. Einzelplatz ohne Serverkontakt; Serverberichte nur nach Anmeldung, Freigabe und ausdruecklichem Opt-in.\n" +
            "Keine Schutzabschaltung und keine automatischen Reparaturen.\n" +
            "Einstellungen/Backups/Protokolle liegen lokal unter %LOCALAPPDATA%\\AetherCockpit.\n" +
            "Nicht verfuegbare Werte bleiben 'Nicht verfuegbar'. Exitcode 0 ist kein Gesundheitsbeweis. " +
            "Vor Reparaturen sichern; Wiederherstellungspunkt nutzen.",
            "datenschutz telemetrie cloud grenzen appdata sicherheit philosophie")
    ];
}
