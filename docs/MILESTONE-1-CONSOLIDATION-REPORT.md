# AETHER — Meilenstein 1: technische Konsolidierung

Stand: 9. November 2026. **MEILENSTEIN 1 ABNAHMEBEREIT** für die
freigegebene technische Grundlage. Kein RC-Freigabenachweis.
Die neun Arbeitspakete sind bearbeitet; Umfang und verbleibende Grenzen
stehen unten. Die ausdrücklich freigegebene Fortsetzung ist abgeschlossen.
Stopp vor Meilenstein 2 und 3, ohne neue Version oder Paketierung.

Nachträglicher Abnahmestand: Der Auftraggeber hat Meilenstein 1 ausdrücklich
abgenommen und anschließend Meilenstein 2 freigegeben.
Der folgende Bericht hält den damaligen Abschluss fest;
[Fortsetzung und Ergebnis](MILESTONE-2-NEON-FRAME-REPORT.md).

## 1. Baseline-Commit

Branch: `rc/grosse-entfesselung`.
Baseline: `cc39d0f3a4d03a1d94fb6f84fcc26e58e14bff10`
(`chore(rc): establish pre-consolidation baseline`).
Vor dem Commit stimmten alle 320 Git-relevanten Dateien nach Größe und
SHA256 mit dem finalen Nachweis aus Freigabe 0.5 überein. Alle 66
freigegebenen Kandidaten wurden aufgenommen; danach sauberer Arbeitsbaum.
Das ist der vorhandene Entwicklungsstand vor technischen M1-Änderungen.

Die externe Sicherung `D:\AetherBaselineSnapshots\rc05-20261109-212329`
mit 8.720 geprüften Dateien, Gitbundle und separatem Abschlusssupplement
bleibt erhalten. Gleicher Datenträger, keine unabhängige Katastrophensicherung.
[Sicherungs- und Restorenachweise](MILESTONE-0-CLOSURE-REPORT.md).
Ignorierte Builds, Pakete, Testausgaben und Diagnosefixtures sind nicht
Bestandteil der neuen Commits.

## 2. Lokale Meilenstein-1-Commits

| Commit | Inhalt |
|---|---|
| `3a46fe8` | Ausgangsvalidierung und begrenzte Dateizugriffsdiagnose |
| `191742c` | Kompatible Quellen-, Ergebnis- und Datenschutzverträge |
| `7c95714` | Ausgabegrenzen, Abbruch und deterministische Prozessfreigabe |
| `83dae2c` | Additives technisches Journal und explizite Fehlerdiagnostik |
| `d48e3a2` | Lebenszyklus und serialisierte Wake-Signale der Hintergrundworker |
| `19091ab` | Native Fehlerklassen, Secret-Feldnamen, lesbare Journalzustände und stderr-Grenzen |
| `a29781e` | Ausführbarer synthetischer Messmodus, Messbericht und RC-Sprachstrategie |
| `7a6f007` | Probe-Streamabschluss, Sensorsemaphore und sichtbare native Bereinigungsfehler |
| Abschlusscommit dieses Berichts | `docs(rc): close milestone one consolidation` |

Der letzte Hash steht nach dem Commit in `git log`; kein selbstreferenzielles
Umschreiben oder Amend. Alle Commits bleiben lokal. Kein Push, Tag, Release
oder Merge und keine Änderung von Versionsprofilen oder historischen Paketen.

## 3. Veränderte technische Verträge und Kompatibilität

| Oberfläche | Änderung / erhaltene Kompatibilität |
|---|---|
| Shared-Bibliothek | Neue `SourceMetadata`, `SourceObservation<T>`, `OperationOutcome`, Datenschutz- und Eventtypen; keine Ersetzung des Verwaltungsprotokolls |
| `SensorReading` | Unverändert; expliziter `Observe(metadata)`-Adapter, keine erfundene Herkunft aus nullable Werten |
| `CommandResult` | Bestehender Konstruktor und Deconstruction unverändert; zusätzliches `OutputTruncated` ist `JsonIgnore` |
| `ManagedActionResult` | Unverändert; vorhandene Kürzungsinformation wird im Ergebnisadapter verwendet |
| BoardFeed / Named Pipe | Name, Frame und Wireformat unverändert; Name wird mit freigegebenen Windows-/Processwrappern ermittelt |
| History / LiveSample / Exporte | Formate unverändert; keine pauschale Migration oder automatische Neuklassifikation alter Dateien |
| Aktionsjournal | Bestehende Actionnamen, `timestamp` und `details` bleiben; strukturierte Metadaten unter additivem `technical` mit Schema 1 |
| Server / Client / Ressourcen | DTOs, Sprachressourcen, Sprachwahl und Zustimmungslogik unverändert |

