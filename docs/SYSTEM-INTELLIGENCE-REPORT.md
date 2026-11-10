# AETHER System Intelligence — Abschlussbericht

**Status: lokal implementiert und geprüft; ausdrückliche Abnahme ausstehend.**
Keine Veröffentlichung, Paketierung oder öffentliche Versionsänderung.
Dieser Bericht bewertet den neuen Auftrag, nicht rückwirkend die bereits
abgenommenen Hardware- und Prozesspakete.

## 1. Ausgangsbranch

`rc/grosse-entfesselung`. Direkt im bestehenden Workspace gearbeitet.
Keine Remote-Dateien über lokale Änderungen kopiert, kein Merge.

## 2. Ausgangscommit

`c7b6d962dd215575c2ab9b82154eca961dfbf90f`, abgeschlossener Prozessbericht.
Die vom Auftrag erklärte Prozessabnahme wurde zuerst dokumentiert.
Phase-0-Baseline danach:
`5480685c046f6aa97b3fecce21ccd92aa887dcd5`.
Der Arbeitsbaum war vor Beginn der neuen Implementierung sauber.
M3-Grenzen, frühere Proofs und Diagnosedaten wurden nicht verändert.

## 3. Abschlusscommit

Verifizierter **Quell-/Testabschluss**:
`cd2ce3345901975cae1002111ed7148b31299b79`.
Alle unten als abschließend bezeichneten Läufe prüfen dessen Produkt- und
Testinhalt; die letzten Änderungen danach betreffen ausschließlich Dokumente.

Dieser Bericht gehört zum separaten Dokumentationsabschluss. Dessen eigenen
Hash kann eine Datei nicht vorab enthalten. Eindeutig auflösen:

```powershell
git log -1 --format=%H -- docs\SYSTEM-INTELLIGENCE-REPORT.md
git status --short
```

Öffentliche Version unverändert `0.7.0-beta.45`, Dateiversion `0.7.0.45`.
Bestehende Pakete sowie Build-/Publishdefinitionen sind unverändert.

## 4. Lokale Commits

| Commit | Inhalt |
|---|---|
| `5480685c046f6aa97b3fecce21ccd92aa887dcd5` | Prozessabnahme und saubere neue Baseline |
| `e3d08e4d5403c3c4b3ef3969a2c7782006ff09ca` | Gemeinsames Evidenzmodell, begrenzte Historie, Speicherung, Compare und Windows-Quellen |
| `44cfc8301aa8fc8eb93bd9f2eff2e1cfcc859214` | Quellenjoins, Watch, Recorder und geschützte Retention |
| `4eac06ae0abccce9f1e2ce87c69ba3eac064a24f` | Gemeinsame Fachansicht, System Map und exakte globale Navigation |
| `04486c30aa84efc8e9aca1e66c693bbfdba92880` | Modell-/Quellen-/UI-/Lebenszyklus- und Regressionstests |
| `c165f3e349d5c241a4cdb6226cf849fa4ac8fdb7` | Qualifizierte Ereigniskanten und wiederverwendete vollständige Prozesshistorien |
| `cd2ce3345901975cae1002111ed7148b31299b79` | Zielgebundene Watch-Regeln, geprüfte WLAN-Joins, monotone Speicherintervalle und begrenzte Lebenszykluszustände |
| Dokumentationsabschluss, über obigen Befehl auflösbar | `docs(system-intelligence): finalize implementation evidence and handoff` |

Die Reihenfolge folgt den tatsächlichen Abhängigkeiten: gemeinsames Modell
vor Fachprojektionen, danach integrierte Bedienung und Nachweise.
Deshalb kein separater Datenspeicher oder künstlicher Commit pro Fachreiter.
Keine Leercommits; lokale Commits mit Co-Autoren-Trailer, ohne Push.

## 5. Geänderte Dateien

Insgesamt 37 Dateien einschließlich dieses Berichts gegenüber dem Ausgang:

- Projekttexte: [README](../README.md), [CHANGELOG](../CHANGELOG.md),
  [PROJEKTSTAND](../PROJEKTSTAND.md).
- Fachunterlagen: [DEVELOPMENT](DEVELOPMENT.md), [FEATURES](FEATURES.md),
  [PERFORMANCE-BASELINE](PERFORMANCE-BASELINE.md),
  [PROCESS-COMMAND-CENTER-REPORT](PROCESS-COMMAND-CENTER-REPORT.md)
  ausschließlich zur dokumentierten Abnahme,
  [SECURITY-AND-PRIVACY](SECURITY-AND-PRIVACY.md),
  [SYSTEM-INTELLIGENCE](SYSTEM-INTELLIGENCE.md), [TESTING](TESTING.md),
  [USER-GUIDE](USER-GUIDE.md), dieser Bericht.
- Ansichten:
  [EventBrowser.xaml.cs](../src/SystemCockpit/Controls/EventBrowser.xaml.cs),
  [HardwareIntelligenceView.xaml.cs](../src/SystemCockpit/Controls/HardwareIntelligenceView.xaml.cs),
  [ProcessCommandView.xaml.cs](../src/SystemCockpit/Controls/ProcessCommandView.xaml.cs),
  [SystemIntelligenceView.xaml](../src/SystemCockpit/Controls/SystemIntelligenceView.xaml),
  [SystemIntelligenceView.xaml.cs](../src/SystemCockpit/Controls/SystemIntelligenceView.xaml.cs).
