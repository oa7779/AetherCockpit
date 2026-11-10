# AETHER — Process Command Center / System Correlation

## Abschlussstatus

**Lokal implementiert und geprüft; ausdrückliche Abnahme ausstehend.**
Stand: 10. November 2026. Keine öffentliche Versionsänderung, Paketierung
oder Veröffentlichung. Nach Vorlage dieses Berichts gilt **STOPP**.
Die bestehende Beta bleibt unverändert.

Community Request: Prozesse können jetzt direkt über ihr Kontextmenü
kontrolliert beendet werden. AETHER validiert den Zielprozess vor der Aktion,
behandelt fehlende Berechtigungen und aktualisiert den Prozesszustand
nach der Ausführung. Es werden keine personenbezogenen Angaben zum
Community-Beitrag übernommen.

Das Paket umfasst darüber hinaus Prozessbaum, Details, Ressourcenhistorie,
Systembeziehungen, lokale Snapshots und die Erweiterung der vorhandenen
Befehlspalette. Die ursprünglichen Cockpitinstrumente bleiben erhalten.
Technisch nicht belegbare Beziehungen sind ausdrücklich begrenzt, nicht
als implementierte Messwerte ausgegeben.

## Ausgang und lokale Commits

- Branch: `rc/grosse-entfesselung`.
- Vollständiger M3-Dokumentationsabschluss:
  `5dc869299b57af93083f53e680d5afca3fc14536`.
- Sauberer Implementierungsbaseline nach dokumentierter ausdrücklicher
  M3-Abnahme: `7cc28a120db3f6058503d6976bf90f37d55b21ca`.
- Produkt-/Teststand dieses Berichts: `be29028`.
- Der abschließende Dokumentationscommit enthält diesen Bericht und die
  aktualisierten Fachanleitungen; er ändert kein Programmverhalten.

| Commit | Inhalt |
|---|---|
| `7cc28a1` | Phase 0: ausdrückliche M3-Abnahme und neuer Auftrag, ohne Produktcode |
| `3e3d36f` | Prozessidentität, native minimale Rechte, Schutz- und Aktionskern |
| `b4f92ba` | Community-Aktion, Sicherheitsdialog, WM_CLOSE, Dateidetails; alter Verwaltungsweg auf denselben Kern |
| `4bf168d` | Stabile Prozesszeilen, Elternbezüge und begrenzte Historie |
| `45224f3` | Cancellation-sichere Prozesskohorten, Details und zielgenaue Teilresultate |
| `84a34b7` | Dienst-/Netzwerk-/Autostart-/Ereignisquellen und GPU-PDH |
| `953c1c5` | Lokale Snapshots, Vergleich, Importprüfung und redigierte Berichte |
| `4e45d82` | Bestehende Palette, Prozesssuche, Favoriten und letzte Werkzeuge |
| `e616a91` | Integrierte Prozesszentrale, Hardware-Rückwärtsnavigation und umbrechende Navigationstexte |
| `2cc2427` | Sicherheits-/Privacyfixtures, native Eigenprozesstests und integrierte UI-Proofs |
| `be29028` | Tatsächlich eingefrorene Anzeigepause bei weiterlaufender Historie, explizite unbekannte Werte und Kapazitätsprüfung |

Alle Änderungen sind lokale echte Commits mit Coauthor-Trailer, keine
Leercommits. Kein Push, Merge, Tag, Release oder Installer.
Die akzeptierten M3-Grenzen, vorhandenen Messdateien und früheren Proofs
wurden nicht nachträglich verändert.

## Architektur und geänderte Dateien

Die folgenden Pfade sind relativ zum Repository; `.xaml.cs` bezeichnet
jeweils den tatsächlich vorhandenen Codebehind.

