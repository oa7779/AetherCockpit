# AETHER — RC-Readiness-Audit

## Auftrag, Prüfstand und Aussagegrenzen

**Meilenstein 0: Bestandsaufnahme. Keine Freigabe zur Implementierung.**

Geprüft wird der vorhandene Arbeitsbaum auf `rc/grosse-entfesselung`,
ausgehend von `5382388` (`Fix GPU cooler fan occlusion`). Der neue Branch
sichert den Commitbezug, **nicht** die bereits vorhandenen uncommitteten
Änderungen. Vor dem Audit bestanden 48 geänderte und 13 nicht nachverfolgte
Dateien. Diese Arbeiten bleiben erhalten.

Die Prüfung unterscheidet:

- **STATISCH BELEGT:** im aktuellen Quelltext, einer Konfiguration oder einem
  Dokument gefunden. Das beweist nicht die erfolgreiche Ausführung.
- **HISTORISCH VERIFIZIERT:** in dieser Arbeitssitzung vor dem Audit ausgeführte
  Prüfungen; keine Wiederholung während Meilenstein 0.
- **NICHT VERIFIZIERT:** kein ausreichender Nachweis, insbesondere für reale
  Hardware, Installation, Dauerbetrieb oder einen Microsoft-Tenant.
- **VORSCHLAG:** Zielarchitektur oder künftige Abnahme, noch nicht implementiert.

Ein erfolgreicher Build ist kein Laufzeittest. Screenshots belegen Darstellung,
nicht die ausgeführte Verwaltungsfunktion. Synthetische Tests belegen keine
Hardwarekompatibilität. Diese statische Sicherheitsbetrachtung ist weder
Penetrationstest noch Sicherheitsfreigabe.

Während dieses Audits wurden keine Builds, Tests, Produktstarts, Sensorzugriffe,
Benchmarks, Reparaturen, Updates, Installationen oder Remoteaktionen ausgeführt.
Es wurden keine Versionen, Pakete, Abhängigkeiten oder Schutzrichtlinien geändert.

### Leseabdeckung — historischer Auditstand vor Freigabe 0.5

Die folgenden Angaben dokumentieren die ursprüngliche Prüflücke unverändert
nachvollziehbar. Der neue Prüfstand, Sicherungsnachweise und Entscheidungen
werden im Nachtrag „Freigabe 0.5“ am Dokumentende sowie im
[Einzeldatei-Leseverzeichnis](RC-SOURCE-READING-MANIFEST.md) ausgewiesen.

Die fünf beauftragten Hauptdokumente und die 14 vorhandenen Markdown-Anleitungen
direkt unter `docs` wurden gelesen; ebenso die AETHER-Lizenz. Historische Bilder
und Binärpakete sind keine Textlektüre. Die beiden älteren UX-Dokumente sind
Planungsbelege, keine aktuelle Freigabe für eine Cockpitvereinfachung.