- Hauptfenster:
  [MainWindow.Navigation.cs](../src/SystemCockpit/MainWindow.Navigation.cs),
  [MainWindow.xaml](../src/SystemCockpit/MainWindow.xaml),
  [MainWindow.xaml.cs](../src/SystemCockpit/MainWindow.xaml.cs).
- Evidenzkern:
  [IntelligenceModel.cs](../src/SystemCockpit/Services/IntelligenceModel.cs),
  [IntelligenceSession.cs](../src/SystemCockpit/Services/IntelligenceSession.cs),
  [IntelligenceSources.cs](../src/SystemCockpit/Services/IntelligenceSources.cs),
  [IntelligenceStore.cs](../src/SystemCockpit/Services/IntelligenceStore.cs),
  [IntelligenceWindowsEvents.cs](../src/SystemCockpit/Services/IntelligenceWindowsEvents.cs).
- Gemeinsame Quellen und Suche:
  [EventMonitor.cs](../src/SystemCockpit/Services/EventMonitor.cs),
  [ProcessCorrelation.cs](../src/SystemCockpit/Services/ProcessCorrelation.cs),
  [ProcessGpu.cs](../src/SystemCockpit/Services/ProcessGpu.cs),
  [ProcessSystemSnapshot.cs](../src/SystemCockpit/Services/ProcessSystemSnapshot.cs),
  [SystemInventory.cs](../src/SystemCockpit/Services/SystemInventory.cs),
  [TaskCatalog.cs](../src/SystemCockpit/Services/TaskCatalog.cs),
  [TaskCatalogEnglish.cs](../src/SystemCockpit/Services/TaskCatalogEnglish.cs),
  [TaskCenterWindow.cs](../src/SystemCockpit/TaskCenterWindow.cs).
- Tests:
  [InterfaceTests.cs](../tests/SystemCockpit.Tests/InterfaceTests.cs),
  [ModernDesktopTests.cs](../tests/SystemCockpit.Tests/ModernDesktopTests.cs),
  [Program.cs](../tests/SystemCockpit.Tests/Program.cs),
  [SystemIntelligenceTests.cs](../tests/SystemCockpit.Tests/SystemIntelligenceTests.cs).

Keine geänderten Abhängigkeiten, Server-/Clientprotokolle, Installer,
historischen Releasepakete oder externen Wartungscenter-Dateien.

## 6. Architektur

Eine gemeinsame Entitätsprojektion versorgt zwölf Arbeitsbereiche:
Übersicht, Timeline, Change / Compare, Reliability, Software, Treiber,
Startup, Netzwerk 2.0, Diagnose, Recorder, Watch und System Map.
Die bestehende globale Suche und Fachnavigation werden erweitert, nicht ersetzt.

Vorhandene Dashboard-/NativeMetrics-, Ereignis- und Hardwareproben werden
weitergereicht. Die vollständige Prozesshistorie stammt aus der Prozesszentrale.
Manuelle Inventuren arbeiten asynchron und abbrechbar. Die separat aktivierbare
Inventarbeobachtung nutzt seriell die vorhandene 60-s-Schleife; standardmäßig aus.
Keine zusätzliche permanente Samplerschleife, kein ETW- oder Paketmitschnitt.

## 7. Persistentes Datenmodell

Schema 1 trennt Snapshot, Baseline, Sitzung, Aufzeichnung und Einstellungen.
Entität: ID, Klasse, Name, Felder, Quellenmetadaten.
Kante: vorhandene Endpunkte, Beziehung, Quellenmetadaten.
Snapshot: GUID, Zeitpunkt, Domänenabdeckung und optionaler Kontextbezug.
Sitzung: Timeline, Regeln, Startbeobachtungen und letzte Watch-Auslösungen.
Aufzeichnung: Umfang, Proben, Marker, Vorher/Nachher, Dauer und Lücken.

Die lokale Hülle enthält Typ, Payload und SHA-256. Serialisierte Dateizugriffe,
Tempdatei und atomarer Austausch schützen vor unvollständigen Schreibständen.
Ungültige Einzelbestände werden isoliert und sichtbar gemeldet.
Prüfsumme ist Integritätskontrolle, keine Authentifizierung.
Der Legacy-Prozessimport ergänzt weder unbekannte Vollständigkeit noch Hostbezug.

## 8. Timeline

Gemeinsame Proben liefern Prozessauftreten/-verschwinden und Messlücken.
Verglichene Inventare liefern Software-, Dienst-, Aufgaben-, Autostart-,
Treiber-, Geräte- und Netzwerkänderungen. Windows liefert qualifizierte
Ereignisse; Benutzeraktionen, Aufzeichnungen und Watch ergänzen Marker.

Ereigniszeit und Beobachtungszeit bleiben getrennt. Stabile Quellenschlüssel
verhindern Doppelzählung; verspätete strukturierte Subjekte können ergänzen.
Kategorie, Text und Zeitraum filtern die chronologische Darstellung.
Inspektor, Vergleich, technische Quelle und datenschutzbewusster Export
verwenden dieselben Identitäten. Verschwundene Prozesse gelten nicht allein
deshalb als bestätigt beendet. Sehr kurze Prozesse zwischen Proben können fehlen.