| Bereich | Dateien |
|---|---|
| Native Identität, Rechte und Handlefreigabe | `src\SystemCockpit\Services\ProcessIdentity.cs` |
| Aktionen, Zielmenge und Resultate | `Services\ProcessActions.cs`, `src\SystemCockpit\ProcessActionDialog.cs` |
| Einzelprozessdetails und lokale Dateien | `Services\ProcessDetails.cs`, `Services\ProcessFileInfo.cs` |
| Gemeinsame Messung und Verlauf | `Services\DashboardMetrics.cs`, `Services\ProcessHistory.cs` |
| Systemkorrelation | `Services\ProcessCorrelation.cs`, `Services\ProcessGpu.cs` |
| Wiederverwendete Hardwarehelfer | `Services\HardwareCpuFrequency.cs`, `Services\HardwareIntelligence.cs` |
| Snapshots und Ansichten | `Services\ProcessSystemSnapshot.cs` |
| Prozessoberfläche | `src\SystemCockpit\Controls\ProcessCommandView.xaml`, `.xaml.cs` |
| Integration und Navigation | `src\SystemCockpit\MainWindow.xaml`, `.xaml.cs`, `MainWindow.Navigation.cs` |
| Hardware-Rückwärtsbezug | `Controls\HardwareIntelligenceView.xaml`, `.xaml.cs` |
| Bestehende Verwaltungsaktion | `Controls\AdminWorkbench.xaml.cs`, `Services\AdminManagement.cs` |
| Bestehende Befehlspalette | `src\SystemCockpit\TaskCenterWindow.cs`, `Services\TaskCatalog.cs`, `TaskCatalogEnglish.cs`, `TaskCenterPreferences.cs` |
| Tests | `tests\SystemCockpit.Tests\ProcessCommandTests.cs`, `ProcessAcceptanceTests.cs`, `Program.cs`, `ModernDesktopTests.cs`, `ProfessionalWorkflowTests.cs` |
| Dokumentation | `README.md`, `CHANGELOG.md`, `PROJEKTSTAND.md`, `docs\FEATURES.md`, `USER-GUIDE.md`, `DEVELOPMENT.md`, `TESTING.md`, `SECURITY-AND-PRIVACY.md`, `PERFORMANCE-BASELINE.md`, dieser Bericht |
| M3-Abnahmevermerk in Phase 0 | `docs\MILESTONE-3-HARDWARE-REPORT.md` |

Unqualifizierte `Services`-/`Controls`-Pfade in der Tabelle liegen unter
`src\SystemCockpit`. Keine neuen Abhängigkeiten oder Ersatzframeworks.
Server und Clientdienst erhalten weder Remote-Kill noch geänderte Wireformate.

### Gemeinsame Erfassung

`DashboardMetrics` bleibt der einzige dauerhafte Prozesssampler.
Die vorhandene Messschleife liefert dieselbe unveränderliche Probe an
Cockpit und Prozesszentrale. Nach Aktionen wird dieser Loop geweckt,
kein zweiter Vollpoller gestartet. Prozessobjekte werden auch dann vollständig
freigegeben, wenn Cancellation eine Enumeration vorzeitig beendet.
Shutdown wartet auf aktive Erfassung und Detailarbeit.

CPU ist die Differenz der Prozess-CPU-Zeit, normalisiert auf die gesamte
logische CPU-Kapazität. RAM und private Bytes werden als MiB angezeigt.
Prozess-I/O stammt aus Bytezählerdifferenzen und wird als MiB/s ausgewiesen;
das ist nicht automatisch physische Datenträgeraktivität.
Erste/unlesbare Proben bleiben unbekannt statt null.

PID und UTC-Startzeit bilden die stabile Identität. Elternbeziehungen
benötigen einen eindeutigen PID-Treffer und passende Erstellungsreihenfolge.
Eine wiederverwendete PID bekommt andere Zeilen und eine getrennte Historie.
Unbekannte Identität berechtigt zu keiner verändernden Aktion.

### Historie und Oberfläche

- Höchstens 8.192 gespeicherte Identitäten, je 120 Proben.
- Nicht mehr beobachtete Zeilen bleiben bis zu 20 Sekunden sichtbar;
  das ist kein bestätigtes Prozessende.
- Eine Samplinglücke über sechs Sekunden wird markiert; keine Interpolation.
- MIN/MAX/AVG verwenden nur vorhandene Werte. Netzwerkbytes werden nicht
  aus Adapterlast auf Prozesse verteilt.
