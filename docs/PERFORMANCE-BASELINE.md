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

## Meilenstein 2: Neonrahmenvergleich

### Methode und Trennung von der Produktmessung

Der M1-Wert des alten FrameGlow bleibt unverändert: 22,42 Prozent eines
Kerns, 0,934 Prozent normalisiert, 133,73 MiB Working Set des ganzen
Testhosts, 18 Threads und 640 Handles. Er ist **kein** direkt abziehbarer
Vorherwert für später gestartete Prozesse.

Für M2 werden alter und neuer Rahmen aus derselben Assembly im selben
STA-Testhost gemessen: 1280 × 720 DIP, tatsächliche DPI 150 Prozent,
identischer Border mit Margin 24, schwarzem Hintergrund und Cyan-Kontur,
eine Sekunde Einlauf und drei Sekunden angefordertes Messfenster je Phase.
Je drei Runden; Runde zwei tauscht die Alt-/Neu-Reihenfolge.
Vor jeder Phase wird die exklusive Aktivierung ihres Rahmenbesitzers
geprüft. Neu wird statisch, atmend und bei verborgenem Fenster gemessen.
Kein Cockpit-/Sensorstart.

```powershell
dotnet run --project .\tests\SystemCockpit.Tests -c Release --no-build -- --neon-frame-profile C:\Temp\aether-frame-01.json
```

Ziel muss absolut und neu sein. `AETHER_MEASUREMENT_COMMIT` kann den
Quellstand in der Rohdatei benennen. Testcode:
[NeonFrameProof](../tests/SystemCockpit.Tests/NeonFrameProof.cs).
Der Modus endet und entfernt beide Rahmen sowie das Fenster.

Umgebung des finalen Laufs vom 9. November 2026, 22:48 Uhr:
.NET 10.0.14, Windows 26300, 24 logische Prozessoren, WPF-Tier 2,
Windows-Animationen an, hoher Kontrast aus, Netzbetrieb `Online`.
Energieplan und konkurrierende Hostlast wurden nicht verändert oder
vollständig erfasst. Der tatsächliche Dispatcher-Timerabschluss streut;
final 2999,84–4233,12 ms. Alle CPU-Prozente verwenden die jeweils
**tatsächlich verstrichene** Zeit, nicht pauschal drei Sekunden.

### Mehrere gültige Vergleichsserien

CPU eines Kerns, Prozent; je Median aus drei Runden, Klammer Min–Max.
Serien werden wegen wechselnder Last und gemeinsamem JIT-/Cachezustand
nicht zu einem scheinbar präziseren Gesamtwert zusammengelegt.

| Serie / Quellstand | Alt laufend | Neu statisch | Neu atmend |
|---|---:|---:|---:|
| Erster unintegrierter Proof `d558463` | 31,2493 (30,2123–38,0176) | 1,7620 (0,4801–2,1602) | 2,6057 (2,6018–3,1122) |
| Abgesicherter Proof `0caeedc` | 22,4267 (18,7500–28,6441) | 1,0450 (0–1,4619) | 2,5986 (1,5633–3,5994) |
| Cockpit-Migrationsstand vor `d1eaadf` | 13,0228 (13,0200–19,2699) | 0,9561 (0,5178–1,3589) | 1,5510 (1,5264–3,1241) |
| Sidebar-/Eingabestand vor `ea5ed52` | 20,3415 (18,7511–29,1596) | 1,9367 (1,5512–2,6037) | 2,0805 (1,5571–2,6104) |
| Schonerstand vor `cfa096c` | 33,8675 (30,7269–35,9385) | 1,5326 (0,9980–2,9589) | 1,5550 (1,5511–2,0746) |
| Finaler Rendercode `2066ba9` | 15,1050 (13,5389–34,3738) | 2,8870 (0,4558–3,4817) | 2,0797 (1,0069–4,1484) |

Der neue Rahmen war in jeder gültigen Serie im Median günstiger als
der alte. Keine allgemeine Produktbeschleunigung daraus ableiten.
Ein gemessener Null-CPU-Delta-Wert bedeutet nur keine zusätzliche
Prozess-CPU-Zeit innerhalb der Zählerauflösung dieses Fensters.