## 9. Change Intelligence

Benannte Baselines und Snapshots halten den tatsächlich verfügbaren Zustand.
`New`, `Removed`, `Changed`, `Unchanged`, `NotComparable` und `Incomplete`
trennen beobachtete Differenzen von fehlender Vergleichbarkeit.
Neu/entfernt benötigen vollständige Quelldomänen auf beiden Seiten.
Bekannte passende Identitäten bleiben auch unter partieller Domäne vergleichbar.
Stale oder nicht verfügbare Entitäten ergeben keine sichere Zustandsdifferenz.

Kontextbezug: SHA-256 aus Windows-MachineGuid und aktuellem Benutzer-SID.
Andere oder unbekannte Installations-/Benutzerkontexte bleiben nicht vergleichbar.
Kein Rückschluss auf historische Installationen ohne gespeicherte Zustände.

## 10. Reliability

Wiederholungen werden nach Kanal, Provider, Event-ID, Kategorie und vorhandenem
strukturiertem Subjekt gruppiert. Erstes/letztes beobachtetes Auftreten und
Anzahl sind nachvollziehbar. Gleiches Subjekt ist keine installierte
Softwareidentität. Ein von Windows gelieferter Reliability-Wert bleibt
Windows-Wert; AETHER erfindet keinen Gesundheitsscore.

Ein vorhandener Snapshot vor dem ersten Auftreten wird als Vorhervergleich
angeboten. Ohne ihn wird der damalige Rechnerzustand nicht rekonstruiert.
Änderungen in zeitlicher Nähe sind keine Verursacherliste.

## 11. Startup Intelligence

Windows-Bootdaten, interaktive Anmeldesitzungen, Startquellen und aktuelle
Ressourcen liegen im gemeinsamen Modell. Logonidentität enthält Sitzungsbeginn,
nicht nur wiederverwendbare LogonId.
BootTime-Vergleiche benötigen mindestens zwei verfügbare Windows-Einträge
derselben OS-/Schema-/Startartgruppe.

AETHER-Ruhekriterium: zehn aufeinanderfolgende gültige gemeinsame Proben mit
CPU unter 10 Prozent und Abstand höchstens sechs Sekunden.
Beginn ist AETHER-Beobachtungsbeginn, nicht Windows-Boot oder Login.
Abgeschlossene gleich definierte Intervalle zeigen kürzeste/längste Spannen.
Rückwärts laufende Zeit beginnt ein neues unvollständiges Intervall.
Keine behauptete lückenlose Boot→Login→Desktop-Messung bei nachträglichem Appstart.
Diagnostics-Performance ist im realen Abschlusslauf **AccessDenied**.

## 12. Software Intelligence

Vorhandene Uninstall-Filter liefern getrennte Registry-/Paketidentitäten,
Versionen und verfügbare Zusatzangaben; AppX liefert deklarierte Executables.
Aktivität benötigt explizite Software→Datei←Prozess-Beziehungen.
Ähnliche Namen, Publisher und Installationsordner beweisen keine Win32-Zuordnung.

Aktuelle und in bis zu acht letzten Snapshots belegte Instanzen sind sichtbar.
CPU/RAM wiederverwenden höchstens 16 passende Instanzen mit je 32 letzten Proben
der vorhandenen Prozesshistorie. Separater CPU-Spitzenprozess-Kurzverlauf ist
als Auswahl gekennzeichnet. GPU-/Dienst-/Netzwerkbezüge werden an der belegten
Prozessinstanz geöffnet; keine vollständige Ausführungschronik behauptet.
Dateimetadaten und Offline-Vertrauen werden nur auf Anforderung gelesen.

## 13. Driver Intelligence

PnP-Gerät, signierter Treiberbericht, Kernel-Dienst und lokaler Treiberpfad
werden über exakte Schlüssel verbunden. Version, Provider, Datum, INF,
Signaturmeldung, Gerätestatus und Startdaten stammen aus Windows.
Gruppen folgen gemeldeten PnP-Klassen. WLAN braucht zusätzlich NDIS-Medium
1 oder 9 und genau eine passende PnPDeviceID; Zusatzquelle steht im Feld.
Mehrdeutige Adapteridentitäten werden vor der Medienfilterung ausgeschlossen.

Systemgeräte werden nicht als bestimmter Chipsatz umgedeutet:
Windows-PnP-Klasse `System` liefert diese Unterscheidung nicht.
Timeline und Compare zeigen beobachtete Treiberänderungen.
Kernel-PnP-Ereignisse können dieselbe Geräteinstanz referenzieren;
das beweist keine Fehlerursache. Keine Treiberinstallation oder Datenbank ergänzt.
Im nativen Abschluss wurden keine eindeutig zugeordneten WLAN-Treibergruppen
erzeugt; die Ableitung und Mehrdeutigkeitsabwehr sind synthetisch nachgewiesen.

## 14. Network Center