- Regeln nennen CPU-Schwelle und Fünf-Proben-Fenster, streng steigendes RAM,
  mindestens 10.000 Handles beziehungsweise 500 Threads.
- Auf gezielte Detailabfrage: Temp-Pfad, fehlendes sichtbares Fenster bei
  mindestens zehn Prozent CPU, verweigerte Details, verschwundene Datei
  und mehrere beobachtete Starts derselben exakten Datei.
  Mehrere Instanzen sind kein Absturz- oder Neustartbeweis.
- Stabile Zeilen statt vollständigem Listenleeren; Recyclingvirtualisierung.
  Im Baum sortieren Geschwister, nicht beliebige Zeilen über Eltern hinweg.
- Suchtext, Zustandsfilter, exakter Identitätsfilter, Pause,
  links/rechts für Zweige und Enter für Details.
- Pause verwendet eine getrennte eingefrorene Zeilen-/Historienprojektion.
  Gemeinsame Proben laufen weiter; Fortsetzen übernimmt die aktuelle Probe.
  Navigation über globale Prozesssuche oder Cockpit beendet die Anzeigepause.
- Symbole stammen aus der lokalen Datei und werden erst bei Details geladen.
  Beschreibungen/Firmenangaben sind unbestätigte Dateimetadaten.

Retro-Neon und Modern hell/dunkel nutzen die bestehenden Ressourcen.
Keine neue Daueranimation; ReducedMotion wird übernommen.
Menüeinträge, Prozessliste, Inspektor, Suche und Bestätigung haben
Screenreader-Bezeichnungen beziehungsweise lesbare Control-Inhalte.
Lange Navigationstitel umbrechen innerhalb ihrer vorhandenen Seitenleiste.
Der Inspektor und Menüs halten fortgeschrittene Funktionen aus den
unveränderten Cockpitinstrumenten heraus.

## Prozessaktionen, APIs und Rechte

| Zweck | Tatsächlich verwendeter Pfad |
|---|---|
| Identität und Existenz | `OpenProcess`, `GetProcessTimes`, `WaitForSingleObject` |
| Minimalrechte | `PROCESS_QUERY_LIMITED_INFORMATION` 0x1000, `SYNCHRONIZE` 0x100000; nur für harte Aktion zusätzlich `PROCESS_TERMINATE` 0x1 |
| Pfad und Architektur | `QueryFullProcessImageName`, `IsWow64Process2` |
| Technischer Schutz | `IsProcessCritical`, `GetProcessInformation` / Protection-Level |
| Benutzer/Elevation/Integrität | `OpenProcessToken(TOKEN_QUERY)`, `GetTokenInformation`, SID/RID |
| Elternliste und I/O | Toolhelp-Prozesssnapshot, `GetProcessIoCounters` |
| Harte Beendigung | `TerminateProcess`; Erfolg erst nach signalisierter gleicher Handleinstanz |
| Fenster | `EnumWindows`, `GetWindowThreadProcessId`, `IsWindowVisible`, `IsHungAppWindow`, `PostMessage(WM_CLOSE)` |
| Datei | lokale Dateimetadaten, Icon, Offline-`WinVerifyTrust` mit anschließendem State-Close |
| Dateinavigation | Explorer-Selektion, `ShellExecuteEx` für Eigenschaften |
| Systemquellen | CIM/Windows-Netzwerkcmdlets, Registry Run, Aufgabenplanung, AppX-Manifeste, `EventLogReader` |
| GPU | Windows-PDH `GPU Engine`, PID/LUID/Engineinstanz, zwei Proben |

### Vertikale Community-Scheibe

1. Rechtsklick selektiert die tatsächlich angeklickte Zeile, auch wenn vorher
   eine andere markiert war. Beide Prozessoberflächen haben dasselbe Menü.
2. Preflight liest frische Identitäten und Schutzmerkmale. AETHER selbst,
   eigene Nachkommen, bekannte aktive SCM-Komponenten und tatsächliche
   Eigenprogramme bleiben geschützt.
