# AETHER — Meilenstein 3: Hardware Intelligence

Stand: 10. November 2026.

**MEILENSTEIN 3 NICHT ABNAHMEBEREIT**

Die zentrale Hardwarezentrale und der nachvollziehbare Inventar-/Exportpfad
sind implementiert und geprüft. Das ist noch nicht die vollständige
beauftragte Hardwaretiefe. Die unten aufgeführten fehlenden Lesepfade und
Abnahmeprüfungen bleiben offen; „Unsupported“ ersetzt keine Implementierung.
Die ausdrücklich beauftragte Reihenfolge ist jetzt: offene M3-Arbeiten,
danach Process Command Center. Der Prozessauftrag ist analysiert und geplant,
aber noch nicht implementiert; dieser Bericht erteilt keine M3-Abnahme.
Kein Meilenstein 4, kein neuer Installer,
Push, Tag, Release, Merge oder Versionswechsel.

## 1. Ausgangscommit

`21b2a0b466c34b526961e83ab234e08430b6750e`,
Branch `rc/grosse-entfesselung`. Sauberer Ausgangsstand. M2 wurde ausdrücklich
abgenommen. Bestehende Beta-45-Pakete und Veröffentlichungen unverändert.

## 2. Lokale M3-Commits

| Commit | Inhalt |
|---|---|
| `7e5eff9` | Normalisierte Hardwarebeobachtungen, M1-Metadaten, JSON/CSV/Text, Datenschutz, Datenbank-/Logo-Freigabemodelle |
| `ca60dd6` | CIM-Katalog und lesende Windows-/CPU-/DXGI-/Peripherieprovider |
| `37bb74c` | Eigene Navigation, dreispaltige Hardwarezentrale, Inspektor, gezielte Invalidierung, Sensorübernahme |
| `c72ebe0` | Quellen-/Topologie-/Mehrfachgeräte-/Storage-/Exporttests, UI-Proof und expliziter Read-only-Modus |
| `5f3d22a` | Doppelte Sensoridentitäten absichern, PCI-Adresse ableiten und vollständige Routenregression prüfen |
| `7159742` | Dokumentierte Grundlage und offene Abnahme |
| `db0ad45` | Endliche Sensorstatistik je Kanal mit Messintervall und Stichprobenherkunft |
| `a36915b` | Native Wi-Fi, Adapterraten, Microcode, Direct3D, Audio/Display und zugeordnete SMART-/NVMe-Healthwerte samt Regressionstests |

Die finalen Tests betreffen den funktionalen Stand von `a36915b`.
Reine Dokumentationsänderungen danach verändern weder Binärlogik noch Tests.

## 3. Architektur

[Architektur und Lebensdauer](HARDWARE-INTELLIGENCE-ARCHITECTURE.md).
20 technische Klassen, eigene Komponentenauswahl, kompakte Tabellen und
Quelleninspektor. Alte Cockpitanzeigen und die Softwareinventarliste bleiben
bestehen. Kein Umbau von Wireformaten oder bestehenden Verwaltungsaktionen.

## 4. Quellen

[Vollständige aktuelle Quellenmatrix](HARDWARE-DATA-SOURCES.md).
CIM, CPUID/Windows-Topologie, rohe Windows-Microcodebytes, DXGI/Direct3D,
NetworkInterface, Native Wi-Fi, Core Audio, DisplayConfig und das vorhandene
smartctl. Tatsächlicher SourceType, Timestamp, Confidence,
Availability und Datenschutz pro Feld. Keine bestätigte Herkunft durch
einen Modellnamen ersetzen. Direct ist direkt übernommen, nicht unabhängig
als physikalisch korrekt bestätigt.

## 5. Komponentenklassen

System, CPU, GPU, NPU, Mainboard, BIOS/UEFI, Chipsatz, RAM, Storage, PCI/PCIe,
Netzwerk, WLAN, Bluetooth, Audio, Display, USB, Sicherheitshardware,
Batterie/Power, Sensoren und Peripherie sind als Klassen erreichbar.
Eine Klasse mit einem Fehler- oder Lückeneintrag beweist kein vorhandenes
physisches Gerät. Windows-Grafik- und DXGI-Adapter bleiben getrennte Sichten.

## 6. Noch nicht implementierte Felder

