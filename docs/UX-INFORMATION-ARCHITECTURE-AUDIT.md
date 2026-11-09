# UX-Audit: Informationsarchitektur

Bestandsaufnahme der sichtbaren Struktur, Funktionen und Benennungen des
AETHER-Desktops mit anschließendem Architekturvorschlag. Dies ist der
Baseline-Stand vor Paket 1; ausgewählte sichtbare Bezeichnungen wurden seitdem
in Paket 1 präzisiert, ohne interne Routen oder Funktionen zu ändern.
Grundlage sind der
Quellstand auf dem Arbeitsbranch `test/rc-localization` (nach der
veröffentlichten Beta 38) sowie [README](../README.md),
[Funktionsübersicht](FEATURES.md), [Benutzerhandbuch](USER-GUIDE.md),
[Verwaltung und Wartung](SYSTEM-CONTROL.md), [Hardware und Sensoren](HARDWARE-SENSORS.md),
[Netzwerk](NETWORK.md) und [Zentrale Verwaltung](CENTRAL-MANAGEMENT.md).

Dieses Dokument verändert keinen Programmcode und ist keine Freigabe für
Umbauten. Aussagen ohne Quellcode- oder Dokumentationsbeleg sind ausdrücklich
als **Annahme** gekennzeichnet. Quellenangaben nennen Datei und ungefähre
Zeile des geprüften Arbeitsstands; Zeilennummern können sich verschieben.

## A. Sichtbare Hauptbereiche

AETHER besteht aus zwei Hauptfenstern und mehreren persistenten
Desktop-Komponenten.

**Hauptfenster (`MainWindow`)** mit linker Gruppen-Navigation
(`src/SystemCockpit/MainWindow.Navigation.cs:14-27`, deutsche Texte in
`src/SystemCockpit/Resources/DesktopUiText.resx:325-351`):

| Gruppe | Einträge |
|---|---|
| Übersicht | Cockpit · Prozesse / Leistung |
| Hardware | Mainframe / Board (Modern: System-Schema) · Sensoren · Laufwerke · SMART ↗ |
| Diagnose | Windows-Diagnose · Schutzstatus · Ereignisse · Netzwerk / Tests ↗ · Messauswertung · Logs / Gaming ↗ |
| Wartung | Cleaner · Assistenten / Pläne · Windows-Werkzeuge ↗ · Pakete / Updates ↗ |
| Verwaltung | Systemverwaltung · Autostart ↗ · Aufträge ↗ · Ordnerspuren ↗ · Schlüssel ↗ |
| Hilfe | Anleitung / Suche · Parameter / Server ↗ |

Das sind 23 Navigationseinträge in 6 Gruppen. 13 Einträge öffnen eingebettete
Hauptseiten, 10 mit **↗** markierte Einträge wechseln in das separate
Systemkontrollfenster.

**Systemkontrolle (`ControlCenterWindow`)** mit 10 Modul-Tabs in dieser
Reihenfolge (`src/SystemCockpit/ControlCenterWindow.xaml:88-401`):
SMART · Netzwerk · Pakete · Aufträge · Wartung · Autostart · Spuren ·
Logs / Gaming · Einstellungen · Schlüssel. Oberhalb der Tabs liegen
Zurück/Vorwärts, Rücksprung zum Hauptfenster, ein Terminal-Schalter und die
aufklappbare freie Konsole (`ControlCenterWindow.xaml:1-86`).

**Weitere eigenständige Fenster und Flächen:**

- Funktionssuche „Funktion finden“ (`src/SystemCockpit/TaskCenterWindow.cs`)
- Darstellung / Desktop (`src/SystemCockpit/DesktopSettingsWindow.cs`)
- xBar, Desktop-Gadgets, Temperatur-Overlay und Tray
  (`src/SystemCockpit/DesktopController.cs`)