Adapter und strukturierte TCP-/UDP-Endpunkte zeigen Adressen, numerische Ports,
Status, Quellenzeit und verifizierte Prozessidentität.
Listenerfilter: TCP Listen beziehungsweise gebundene UDP-Endpunkte.
Owner-PID wird nur während erneut geprüfter Prozesslebenszeit verbunden;
Adapter nur bei eindeutiger exakter lokaler Unicastadresse.
Wildcardlistener bekommen keine geratenen Adapterbeziehungen.

Von einem eindeutigen frischen Endpointziel zur Prozesszentrale navigieren;
umgekehrt bestehen die Prozess-Netzwerkbeziehungen weiter.
Kein IP-Reputationsdienst, kein Sicherheitsurteil aus Ports und kein
aus Adapterraten erfundener Prozess-Netzwerkdurchsatz.

## 15. Slow-PC-Diagnose

Die große Aktion „Warum ist mein PC gerade langsam?“ zeigt aktuelle
CPU-/RAM-/Prozess-I/O-Spitzen und vorhandene Ressourcen-, Adapter- und Sensorwerte.
Quelle, Messzeit, Identität, gemeinsamer Kurzverlauf und zeitliche
Ereignisnachbarn bleiben nachvollziehbar. Technische Zeilen öffnen den Inspektor.

Prozess-CPU ist auf alle logischen Prozessoren normiert.
Prozess-I/O ist kein physischer Datenträgerwert.
Globale GPU-/Sensorbeobachtungen werden keinem Prozess als Ursache zugeschrieben.
Dies ist eine lesende Bestandsauswertung, keine automatisch bewiesene Diagnose.

## 16. Diagnostics Recorder

Start, sofortiges Stoppen und Verwerfen; Profile Ressourcen,
Ressourcen und Timeline sowie benutzerdefinierter Umfang.
Aufnahme nutzt ausschließlich vorhandene Proben.
Bis zu 1.800 Proben, 2–3.600 s, 16 CPU-/RAM-Spitzenprozesse,
acht Adapter mit je zwei Raten und 16 Sensoren je Probe.
Bytebudgets: Proben 1 MiB, Marker 512 KiB, Kontext vorher/nachher je 512 KiB.

Zu großer Start wird abgewiesen; zu großer Endkontext ausdrücklich unvollständig.
Mess-/Budgetlücken, Proben, Spitzen, Prozessbeiträge, Marker und Differenzen
stehen in Inspektor und Export. UTC-Uhrsprünge werden ausgewiesen,
monotone Dauer steuert die Laufzeit. Automatisches Ende folgt der nächsten Probe.
Stoppen wirkt auch bei laufender Inventur sofort, wartet vor Speicherung
aber deren Abbruch ab. Verwerfen speichert die Aufnahme nicht.
Vor Start sichtbarer Hinweis auf sensible unverschlüsselte Speicherung.

## 17. Watch

Maximal 32 Regeln: CPU, RAM, Prozess-CPU/-RAM, Prozessauftreten/-verschwinden,
vorhandener GPU-/Sensorwert, Freiplatz, neue Listener/Autostarts/Dienste,
Hardwareänderung, Adaptertrennung sowie frische Crash-/Hangereignisse.
Inventarregeln benötigen tatsächliche vollständige Folgeinventare.

Quellenfrische, Dauer, Cooldown, Verriegelung bis Rückkehr und Neustartverhalten
sind geprüft. Fehlende, NaN-, doppelte, rückwärts laufende oder unterbrochene
Proben erfüllen keine Dauerschwelle. Fehlender Freiplatz bleibt unbekannt.
Zielgebundene Crash-Regeln benötigen das exakte strukturierte Windows-
AppPath-/ExeFileName-/AppName-Subjekt, nicht Nachrichtentext oder Namensähnlichkeit.
Alte/duplizierte Ereignisse lösen keine neue Warnung aus.

Aktionen: Benachrichtigung, Timeline, Snapshot und Diagnosemarker.
Auslösung erklärt Ziel, Wert, Schwelle, Dauer, Zeitpunkt und Quelle.
Snapshot speichert automatisch sensibel und unverschlüsselt, auch bei
deaktivierter Sitzungsspeicherung; Auswahlhilfe weist darauf hin.
Keine destruktive oder administrative Watch-Aktion.

## 18. Universal Compare

Gemeinsamer Vergleich für Jetzt/Snapshot, Snapshot A/B, Baseline/Jetzt und
Aufnahmezustände. Vor/Nach einer Installation sind ausdrücklich gespeicherte
Zustände, kein nachträglich erfundener Installationsverlauf.
Aufzeichnungen zeigen ihren eigenen Vorher-/Nachhervergleich.
Für A/B lassen sich gespeicherte Zustände auswählen; Windows-Startzeitvergleiche
haben zusätzliche Vergleichbarkeitsregeln.

Hardware, Treiber, Software, Dienste, Tasks, Startquellen, Netzwerk,
Prozesse, Ressourcen und Ereignisse verwenden dieselben Differenzzustände.
Verglichen werden Entitäten und Felder; kein gesonderter Kanten-Diffbericht.

## 19. System Map

