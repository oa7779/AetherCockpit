# AETHER System Control

**Kostenloses, lokales Windows-Cockpit** fuer Monitoring, Diagnose und Wartung -
von Olaf Weins.

> **Grundregel:** Lesen passiert automatisch. Aendern niemals ohne deine
> ausdrueckliche Bestaetigung. Keine Cloud, kein Tracking - alles bleibt
> auf deinem Rechner.

## Download

Die aktuelle portable Version gibt es unter
**[Releases](../../releases/latest)** - ZIP herunterladen, vollstaendig
entpacken, `SystemCockpit.exe` starten. Keine Installation noetig; die
.NET-Laufzeit ist enthalten.

Neben jedem Release liegt die **SHA256-Pruefsumme** (`.sha256`-Datei).
Pruefen vor dem Entpacken:

```powershell
Get-FileHash .\AetherCockpit-Portable.zip -Algorithm SHA256
```

Beim ersten Start fuehrt ein kurzer Assistent durch die Funktionsuebersicht,
die Lizenzvereinbarung (Zustimmung erforderlich) und erstellt ein lokales
Rechner-Inventar.

---

## Was AETHER macht - ausfuehrlich

AETHER buendelt Beobachten, Auswerten, Pruefen und gezieltes Eingreifen in
einer Oberflaeche im eigenstaendigen Retro-Neon-Design. Die Hauptseiten
folgen genau diesem Arbeitsfluss; eine Navigationsleiste mit Zurueck/Vor
(auch Maus-Daumentasten und Alt+Pfeiltasten) fuehrt jederzeit durch die
Module, und ein durchsuchbares Hilfe-Center erklaert jede Funktion
Schritt fuer Schritt.

### Beobachten - live sehen, was der Rechner tut

- **Cockpit:** Rundinstrumente und Live-Kurven fuer CPU-Gesamtlast, RAM,
  GPU und Netzwerkdurchsatz, dazu CPU-/GPU-Temperatur, Paketleistung,
  Hotspot, VRAM, Luefterdrehzahlen und Taktraten - gemessen etwa alle zwei
  Sekunden ueber Windows-Kernzaehler und Hardware-Sensoren. Ein Systempuls
  sammelt alle aktuellen Warnungen an einer Stelle. Was die Hardware nicht
  meldet, zeigt AETHER ehrlich als "Nicht verfuegbar" - niemals als 0 und
  niemals geschaetzt.
- **Overlay-Panel:** ein kompaktes Immer-im-Vordergrund-Fenster mit CPU-,
  GPU- und SSD-Temperatur plus CPU-Last - z.B. fuers Spielen. Beim
  Ueberschreiten der selbst einstellbaren Warnschwellen faerben sich Werte
  rot, ein Banner nennt die Komponente, ein Hinweiston ertoent einmalig.
- **Taskmanager:** durchsuchbare Live-Prozessliste (CPU-Anteil korrekt auf
  die gesamte Maschine normiert, RAM, I/O) und ein eigener Verlaufsgraph
  fuer **jeden logischen CPU-Kern**. Prozessdetails (Threads, Pfad,
  Speicher) werden auf Klick rein lesend angezeigt.
- **Sensor-Matrix:** saemtliche lesbaren Sensoren (Temperaturen, Spannungen,
  Luefter, Takte, Leistungen) mit Filter. Von Laufwerken gemeldete
  Grenzwerte werden klar als Grenzwerte gekennzeichnet und nicht mit
  Ist-Temperaturen vermischt. ATX-Spannungen (12V/5V/3,3V) und die
  CMOS-Batterie werden gegen einstellbare Toleranzen geprueft.
- **Laufwerke:** Windows-Storage-Health und Speicherplatz aller Volumes auf
  einen Blick; die physischen Datentraeger zusaetzlich mit Lese-/Schreibraten
  und Aktivzeit im Cockpit.

### Auswerten - Messungen festhalten und vergleichen

- **Messauswertung:** laufende MIN/MAX/AVG-Statistik ueber alle Messkanaele,
  pausierbar und als CSV exportierbar.
- **Aufzeichnung:** auf Wunsch schreibt AETHER Messwerte als Tagesdateien
  (Intervall einstellbar). Typischer Ablauf: Aufzeichnung starten, eine
  Stunde spielen oder arbeiten, stoppen - danach den Verlauf im Log-Viewer
  ansehen, Spitzen finden oder zwei Logs direkt vergleichen (z.B. vor/nach
  einer neuen Luefterkurve).