Die Lückentabelle in [Datenquellen](HARDWARE-DATA-SOURCES.md#noch-nicht-integrierte-detailpfade)
ist maßgeblich. Besonders offen: GPU-API-/Video-/Speicherspezifikationen,
NPU-Telemetrie/-Bestätigung, effektiver CPU-Takt, SPD,
TRIM/Controller-Mapping, physische Paneldaten/separates HDR,
vollständige Audioformatcapabilities und gerätegenaue Sensorverknüpfungen.
Dafür fehlen integrierte belastbare Lesepfade,
nicht zwingend die Information auf der Hardware.

## 7. CPU

Windows-Identität und Kerne/Threads, CPUID-Signatur und nachvollziehbare
Bit-/Family-/Model-Auswertung. Processor Groups, Sockel, Kerne,
Affinitymasken, NUMA, EfficiencyClass und Caches werden technisch angezeigt.
Keine geratene P-/E-Core-Bezeichnung; CPUID bezieht sich auf den ausführenden
logischen Prozessor, nicht pauschal alle Sockel. Update Revision wird pro
Windows-Prozessoreintrag als rohe Bytefolge gelesen, ohne undokumentierte
Herstellerinterpretation oder Sockelzuordnung. Im realen Lauf 24 verfügbare
Revisionseinträge. Effektive Takte und eindeutig auf einzelne CPU-Geräte
gemappte Sensoren bleiben offen.

## 8. GPU

Mehrere Windows-/DXGI-Instanzen bleiben identifizierbar. DXGI liefert
dedizierte/shared Speichergrößen und Prozessbudgets/-nutzung für Node 0.
Das ist kein geräteweiter VRAM-Verbrauch. Legacy AdapterRAM ist ausdrücklich
begrenzt beschriftet. Direct3D 12 wird für genau den DXGI-Adapter geöffnet:
höchstes getestetes Featurelevel, Raytracing-Tier und UMA/CacheCoherentUMA
für Node 0. Kein Render-/Computeauftrag. Weitere API-/Herstellerpfade fehlen;
keine CUDA-Zusage oder iGPU/dGPU-Einstufung aus dem Herstellernamen.

## 9. RAM

Modulweise CIM-/SMBIOS-Daten einschließlich geschützter Seriennummer,
Breiten, gemeldeter Geschwindigkeiten und Spannungen. Rohe Attribute bleiben
als solche bezeichnet. Kein SPD-, JEDEC-, XMP- oder EXPO-Dump behauptet.

## 10. Storage

PhysicalDisk → Windows-Disk → Partition → Volume/Dateisystem wird nur über
eindeutige UniqueId-/DiskNumber-/Pfadbeziehungen verknüpft. Mehrdeutige
Matches bleiben ohne Kante. Modellnamen werden nicht zum Join verwendet.
Reliability-Werte gehören zur tatsächlich abgefragten PhysicalDisk.
Im realen Standardrechte-Lauf waren vier Reliability-Abfragen AccessDenied.
Es wurden keine Rechte angehoben oder Selbsttests gestartet.
Das vorhandene smartctl liest SMART-/NVMe-Healthdaten über `-x -j -d auto`.
Zuordnung über dokumentiertes `/dev/pdN`, exakten zurückgelieferten
Gerätenamen und erneute MSFT_Disk-Prüfung von Number, UniqueId und Path.
Keine Gleichsetzung unterschiedlich definierter Windows-/NVMe-Serienkennungen.
Unklare oder geänderte Identität verwirft die Healthwerte; kein atomarer
Hardware-Snapshot behauptet.

Im finalen realen Lauf zusätzliche 48 verfügbare Healthfelder auf drei
NVMe-Geräten. Zwei unbekannte USB-Brücken bleiben ProviderError mit
Exit-Bitmaske 1. Keine erzwungene Transportauswahl. NVMe-Zähler bleiben
präzise Skalare; Data Units sind als 512.000-Byte-Einheiten gekennzeichnet.
Percentage Used ist verbrauchte Ausdauer, keine Restlebensdauerprognose.
ATA-Normalized/Worst/Threshold/Raw bleiben Herstellerattribute ohne
pauschale Gesundheitsdeutung.

## 11. PCIe

Explizite PnP-IDs, Bus/Address, validierte Device/Function-Auswertung,
Treiber und gemeldete Linkcodes/-breiten. Keine vermutete physische Slotnummer.
Eine fachliche Übersetzung aller Linkcodes und eine Slot-/Controllerdatenbank
sind noch nicht vorhanden.

## 12. Netzwerk

Windows- und .NET-Sichten mit aktiven/inaktiven sowie virtuellen Adaptern.
MAC, Adressen, Gateways und DNS sind Sensitive. Kumulierte Byte-/Fehlerzähler
sind keine momentane Rx-/Tx-Rate. Ein Legacy-WLAN-Provider meldete einen
Providerfehler; daraus wird kein fehlendes WLAN-Gerät abgeleitet.
Native Wi-Fi liefert PHY/Standard, Signalqualität in Prozent, Linkraten
und Kanal. Mittenfrequenz/Band nur bei eindeutigem SSID/BSSID/BSS-Match
der Windows-Cacheliste; kein aktiver Scan, keine SSID/BSSID im Export.
Abgeleitete Rx/Tx-Raten verwenden die tatsächlich verstrichene Zeit
zwischen angeforderten Adapterabfragen, nicht einen neuen Poller.
Fehlende/rückläufige Zähler starten eine neue Baseline; eine echte
Nulldifferenz ist ein gültiger Nullwert. Nicht unterstützte
Protokolleigenschaften verlieren nicht die übrigen Adapterwerte.

## 13. Display

Windows-EDID-Zeichenfolgen mit expliziter ASCII-Ableitung, Identitäten,
Größen und aktive Desktopflächen. Aktueller Modus, Refreshcode, Orientierung
und Desktopfarbtiefe aus EnumDisplaySettingsEx. Keine native Panelauflösung,
HDR-Freigabe oder monitorbezogene Skalierung daraus ableiten.
DisplayConfig ergänzt aktive Zielverbindung, bevorzugten Windows-Modus,
Refresh und Advanced Color/Bits pro Kanal. Die zugeordnete Desktopfläche
liefert ihren Skalierungsfaktor. Das ist keine physische Panelzertifizierung;
Advanced Color allein ist kein separat bestätigter HDR-Modus.

## 14. Audio

Geräte, Endpoints, Status und vorhandene Standardrollen/Sitzungsangaben
aus der bestehenden lesenden Core-Audio-Topologie. Keine Aufnahme,
Wiedergabe oder Lautstärkeänderung. GetMixFormat ergänzt aktive Endpoints
um ihr Shared-Mode-Audioengineformat einschließlich Kanälen, Sample Rate,
Container-/gültigen Bits, Byte/s, Kanalmaske und Subtyp.
Keine Aussage über alle oder Exclusive-Mode-Geräteformate.

## 15. USB

Hostcontroller, Windows-USB-Geräte und deren tatsächliche Elternidentität.
VID/PID werden nur aus expliziten IDs extrahiert. Eltern außerhalb der
gelesenen Gruppe werden nicht durch vermutete Hubs oder Portnummern ersetzt.
Elektrische Topologie nicht implementiert.

## 16. Sensoren

Nur der vorhandene aktivierte Cockpit-Sensorpfad speist die Zentrale.
Alle gelieferten Sensortypen bleiben bezeichnet, einschließlich nicht
klassischer Typen. Keine neue Instanz von HardwareSensors/Computer geöffnet.
Endliche Nullwerte bleiben gültig, fehlende/ungültige Werte fehlen.
Temperaturgrenzen sind ausdrücklich keine Temperaturmessungen.
Doppelte IDs liefern ProviderError, keine willkürliche Zusammenlegung.
Der vorhandene SessionStatistics-Helfer aggregiert echte endliche Stichproben
je exaktem Kanal zu Minimum/Maximum/Mittel und Anzahl. Intervall vom ersten
bis zum letzten Messwert bleibt sichtbar; Lücken werden nicht interpoliert.
Keine Zeitgewichtung oder Grenzwertstatistik. Entfernte/geänderte Kanäle
und deaktivierter Zugriff setzen die jeweilige Messbasis zurück.
Maximal 2.048 aktive Reihen; Statistikfehler werden protokolliert und angezeigt.

## 17. Hardwaredatenbank

[Datenbankrichtlinie](HARDWARE-DATABASE-POLICY.md).
Versionierbarer Eintragsvertrag und exaktes Match-/Lizenz-/Prüfdatumgate
vorbereitet und getestet. Kein Datenbestand, Importer oder produktiver
Database-Match aktiviert. Keine proprietären Tabellen kopiert.

## 18. Branding

[Logo-Richtlinie](HARDWARE-BRANDING-AND-LOGO-POLICY.md).
Keine neuen Herstellerlogos. Vollständiges Freigabe-/Hashmodell vorbereitet,
aber kein Herstellerasset aktiviert oder juristisch freigegeben.

## 19. Datenschutz

Seriennummern, Gerätepfade, PnP-/EDID-Kennungen, MAC/IP/Gateway/DNS und
interne Instanzkennungen werden nicht als normale öffentliche Identitäten
exportiert. Interne Schlüssel und Anzeigenamen werden durch Berichtsaliase
ersetzt. Secret wird vollständig ausgelassen.
Im finalen realen Bericht: 792 Sensitive-Feldzeilen, alle redigiert;
kein Sensitive-Value ausgegeben, keine Secret-Zeile enthalten.
Dies ist zusätzlich zu den synthetischen Datenschutzfixtures geprüft.

## 20. Export

JSON `aether.hardware/1`, tabellarisches CSV und Text. Expliziter
Yes/No/Cancel-Dialog vor Sensitive-Export, Nein als Standard.
Nur bereits gelesene Klassen. Wertanwesenheit, Stale, Unavailable,
AccessDenied, Unsupported und ProviderError bleiben unterscheidbar.
CSV-Formelinjektion geprüft. Keine externen Uploads.
Der Dialog wurde nicht durch automatisierte Dateidialog-/Benutzerklicktests
freigegeben; der Serializer und seine Zustimmungsparameter sind getestet.

## 21. Performance

[Tatsächliche neue Messungen](PERFORMANCE-BASELINE.md#meilenstein-3-hardwarezentrale).
Drei synthetische Runden der Fortsetzung, jeweils 58 Fixturekomponenten.
Median Fixtureaufbau 0,6591 ms, 100 Snapshotprojektionen 1,5420 ms,
JSON-Export 1,0992 ms; erster JIT-/Kaltlauf separat sichtbar.
Geänderter Fixtureumfang: kein belastbarer Geschwindigkeitsvergleich
mit der Grundlage mit 66 Komponenten.
Kein allgemeiner Produktbenchmark oder Langzeit-Leaknachweis.

Ein realer kompletter Prüfmodus las 19 Klassen nacheinander. Einzelzeiten
0,329 bis 13,721 s; USB/PCI/Bluetooth sind insbesondere noch langsam.
Der Modern-Smoke lief parallel; dies ist keine isolierte Performanceprüfung.
Die produktive Ansicht liest dagegen eine ausgewählte Klasse asynchron.
Keine permanente Komplettinventur. Ressourcenfreigabe und Abbruch sind
synthetisch geprüft; eine belastbare reale Idle-/Langzeit-/Resume-Matrix
fehlt noch. Deshalb keine vollständige Performanceabnahme.

## 22. Tests

- Release-Solutionbuild: 0 Warnungen / 0 Fehler.
- STA-Grundrunner: **4.641 Assertions**, davon 256 zusätzliche Assertions
  gegenüber dem M2-Grundstand; bestehende Tests nicht doppelt addieren.
- Kombinierte synthetische Interface-/Schoner-/Brandingprüfung:
  **4.994 Assertions einschließlich Grundrunner**.
- Bestehender deutscher Modern-/Neon-Smoke: **3.611 Assertions** und
  22 Bilder.
- Hardwareproof: neun Bilder in Retro-Neon und Modern hell/dunkel mit
  synthetischer Root-DPI 100/150/200 Prozent und ReducedMotion.
- Fixtures: Null/Missing, alle SourceType-/Fehlerzustände, Ablauf/
  Invalidierung, in-flight Änderungen, Geräteentfernung, identische
  Modelle, CPU-Topologie, Storage-/USB-Kanten, EDID, Sensorfehler,
  Lizenz-/Logo-Gates, JSON/CSV/Text-Redaktion, Abbruch/Shutdown und
  PowerShell-AST-Syntax ohne Abfrageausführung.
- Fortsetzung: 83 zusätzliche Assertions für echte Sensorstatistik,
  Extremwertmittel, WLAN-Layouts, Frequenz-/Bandzuordnung, Adapterraten,
  Protokollfehler, Microcode/Audio/Direct3D sowie SMART-/NVMe-Identität,
  Zählerpräzision und redigierte Herstellertexte.

Reale Gerätewechsel, Resume und physische Monitor-/DPI-Wechsel sind nicht
durch diese Fixtures nachgewiesen. Die unveränderten Verwaltungsrunner
wurden für M3 nicht erneut ausgeführt; deren früherer Timeout bleibt
unabhängig dokumentiert.

## 23. Reale Hardwareprüfungen

Der Auftraggeber hat die rein lesende Prüfung dieses Rechners ausdrücklich
freigegeben. Keine BIOS-/Treiber-/Firmware-/Netzwerkänderung, kein Lasttest,
SMART-Selbsttest oder neuer Kernel-/Sensorprovider.

Finaler Nachweis `m3-continuation-readonly-final.json` enthält **5.588 Feldzeilen**,
jeweils Quelle, Zeit, Ergebniszustand und redigierten/normalen Wert.
Er liegt nur in den lokalen Sitzungsnachweisen, nicht im Repository oder
auf GitHub. Quellen-Antworten sind keine unabhängige Richtigkeitsprüfung.

| Status beim Berichtexport | Feldzeilen |
|---|---:|
| Available | 3.991 |
| Stale | 214 |
| Unavailable | 514 |
| AccessDenied | 8 |
| Unsupported | 858 |
| ProviderError | 3 |

Die im langen Gesamtlauf abgelaufenen dynamischen Werte werden korrekt als
Stale exportiert. Zwei Providerfehler betreffen unbekannte USB-Brücken,
einer den Legacy-NDIS-WLAN-Pfad; Native Wi-Fi liefert separat Werte.
Nicht unterstützte IPv4-Eigenschaften (native Fehler 50/10043/10047)
werden isoliert als Unsupported statt als pauschaler Adapterfehler angezeigt.
Der gestiegene Unsupported-Anteil ist daher kein Nachweis eines
Hardwareverlusts. Keine neue Sensorprüfung durchgeführt;
Sensoren wurden ausschließlich mit Fixtures geprüft.

SHA256-Nachweise:

| Lokaler Nachweis | SHA256 |
|---|---|
| `m3-continuation-readonly-final.json` | `1C41E36B6D9F15D2D0126EEDBA9B0141199BCB9652053A040D45C8306CE95B92` |
| `m3-continuation-readonly-final.json.timings.json` | `088100CFF6BA60351A67FB844EE40E5783F3E87D337BC95FE778EDEC6E48CCFF` |
| `m3-continuation-proof-01\fixture-performance.json` | `C62CCA56DAD0F08B2538DBFB02EED360EE280D7C7D5D49DED561EB20B8DAA653` |

Frühere Diagnoseausgaben einschließlich der Zwischenläufe der Fortsetzung
sind keine aktuellen Abschlussnachweise. Reale Berichte bleiben lokal.

## 24. Regressionen und Korrekturen

Während der Implementierung wurden erkannt und vor den finalen Läufen
korrigiert: Skriptklammer/Syntax, fehlender CIM-Schlüsselpfad, zwischen
System/Firmware kollidierende Zusatzschlüssel, reine Header- statt
Routenvergleiche in alten Navigationstests sowie der Test-Dispatcher für
asynchronen Abbruch. Eine helle Proofdarstellung benötigte einen tatsächlich
gezeichneten Hintergrund. Regressionstests zeigen weiter das alte Cockpit,
Mainboard und Schoner; keine Datenquelle oder Bestätigung abgeschaltet.

In der Fortsetzung zusätzlich korrigiert: zwei Variablennamenskonflikte,
isolierte IPv4-Protokollfehler, vom Tool nicht akzeptierte
PhysicalDrive-Schreibweise und die unzulässige Gleichsetzung von
Windows-Storage- und NVMe-Controller-Serienkennungen. Statt schwächerem
Modellvergleich wird die Windows-Geräteidentität nachgeprüft.
Ein Regressionstest deckt Mittelwertüberlauf bei endlichen Gegenwerten ab.

## 25. Offene Abnahmegrenzen

- Vollständige Deep-Dive-Abdeckung gemäß Quellenlücken fehlt.
- NPU-Klassenzuordnung nicht unabhängig bestätigt.
- Gerätegenaue Live-Sensorverknüpfungen fehlen.
- Weitere GPU-Funktions-/Video-APIs, SPD, TRIM-/Controller-Mapping,
  physische Paneldaten und vollständige Audioformatcapabilities fehlen.
- Zwei unbekannte USB-Brücken liefern keine SMART-Healthdaten;
  zusätzlicher Transportzugriff wird nicht ungeprüft erzwungen.
- Echte Hotplug-/Resume-/Langzeit-/Monitorwechselmatrix nicht verifiziert.
- Reale UI-Latenz-/Idle- und Ressourcenabnahme für große Inventare offen.
- Die Oberfläche ist als neue Ansicht geprüft, nicht als neue installierte
  Version abgenommen.

## 26. Meilenstein 4 und Neon Wiki

Stabile Feldschlüssel, Quelleninspektor, Datenschutz und unveränderliche
Beobachtungen können spätere Artikel und belegte Datenbankzuordnungen tragen.
Es gibt noch keine fertige Wiki-Anbindung. Weder Meilenstein 4 noch
Lokalisierungs-/Releasearbeiten wurden begonnen.

**MEILENSTEIN 3 NICHT ABNAHMEBEREIT.**
Keine Freigabe des unvollständigen Umfangs und kein vorgezogener Prozessumbau.