3. Der Dialog zeigt Ziel, PID/Startidentität, Pfad, Benutzer-SID/Sitzung,
   verfügbare Firmenangabe, beobachtete Kinder und feste Zielmenge.
   Eine fehlende Kinderzahl wird ausdrücklich als nicht eindeutig angezeigt.
4. **Abbrechen** ist Default, Escape-Aktion und initialer Fokus.
   Harte Beendigung ist ausdrücklich als ungeordnet und verlustbehaftet erklärt.
5. Nach Bestätigung: erneuter Preflight, geprüfte Zielhandles, Schutzprüfung.
   Beim Baum zusätzlich erneuter Baumabgleich unmittelbar vor dem Eingriff.
   Abweichungen erfordern eine neue Bestätigung, keine automatische Erweiterung.
6. Protokollierte Absicht muss vor dem ersten Eingriff erfolgreich sein.
   Kinder werden vor Eltern angefordert, Resultate je Identität erfasst.
7. Begrenztes Warten bis fünf Sekunden pro Ziel an denselben Handles.
   Ein akzeptierter `TerminateProcess`-Aufruf allein ist kein Erfolg.
8. Ergebnis anzeigen und gemeinsamen Sampler zur Aktualisierung wecken.
   Handles werden auch bei Fehlern und Cancellation freigegeben.

Zustände umfassen bestätigtes/bereits eingetretenes Ende, erneute Bestätigung,
Sperre, AccessDenied, Fehler, Timeout, Abbruch und Teilwirkung.
Auch früh verschwundene Ziele und wiederverwendete PIDs ergeben eine
explizite deutsche Meldung ohne Ersatzaktion; nicht jede frühe Ablehnung
hat bereits den spezielleren `AlreadyExited`-Status.
Native Codes bleiben intern nachvollziehbar.
Ergebnisprotokollfehler werden nicht als erfolgreiche Journalierung ausgegeben.

Die bisherige Verwaltungsaktion verwendet dieselbe Engine statt eines
zusätzlichen PowerShell-Kill-Pfads. Keine pauschale System-EXE-Liste,
kein `PROCESS_ALL_ACCESS`, kein automatischer UAC-Neustart und keine
Umgehung von Windows-Schutz.

**Fenster schließen** ist ein eigener, identitätsgebundener `WM_CLOSE`-Pfad.
Nach zwei Sekunden ohne verifiziertes Ende bleibt lediglich
„Schließen angefordert“; keine automatische harte Eskalation.

## Systemkorrelation

Jede Beziehung trägt Quelle, Zeit, Verfügbarkeit und Datenschutzklassifikation.
Ein ausgewählter Prozess bleibt während der Abfrage handlegebunden;
ist er danach beendet, werden die PID-Beziehungen verworfen.

| Beziehung | Beleg / Grenze |
|---|---|
| Datei | abgefragter vollständiger Prozesspfad |
| Windows-Dienst | tatsächliche `Win32_Service.ProcessId`; Name/Status/StartMode |
| TCP/UDP | tatsächliche Owner-PID, Endpoint und Quellenzeit; UDP ohne erfundenes Remoteziel |
| Netzwerkadapter | genau eine passende lokale Unicastadresse; keine Wildcard-/Routing-/VPN-Heuristik |
| Autostart | beobachtete HKCU-/HKLM-Run-/RunOnce-Pfade einschließlich erfasstem 32-Bit-Run; nur eindeutig parsebarer absoluter Executable-Pfad |
| Geplante Aufgabe | `Actions.Execute`; gequotete absolute Pfade normalisiert, relative/ungültige Angaben ohne Prozessmatch |
| Installierte Software | exakter AppX-Manifest-Executable-Pfad plus InstallLocation, aktuelles Benutzerprofil |
| GPU | strikt geparste Windows-PDH-Instanz mit PID, Adapter-LUID und Engine; Enginewerte werden nicht addiert |
| Ereignis | bekannte Application-Error-1000-/Application-Hang-1002-Schemata, strukturierte Ziel-PID und Zeitpunkt innerhalb der Prozesslebenszeit |
| Hardware-Rückwärtssicht | dieselbe LUID beziehungsweise exakte Adapteridentität; Prozessstart und lebende Identität erneut geprüft |

