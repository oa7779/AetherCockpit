# Benutzerhandbuch

## Erststart

`SystemCockpit.exe` starten und UAC bestätigen. Der erste Assistent zeigt
Funktionsübersicht, EULA und Rechner-Inventar. Ohne EULA-Zustimmung endet
das Programm. Die Inventarisierung zeigt ihre Abfragestufen; bei einem
Fehler erneut aktualisieren, bevor das Cockpit geöffnet wird.

Das Inventar liest Windows-Hardwaredaten und installierte Software aus der
Registry. Als Textdatei gespeichert kann es Rechnernamen und Seriennummern
enthalten. Die EULA-Zustimmung liegt lokal im ausführenden Windows-Konto.
Der Erststart verwendet immer das Retro-Neon-Originaldesign.

## Navigation und Suche

Die linke Navigation gruppiert die Funktionen in **Übersicht, Hardware,
Diagnose, Wartung, Verwaltung und Hilfe**. Gruppen lassen sich ausklappen;
die Gruppe des aktiven Bereichs wird geöffnet. Die vorhandenen Module
bleiben erreichbar. **↗** kennzeichnet einen Wechsel in die separate
Systemkontrolle, etwa zu **SMART-Diagnose**, **Netzwerk & Verbindungen**,
**Softwarepakete & Updates** oder **Windows-Werkzeuge**. Ein Klick öffnet
das Zielmodul und startet keine Aktion.

Weitere Bezeichnungen nennen die Aufgabe direkt: **Cache-Bereinigung** öffnet
die Dateivorschau, **Wartungspläne & Diagnose** bündelt geführte Prüfungen,
**Autostart-Programme** öffnet die Starttabelle und **Auftragsstatus** zeigt
laufende beziehungsweise abgeschlossene Aufträge. **Explorer-Verlauf** ist
eine lesende Auswertung gespeicherter Ordneransichten; **Windows- und
WLAN-Schlüssel** zeigt vertrauliche Schlüssel- und Zugangsdaten. Die
Bezeichnungen ändern weder interne Routen noch Sicherheitsabfragen.

**Zurück/Vor**, Maus-Daumentasten und `Alt+Pfeil links/rechts` bewegen sich
durch den lokalen Besuchsverlauf. Er hält bis zu 100 Schritte im RAM.
**Cockpit** und **Hilfe** springen direkt zur jeweiligen Seite.

**Funktion finden / Strg+K** kombiniert Arbeitsbereich und Suchbegriffe.
Der Treffer erklärt Wirkung und Schritte und öffnet den passenden Bereich.
Die Suche selbst startet keinen Auftrag und aktiviert keinen Expertenmodus.

Im unveröffentlichten Arbeitsstand findet `Strg+K` zusätzlich Prozesse aus
der vorhandenen Cockpitprobe. Ein Treffer wählt genau dessen PID und
Startidentität aus; ein inzwischen verschwundener Treffer wählt keinen
Ersatzprozess. **Favorit umschalten**, **Favoriten** und **Zuletzt verwendet**
gelten für Werkzeuge, nicht für eine dauerhaft gespeicherte Prozesschronik.

## Prozesszentrale (unveröffentlichter Arbeitsstand)

**Übersicht → Prozesse / Leistung** öffnet die Prozesszentrale.
Suche, **Nur aktuell**, **CPU ≥ 10 %** und **Prozessbaum** kombinieren
Ansichtsfilter. Spaltenköpfe sortieren numerische Werte numerisch;
im Baum bleibt die Eltern-Kind-Reihenfolge erhalten und nur Geschwister
werden umgeordnet. Links/rechts klappt den ausgewählten Zweig ein/aus.
**Enter** lädt Details. **Ansicht pausieren** friert nur die Anzeige ein,
nicht die gemeinsame Messung. **Abbrechen** beendet eine laufende Detailabfrage.
Die globale Prozesssuche oder eine Prozessauswahl aus dem Cockpit setzt
die Anzeigepause zurück, damit das aktuelle Ziel ausgewählt werden kann.

