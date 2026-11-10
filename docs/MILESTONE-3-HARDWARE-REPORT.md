# AETHER — Meilenstein 3: Hardware Intelligence

Stand: 10. November 2026.

**ABNAHMEEMPFEHLUNG: MIT DOKUMENTIERTEN GRENZEN ABNAHMEFÄHIG.**

Das ist eine technische Empfehlung, keine bereits erteilte Abnahme des
Auftraggebers. Alle neun Prioritätsgruppen des autonomen Abschlussauftrags
sind bearbeitet. Sichere Lesepfade wurden ergänzt, nicht sicher belegbare
Informationen ausdrücklich begrenzt. Es gibt keine bekannte schwere
funktionale Regression in den ausgeführten Prüfungen.

**Nach diesem Bericht stoppen.** Process Command Center erst nach
ausdrücklicher M3-Abnahme. Kein Push, Merge, Tag, Release, Installer oder
Versionswechsel. Bestehende Beta-45-Pakete bleiben unverändert.

## 1. Ausgang und Abschluss

- Branch: `rc/grosse-entfesselung`.
- Ausgang des autonomen Abschlussauftrags:
  `28835d064a8f7854fbb48e65606874737ae83659`, sauberer Arbeitsbaum.
- Ursprünglicher M3-Ausgang:
  `21b2a0b466c34b526961e83ab234e08430b6750e`.
- Funktionaler Abschluss:
  `428f1d703d8dbf96575c5495b3094dd1b7fd5512`.
- Abschließende Ressourcen-Gegenprobe:
  `69243483c1fdc42409aa3ebfd9f82ca8a7eb7eec`.
- Danach ausschließlich Dokumentationsabschluss dieses Berichts;
  dessen Commit ist mit
  `git log -1 --format=%H -- docs\MILESTONE-3-HARDWARE-REPORT.md`
  eindeutig auflösbar. Ein Dokument kann seinen eigenen Git-Objekthash
  nicht vor seiner Commit-Erstellung enthalten.

### Lokale M3-Commits

| Commit | Inhalt |
|---|---|
| `7e5eff9` | Normalisierte Beobachtungen, Quellen-/Datenschutzmodell, Berichte, Datenbank-/Logo-Verträge |
| `ca60dd6` | CIM-, CPU-, DXGI- und Peripheriegrundlage |
| `37bb74c` | Hardwareansicht, Inspektor, Invalidierung, Sensorübernahme |
| `c72ebe0` | Quellen-/Topologie-/Exporttests, UI-Proof, Read-only-Modus |
| `5f3d22a` | Doppelte Sensoridentitäten, PCI-Adressen und Routenregression |
| `7159742` | Bericht der geprüften Grundlage und damaliger Restumfang |
| `db0ad45` | Echte Sensorstatistik mit Stichprobenintervall |
| `a36915b` | Native Wi-Fi, Adapterraten, Microcode, Direct3D, Audio/Display, SMART/NVMe |
| `28835d0` | Dokumentierte Fortsetzung, Ausgang dieses Abschlussauftrags |
| `4d0c483` | Reale Ansichts-/Lifecyclematrix, Großinventar und Virtualisierung |
| `6da8d37` | Exakte lokale Sensorgeräteprovenienz und PnP-Beziehungen |
| `be2a4ae` | Zurückhaltende NPU-Identität, PDH-Proben, inaktiver SPD-Vertrag |
| `f1c3857` | Direct3D-Nodes, Shader-/Wave-/Mesh- und Videofunktionen |
| `15bd55b` | Lesende TRIM-/Bus-/Wechselmedium- und Controllerintegration |
| `1fc59a3` | Aktive Displaymodi, HDR, EDID und Audioformatunterstützung |
| `cee4969` | Adaptergenaues WDDM und Windows-Hybridflags |
| `f415533` | Reale CIM-/PDH-Cancellation und gestufte Ressourcenmessung |
| `6924348` | Compositor-Nachlauf und Gegenprobe mit leeren WPF-Fenstern |
| `428f1d7` | Abschließende Windows-Diskidentität auch für native TRIM-/Transportwerte prüfen |
| Dokumentationsabschluss | `docs(hardware): finalize autonomous M3 acceptance evidence` |