`System.Execution.ProcessID` bezeichnet nicht automatisch das Ereignisziel.
Unbekannte XML-Schemata ergeben keinen Match.
Ereignisse sind auf Application, eine Stunde und höchstens 256 gelesene
Datensätze begrenzt. Systemquellen sind auf 20.000 Rohzeilen und den
vorhandenen 35-Sekunden-Runner begrenzt; abgeschnittene Antworten werden abgelehnt.
Quellenfehler bleiben ausdrücklich sichtbar, AccessDenied wird getrennt erkannt.

## Snapshots, Palette und Datenschutz

`aether.systemsnapshot/1` enthält Prozessidentitäten und ausgewählte Werte,
Systembeziehungen und bereits gelesene Hardwareverfügbarkeit mit Einzelzeiten.
Das ist **keine atomare Windows-Gesamtaufnahme** und startet keine neue
Hardwarevollinventur.

Vergleich zeigt neu/nicht mehr beobachtete Identitäten, Dienständerungen
einschließlich Starttyp, Autostarts/Aufgaben, TCP-/UDP-Änderungen,
Hardwarestatusänderungen sowie RAM-Differenzen ab 100 MiB.
Unvollständige Quellen unterdrücken sichere Neuanlage-/Entfernungsaussagen.
Mehrere Aufgabenaktionen mit gleichem Schlüssel werden zusammengefasst.
Eine Veränderung ist keine Sicherheitsdiagnose.

Normale JSON-/CSV-/Textberichte verwenden die vorhandene Datenschutzpolicy
und den gemeinsamen CSV-Formelschutz. Namen/Identitäten und sämtliche
Beziehungsmetadaten sind konservativ Sensitive; Aliase vermeiden
Kennungsnebenkanäle. Sensitive benötigt ausdrückliche Freigabe, Secret
bleibt ausgeschlossen. Vollständige lokale `.aethersnapshot`-Dateien
benötigen einen getrennten Warn-/Bestätigungsdialog und sind unverschlüsselt.
Import prüft Größe, Schema, Identitäten, Zeitfelder, Zahlen und Zustände,
verändert keine Live-Liste und führt keine Aktion aus.

Die bestehende `Strg+K`-Palette enthält jetzt 33 feste Arbeitsabläufe statt
25 sowie den vorhandenen Befehlskatalog. Acht neue Arbeitsabläufe öffnen
Prozesse, Suche, Hardwareexport, Verbindungen, Dienste, Snapshot, Vergleich
und Dateispeicherort. Eine Namens-/PID-Suche findet außerdem aktuelle
Prozessidentitäten aus derselben Probe. Kein Eingriff durch bloße Suche.
Werkzeugfavoriten und letzte Werkzeuge speichern keine dynamischen Prozesse.
Bis zu 16 benannte Ansichten speichern lokale Filterkonfiguration.
Es gibt keinen neuen Uploadpfad.

## Tests und reale Nachweise

| Lauf am endgültigen Produkt-/Teststand | Ergebnis |
|---|---|
| Release-Solutionbuild, `--no-restore` | 0 Warnungen / 0 Fehler |
| STA-Grundrunner | 4.770 Assertions |
| Professional-UI + Interface + Schoner + Branding einschließlich Grundrunner | 5.151 Assertions |
| Modern-Smoke, separat | 3.611 Assertions, 22 Bilder |
| Eigenprozess-Akzeptanz | native Details, geprüftes Ende, 100 Handlezyklen, Eigenschutz und reale lesende Quellen bestanden |
| Prozessproof | neun 5.000-Zeilen-Fälle, Menü/Dialog/MainWindow/Palette, Abbruch/Shutdown und 21 Bilder bestanden |

Die Zahlen enthalten mehrfach gemeinsame Tests und dürfen nicht addiert werden.
72 Prozessassertions ergänzen den M3-Grundstand; keine alte Assertion entfernt
oder deaktiviert. Bestehende UI-Tests selektieren den Öffnen-Button ausdrücklich,
prüfen Routen über die echten Tags und messen umbrochene Navigationstexte
mit erforderlicher tatsächlicher Höhe statt nur ungebrochener Textbreite.