Adapter sind die vorbereitete Integrationsgrenze, **keine Behauptung einer
vollständigen Migration aller Live-Modelle**. Laufende Anzeigen, Skalen,
Aktualisierungsfrequenzen und der Retro-Neon-/Modern-Funktionsumfang bleiben.

## 4. Quellen- und Metadatenmodell

[Verträge](../src/AetherOperations.Shared/SourceObservation.cs) und
[Adapter](../src/SystemCockpit/Services/SourceAdapters.cs) bilden ab:

- Value, Unit und explizites `HasValue`; gemessene Null bleibt ein Wert.
- Source, SourceType, Timestamp, Confidence und Availability.
- RequiresElevation, IsEstimated, IsCalculated, IsSensitive,
  Classification, Error und optionalen DocumentationLink.
- Herkunft `Direct`, `Derived`, `Database`, `Unconfirmed`, getrennt von
  Confidence `Unknown`, `Unconfirmed`, `ProviderReported`, `Corroborated`.
- Verfügbarkeit `Available`, `Unavailable`, `AccessDenied`, `Unsupported`,
  `ProviderError`, `Stale`, `Unknown`.

`Present` akzeptiert nur vorhandene, endliche numerische Werte mit
Available/Stale. `Missing` verlangt einen expliziten anderen Zustand;
ein `default(T)` ohne `HasValue` ist kein Messwert. `AsStale` bewahrt
Originalzeitpunkt und Wert. Fehlgeschlagener Refresh wird dadurch nicht
als neue Messung ausgegeben. Unbestätigte Herkunft kann kein bestätigtes
Vertrauen beanspruchen; abgeleitete Werte sind als Derived erkennbar.
Datenbankherkunft wird nicht in Direct umgewandelt.
Dokumentationslinks müssen absolute HTTP(S)-URLs ohne Zugangsdaten sein.
Die Semantik wird mit gültigen und ungültigen Fällen geprüft.

Die bestehende Board-Hardwarecache-Zeitbasis ist davon noch nicht
umgestellt; die Grenze wird in Abschnitt 10 ausdrücklich erhalten.

## 5. Fehlerbehandlungsmodell

[OperationOutcome](../src/AetherOperations.Shared/OperationOutcome.cs)
trennt Success, Partial, Failed, Cancelled, TimedOut, Unavailable,
AccessDenied, Unsupported und Unknown mit Quelle, stabilem Code,
FailureKind, optionalem Exit-Code und HRESULT.

| Ereignis | Neue technische Einordnung |
|---|---|
| Prozess-Exit 0, vollständig / gekürzt | Success / Partial |
| Generischer Prozess-Exit ungleich 0 | Failed/Process, ursprünglicher Exit-Code |
| Angeforderter Caller-Abbruch | Cancelled/Cancellation |
| `TimeoutException` | TimedOut/Timeout |
| OCE ohne nachgewiesenen Caller-Abbruch | Unknown, nicht automatisch Timeout oder Erfolg |
| Rechtefehler einschließlich Win32 5 | AccessDenied/Access |
| Fehlende Datei / Win32 2 oder 3 | Unavailable/MissingTool |
| Nicht unterstützter Provider / Plattform | Unsupported/Provider |
| HTTP-, Socket- oder Adapternetzfehler | Failed/Network |
| Übrige Exception | Failed/Provider, keine Erfolgsersatzdaten |

Der generische Exit-Adapter ersetzt **nicht** werkzeugspezifische
Semantik, insbesondere smartctl-Bitmasken, Update-/Neustartzustände und
bereits wirksam gewordene Windows-Schritte. OperationsHub bewahrt
Fehler und möglichen Teilerfolg; scheitert zugleich das Auditjournal,
bleiben beide Ursachen in einer AggregateException erhalten.
Ein Live.log-Schreibfehler stoppt Folgewartung und führt nicht zu Erfolg.
Keine neue versteckte Wiederholung, keine gelöschten Assertions.

Erwartete Shutdown-OCE und „Prozess bereits beendet“ bleiben gezielte
Lebenszyklusbehandlung. Native Win32-Fehler beim Shared-Prozessbereinigen
werden jetzt mit PID, Fehlercode und HRESULT über Trace sichtbar.
Bestehende sichtbare Modulfehler werden nicht blind global umgeschrieben.