## 2. Architektur und geänderte Dateien

20 technische Klassen, eigene Komponentenauswahl, kompakte Wertetabelle und
Quelleninspektor. Alte Cockpitanzeigen, Mainboard, Messauswertung und
Softwareinventar bleiben erhalten. Keine Änderung der Verwaltung,
Zustimmungslogik, Versionsquelle oder Feed-Wireformate.

[Architektur](HARDWARE-INTELLIGENCE-ARCHITECTURE.md) und
[Quellenmatrix](HARDWARE-DATA-SOURCES.md) sind die Fachreferenzen.

Änderungen seit `28835d0`, relativ zum Repository:

| Bereich | Dateien |
|---|---|
| Ansicht/Modelle | `src\SystemCockpit\Controls\HardwareIntelligenceView.xaml`, `HardwareIntelligenceView.xaml.cs`; `src\SystemCockpit\Models.cs` |
| Sensoren | `src\SystemCockpit\Services\HardwareSensors.cs`, `SensorSemantics.cs`, `SessionStatistics.cs`, `HardwareSensorCorrelation.cs` |
| Inventar/CPU/SPD | `src\SystemCockpit\Services\HardwareCimReader.cs`, `HardwareCimCatalog.cs`, `HardwareCpuFrequency.cs`, `HardwareSpdBoundary.cs` |
| Grafik | `src\SystemCockpit\Services\HardwareDirect3D.cs`, `HardwareGpuReader.cs`, `HardwareGpuDriver.cs` |
| Storage | `src\SystemCockpit\Services\HardwareStorageProperties.cs`, `HardwareStorageHealth.cs` |
| Display/Audio | `src\SystemCockpit\Services\HardwareDisplayConfig.cs`, `HardwareRuntimeReader.cs`, `AudioTopology.cs` |
| Tests | `tests\SystemCockpit.Tests\HardwareAcceptanceTests.cs`, `HardwareCompletionTests.cs`, `HardwareIntelligenceTests.cs`, `Program.cs` |
| Dokumentation | `README.md`, `CHANGELOG.md`, `PROJEKTSTAND.md`; `docs\FEATURES.md`, `HARDWARE-DATA-SOURCES.md`, `HARDWARE-INTELLIGENCE-ARCHITECTURE.md`, dieser Bericht, `PERFORMANCE-BASELINE.md`, `SECURITY-AND-PRIVACY.md`, `TESTING.md` |

## 3. Bearbeitung der neun Prioritäten

### 1 — Stabilität und reale Ansicht

Neuer expliziter Modus `--hardware-acceptance` öffnet die tatsächliche
Hardwareansicht mit realen lesenden Providern. Er misst die erste
Dispatcherantwort getrennt von fertigen Daten, Klassenwechsel,
Wiederöffnen, instrumentierten Idle und reale CIM-Cancellation.
Danach prüft er 30 synthetische Navigations-/Shutdownzyklen mit
AccessDenied, ProviderError und langsamem abbrechbarem Provider.
5.000 Komponenten werden vollständig gehalten, nur 32 Listenelemente
erzeugt. Recycling-Virtualisierung ist ausdrücklich gesetzt.

Ressourcen werden vor/nach Idle und nach je zehn Zyklen erfasst.
Eine getrennte GC prüft Referenzen, nicht Produktperformance.
Compositor-Nachlauf und 30 leere WPF-Hostfenster dienen als Gegenprobe.
Die beobachteten Anstiege werden nicht als „kein Leak“ weginterpretiert.
Reale Langzeit-/Geräte-/Standby-/Monitorwechsel wurden nicht ausgelöst.

### 2 — Gerätegenaue Sensorverknüpfung