- **Frame-Telemetrie:** echte Frame-Zeiten eines laufenden Spiels lassen
  sich fuer einen waehlbaren Zeitraum mitschneiden und auswerten:
  durchschnittliche FPS, P99-Frametime und 1%-Low - sauber getrennt pro
  Renderkette.

### Pruefen - Zustand und Sicherheit verstehen

- **Diagnose:** lesende Windows-Pruefungen (automatisch alle 60 Sekunden)
  zu Systemzustand, Abstuerzen der letzten 30 Tage (Windows Error
  Reporting), Stabilitaetsereignissen (Kernel-Power/WHEA), Netzwerk-
  adaptern und mehr. Ergebnisse vorhandener Windows-Speicherdiagnosen
  (RAM-Test) werden aus dem Ereignislog gelesen.
- **Diagnosebericht-Export:** erzeugt einen Textbericht aller aktuellen
  Pruefwerte mit Vorschau. Eine standardmaessig aktive Schwaerzung ersetzt
  Rechnername, Benutzername, Benutzerpfade sowie IP- und MAC-Adressen durch
  Platzhalter - geeignet zum Teilen in Foren oder mit Helfern.
- **Rechner-Inventar:** eine lokale Momentaufnahme von Hardware (Windows,
  Mainboard, CPU, GPU, RAM, Datentraeger, Volumes, Netzwerkadapter) und
  aller installierten Programme (Name, Version, Herausgeber) - anzeigbar
  und als Textdatei speicherbar.
- **Sicherheit:** Status von Defender, Firewall, BitLocker, Secure Boot,
  TPM und Updates - rein lesend, mit klarer Ampel. "Unbekannt" wird nie
  als sicher dargestellt.
- **SMART-Tiefendiagnose:** vollstaendige SMART-Berichte je Laufwerk
  (Gesundheitsstatus, Betriebsstunden, Fehlerzaehler, Temperaturen,
  Fehler-Logs). Selbsttests (kurz/lang/selektiv) starten nur nach
  Bestaetigung; die Faehigkeiten des Laufwerks werden vorher gelesen.
- **Zertifikate:** Offline-Pruefung der Root-/Benutzer-Zertifikatspeicher
  auf ablaufende oder auffaellige Eintraege - ohne jede Online-Abfrage.

### Eingreifen - gezielt und immer bestaetigt

- **Cleaner:** feste, bekannte Cache-Kategorien (Browser-Caches, App-Caches
  wie Kommunikations- und Spieleplattformen, Temp, CrashDumps, Prefetch u.a.)
  in vier Stufen von "Schnell" bis "Experte". Erst Vorschau mit Bewertung
  (Risikostufe, Anzahl, MiB), dann werden **nur ausdruecklich angekreuzte
  Dateien** geloescht - endgueltig, mit erneuter Pruefung jeder Datei direkt
  vor dem Loeschen. Verknuepfungen, gesperrte oder veraenderte Dateien
  werden uebersprungen; jede Aktion wird lokal protokolliert.
- **Paket-Center:** Software-Verwaltung ueber den Windows-Paketmanager:
  installierte Programme auflisten, verfuegbare Updates anzeigen, im Katalog
  suchen, per Mehrfachauswahl installieren, aktualisieren oder
  deinstallieren. Vor jedem Lauf zeigt ein Dialog die vollstaendige Liste
  der exakten Paket-IDs; nicht eindeutig lesbare Eintraege bleiben bewusst
  ohne Aktion.
- **Befehls-Katalog:** 103 erklaerte Windows-Befehle (Systemreparatur,
  Ereignisse, Prozesse/Dienste, Speicher/Treiber, Netzwerk, Sicherheit,
  Verwaltung) mit farbiger **Risikoanzeige** (LESEND / NETZWERK / AENDERND),
  Hinweisen "wann sinnvoll" und "was beachten" sowie klickbaren Parametern.
  Ein **Einsteigermodus** blendet alles Aendernde aus und sperrt die freie
  Konsole; der Expertenmodus bietet den vollen Umfang samt
  CRT-Phosphor-Terminal mit Live-Ausgabe, einzeiligem Fortschrittsbalken,
  Laufzeit, PID und Exitcode.
- **Superadmin:** statusbasierte Verwaltung von Diensten, Aufgaben,
  Firewall-Regeln, Paketen, Energieplaenen, Adaptern, Benutzern, Gruppen
  und ACLs: erst Status lesen, dann Zeile waehlen, Vorschau pruefen, einzeln
  bestaetigen - viele Aktionen mit vorbereiteter Ruecknahme.