## 6. Logging- und Journalmodell

Die acht Severityklassen sind TRACE, DEBUG, INFO, NOTICE, WARNING, ERROR,
CRITICAL und AUDIT. [TechnicalLog](../src/SystemCockpit/Services/TechnicalLog.cs)
nutzt vorhandenes Trace: Error/Critical als TraceError, Warning als
TraceWarning, übrige Kategorien bleiben benannt. Kein weiteres Framework;
Server-ILogger bleibt erhalten. Trace hat damit **keinen neuen dauerhaften
Sink** und ersetzt kein persistentes Audit.

[TechnicalEvent](../src/AetherOperations.Shared/TechnicalEvent.cs) enthält
Korrelations-ID, Modul, Aktion, Quelle, klassifiziertes Ziel, Rechte- und
Bestätigungsangabe, Start/Ende, StopBehavior, Timeout, Rücknahmemöglichkeit
und Outcome einschließlich Teilresultat/Exit/HRESULT. Der Zeitstempel
liegt im äußeren Aktionsjournal. Unbekannte Rechte-/Bestätigungsangaben
bleiben nullable statt erfunden. OperationsHub verwendet lokale/entfernte
Zieltypen; keine neuen Rohbefehle oder Exceptiontexte im technischen Teil.
Abschlussereignisse existieren jetzt auch bei Fehler, Timeout und Abbruch.

| Bestehender Pfad | Größen-/Aufbewahrungsstand und Grenze |
|---|---|
| ActionJournal | 16 MiB je Tagesdatei, höchstens 30 Dateien; erste übergroße Zeile wird jetzt ebenfalls abgewiesen |
| LocalHistory | Opt-in; 16 MiB je Tagesdatei, sieben Dateien |
| NetworkAlerts | Bestehende Prüfung um 2 MiB und Bereinigung nach sieben Tagen; kein neuer globaler Logvertrag |
| Operations-`Live.log` | Bestehendes bestätigtes Ablaufprotokoll; keine allgemeine Rotation/Größenbegrenzung ergänzt |
| Server-/Kontenaudit | Bestehende 100.000-Eintragsgrenzen; JSON-Speicherregeln separat |
| Host-ILogger / Trace | Vorhandene Providerkonfiguration; keine neue persistente Rotation behauptet |
| PCAP | Eigenes bestehendes Größen-/Zeit-/Aufbewahrungsmodell, kein Textjournal |

Technische Zielwerte Sensitive/Secret werden redigiert. Das migriert
**nicht** die alten `details` mit Ziel-/Berichtspfaden oder bestätigte
Liveprotokolle. Bestehende Exportwarnungen bleiben erforderlich.
Weitere Datenschutzgrenzen: Abschnitt 12 und
[Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md).

## 7. Cancellation- und Timeoutmodell

Die unterschiedlichen Betriebsmodelle bleiben absichtlich erhalten.
„Prozessbaum“ meint den eigenen lokalen Baum; es ist keine Garantie,
dass gestartete Dienste, Remoteprozesse oder Geräteaktionen enden.