Server und Clientdienst: alle 40 nicht generierten Dateien vollständig statisch
gelesen. Im Lieferungsbereich wurden 53 der 54 Textdateien aus Tests, Build und
Workflow vollständig gelesen (10.127 Zeilen einschließlich aller 7.233
C#-Testzeilen). Die übrige Datei, das 8.643-zeilige generierte
Lokalisierungsmanifest, wurde vollständig strukturiert geparst, nicht manuell
Zeile für Zeile geprüft. Zusätzlich vollständig gelesen: alle sieben
Projektdateien, Solution, Publishprofil und drei Tool-Provenienz-/Lizenztexte.
Desktop, Schoner und Shared umfassen 158 inventarisierte Dateien, davon 156
Textdateien und zwei Bildressourcen. Vollständig gelesen wurden insbesondere
alle 72 Dateien unter `SystemCockpit/Services`, MainWindow-XAML und Codebehind,
ControlCenter-Codebehind, AdminWorkbench-Codebehind, FrameGlow sowie
Shared-Katalog, Runner und WindowsUpdateSelection.

**Historischer Audit-Ausgangsstand: Die beauftragte Volllektüre war nicht abgeschlossen.** Konkret fehlten
lückenlose Volllektüren von `ControlCenterWindow.xaml`,
`ControlCenterWindow.ManagedActions.cs`, `ControlCenterWindow.Packages.cs` und
`Controls/OperationsHub.xaml.cs`; außerdem ist die vollständige Lektüre weiterer
UI-/Control-/Ressourcendateien nicht nachgewiesen. Für diesen Rest liegt kein
erschöpfendes, abschließend bereinigtes Einzeldateiverzeichnis vor.
Eine exakte Gesamtzahl vollständig gelesener Desktopdateien wird deshalb
nicht behauptet. Ausschnitt-/Suchbelege zu diesen Dateien bleiben solche.
Generierte Dateien und Binärressourcen sind von einer semantischen
Quelltextprüfung zu unterscheiden.

Der Bericht deckt die 22 geforderten Themen als Entscheidungsvorlage ab,
erfüllt aber wegen dieser Leselücke **noch nicht sämtliche Anforderungen an
den Abschluss von Meilenstein 0**. Die Lücke ist kein Anlass, produktiven
Code vorzeitig zu ändern.

## 1. Tatsächlicher Projektstand

| Punkt | Nachweis und Bewertung |
|---|---|
| Aktuelles Cockpitprofil | `0.7.0-beta.44`, Datei `0.7.0.44`, Assembly `0.7.0.0`, selbstenthaltenes `win-x64`; [Publishprofil](../src/SystemCockpit/Properties/PublishProfiles/PortableBeta.pubxml):3–12 |
| Lokaler Cockpitinstaller | Beta 44, 83.787.625 Bytes; SHA256 `dad4ced3e01ba38d3d5d5fe7a3d80473e42e296da527b3de3b3301182934f289`; Sidecar stimmt überein; im Audit nur Metadaten/Hash/Authenticode gelesen |
| Signatur dieses Installers | `NotSigned`, kein Signerzertifikat. Kein Signing-Schritt im [Workflow](../.github/workflows/windows-build.yml):148–178 |
| Separater Serverinstaller | Lokale Beta 40 laut [Übergabe](../PROJEKTSTAND.md); nicht Bestandteil des Cockpitinstallers. Neue Serverausgabe aus aktuellem Beta-44-Profil: **NICHT VERIFIZIERT** |
| Öffentlicher Stand | Beta 38 laut [README](../README.md):15–31; Remotezustand während des Audits nicht erneut abgefragt |
| Letzter Build vor diesem Audit | Beta-44-Releasebuild ohne Warnungen/Fehler; historisch verifiziert. Inno Setup meldete den bekannten Hinweis zum veralteten Architekturbezeichner `x64`; nicht mit einem warnungsfreien Gesamtpaketbuild verwechseln |
| Letzte Desktoplaufzeittests | Historisch 4.477 Grund-/Interface-/Branding-/Schonerassertions gegen publizierte Assemblies sowie 3.611 Modern-/Neon-Smoke-Assertions; synthetische Darstellung, kein realer Hardwarestart |
| Letzte Managementtests | Historisch 384 Assertions einschließlich 162 HTTPS-/Rollen-/CSRF-/Sitzungsprüfungen, 150 Konten- und 14 Flottenprüfungen; synthetische Graph-Anfragen, kein Tenantbetrieb |
| Letzte Renderprüfung | Historisch p95 25,0 ms bei Budget 33,4 ms; WPF-Kompositionsintervalle, keine GPU-Present-FPS und keine Idle-Baseline |
| Englisch | In der Oberfläche gesperrt; vorliegendes Manifest enthält 873 bestätigte offene Sites und `english_complete=false`; vollständige Übersetzung und semantische Abnahme **NICHT VERIFIZIERT** |
| Installierter Produktbetrieb | Installation, Update, Deinstallation, Dienstbetrieb und reales Enrollment dieses Standes **NICHT VERIFIZIERT** |

Die historische Prüfbasis steht in der [Übergabe](../PROJEKTSTAND.md):5–72.
Ein sauberer, alle lokalen Funktionen enthaltender Quellcommit existiert für
diese Arbeitskopie nicht. Eine RC-Kennzeichnung wäre deshalb verfrüht.
Der Sprachstand stammt aus dem vorhandenen
[Lokalisierungsmanifest](../tests/desktop-localization-audit.json):19–52,8636–8643:
1.053 inventarisierte Sites, davon 211 direkte und 662 verfasste UI-Textstellen
als unübersetzt bestätigt. Null fehlende explizite Ressourcen beweist keine
vollständige Übersetzung. Das Manifest wurde im Audit nicht neu erzeugt;
ältere Übergabezahlen wie 824 sind kein aktueller Neulauf.

## 2. Architekturkarte

```text
Windows / Hardware / optionale Werkzeuge
 ├─ Win32 / CIM / Registry / Performance Counter / Core Audio / PnP
 ├─ LibreHardwareMonitor → Sensorprovider
 └─ smartctl / PresentMon / Npcap / Windows PowerShell / Windows-Werkzeuge
             ↓
SystemCockpit (erhöhte WPF-Anwendung)
 ├─ MainWindow → CockpitModel / DashboardModel → Instrumente, Tabellen, Charts
 ├─ Systemkontrolle → Workbench / Pakete / Konsole / Diagnose / Einstellungen
 ├─ DesktopController → Tray, xBar, Gadgets, Overlay
 ├─ lokale Hilfe, Export, History, Operations-Berichte
 ├─ BoardLiveFeed → lokale Named Pipe → AetherScreensaver (asInvoker)
 └─ optional ManagerReportingService ── HTTPS ────────────────┐
                                                            ↓
AetherClient.Service (optional, LocalSystem) ── HTTPS → AetherManager.Server
 ├─ Heartbeat / Inventar / feste Aufträge                ├─ Web-UI / OpenAPI
 └─ Claim-/Ergebnispuffer                                ├─ Konten/Rollen/CSRF
             ↕ AetherOperations.Shared                  ├─ JSON-Stores / Audit
             Katalog / Runner / Updateauswahl           └─ optional MSAL → Graph
```

Die Desktopmessung, Windows-Probe und Ereignisabfrage laufen getrennt;
UI-Übernahme erfolgt ins Modell. Der Schoner teilt Board-/Darstellungscode
mit dem Cockpit, initialisiert aber keinen eigenen Sensorprovider.
Server und Clientdienst sind separat aktivierte Prozesse, kein notwendiger
Hintergrundbetrieb des Einzelplatz-Cockpits.

Belege: [MainWindow](../src/SystemCockpit/MainWindow.xaml.cs):17–34,144–153,299–388;
[Schonersitzung](../src/AetherScreensaver/SaverSession.cs):63–116;
[Serverstart](../src/AetherManager.Server/Program.cs):118–175;
[Clientworker](../src/AetherClient.Service/ManagedClientWorker.cs):26–97.

## 3. Funktionsinventar

| Bereich | Vorhandener Umfang | Abgrenzung |
|---|---|---|
| Cockpit | Ursprüngliche dichte Instrumente, Kernlasten, RAM/Commit, GPU, Sensoren, physische Disk-Karten, Prozesse, Ereignisse, Systempuls, Datenkontrolle | Keine neue Summary-Seite oder vorgeschaltete Detailklappe |
| Hardware | Sensor-Matrix, Board/PnP, Volumes, Windows-Storage, smartctl-Diagnose | Schematische Platine, keine echte Lane-/Leiterbahntopologie |
| Auswertung | Statistik, CSV, Messlogs/Vergleich, PresentMon/Framezeiten | Begrenzte Reihen/Dateien; Capture misst keine allgemeine Systemgesundheit |
| Diagnose | Windows-Assistenten, Ereignisse, Schutzstatus, Inventar, Diagnoseexport, optionale Hintergrunddiagnose | Einzelne Befunde statt pauschaler Gesundheitsnote |
| Verwaltung | Prozesse, Dienste, Aufgaben, Features, Energie, Adapter/DNS/Proxy/Firewall, Konten/Gruppen, ACL, Run-Autostart, acht Registry-Einstellungen | Rücknahme nur je unterstützter Aktion; Expertenmodus ist keine Rechtegrenze |
| Wartung | 17 Kategorien, Planvorlagen, 25 gemeinsame Werkzeugaktionen, Windows Update und WinGet, Cleaner | Änderungen einzeln bestätigen; Cleaner löscht endgültig |
| Netzwerk | Adapter/TCP, Ping/DNS, LAN-Scan, bestätigtes Wake-on-LAN, Captive Portal, Downloadtest, optionale Npcap-Aufnahme, WinRM-Diagnose | Magic-Packet-Versand beweist keinen Gerätestart; Capture ist keine vollständige Protokollanalyse |
| Desktop | xBar, Gadgets, Tray, Overlay, Multi-Monitor-Schoner, Skins, Tag/Nacht, Windows Modern | Modern bleibt neutral; AETHER-Schoner dort gesperrt |
| Management | Enrollment/Freigabe, Berichte, Heartbeats, feste Aufträge, Konten/Rollen, Flotte, Audit | Einzelserver-JSON, keine freie Remote-Shell, keine MFA/HA |
| Microsoft Graph | Intune-Geräte lesen/Sync; bestehende Autopatch-Deployments lesen/pausieren/fortsetzen | Optional serverseitig, Autopatch Beta, keine neuen Rollouts |
| Hilfe | Lokale Hilfe und Funktionssuche; 36 `HelpTopic`-Deklarationen statisch gefunden | Noch kein eigenständiges Neon Wiki |

Quellen: [Handbuch](USER-GUIDE.md), [Verwaltung](SYSTEM-CONTROL.md),
[Netzwerk](NETWORK.md), [Management](CENTRAL-MANAGEMENT.md);
[Hilfekatalog](../src/SystemCockpit/Services/HelpCatalog.cs):9 ff.;
[Aktionskatalog](../src/AetherOperations.Shared/ManagedActionCatalog.cs):8–51.
Lexikalische `Add`-Treffer im Befehlskatalog sind keine belastbare Anzahl
der zur Laufzeit expandierten Befehle.

## 4. Technische Schulden und Konsolidierungsbedarf

| Priorität | Befund | Nächster belastbarer Schritt |
|---|---|---|
| P0 | Umfangreicher Dirty Tree einschließlich neuer Graph-/Branding-/Setupdateien; Branch ist kein vollständiger Rückkehrpunkt | Autorisierten lokalen Snapshot mit Datei-/Hashmanifest und Wiederherstellungsprobe festlegen |
| P0 | Dokumente widersprechen sich: Graph integriert vs. „noch nicht integriert“; ursprüngliches Cockpit vs. alte Summary-Beschreibung | Aussagen gegen Quellen konsolidieren; nichts aus einem älteren Plan ungeprüft implementieren |
| P1 | Bestätigung, Fehlerdarstellung, Journale und Abbruch unterscheiden sich zwischen Workbench, Operations, festen Aktionen und freier Konsole | Aktionsvertrag mit Wirkung, Rechten, Bestätigung, Ergebnis, Abbruch und Rücknahme inventarisieren; bestehende Schutzmechanik erhalten |
| P1 | Hauptfenster, Systemkontrolle, Tray und Suche bieten mehrere Zugänge zum selben Ziel | Routen-/Zielinventar testen, nicht Funktionen streichen; langfristige Fensterkonsolidierung separat entscheiden |
| P1 | Hardwarequellen besitzen unterschiedliche Identitäten, Einheiten, Alter und Fehlerzustände | Gemeinsames Metadatenmodell schrittweise einführen, keine unbekannten Werte ergänzen |
| P1 | JSON-Stores haben keine storeübergreifenden Transaktionen oder allgemeine Schema-/Rollbackmigration | Backup-/Restore-/Schema- und Ausfallmodell vor Änderungen spezifizieren |
| P1 | Jobs/Audits erreichen harte Grenzen; Berichte/Operations können Speicher beanspruchen | Aufbewahrung, Archivierung, Speicherbudget und sichtbaren Vollzustand definieren |
| P2 | Synchrone/periodische Abfragen und UI-Geometrie können Arbeit erzeugen, die im Tray nicht benötigt wird | Erst messen, dann einen zentralen Bedarf-/Sichtbarkeitsplan entwerfen |

Belege: [AdminWorkbench](../src/SystemCockpit/Controls/AdminWorkbench.xaml.cs):64–176;
[ManagementService](../src/AetherManager.Server/Services/ManagementService.cs):120–122,216–270;
[JsonManagerStore](../src/AetherManager.Server/Services/JsonManagerStore.cs):209–231,350–369.
Dies ist eine Priorisierung, kein Nachweis bereits aufgetretener Ausfälle.

## 5. Ressourcenprobleme und Performance-Baseline

**Aktuelle reale Ressourcenlast: NICHT VERIFIZIERT.** Für Kalt-/Warmstart,
Zeit bis Bedienbarkeit, Scanzeit, CPU/GPU, Private Bytes/Working Set, Handles,
Threads, Timer, I/O, Prozessstarts, Netzwerk und Standby-/Langzeitverhalten
liegen in diesem Audit keine neuen Messungen vor.

Statisch erkennbar:

- Livepfad ungefähr alle zwei Sekunden, zusätzlich Abfragelaufzeit:
  native Werte, Sensoren, Prozesse und Adapter
  ([MainWindow](../src/SystemCockpit/MainWindow.xaml.cs):299–357;
  [DashboardMetrics](../src/SystemCockpit/Services/DashboardMetrics.cs):31–101).
- Windows-Probe etwa 60 Sekunden, Ereignis-/Diskpfad etwa 15 Sekunden;
  weder diese Intervalle noch ein sichtbares Diagramm sind ein Beleg für
  die tatsächliche Last.
- NativeMetrics und DashboardMetrics lesen beide Adapterdaten. Ob gemeinsame
  Snapshots netto nützen, ist zu messen; keine spekulative Cacheänderung.
- Dashboardhistorie 90 Punkte; Sensorcharts 180 Zyklen. Daten wachsen dort
  nicht unbegrenzt ([DashboardModel](../src/SystemCockpit/DashboardModel.cs):9–48;
  [SensorChartHistory](../src/SystemCockpit/Services/SensorChartHistory.cs):11–12,68–70).
- Unsichtbare Boards und Rahmen besitzen Abschaltpfade. Die zentrale
  Telemetrie darf im Tray weiterlaufen, weil Gadgets, Alarmierung und Feed
  sie benötigen können; ein pauschales Stoppen wäre eine Regression.
- Servermonitoring alle fünf Sekunden; Clientpause 30 Sekunden nach dem
  Zyklus. Lange Aufträge verlängern den Heartbeatabstand.

### Begrenzter statischer Performance-Scan

Elf Dateien: DashboardModel, MainWindow-Codebehind, FrameGlow, fünf
HardwareBoardView-Teile, DashboardMetrics, NativeMetrics, SensorChartHistory.
Gezählt wurden Quelltextvorkommen, **keine bewiesenen Performancefehler**:

| Muster | Anzahl | Einordnung |
|---|---:|---|
| `.Result`, `.Wait(`, `.GetAwaiter().GetResult(` | 0 | Kein solcher Treffer in diesem begrenzten Scope |
| `async void` | 8 | MainWindow-Ereignishandler; Signatur allein ist kein Fehler |
| LINQ-Memberaufrufe aus der erfassten Methodenliste | 158 | Auch kalte Zeichen-/Ladepfade enthalten; nicht pauschal ersetzen |
| Replace/Substring/Groß-/Kleinschreibung | 4 | Kein gemessener Engpass |
| `new Regex` / `GeneratedRegex` | 0 | Keine Aussage über andere Dateien |
| `new HttpClient` | 0 | Keine Aussage über die gesamte Anwendung |
| `Task.Run` | 13 | Auslagerung und Lebensdauer je Aufruf prüfen, keine automatische Optimierung |
| Eindeutige Klassentypen | 2 nicht sealed, 8 sealed, 1 static | Partial-Deklarationen zusammengefasst; keine pauschale Sealing-Maßnahme |

LINQ-Zähler pro Datei: DashboardModel 31, MainWindow 27, Board.Drawing 40,
Board.Glow 3, Board.Landscape 14, Board.Modern 6, Board.Codebehind 17,
DashboardMetrics 2, NativeMetrics 2, SensorChartHistory 16, FrameGlow 0.
Ein wiederholbarer Vollscan aller Performancekategorien ist damit
**nicht** behauptet.

**VORSCHLAG für spätere Messung:** identische Hardware, Build, Datenmenge,
Fenstergröße, DPI, Theme, Energieprofil und Aufwärmphase dokumentieren;
je sichtbarer Liveansicht, Tray, minimiert, Schoner je Monitor, fehlendem
Provider, Offline und Resume messen. Mittelwerte, p95, Maxima und
Beobachtungsdauer nennen. Grenzwerte erst vor der jeweiligen Abnahme
vereinbaren. Live/Ausgewogen/Ruhe sind Zielprofile, noch keine vorhandene
Funktion. Sie dürfen aktive Überwachung und Datenfrische nicht verschleiern.

## 6. Sicherheit, Rechte und Datenschutz

### Vorhandene Grenzen

- Cockpit `requireAdministrator`, zusätzliche Tokenprüfung; Schoner
  `asInvoker` ([Cockpitmanifest](../src/SystemCockpit/app.manifest):7;
  [App](../src/SystemCockpit/App.xaml.cs):19–23;
  [Schonermanifest](../src/AetherScreensaver/app.manifest):6).
- Geführte Änderungen behalten Vorschau, Bestätigung und Zielzustandsprüfung.
  Freie PowerShell startet dagegen mit Enter unmittelbar im erhöhten Kontext.
  Dies ist vorhandenes Power-User-Verhalten, **keine** Absicherung beliebiger
  Eingaben durch den geführten Katalog.
- Clientdienst optional LocalSystem; Änderungen brauchen lokales Opt-in,
  zentrale Policy und konkrete Bestätigung. Automatik ist auf den erneut
  geprüften DNS-Cache-Befund begrenzt.
- Netzwerkserver HTTPS/konkreter Hostfilter; Gerätetokens und Enrollmentcodes
  serverseitig gehasht; Clienttoken DPAPI. Lokale Konten mit Identity-Hash,
  Rollen, absolut 30 Minuten Sitzungsdauer, Secure/HttpOnly/SameSite-Cookie,
  Same-Origin und CSRF für Sitzungsmutationen.
- Keine freie Remote-Befehlsschnittstelle in den geprüften Endpunkten.
  Katalog, Clientprüfung und Runner sind als gemeinsame Kette zu betrachten.

Belege: [AdminTokenFilter](../src/AetherManager.Server/Middleware/AdminTokenFilter.cs):16–78;
[Konten](../src/AetherManager.Server/Services/LocalAccountService.cs):63–104,193–204;
[Clientkonfiguration](../src/AetherClient.Service/ClientConfiguration.cs):7–37;
[Dienstdateischutz](../build/Assert-ServiceFilesProtected.ps1):1–61.

### Offene Architektur- und Betriebsfragen

1. **Dauerhaft erhöhte Oberfläche:** heute erforderlich durch Manifest,
   nicht damit als technisch nötig für jede Anzeige bewiesen.
   Eine normale UI plus enger privilegierter Helper ist ein eigener Umbau:
   IPC-Authentisierung, feste Aktionen, Zielidentität, Benutzer-/Adminprofil,
   Sensorzugriff und UAC-Ausfälle müssen zusammen getestet werden.
2. **Klartext-Bootstrap:** `admin-token.txt` und der ausgegebene Erstcode sind
   sensible lokale Klartextdateien. ACL-Schutz ist keine Verschlüsselung.
   Der Legacyzugang ist nach Bootstrap standardmäßig aus, der Token bleibt
   aber vorhanden. Rotation, Lebensdauer und geschützte Bereitstellung sind
   vor RC zu entscheiden
   ([JsonManagerStore](../src/AetherManager.Server/Services/JsonManagerStore.cs):19–76;
   [AdminTokenFilter](../src/AetherManager.Server/Middleware/AdminTokenFilter.cs):16–46).
3. **Datenminimierung:** Reports sind JSON-Payloads ohne fachliche
   Feld-Allowlist/Redaktion am Server. Gerätekennungen, Pfade und Ereignisse
   können sensibel sein. Reader/Operator dürfen GET-Berichte und Graphinventar
   lesen; das Rollenmodell ist mit dem Einsatzfall abzugleichen.
4. **Aufbewahrung:** Reports 90 Tage; Jobs/Audits ohne automatische Löschung,
   harte Kapazitätsgrenzen. Operations-Berichte ohne allgemeine Frist.
5. **Export:** Musterschwärzung ist kein vollständiger Identifikatorschutz.
   IPv6, Seriennummern, PnP-/Monitor-/Audio-IDs und Freitext brauchen einen
   feldbezogenen Exportmodus; vorhandene Vorschau erhalten.
6. **Betriebsnachweis:** echte ACLs, Dienstkonto, Zertifikatszugriff, DNS/SAN,
   Firewall, Backup/Restore und Enrollment sind **NICHT VERIFIZIERT**.
   Es wurden keine bestätigten ausnutzbaren Schwachstellen ermittelt.

Die vollständig gelesene gemeinsame Ausführungskette akzeptiert nur
unveränderte Katalogaktionen. Der Runner verwendet Windows-PowerShell aus
dem Systemverzeichnis mit `-NoProfile`, `-NonInteractive` und
`-EncodedCommand`; keine freie Remote-Skriptübergabe. Standardmäßig bleiben
65.536 Zeichen je Ausgabestrom erhalten, konfigurierbar bis 8 MiB.
Timeout: 30 Minuten. Bei Abbruch/Timeout wird der eigene Prozessbaum beendet
und die Ausgabe weiter entleert. Das ist keine Rücknahme bereits erfolgter
Windows-Änderungen
([ManagedActionRunner](../src/AetherOperations.Shared/ManagedActionRunner.cs):9–33,75–174).
Windows-Updateauswahl bindet GUID, Revision und EULA-Hash; Prüfung und
Einzelresultate sind ein eigener Pfad
([WindowsUpdateSelection](../src/AetherOperations.Shared/WindowsUpdateSelection.cs)).
Diese Quelllektüre schließt die anfangs offene Runner-Abhängigkeit der
Serverprüfung, ersetzt jedoch keinen Ausführungs-/Abbruchtest.

Lokale Ablagen und Uninstallfolgen stehen in
[Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md):94–151 und
[Installation](INSTALLATION.md):125–173. Datenschutz-/Lizenztexte sind
keine juristische Freigabe.

## 7. Aktuelle Rahmen- und Animationstechnik

`FrameGlow` ist ein WPF-Attached-Behavior mit Adorner, kein zentrales
semantisches Frame-Control. Es trennt statische Leuchtränder von bewegten
Konturen. Sichtbarkeit, `Enabled`, Windows-Animationsfreigabe und Lebenszyklus
steuern die Bewegung. Modern deaktiviert den Neon-Glow.

Technische Ursache der umlaufenden Linien: zwei getrennte Rechtecke für
Trail und Core mit `StrokeDashArray` und wiederholten
`StrokeDashOffset`-Animationen; der Trail animiert zusätzlich seine Opazität.
Drei statische Randlagen bilden die Kontur; die breite Glow-Lage nutzt
`BlurEffect` und `BitmapCache`, ohne den Inhalt zu verwischen.
`Stop()` entfernt Sichtbarkeits-/Systemparameter-Handler und die drei
Animationsclocks; Unload bzw. ausgeschaltete Attached-Flags entfernen den
Adorner ([FrameGlow](../src/SystemCockpit/Controls/FrameGlow.cs):58–141).

Die Bewegung der Rahmen ist von anderen Effekten zu trennen:
Board-Datenfluss, dekorative Lüfter, Landschaft, Instrumentwechsel,
Brandingpuls und Lichtreflex sind keine umlaufenden Panelkonturen.
Ein Austausch des Frames darf diese unabhängigen Darstellungen nicht
versehentlich entfernen oder das Dashboard umordnen.

Belege: [FrameGlow](../src/SystemCockpit/Controls/FrameGlow.cs):12–112;
[ModernWindowAppearance](../src/SystemCockpit/Services/ModernWindowAppearance.cs):31–44;
[HardwareBoardView](../src/SystemCockpit/Controls/HardwareBoardView.xaml.cs):59–69,132–140,314–319;
[AnimatedBrand](../src/SystemCockpit/Controls/AnimatedBrand.xaml.cs).

## 8. Dateien der umlaufenden Linien

| Oberfläche | Einbindung |
|---|---|
| Hauptfenster und Panelstil | [MainWindow.xaml](../src/SystemCockpit/MainWindow.xaml):28–30,52,73,93–94,132 |
| Diagrammleiste | [SidebarWindow.xaml](../src/SystemCockpit/SidebarWindow.xaml):40 |
| Diagrammvorlagen | [DiagramTemplates.xaml](../src/SystemCockpit/Controls/DiagramTemplates.xaml):28,45,59 |
| Eingabeelemente | [NeonInputs.xaml](../src/SystemCockpit/Controls/NeonInputs.xaml):14,27,38; statische Glow-Einbindung nicht mit jedem animierten Panel gleichsetzen |
| Bildschirmschoner | [SaverWindow.cs](../src/AetherScreensaver/SaverWindow.cs):70–78,105,153–170 |
| Erzeugung und Animation | [FrameGlow.cs](../src/SystemCockpit/Controls/FrameGlow.cs) |

Diese Liste benennt die gefundenen zentralen Einbindungen. Für den Umbau
zusätzlich Ressourcen-/Stilvererbung und erzeugte Adorner prüfen; keine
rein textuelle Ersetzung aller `DoubleAnimation`-Vorkommen.

## 9. Vorschlag für AetherNeonFrame

**VORSCHLAG, nicht implementiert:** zentrale, zum vorhandenen Theme-System
passende Rahmenkomponente bzw. dessen kompatibles Attached-Behavior.
Ein kleines Proof entscheidet die Form, nicht ein vorschneller Gesamtumbau.

- Scharfer Kern, separater äußerer und optional innerer Glow; Inhalt und Text
  unweichgezeichnet. Geometrie bei Größenänderung, nicht pro Animationsframe.
- Keine StrokeDashOffset-Laufspur, Rotation oder wandernde Panelkontur.
  Ruhiger statischer Rahmen; optional langsames Atmen der Opazität.
- Zustände `Normal`, `Analysis`, `Notice`, `Error`, `Admin`, `Offline`.
  Bedeutung zusätzlich als Text/Icon, Farbe allein genügt nicht.
- Fehler nicht hektisch pulsieren lassen; Adminzustand kennzeichnet Kontext,
  ersetzt niemals eine Aktionsfreigabe. Offline zeigt Alter/fehlende Werte.
- Bestehender Animationsschalter und Windowsfreigabe; minimiert/unsichtbar/
  entladen keine aktiven Clocks. Statischer Glow bleibt bei Bewegung aus.
- Keine neuen häufigen Timer, Layoutanimationen oder laufend erzeugten
  Bitmaps. Ressourcen einfrieren/wiederverwenden, Blurfläche messen.
- Neon/Tag/Nacht/Skins und Modern getrennt testen; stufenweise Einführung
  mit identischem Inhalt und altem Verhalten als begrenztem Rückfallpfad.

Proof-Abnahme: gleiche Cockpitanordnung, alle Zustände, Bewegung aus,
Hide/Minimize/Unload, DPI, mehrere Monitore und Vorher-/Nachhermessung.

## 10. Vorhandene Hardware-Datenquellen

| Bereich | Tatsächliche Quelle | Aussage und Grenze |
|---|---|---|
| CPU/GPU-Modell | `Win32_Processor`, `Win32_VideoController` | Windows-Meldung; keine vollständige Architektur-/Featuredatenbank |
| CPU/GPU/Sensoren | LibreHardwareMonitor, Provider-ID/Hardware/Typ/Einheit | Nur gemeldete verfügbare Kanäle; Schwellen nicht als Istwert |
| RAM gesamt | `GlobalMemoryStatusEx` | Aggregierter Windows-Speicherzustand |
| RAM-Module | `Win32_PhysicalMemory` | Locator, Hersteller, PartNumber, Kapazität, gemeldeter konfigurierter Takt; keine SPD-Rohdaten |
| Mainboard/Windows | `Win32_BaseBoard`, `Win32_OperatingSystem` | Windows-/Firmwareangaben; keine elektrische Topologie |
| BIOS/Firmware | Separater Katalogbefehl | Keine belegte vollständige strukturierte BIOS-Ansicht |
| Volumes | `DriveInfo` | Logische Kapazität/Belegung, nicht physische Laufwerksidentität |
| Physische Disks | Windows Performance Counter, PhysicalDisk/Reliability | Read/Write, Aktivität/Queue und verfügbare Windows-Healthdaten |
| SMART | Separates smartctl-JSON | ATA/NVMe-Gerätezugriff; Windows-Reliability ist kein Ersatz für vollständiges SMART |
| Netzwerk | Adapterzähler/.NET, CIM/Windows-Werkzeuge | Durchsatzdifferenzen, erster Punkt Baseline; VPN-Doppelzählung möglich |
| Monitore | Windows Display-API, `WmiMonitorID` | Aktive Geräte/Auflösung/Name; vollständiger EDID-/Serienbericht nicht belegt |
| Audio | MMDevice/Core Audio | Endpunkte, Standardrollen, Sitzungsstatus; keine Pegel-/Qualitätsmessung |
| USB/PnP | Vorhandene PnP-Geräte, Elternbeziehung | Windows-Gerätebaum, keine gesicherte Gehäuse-/Porttopologie |
| Software | HKLM/HKCU-Uninstall-Registry | Kein vollständiges AppX-/aller Benutzer-Inventar |
| Servertelemetrie | Host-/Prozesszähler, `DriveInfo` | Alle fünf Sekunden; Temperatur ausdrücklich nicht verfügbar |

Quellen: [WindowsProbe](../src/SystemCockpit/Services/WindowsProbe.cs):101–105,174–178;
[HardwareSensors](../src/SystemCockpit/Services/HardwareSensors.cs):40–96;
[NativeMetrics](../src/SystemCockpit/Services/NativeMetrics.cs):17–40,62–88;
[HardwareTopology](../src/SystemCockpit/Services/HardwareTopology.cs):14–26,132–181;
[DisplayTopology](../src/SystemCockpit/Services/DisplayTopology.cs):8–34;
[AudioTopology](../src/SystemCockpit/Services/AudioTopology.cs):8–81;
[SystemInventory](../src/SystemCockpit/Services/SystemInventory.cs):21–117;
[SmartService](../src/SystemCockpit/Services/SmartService.cs):18–61,90–118;
[Servermonitor](../src/AetherManager.Server/Services/ServerMonitoringService.cs):14–101.

## 11. Fehlende Hardwareinformationen

Eine einheitliche Hardwarezentrale mit Herkunft, Vertrauensstufe und
Datenschutzsteuerung je Feld fehlt. In der geprüften Inventarkette sind
insbesondere folgende vollständige Informationswege **NICHT VERIFIZIERT**:

- CPU-Topologie/Cache/Instruktionsmerkmale und NPU-Fähigkeiten über den
  Modellnamen hinaus; GPU-VBIOS, PCIe-Link und VRAM-Details je Hersteller.
- BIOS/UEFI-Version, Firmwarezustand und Updatequelle als zusammenhängender
  Datensatz; Board-Chipsatz-/PCIe-Lane-Informationen.
- SPD-/JEDEC-/XMP-/EXPO-Rohdaten, bestückte vs. verfügbare Slots und
  vollständige Timingangaben. CIM-Takt nicht in SPD-Daten umbenennen.
- Eindeutige Zuordnung physische Disk ↔ Partition ↔ Volume ↔ SMARTgerät
  über alle Controller/USB-Bridges/RAIDfälle.
- Vollständiges EDID, HDR-/Farbraum-/Linkdetails und belastbare
  Monitorseriennummern; USB-Controller-/Hub-/Geschwindigkeitstopologie.
- Durchgängige Firmware-, Treiber-, Quellenzeit- und Fehlerdarstellung sowie
  feldbezogener Schutz von Seriennummern und Gerätekennungen.

Das ist eine Lückenliste, kein Auftrag für zusätzliche Low-Level-Treiber.
Nicht dokumentiert oder nicht sicher abfragbar bleibt „nicht verfügbar“.
Herstellerdatenbanken liefern Spezifikationen, keine Live-Messwerte.

## 12. Quellen- und Vertrauensmodell

**VORSCHLAG:** Jeder relevante Feldwert erhält
`Value`, `Unit`, `Source`, `SourceType`, `Timestamp`, `Confidence`,
`Availability`, `RequiresElevation`, `IsEstimated`, `IsCalculated`,
`IsSensitive`, `Error`, `DocumentationLink`.

| Stufe | Bedeutung | Darstellung |
|---|---|---|
| Direct | Unmittelbar von dokumentierter API/Provider gemeldet | Quelle und Mess-/Abrufzeit nennen; nicht als absolute Wahrheit behandeln |
| Derived | Aus belegten Eingangswerten berechnet | Formel, Eingangsquellen und Zeitraum nachvollziehbar |
| Database | Externe Spezifikation/Zuordnung | Datenbankversion, Lizenz und exakter Match; kein Messwert |
| Unconfirmed | Nicht eindeutig zuordenbar/widersprüchlich | Nicht als bestätigte Eigenschaft oder Livewert ausgeben |

`Unavailable`, veraltet, Zugriff verweigert und Providerfehler getrennt
halten. Null ist nur bei einer gültigen Nullmessung null. Ein geschätzter
Wert benötigt sichtbare Kennzeichnung, niemals einen stillen Ersatzwert.
`Confidence` zunächst kategorial begründen, keine erfundenen Prozentwerte.
Stabile Geräte-/Kanalidentitäten und Einheiten vor Export/API-Versionierung
festlegen; alte Logs und Pipeverträge nicht still brechen.

## 13. Logo- und Lizenzkonzept

Die eigene [MIT-Lizenz](../LICENSE) und [Drittanbieter-Hinweise](../THIRD-PARTY-NOTICES.md)
liegen vor. Direkte Komponenten: LibreHardwareMonitorLib 0.9.6,
smartmontools 7.5, PresentMon 2.6.0 und MSAL 4.92.0; Windows-/OpenAPI-Pakete
laut Projektdateien. Npcap ist separat, Sysinternals/ADK werden nicht gebündelt.
Transitive Lizenzvollständigkeit und sämtliche Toolhashes gegen den
Herkunftsmanifestbestand: **NICHT VERIFIZIERT**.

Für das bestehende AETHER-Logo ist eine nachvollziehbare Ursprungs-/
Rechtefreigabe aus der allgemeinen Quellcodelizenz allein nicht bewiesen.
Für zusätzliche Herstellerlogos liegt im geprüften Stand keine freigegebene
Logo-Registry vor. Es wurden keine Logos bezogen oder hinzugefügt.

**VORSCHLAG:** Manifest je Asset mit Rechteinhaber, offizieller Quelle,
Lizenz/Freigabenachweis, erlaubtem Verwendungszweck, Änderungserlaubnis,
Attribution, Version/Hash und Prüfdatum. Nur freigegebene Assets verteilen;
keine automatische Websuche, kein Kopieren, Umfärben oder Animieren fremder
Marken ohne entsprechende Erlaubnis. Fallback: eigene neutrale Symbole und
Herstellertext, keine Partnerschaftsbehauptung. Hardwaredatenbanken haben
ein separates Lizenz-/Aktualisierungsmodell.

## 14. Microsoft-Admin-Capability-Matrix

Die Matrix gehört in Meilenstein 0 in diesen Bericht; das eigenständige
Fachdokument entsteht erst nach Freigabe. Sie ist eine systematische
Auswahl des beauftragten Arsenals, **kein Vollständigkeitsnachweis sämtlicher
Microsoft-Werkzeuge**. „Vorhanden“ unterscheidet API, geführte Aktion,
Katalog und externen Start. Adminangaben beschreiben die Aktion; das
Cockpit läuft derzeit trotzdem insgesamt erhöht.

Teststatus: **S** = Quellpfad statisch belegt, reale Ausführung im Audit
**NICHT VERIFIZIERT**; **F** = passende synthetische Fixture vorhanden,
nicht neu ausgeführt; **O** = Integrations-/Abnahmetest offen.
Wiki: **P** = Artikel/Deeplink geplant, noch kein Neon Wiki.

| Bereich | Funktion | Datenquelle | Werkzeug/API | Lesend/ändernd | Adminrechte | Risiko | Vorhanden | Erweiterung sinnvoll | Wiki | Teststatus |
|---|---|---|---|---|---|---|---|---|---|---|
| Windows-Basis | System-/Hardwarestatus | Windows | Win32/.NET/CIM | Lesend | Teilweise | Kennungen | API/Probe | Quellenmetadaten | P | S/F |
| PowerShell | Befehle ausführen | Windows PowerShell | PS 5.1 | Beides | Aktionsabhängig | Freie erhöhte Ausführung | CRT-Konsole | Klare Lauf-/Abbruchgrenzen | P | S/F |
| PowerShell 7 | Separater Host | Installiertes pwsh | pwsh | Beides | Aktionsabhängig | Andere Modulkompatibilität | Kein belegter austauschbarer Host | Nur explizit geprüft | P | O |
| Komponenten | Zustand/Reparatur | Komponentenstore | DISM | Beides | Für Reparatur ja | Änderungen, Quelle/Neustart | Geführt/Katalog/Plan | Gemeinsame Diagnosekette | P | S/F |
| Systemdateien | Prüfen/reparieren | Windows-Dateien | SFC | Beides | Ja | Dateiersatz | Geführt/Plan | Ergebnisparser verifizieren | P | S/F |
| Dateisystem | Onlinescan | NTFS | CHKDSK `/scan` | Ändernd eingestuft | Ja | Onlinekorrekturen | Geführter Plan | Kein pauschales `/f`/`/r` | P | S/F |
| Dateisystem | Detailmetadaten | Dateisystem | fsutil | Aktionsabhängig | Teilweise | Falsche Schreibparameter | Vollständige Integration nicht belegt | Lesende Auswahl prüfen | P | O |
| Ereignisse | Lesen/filtern/XML | Eventlog | Get-WinEvent/Event Log | Lesend | Kanalabhängig | Sensible Freitexte | UI/Collector/Katalog | Quellen-/Fehlerlinks | P | S/F |
| Ereignisse | Kanalverwaltung | Eventlog | wevtutil | Beides | Teilweise | Loglöschung | Geführte Verwaltung nicht belegt | Zuerst lesend | P | O |
| Aufgaben | Status/Start/Aktivierung | Aufgabenplanung | PowerShell/schtasks | Beides | Für Verwaltung ja | Persistenz/Seiteneffekte | Workbench/Setup | Aufgabenidentität prüfen | P | S/F |
| Dienste | Status/Starttyp/Start/Stopp | SCM | Service-Cmdlets/Windows | Beides | Für Änderung ja | Ausfall/Abhängigkeiten | Workbench | Abhängigkeiten/Rücknahme | P | S/F |
| Geräte | PnP/Probleme/Treiber | CIM/PnP | PnP-Cmdlets/pnputil | Beides | Aktionsabhängig | Treiber-/Geräteausfall | Status/Katalog | SetupAPI nur begründet | P | S/F |
| Treiber | Version/Bestand/Sicherung | Windows-Treiberstore | CIM/PnPUtil/DISM | Beides | Für Export/Änderung teils | Bestand ist keine Aktualitätsprüfung | Diagnose/Katalog | Signatur/Quelle zuordnen | P | S/F |
| Energie | Pläne/Status/Diagnose | Windows-Energie | powercfg | Beides | Aktionsabhängig | Schlaf-/Energieverhalten | Workbench/Katalog | Standbydiagnose | P | S/F |
| Recovery | WinRE/Recovery öffnen | Windows Recovery | reagentc/Einstellungen | Beides | Aktionsabhängig | Reset kann Daten entfernen | Katalog/Windows-Übergabe | Keine stille Cloudreparatur | P | S/O |
| Boot | BCD anzeigen | Bootkonfiguration | bcdedit | Lesend vorgesehen | Ja | Schreibzugriff bootkritisch | Lesender Katalog | Keine automatische Änderung | P | S/O |
| Backup | Sicherung/Wiederherstellung | Windows Backup | wbadmin | Beides | Ja | Datenträger/Restore | Geführter Ablauf nicht belegt | Voraussetzungen zuerst prüfen | P | O |
| Netzwerk | Adapter/IP/Routen/ARP | Windows-Netzwerk | .NET/CIM/ipconfig/route/arp/netsh | Beides | Änderung teils | Verbindungsverlust | Diagnose/Workbench/Katalog | Gemeinsame Snapshots | P | S/F |
| DNS | Auflösung/Cache | Resolver/Adapter | Resolve-DnsName/ipconfig | Beides | Cacheänderung ja | Netzwerkverkehr/Fehldiagnose | Diagnose/enger Repairpfad | Recheck erhalten | P | S/F |
| Erreichbarkeit | Zielprüfung | Gewähltes Ziel | Ping/Test-NetConnection | Lesend, netzaktiv | Meist nein | Ziel/IP sichtbar | Werkzeuge/Katalog | Einwilligung/Timeout | P | S/F |
| Leistung | CPU/RAM/Disk/Prozess | Windows-Zähler | Performance Counter/Win32 | Lesend | Teilweise | Abfragekosten | Dashboard | Bedarf messen | P | S/F |
| ETW | Framecapture/Sitzungen | ETW | PresentMon/Abfragen | Lesend, Dateiausgabe | Berechtigungsabhängig | Last/Prozessmetadaten | Capture/Katalog | Kein allgemeiner Traceviewer | P | S/F |
| WER/Dumps | Absturzmetadaten | WER/Dumpdateien | Windows-Dateien/APIs | Lesend geplant | Pfadabhängig | Speicher kann Geheimnisse enthalten | Vollständiger Parser nicht belegt | Metadaten vor Vollanalyse | P | O |
| Windows Update | Suchen/Einzelinstallation | Konfigurierte Updatequelle | Windows Update COM | Beides | Ja | Lizenz/Neustart/Änderung | Lokal/Managed | Identitäts-/EULA-Bindung erhalten | P | S/F |
| Defender | Status/Signaturen/Scan | Defender | PowerShell | Beides | Aktionabhängig | Scan kann behandeln | Diagnose/Plan/Katalog | Richtlinienstatus erklären | P | S/F |
| Firewall | Status/Regeln | Windows Firewall | NetSecurity/Windows | Beides | Änderung ja | Aussperren/Exposition | Workbench/Katalog | Zielzustand/Rücknahme | P | S/F |
| Storage | Health/Volumes/Disks | Windows Storage | Storage-Cmdlets/DriveInfo | Beides | Aktionabhängig | Datenträgeränderung | Diagnose/Verwaltung | Eindeutige Zuordnung | P | S/F |
| SMART | Attribute/Tests | Laufwerksfirmware | smartctl | Beides | Gerätezugriff nötig | Test bleibt im Laufwerk aktiv | Separates Modul | Herstellergrenzen erklären | P | S/F |
| BitLocker | Status/Schlüssel | Windows | BitLocker-Cmdlets | Lesend, geheimniskritisch | Schlüssel ja | Recovery-Schlüssel offenlegen | Status/geschützte Anzeige | Feldschutz/keine Logs | P | S/F |
| TPM/Secure Boot/VBS | Schutzstatus | Windows/Firmware | CIM/Windows-Abfragen | Lesend | Teilweise | Interpretation | Teilstatus vorhanden | Zusammenhängende Quellenansicht | P | S/O |
| Pakete | Liste/Install/Update/Remove | WinGet-Quellen | winget | Beides | Paketabhängig | Fremdinstaller/Lizenzen | Paket-Center/Pläne | IDs/Quellen erhalten | P | S/F |
| Windows-Konsolen | Native Verwaltung öffnen | Installierte Windows-Tools | MMC/Einstellungen | Start, danach extern | Werkzeugabhängig | Externe Änderungen | Schnellzugriffe | Verfügbarkeit sichtbar | P | S/O |
| Wake-on-LAN | Magic Packet senden | Eingabe-MAC | UDP-Broadcast | Netzaktiv | Nicht generell nötig | Kein Empfangsnachweis | Bestätigter Ablauf | Hardwaretest separat | P | S/F |
| Deployment | Features/Capabilities/Images/Treiber | Windows/Imagequelle | DISM/PowerShell | Beides | Änderung ja | Image-/Komponentenänderung | Katalog/Inventar | Kein unbeaufsichtigtes Apply/Reset | P | S/F |
| ADK/WinPE | Bereitstellungswerkzeuge | Separate Installation | ADK | Beides | Aktionabhängig | Images/Partitionen | Externer Start/Bezugsseite | Eigene Freigabekette | P | S/O |
| Sysinternals | Vertiefte Diagnose | Separat installierte Tools | Autoruns/Procmon/Procexp u. a. | Werkzeugabhängig | Teilweise | Capture/Dumps/Änderungen | Optionaler Start/Bezugsquelle | Import nur mit dokumentiertem Format | P | S/O |
| Intune | Geräte lesen/Sync | Microsoft Graph | v1.0 API/MSAL | Beides | AETHER-Admin für Sync | Tenantänderung | Optional serverseitig | Tenantabnahme | P | S/F |
| Autopatch | Bestehende Deployments steuern | Microsoft Graph Beta | GET/PATCH | Beides | AETHER-Admin für Schreiben | Beta-/Rolloutauswirkung | Pause/Fortsetzen | Kein neuer Rollout ohne Auftrag | P | S/F |

Quellbasis: [Befehlskatalog](../src/SystemCockpit/Services/AdminCommandCatalog.cs);
[SystemActions](../src/SystemCockpit/Services/SystemActions.cs):17–31;
[AdminManagement](../src/SystemCockpit/Services/AdminManagement.cs);
[ManagedActionCatalog](../src/AetherOperations.Shared/ManagedActionCatalog.cs):8–51;
[GraphService](../src/AetherManager.Server/Services/MicrosoftGraphService.cs):30–57;
[System-Control-Anleitung](SYSTEM-CONTROL.md).

Offizielle Dokumentation für spätere Einzelprüfung:
[Windows commands](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/windows-commands),
[DISM](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-reference--deployment-image-servicing-and-management),
[PowerShell](https://learn.microsoft.com/en-us/powershell/windows/overview),
[Sysinternals](https://learn.microsoft.com/en-us/sysinternals/),
[Graph](https://learn.microsoft.com/en-us/graph/overview).
Externe Dokumentationsstände, Editions-/Buildvoraussetzungen und Lizenzen
wurden im Audit nicht online neu verifiziert. Vor jeder neuen Integration
konkrete API-/Befehlsquelle, Rechte, Timeout, Abbruch, Logging, Wiki und Test
einzeln nachweisen.

Sysinternals nur kontrolliert aus vorhandener Installation starten;
keine stillen Downloads, Bündelung oder automatische EULA-Annahme.
Autoruns, Process Explorer, Process Monitor, TCPView, RAMMap, VMMap, Sigcheck,
Handle, ListDLLs, ProcDump, PsTools, Sysmon, Coreinfo, AccessChk, Streams,
Junction, NTFSInfo, LiveKd und WinObj sind Prüfkandidaten, keine behaupteten
fertigen Integrationen.

## 15. Wiki-Konzept

**VORSCHLAG:** eigenständige lokale Wissensoberfläche mit versionierten
Artikeln und lokalem Suchindex. Kein dauerhaft laufender Webserver, kein
Cloudkonto, kein Tracking. WPF-Host als naheliegender Prototyp; ein separater
Prozess gegenüber einem eigenen Fenster ist noch zu entscheiden.

Artikel-ID und versionierter Deeplink verbinden UI, Fehler, Hardwarefeld,
Diagnosekette und Werkzeug. Links öffnen Wissen, führen keine Befehle aus.
Keine beliebigen Skripte oder externen Inhalte als ausführbares HTML laden.
Suche indiziert Titel, Synonyme, Fehlercodes, Ereignis-ID **mit Provider**,
Geräteklasse und Quellen. Fehlercodes allein sind kein Ursachenbeweis.

Ein Artikel enthält Kurzfassung, Zusammenhang, Datenquelle, angezeigte
Felder/Zustände, belegte Ursachen bzw. Hypothesen, sichere/erweiterte Prüfung,
Maßnahmen nach Eingriffstiefe, Risiken, Rechte, Rollback, AETHER-Deeplinks,
Windows-Werkzeuge, technische Details und offizielle Quellen mit Prüfstand.

Erster Prototyp: nicht verfügbare CPU-Temperatur/HVCI, NVMe-Temperaturgrenze
vs. Messwert, DISM/SFC-Kette, DNS-Diagnose, Windows-Update-EULA,
verwaister Managed-Auftrag und Datenschutz bei Dump/Export.
Diese Artikel werden erst nach Freigabe erstellt und geprüft.
Die 41 gewünschten Themenbereiche können die spätere Taxonomie bilden,
nicht automatisch tausende ungeprüfte Artikel. Offline-Suche, defekte Links,
Artikelversionen und Rücksprung zum richtigen UI-Ziel separat testen.

## 16. Changelog- und Dokumentationsbewertung

Der [CHANGELOG](../CHANGELOG.md) enthält rund 598 Zeilen und viele kleine
Beta-/Darstellungsänderungen. Einträge beantworten technische Ursache,
Migration und Verifikation nicht einheitlich. Beta 39/40 bzw. neuere
Serverarbeit sind nicht als konsistente Meilensteine abgebildet.
Ältere „letzter Release“-Aussagen sind historisch, nicht aktuell.

Konkrete Widersprüche:

- [README](../README.md): integrierte Graph-Anbindung im lokalen Stand,
  später noch „nicht integriert“.
- [FEATURES](FEATURES.md): Beta-38-Einleitung mit späteren Erweiterungen,
  Cockpit-Summary trotz Rückbau und doppelter Wake-on-LAN-Zeile.
- [CENTRAL-MANAGEMENT](CENTRAL-MANAGEMENT.md): lokale Konten vs. ältere
  Tokenloginbeschreibung; lokaler Serverinstaller noch Beta 39.
- [DEVELOPMENT](DEVELOPMENT.md): historischer Branch `main` und alte
  Paketbeispiele neben aktuellem Beta-44-Profil.
- [UX-REDESIGN-PLAN](UX-REDESIGN-PLAN.md): Rückbauhinweis ist maßgeblich;
  spätere alte Paket-2-Zielbilder sind keine neue Freigabe.
- [PROJEKTSTAND](../PROJEKTSTAND.md): umfangreiche parallele Versionschronik,
  alte Aussagen zu fehlender Kontenverwaltung und Branchständen.

**VORSCHLAG für Meilenstein 6:** vollständigen Changelog zunächst mit
Hashnachweis archivieren, danach öffentliche Geschichte auf technische
Meilensteine konzentrieren. Pro Eintrag Was/Warum/Ursache/Auswirkung,
Migration, echte Verifikation und Restgrenzen. Keine Daten erfinden.
Übergabe auf aktuellen Zustand und offene Arbeiten kürzen; Anleitungen
fachlich führen, nicht als zweite Versionsgeschichte.
**Jetzt keine Archivierung, Umschreibung oder Löschung der Historie.**

## 17. RC-Blocker

| ID | Priorität | Blocker | Nachweis zum Schließen |
|---|---|---|---|
| M0-READ | In 0.5 geschlossen | Ursprüngliche Desktop-/UI-/Ressourcenleselücke geschlossen | [Einzeldateiverzeichnis](RC-SOURCE-READING-MANIFEST.md): 320 Dateien, 279 Volllektüren, drei strukturierte Sonderfälle, 38 Binärdateien; kein offener Lesebeleg |
| RC-01 | Sicherungsanteil in 0.5 geschlossen | Aktueller Entwicklungsbestand extern hashgeprüft gesichert und Restore nachgewiesen; Commitfreigabe offen | [Abschlussbericht](MILESTONE-0-CLOSURE-REPORT.md); kein Commit und noch kein frischer Reproduzierbarkeitsbuild |
| RC-02 | P0 | Reale Installation/Portable/Update/Uninstall/Dienstaktivierung nicht nachgewiesen | Freigegebene isolierte Windows-Lebenszyklusmatrix einschließlich Datenerhalt |
| RC-03 | P0 | Realer Start/Hardware-Scan/Hauptworkflow/Resume/DPI/Monitore fehlen als Abnahme | Zielhardware-/OS-Matrix und protokollierte Ergebnisse |
| RC-04 | P0 | Keine reale Last-/Langzeit-/Shutdown-Baseline | Reproduzierbare Messung und vereinbarte Budgets vor Optimierung |
| RC-05 | P1 | Privilegien-/Kennungs-/Exportmodell erfüllt Zielbild noch nicht | Rechteentscheidung, feldbezogener Datenschutz, geprüfte Helpergrenze falls beauftragt |
| RC-06 | P1 | Neuer ruhiger semantischer Frame fehlt | Proof, Implementierung, Lifecycle-/DPI-/Theme-/Lastvergleich |
| RC-07 | P1 | Quellenmodell und vollständige Hardwarezentrale fehlen | Verifizierte Quellenfelder, Lückenanzeige, Identitäts-/Exporttests |
| RC-08 | P1 | Wiki-Prototyp und Quellen-/Deeplinkabnahme fehlen | Repräsentativer Offlineprototyp mit geprüften Artikeln |
| RC-09 | P1 | Betriebsgrenzen für JSON, Retention, Backup/Restore, verwaiste Jobs und Bootstrap offen | Ausfall-/Wiederanlauf-/Kapazitätstests und Betriebsanleitung |
| RC-10 | P1 | Logo-/Datenbankrechte und transitive Lizenzprüfung nicht vollständig belegt | Asset-/Lizenznachweise und neutrale Fallbacktests |
| RC-11 | P1 | Dokumentation widersprüchlich | Quellenbasierte Konsolidierung aller aktuellen Anleitungen |
| RC-12 | Bedingt P0 | Englisch-Gate für jedes `-rc.`-Profil noch nicht erfüllt | Vollständige Sprachfreigabe oder ausdrücklich beauftragte Änderung des RC-Sprachumfangs/Gates |
| RC-13 | Bedingt P0 | Reale Graph-/Autopatch-Beta-Abnahme fehlt, falls im RC freigegeben | Testtenant, Lizenz/Rechte/Zertifikat, Fehler-/Ergebnisprüfung oder sichtbar experimenteller Ausschluss |

Signierung ist offen, muss klar ausgewiesen und auf Zielrichtlinien geprüft
werden. Ein beantragtes Zertifikat ist keine Signatur. Ob unsignierter RC
zulässig ist, entscheidet Olaf; Schutzrichtlinien werden nicht umgangen.
Keine neuen reproduzierbaren Kernabstürze wurden im Audit nachgewiesen;
Abwesenheit solcher Fehler ist ebenfalls **NICHT VERIFIZIERT**.

## 18. Priorisierter Meilensteinplan

| Meilenstein | Umfang | Gate |
|---|---|---|
| 0 — jetzt | Bestandsaufnahme, Quellen, Grenzen, Blocker und Entscheidungen | Bericht vorlegen, anschließend stoppen |
| 1 — Konsolidierung | Reproduzierbarer Ausgangspunkt; Fehler/Logging/Cancellation/Timeout/Disposal; gemeinsame Quellen-/Metadatenverträge | Keine Funktionsverluste; gezielte bestehende Tests, Fehler-/Abbruchfälle |
| 2 — Neon Frame | Erst Proof, dann zentraler ruhiger Frame und stufenweise Migration | Anzeigeanordnung erhalten; semantische Zustände, Lifecycle und Lastvergleich |
| 3 — Hardwarezentrale | Quellenbasierte Feldgruppen, IDs, Datenschutz, Export, Datenbank-/Logofallback | Reale Quellenabnahme je Hardwareklasse, unbekannt bleibt unbekannt |
| 4 — Admin-Arsenal | Bestehende Abläufe zuerst prüfen; sinnvolle Lücken je Capability ergänzen | Jede Integration mit Rechten, Risiko, Bestätigung, Timeout, Abbruch, Journal, Wiki/Test |
| 5 — Neon Wiki | Lokaler Prototyp, Suchindex, Deeplinks, repräsentative Artikel | Offline-, Quellen-, Link- und Sicherheitsabnahme |
| 6 — Handschrift | Sachliche deutsche Texte, Begriffe, Status, Assistenten und Meilenstein-Changelog | Kein Verhaltensumbau durch Textpflege; Lokalisierung separat behandeln |
| 7 — RC-Härtung | Messbasierte Profile, Langzeitbetrieb, Lebenszyklus, Ausfälle, DPI/Monitore/Themes | Vereinbarte Budgets und reale Testmatrix bestanden |
| 8 — RC | Exitkriterien, bekannte Restfehler, konsistente Pakete/Hashes/Lizenzen | Ausdrücklicher Releaseauftrag; kein RC bei offenem P0 |

Jeder Meilenstein erhält einen eigenen Bericht und wartet auf Freigabe.
Kein großer Mischumbau, keine pauschale Versionsautomatik.

## 19. Betroffene Projekte und Dateien

Nur für die spätere Umsetzung, nicht als bereits geänderte Dateien:

| Paket | Erwartete Oberflächen |
|---|---|
| Konsolidierung/Quellen | [MainWindow](../src/SystemCockpit/MainWindow.xaml.cs), [CockpitModel](../src/SystemCockpit/CockpitModel.cs), [DashboardModel](../src/SystemCockpit/DashboardModel.cs), [Services](../src/SystemCockpit/Services), [Shared](../src/AetherOperations.Shared) |
| Frame | [FrameGlow](../src/SystemCockpit/Controls/FrameGlow.cs), [NeonInputs](../src/SystemCockpit/Controls/NeonInputs.xaml), [DiagramTemplates](../src/SystemCockpit/Controls/DiagramTemplates.xaml), [MainWindow-XAML](../src/SystemCockpit/MainWindow.xaml), [Sidebar](../src/SystemCockpit/SidebarWindow.xaml), [SaverWindow](../src/AetherScreensaver/SaverWindow.cs), Theme-/Modern-Anpassung |
| Hardware | [HardwareTopology](../src/SystemCockpit/Services/HardwareTopology.cs), [HardwareSensors](../src/SystemCockpit/Services/HardwareSensors.cs), [WindowsProbe](../src/SystemCockpit/Services/WindowsProbe.cs), [SystemInventory](../src/SystemCockpit/Services/SystemInventory.cs), [Board](../src/SystemCockpit/Controls/HardwareBoardView.xaml.cs), Export/History/Pipeverträge |
| Administration | [AdminManagement](../src/SystemCockpit/Services/AdminManagement.cs), [AdminCommandCatalog](../src/SystemCockpit/Services/AdminCommandCatalog.cs), [OperationsHub](../src/SystemCockpit/Controls/OperationsHub.xaml), [ControlCenter](../src/SystemCockpit/ControlCenterWindow.xaml.cs), [Shared-Runner](../src/AetherOperations.Shared/ManagedActionRunner.cs), Server/Client-Freigaben |
| Management | [Server](../src/AetherManager.Server), [Clientdienst](../src/AetherClient.Service), Dienstassistenten, Stores/Rollen/Graph und zugehörige Web-UI |
| Wiki/Texte | [HelpCatalog](../src/SystemCockpit/Services/HelpCatalog.cs), Funktionssuche, Ressourcen, Handbuch/Fachdokumente; Host/Artikeldateien erst nach Architekturfreigabe |
| Abnahme/Paketierung | [Desktoptests](../tests/SystemCockpit.Tests), [Managementtests](../tests/AetherManager.Tests), [Build](../build), [Workflow](../.github/workflows/windows-build.yml), [Publishprofil](../src/SystemCockpit/Properties/PublishProfiles/PortableBeta.pubxml) |

Eigene Sicherheits-, Frame-, Performance-, Wiki-, Capability- und
Exitkriterien-Dokumente sowie das Changelogarchiv gehören in die jeweils
freigegebenen Folgemeilensteine. Sie werden jetzt nicht als leere
Parallelstruktur erzeugt.

## 20. Teststrategie und Exitkriterien

Die vorhandenen Projekte sind Konsolenrunner; **kein `dotnet test`**.
Kommandos und Nebenwirkungen stehen in [TESTING](TESTING.md).

| Ebene | Bestehender Ansatz | Erforderliche Ergänzung/Abnahme |
|---|---|---|
| Modelle/Parser | Grundassertions, Datei-/Update-/Katalogfixtures | Metadaten/Einheiten/Fehler/Alter/Identität und Abwärtskompatibilität |
| Desktopdarstellung | Interface, Branding, Schoner, Modern, Board/GPU mit Testdaten | Semantischer Frame, Minimize/Hide/Unload, Tastatur, DPI/Monitore |
| Verwaltung | Rollen/CSRF/Sitzung, synthetische Jobs/Updates/Graph | Netzwerkverlust, Vollzustand, Wiederanlauf, Ergebnis unbekannt, echte Opt-in-Kette |
| Installer | Verify-Setup/Recovery/Uninstall vorhanden, nicht vollständig in CI | Freigegebene VM: Neuinstallation, erhaltendes Update, Recovery, Abbruch, Daten/ACL/Dienst/Uninstall |
| Pakete | Hash-/Versions-/ZIP- und Intune-Integritätsprüfungen | Aktuelles versionsgleiches Cockpit/Schoner/Client/Server, veröffentlichte Assemblies |
| Realbetrieb | Gesonderte Live-/Hardware-/Produktstartmodi | Autorisierte Hardware-/OS-Matrix; keine Reparaturen als Nebeneffekt |
| Ressourcen | Synthetisches Boardprofil vorhanden | Kalt/Warm/Idle/Tray/Live/4K/Resume/Langzeit mit gemessenen CPU/GPU/RAM/I/O |
| Wiki/Lizenzen | Noch kein Wiki-/Logo-Registry-Abnahmesatz | Offline-Suche/Deeplinks/Quellen/Fallbacks und Assetmanifest |

CI enthält Releasebuild, beide Runner, mehrere synthetische Desktopmodi,
48-Geräte-Browserfixture, Publish/Setup/Hashes. Installation, Intune-Rollout,
Hardware-End-to-End, realer Tenant und vollständige DPI-/Monitor-/Standby-
Matrix werden dadurch **nicht** bewiesen
([Workflow](../.github/workflows/windows-build.yml):53–178).

Konkrete Abdeckungslücken aus der vollständigen Testlektüre:

- [GraphApiAssertions](../tests/AetherManager.Tests/GraphApiAssertions.cs):12–106
  verwendet gefälschten Handler und `synthetic-token`. Kein echter Tokenbezug
  und kein Readback nach Sync/PATCH. Der
  [Browser-Smoke](../tests/AetherManager.Tests/browser-smoke.mjs):46–48,97–112
  prüft nicht eingerichtete Graph-Konfiguration und deaktivierte Cloudaktionen,
  nicht die erfolgreiche Tenantbedienung.
- [DashboardTests](../tests/SystemCockpit.Tests/DashboardTests.cs):99–139
  prüft den isolierten Frame und seine Freigaben/Lifecyclepfade. Ein realer
  Minimize-/Hide-Renderingtest des Hauptfensters fehlt; daraus folgt noch
  keine bewiesene Lastabschaltung im minimierten Produkt.
- [ProfessionalWorkflowTests](../tests/SystemCockpit.Tests/ProfessionalWorkflowTests.cs):30–53,145–197
  und [ModuleTests](../tests/SystemCockpit.Tests/ModuleTests.cs):14–65
  prüfen Parser, Queue und Befehlsbindung mit Fixtures. Echte
  WinGet-Installation/Update/Entfernung und Paketrollback sind nicht abgenommen.
  Lesendes `--winget-live` und Inno-Setupfixtures ersetzen diese Abnahme nicht.

RC-Exit verlangt zusätzlich: gesicherter Quellstand, konsistente Versionen,
vollständige Lizenzen, keine Debug-Testartefakte im Produkt, real verifizierte
Hauptabläufe, kein bekannter reproduzierbarer Kernabsturz/Datenverlust,
geprüftes Beenden, begrenzte Last, korrekte fehlende Daten, Quellenmetadaten,
ruhigen Frame, Offline-Wiki, dokumentierte Rechte/Opt-ins und aktuelle
Anleitungen. Fehlende Messergebnisse blockieren eine Erfolgsaussage.
Neue Tests werden nicht durch Abschwächen vorhandener Tests „grün“ gemacht.

## 21. Rollbackstrategie

1. Vor produktiven Änderungen den **gesamten aktuellen** Quell-/Konfigurations-
   stand einschließlich untracked Dateien autorisiert sichern. Ein Patch nur
   für getrackte Dateien genügt nicht. Hashmanifest und Rückleseprobe gehören
   dazu; Secrets und Benutzer-/Serverdaten nicht ins Repository übernehmen.
2. Branch plus Ausgangscommit dokumentieren; separate begrenzte Pakete.
   Kein Commit, Push, Tag oder Release ohne ausdrücklichen Auftrag.
3. Je Paket nur eigene Änderungen zurücknehmen. Kein Reset/Checkout über
   bestehende lokale Arbeiten. Bei Konflikten stoppen.
4. Settings-/History-/Pipe-/Serverdatenformate nur mit Schema- und
   Rückwärtskompatibilitätsplan ändern. Vor Migration separaten Datenbackup
   unter dem richtigen Benutzer-/Dienstkonto prüfen.
5. Alte `dist`-Ausgaben erhalten. Ein alter Installer allein stellt neue
   Datenformate nicht wieder her; Neuinstallation ist kein Datenrollback.
   Vorheriger Uninstall würde aktuelle Kontodaten löschen.
6. Administrative Maßnahmen gesondert bewerten: beendeter Prozess,
   Windows-Reparatur, Update oder endgültige Löschung lassen sich nicht
   durch Quellrollback rückgängig machen. Keine globale Rücknahme versprechen.

Für diesen Audit wurde ein SHA256-Inventar der 315 vorhandenen Git-
relevanten Dateien außerhalb des Repositories erfasst. Es dient dem
Änderungsnachweis, **nicht** als vollständiges wiederherstellbares Backup.

## 22. Entscheidungen von Olaf

Vor der nächsten Implementierung nötig; **keine Antwort wird hier vorweggenommen**:

1. Meilenstein 0 abnehmen und Meilenstein 1 separat freigeben; zuerst
   reproduzierbaren lokalen Snapshot oder ausdrücklich beauftragte Commits.
2. RC-Sprachumfang: zunächst Deutsch oder vollständiges Englisch.
   Das bestehende CI-Gate blockiert derzeit jedes `-rc.`-Profil ohne
   Englischfreigabe; keine stille Umgehung.
3. Privilegienziel: erhöhtes Power-User-Cockpit vorerst behalten oder
   normal laufende UI plus eng begrenzten Adminhelper beauftragen.
   Freie lokale Konsole bleibt bei einer Trennung ein eigener privilegierter
   Bereich, niemals eine freie Remote-Shell.
4. Testumgebung: autorisierte VM/Zielgeräte, Windows-Builds/Editionen,
   vorhandene Hardware, Monitore/DPI und zulässige Nebenwirkungen.
5. Lastbudgets und Beobachtungsdauer pro Profil; welche aktivierten
   Überwachungen im Tray weiterhin zeitnah arbeiten müssen.
6. Frame-Proof und semantische Farb-/Statuspriorität, ohne Anzeigenumbau;
   unabhängige Board-/Logoanimationen nicht automatisch entfernen.
7. Erste Hardwareklassen und erlaubte Quellen; zusätzliche Provider/Treiber
   nur mit eigenem Nutzen-/Lizenz-/Sicherheitsnachweis.
8. Datenschutzvorgaben für Seriennummern, Geräte-IDs und Exporte;
   Reader-/Operatorzugriff, Aufbewahrung, Archivierung und Löschung.
9. Bootstrap-/Tokenrotation und produktiver Managementumfang; erlaubter
   Netzbetrieb, JSON-Betriebsgrenzen und Sicherungsverfahren.
10. Graph/Autopatch im RC: freigegebener Testtenant mit Rechten/Lizenzen,
    experimentell gekennzeichnet oder zunächst nicht Bestandteil der Abnahme.
    Neue Rollouts sind ein separater Auftrag.
11. Rechteherkunft des AETHER-Logos und zulässige Herstellerassets;
    bis zum Nachweis neutrale Fallbacks.
12. Wiki als eigener Prozess oder eigenes Fenster; erster repräsentativer
    Artikelsatz und Pflege-/Quellenverantwortung.
13. Changelogarchivierung/-verdichtung erst in Meilenstein 6 genehmigen;
    vorhandene Pakete und Historie jetzt unverändert lassen.
14. Signierungsanforderung vor RC und Umgang mit nicht signierten
    Testpaketen, ohne Schutzrichtlinien zu verändern.

## Historischer Abschluss und Stopp vor Freigabe 0.5

Dieser Bericht ist die Entscheidungsvorlage für den weiteren Weg. Er
erteilt keine RC-Freigabe und behauptet keine vollständige reale
Performance-, Sicherheits- oder Hardwareabnahme.
Die verbleibende Volllektürelücke ist oben ausgewiesen; Meilenstein 0 wird
deshalb nicht als vollständig abgeschlossen ausgegeben.
**Die Bearbeitung stoppt hier ohne produktive Umsetzung. Produktive
Änderungen beginnen erst nach ausdrücklicher Freigabe von Olaf.**

## Nachtrag Freigabe 0.5 — 9. November 2026

Dieser Nachtrag korrigiert/ergänzt den vorstehenden historischen Auditstand;
er ersetzt keine fehlenden Laufzeittests. Keine produktive Entwicklung und
keine Freigabe von Meilenstein 1. Der Status der vollständigen Lektüre steht
im [Leseverzeichnis](RC-SOURCE-READING-MANIFEST.md), das abschließende Urteil
im [Abschlussbericht](MILESTONE-0-CLOSURE-REPORT.md).

### Neu verifizierter Ausgangsstand und Sicherung

Branch `rc/grosse-entfesselung`, HEAD
`53823883a9d1187395dc0fa3e3d8deebd3d3d8cb`. Vor 0.5 erneut erfasst:
48 geänderte getrackte und 14 untracked Dateien, kein Staging,
keine Löschung/Umbenennung, keine Stashes und keine ausschließlich lokalen
Commits gegenüber den vorhandenen lokalen Remotereferenzen.
Kein Fetch und keine Remoteänderung.

Die physische Workspaceinventur erfasst 13.183 Dateien ohne `.git`,
darunter 8.403 dist-Artefakte und 4.463 generierte Ausgaben. Alle 8.720
ausgewählten Restore-Dateien wurden extern kopiert und anhand Größe/SHA256
geprüft, insgesamt 4.823.007.346 Bytes. Für 317 Dateien außerhalb von dist
wurde zusätzlich die Rückkopie in ein getrenntes Restore-Ziel geprüft.
Gitbundle und ein daraus erzeugtes externes Bare-Repository enthalten HEAD.
Ein erster Probeaufruf scheiterte an der PowerShell-Argumentübergabe für
`--git-dir`; der korrigierte Aufruf und der vollständige erneute
Dateivergleich waren erfolgreich. Kein Fehler wird als Erfolg ausgegeben.

Ort: `D:\AetherBaselineSnapshots\rc05-20261109-212329`.
[Sicherungsplan/Restore-Matrix](RC-BASELINE-SNAPSHOT-PLAN.md) und
[Ausgangsmanifest](RC-BASELINE-HASHES.csv) unterscheiden Kopie,
Integritätsnachweis und nicht gesicherte Laufzeit-/Benutzerdaten.
Generierte Ausgaben sind nicht Teil des Quellrestores. Die Sicherung auf
demselben Laufwerk ist kein Schutz vor Datenträgerverlust.

### Präzisierungen nach der Restlektüre

Die Restlektüre ist jetzt abgeschlossen: vier zuvor explizit offene
ControlCenter-/OperationsHub-Dateien und alle übrigen 65 UI-/Ressourcen-/
Schonertexte. Das aktuelle Verzeichnis zählt 279 vollständige Textlektüren,
drei strukturierte Sonderfälle und 38 Binärdateien, insgesamt 320 Dateien,
ohne offene Lesebelege. Frühere Quellenbelege sind hashgeprüft unverändert.
Die vier RESX-Ressourcen wurden vollständig mit allen Schlüsseln und Werten
gelesen; die paarweise Schlüssel-/Platzhalterprüfung ist kein Nachweis
vollständiger englischer Oberflächenabdeckung.

Die externe Drive-Datenbank ist als nicht generierte Drittanbieterreferenz
getrennt ausgewiesen: 792 Datensätze / 6.940 Zeilen vollständig lexikalisch
und strukturell geprüft, keine fachliche Einzelabnahme aller Modellmatches.
Der ursprüngliche Auditabschluss oben bleibt als historischer Beleg erhalten.
Das aktuelle Urteil lautet **MEILENSTEIN 0 ABSCHLUSSBEREIT**, nicht RC-freigegeben.

1. **Zeitlimits:** Die 30 Minuten gelten für den gemeinsamen
   ManagedActionRunner. OperationsHub verwendet bei Wartung
   `Timeout.InfiniteTimeSpan`, sonst lokal 90 Minuten und für entfernte Ziele
   drei Minuten. Seine Wartung wartet bei „Anhalten“ den aktuellen Schritt
   ab. Die freie Konsole besitzt nochmals eigene Grenzen. Keine allgemeine
   Kill-/Rollbackgarantie aus einem einzelnen Runner ableiten.
2. **Paketfehler:** Serielle Paketaktionen können nach einem Einzelfehler
   weitere ausgewählte Pakete verarbeiten und melden am Ende einen Fehler.
   Wartungspläne stoppen dagegen Folgeschritte beim ersten Fehler.
   Beide Wege kennzeichnen Teiländerungen, bieten aber keine Transaktion.
3. **Updatebestätigung:** Der lokale feste Updatepfad verwirft unvollständige/
   abgeschnittene Ergebnisse, prüft GUID/Revision/EULA-Hash und die einstündige
   Frische. Auswahlwechsel löscht Zustimmung; der Plan wird vor Installation
   verbraucht. Dies bestätigt die Schutzbeschreibung, nicht den echten Erfolg.
4. **Microsoft-Zusatztools:** In der geprüften Systemkontrollauswahl gibt es
   Verfügbarkeitsprüfung und Öffnen der offiziellen Bezugsseite. Das ist
   keine eingebettete Sysinternals-Auswertung. Frühere pauschale Aussagen
   über „Start/Integration“ sind je konkretem UI-Pfad zu lesen.
5. **Konsole/Management:** Lokale freie erhöhte Befehle und feste
   Remoteaktionen bleiben getrennt. Enrollment verwendet ein PasswordBox-
   Feld, Reporting ein eigenes Opt-in. Die zentrale Verwaltung liegt weiter
   im Einstellungsmodul; ein eigenständiger Hauptnavigationseintrag ist
   damit nicht implementiert.
6. **Board und Schoner:** Schematische Datenflüsse/Lüfter sind als solche
   bezeichnet; bei fehlendem Feed kann der Schoner einmalig lokal lesen.
   Daraus folgt weder eine erfundene Livequelle noch eine neue Hardwareabnahme.
7. **Ressourcen und Bewegung:** Die vorhandenen Bewegungs-/Sichtbarkeitsgates
   und Bestätigungen sind statisch nachvollziehbar. Ihre tatsächliche
   Lifecyclewirkung, Ressourcenlast und Bedienbarkeit bleiben ungeprüft.

Belege:
[OperationsHub](../src/SystemCockpit/Controls/OperationsHub.xaml.cs):55–64,174–248,273–299;
[Paketqueue](../src/SystemCockpit/ControlCenterWindow.Packages.cs):86–145;
[Updatepfad](../src/SystemCockpit/ControlCenterWindow.ManagedActions.cs):25–153;
[Zusatztools](../src/SystemCockpit/ControlCenterWindow.ManagedActions.cs):187–219;
[Systemkontrolle](../src/SystemCockpit/ControlCenterWindow.xaml):356–477.
Weitere Belege:
[Board](../src/SystemCockpit/Controls/HardwareBoardView.Drawing.cs):214,344–346;
[Schonersitzung](../src/AetherScreensaver/SaverSession.cs):34–49,61–169;
[Branding](../src/SystemCockpit/Controls/AnimatedBrand.xaml.cs):25–60;
[Schonerfenster](../src/AetherScreensaver/SaverWindow.cs):95–105,140–170.

### Vorgemerkte Entscheidungen, noch nicht implementiert

- **RC1 Deutsch:** vollständig freigegebenes Deutsch ist die Zielrichtung.
  Lokalisierungsarchitektur erhalten; Meilenstein 1 legt einen sauberen
  Plan für sprachbezogene RC-Kriterien vor. Das bestehende Englisch-Gate
  wird jetzt weder entfernt noch umgangen.
- **Rechte:** erhöhtes Power-User-Cockpit zunächst beibehalten; eine
  UI-/Helpertrennung ist keine Voraussetzung für RC1. Unnötige Privilegien
  und Sicherheitsfragen bleiben zu prüfen.
- **Frame:** ruhiger scharfer Neonrahmen mit Glow, langsamem Atmen,
  Statussignalen und Lifecycleabschaltung grundsätzlich akzeptiert.
  Umsetzung erst nach Meilenstein-2-Freigabe; übrige Animationen erhalten.
- **Hardware:** eigene Quellen und Metadatenmodell mit Direct, Derived,
  Database, Unconfirmed. Keine kopierten fremden Mess-/Spezifikationsdaten,
  keine erfundenen Felder. Noch keine neue Quelle angebunden.
- **Logos:** bis zum konkreten Freigabenachweis eigene neutrale Symbole
  und ausgeschriebene Herstellernamen. Keine neuen fremden Assets.
- **Wiki:** zuerst separates Fenster innerhalb der bestehenden Anwendung,
  kein eigener Prozess. Lokal/offline, Suche, Quellen und Deeplinks bleiben Ziel.
- **Graph/Autopatch:** vorhandene Funktion erhalten; für RC1 ohne
  Tenantabnahme experimentell bzw. außerhalb der verbindlichen RC1-Abnahme.
  Kein neuer Rollout und keine jetzige Funktionsänderung.
- **Changelog:** vollständige Historie vor späterer Verdichtung sichern;
  in 0.5 keine Bereinigung.

Damit sind die früheren offenen Entscheidungsfragen zu Sprache, erhöhtem
Cockpit, Wiki-Prozess und experimentellem Graphumfang für die weitere
Planung präzisiert. RC-12 und RC-13 bleiben historische Gatebefunde:
Englisch/Tenantabnahme sollen RC1 nach dieser Richtung nicht pauschal
blockieren; das technisch saubere Gate-/Kennzeichnungskonzept ist noch
vorzulegen. Die übrigen realen RC-Abnahmen bleiben offen.