- **Operations:** Wartungsplaene aus 17 Kategorien (u.a. Inventar,
  Komponentenpruefung und -reparatur, NTFS-Onlinescan, Defender-Scans,
  Laufwerksoptimierung, Windows-Update-Suche und gezielte Installation)
  mit Vorlagen, Vorschau, Wiederherstellungspunkt-Abfrage, Einzel-
  bestaetigung je Kategorie und dauerhaften Protokollen (inkl. HTML).
  Dazu der gespeicherte Windows-Leistungsindex und eine rein lesende
  Ferndiagnose fuer ausdruecklich eingetragene Arbeitsgruppen-Rechner.
- **Netzwerk:** Ping/Router-Ping, Scan des eigenen privaten LAN-Segments
  (mit MAC- und optionaler Herstellerzuordnung), WLAN-Uebersicht,
  Captive-Portal-Erkennung und ein Download-**Speedtest** - jeder aktive
  Test startet erst nach Freigabe mit klarem Hinweis.
- **Schluessel:** gespeicherte WLAN-Passwoerter, BitLocker-
  Wiederherstellungsschluessel und einen im BIOS hinterlegten
  Windows-Produktschluessel nur auf ausdruecklichen Klick nach Warndialog
  anzeigen. Die Anzeige verschwindet beim Verlassen; nichts wird
  gespeichert oder exportiert. Ist kein Schluessel vorhanden, sagt AETHER
  genau das - es wird nie einer "errechnet".
- **Spuren:** lesende Analyse der Explorer-Ordnerspuren in der Registry -
  zeigt, welche Ordner (auch geloeschte) Windows sich gemerkt hat.
  Bewusst ohne Loeschfunktion.
- **Schnellzugriffe:** die originalen Windows-Werkzeuge (Task-Manager,
  Geraete-Manager, Datentraegerverwaltung, Ereignisanzeige, Dienste,
  Aufgabenplanung, Einstellungsseiten) direkt oeffnen.

### Bedienung und Oberflaeche

- **Vier Skins**, sofort umschaltbar: Retro-Neon (Original), ein dunkler
  Glas-Look in Blau, eine glaenzende Bonbon-Variante und ein flacher,
  dezenter moderner Look. Die Inhalte bleiben in allen Skins gut lesbar.
- **Hilfe-Center** mit ueber 20 durchsuchbaren Themen: je Modul eine
  Schritt-fuer-Schritt-Anleitung, Sicherheitsregeln und ehrliche Grenzen.
- **Tooltips ueberall**, Risiko-Badges, Brotkrumen-Navigation und eine
  logisch gruppierte Seitenleiste.

### Sicherheits- und Ehrlichkeitsprinzipien

1. **Nichts aendert sich ohne Bestaetigung.** Jede loeschende, reparierende
   oder installierende Aktion zeigt vorher genau, was passieren wird.
2. **Keine erfundenen Werte.** Nicht lesbare Sensoren bleiben
   "Nicht verfuegbar"; Grenzwerte werden nicht als Messwerte ausgegeben;
   ein beendeter Reparaturlauf wird nicht pauschal als "gesund" verkauft.
3. **Alles bleibt lokal.** Einstellungen, Protokolle, Berichte und das
   Inventar liegen unter `%LOCALAPPDATA%\AetherCockpit`. Keine Cloud,
   keine Telemetrie, keine Hintergrunddienste.
4. **Schutz bleibt an.** AETHER schaltet keine Windows-Schutzfunktionen ab
   und installiert keine Treiber im Hintergrund.

---

## Systemvoraussetzungen

- Windows 10 oder 11, 64-Bit (x64)
- Administratorrechte (UAC-Abfrage beim Start; noetig fuer Hardware-Sensoren)

## Hinweis zu Windows-Warnungen

Die EXE ist nicht codesigniert. Windows zeigt daher ggf. "Unbekannter
Herausgeber" bzw. eine SmartScreen-Warnung - ueber *Weitere Informationen ->
Trotzdem ausfuehren* startet das Programm. Virenscanner koennen wegen der
tiefen Hardware-/PowerShell-Zugriffe per Heuristik warnen (False Positive).
Im Zweifel die ZIP mit der Pruefsumme verifizieren.

## Lizenz

Die Nutzung setzt die Zustimmung zur **EULA** voraus (liegt dem Download bei
und wird beim ersten Start angezeigt). Kostenlos fuer private Nutzung;
keine Gewaehrleistung, Nutzung auf eigene Verantwortung.

Enthaltene Fremdkomponenten unter eigenen Lizenzen:

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) - MPL 2.0
- [smartmontools](https://www.smartmontools.org/) - GPL v2
- [PresentMon](https://github.com/GameTechDev/PresentMon) - Intel/MIT, Lizenz im Paket

(c) 2026 Olaf Weins