Der bestehende aktivierte Sensorpfad liefert zusätzlich lokale
`IHardware.Identifier`, tatsächliches Parent und, bei GenericGpu,
`DeviceId`. Kein zweiter Computer/Provider wird geöffnet.
Sensoren gehören zu konkreten Providergeräten. Windows-Geräte werden nur
über exakt gleiche, eindeutige vollständige PnP-Kennungen verknüpft;
kein Hersteller-/Modell-/Slot-/Listenindexvergleich.

Providerkennungen sind Direct/ProviderReported und Sensitive.
Der nachvollziehbare PnP-Join ist Derived/ProviderReported mit Zeitpunkt
und Begründung. Unklare Beziehungen bleiben Unavailable und ohne Kante.
Lokale Zusatzkennungen sind mit JsonIgnore vom Feed ausgeschlossen.
Gerätekennungen sind Teil des Statistikschlüssels: Ein neues Gerät
übernimmt nicht den Verlauf eines gleich benannten Vorgängers.
MIN/MAX/Stichprobenmittel, endliche Anzahl, Messintervall und Lücken bleiben
erhalten. Temperaturgrenzen sind weiterhin keine Messungen.

Neue Zuordnungen wurden mit Fixtures geprüft; kein neuer realer
Sensorzugriff wurde für diese Abnahme gestartet. Nicht jeder LHM-Gerätetyp
liefert eine vollständige Windows-PnP-ID; solche Geräte bleiben in ihrer
belegten Providersicht. CPU-Sockel- und Storagezuordnungen werden nicht geraten.

### 3 — NPU

Windows-Klasse, ClassGuid, PnP-/Hardware-/kompatible Kennungen,
Treiberservice, PnP-Fehlercode und Status sind sichtbar.
ComputeAccelerator wird gegen die dokumentierte Klassen-GUID geprüft.
Das bestätigt eine Windows-Geräteklasse, keine unabhängige NPU-Funktion.
Der Bestätigungsstand bleibt ausdrücklich Unconfirmed; Navigation:
„NPU / KLASSENHINWEISE“. Keine TOPS, Telemetrie oder AI-Funktion aus Namen,
DirectML-Verfügbarkeit oder Hersteller abgeleitet.
Der reale Rechner lieferte einen solchen unbestätigten Klasseneintrag.

### 4 — CPU-Takt

Frische Windows-PDH-Query mit englischen Counterpfaden:
`Processor Information(*)\Processor Frequency` und
`% Processor Performance`. Baseline, abbrechbare 300-ms-Wartephase und
zweite Probe; tatsächlicher Zeitabstand und Endzeit sind dokumentiert.
Gruppen-/Prozessorinstanzen und Windows-Aggregate bleiben unverändert.
Gruppen-/Prozessoranzahlen werden vor/nach Sampling geprüft;
Topologieänderungen verwerfen die Probe. Ungültige/fehlende Proben
werden nicht als Null ausgegeben, echte Null bleibt gültig.
Queryhandles werden auch bei Abbruch geschlossen.

**Kein eigener effektiver Hardware-Zyklustakt:** Windows-Frequenz und
Leistungsprozent sind separate Providerangaben. APERF/MPERF und
Residenzzeiten werden ohne eigenen Kernelzugriff nicht gemessen.
Eine Multiplikation mit vermutetem Basis-/Maximaltakt wäre nicht belastbar
und wurde verworfen. Keine pauschale P-/E-Core- oder Sockelzuordnung.
Drei reale PDH-Abbrüche sind im Abschlussnachweis enthalten.

### 5 — SPD/JEDEC/XMP/EXPO

Vorhandene LibreHardwareMonitor-Schnittstellen und Windows-CIM wurden
untersucht: SMBIOS-/PhysicalMemory-Angaben sind kein roher SPD-Dump.
Kein belegter allgemeiner sicherer Benutzerkontextpfad für beliebige DIMMs
integriert. SMBus-/Kernelzugriff oder zusätzliche Treiber werden nicht
aktiviert. Das ist eine begründete Zugriffsgrenze, kein erfundenes Profil.

