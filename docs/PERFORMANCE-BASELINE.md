# AETHER — Performance-Baseline für die Konsolidierung

## Geltungsbereich

Neue Messung vom 9. November 2026, 22:10 Uhr Ortszeit, im Release-STA-Testhost.
Kein produktiver Cockpitstart, keine Sensorinitialisierung, keine Installation,
kein Netztest und kein Windows-Verwaltungsauftrag. Die Zahlen unten sind
**synthetische Teilmessungen**, keine Produktfreigabe und kein Vorher/Nachher-
Nachweis einer Beschleunigung. Historische Ergebnisse wurden nicht übernommen.

Umgebung laut Messprogramm: .NET 10.0.14, Windows Build 26300, x64,
24 vom Prozess gemeldete logische Prozessoren, WPF-Render-Tier 2,
Windows-Animationen aktiviert, DPI-Skalierung 150 Prozent.
Die Umgebung wird vom Messmodus erfasst, nicht vom Skript verändert.

## Reproduzieren

Nach dem [Releasebuild](DEVELOPMENT.md#build), auf grafischem Windows-Desktop:

```powershell
dotnet run --project .\tests\SystemCockpit.Tests -c Release --no-build -- --consolidation-baseline C:\Temp\aether-baseline-01.json
```

Der Ausgabeordner muss existieren; die Datei darf noch nicht existieren.
Der Modus erzeugt ein eigenes 1280 × 720-DIP-Testfenster mit dem vorhandenen
FrameGlow und einem synthetischen Board. Keine Produktkonfiguration wird
gespeichert. Jede UI-Phase erhält eine Sekunde Einlauf und etwa drei Sekunden
Messfenster. Events, Timer und Fenster werden danach entfernt.
Der Modus ist eine ausdrücklich aufgerufene Messung, kein zusätzlicher
Grundlauf und kein automatischer CI-Performancegate.

Die Rohdatei des tatsächlichen Laufs liegt lokal im Sessionordner als
`m1-performance-baseline.json`. Für Vergleichsläufe Rohdaten einschließlich
Quellcommit, SDK, Laufzeit, OS, Auflösung, DPI, Render-Tier, Animationsfreigabe,
Energiezustand und konkurrierender Last zusammen aufbewahren. Noch kein
statistischer Langzeit- oder Mehrlaufnachweis.

## Frisch gemessene Teilwerte

CPU „ein Kern“ = Prozess-CPU-Zeit / verstrichene Zeit × 100.
CPU „normalisiert“ teilt zusätzlich durch 24; Werte über 100 Prozent
bei „ein Kern“ sind bei mehreren Prozess-Threads möglich.
Working Set und Threads/Handles sind Momentaufnahmen am Ende der Phase,
keine Peaks und kein isolierter Speicherverbrauch des Controls.

| Synthetische Phase | Dauer ms | CPU ms | CPU ein Kern % | CPU normalisiert % | Working Set MiB | Threads | Handles |
|---|---:|---:|---:|---:|---:|---:|---:|
| Leeres Testfenster | 3008,50 | 78,13 | 2,60 | 0,108 | 119,80 | 18 | 636 |
| Bestehender FrameGlow, sichtbar | 2996,64 | 671,88 | 22,42 | 0,934 | 133,73 | 18 | 640 |
| Synthetisches Board, sichtbar | 3009,80 | 4343,75 | 144,32 | 6,013 | 197,13 | 19 | 648 |
| Dasselbe Board, Collapsed | 3188,39 | 15,63 | 0,49 | 0,020 | 192,62 | 19 | 649 |

| WPF-Kompositionscallbacks | Intervalle | Median ms | p95 ms |
|---|---:|---:|---:|
| Leeres Testfenster | 96 | 31,56 | 41,88 |
| Bestehender FrameGlow | 719 | 4,17 | 4,17 |
| Synthetisches Board sichtbar | 158 | 20,80 | 24,98 |
| Board Collapsed | 191 | 16,74 | 29,29 |

**Keine GPU-Present-FPS:** `CompositionTarget.Rendering` misst WPF-
Kompositionscallbacks im Testhost. Das Messabonnement selbst kann
Komposition anfordern. Insbesondere die Intervalle des leeren/verdeckten
Fensters sind kein Nachweis laufender Produktanimationen. Die Reihenfolge
teilt einen Prozess; JIT, Ressourcen und Caches der vorherigen Phase bleiben
erhalten. Keine isolierten Speichervergleiche aus den Differenzen ableiten.

Zeit bis zum synthetischen Fenster nach `Show` und ApplicationIdle:
268,57 ms. Nicht Kaltstart, Produktstart oder Zeit bis Hardwarezustand.

Lokale Adapterenumeration einschließlich aktiver Nicht-Loopback-
IP-Zähler: 25 Aufrufe, 61 insgesamt enumerierte Adapter. Erster Aufruf
18,144 ms; Median der übrigen 24 Aufrufe 11,5983 ms, p95 13,0025 ms.
Keine Namen, IDs, Adressen oder MACs in der Messdatei. Das misst diese
API-Kombination, nicht einen vollständigen Dashboard-/Inventarlauf.

## Buildzeiten separat

Laut tatsächlicher Buildausgabe: Baseline-Gesamtbuild 27,98 s; danach
inkrementelle Paketbuilds unter anderem 4,37 s, 20,61 s, 13,81 s,
3,82 s, 0,87 s und Messmodusbuild 4,04 s.
Unterschiedlicher Inkremental-/Cachezustand: **kein Buildbenchmark und
kein Nachweis einer Beschleunigung**. Keine Runtime-Kennzahl daraus ableiten.

## Produktmessmatrix

Alle folgenden Produktwerte sind **NICHT GEMESSEN**. Vorgehen ist vorbereitet;
keine Werte durch die synthetischen Teilmessungen oder alte Tests ersetzen.

| Szenario | Reproduzierbare Erfassung für die spätere freigegebene Produktmessung |
|---|---|
| Kaltstart | Frischer freigegebener VM-/Startzustand, bestehende EULA/Profilzustände benennen; Prozessstart bis sichtbare Oberfläche getrennt protokollieren |
| Warmstart | Gleiches Profil, gleicher Energiezustand, wiederholte Starts; Cachezustand nicht als kalt bezeichnen |
| UI bedienbar | Startzeit bis Dispatcher-Idle plus tatsächlich ausgeführte harmlose UI-Interaktion; Bootdialogzeit separat |
| Erster Hardwarezustand | Start bis erster gültiger Sample-/Probezeitpunkt; fehlende Provider getrennt markieren, nicht als Nullwert |
| Idle / sichtbares Cockpit | Nach Einlauf CPU-Zeitdelta, Wallclock, Working Set, Private Bytes, Threads und Handles; aktive Module und Fensterzustand benennen |
| RAM / Threads / Handles | Gleiche Abtastfrequenz und Phase, Beginn/Ende/Peak; weder Cachebereinigung noch erzwungene GC zum Verschönern |
| GPU-Last der UI | Separat geeignete Windows-GPU-Prozessmessung auf freigegebenem Testsystem; nicht aus WPF-Callbacks ableiten |
| Live-Charts | Gleiche Kanalzahl und synthetischer oder realer Datenmodus, Renderfläche und Updatefrequenz dokumentieren |
| HardwareBoard | Gleiche Geometrie, Auswahl, Auflösung und Animationsfreigabe; synthetischen und echten Feed trennen |
| FrameGlow ALT | Gleiche Rahmenzahl und Größen; Original unverändert, Renderzustand und Animation getrennt messen |
| Minimiert / Tray | Fensterzustand und weiterhin benötigte Telemetrie benennen; keine Abschaltung von Alarmen, Gadgets oder BoardFeed erzwingen |
| xBar / Gadgets | Gleiche Widgets, Monitorzuordnung und aktive Datenquellen; Hauptfenster getrennt sichtbar/versteckt |
| Schoner | Eigener Host, Feed/Fallback getrennt; Auflösung je Monitor und Ende bei Eingabe prüfen |
| Multi-Monitor / hohe DPI | Reale Auflösungen und Skalierungen je Monitor; Verschieben und Displaywechsel getrennte Phasen |
| Offline | Freigegebene isolierte Testumgebung; nicht das gemeinsam genutzte Hostnetz umkonfigurieren |
| Fehlender Sensorprovider | Freigegebene Fixture oder Testsystem ohne Provider; keine Treiber/Schutzrichtlinien des Hosts verändern |
| Standby / Resume | Freigegebener Testhost, Standbydauer und erste gültige neue Samples/Topologiezustände protokollieren |
| Langzeitbetrieb | Vorab freigegebenes Zeitfenster; Ressourcenverlauf, Loggrenzen und Shutdown messen, keine unbefristeten Helfer starten |

Messlauf-ID und Quellcommit je Datensatz, monotone Zeitmessung für Dauern,
UTC-Zeitpunkt für Herkunft. Fehlgeschlagene Läufe bleiben fehlgeschlagen.
Ein Messfenster legt keine künftigen Grenzwerte fest; Budgets werden vor
Meilenstein-2-/3-Optimierungen anhand dieser Matrix vereinbart.

## Doppelte Abfragen und Cacheentscheidung

| Kandidat / heutige Quellen | Nutzer und Aufruffrequenz | Änderungsrate / Aktualität | Kostenbeleg und Entscheidung |
|---|---|---|---|
| `GetAllNetworkInterfaces` + IP-Zähler in NativeMetrics und DashboardMetrics | Summenkarte und Adapterdetails, beide im Sampleloop; zwei Sekunden Pause **nach** Arbeitsende | Zähler laufend, Adapter/PnP/Netzwechsel jederzeit; Differenzbildung braucht eigene gültige Baseline | Lokaler API-Teil oben gemessen; kein geteilter Cache. Gemeinsamer unveränderlicher Rohsnapshot wäre möglich, braucht aber synchronisierte Zeitbasis und Ausfallsemantik für beide Verbraucher |
| Adapter in SystemInventory / DefaultGateway | Explizites Inventar und Netzwerkauswahl auf Anforderung | Gateway/Link/MAC ändern sich, Auswahl darf nicht ungeprüft alt sein | Vollständige Inventar-/Gatewaykosten NICHT GEMESSEN; kein Cache |
| WindowsProbe / Diagnose / Shared-Inventar | Boot + wiederholte Probe (60 Sekunden Pause), explizite Diagnose; Reporting/Client getrennte Prozesse und Zustimmungen | OS-Version relativ statisch; Bootzeit, Status, Sicherheit und Fehler nicht statisch | Teilkosten NICHT GEMESSEN. Kein Zusammenlegen vollständiger Berichte oder heimliches Reporting aus fremdem Opt-in |
| HardwareTopology mit AudioTopology/DisplayTopology | Board on demand; BoardFeed bei neuer Verbindung nach Ablauf seines Minutenfensters; Schoner einmaliger Fallback | PnP, Audio-Standardrollen, Treiber und Displaylayout können sofort wechseln | Kosten NICHT GEMESSEN. Kein neuer Cache; bestehender Feedcache separat unten |
| Geräteidentitäten / statische Geräteinfos | Probe, Board, diagnostisches Inventar, verwalteter Client | Austausch/Treiber/PnP, nicht durch Prozesslaufzeit unveränderlich | Kosten NICHT GEMESSEN; IDs sind Sensitive. Kein Lazy-Cache ohne Invalidierung und getrennte Aktualität |
| Monitorinformationen | Board-Lesevorgang, Schonerfensterzuordnung, Desktoppositionierung | Displaywechsel/DPI/Resume jederzeit | Kosten NICHT GEMESSEN; keine Übernahme eines einzelnen Monitor-Snapshots für alle Verbraucher |

Bestehender BoardFeed hält Hardware innerhalb des Serverloops für ungefähr
eine Minute; die Aktualitätsprüfung des Frames betrifft Messzeit, nicht einen
eigenen Hardwarezeitpunkt im Legacyvertrag. Fehler beim Refresh werden
protokolliert und der Frame wird für diese Verbindung nicht als frischer
Refresh ausgegeben. **Keine neue Zusage**, dass alte Topologie innerhalb
dieses Fensters nach PnP/Resume sofort invalidiert wird.

Voraussetzungen für einen späteren gemeinsamen Snapshot: eigener
Erfassungszeitpunkt je Datenklasse, explizite Available/Stale/Fehlerzustände,
Invalidierung durch Resume/PnP/Netzänderung, Neuaufbau der Zählerbaselines,
keine Weitergabe alter Werte als neue Messung und Tests für Refreshfehler.
**Tatsächlich eingeführte Caches: keine.** Die Messung rechtfertigt noch
keinen invasiven Umbau der voneinander abweichenden Abfrageverträge.