### Synthetische Prüfungen

Race-Condition-, Schutz-, AccessDenied-, PID-Reuse-, Baumänderungs-,
Timeout-, Teilwirkungs-, Journal- und Cancellationfälle verwenden injizierte
Quellen. Dazu kommen native lesende Selbstidentität, Datenschutz-/Import-/
CSV-/Event-/GPU-/Historienfixtures und tatsächlich ausgeführte isolierte
PowerShell-Quellenfixtures.

Die UI-Prüfung öffnet echte WPF-Menüs und Dialoge ohne Bestätigen einer
destruktiven Aktion, prüft Cancel-Default/Fokus, den tatsächlichen
Rechtsklickhandler, exakte Filterung, Geschwistersortierung, globale Suche
und Shutdown einer laufenden abbrechbaren Abfrage.
Root-DPI ist synthetisch, kein physischer Monitorwechsel.

### Echte lokale Prüfung

Nur ein selbst gestartetes kurzlebiges Testhostkind wurde beendet.
Der produktive Nachkommenschutz wurde dafür nicht abgeschaltet:
eine ausschließlich auf diese eine Fixture-Identität begrenzte Testquelle
verwendet denselben nativen Handle-/Beendigungspfad.
Produktiver AETHER-Eigenschutz wurde separat real geprüft.

- 16 native Detailfelder.
- Ende an derselben Prozessinstanz bestätigt; Gesamtzeit der Fixtureaktion
  78,273 ms.
- 100 native Handlezyklen: 442 → 442.
- Reale Prozessidentitätsquelle: 404 Zeilen.
- Reale Systemquellen: 1.208 aggregierte Zeilen, davon Dienste 331,
  TCP 194, UDP 82, Autostart 11, geplante Aufgaben 215,
  AppX-Softwarezeilen 209, exakte Adapterkanten 166; alle verfügbaren
  Quellenzeilen dieses Laufs als Available erfasst.
- Die eigene ausgewählte Prozessinstanz hatte dagegen nur einen verfügbaren
  Dateibezug, drei ausdrücklich nicht verfügbare und einen nicht unterstützten
  Bezug. Globale verfügbare Zeilen sind kein Nachweis eigener GPU-/Dienstnutzung.

Keine fremden Prozesse beendet, keine realen Dienste/Autostarts/Aufgaben
verändert. Kein WM_CLOSE auf fremde Anwendungen, keine Schutzabschaltung,
Capture-, Reparatur-, Update-, Treiber- oder Lastaktion.
Nicht jede Windows-Fehlersituation wurde real provoziert.

## Performance, Proofs und Ressourcen