Ein inaktiver optionaler `IHardwareSpdProvider`-Vertrag ist vorbereitet:
genaue Gerätekennung, begrenzter Rohdatenpuffer, Direct-/Sensitive-Metadaten,
Zeitstempel, Lizenz- und HTTPS-Dokumentationsangabe. Keine automatische
Providererkennung, kein Loader, keine Bibliotheks-/Treiberinstallation.
Vertrag ist getestet, aber kein produktiver SPD-Lesepfad behauptet.

### 6 — GPU

Exakter DXGI-Adapter/LUID statt Name. D3DKMT liefert WDDM und rohe
AdapterType-/Hybridflags. Die speziellen Hybridflags werden nicht in
eine allgemeine iGPU/dGPU-Klassifizierung umgedeutet.

Direct3D liefert getestete Featurelevel und Shader Models, Raytracing,
Wave/Int64, Mesh/Sampler-Tiers, Nodezahl und je Node UMA/CacheCoherentUMA/
isolierte MMU. DXGI liefert Prozessbudgets und lokale/nichtlokale Nutzung
je belegtem Node; ohne Nodequelle bleibt die explizite Node-0-Sicht.
Kein geräteweiter VRAM-Nutzungsnachweis.

ID3D12VideoDevice fragt Decode-Profil-GUIDs und maximale Video-Process-
Inputstreams ab. Grenzen: maximal 64 Nodes und 256 Profil-GUIDs je Node,
ungültige Antwortgrößen werden nicht übernommen.
Keine Command Queue, Render-/Compute-/Decode-/Encodeausführung.
COM, Puffer, Pins und Windows-Adapterhandles werden freigegeben.

Scheduler-Engines sind keine Direct3D-Nodes. Eine Engine-/Vendorintegration,
vollständige Videoformat-/Auflösungs-/Frameratematrix, VBIOS,
Speicherspezifikation und Vulkan/OpenCL/CUDA bleiben außerhalb der
implementierten sicheren Abdeckung. Resizable BAR wird nicht aus UMA,
VRAM-Größe oder Hybridflags abgeleitet. Unbekannte Codes bleiben roh.

### 7 — Storage

Vorhandene eindeutige PhysicalDisk → Disk → Partition → Volume-Beziehungen
und SMART-/NVMe-Identitätsprüfung bleiben erhalten.
Win32_DiskDrive.Index wird eindeutig mit MSFT_Disk.Number verknüpft.
Controller stammen aus expliziten SCSI-/IDE-CIM-Assoziationen;
mehrdeutige oder fehlende Beziehungen werden nicht erzwungen.

Der genaue MSFT_Disk.Path wird ohne Lese-/Schreibzugriffsrechte geöffnet.
IOCTL_STORAGE_GET_DEVICE_NUMBER prüft die Disknummer am geöffneten Handle.
Reine Query-IOCTLs lesen TrimEnabled, Wechselmedium und BusType getrennt.
Eine nicht unterstützte TRIM-Abfrage verliert nicht die übrigen Angaben.
TrimEnabled beweist weder ausgeführte Deallokation noch eine globale
Dateisystemrichtlinie.

smartctl bleibt bei `-x -j -d auto`, dokumentiertem `/dev/pdN`, exaktem
zurückgeliefertem Gerätenamen und anschließender Windows-Number/UniqueId/
Path-Prüfung. Keine Modellnamen-/Seriennummernheuristik, Selbsttests oder
erzwungenen unbekannten USB-Transporte. Geänderte/entfernte/mehrdeutige
Identität verwirft neue Health-/TRIM-/Wechselmedium-/Buswerte.
Die Nachprüfung gilt auch ohne verwertbare smartctl-Werte für verfügbare
native Gerätedetails. Die Inventur ist nicht atomar.
Zählerpräzision und herstellerspezifische ATA-Deutung bleiben unverändert.