Die Zwischenserie `m2-lifecycle-comparison.json` ist **kein gültiger
Alt-/Neu-Nachweis**: Ihr erster Stressmodus ließ den neuen Rahmen für
die nächste Runde aktiv. Das fiel bei der Rohdatenauswertung vor der
ersten Migration auf. `0caeedc` entfernt ihn ausdrücklich und ergänzt
die Exklusivitätsprüfung. Die Datei bleibt als Diagnosebeleg erhalten,
wird aber weder in diese Tabelle noch in eine Verbesserungszahl gemischt.

### Finale Phase: Median und Streuung

| Phase | CPU eines Kerns Median / Min–Max % | Normalisierter Median % | Working Set Min–Max MiB | Threads | Handles |
|---|---:|---:|---:|---:|---:|
| Idle | 2,0821 / 0,9422–5,1923 | 0,0868 | 104,15–146,10 | 13–18 | 621–626 |
| Alter FrameGlow | 15,1050 / 13,5389–34,3738 | 0,6294 | 125,84–146,71 | 12–18 | 618–631 |
| Neuer Rahmen statisch | 2,8870 / 0,4558–3,4817 | 0,1203 | 127,23–146,74 | 13–18 | 621–631 |
| Neuer Rahmen atmend | 2,0797 / 1,0069–4,1484 | 0,0867 | 127,11–145,00 | 13–18 | 621–631 |
| Neuer Rahmen verborgen | 1,8456 / 0,8608–2,0278 | 0,0769 | 127,72–145,04 | 12–18 | 618–631 |

Working Set gehört zum gesamten Prozess; Reihenfolge, GC und Caches
erklären mit, warum statisch nicht in jeder Serie unter atmend liegt.
Keine exklusive Control-Speicherzahl aus den Differenzen berechnen.
Der verborgene Rahmen hat nach Lifecycleassertions keinen Atmungsclock;
die verbleibende Testhost-CPU enthält das Messabonnement.

### Finale Rohwerte

Verstrichene Zeit und Prozess-CPU in ms, Working Set/Private Bytes in
Bytes. L = LayoutUpdated-Ereignisse im Messfenster. Sie sind ein
Indikator für Layoutarbeit, keine exakte Zählung interner Measureaufrufe.

| Runde / Phase | Dauer | CPU | Working Set | Private Bytes | Threads / Handles | L |
|---|---:|---:|---:|---:|---:|---:|
| 1 idle | 3009,2607 | 156,25 | 109211648 | 88735744 | 18 / 626 | 0 |
| 1 alt | 3000,1048 | 1031,25 | 131952640 | 213905408 | 18 / 631 | 720 |
| 1 statisch | 3247,3438 | 93,75 | 133414912 | 202309632 | 18 / 631 | 0 |
| 1 atmend | 3005,1997 | 62,5 | 133279744 | 232452096 | 18 / 631 | 0 |
| 1 verborgen | 3082,2065 | 62,5 | 133922816 | 233058304 | 18 / 631 | 0 |
| 2 idle | 3316,6806 | 31,25 | 151990272 | 216416256 | 16 / 623 | 0 |
| 2 statisch | 4038,9668 | 140,625 | 152023040 | 216416256 | 16 / 623 | 0 |
| 2 atmend | 3103,5469 | 31,25 | 152043520 | 231555072 | 17 / 626 | 0 |
| 2 verborgen | 3630,3931 | 31,25 | 152088576 | 231575552 | 17 / 626 | 0 |
| 2 alt | 2999,8434 | 453,125 | 152788992 | 227332096 | 17 / 626 | 720 |
| 3 idle | 3001,8052 | 62,5 | 153198592 | 220479488 | 13 / 621 | 0 |
| 3 alt | 3000,6187 | 406,25 | 153833472 | 230084608 | 12 / 618 | 720 |
| 3 statisch | 3427,8202 | 15,625 | 153870336 | 246206464 | 13 / 621 | 0 |
| 3 atmend | 3013,1809 | 125 | 140693504 | 199913472 | 13 / 621 | 0 |
| 3 verborgen | 4233,1155 | 78,125 | 139345920 | 190218240 | 12 / 618 | 0 |

