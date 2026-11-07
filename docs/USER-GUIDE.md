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

Die Hauptseiten führen von Beobachtung über Auswertung und Diagnose zur
Verwaltung: Cockpit, Taskmanager, Sensoren, Laufwerke, Messauswertung,
Diagnose, Sicherheit, Cleaner, Ereignisse, Superadmin, Operations und Hilfe.
Die erweiterte Systemkontrolle bietet unter anderem SMART, Pakete, Wartung,
Netzwerk, Spuren und Einstellungen.

**Zurück/Vor**, Maus-Daumentasten und `Alt+Pfeil links/rechts` bewegen sich
durch den lokalen Besuchsverlauf. Er hält bis zu 100 Schritte im RAM.
**Cockpit** und **Hilfe** springen direkt zur jeweiligen Seite.

**Funktion finden / Strg+K** kombiniert Arbeitsbereich und Suchbegriffe.
Der Treffer erklärt Wirkung und Schritte und öffnet den passenden Bereich.
Die Suche selbst startet keinen Auftrag und aktiviert keinen Expertenmodus.

## Cockpit und Mainboard

Das Cockpit zeigt Live-Instrumente, Verlaufskurven und Windows-Befunde.
Ausklappbare Bereiche schaffen Platz für CPU-Kerne, Prozesse und
Datenträger. Unlesbare Werte erscheinen als nicht verfügbar.
Die Bedeutung von Skalen und Messkanälen steht unter
[Hardware und Sensoren](HARDWARE-SENSORS.md).

Unter **Cockpit → Mainboard / 1986** öffnet sich die Neon-Platine. Bauteile
und Beschriftungen sind anklickbar; Details enthalten die erfassten
Windows-Geräte, Status und verfügbare Eltern-ID. Laufwerke erscheinen
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