Real: drei verfügbare TrimEnabled-Felder; zwei USB-Geräte unterstützen
diese Query nicht. Drei NVMe-Healthpfade liefern Daten; zwei unbekannte
USB-Brücken bleiben explizite smartctl-Fehler.

### 8 — Display

Aktive Quell-/Zielmodi werden nur bei passendem Modustyp, Adapter und ID
übernommen. Desktopfläche/Position, Zielsignal, bevorzugter Windows-Modus,
Refresh, Verbindung, Orientierung, Advanced Color, Zielbits pro Kanal und
Skalierung bleiben getrennt beschriftet.
Info2 liefert separat HDR-Unterstützung, Benutzerfreigabe und aktiven
SDR/WCG/HDR-Modus; alte Windows-/Treiberstände können Unsupported liefern.

WmiGetMonitorRawEEdidV1Block liest bis neun 128-Byte-Blöcke mit gültigem
Basiskopf und Blockprüfsumme. Deklarierte Erweiterungen und gelesene
Anzahl sind sichtbar, Rohdaten Sensitive. Reale Windows-CIM-Metadaten
melden hier Boolean statt des dokumentierten UInt32-Rückgabetyps.
Beide typgeprüften Erfolgsverträge werden getestet und behandelt;
False, Fehlercodes oder kaputte Daten sind kein Erfolg.
Exakte Monitorinstanz statt Modellname verbindet EDID-Sichten.
Physische Panelauflösung/-farbtiefe wird daraus nicht zertifiziert.

### 9 — Audio

GetMixFormat bleibt aktuelle Shared-Mode-Enginesicht. Für genau dieses
Format fragt IsFormatSupported Shared und Exclusive ab. Exakte
Unterstützung, Näherungsformat und echte Nichtunterstützung sind Werte;
AccessDenied, invalidiertes Endpoint und unerwartete HRESULTs sind
gesonderte Fehlerzustände. Näherungsformatpuffer werden freigegeben.
Kein Initialize/Start, Aufnahme, Wiedergabe oder Lautstärkeänderung.

Real: fünf aktive Endpoints mit exakter Shared-Unterstützung, dieselben
fünf lehnen dieses konkrete Format in Exclusive ab. Das ist keine
erschöpfende Geräteliste unterstützter Formate oder Qualitätsbewertung.

## 4. Quellen, Fehler und Datenschutz

Jedes Feld behält SourceType, Confidence, Timestamp, Einheit,
Availability, Fehler, Datenschutz und gegebenenfalls Berechnung.
Direct/ProviderReported ist keine unabhängige physische Bestätigung.
Derived kennzeichnet nachvollziehbare Berechnung/Verknüpfung.
NPU-Funktion bleibt Unconfirmed; kein produktiver Database-Match aktiviert.

Available, Stale, Unavailable, Unsupported, AccessDenied und ProviderError
bleiben unterscheidbar. Alte Werte werden bei Invalidierung/Fehler nicht
als frisch ausgegeben. Kein automatischer UAC-/Treiber-/Transportfallback.
Cancellation erfolgt bei Wartephasen/Kindprozessen und zwischen nativen
Aufrufen; synchrone Treibercalls werden nicht unsicher vorzeitig beendet.

JSON `aether.hardware/1`, CSV und Text enthalten nur gelesene Klassen.
Seriennummern, PnP-/Gerätepfade, EDID-Rohdaten, Controller-/Providerkennungen
und Netzwerkidentitäten sind Sensitive. Berichtsaliase ersetzen interne
Komponenten-/Elternschlüssel. Secret entfällt vollständig; CSV-Formelschutz
und ausdrücklicher Sensitive-Dialog mit Nein als Standard bleiben bestehen.
Keine Rohberichte oder Hardwaredaten hochgeladen.