Der Inspektor zeigt Quellen, Zustände, Zeitpunkte und die vorhandene
CPU-/RAM-/Prozess-I/O-Historie. Fehlende Proben werden nicht interpoliert.
**Nicht mehr beobachtet** bedeutet nicht, dass Windows das Prozessende
bestätigt hat. Kurzlebige Prozesse zwischen zwei Proben können fehlen.
Benannte Filteransichten lassen sich mit **Ansicht merken/laden** erhalten.
Eigene Suchtexte und Namen liegen dabei unverschlüsselt im lokalen Profil.

Rechtsklick auf eine Zeile funktioniert hier und in der Cockpit-Prozesstabelle.
Das Menü bietet Datei-/Kopieraktionen, Eltern/Kinder und Systembeziehungen.
**Fenster schließen** wird nur bei beobachtetem sichtbarem Fenster angeboten;
die Anwendung kann nachfragen oder weiterlaufen.

**Prozess beenden** ist dagegen eine harte Beendigung und kann ungespeicherte
Daten verlieren. Den Dialog mit PID, Startidentität und Pfad sorgfältig prüfen.
**Abbrechen** ist Standard und erhält den Fokus; erst ein ausdrücklicher Klick
auf **Prozess beenden** führt zur erneuten technischen Prüfung.
**Prozessbaum beenden** bestätigt eine feste Zielmenge. Bei geänderten
Kindern neu auswählen und bestätigen. Bereits erfolgte Eingriffe lassen
sich durch Abbrechen nicht rückgängig machen.

Die Meldung nach der Aktion unterscheidet bestätigtes Ende, Zeitüberschreitung,
fehlenden Zugriff und Teilwirkung. Eine angenommene Anforderung ist noch kein
Endnachweis. Windows-Schutz, AETHER und notwendige Eigenkomponenten werden
nicht umgangen. Bei fehlenden Rechten gibt es keine automatische UAC-Schleife.

**Systemkorrelation anzeigen** liest Dienste, Verbindungen, ausführbare
Autostart-/Aufgaben-/Paketpfade, GPU-Engines und geeignete Ereignisse.
Beziehungen sind zeitbezogen, nicht dauerhaft. **Prozessbezug** im
Hardwarebereich zeigt belegbare GPU-/Netzwerkbeziehungen in umgekehrter Richtung.
Eine gleichzeitige Sensoränderung belegt keine Prozessursache.