WPF-Kompositionsintervalle des alten Rahmens: Median 4,1667 ms,
p95 4,1668–4,1669 ms. Neu statisch/atmend: Median 16,7364 ms,
p95 16,7364–25,1046 ms. Das Rendering-Abonnement kann selbst
Komposition anfordern. **Keine GPU-FPS, kein globales UI-Framelimit.**

### Ressourcen über 300 Reloadzyklen

Je Runde folgen 100 tatsächliche Unload/Reload-Zyklen mit eindeutiger
Adornerzählung; keine erzwungene GC im Messmodus.

| Vollendete Zyklen | Working Set Bytes | Private Bytes | Threads / Handles |
|---:|---:|---:|---:|
| 0 | 134070272 | 233218048 | 18 / 631 |
| 20 | 139071488 | 205647872 | 18 / 631 |
| 60 | 151932928 | 216461312 | 17 / 626 |
| 100 | 152047616 | 216498176 | 17 / 626 |
| 120 | 152514560 | 219820032 | 13 / 621 |
| 160 | 152514560 | 219820032 | 13 / 621 |
| 200, Ende Runde 2 | 153190400 | 220483584 | 13 / 621 |
| 200, Beginn Runde 3 nach Vergleichsphasen | 139431936 | 206147584 | 13 / 621 |
| 240 | 139431936 | 206147584 | 13 / 621 |
| 280 | 140398592 | 207323136 | 13 / 621 |
| 300 | 140406784 | 207331328 | 13 / 621 |

Initialer Aufbau, Plateaus und spätere Speicherfreigabe sind sichtbar.
Kein ungebremstes Ressourcenwachstum in diesem begrenzten Lauf beobachtet.
Das ist **kein Langzeit-Leakfreiheitsbeweis**. Zusätzlich prüft der
Grundrunner per WeakReference, dass entfernte Adorner sammelbar sind.

### Lokale Rohdateien und Integrität

Die JSON-Dateien liegen im persistenten Sessionverzeichnis
`%USERPROFILE%\.copilot\session-state\64a11d20-8d22-4d0e-9eca-a7e030f0d975\files`.
Sie enthalten die ungerundeten Einzelwerte, Umgebungsangaben und
Quellstand, keine Adapteridentitäten oder produktiven Hardwaredaten.

| Datei | SHA256 |
|---|---|
| `m2-proof-comparison.json` | `9BDB3F9E7002D490A30FE3FF84CBB3C3F745DCECD8A507EAB82FBDDD28E3D5A1` |
| `m2-gated-proof-comparison.json` | `EB0EB657321064C5E1D41512AB22D1AEDD7D51F5E3320A36119E5B2B2D48A2D0` |
| `m2-cockpit-comparison.json` | `6345AB3CDF1FAE6B6D9AFCD9D66690572CFFA5D85421FF0BD53AA7E1DEB6C003` |
| `m2-sidebar-comparison.json` | `CDEF164CA8B64B5889673ECB274F7B9665D71BA1E17CD3D21384F053EC8C34FF` |
| `m2-saver-comparison.json` | `FB30E564387EB3896530636901F823D841574616386DBB08A5AE935D06B0E85A` |
| `m2-final-comparison.json` | `8B543555FF3DCA699B1E05E65CB2337CA6D4E84C93511F4EFEA858CD6E7E77C4` |

Die produktbezogene NICHT-GEMESSEN-Matrix oben bleibt vollständig gültig.
Es wurden keine Systemtimerauflösung, Energieeinstellung, Treiber,
Animationseinstellung oder Hintergrunddienste für bessere Zahlen verändert.

### Gezielte .NET-Musterprüfung der neuen Renderkomponente

Ausgeführte Rezepte in AetherNeonFrame, vor Befundklassifikation:

| Rezept | Treffer |
|---|---:|
| IndexOf mit Literal / Substring / StartsWith-EndsWith-Contains | 0 / 0 / 0 |
| ToLower-ToUpper / Replace / params | 0 / 0 / 0 |
| Select-Where-OrderBy-GroupBy / All-Any | 0 / 1 |
| neue Dictionary-List / static readonly Dictionary | 0 / 0 |
| RegexOptions.Compiled / new Regex / GeneratedRegex | 0 / 0 / 0 |
| unversiegelte / versiegelte Instanzklassen | 0 / 1; eine von einer versiegelt |
| IEquatable / string.Format | 0 / 0 |
| `+=` | 11, ausschließlich Eventabonnements, keine Stringakkumulation |