Finaler realer Bericht: **5.952 Feldzeilen**, davon **835 Sensitive**,
alle redigiert, keine unredigierten Sensitive-Werte und keine Secrets.

| Status beim Export | Feldzeilen |
|---|---:|
| Available | 4.229 |
| Stale | 267 |
| Unavailable | 548 |
| AccessDenied | 8 |
| Unsupported | 897 |
| ProviderError | 3 |

267 dynamische Werte sind im langen sequenziellen Gesamtlauf abgelaufen
und deshalb Stale. Drei Providerfehler: zwei unbekannte USB-smartctl-Brücken,
ein Legacy-NDIS-WLAN-Pfad. Native Wi-Fi liefert separat reale Werte.
Fehlende Berechtigungen wurden nicht verändert.

## 5. Prüfungen und visuelle Nachweise

| Prüfung | Ergebnis |
|---|---|
| Release-Solutionbuild, ohne Restore | 0 Warnungen / 0 Fehler |
| STA-Grundrunner | 4.698 Assertions |
| Interface/Schoner/Branding | 5.051 Assertions einschließlich Grundrunner |
| Deutscher Modern-/Neon-Smoke | 3.611 Assertions, 22 Bilder |
| Hardwareproof | neun Theme-/Root-DPI-Bilder, drei Fixture-Messrunden |
| Reale Ansicht und Lifecyclematrix | bestanden; Messungen unten |
| Reale lesende Inventur | alle 19 Nicht-Sensorklassen durchlaufen; Feldfehler separat sichtbar |
| Neue Abschlussassertions | 57 gegenüber 4.641; insgesamt 313 gegenüber M2 |

Build, Grundrunner, kombinierter UI-Lauf und vollständige reale Inventur
wurden mit Quellstand `428f1d7` erneut erfolgreich ausgeführt.
Ansichts-/Ressourcenmatrix (`6924348`) und unveränderte Themeproofs stammen
aus den Läufen vor dem letzten Storagefix; dieser betrifft allein die
zusätzliche Identitätsprüfung der Storagewerte.

Dokumentationsabschluss: `git diff --check` ohne Befund, keine neuen
fehlenden relativen Linkziele. Zehn bereits fehlende historische Paketziele
bleiben unverändert; historische Pakete wurden nicht angefasst.

Fixtures prüfen insbesondere genaue/mehrdeutige Gerätebeziehungen,
Statistikwechsel, NPU-Confidence, SPD-Vertrag, PDH-Probenzustände,
Shader-/WDDM-Codes, TRIM-Deskriptoren, Controller-Doppelbelegung,
HDR-/Audioantworten sowie tatsächlich ausgeführte isolierte EDID-Zweige.
Vorhandene Assertions wurden weder entfernt noch abgeschwächt.
JSON-/CSV-/Text-Datenschutz- und Exporttests laufen im Grundrunner.
Der native Dateidialog ist nicht durch automatisierte Benutzerklicks abgenommen.

Hardwareproof: Retro-Neon und Modern hell/dunkel, synthetische Root-DPI
100/150/200 Prozent mit ReducedMotion. Keine physischen Monitorwechsel.
Stichproben wurden als Bilder visuell geprüft; das ersetzt keinen
manuellen Gesamtdurchlauf auf allen Geräten.
Unveränderte Verwaltungsrunner wurden nicht als M3-Routineprüfung gestartet.

### Nachweismatrix