| Pfad | Abbruch / Prozess / Timeout | Teilresultat, Änderungen und Rücknahme |
|---|---|---|
| Shared ManagedActionRunner | Caller-Cancellation, eigener Baum; 30 Minuten | Ausgabe begrenzt, Kürzung explizit; Tooländerungen nicht automatisch zurückgenommen |
| CommandRunner / AdminShell | Caller-Cancellation, eigener Baum, Timeout je Aufruf | Wartet Prozess und beide Streams ab; 2.000.000 Zeichen je stdout/stderr, Überlauf explizit; keine implizite Rücknahme |
| OperationsHub lokal / remote | Sofortiges Signal an Runner; lokal 90 Minuten, remote drei Minuten | Live-/Journalteilresultate; Remoteeffekte können fortbestehen |
| Geführte Wartung | Stop nach laufendem Schritt, Timeout unendlich | Folgeaktionen stoppen; bereits abgeschlossene Schritte bleiben, Wiederherstellungspunkt kein Vollrollback |
| Paketqueue | Serialisiert; Abbruch/Journalfehler beenden Folgearbeit; pro Paket 180 Minuten, Listenabfrage drei Minuten | Gewöhnliche Paketfehler können Folgepakete zulassen; Rest wird markiert; keine Transaktion |
| Freie Konsole | Gewählte Minuten, eigener Shellbaum | Sensible freie Befehle nicht durch neuen technischen Logger protokolliert; Änderungen bleiben möglich |
| Exakte Windows-Updates | Shared-Ausführung 30 Minuten; frische Auswahl, GUID/Revision/EULA/Zustimmung und verbrauchter Plan bleiben Pflicht | Teilinstallation/Neustart werkzeugspezifisch; kein Rollbackversprechen |
| WindowsProbe / Topologie | Eigene PowerShellprozesse, 45 / 60 Sekunden | Probe wartet jetzt Stream-/Prozessende auch auf Fehlerpfaden; Topologie-Fallbacks bleiben erkennbar |
| Ping / LAN | Ping zwei Sekunden; LAN 700 ms je Probe, höchstens 16 parallel / 254 Hosts | Abbruch an bestehender Probe-/Schleifengrenze; kein Scan für diese Abnahme ausgeführt |
| Speedtest | Explizite Zustimmung; 120 Sekunden Gesamtfrist | Bereits gesendete Daten nicht rückholbar; kein Test ausgeführt |
| smartctl / PresentMon | Eigener Werkzeugprozess; smartctl 90 Sekunden, PresentMon begrenztes Messfenster | Geräte-Selbsttest kann CLI überleben; vorhandene Logs/Bitmasken bleiben, kein Test gestartet |
| Npcap | Nichtblockierende 20-ms-Pollschleife, 15 Minuten / 100.000.000 Bytes | Stop nach aktuellem Poll; Datei bleibt, Handle wird geschlossen; kein Mitschnitt ausgeführt |
| Reporting / Server / Client | Bestehende Loop-/Request-/Hosttokens; Reporting-HttpClient zwei Minuten, feste Aufträge über Shared | Kein globaler Timeout erfunden; Opt-ins, Rechte und Bestätigungen unverändert; Tenantaktionen nicht geprüft |

CommandRunner prüft bereits angeforderten Abbruch und ungültigen Timeout
vor Prozessstart. Reader laufen vor möglichem Progressfehler; dessen
Prozess wird im finally beendet und vollständig abgewartet. Caller-Token
bleibt erhalten, Timeout bleibt eine eigene Exception. Hinweise auf
Teilveränderungen und fehlende automatische Rücknahme bleiben sichtbar.
**Stopp und Timeout sind kein Rollback.**

## 8. Lebenszyklen und Ressourcen

Prüfbasis sind die vollständige relevante Quelllektüre aus 0.5 und die
gezielte M1-Nachverfolgung der Besitzer, Start-/Stop- und Fehlerpfade.
Suchtreffer sind keine belegte Fehlerzahl. Statische Kontrolle und
isolierte Tests werden nicht als realer Dauerbetriebstest ausgegeben.

| Ressource / Besitzer | Befund und tatsächliche Maßnahme |
|---|---|
| Prozesse / WindowsIdentity | Board-Pipename und Explorer-Prozesswrapper jetzt deterministisch freigegeben |
| CommandRunner-Tasks / stdout / stderr | Initialer Progressfehler war außerhalb des Cleanupbereichs; korrigiert, eigener Baum und Reader vor Freigabe abgewartet |
| WindowsProbe-Streams | Reader jetzt ohne vorzeitiges Caller-Cancel, nach Kill/Exit beide Tasks auch im finally abgewartet; Vorab-Cancellation verhindert Start |
| CTS / Prozess-Events | using/finally in Runnern bleibt; keine neuen globalen Process-Events oder Fire-and-forget-Reader |
| Worker-Tasks / Semaphore | Diagnose und Reporting nach Loopende entsorgt; Wake-Konfiguration unter Lock, beide MainWindow-Schließpfade warten Besitzer ab |
| Sensorsemaphore / Hardwareprovider | Beide Schließpfade warten exklusiven Sensorzugriff und Providerfreigabe ab, anschließend Semaphore entsorgt; keine aktive Sensorfunktion abgeschaltet |
| Timer / DispatcherTimer | DisplayPulse stoppt und entfernt Tick; Desktop-/Eventbrowser-/Sidebar-Lebenszyklen behalten Stop/Cancel; kein pauschales Stoppen bei minimiertem Hauptfenster |
| Eventhandler / Adorner / AnimationClocks | FrameGlow/Branding behalten Unload-/Sichtbarkeits-/Systemanimationsregeln und entfernen globale Handler/Animationen; kein neues Neon-Design |
| Named Pipes | Bestehende using-/Loop-Cancellation und Besitzgrenzen beibehalten; Wireformat unverändert, realer Host-/Schonerwechsel noch nicht abgenommen |
| Sockets / HttpClient | Vorhandene endliche Requests, using/factory bzw. langlebige Clients nicht pro Tick entsorgt; keine Socket-/HTTP-Massenneuanlage eingeführt |
| FileSystemWatcher / PerformanceCounter | Keine entsprechenden verwalteten Besitzer im eigenen C#-Code gefunden; Windows-Counterabfragen laufen in bestehenden subprocessbasierten Pfaden |
| WMI / Audio / Display | Bounded PowerShell-/Interop-Abfragen bleiben; kein neuer langlebiger WMI-Searcher oder Topologiecache |
| PresentMon / smartctl | Existierende Werkzeugbesitzer, Messgrenzen und Auswertung erhalten; Gerätewirkung nicht mit Wrapperlebensdauer gleichgesetzt |
| Npcap / Dateien / native Handles | Bestehende finally-/Dispose-Pfade für Geräteliste, Capture und DLL sowie Dateigrenzen erhalten; keine Live-Capture-Abnahme |
| Graph / Server | Zertifikatsprovider besitzt Dispose, bestehende HTTP-/Hostlebensdauer erhalten; keine neuen Zugangsdaten oder Tenantzugriffe |