Für 5.000 synthetische Zeilen lagen erste Darstellung zwischen
227,63 und 324,26 ms und Updates zwischen 8,57 und 123,66 ms.
14 beziehungsweise 19 erzeugte Zeilencontainer zeigen aktive Virtualisierung.
Vorab festgelegt waren jeweils 2.500 ms und weniger als 200 Container.
[Vollständige Messreihe und Ressourcenwerte](PERFORMANCE-BASELINE.md#process-command-center).

30 zusätzliche 500-Zeilen-Lebenszyklen zeigten Handles 719 → 751,
Threads konstant 24 und schwankenden Speicher. Nach getrennter GC war
zunächst eine Ansicht noch referenziert; nach 30 leeren WPF-Fenstern und
erneuter GC waren alle 30 sammelbar. Auch die leeren Fenster erhöhten
Handles 751 → 781. Diese Gegenprobe beweist nicht die Ursache jedes Handles.
**Keine Langzeit-/Leakfreiheitsfreigabe.**

Endgültige lokale Evidenz liegt im Sitzungsordner
`64a11d20-8d22-4d0e-9eca-a7e030f0d975\files`:

| Datei / Ordner | Inhalt |
|---|---|
| `process-native-04.json` | aggregierte reale Eigenprozess-/Systemquellenprüfung |
| `process-proof-07\measurements.json` | neun Gate-Messungen und unverdeckte Ressourcenfolge |
| `process-proof-07` | 21 synthetische Tabellen-/Menü-/Dialog-/MainWindow-/Palettenbilder |
| `process-modern-03` | 22 synthetische Modern-Regressionsbilder |

| Nachweisdatei | SHA256 |
|---|---|
| `process-native-04.json` | `8210D64AAC0D2F70238FCF84EA5DE1618D1DB2394FA9E5F9273A8907DE28577C` |
| `process-proof-07\measurements.json` | `19E3E114F174BC8BE7BE57D019A125FDEF16D7FA9F816012670E46DFB01640D2` |
| `process-proof-07\main-process-retro-neon.png` | `2683BC5C53C61DB2989233B653D06A8F63AD5C0F05FA5B556976C7868B737062` |
| `process-proof-07\confirmation-windows-modern-dark.png` | `DA8F4C99D05676BC2B2060762188FA1F8486947F1E3409C3F402B7B8CFE4BC87` |

Die Daten wurden nicht hochgeladen. Reale Diagnosebestände und M3-Evidenz
bleiben unverändert lokal; die Bilddaten verwenden erkennbare Fixtures.

### Korrigierte Fehlversuche

- Früher Compilerfehler durch falsch platzierten Übersetzungseintrag behoben;
  fehlende explizite englische Routentexte ergänzt.
- Erster Retro-Proof hatte einen ungestylten weißen Grid; bestehender
  NeonDataGrid-Style wiederverwendet. Menüs ebenfalls an die Palette angebunden.
- Erster realer Lauf brach innerhalb der Aufgabenquelle an einer ungültigen/
  gequoteten Executable-Angabe ab. Isoliert und real erneut geprüft:
  Normalisierung und nicht zuordenbare Einzelzeilen statt Quellenabbruch.
- Ereignisse auf tatsächlich bekannte Ziel-PID-Schemata eingegrenzt;
  beliebiges `ProcessId` ist kein ausreichender Beleg.
- Proof 03 zeigte fehlenden Dispatcher-SynchronizationContext im Testhost
  bei asynchronem Abbruch. Testhost wie vorhandene Hardwaretests korrigiert;
  Produktions-UI-Kontext nicht durch Threadpoolzugriffe ersetzt.
- Professional-UI deckte die durch neue Favoriten nicht mehr eindeutige
  Buttonsuche, Routenprüfung über Anzeigetext und zu lange Navigation auf.
  Echte Routen/Buttons geprüft und vollständigen Textumbruch implementiert.
- Integrierter Proof 05 hatte einen ungültigen synthetischen Commit-Limitwert
  null; Fixture berichtigt, keine Produktionsskalierung abgeschwächt.
- Zusätzliche Prüfung der Pause fand weitergebundene Live-Zeilen:
  nun getrennte eingefrorene Projektion, nachweislich weiterlaufende Historie
  und aktuelles Fortsetzen. Harte Kapazität gegen mehr als 8.192 Identitäten geprüft.
- Frühere `process-native-01/02/03`, `process-proof-01/02/03/04/05/06` und
  `process-modern-01/02` bleiben als frühere beziehungsweise teils unvollständige
  Evidenz bestehen. Endgültig sind nur die oben benannten Läufe.
  Keine Messgrenze wurde nachträglich angehoben.

## Bekannte Grenzen und nicht implementierte Punkte

1. Keine per Prozess zugeordneten Netzwerkbytezähler: Owner-PID-Tabellen
   enthalten Endpunkte, nicht Durchsatz. Keine neue ETW-/Kernel-Capturearchitektur.
2. Prozess-I/O enthält nicht zwingend physische Datenträgerzugriffe.
   Keine Zuordnung auf ein bestimmtes Laufwerk ohne belegte Quelle.
3. Keine kausale Temperatur-/Sensor-/Energiezuweisung zu Prozessen.
4. AppX-Zuordnung ist konkret implementiert. Klassische
   Uninstall-InstallLocation oder Produktnamen beweisen keine Dateieigentümerschaft;
   keine universelle MSI-/Win32-Softwarezuordnung behauptet.
5. Keine rohe WER-Dateisammlung mit heuristischer Namenszuordnung.
   Qualifizierte strukturierte Application-Ereignisse sind davon getrennt.
6. Run-/Aufgaben-/AppX-Quellen sind kein vollständiger Windows-Autostartmonitor;
   Shell-Starter, relative Befehle, alle Benutzerprofile und alle
   Hintergrundaktivierungen werden nicht aufgelöst.
7. Polling übersieht Prozesse, die vollständig zwischen zwei Proben leben.
   Mehrere beobachtete Dateistarts sind kein vollständiger Neustart-/Crashmonitor.
8. Windows-Prozessbäume sind nicht atomar. Neue Kinder können auch nach der
   letzten Prüfung entstehen; bereits angeforderte Beendigungen sind unumkehrbar.
9. Eigenschutz ist bewusst konservativ. Nicht vollständig prüfbarer Schutz
   kann eine Aktion blockieren; Rechte werden nicht erweitert.
10. Pfad, Symbole, Beschreibung und Offline-Vertrauen betreffen die gerade
    vorhandene lokale Datei, nicht beweissicher das ursprüngliche Prozessabbild.
    Signaturcache ersetzt keine Online-Sperrlistenprüfung.
11. Native Datei-/Vertrauensaufrufe lassen sich nicht mitten im Windows-Call
    zwangsweise abbrechen. Cancellation gilt zwischen Aufrufen;
    Shutdown wartet auf deren Rückkehr.
12. Lange Gesamt-Baumaktionen können fünf Sekunden je Ziel benötigen.
    Timeout ist kein verifiziertes Ende; Abbruch macht keine Teilwirkung rückgängig.
13. Lokale vollständige Snapshotdateien und eigene Filtertexte sind
    unverschlüsselt. Exporte vor Weitergabe weiterhin prüfen.
14. Keine manuelle Screenreader-/Hochkontrastabnahme, physische DPI-/
    Monitor-/Dock-/Standby-/Resume-Matrix oder mehrstündige Betriebsmessung.
    Keine universelle Windows-/Hardwarefreigabe aus den lokalen Tests.
15. Der dokumentierte WPF-Host-Handleanstieg bleibt nachzubeobachten.
    Die native Prozesshandleprobe und sammelbare Ansichten grenzen ihn ein,
    beweisen aber keine allgemeine Ressourcenfehlerfreiheit.

Die ausdrücklich akzeptierten
[M3-Grenzen](MILESTONE-3-HARDWARE-REPORT.md) werden weder aufgehoben noch
als offene M3-Blocker wieder eingeführt.

## Regressionen und offene Abnahme

Relevante Release-, Grund-, Interface-, Schoner-, Branding-, Professional-
und Modern-Läufe sind erfolgreich. Bestehende Hardware-/Netzwerk-/
Verwaltungsfixtures sind im Grundrunner enthalten. Keine Sensorprovider,
Serverprotokolle oder bestehenden Releasepakete geändert.
Der separate Server-/Client-Verwaltungsrunner wurde für dieses Paket
nicht neu ausgeführt; seine Verträge wurden nicht verändert.
Keine Installer-/Installations-/Remoteprüfung oder Releasefreigabe.

Repositoryweite Dokumentationsprüfung: 36 Markdown-Dateien,
keine neuen fehlenden relativen Ziele und keine ungültigen Markdownanker.
Die zehn bekannten historischen, lokal fehlenden `dist`-Ziele in der
Übergabe sind unverändert. `git diff --check` ist ohne Beanstandung.
Die beiden zuvor dokumentierten SHA256-Werte der finalen M3-Read-only-
und Timingdatei wurden erneut abgeglichen und sind unverändert.

Offen ist die **ausdrückliche Abnahme dieses Prozesspakets** einschließlich
der genannten Grenzen und der manuellen Bedienbewertung auf Zielrechnern.
Dieser Bericht erteilt keine automatische Freigabe für weitere Phasen
oder Veröffentlichungen. **Bis zur Abnahme wird nicht weiter implementiert.**