| Gegenstand | Nachweisart und Grenze |
|---|---|
| Hardwareansicht öffnen, Klassen wechseln, Wiederöffnen | real mit lesenden Providern in isoliertem WPF-Host; nicht vollständiger produktiver MainWindow-Start |
| Navigation während CIM-Erfassung, PDH-Abbruch | real auf diesem Rechner |
| ProviderError/AccessDenied/langsamer Provider, Shutdown, 5.000 Komponenten | synthetisch, reproduzierbar |
| Entfernte/mehrdeutige Geräte, Invalidierung/in-flight Änderung | synthetische Inventar-/Identitätsfixtures |
| Themes/ReducedMotion/Root-DPI | synthetisch gerendert, ausgewählte Bilder visuell beobachtet |
| Tatsächliches Standby/Resume, physischer Hotplug, Docking, Monitor-/DPI-Wechsel | nicht getestet; kein sicherer reproduzierbarer physischer Ablauf ohne Eingriff ausgelöst |
| Langzeit-/Dauerbetrieb | nicht getestet; begrenzte Ressourcenmatrix ist kein Ersatz |
| Neue Sensorgerätezuordnung | Fixtures; bestehende Sensorinstanz nicht für diese Prüfung neu aktiviert |

## 6. Performance und Ressourcen