xBar, Gadgets, Alarmierung, BoardFeed, Tray, Aufzeichnung und Server/Client
wurden nicht für vermeintliche Idle-Gewinne abgeschaltet. Keine neue
Telemetrie-Sichtbarkeitsregel. Realer Langzeit-/Resume-/Hardwarebetrieb
bleibt separat zu messen.

## 9. Performance-Baseline

[PERFORMANCE-BASELINE](PERFORMANCE-BASELINE.md) enthält Methode,
Reproduktionsbefehl, Umgebung, aktuelle Rohdatenauswertung und vollständige
Produktmessmatrix. Der optionale Testmodus erzeugt ausschließlich ein
synthetisches Fenster; lokale Adapterenumeration ohne Adress-/ID-Protokoll.

Frisch gemessen: leeres Testfenster 2,60 Prozent Prozess-CPU bezogen auf
einen Kern, bestehender FrameGlow 22,42 Prozent, synthetisches Board
sichtbar 144,32 Prozent, Collapsed 0,49 Prozent. Auf 24 logische
Prozessoren normalisiert: 0,108 / 0,934 / 6,013 / 0,020 Prozent.
Board-Kompositionsintervalle p95 24,98 ms; **keine GPU-FPS**.
Lokale Adapter-API: erster Aufruf 18,144 ms, Warmmedian 11,5983 ms bei
25 Aufrufen. Keine neue Performanceverbesserung daraus behauptet.

Produktkalt-/Warmstart, Bedienbarkeit, erste Hardwaredaten, Idle, GPU,
Charts, echte Board-/Schonerhosts, Minimierung/Tray, xBar/Gadgets,
Multi-Monitor/DPI, Offline/Providerfehler, Resume und Langzeitwerte sind
**NICHT GEMESSEN**. CPU/Speicher/Threads/Handles des Testhosts ersetzen
diese Produktwerte nicht. Keine historischen Werte als neue Baseline.

## 10. Untersuchte doppelte Abfragen