- Bildschirmschoner (`src/AetherScreensaver/`)
- Erststart-Assistent mit Funktionsübersicht, EULA und Inventar
  ([Benutzerhandbuch](USER-GUIDE.md#erststart))

## B. Unterbereiche und Funktionen

Hauptseiten im Hauptfenster (`src/SystemCockpit/MainWindow.xaml:190-640`):

| Hauptseite | Unterbereiche |
|---|---|
| Cockpit | CPU-/RAM-/Storage-Live-Panel, Detailgraphen, CPU-Kerne, physische Laufwerke, „A-01 // SYSTEM BUS“/Visual Scanner, Metric Cards |
| Prozesse / Leistung | Prozesstabelle mit Suche/Sortierung, Systeminformationen |
| Mainframe / Board | Neon-Platine bzw. System-Schema, getrennte Gerätedetails, Zoom, Legende (`Controls/HardwareBoardView.xaml`) |
| Sensoren | Sensorstatus, Live-Diagramme, Detailmatrix / Grenzwerte (`Controls/SensorChartsView.xaml`) |
| Laufwerke | Belegung, Windows-Storage-Status, Datenträgerinformationen |
| Messauswertung | Messsession, Kanäle/Charts, Export, Logvergleich |
| Windows-Diagnose | Diagnosekarten, Windows-Assistenten, Inventar (`Controls/InventoryView.xaml`) |
| Schutzstatus | Defender-/Schutz- und Sicherheitsstatus |
| Ereignisse | Kanalwahl, Zeitraum, Stufenfilter, Volltext, XML-Details (`Controls/EventBrowser.xaml`) |
| Cleaner | Kategorien, Vorschau, Dateiauswahl, Löschbestätigung |
| Assistenten / Pläne | Operations-Hub: 17 Wartungskategorien, 4 Planvorlagen, Windows-Assistenten, Hintergrunddiagnose (`Controls/OperationsHub.xaml`) |
| Systemverwaltung | Admin-Workbench: Bereichsauswahl, Statustabellen, Vorschau, Anwenden/Rückgängig (`Controls/AdminWorkbench.xaml`) |
| Anleitung / Suche | Lokales Hilfe-Center mit Suche und Themenliste |

Systemkontroll-Module (Auswahl des Umfangs, Details in
[Verwaltung und Wartung](SYSTEM-CONTROL.md) und [Netzwerk](NETWORK.md)):

| Modul | Umfang |
|---|---|
| SMART | ATA-/NVMe-SMART-Diagnose und Rohdaten |
| Netzwerk | Adapter/IP/TCP, Ping, WLAN/Bluetooth, Captive-Portal, Durchsatztest, LAN-Scan, Npcap-Mitschnitt, WinRM-Diagnose |
| Pakete | WinGet-Listen, Suche, Installation/Update/Entfernung, Wunschliste |
| Aufträge | Auftragsstatus der Sitzung (bis 200 Einträge im RAM) |
| Wartung | 25 feste Windows-Werkzeugaktionen, Befehlskatalog, freie Administratorkonsole, Expertenmodus-Schalter |
| Autostart | Suchbare Run-Key-Tabelle, Sicherung und Deaktivierung |
| Spuren | Lesende Shellbag-/Ordnerspuren-Auswertung |
| Logs / Gaming | PresentMon-Capture, Frame-CSV-Auswertung |
| Einstellungen | Skins, Sprache, Desktop-Gadgets, Warnschwellen, Werkzeugpfade, Hintergrunddiagnose, Zentralverwaltung/Management Reports, Expertenmodus |
| Schlüssel | BitLocker-, Windows-Produktschlüssel- und WLAN-Schlüsselanzeige |

## C. Aktuelle Navigationshierarchie

```text
AETHER Desktop
├─ Hauptfenster (verdecktes TabControl „Pages“, Routen über Button-Tags)
│  ├─ Übersicht ─ Cockpit (Startseite) · Prozesse / Leistung
│  ├─ Hardware ─ Mainframe/Board · Sensoren · Laufwerke · [↗ SMART]
│  ├─ Diagnose ─ Windows-Diagnose · Schutzstatus · Ereignisse ·
│  │             [↗ Netzwerk/Tests] · Messauswertung · [↗ Logs/Gaming]
│  ├─ Wartung ─ Cleaner · Assistenten/Pläne · [↗ Windows-Werkzeuge] · [↗ Pakete/Updates]
│  ├─ Verwaltung ─ Systemverwaltung · [↗ Autostart] · [↗ Aufträge] ·
│  │               [↗ Ordnerspuren] · [↗ Schlüssel]
│  └─ Hilfe ─ Anleitung/Suche · [↗ Parameter/Server]
├─ Systemkontrolle (TabControl „Modules“, 10 Tabs, eigene Zurück/Vor-Leiste,
│  Terminal-Schalter, freie Konsole)
├─ Funktion finden (Strg+K, eigenes Fenster, 25 geführte Abläufe plus
│  Befehlskatalogeinträge; Services/TaskCatalog.cs:18-78)
├─ Darstellung / Desktop (eigenes Fenster: Tag/Nacht, Design, Deckkraft)
├─ Tray (Öffnen · Einstellungen · Netzwerkmonitor · Gadgets · Beenden)
├─ xBar, Gadgets, Temperatur-Overlay (persistente Desktopflächen)
└─ Bildschirmschoner (eigenes Programm mit Live-Feed)
```

Technik: Seitenwechsel über `Navigate`/`SelectPage`, externe Routen über
`OpenControlCenterModule`; Besuchsverlauf `NavigationTrail` mit Zurück/Vor,
Maus-Daumentasten und `Alt+Pfeil` (bis 100 Schritte im RAM). Eine
Breadcrumb-ähnliche Anzeige `Gruppe ▸ Seitentitel` existiert im Kopfbereich
(`MainWindow.xaml:160-181`, `MainWindow.xaml.cs:923-969`,
`Services/NavigationTrail.cs`).

## D. Mehrfachzugänge zu derselben Funktion

| Funktion | Zugänge |
|---|---|
| Funktionssuche | Schaltfläche Hauptfenster · Strg+K · Schaltfläche Systemkontrolle |
| Cleaner | Hauptnavigation (Wartung) · Verweis aus Operations (`MainWindow.xaml.cs:91`) |
| Windows-Assistenten | Operations-Hub · Tray-Hinweis der Hintergrunddiagnose · Navigationsgruppe Diagnose |
| Darstellung / Desktop | Linke Leiste · Tray-Menü ([Benutzerhandbuch](USER-GUIDE.md#cockpit-und-mainboard)) |
| Desktop-Gadget-Parameter | Systemkontrolle → Einstellungen · Tray-Menü |
| Systemkontroll-Module | je 2 Wege: ↗-Eintrag der Hauptnavigation · Tab in der Systemkontrolle |
| Hauptfenster öffnen | Tray-Doppelklick · Tray-Menüpunkt |
| Zurück/Vor | Schaltflächen · Maus-Daumentasten · Alt+Pfeiltasten |

## E. Beim ersten Start unmittelbar sichtbar

1. Erststart-Assistent: Funktionsübersicht, EULA, Rechner-Inventar
   ([Benutzerhandbuch](USER-GUIDE.md#erststart)). Erststart immer in Retro-Neon.
2. Danach öffnet das Cockpit als Startroute (`MainWindow.xaml.cs:54-57`),
   nur die Gruppe „Übersicht“ ist aufgeklappt
   (`MainWindow.Navigation.cs:32`). Gleichzeitig sichtbar sind:
   CPU-Gesamtanzeige, bis zu vier Detailgraphen, CPU-Temperatur und
   Package Power, CPU-Kern-Kacheln, Laufwerkskacheln, die
   „A-01 // SYSTEM BUS“-Leiste und weitere Metric Cards
   (`MainWindow.xaml:190-433`).
3. Zusätzlich dauerhaft: Tray-Icon; Gadgets/xBar nur nach Aktivierung.

**Wichtig für die Bewertung:** Der Expertenmodus ist als gespeicherte
Vorgabe **eingeschaltet** (`Services/CockpitSettings.cs:20-22`). Ein neuer
Benutzer startet also mit vollem Befehlskatalog und freigeschalteter freier
Konsole in der Systemkontrolle.

## F. Funktionen mit erhöhten Rechten

Rechteprüfung über `SystemActions.IsAdmin()`/`RequireAdmin()`
(`Services/SystemActions.cs:49-58`); eine automatische UAC-Erhöhung einzelner
Aktionen ist nicht erkennbar, die App läuft laut
[Benutzerhandbuch](USER-GUIDE.md#erststart) nach UAC-Bestätigung erhöht.

- Admin-Workbench komplett: Prozesse, Dienste, Aufgaben, Features,
  Energiepläne, Adapter, DNS, Proxy, Firewall, Benutzer/Gruppen, ACL
  (`Services/AdminManagement.cs:19-132`)
- SFC, DISM, CHKDSK, Firmware-Neustart, ARP-Cache
  (`Services/SystemActions.cs:18-25`)
- HKLM-Autostart (`Services/SystemActions.cs:137-165`)
- Windows-Update-Suche/-Installation
  (`ControlCenterWindow.ManagedActions.cs:145-188`)
- WinGet-Aktionen (meist), Delivery-Optimization-Bereinigung
- Freie Administratorkonsole (`Services/AdminShell.cs:46-52`)
- Schlüsselanzeigen (BitLocker, Produktschlüssel, WLAN) — lesend, aber
  geheimniskritisch (`Services/SystemActions.cs:60-91`)

## G. Funktionen mit Systemänderung

Vollständigere Tabelle der ändernden Aktionen mit Bestätigungs- und
Rücknahmelage:

| Funktion | Quelle | Bestätigung | Rücknahme |
|---|---|---|---|
| Prozess beenden / Priorität | `Services/AdminManagement.cs:19-27` | Vorschau + Ja/Nein | Nein |
| Dienste Start/Stopp/Starttyp | `AdminManagement.cs:28-42` | Ja | Starttyp teilweise |
| Aufgaben ausführen/aktivieren | `AdminManagement.cs:43-47` | Ja | Teilweise |
| Windows-Features | `AdminManagement.cs:48-52` | Ja | Teilweise |
| WinGet install/update/remove | `AdminManagement.cs:53-70` | Ja, ID-Liste | Nicht zuverlässig |
| Windows-Update installieren | `ControlCenterWindow.ManagedActions.cs:145-188` | Expertenmodus + EULA + Detailbestätigung | Nein |
| SFC/DISM-Reparatur | `Services/SystemActions.cs:18-21` | Aktionsdialog | Nicht sicher |
| CHKDSK `/scan` | `SystemActions.cs:22` | Aktionsdialog; als Änderung eingestuft | — |
| Firmware-Neustart (30 s) | `SystemActions.cs:23-24` | Ja | Abbruch möglich |
| Adapter/DNS/Proxy/Firewall | `AdminManagement.cs:92-132` | Ja | DNS/Proxy/Firewall mit Restore |
| Energieplan | `AdminManagement.cs:82-91` | Ja | Vorheriger Plan gesichert |
| Registry-Einstellungen (8 feste) | `Services/RegistryTweaks.cs` | Ja | Originalwert gesichert |
| Autostart deaktivieren | `SystemActions.cs:105-165` | Ja | Backup `startup-backups.json` |
| Cleaner-Löschung | `Services/TempCleaner.cs:77-111` | Mehrstufig (Vorschau, Ankreuzen, Gesamtbestätigung) | **Nein, endgültig ohne Papierkorb** |
| Wartungspläne | `Controls/OperationsHub.*` | Planvorschau + Lauf + Einzelfreigabe + Pflicht-Wiederherstellungspunkt | Punkt ersetzt kein Backup |
| Freie Konsole | `ControlCenterWindow.xaml.cs:749` | **Keine; Enter führt direkt aus** (nur im Expertenmodus) | Nein |
| Remote-Aufträge (Managed) | `src/AetherOperations.Shared/ManagedActionCatalog.cs` | Serverpolicy + lokale Freigabe + ConfirmChanges | Nein |

Bestätigungen folgen mindestens fünf parallelen Mustern: MessageBox Ja/Nein,
zentrale `RunAsync`-Bestätigung mit `admin`/`sensitive`/Detailstufe, eigener
Neustartdialog, Paket-Spezialbestätigungen und die mehrstufige
Workbench-Vorschau (`ControlCenterWindow.xaml.cs:296-305`,
`Controls/AdminWorkbench.xaml.cs:92-160`, `Services/DesktopRestart.cs:10-53`).

## H. Rein informative Funktionen

Sensorik und Live-Werte (`Services/HardwareSensors.cs`, `BoardLiveFeed.cs`),
Sensor-Charts/Historie (`SensorChartHistory.cs`), SMART/Storage
(`SmartService.cs`, `StorageAnalysis.cs`), Inventar (`SystemInventory.cs`),
Ereignisse (`EventMonitor.cs`, `Controls/EventBrowser.xaml.cs`),
Netzwerkstatus/WLAN/Nachbarn (`NetworkTools.cs`, `NetworkWatch.cs`),
Paketmitschnitt-Auswertung (`PacketAnalysis.cs`), Display/Audio-Topologie,
Leistungsindex (`PerformanceIndex.cs`), Statistik (`SessionStatistics.cs`),
Shellbags (`ShellbagAnalyzer.cs`), Diagnose-Assistenten lesend
(`DiagnosticAssistants.cs`). Sonderfall: Schlüsselanzeigen sind lesend,
aber vertraulich (siehe F).

## I. Experten- und Spezialfunktionen

- Freie PowerShell-Administratorkonsole mit Befehlskatalog; Enter führt
  direkt aus; Einsteigermodus sperrt den freien Start und filtert den
  Katalog auf Risiko 0 (`ControlCenterWindow.xaml.cs:197-205,749`)
- Expertenmodus-Pflicht für Windows-Update-Aktionen
  (`ControlCenterWindow.ManagedActions.cs:110-130`)
- Admin-Workbench mit Skriptvorschau und Rücknahme
- Aktionsjournal (`Services/ActionJournal.cs`), Terminal-/Debugausgabe
- Geheimnisanzeigen (BitLocker/Produktschlüssel/WLAN)
- PresentMon/Frame-Analyse, Npcap-Mitschnitt, WinRM-Diagnose, LAN-Scan
- Werkzeugpfade (smartctl, PresentMon, OUI-Datei) in den Einstellungen

## J. Persistente Desktop-Funktionen

| Komponente | Stand |
|---|---|
| Gadgets | CPU, RAM, GPU, Temperaturen, Netzwerk, Laufwerke/Volumes; Position und Deckkraft je Gadget gespeichert; Platzierung auf dem primären Monitor (`DesktopController.cs:127-235`, `Services/CockpitSettings.cs:40-51`) |
| xBar | Gleiche sechs Modulgruppen; Monitorwahl, PIN/FLOAT, Auto-Hide, Deckkraft, Shortcuts (`Services/CockpitSettings.cs:25-45`, `Services/SidebarDisplays.cs`) |
| Tray | Öffnen, Einstellungen, Netzwerkmonitor, Gadgets ein/aus/schließen, Beenden; Balloon-Hinweise für Diagnose/Netzwerk (`DesktopController.cs:29-78,184-199`) |
| Temperatur-Overlay | Separat zu öffnen, meldet Warnübergang ([Benutzerhandbuch](USER-GUIDE.md#xbar-gadgets-und-overlay)) |
| Bildschirmschoner | Ein Fenster je Monitor, Neon-Platine, Live-Feed über lokale Named Pipe, Fallback „Keine Live-Messwerte“; in Windows Modern gesperrt (`src/AetherScreensaver/SaverSession.cs`, `SaverWindow.cs`) |
| Multi-Monitor | xBar-Monitorwahl und Schoner je Monitor vorhanden; Gadgets starten auf dem primären Arbeitsbereich (`DesktopController.cs:151-157`) |

Animationen folgen der Windows-Einstellung `ClientAreaAnimation`
(`BootWindow.xaml.cs:31-37`, `Controls/FrameGlow.cs:98-121`,
`AetherScreensaver/SaverWindow.cs:136-157`). **Annahme/Befund:** Ein eigener
AETHER-Schalter „Animation ein/aus“ wird in [FEATURES](FEATURES.md) genannt;
in der Code-Stichprobe wurde nur die Windows-Kopplung belegt. Vor Umbauten
am Animationsverhalten ist der genaue Schalterort zu verifizieren.

## K. Server-/Client- und Zentralverwaltungsfunktionen

In der Desktop-App liegt die zentrale Verwaltung derzeit unter
**Systemkontrolle → Einstellungen → „Zentrale Verwaltung / Diagnoseberichte“**
(`ControlCenterWindow.xaml:370-390`): Betriebsart „Standalone“/„Managed
Client“, Server-URL, Enrollment-Code, „Enroll“, Reporting-Einwilligung und
Statusanzeige (Standalone · Managed/lokal · Managed/registriert ·
Managed/ausstehend, `ControlCenterWindow.xaml.cs:256-290`).

Remote-Ausführung ist mehrfach geschützt: fester Aktionskatalog ohne freie
Fernkonsole (`AetherOperations.Shared/ManagementProtocol.cs:12-25`),
Systemänderungen nur bei Serverpolicy `AllowChanges` **und** lokaler
Clientfreigabe **und** `ConfirmChanges`
(`AetherManager.Server/Services/ManagementService.cs:71-79`); die
DNS-Autoreparatur verlangt zusätzlich beidseitiges Opt-in. Der optionale
Windows-Dienst „AETHER Managed Client“ (`src/AetherClient.Service/`) arbeitet
unabhängig vom geöffneten Cockpit. Serverfunktionen (Geräte, Berichte,
Aufträge, Audit, Konten/Rollen, Flotte) siehe
[Zentrale Verwaltung](CENTRAL-MANAGEMENT.md).

## L. Uneinheitliche oder technisch geprägte Bezeichnungen

| Befund | Beispiele | Quelle |
|---|---|---|
| Deutsch/Englisch gemischt in einer Navigationsebene | „Cleaner“ neben „Assistenten / Pläne“; „Cockpit“ neben „Prozesse / Leistung“ | `DesktopUiText.resx:325-349` |
| Interne Technikbegriffe sichtbar | „Superadmin“ (Route), „Operations“ (Route), „Taskmanager“ (Route für „Prozesse / Leistung“) | `MainWindow.Navigation.cs:16-26` |
| Dekorative Pseudo-Technik als Panel-Titel | „CPU // MEMORY // STORAGE // LIVE“, „A-01 // SYSTEM BUS“, „Visual Scanner“ | `MainWindow.xaml:190-433` |
| Gleiche Sache, zwei Namen | „Mainframe / Board“ vs. „System-Schema“ (gewollt je Design); Navigationseintrag „Windows-Werkzeuge ↗“ öffnet Tab „Wartung“; „Parameter / Server ↗“ öffnet Tab „Einstellungen“ | `DesktopUiText.resx:332-349`, `ControlCenterWindow.xaml:193,349` |
| Doppeldeutige Gruppennamen | Hauptgruppe „Wartung“ und Systemkontroll-Tab „Wartung“ bezeichnen verschiedene Dinge | `DesktopUiText.resx:328,341` |
| Englische Fachbegriffe ohne Erklärung | „Managed Client“, „Enroll“, „Enrollment Code“; die Abschnittsüberschrift ist bereits lokalisiert als „Zentrale Verwaltung / Diagnoseberichte“ | `ControlCenterWindow.xaml:374-388`, `DesktopUiText.resx:1215` |
| Unklare Kurznamen | „Spuren“/„Ordnerspuren“ (Shellbags), „Schlüssel“ (BitLocker/Produkt/WLAN), „Logs / Gaming“ (PresentMon) | `DesktopUiText.resx:338-347` |

Positiv: Die ↗-Kennzeichnung und die Tooltips „Öffnet … ; startet keine
Aktion.“ existieren bereits einheitlich (`DesktopUiText.resx:350-351`).

## M. Bereiche mit besonders hoher Informationsdichte

1. **Cockpit (Startseite):** viele gleichzeitige Panels, Graphen, Kacheln
   und Metric Cards ohne Priorisierung nach Relevanz (`MainWindow.xaml:190-433`).
2. **Systemkontrolle gesamt:** 10 gleichrangige Tabs plus globale Leiste,
   Terminal und Konsole; je Modul 3–10 unmittelbare Aktionsschaltflächen.
3. **Admin-Workbench:** Bereichs-, Operations-, Filter-, Tabellen-,
   Parameter- und Aktionszone in einer Ansicht (`Controls/AdminWorkbench.xaml:22-50`).
4. **Operations-Hub:** 17 Kategorien, 4 Pläne, 4 Assistenten,
   Hintergrunddiagnose und Hinweise in einem Bereich (`Controls/OperationsHub.xaml:53-126`).
5. **Netzwerkmodul:** Status, Tests, Scan, Mitschnitt und WinRM in einem Tab.
6. **Sensor-Matrix:** fachlich gewollt dicht; Filter und Legende vorhanden.

## N. Wahrscheinliche Orientierungsprobleme neuer Benutzer

| Frage | Befund |
|---|---|
| Wofür ist der Bereich da? | „Spuren“, „Schlüssel“, „Logs / Gaming“, „Messauswertung“, „Management Reports“ erklären ihren Zweck nicht im sichtbaren Titel; Erklärtexte existieren nur in einzelnen Bereichen (Diagnose, Hilfe, Operations). |
| Was öffne ich als Nächstes? | Das Cockpit priorisiert nicht zwischen „alles in Ordnung“ und „Handlungsbedarf“; es gibt keine Startseiten-Auffälligkeitenliste. Die Funktionssuche beantwortet Absichten, ist aber nur ein Eintrag unter vielen. |
| Informiert die Funktion nur oder ändert sie etwas? | Tooltips der Navigation stellen das klar; **innerhalb** der Module stehen Anzeige und Änderung jedoch gleichgewichtet nebeneinander (Workbench, Netzwerkverwaltung, Windows-Update-Bereich, Cleaner; siehe G). |
| Brauche ich Administratorrechte? | Keine durchgängige visuelle Admin-Kennzeichnung an Schaltflächen; die Prüfung erfolgt beim Ausführen. |
| Kann ich das rückgängig machen? | Rücknahme ist je Aktion sehr unterschiedlich (G); eine einheitliche Kennzeichnung „reversibel/irreversibel“ vor der Aktion fehlt außerhalb der Workbench-Vorschau. |
| Einsteiger oder Experte? | Der Expertenmodus ist Standard; ein neuer Benutzer bemerkt die Existenz des Einsteigermodus erst im Wartungs-Tab der Systemkontrolle. |

## Funktionsinventar mit Einstufung

Zielgruppen: Einsteiger · Standardbenutzer · Fortgeschrittene ·
Administratoren · Spezialisten. Häufigkeit: täglich · regelmäßig ·
gelegentlich · selten. Risiko: nur Anzeige · lokale Einstellung ·
Systemänderung · administrative Änderung · potenziell kritische Änderung.
Empfohlene Ebene: Startseite · Hauptnavigation · Unterbereich ·
Expertenbereich · Kontextaktion · Einstellungen. Die Spalte „Empfohlene
Ebene“ ist ein Vorschlag dieses Audits, keine beschlossene Änderung.

| Funktion | Aktueller Ort | Aufgabe | Zielgruppe | Häufigkeit | Risiko | Empfohlene Ebene |
|---|---|---|---|---|---|---|
| Cockpit-Live-Werte | Übersicht → Cockpit | Aktuelle Auslastung sehen | Einsteiger | täglich | nur Anzeige | Startseite |
| Warnschwellen-Befunde | Cockpit/Overlay/Tray | Auffälligkeiten erkennen | Einsteiger | täglich | nur Anzeige | Startseite |
| Funktionssuche | Strg+K / Schaltflächen | Funktion nach Absicht finden | Einsteiger | regelmäßig | nur Anzeige | Startseite |
| Prozesse beobachten | Übersicht → Prozesse / Leistung | Lastverursacher finden | Standardbenutzer | regelmäßig | nur Anzeige | Hauptnavigation |
| Prozess beenden/priorisieren | Verwaltung → Systemverwaltung | Hängendes Programm beenden | Fortgeschrittene | gelegentlich | Systemänderung | Unterbereich |
| Mainframe / Board | Hardware | Hardware verstehen | Standardbenutzer | gelegentlich | nur Anzeige | Hauptnavigation |
| Sensor-Matrix | Hardware → Sensoren | Alle Sensorwerte sehen | Spezialisten | regelmäßig | nur Anzeige | Hauptnavigation |
| Laufwerksübersicht | Hardware → Laufwerke | Belegung/Status prüfen | Standardbenutzer | regelmäßig | nur Anzeige | Hauptnavigation |
| SMART-Diagnose | Systemkontrolle → SMART | Laufwerksgesundheit prüfen | Fortgeschrittene | gelegentlich | nur Anzeige | Unterbereich |
| Messaufzeichnung/-vergleich | Diagnose → Messauswertung | Verläufe auswerten | Spezialisten | gelegentlich | nur Anzeige | Unterbereich |
| PresentMon/Framezeiten | Systemkontrolle → Logs / Gaming | Spielleistung analysieren | Spezialisten | gelegentlich | nur Anzeige | Unterbereich |
| Ereignisanzeige | Diagnose → Ereignisse | Windows-Fehler suchen | Fortgeschrittene | gelegentlich | nur Anzeige | Hauptnavigation |
| Windows-Assistenten | Wartung → Assistenten / Pläne | Status geführt prüfen | Standardbenutzer | gelegentlich | nur Anzeige | Hauptnavigation |
| Schutzstatus | Diagnose → Schutzstatus | Defender-Status prüfen | Standardbenutzer | regelmäßig | nur Anzeige | Hauptnavigation |
| Hintergrunddiagnose | Einstellungen (Desktop) | Automatisch prüfen lassen | Standardbenutzer | selten (einrichten) | lokale Einstellung | Einstellungen |
| Netzwerkstatus/Tests | Systemkontrolle → Netzwerk | Verbindung prüfen | Standardbenutzer | gelegentlich | nur Anzeige | Hauptnavigation |
| LAN-Scan | Systemkontrolle → Netzwerk | Geräte im Netz finden | Fortgeschrittene | selten | nur Anzeige | Unterbereich |
| Npcap-Mitschnitt | Systemkontrolle → Netzwerk | Pakete mitschneiden | Spezialisten | selten | nur Anzeige (Datenschutz!) | Expertenbereich |
| Dienste/Aufgaben/Features | Verwaltung → Systemverwaltung | Windows verwalten | Administratoren | gelegentlich | administrative Änderung | Unterbereich |
| Autostart verwalten | Systemkontrolle → Autostart | Anmeldestart aufräumen | Fortgeschrittene | gelegentlich | Systemänderung | Hauptnavigation |
| WinGet-Pakete | Systemkontrolle → Pakete | Software aktualisieren | Standardbenutzer | regelmäßig | administrative Änderung | Hauptnavigation |
| Windows-Updates | Systemkontrolle → Wartung | Updates gezielt installieren | Administratoren | gelegentlich | administrative Änderung | Expertenbereich |
| Wartungspläne | Wartung → Assistenten / Pläne | Wartung geführt ausführen | Fortgeschrittene | gelegentlich | administrative Änderung | Hauptnavigation |
| SFC-/DISM-Reparatur | Wartung / Werkzeuge | Systemdateien reparieren | Administratoren | selten | administrative Änderung | Unterbereich |
| Cleaner | Wartung → Cleaner | Speicherplatz freigeben | Standardbenutzer | gelegentlich | potenziell kritische Änderung (endgültig) | Hauptnavigation |
| Große Dateien finden | Cleaner-Umfeld | Platzfresser finden | Standardbenutzer | gelegentlich | nur Anzeige | Unterbereich |
| 25 Windows-Werkzeuge | Systemkontrolle → Wartung | Gezielte Prüf-/Reparaturaktionen | Administratoren | gelegentlich | administrative Änderung | Expertenbereich |
| Befehlskatalog | Systemkontrolle → Wartung | Befehle nachschlagen | Fortgeschrittene | gelegentlich | nur Anzeige | Expertenbereich |
| Freie Administratorkonsole | Systemkontrolle → Wartung | Freie Befehle ausführen | Spezialisten | selten | potenziell kritische Änderung | Expertenbereich |
| Registry-Einstellungen (8) | Verwaltung → Systemverwaltung | Explorer/Datenschutz anpassen | Fortgeschrittene | selten | Systemänderung | Unterbereich |
| Ordnerspuren (Shellbags) | Systemkontrolle → Spuren | Nutzungsspuren auswerten | Spezialisten | selten | nur Anzeige | Expertenbereich |
| Schlüsselanzeige | Systemkontrolle → Schlüssel | BitLocker/Produkt/WLAN einsehen | Administratoren | selten | nur Anzeige (vertraulich) | Expertenbereich |
| Aufträge | Systemkontrolle → Aufträge | Auftragsstatus verfolgen | Fortgeschrittene | gelegentlich | nur Anzeige | Unterbereich |
| Diagnoseexport | Diagnose | Befunde weitergeben | Standardbenutzer | selten | nur Anzeige (Schwärzung) | Kontextaktion |
| xBar | Diagnose → xBar an/aus · Tray | Werte auf dem Desktop | Standardbenutzer | täglich | lokale Einstellung | Hauptnavigation (Desktop) |
| Gadgets | Einstellungen · Tray | Werte auf dem Desktop | Standardbenutzer | täglich | lokale Einstellung | Hauptnavigation (Desktop) |
| Temperatur-Overlay | separat | Temperaturen im Spiel | Fortgeschrittene | regelmäßig | lokale Einstellung | Unterbereich (Desktop) |
| Bildschirmschoner | Windows + Setup | Schoner mit Live-Platine | Standardbenutzer | täglich (passiv) | lokale Einstellung | Unterbereich (Desktop) |
| Tag/Nacht/Design | Darstellung / Desktop · Tray | Darstellung wählen | Einsteiger | gelegentlich | lokale Einstellung | Einstellungen |
| Skins/Deckkraft | Systemkontrolle → Einstellungen | Oberfläche anpassen | Standardbenutzer | selten | lokale Einstellung | Einstellungen |
| Warnschwellen | Systemkontrolle → Einstellungen | Warnungen kalibrieren | Fortgeschrittene | selten | lokale Einstellung | Einstellungen |
| Zentralverwaltung/Enrollment | Systemkontrolle → Einstellungen | Gerät zentral verwalten | Administratoren | selten | administrative Änderung | Eigener Bereich (siehe Vorschlag) |
| Einsteiger-/Expertenmodus | Systemkontrolle → Wartung | Darstellungstiefe wählen | alle | selten | lokale Einstellung | Einstellungen |

## Phase 2: Architekturvorschlag

### Kernbefund

AETHER hat die aufgabenorientierte Grundordnung bereits: Die sechs
Navigationsgruppen entsprechen weitgehend der Aufgabenlogik, die
↗-Kennzeichnung samt Tooltip trennt Navigation von Aktion, und die
Funktionssuche mit geführten Abläufen existiert. Die Unübersichtlichkeit
entsteht nicht durch fehlende Aufgabenstruktur, sondern durch fünf
konkrete, belegbare Strukturprobleme:

1. **Zwei-Ebenen-Split ohne erkennbares Prinzip.** Dieselbe Aufgabe verteilt
   sich auf Hauptfenster und Systemkontrolle: Gruppe „Wartung“ enthält
   Cleaner (intern) neben Windows-Werkzeugen (extern, Tab heißt dort
   „Wartung“); „Verwaltung“ mischt die interne Workbench mit vier externen
   Tabs; Einstellungen existieren dreifach (Darstellung / Desktop,
   Systemkontrolle → Einstellungen, Einsteiger-/Expertenschalter im
   Wartungs-Tab). Für Benutzer ist nicht vorhersagbar, was „drüben“ öffnet.
2. **Expertenmodus als Standard.** Neue Benutzer erhalten sofort freien
   Konsolenstart und vollen Katalog (`CockpitSettings.cs:20-22`), obwohl ein
   Einsteigermodus existiert und laut [SYSTEM-CONTROL](SYSTEM-CONTROL.md)
   Systemänderungen dort nicht freigibt.
3. **Keine priorisierende Startseite.** Das Cockpit ist eine dichte
   Messwand ohne „Gibt es Handlungsbedarf?“-Antwort; Befunde der
   Hintergrunddiagnose erscheinen nur als Tray-Ballon.
4. **Benennungsstreuung** (Abschnitt L) inklusive doppeltem „Wartung“ und
   abweichender Eintrag→Tab-Namen.
5. **Anzeige und Änderung visuell gleichrangig** innerhalb der Module
   (Abschnitt G/N), ohne durchgängige Admin-/Irreversibilitätskennzeichnung.

### Abgleich mit der 9-Bereiche-Hypothese

| Hypothese | Bewertung gegen den Bestand |
|---|---|
| 1 Überblick | Fehlt als priorisierte Startseite; Cockpit ist heute Messwand. **Übernehmen**, aber als Ergänzung vor dem Cockpit, nicht als Ersatz. |
| 2 Beobachten | Entspricht „Übersicht“+„Hardware“: Cockpit, Prozesse, Sensoren, Mainframe, Laufwerke. **Übernehmen**; Warnschwellen bleiben Einstellung. |
| 3 Analysieren | Messauswertung, SMART, Logs/Gaming, Statistik existieren verstreut (Diagnose-Gruppe und Systemkontrolle). **Übernehmen** als eigene Gruppe, da „nur Anzeige“-Risiko einheitlich. |
| 4 Diagnostizieren | Deckt „Diagnose“ ab (Windows-Diagnose, Ereignisse, Schutzstatus, Netzwerk-Tests, geführte Suche). **Übernehmen.** |
| 5 Verwalten | Deckt „Verwaltung“+Pakete+Autostart ab. **Übernehmen**, Pakete hierher statt unter Wartung. |
| 6 Warten und Reparieren | Deckt Operations, Cleaner, Werkzeuge, Wiederherstellungspunkt ab. **Übernehmen.** |
| 7 Desktop | Heute verstreut (Tray, Diagnose→xBar, Einstellungen→Gadgets, Darstellung / Desktop). **Übernehmen** — größter Einzelgewinn an Auffindbarkeit. |
| 8 Zentrale Verwaltung | Heute ein Unterabschnitt im Einstellungs-Tab. **Übernehmen** als klar getrennter eigener Bereich mit Zustandsanzeige. |
| 9 Einstellungen | Heute dreifach verteilt. **Übernehmen** als ein Einstieg mit Unterkategorien (Darstellung, Verhalten, Warnschwellen, Datenschutz, Experten). |

**Begründete Abweichungen von der Hypothese:**

- **„Hilfe“ bleibt eigenständig erreichbar** (lokales Hilfe-Center und
  Funktionssuche), statt in „Überblick“ aufzugehen: beide existieren,
  funktionieren und sind Rettungsanker für Verirrte.
- **Beobachten und Analysieren trennen, aber verlinken:** Messauswertung
  startet aus Beobachten heraus (Aufzeichnen) und wertet in Analysieren aus;
  harte Trennung würde den Arbeitsfluss brechen. Querverweise statt
  Dopplung.
- **Keine Zusammenlegung der beiden Fenster in Paket 1–3.** Die
  Systemkontrolle als getrenntes Fenster ist tief verankert (eigene
  Navigation, Aufträge im Fenster-RAM, Konsole). Kurzfristig genügt es,
  die Zuordnung Eintrag→Tab namensgleich zu machen und das Prinzip
  („Werkzeugfenster für risikobehaftete Verwaltung“) sichtbar zu erklären.
  Eine echte Konsolidierung wäre ein eigenes, später zu bewertendes
  Vorhaben mit hohem Regressionsrisiko.

### Vorgeschlagener Navigationsbaum (Zielbild)

```text
ÜBERBLICK (neue, schlanke Startseite)
├─ Systemstatus (belegbare Zustände, „nicht verfügbar“ bleibt sichtbar)
├─ Auffälligkeiten (Warnschwellen-Treffer, Hintergrunddiagnose-Befunde)
├─ Wenige zentrale Live-Werte + „Details anzeigen“ → Beobachten
├─ Schnellzugriffe (konfigurierbar, wenige)
└─ Funktion finden (prominent, vorhandene Suche)

BEOBACHTEN          ─ Cockpit (heutige Messwand) · Prozesse / Leistung ·
                      Sensoren · Mainframe / Board · Laufwerke
ANALYSIEREN         ─ Messauswertung · SMART ↗ · Frame-Analyse ↗ (PresentMon) ·
                      Statistik/Berichte
DIAGNOSTIZIEREN     ─ Windows-Diagnose · Ereignisse · Schutzstatus ·
                      Netzwerk-Tests ↗ · geführte Problemsuche (Strg+K)
VERWALTEN           ─ Systemverwaltung · Autostart ↗ · Softwarepakete ↗ ·
                      Aufträge ↗
WARTEN & REPARIEREN ─ Wartungspläne · Cleaner · Windows-Werkzeuge ↗
DESKTOP             ─ xBar · Gadgets · Temperatur-Overlay · Bildschirmschoner ·
                      Tag/Nacht & Design
ZENTRALE VERWALTUNG ─ Gerätestatus/Enrollment · Berichte/Einwilligung ·
                      Clientdienst-Hinweis (klar als optional gekennzeichnet)
EINSTELLUNGEN       ─ Darstellung · Verhalten · Warnschwellen · Datenschutz ·
                      Werkzeugpfade · Einsteiger-/Expertenmodus
EXPERTENBEREICH (gekennzeichnet, kein Sicherheitsersatz)
                    ─ Freie Konsole · Befehlskatalog (ändernd) ·
                      Windows-Updates · Ordnerspuren ↗ · Schlüssel ↗ ·
                      Mitschnitt/WinRM
HILFE               ─ Anleitung / Suche (unverändert)
```

Alle heutigen Funktionen bleiben erreichbar; es entfällt nichts. Die
Ebenenempfehlung je Funktion steht in der Inventartabelle.

### Flankierende Vorschläge (zur späteren Planung, nicht Teil von Phase 1/2)

1. **Einsteigermodus als Vorgabe für Neuinstallationen prüfen**, bestehende
   gespeicherte Einstellung unverändert migrieren (Expertenmodus bleibt für
   Bestandsbenutzer erhalten). Entscheidung des Auftraggebers nötig.
2. **Benennungskonzept** je Eintrag: Benutzerbezeichnung, Kurzsatz,
   technischer Begriff, Suchsynonyme — unter Beachtung der laufenden
   englischen Lokalisierung (Doppelarbeit vermeiden; Textänderungen mit dem
   RC-Lokalisierungsstand koordinieren).
3. **Risikokennzeichnung vereinheitlichen:** einheitliche visuelle Marker
   für „ändert das System“, „Administrator“, „nicht rückgängig machbar“ auf
   Basis der bestehenden Bestätigungsmechanik; keine Bestätigung wird
   reduziert.
4. **Semantische Farbrollen** über das vorhandene Skin-/Ressourcensystem
   definieren (Cyan Information/Navigation, Grün bestätigter Normalzustand,
   Gelb/Orange prüfbedürftig, Rot Fehler/kritisch, Magenta Akzent/Marke,
   Grau inaktiv). Befund: zentrale Oberflächenfarben liegen in
   Skin-Ressourcen (`Services/SkinCatalog.cs:45-67`), Neon-Inhaltsfarben
   sind teils hartcodiert (`Controls/HardwareBoardView.Drawing.cs:63-66`);
   eine schrittweise Zentralisierung ist möglich, Umfang im Plan zu klären.

### Offene Entscheidungen für den Auftraggeber

1. Reihenfolge gegenüber der laufenden RC-/Lokalisierungsarbeit
   (Benennungsänderungen erzeugen dort Doppelarbeit).
2. Einsteigermodus als Standard für Neuinstallationen: ja/nein.
3. Neue Startseite „Überblick“ zusätzlich vor dem Cockpit oder als
   umgebautes Cockpit.
4. Eigener Navigationsbereich „Zentrale Verwaltung“ sichtbar für alle oder
   erst nach Aktivierung in den Einstellungen.
5. Umgang mit dem Zwei-Fenster-Modell langfristig (beibehalten und erklären
   vs. späteres Konsolidierungsvorhaben).

Der konkrete Umsetzungsplan mit Arbeitspaketen, Teststrategie und
Akzeptanzkriterien folgt nach Freigabe als separates Dokument
(`docs/UX-REDESIGN-PLAN.md`, Phase 8 des Auftrags).