Der einzelne All-Aufruf validiert CornerRadius bei einer Parameteränderung,
nicht pro Atmungstick. Refresh erzeugt neue Brushes/Effects bei tatsächlichen
Zustands-/Layoutänderungen; keine solche Änderung wurde pro Atmungstick
beobachtet. Keine spekulative Mikrooptimierung aus statischen Treffern.

| Schwere | Bestätigte Performancebefunde |
|---|---:|
| Kritisch | 0 |
| Moderat | 0 |
| Information | 0 |

Diese begrenzte KI-gestützte Musterprüfung kann Fehler übersehen und ersetzt
keine menschliche Prüfung oder reale Produktprofilierung.

## Meilenstein 3: Hardwarezentrale

Neue Messung vom 10. November 2026, funktionaler Stand `5f3d22a`.
`--hardware-proof` misst drei Runden im selben Release-Testprozess.
Je Runde 66 synthetische Komponenten, 100 Snapshotprojektionen und ein
redigierter JSON-Export. Keine Hardwareabfrage, kein erzwungener GC,
keine allgemeine Produkt- oder GPU-Leistungsaussage.

| Runde | Fixtureaufbau ms | 100 Snapshots ms | JSON ms | CPU ms | Working Set B | Private B | Handles | Threads |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 37,3495 | 5,0080 | 9,2617 | 46,875 | 59.289.600 | 19.718.144 | 280 | 8 |
| 2 | 0,7368 | 1,4849 | 1,1019 | 0 | 61.669.376 | 22.179.840 | 280 | 8 |
| 3 | 0,5618 | 1,4401 | 1,0963 | 0 | 64.094.208 | 24.567.808 | 280 | 8 |

Der erste Lauf enthält kalte Initialisierung/JIT. CPU-Nullwerte liegen
innerhalb der Auflösung der Prozesszeitmessung, nicht bei nachgewiesen
kostenloser Verarbeitung. Der kurzfristige Speicheranstieg ist weder ein
Leakbeweis noch ein Nachweis begrenzten Langzeitverbrauchs.
Rohdatei `m3-proof-final\fixture-performance.json`, SHA256
`FC90689012504CD322C154A6C35D9DB6D25D976AC9972AA6969378BC84245FE7`.

Zusätzlich wurde nach ausdrücklicher Freigabe ein rein lesender echter
Durchlauf mit 19 Klassen erfasst. Kein Vergleich mit der bisherigen
Produktinventur: anderer Umfang, daher keine Beschleunigungsbehauptung.

| Klasse | Dauer ms |
|---|---:|
| System | 9.345 |
| CPU | 1.384 |
| GPU | 812 |
| NPU | 1.037 |
| Mainboard | 333 |
| Firmware | 2.684 |
| Chipsatz | 10.110 |
| RAM | 320 |
| Storage | 1.172 |
| PCI | 13.695 |
| Netzwerk | 5.200 |
| WLAN | 319 |
| Bluetooth | 11.351 |
| Audio | 2.104 |
| Display | 346 |
| USB | 15.228 |
| Sicherheit | 5.356 |
| Power | 295 |
| Peripherie | 3.334 |

Gerundete Konsolenausgabe; genaue Einzelwerte in
`m3-readonly-final.json.timings.json`, SHA256
`9C7F4F8E7DA83FBA494B8BA8890481FBC97A23D54112808499950BBC1180F2DB`.
Die produktive Ansicht liest jeweils eine Klasse asynchron, nicht alle
Klassen periodisch. Besonders USB/PCI/Bluetooth benötigen weitere
Latenzarbeit. Reale Idle-/Langzeit-/Resume-/Hotplug- und UI-Latenzabnahme
bleibt offen; Fixtures sind dafür kein Ersatz.
[Vollständiger Prüfbericht](MILESTONE-3-HARDWARE-REPORT.md).