Bewertet wurden Netzadapter/Zähler in NativeMetrics und DashboardMetrics,
Adapterinventar/Gateway, WindowsProbe/Diagnose/Clientinventar, Hardware-,
Audio-/Displaytopologie, Geräteidentitäten und Monitorinformationen.
Die [Cachematrix](PERFORMANCE-BASELINE.md#doppelte-abfragen-und-cacheentscheidung)
dokumentiert Quelle, Verbraucher, Frequenz, Änderungsrate, Kostenbeleg
und nötige Invalidierung. Nur der lokale Adapter-API-Teil ist frisch
gemessen; vollständige Abfragekosten ansonsten NICHT GEMESSEN.

Der vorhandene BoardFeed-Hardwarecache von ungefähr einer Minute hat im
Legacyframe keinen eigenen Hardwarezeitstempel; Framefrische ist nicht
gleich Topologiefrische. Keine neue PnP-/Resume-Invalidierung zugesichert.
Ein geteilter Snapshot benötigt eigene Erfassungszeitpunkte, explizite
Fehler/Stale-Semantik und Neuaufbau der Zählerbaselines nach Wechseln.

## 11. Tatsächlich durchgeführte Optimierungen

**Keine Abfragen zusammengelegt und keine neuen Caches eingeführt.**
Der einzelne API-Kostenwert rechtfertigt keine Vermischung unterschiedlicher
Zeitbasen, Zustimmungen und Fehlerverträge. Keine Abfragequelle oder
Pollingfrequenz geändert. Die belegten Verbesserungen betreffen
Lebensdauer, explizite Ausgabekürzung und nachvollziehbare Fehler,
nicht behauptete Laufzeitbeschleunigung. Gemeinsame Ressourcen und
eingefrorene UI-Objekte aus dem Ausgangsstand bleiben erhalten.

## 12. Datenschutzklassifikation

Neue Grundlage: Normal / Sensitive / Secret, Feldklassifikation unabhängig
von Groß-/Kleinschreibung. Unbekannte Feldnamen gelten als Sensitive,
leere Namen werden abgewiesen. Die explizite Normal-Allowlist enthält
technische Metafelder wie Unit, SourceType, Timestamp, Availability,
RequiresElevation, IsEstimated, IsCalculated, OperationState und ExitCode.

| Geprüfte Datenklasse | Behandlung in der neuen Grundlage |
|---|---|
| Seriennummer, MAC, Monitor-/PnP-/Audio-ID, Gerätekennung | Sensitive |
| Computer-/Benutzername, Benutzerpfad, IPv4/IPv6, Dumpinformationen | Sensitive, nicht aus vermeintlich anonymen Kennungen Normal ableiten |
| Password/PasswordHash, Token/DeviceToken/AccessToken/RefreshToken/ClaimToken/CsrfToken/ProtectedToken | Secret |
| ClientSecret, RecoveryKey/RecoveryPassword, PrivateKey/CertificatePrivateKey | Secret |

`CanShare` erlaubt Normal, Sensitive nur mit expliziter Zustimmung,
Secret auch mit dieser Zustimmung niemals. Ungültige Klassen bleiben
gesperrt. Technische Journalziele werden ohne Sensitive-Zustimmung
redigiert; dort keine zusätzlichen Hardwareidentitäten oder Pfade.

Das ist eine feldbezogene Grundlage, **kein Scanner beliebiger Texte,
keine automatische Nachrüstung aller Export-/Reportingpfade und keine
pauschale Geheimnisfreiheit alter Protokolle**. Bestehende funktionale
Gerätedetails bleiben sichtbar. Diagnostikexport hat weiterhin Vorschau,
Zustimmung und bestehende Heuristik; vollständige IPv6-Redaktion ist
damit nicht belegt. Alte Pfade/Formate müssen einzeln migriert werden.
Keine Secrets, produktiven Kontendaten oder realen Dumps für Tests verwendet.

## 13. RC1-Sprachstrategie

[RC-LANGUAGE-STRATEGY](RC-LANGUAGE-STRATEGY.md) beschreibt den
sprachabhängigen Releasevertrag: explizite erweiterbare `releaseLanguages`,
sprachbezogener Status released/experimental, Prüf-/Reviewnachweise,
Parität und Formatvalidierung für alle Ressourcen sowie vollständige
Freigabe der tatsächlich erklärten Releasesprachen.

RC1-DE ist das Ziel, nicht bereits abgenommen. Englisch bleibt vorhanden,
**NICHT VOLLSTÄNDIG FREIGEGEBEN** und nicht auswählbar. Das heutige
Englischgate bleibt unverändert aktiv, auch wenn es RC1-DE noch blockiert.
Umstellung erst atomar mit vollständig getestetem sprachbewusstem
Nachfolger; Negativtests und spätere eigene Englischfreigabe sind geplant.
Keine CI-Änderung, kein Gate-Bypass, keine Sprach-/RESX-Entfernung.

## 14. Testergebnisse

Die [Testanleitung](TESTING.md) enthält Runner und Nebenwirkungen.
Nach logischen Codepaketen wurden Releasebuild und gezielte STA-Prüfungen
ausgeführt; nach den letzten Ressourcenänderungen nochmals Gesamtbuild,
beide Grundrunner und synthetischer Desktop-Smoke.

| Prüfung | Tatsächliches Ergebnis |
|---|---|
| Finaler Release-Gesamtbuild, `--no-restore` | Bestanden, 0 Warnungen / 0 Fehler |
| Finaler STA-Grundrunner | 4.250 Assertions bestanden, gegenüber Ausgangsstand 4.128: 122 zusätzliche |
| Finaler Verwaltungsrunner | 384 Assertions bestanden; 162 HTTPS-, 150 Konten-, 14 Flottenprüfungen sind Teilmengen, nicht zusätzlich zu addieren |
| Ressourcen-/Sprachkompatibilität | 3.277 Assertions bestanden |
| Finaler synthetischer deutscher Modern-/Neon-Smoke | 3.611 Assertions bestanden; 22 Hell-/Dunkel-Testbilder, kein Realbetrieb |
| Python-Lokalisierungstests | Drei Tests bestanden |
| Optionaler Konsolidierungsmessmodus | Beendet, lokale Rohdatei erzeugt; kein Produktbenchmark |
| Dokumentlinks / Diff | 63 relative Dateiziele geprüft, keine neuen fehlenden Ziele; zehn bereits in der Baseline enthaltene historische Paketlinks der Übergabe fehlen weiterhin; `git diff --check` bestanden |

Neue Tests prüfen Quellen-/Stale-/Nullsemantik, Herkunft und Confidence,
Datenschutzklassen/Feldaliasse, technische Journalform und Größenlimits,
exakt 2.000.000 / 2.000.001 Zeichen jeweils stdout **und** stderr,
Caller-Abbruch, Timeout, Pre-Cancel, ungültigen Timeout, Progressfehler
mit nachgewiesen beendetem eigenem Fixtureprozess und Worker-Dispose.
Keine bestehenden Assertions gelöscht oder geschwächt.

Keine Reparatur, Windows-Verwaltungsaktion, Updateinstallation, Erhebung
produktiver Hardware/Sensoren, Treiberänderung, PCAP-Aufnahme, Speedtest,
Netzscan, Tenantaktion, Installerprüfung oder Veröffentlichung ausgeführt.
Kein Paket installiert oder neue Abhängigkeit benötigt.

## 15. Bekannte intermittierende Ausgangsabweichung

**BEOBACHTETE INTERMITTIERENDE AUSGANGSABWEICHUNG – URSACHE NICHT BELEGT**

Vor technischen M1-Änderungen scheiterte der erste Verwaltungsrunner nach
162 HTTPS-Assertions beim Austausch der synthetischen Kontendatei:

```text
System.UnauthorizedAccessException: Access to the path is denied.
System.IO.FileSystem.MoveFile(..., Boolean overwrite)
LocalAccountService.SaveAsync(...):244
LocalAccountService.LoginAsync(...):102
VerifyAccountsAsync(...):54
```

Betroffen: zufälliger eigener Ordner unter `tests\.manager-test-runs`,
keine produktiven Daten. Für den ursprünglichen Fehler wurden weder
exakter Zielpfad/HRESULT noch Handlehalter protokolliert. Keine Ursache
aus dem späteren Erfolg ableiten.

Die ausdrücklich freigegebene begrenzte Diagnose ergab:

1. Schreibstream wird vor atomarem Move abgewartet/geschlossen;
   Zustand erst nach erfolgreichem Austausch übernommen, Semaphore
   schützt interne Zugriffe. Kein eigener offener Leser belegt.
2. Unveränderter direkter Folgelauf und zwei weitere unveränderte
   Diagnoseläufe bestanden jeweils 384 Assertions.
3. Zwei isolierte Serien mit je 500 Austauschen ohne gehaltenen Reader
   bestanden; PowerShell-.NET-Probe, kein weiterer Produktkontentest.
4. Bewusst gehaltene Leser reproduzierten in allen vier getesteten
   FileShare-Kombinationen UnauthorizedAccessException / `0x80070005`.
   Ziel blieb unverändert, Dateiattribut Archive, nicht ReadOnly.
5. Im begrenzten Zeitfenster ab 21:50 keine passenden Defender-Ereignisse
   1123/1124 zum Fixturepfad. Kein allgemeiner AV-Ausschluss.

Ein Handle kann die Exceptionklasse auslösen; Sperrhalter und Ursache
des ursprünglichen Laufs bleiben unbelegt. **Kein Virenscanner als
Ursache behauptet.** Keine ACL-/Schutzänderung, versteckten Retries oder
systemweite Daueraufzeichnung. Die spätere ausdrückliche Fortsetzungsfreigabe
ersetzte den vorsorglichen Stopp. Kein erneutes Auftreten in den danach
ausgeführten M1-Verwaltungsläufen.

Der Runner gibt künftig beim echten Fehler Zeitpunkt, Phase,
synthetischen Fixturepfad und HRESULT aus und endet weiterhin fehlerhaft.
Bei Wiederkehr begrenzte Handleprüfung nur bei vertretbarem Aufwand,
danach technische Blockerbewertung; keine Wiederholung bis Grün.

## 16. Neue Regressionen

In den ausgeführten Prüfungen **keine neuen Regressionen beobachtet**.
Das ist keine Aussage über ungetestete Hardware-, Installer-, Tenant-,
Resume- oder Langzeitpfade. Die Ausgangsabweichung wird weder als neue
M1-Regression noch als gelöst umetikettiert. Keine neue ungeklärte
Testabweichung, beschädigte Schutzabfrage oder Datenverlust beobachtet.

## 17. Offene technische Schulden

- Schrittweise Integration der Metadaten in vorhandene Verbraucher;
  getrennte Quellen-/Hardwarezeitpunkte und Stale-Verhalten pro Legacy-Pfad.
- Klassifizierte Migration alter `details`, Exporte, Reports und Live.log;
  allgemeine Live.log-Größen-/Aufbewahrungsregeln, vollständige IPv6-Prüfung.
- Ursache der dokumentierten einmaligen Kontendatei-Abweichung.
- Produktmessmatrix einschließlich GPU, Resume, Multi-Monitor und
  Langzeit-Ressourcenverlauf; keine Grenzwerte ohne reale Messbasis.
- Nachweis echter Prozess-/Dienst-/Remote-/Gerätefolgen bei Abbruch;
  Prozessende bleibt kein universelles Ende externer Arbeit.
- Sprachbezogener Auditvertrag/Validator, atomarer CI-Wechsel und
  reale vollständige deutsche Abnahme; spätere Englischfreigabe separat.
- Reale Hardware-/Sensor-/Capture-/Schoner-/Installations-/Tenantabnahme
  und weitere RC-Risiken aus dem [Readiness-Audit](RC-READINESS-AUDIT.md).
- Zehn alte Paketverweise der Übergabe zeigen auf nicht vorhandene
  historische Ausgaben; keine Ersatzpakete erzeugt oder Geschichte verändert.

Das sind benannte Folgearbeiten, keine stillen Erfolgsersatzwerte oder
entfernten Funktionen. Dieser Meilenstein erklärt sie nicht für erledigt.

## 18. Auswirkungen auf Meilenstein 2

Quellen-/Ergebnisverträge, sichere technische Metadaten und reproduzierbarer
Teilmessmodus sind als Ausgangspunkt vorhanden. Vor weiteren Optimierungen
Produktmessmatrix/Budgets und Invalidierungsvertrag festlegen. Vollständige
Verbrauchermigration nur kompatibel und mit Zustands-/Fehlernachweisen.
Keine Meilenstein-2-Umsetzung oder neue Funktionsplanung begonnen.

## 19. Auswirkungen auf Meilenstein 3

Die Sprachstrategie verhindert eine dauerhafte Deutsch-Sackgasse;
Release-/Hardware-/Installerabnahme bleibt separat. Kein neuer Renderer,
FrameGlow-Ersatz, UI-Umbau oder Featureabbau. Retro-Neon, umlaufende Linien,
Animationen und Anzeigen bleiben erhalten. Meilenstein 3 nicht begonnen.

## 20. Abnahmestatus und Stopp

**MEILENSTEIN 1 ABNAHMEBEREIT.**

Begründung: alle neun freigegebenen Themen sind durch kompatible Grundlage,
belegte Lebenszyklus-/Fehlerkorrekturen, ausführbare Tests, frische
Teilmessungen, begründete Nicht-Caching-Entscheidung und die verlangte
Sprachstrategie bearbeitet. Die erlaubten nicht gemessenen Produktfälle
und noch nicht migrierten Legacyflächen sind ausdrücklich ausgewiesen.
Build/Runner bestehen; die bekannte Ausgangsabweichung bleibt ursächlich
offen und wurde entsprechend der Fortsetzungsfreigabe nicht verdeckt.

Das Urteil betrifft die technische Grundlage, **nicht RC-Reife, vollständige
Datenschutzmigration oder reale Produktperformance**. Abnahme durch den
Auftraggeber steht aus. Keine Versionsanhebung, neue Installer, Pushes,
Tags, Releases oder Merges. **Hier Stopp; Meilenstein 2 und 3 benötigen
einen neuen Auftrag.**