Interaktive Karte mit Typ-/Textfilter, Zoom, Pan, Auswahl, Fokus auf direkte
Nachbarn, Reset und tatsächlicher ungerichteter Pfadsuche.
Maximal 200 sichtbare Knoten, Begrenzung ausdrücklich angezeigt.
Inspektor zeigt Beziehungsart, Quelle, Zeit, Confidence, Availability und Privacy.
Stale Kanten werden nicht als verfügbare Beziehungen gezeichnet.
Lange Pfade werden mit tatsächlicher Gesamtlänge begrenzt dargestellt.
Keine Fantasieverbindungen oder dauernde Kartenanimation; Pfad ist keine Kausalkette.

## 20. Globale Suche

Bestehendes `Strg+K` findet Werkzeuge, Prozesse, Entitäten, Ports, Timeline,
Snapshots, Aufzeichnungen und Regeln mit typisierten Zielen.
Exakte Identität statt Teilstringauswahl. Verschwundene Entitäten wählen
keinen Ersatz. Prozess-, Event-, Hardware- und Dienstnavigation verwendet
vorhandene Fachziele, soweit eindeutig; sonst bleibt die Evidenz im Inspektor.
Dynamische Suchwerte werden nicht als separate Suchhistorie gespeichert.

## 21. UI / UX

Sichtbarer Eintrag **Übersicht → System Intelligence**, gemeinsame
Übersicht mit Zuständen, Timeline, Aufnahmen und Regeln statt Designneubau.
Bestehende Cockpitinstrumente und Sicherheitsdialoge bleiben erhalten.
15 Hauptseiten, 25 Navigationsrouten und 37 katalogisierte Arbeitsabläufe
sind die bewusst aktualisierten Interface-Verträge.

Virtualisierte Tabellen mit Recycling und stabiler Auswahl, gesonderter
Inspektor, zugängliche Namen und Tastaturbedienung.
Bereichswechsel entfernt veraltete technische Ziele.
Retro-Neon, Modern hell/dunkel, Reduced Motion und Root-DPI geprüft.
Keine vollständige Screenreader- oder physische Multi-Monitor-Abnahme behauptet.

## 22. Quellen