[Messmethode und vollständige Tabellen](PERFORMANCE-BASELINE.md#autonomer-m3-abschluss).
Aktueller Nachweis `m3-final-acceptance-05.json`:

- Erste Dispatcherantwort beim Öffnen: 455,8 ms; Systemdaten fertig
  nach 9.768,5 ms. Die Providerlaufzeit bleibt sichtbar, UI arbeitet asynchron.
- Klassenwechsel: Dispatcher 12,0–28,9 ms; fertige Daten 498,4–4.724,2 ms.
- Reale CIM-Cancellation bei Navigation: 52,2 ms.
- Langsamer Fixtureprovider beim Schließen: 45,0 ms.
- Drei reale PDH-Abbrüche: 108,8–124,3 ms.
- 5.000 Komponenten: 91,7 ms bis Dispatcher-/Layoutbereitschaft,
  32 erzeugte Container.
- Dispatcher-Herzschlag mit Soll 25 ms: 1.753 Proben,
  P95 76,5 ms, Maximum 426,5 ms über den gesamten gemischten Ablauf.
- 15.034,4 ms instrumentierter Idle: 343,75 ms Prozess-CPU;
  Handles 796 → 796, Private Bytes 212.303.872 → 213.053.440.
  Herzschlag/Testpump sind enthalten; kein uninstrumentierter Produktidle.

Nach 30 Fixturefenstern: 837 Handles, Private Bytes 319.610.880.
Nach getrennter GC und Compositor-Nachlauf: 837 Handles,
Private Bytes 245.149.696. 29 von 30 Ansichten waren sammelbar;
eine Referenz blieb im WPF-Testkontext erhalten.
Die Gegenprobe mit 30 leeren WPF-Hostfenstern erhöhte Handles ebenfalls
von 837 auf 858; Private Bytes sanken dabei auf 239.734.784.
Der Handleanstieg ist also nicht allein der Hardwareansicht zuzuordnen,
seine vollständige Ursache ist nicht bewiesen. Keine allgemeine
Leakfreiheits- oder Langzeitfreigabe aus diesen Daten ableiten.

Separater Fixtureproof: 58 Komponenten, Median Aufbau 0,6545 ms,
100 Snapshots 1,4808 ms, JSON 1,1080 ms. Kalter erster Lauf separat erfasst.
Reale Klassenlaufzeiten 0,293–13,180 s; insbesondere System/USB/PCI/
Bluetooth bleiben langsam. Kein permanenter Komplettpoller,
kein behaupteter Geschwindigkeitsgewinn gegenüber anderem Umfang.
Abschlussprüfungen liefen nacheinander, nicht parallel.

## 7. Nachweisdateien

Alle folgenden Dateien liegen ausschließlich im lokalen Sitzungsordner
`files`, nicht im Repository oder auf GitHub.
Reale Diagnosen werden nicht als öffentliche Artefakte beworben.

| Datei | SHA256 |
|---|---|
| `m3-final-readonly-03.json` | `0FCA138F42CD07CDEDF47C31E4EE3969D4ACA2DD42581E622FBD120B14831692` |
| `m3-final-readonly-03.json.timings.json` | `D08C782316A5C33CA15D72D842BEC22F4FB04E5A2028A1EABAA20528BA252E16` |
| `m3-final-acceptance-05.json` | `08BF89C18AF8F3AAFE562236FFF9843DB6B1EB9BE6D2EF14C9B045A8F465DFAF` |
| `m3-final-proof-02\fixture-performance.json` | `4B8DC653CC98B63E53641BABCCEF1665352E4C9BA113A709F794631A962EA20E` |

Visuell: `m3-final-proof-02\hardware-{theme}-{100,150,200}.png`,
neun Dateien; zusätzlich `m3-final-modern-02`, 22 Bilder.
Zwischenläufe sind Diagnose, nicht die hier genannten Abschlussnachweise.

## 8. Korrekturen und bewusst verworfene Wege

- Der erste Abnahmetest wartete mit einem zu hoch priorisierten
  Dispatcher-Testpump auf ApplicationIdle. Pump korrigiert;
  eigener hängender Testhost gezielt beendet, keine Produktlogik umgangen.
  Ein dadurch gesperrter Build wurde nach Bereinigung erfolgreich wiederholt.
- Der EDID-Zweig war zunächst irrtümlich unter dem Storagezweig verschachtelt.
  Tatsächlicher Leselauf zeigte fehlende Werte; Zweig korrigiert und
  ausführbare isolierte Regression hinzugefügt.
- EDID-Dokumentation und reales CIM-Rückgabetypmodell unterscheiden sich.
  Reale Klassenmetadaten geprüft, Boolean/UInt32-Erfolg typgeprüft behandelt,
  False und kaputte Prüfsummen explizit getestet.
- Unbekannte Shader-/WDDM-Codes werden nicht auf bekannte Fähigkeiten
  gerundet. Nicht unterstütztes Exclusive-Mixformat ist kein Audioausfall.
- Abschließende Windows-Diskidentitätsprüfung auf native Gerätedetails
  erweitert: Ein fehlgeschlagener Abgleich verwirft auch neu gelesene
  TRIM-/Wechselmedium-/Buswerte, nicht nur SMART-/NVMe-Werte.
- Verworfene riskante/spekulative Wege: zweiter Sensorcomputer,
  Kernel-/SMBus-/Treiberinstallation für SPD oder Effektivtakt,
  Prozent-/Nominaltakt-Schätzung, Hersteller-/Modellnamenjoins,
  pauschale NPU-/GPU-Fähigkeitsannahmen, unbekannte USB-Transporte,
  vollständige Capability-Zusage aus einzelnen Video-/Audio-Proben.

## 9. Endgültige Empfehlung und Übergabe

Die zentrale Hardwarezentrale, nachvollziehbare Quellen, genaue verfügbare
Gerätebeziehungen, sichere lesende Details, Fehlerzustände, Datenschutz,
Bedienbarkeit und Regressionen sind im beschriebenen Umfang nachgewiesen.
Alle Prioritäten sind implementiert oder mit untersuchter sicherer
Zugriffs-/Beweisgrenze abgeschlossen; nicht bloß auf Unsupported gesetzt.

**M3 ist mit diesen Grenzen abnahmefähig.** Eine echte Effektivtaktmessung,
rohes SPD, unabhängige NPU-Bestätigung, allumfassende GPU-/Audio-/Panel-
Spezifikationen und physische Langzeit-/Ereignisabnahme werden nicht behauptet.
Ressourcen-Nachbeobachtung und breite Hardwarekompatibilität bleiben
sinnvolle Folgeprüfungen, nicht vorgetäuschte bestandene Tests.

Process Command Center und Community-Feedback bleiben in der Planung:
„Es wäre praktisch, wenn man über's Kontextmenü eines Prozesses die
Möglichkeit hätte, ihn zu killen.“ Spätere UI-Bezeichnung:
**„Prozess beenden“**. Noch keine Umsetzung, keine Prozessbeendigung,
kein Meilenstein 4 oder Releaseauftrag. **Jetzt ausdrückliche Abnahme abwarten.**