**Snapshot** erfasst einen lokalen Sitzungszustand. Zwei Aufnahmen ermöglichen
**Vergleichen**. Das Kontextmenü dieser Schaltfläche speichert vollständige
`.aethersnapshot`-Dateien nach Datenschutzwarnung oder lädt sie zum Vergleich.
Import verändert die Live-Liste nicht und führt keine Aktion aus.
**Export** schreibt JSON, CSV oder Text mit einer eigenen Entscheidung über
sensible Daten. Vollständige lokale Dateien vor Weitergabe prüfen:
[Datenschutz](SECURITY-AND-PRIVACY.md#prozessaktionen-und-systemsnapshots).

## Cockpit und Mainboard

Das Logo links oben erhält im Retro-Neon-Design einen sanften Neon-Puls und
einen wandernden Lichtreflex. Darunter steht ein schräger Chromschriftzug in
Silber, Violett und Pink. Das gleiche Branding erscheint rechts oben im
Bildschirmschoner. Der Animationsschalter und die Windows-Animationsfreigabe
gelten auch für das Logo; bei ausgeschalteter Bewegung bleibt es statisch.
Windows Modern behält seine neutrale, statische Darstellung.

Das Cockpit zeigt seine ursprünglichen Instrumente, Kurven und Listen:
Live-Messwerte, CPU-Kerne, Datenträger, Prozesse, Ereignisse, Systempuls und
Datenkontrolle stehen direkt auf der Seite. Die bisherigen einzelnen
Netzwerk-, Datenträger- und Sensorbereiche behalten ihr Aufklappverhalten.
Eine vorgeschaltete Zusammenfassung oder zusätzliche Detailklappe gibt es nicht.
Unlesbare Werte und nicht verfügbare Prüfergebnisse bleiben als solche
gekennzeichnet.
Die Bedeutung von Skalen und Messkanälen steht unter
[Hardware und Sensoren](HARDWARE-SENSORS.md).

Die physischen Datenträger haben jeweils eine gemeinsame rote Karte:
**Lesen** in Rot, **Schreiben** in Gold, **Aktiv** in Türkis und **Queue** in
Violett. Jede Reihe zeigt Wert, Einheit, Verlauf und Balken. Aktivität hat
die feste Skala 0–100 Prozent; Durchsatz und Queue skalieren mit dem jeweiligen
Verlauf. Die Skala steht neben jeder Reihe, die Höhen verschiedener Reihen
sind daher nicht direkt vergleichbar. Die Windows-Abfrage erfolgt weiterhin
alle 15 Sekunden. Fehlende Werte bleiben `--`; nach einer fehlgeschlagenen
Abfrage werden alte Verläufe geleert.

Neonrahmen und Kurven verwenden separate weichgezeichnete Leuchtkonturen,
damit Text und Messwerte scharf bleiben. Das Mainboard hat im Tool und
Bildschirmschoner dieselbe zusätzliche Glow-Ebene. Bei ausgeschalteter
Animation bleibt der statische Glow sichtbar.

Unter **Hardware → Mainframe / Board** öffnet sich die Neon-Platine. Bauteile
und Beschriftungen sind anklickbar; Details enthalten die erfassten
Windows-Geräte, Status und verfügbare Eltern-ID rechts neben der Zeichnung.
Zeichnung und Geräteliste scrollen getrennt. **Board einpassen** zeigt die
Gesamtansicht; 75, 100 oder 125 Prozent vergrößern die Zeichnung mit
horizontalem und vertikalem Scrollen. Die ausführliche Legende lässt
sich ausklappen. Laufwerke erscheinen
einzeln, auch bei identischen Modellen. Die RAM-Zeichnung zeigt höchstens
acht Riegel, während die Details die erfassten Einträge behalten.

**Hardware neu lesen** aktualisiert die Momentaufnahme nach USB-, Monitor-
oder Audioänderungen. Audio zeigt Standardrollen und aktive Sitzungen:
Grün bedeutet Standardrolle, Gelb aktive Sitzung. Eine Sitzung kann auch
still sein; es handelt sich nicht um eine Pegelanzeige. Abgetrennte
Audio-Endpunkte bleiben in den Details. Bluetooth-Dienstknoten und
mehrere Schnittstellen eines Geräts sind keine zusätzlichen Gehäuse.

Die Zeichnung bildet keine echte Leiterbahn-, PCIe-Lane- oder
Gehäusetopologie ab. Lüfter drehen dekorativ. Rundinstrumente wechseln
etwa alle sechs Sekunden durch zuordenbare Kanäle und nennen Hardware,
Messgröße und Einheit. Gruppenmaxima sind als solche bezeichnet.

**Darstellung / Desktop** in der linken Leiste oder im Tray öffnet den
Tag-/Nachtmodus. **Automatisch** verwendet die lokale Uhrzeit: Tag von
07:00 bis vor 19:00 Uhr, Nacht außerhalb dieses Zeitraums. **Immer Tag**
und **Immer Nacht** übersteuern den Zeitplan. Das Retro-Neon-Design und
die vorhandenen Skins bleiben erhalten. Die Zeithintergründe sind tagsüber
blau und nachts schwarz; Neonlinien, Bauteile und Glow bleiben farbig.
Es werden weder Standortdaten noch Sonnenaufgangszeiten abgefragt.
Die Wahl wird lokal gespeichert und auch beim separaten Schonerstart gelesen.
Ein frischer Cockpit-Livefeed überträgt die aktuelle Wahl an den Schoner.

Die Uhr verwendet die aktuelle lokale Systemzeit, nicht den Zeitpunkt der
letzten Hardwareprobe. Die Uptime läuft zwischen erfolgreichen Windows-Proben
weiter. Zeitkorrekturen von Windows werden in der Uhranzeige übernommen.

## Windows Modern

Retro-Neon bleibt das Standarddesign. Nach der Installation lässt sich unter
**Darstellung / Desktop → Design** oder
**Systemkontrolle → Einstellungen → Skin / Fensteroberfläche**
**Windows Modern** wählen. Die neutrale Oberfläche folgt der Windows-
Einstellung für helle oder dunkle Apps. Beim Speichern bietet AETHER
**Jetzt neu starten** oder **Später** an; das laufende Cockpit wechselt
erst beim Neustart.

Das Mainframe heißt dort **System-Schema**. Geräte erscheinen in neutralen
Karten mit anklickbaren Details. Die animierten Verbindungen sind illustrative
Datenflüsse, keine gemessenen Leiterbahnen oder Transferraten.
Der Animationsschalter und die Windows-Einstellung für reduzierte Bewegung
bleiben wirksam.

Mit gespeichertem Windows Modern startet der AETHER-Bildschirmschoner nicht;
eine laufende Sitzung endet bei der nächsten Einstellungsprüfung.
Diese Sperre gilt bereits vor dem aufgeschobenen Cockpit-Neustart.
Zurück zu Retro-Neon macht den Schoner wieder verfügbar; die Windows-Auswahl
und Wartezeit werden dabei nicht automatisch geändert.

Die Desktopoberfläche bleibt zunächst deutsch. Englisch ist in Vorbereitung
und wird noch nicht zur Auswahl angeboten. Installer, Windows-Dialoge und
externe Werkzeugausgaben behalten ihre jeweils vorhandene Sprache.

## xBar, Gadgets und Overlay

**Diagnose → AETHER xBar an/aus** öffnet die Diagrammleiste. Im Zahnradmenü
CPU, RAM, GPU, Temperaturen, Netzwerk und Laufwerksbelegung getrennt wählen.
Torten zeigen belegt in Lila und frei in Türkis.

**PIN** dockt die Leiste links oder rechts an; **FLOAT** erlaubt Ziehen am
Titel und speichert beide Positionsachsen. Monitorwahl und Windows-Skalierung
werden berücksichtigt. Auto-hide gilt nur für angehefteten Einzelmonitorbetrieb.
Die aktuelle xBar führt keine Programme oder Dateien aus.

**System Control → Einstellungen → Desktop-Gadgets** oder das Tray-Menü
öffnet die Desktopparameter. Gadgets am Titel verschieben. Sie liegen auf
der Explorer-Desktop-Ebene hinter normalen Fenstern. Fehlt diese Ebene,
meldet AETHER den Fehler; es ersetzt sie nicht durch ein dauerhaft vorderstes
Fenster.

Hauptfenster, xBar und Gadgets haben getrennte Deckkraftregler von 20 bis
100 Prozent. Jedes Gadget hat zusätzlich einen eigenen Regler. Das
Temperatur-Overlay wird separat geöffnet, verwendet dieselben Sensorwerte
und meldet den Übergang in einen Warnzustand. Exklusives Vollbild kann
Overlay und xBar verdecken.

**Das X blendet das Hauptfenster nur aus.** Messung, Gadgets und aktivierte
Überwachung laufen weiter. Im Tray lassen sich Gadgets minimieren,
wiederherstellen oder schließen. **AETHER vollständig beenden** beendet
die Anwendung. Der genehmigte Anmeldestart öffnet Tray und Gadgets;
portable Nutzung richtet ihn nicht ein.

## Bildschirmschoner

Im Setup den optionalen Schoner wählen; beim Update dafür gegebenenfalls
**Konfiguration neu festlegen** verwenden. In Windows **Bildschirmschoner
ändern → AETHER Mainframe 1986** auswählen und übernehmen.
Wartezeit und **Anmeldeseite bei Reaktivierung** werden in Windows gesetzt.
Das Setup aktiviert den Schoner nicht automatisch.

`AetherMainboard.scr /s` startet Vollbild. `/c` zeigt nur den Hinweis,
dass keine eigenen Einstellungen verfügbar sind. `/p <Handle>` beziehungsweise
`/p:<Handle>` startet die eingebettete Windows-Vorschau.
Maus oder Tastatur beendet Vollbild; ein Monitorwechsel beendet die Sitzung.
Jeder Monitor erhält die vollständige Szene, die auch auf 4K skaliert.
Breite Displays erweitern Himmel und Boden, nicht die Platine.

Der Schoner initialisiert keine Sensoren und verlangt keine Administratorrechte.
Live-Werte kommen über eine lesende lokale Named Pipe aus AETHER im selben
Windows-Konto und derselben Sitzung. Ohne Verbindung erscheint eine
Hardware-Momentaufnahme mit **Keine Live-Messwerte**; veraltete Werte werden
verworfen. Das Inventar der verbundenen Anzeige wird etwa jede Minute erneuert.

Die Schonerszene zeigt nur eindeutig interne Laufwerke. USB/UASP, SD/MMC,
optische Medien und unbekannte Bustypen bleiben ausgeschlossen; die
Cockpit-Inventarliste bleibt vollständig. Einzelne Storage-Werte benötigen
eine eindeutige Modellzuordnung.

### Animation

Schoner, Hauptfenster und Dashboard verwenden verstärkte Neon-Halos,
Pulslicht und Energiebewegung. Bei ausgeschalteter Bewegung bleiben
statische Leuchteffekte und Messwerte erhalten. Sichtbarkeit und
Windows-Animationseinstellungen begrenzen die laufenden Effekte.

Der Bildschirmschoner verwendet und verwaltet keine Musikdateien.
Eine frühere Audiokonfiguration wird weder gelesen noch aktualisiert.
Vorhandene externe Dateien bleiben unverändert.

Die Szene beachtet Windows-Animationen und bei Verbindung den AETHER-Schalter.
Bewegung reduziert statische Inhalte, verhindert aber nicht verlässlich
OLED-Einbrennen. Monitorabschaltung, passende Helligkeit und Pixelpflege
weiter nutzen. Der Schoner ersetzt nicht die Windows-Sperre.

## Hilfe und Arbeitsabläufe

Das Hilfe-Center ist lokal und durchsuchbar. Die vier Skins werden unter
**System Control → Einstellungen** gewählt und wirken sofort auf offene
Fenster. Ihr Glas-/Fluent-Eindruck ist eine Gestaltung, keine zugesagte
Windows-Acrylic- oder Mica-Integration; ein Skinname ist keine
Windows-Kompatibilitätsangabe.

Typische Abläufe:

1. **Fehler untersuchen:** Zeitpunkt notieren, Ereignisse und Messwerte prüfen,
   passenden Windows-Assistenten starten und Bericht lesen.
2. **Reparatur vorbereiten:** Wartungsplan wählen, Vorschau und Absicherung
   prüfen, anschließend jeden Änderungsschritt freigeben.
3. **Software aktualisieren:** Liste laden, ID und Quelle kontrollieren,
   Auswahl bestätigen, Einzelresultate lesen und Liste erneut laden.
4. **Speicherplatz prüfen:** Große Dateien zunächst lesend finden;
   Cache-Bereinigung separat mit Dateiauswahl und Löschbestätigung ausführen.

Die freie Konsole ist eine Ausnahme vom geführten Ablauf: Enter führt den
eingegebenen Befehl sofort aus. [Verwaltung und Wartung](SYSTEM-CONTROL.md)
erläutert Rücknahme, Abbruch und Protokolle.