Vorhandene Dashboard-/NativeMetrics, M3, ProcessCorrelation/PDH und SystemInventory;
ergänzend `Win32_PnPEntity`, `Win32_PnPSignedDriver`, `Win32_SystemDriver`,
`Win32_ReliabilityStabilityMetrics`, `Win32_OperatingSystem`,
interaktive `Win32_LogonSession`, `Get-NetAdapter -IncludeHidden`
und sieben begrenzte Ereigniskanäle.
Quellen und exakte Joinregeln stehen zentral in
[Architektur und Datenquellen](SYSTEM-INTELLIGENCE.md#datenquellen-und-belastbare-verknüpfungen).
Kein `Win32_Product`, zusätzlicher Kernelzugriff oder externer Dienst.

## 23. Confidence-Modell

Bestehende `SourceMetadata`, `SourceType` und `DataConfidence` werden
weiterverwendet. Direkt gelesene Windows-Daten bleiben ProviderReported,
AETHER-Ableitungen werden konservativ als Unconfirmed gekennzeichnet.
Eine exakte Beziehung ist kein Ursachenbeweis und keine Echtheitsgarantie
importierter Dateien. Die abgeleitete WLAN-Gruppierung besitzt einen eigenen
Feldquellenbeleg neben unveränderten ursprünglichen Treibermetadaten.
Keine neuen Prozent-Vertrauenswerte oder Sicherheitsbewertungen.

## 24. Availability-Modell

Available, Stale, AccessDenied, Unavailable, Unknown, Unsupported und
ProviderError bleiben getrennte Ergebnisse des vorhandenen Modells.
Quellenvollständigkeit ist eine zusätzliche Domäneneigenschaft, nicht bloß
ein vorhandener Wert. Erfolgreich leer ist nicht fehlgeschlagen.
Dynamische Endpoints/GPU werden nach zehn Sekunden, Dienste/Adapter nach
60 Sekunden ohne Neulesung als stale projiziert; ihre Kanten ebenso.
Zeitpunkt bleibt erhalten. Erreichte Ereignisgrenzen sind unvollständig.

## 25. Datenschutz

Lokale Prozess-, Pfad-, Netzwerk-, Geräte- und Ereignisdaten sind sensibel.
Secret-Klassen werden in Entitäten, Kanten und Abdeckung zurückgewiesen.
Ereignis-XML ist auf erlaubte Felder begrenzt; Freitexte bleiben sensibel.
JSON/CSV/Text bieten Redaktion; CSV behält Formelschutz.
Vollständiger Snapshotexport braucht sensible Freigabe.

Sitzungsverlauf und Inventarbeobachtung sind separat opt-in.
Snapshots und abgeschlossene Aufnahmen sind davon unabhängig.
Lokale Dateien sind unverschlüsselt; Watch-Snapshot kann automatisch speichern.
Kontexthash ist ebenfalls sensibel; rohe MachineGuid/SID werden dafür nicht
persistiert. Kein neuer Upload, Cloudzwang oder Telemetriepfad.
[Datenschutz und Löschung](SECURITY-AND-PRIVACY.md#lokale-system-intelligence).

## 26. Retention

Standard 14 Tage, 128 Dateien, 64 MiB gemeinsam.
Konfiguration 1–90 Tage, 8–512 Dateien, 1–256 MiB; Einzeldatei maximal 8 MiB.
Nur exakt eigene Typ-/GUID-N-Dateinamen werden verwaltet.
Temp und Quarantäne zählen mit. Genau ein maximal 64-KiB-Einstellungsbestand
ist altersunabhängig, zählt aber zum Gesamtbudget.

Timeline im RAM maximal 20.000 Einträge, Ressourcen 120 Proben,
Regeln 32, Startbeobachtungen 128; Aufnahme und Map zusätzlich begrenzt.
Automatisches Speichern und Wiederholungsabstand verwenden monotone 30-s-Intervalle.
Abschalten der Sitzungsspeicherung entfernt die gespeicherte Sitzung,
nicht separat angelegte Snapshots/Aufzeichnungen.
Einzellöschung und Zurücksetzen sind vorhanden; fremde Dateinamen bleiben erhalten.

## 27. Performance

Vorab festgelegte Gates mit 5.000 synthetischen Entitäten bestanden:
erste Darstellung 90,6533–181,3064 ms, Filter 17,4947–60,2851 ms,
Map 189,1499–289,4996 ms, jeweils unter 2.500 ms.
13 Retro-/26 Modern-Zeilencontainer, jeweils 200 sichtbare Mapknoten.
Gemeinsame Liveverarbeitung 1,4803 ms; 32 Regeln mit Recorder 21,9605 ms,
beide unter 100 ms. Vergleich 5,8453 ms, Pfad 6,5376 ms,
Suchprojektion 12,2574 ms, jeweils unter 2.500 ms.
Keine Beschleunigung ohne Vorhervergleich behauptet.

## 28. Ressourcen

20 Ansichtszyklen: Handles 702 → 724, Threads 20 → 20;
Private Bytes 587.386.880 → 400.293.888,
Working Set 315.944.960 → 305.864.704.
Eine Ansicht zunächst erreichbar, nach 30 leeren WPF-Fenstern und einer
Sekunde Dispatcher-Nachlauf alle 20 sammelbar.
Danach Handles 741, Threads 23. Handleanstieg bleibt nachzubeobachten.

Beruhigter Testhost: in 2.000,8298 ms keine aufgelöste CPU-Zeit gemessen.
Das beweist weder Nullverbrauch noch vollständigen Produkt-Idle.
Native Kaltinventur: Handles 270 → 370,
Private Bytes 15.273.984 → 56.401.920; enthält Erstinitialisierung.
Getrennte Prozesshandleprüfung: 100 Zyklen 442 → 442.
Keine Langzeit-Leakfreiheit abgeleitet.
Vollständige GC-/Allokationswerte und Einordnung:
[Performance-Baseline](PERFORMANCE-BASELINE.md#system-intelligence).

## 29. Tests

Releasebuild, STA-Grundrunner, kombinierter Professional-/Interface-/
Schoner-/Brandinglauf, Modern-Smoke, Prozess-UI/-Native und
Intelligence-UI/-Native erfolgreich.
STA-Konsolenrunner, kein `dotnet test`-Projekt.
Reproduzierbare Befehle und Nebenwirkungen:
[TESTING](TESTING.md#system-intelligence).
Keine Abhängigkeiten installiert oder wiederhergestellt.
Dokumentationsabschluss verändert danach kein Programmverhalten.
175 relative Dateiverweise in den betroffenen Dokumenten geprüft:
keine neuen fehlenden Ziele. Zehn bereits am Ausgang vorhandene Verweise
auf hier nicht vorhandene historische Pakete bleiben unverändert;
dafür wurden weder Altpakete erzeugt noch historische Nachweise umgeschrieben.
UTF-8 ohne Ersatzzeichen und alle 39 Berichtspunkte geprüft.

## 30. Assertions

| Lauf | Tatsächliches Ergebnis |
|---|---|
| Releasebuild | 0 Warnungen / 0 Fehler |
| STA-Grundrunner | 4.892 Assertions, davon 122 Intelligence-Fälle |
| Kombiniert Professional/Interface/Schoner/Branding | 5.274 einschließlich Grundrunner |
| Modern-Smoke | 3.611 unabhängige Smoke-Assertions |
| Intelligence-Proof | neun Theme-/Root-DPI-Fälle, alle zwölf Bereiche, 20 Lebenszyklen und Gates bestanden |
| Intelligence-Native | Quellen, kontrollierte Benachrichtigung/Aufzeichnung und unmittelbarer Stopp bestanden |
| Prozessregression | 5.000-Prozess-Proof sowie native End-/Handle-/Eigenschutzprüfung bestanden |

Läufe mit enthaltenem Grundrunner nicht addieren.
UI-/Native-Modi haben zusätzliche verhaltensbezogene Gates, keine erfundene
Assertiongesamtzahl. Die 122 neuen Fälle ergänzen die bestehende Grundsuite.

## 31. Visuelle Proofs

Unveränderte lokale Nachweise, nicht in Releasepakete kopiert:

```text
C:\Users\jens-\.copilot\session-state\64a11d20-8d22-4d0e-9eca-a7e030f0d975\files
```

- `intelligence-proof-14`: 50 PNGs und `measurements.json`;
  neun Root-DPI-Bilder 100/150/200 Prozent über drei Themes,
  drei Kartenbilder, 36 Fachbereichsbilder, integriertes Hauptfenster und Palette.
- `modern-intelligence-02`: 22 Bilder bestehender Modern-/Neonflächen.
- `process-proof-si-02`: bestehender Prozessproof mit Menüs, Bestätigungen,
  integrierter Navigation und neun Theme-/Root-DPI-Fällen.

Die Bilder zeigen synthetische Fixtures. Bedienung, sichtbare Kartengröße,
Zoom/Pan/Fokus/Pfad, Filter, Tastatur-/Exaktnavigation und Lifecycle werden
zusätzlich ausgeführt; ein Bild allein beweist das nicht.
Frühere Proofverzeichnisse bleiben einschließlich Fehlversuchen erhalten.

## 32. Reale Tests

`intelligence-native-10.json`: nur lesende reale Quellen,
5.412 Entitäten / 3.267 Kanten in 8.903,4997 ms.
425 Prozesse, 114 Adapterprojektionen, 959 Ressourcen, 345 Softwareeinträge,
860 Dateien, 795 Dienste, 341 Endpunkte, elf Autostarts, 213 Tasks,
333 Hardwarezeilen, 337 Treiber, sieben Boot-/Logonzeilen und 672 Ereignisse.
Diese Zahlen sind Quellprojektionen, keine Zahl physischer Geräte.

Diagnostics-Performance AccessDenied; Application, System und Updatefenster
wegen 128er-Grenze unvollständig. Kein künstlich vollständiger Bestand.
In eigenem Temp-Speicher: eine echte Ressourcenprobe, eine absichtlich auf
0 Prozent gesetzte Benachrichtigungsregel, eine manuell gestoppte Aufnahme.
Inventur lief beim Stopp noch; unmittelbare Wirkung 7,2908 ms,
monotone Aufnahmedauer 103,4595 ms. Speicherung und eigenes Cleanup bestätigt.
Das ist kein längerer Diagnoseeinsatz.

`process-native-si-02.json`: eigenes kurzlebiges Fixture in 86,9651 ms
einschließlich Endprüfung beendet, Absicht/Ergebnis vorhanden,
100 Handlezyklen unverändert und produktiver Eigenschutz aktiv.
1.362 nur lesende Systemquellzeilen.
Keine fremden Prozesse, Dienste, Tasks, Netzwerke oder Treiber verändert.
Keinen realen Crash, Reboot, Resume oder Hardwarewechsel ausgelöst.

## 33. Synthetische Tests

Schema-/Enum-/Null-/Integritätsfehler, Secret-Ausschluss, Redaktion, Migration,
Kontextbezug, vollständige/leere/fehlende Quellen und Vergleichszustände.
Graphpfade, Kreise, getrennte Knoten, Quellenfelder und Bootvergleichbarkeit.
Watch-Dauer/Frische/Lücken/Cooldown/Neustart/Ziele und viele Regeln;
Recorder-Start/Stop/Verwerfen/Budgets/Uhrsprung/Shutdown.
Retention mit fremden Dateien, Präferenzreserve, Quarantäne und Bytegrenzen.

Tatsächlich ausgeführte isolierte PowerShell-Fixtures prüfen fehlende/doppelte
PnP-IDs, Logonstart, numerische NDIS-Medien und allowlistgebundene Crash-Subjekte.
Sie ersetzen Systemabfragen durch Fixtures; kein Nachweis echter Hardwareänderung.
5.000-Entitäten-UI, alle Fachbereiche, Vergleich, Watch-Editor, Aufnahme,
exakte Suche und Integration werden im echten WPF-Dispatcher bedient.

## 34. Verworfene Lösungswege

- Eigene Datenbank, zusätzliche permanente Poller und neue Sensorprovider:
  gemeinsame Quellen und begrenzte versionierte Dateien reichen für den Umfang.
- Programmzuordnung über Namen/Publisher/Ordner, Prozessdurchsatz aus Adapterraten,
  Sensorursachen aus gleichzeitigen Spitzen: kein belastbarer Join.
- Logger-PID als Absturzprozess: nicht dieselbe technische Bedeutung.
- Beliebige Ereignis-XML-Payloads: bekannte Felder und begrenzte Subjekte genügen.
- Pauschaler Boot-/Gesundheits-/Malwarescore: Quellen liefern das nicht.
- Unmittelbare GC-Erreichbarkeit als Leakurteil: leere WPF-Gegenprobe und
  Dispatcher-Nachlauf zeigen vorübergehende Hostreferenz; Handletrend bleibt offen.

## 35. Korrigierte Regressionen und Fehlversuche

- Reale Watch-Regel blieb stumm: NativeMetrics lag wenige Millisekunden nach
  Dashboard-Zeit und wurde fälschlich als Zukunft verworfen.
  Tatsächliche Beobachtungszeit und passende Quellenzeit beheben das;
  synthetischer Fall und reale Benachrichtigung bestehen.
- Recorderstopp wartete hinter Inventur: Stopp jetzt unmittelbar, Inventurabbruch
  wird erst vor dem Speichern abgewartet; realer Überlappungsfall geprüft.
- Rückwärts laufende Zeit konnte negative AETHER-Startintervalle erzeugen:
  neues unvollständiges Intervall; Recorder behält Uhrsprung und monotone Dauer.
- Bereichswechsel/Teilstringsuche konnten veraltete oder ähnliche Ziele zeigen:
  Inspektor zurücksetzen und genaue Identitätsauswahl.
- WLAN-Filter konnte aus mehrdeutiger Gerätezuordnung eine scheinbar eindeutige
  machen: Eindeutigkeit vor Medienfilterung; Gegenfall geprüft.
- Crash-Zielfeld wurde im generischen Ereignisfeed nicht ausgewertet:
  erlaubte strukturierte Subjekte, exakter Vergleich und Skriptfixture ergänzt.
- Speicherintervalle und Snapshot-Typcache: monotone Zeit und mitentfernte
  Cacheeinträge verhindern Uhrsprungblockade beziehungsweise Wachstum.
- Interface-Erwartung 24 Routen war nach neuer Navigation veraltet:
  bewusst auf 25 korrigiert, nicht eine fehlende Route ausgeblendet.
- Proof 05 interagierte nach Root-DPI-Aufnahme mit abgehängter Ansicht:
  Test hängt sie wieder ein. Proofs 06/07 scheiterten an unmittelbarer
  Erreichbarkeit; echte Gegenprobe/Nachlauf ergänzt, nicht Fehlerdateien ersetzt.

Frühere Proofs 01–13 und native Versuche bleiben erhalten.
Der fehlgeschlagene native Watch-Versuch erzeugte keinen erfolgreichen
Abschlussdatensatz; er wird nicht als bestandene Messung geführt.

## 36. Bekannte technische Grenzen

Abfragefenster, Zugriffsrechte, verpasste Kurzprozesse und Quellenlücken
begrenzen Beobachtung. AccessDenied ist kein Implementierungsersatz.
Keine nachträgliche Rekonstruktion unbeobachteter Zustände.
Boot/Login/Desktop sind ohne durchgängige Quellen nicht vollständig vermessen.
PnP-Systemklasse liefert keine eigenständige Chipsatzbestimmung.
Klassische Win32-Installation liefert keine allgemeine beweisbare Prozesszuordnung.
GPU-/Netzwerk-/Sensorbezüge hängen von vorhandenen exakten Windows-Schlüsseln ab.

Map zeigt höchstens 200 Knoten, Recorder bewusst begrenzte Top-Prozesse und
Werte; Compare ist kein separater Kanten-Diff.
Ereignisnachbarschaft und gleiche Signatur beweisen keine Kausalität.
Diese Grenzen sind dokumentiert, aber für System Intelligence noch nicht
vom Benutzer als abgenommen erklärt.

## 37. Bewusst nicht implementierte Funktionen

ETW/Kernelsessions, Paketmitschnitt, dauerhafte Vollprozess-Netzwerkbytes,
externe Reputation, proprietäre Identitätsdatenbanken, automatische Reparaturen
und destruktive Watch-Aktionen sind nicht Bestandteil dieses Ausbaus.
Keine zusätzlichen Spiel-/Crash-/Boot-Lastgeneratoren und kein Hintergrunddienst.
Recorder verwendet drei reale Profile plus auswählbaren Umfang statt einer
Liste unimplementierter Spezialprofile.
Keine Seriennummern-/Firmware-/Treiberänderung als Diagnoseabkürzung.
Keine neue Veröffentlichung oder Installererstellung.

## 38. Verbleibende Risiken und Nachbeobachtung

Mehrstündiger Produktbetrieb, CPU-/GPU-Mehrlast, Hosthandletrend,
physische DPI-/Monitor-/Resume-/Hotplugwechsel und eine breitere Windows-/
Hardwarematrix sind noch nicht nachgewiesen. Root-DPI ersetzt sie nicht.
Screenreader-Namen und Tastaturpfade sind vorhanden, aber keine unabhängige
vollständige Screenreaderabnahme. Lokale unverschlüsselte Dateien und
automatische Watch-Snapshots müssen vor Weitergabe geprüft werden.
SHA-256 schützt nicht gegen absichtliche Änderung durch Schreibberechtigte.
Persistente Daten können durch zulässige Retention entfernt werden;
ein Baseline-Name setzt die Grenzen nicht außer Kraft.

## 39. Finale Abnahmeempfehlung

**Empfehlung: den lokal implementierten Umfang mit den hier offengelegten
technischen und prüfbezogenen Grenzen zur ausdrücklichen Abnahme vorlegen.**
Alle Säulen sind in das gemeinsame Modell, Quelleninspektion, Vergleich,
Suche und Navigation eingebunden; relevante Builds, Tests und Regressionen
sind erfolgreich. Nachweise unterscheiden reale und synthetische Ergebnisse.
Dokumentation und Projektübergabe sind aktualisiert.

Dies ist keine Langzeit-, Installations-, RC- oder öffentliche Produktfreigabe.
Es wurde nichts veröffentlicht, gepusht, gemergt, getaggt oder paketiert;
öffentliche Version und vorhandene Releases bleiben unverändert.
**Jetzt auf die ausdrückliche Abnahme warten.**
