# AETHER — Meilenstein 3: Hardware Intelligence

Stand: 10. November 2026.

**MEILENSTEIN 3 NICHT ABNAHMEBEREIT**

Die zentrale Hardwarezentrale und der nachvollziehbare Inventar-/Exportpfad
sind implementiert und geprüft. Das ist noch nicht die vollständige
beauftragte Hardwaretiefe. Die unten aufgeführten fehlenden Lesepfade und
Abnahmeprüfungen bleiben offen; „Unsupported“ ersetzt keine Implementierung.
Nach diesem Bericht Stopp. Kein Meilenstein 4, kein neuer Installer,
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
| Dokumentationsabschluss dieses Berichts | `docs(hardware): document verified intelligence center and open scope`; über `git log` auflösbar |

Die finalen Tests betreffen den funktionalen Stand von `5f3d22a`.
Reine Dokumentationsänderungen danach verändern weder Binärlogik noch Tests.

## 3. Architektur

[Architektur und Lebensdauer](HARDWARE-INTELLIGENCE-ARCHITECTURE.md).
20 technische Klassen, eigene Komponentenauswahl, kompakte Tabellen und
Quelleninspektor. Alte Cockpitanzeigen und die Softwareinventarliste bleiben
bestehen. Kein Umbau von Wireformaten oder bestehenden Verwaltungsaktionen.

## 4. Quellen

[Vollständige aktuelle Quellenmatrix](HARDWARE-DATA-SOURCES.md).
CIM, CPUID/Windows-Topologie, DXGI, NetworkInterface sowie vorhandene
Audio-/Display-APIs. Tatsächlicher SourceType, Timestamp, Confidence,
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
NPU-Telemetrie/-Bestätigung, CPU-Microcode/effektiver Takt, SPD,
rohe SMART-/NVMe-Daten, Native Wi-Fi, HDR/Skalierung, Audioformate und
eigene Sensorstatistik. Dafür fehlen integrierte belastbare Lesepfade,
nicht zwingend die Information auf der Hardware.

## 7. CPU

Windows-Identität und Kerne/Threads, CPUID-Signatur und nachvollziehbare
Bit-/Family-/Model-Auswertung. Processor Groups, Sockel, Kerne,
Affinitymasken, NUMA, EfficiencyClass und Caches werden technisch angezeigt.
Keine geratene P-/E-Core-Bezeichnung; CPUID bezieht sich auf den ausführenden
logischen Prozessor, nicht pauschal alle Sockel. Mikrocode, effektive Takte
und eindeutig auf einzelne CPU-Geräte gemappte Sensoren bleiben offen.

## 8. GPU

Mehrere Windows-/DXGI-Instanzen bleiben identifizierbar. DXGI liefert
dedizierte/shared Speichergrößen und Prozessbudgets/-nutzung für Node 0.
Das ist kein geräteweiter VRAM-Verbrauch. Legacy AdapterRAM ist ausdrücklich
begrenzt beschriftet. Funktions-/Hersteller-APIs fehlen noch; keine
CUDA-/Raytracing-Zusage aus dem Herstellernamen.

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
Es wurden keine Rechte angehoben, Selbsttests oder Roh-NVMe-Kommandos gestartet.

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
Native-Wi-Fi-Band/Kanal/Standard und eigene Ratenhistorie bleiben offen.

## 13. Display

Windows-EDID-Zeichenfolgen mit expliziter ASCII-Ableitung, Identitäten,
Größen und aktive Desktopflächen. Aktueller Modus, Refreshcode, Orientierung
und Desktopfarbtiefe aus EnumDisplaySettingsEx. Keine native Panelauflösung,
HDR-Freigabe oder monitorbezogene Skalierung daraus ableiten.

## 14. Audio

Geräte, Endpoints, Status und vorhandene Standardrollen/Sitzungsangaben
aus der bestehenden lesenden Core-Audio-Topologie. Keine Aufnahme,
Wiedergabe oder Lautstärkeänderung. Formatcapabilities bleiben offen.

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
Neue Minimum-/Maximum-/Average-Historien sind nicht implementiert.

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
Im finalen realen Bericht: 789 Sensitive-Feldzeilen, alle redigiert;
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
Drei synthetische Runden, jeweils 66 Fixturekomponenten.
Median Fixtureaufbau 0,7368 ms, 100 Snapshotprojektionen 1,4849 ms,
JSON-Export 1,1019 ms; erster JIT-/Kaltlauf separat sichtbar.
Kein allgemeiner Produktbenchmark oder Langzeit-Leaknachweis.

Ein realer kompletter Prüfmodus las 19 Klassen nacheinander. Einzelzeiten
0,295 bis 15,228 s; USB/PCI/Bluetooth sind insbesondere noch langsam.
Die produktive Ansicht liest dagegen eine ausgewählte Klasse asynchron.
Keine permanente Komplettinventur. Ressourcenfreigabe und Abbruch sind
synthetisch geprüft; eine belastbare reale Idle-/Langzeit-/Resume-Matrix
fehlt noch. Deshalb keine vollständige Performanceabnahme.

## 22. Tests

- Release-Solutionbuild: 0 Warnungen / 0 Fehler.
- STA-Grundrunner: **4.558 Assertions**, davon 173 zusätzliche Assertions
  gegenüber dem M2-Grundstand; bestehende Tests nicht doppelt addieren.
- Kombinierte synthetische Interface-/Schoner-/Brandingprüfung:
  **4.911 Assertions einschließlich Grundrunner**.
- Bestehender deutscher Modern-/Neon-Smoke: **3.611 Assertions** und
  22 Bilder.
- Hardwareproof: neun Bilder in Retro-Neon und Modern hell/dunkel mit
  synthetischer Root-DPI 100/150/200 Prozent und ReducedMotion.
- Fixtures: Null/Missing, alle SourceType-/Fehlerzustände, Ablauf/
  Invalidierung, in-flight Änderungen, Geräteentfernung, identische
  Modelle, CPU-Topologie, Storage-/USB-Kanten, EDID, Sensorfehler,
  Lizenz-/Logo-Gates, JSON/CSV/Text-Redaktion, Abbruch/Shutdown und
  PowerShell-AST-Syntax ohne Abfrageausführung.

Reale Gerätewechsel, Resume und physische Monitor-/DPI-Wechsel sind nicht
durch diese Fixtures nachgewiesen. Die unveränderten Verwaltungsrunner
wurden für M3 nicht erneut ausgeführt; deren früherer Timeout bleibt
unabhängig dokumentiert.

## 23. Reale Hardwareprüfungen

Der Auftraggeber hat die rein lesende Prüfung dieses Rechners ausdrücklich
freigegeben. Keine BIOS-/Treiber-/Firmware-/Netzwerkänderung, kein Lasttest,
SMART-Selbsttest oder neuer Kernel-/Sensorprovider.

Finaler Nachweis `m3-readonly-final.json` enthält **5.024 Feldzeilen**,
jeweils Quelle, Zeit, Ergebniszustand und redigierten/normalen Wert.
Er liegt nur in den lokalen Sitzungsnachweisen, nicht im Repository oder
auf GitHub. Quellen-Antworten sind keine unabhängige Richtigkeitsprüfung.

| Status beim Berichtexport | Feldzeilen |
|---|---:|
| Available | 3.620 |
| Stale | 214 |
| Unavailable | 368 |
| AccessDenied | 8 |
| Unsupported | 760 |
| ProviderError | 54 |

Die im langen Gesamtlauf abgelaufenen dynamischen Werte werden korrekt als
Stale exportiert. Von den Providerfehlern entfallen 53 auf Netzwerkdetails
und einer auf den Legacy-WLAN-Pfad. Keine neue Sensorprüfung durchgeführt;
Sensoren wurden ausschließlich mit Fixtures geprüft.

SHA256-Nachweise:

| Lokaler Nachweis | SHA256 |
|---|---|
| `m3-readonly-final.json` | `35C1197469586AB72B8AC4287156B5AF4347B5F6C13B5AB3CE6A5B611253F4EA` |
| `m3-readonly-final.json.timings.json` | `9C7F4F8E7DA83FBA494B8BA8890481FBC97A23D54112808499950BBC1180F2DB` |
| `m3-proof-final\fixture-performance.json` | `FC90689012504CD322C154A6C35D9DB6D25D976AC9972AA6969378BC84245FE7` |

Frühere Diagnoseausgaben `m3-readonly-01/02`, CPU-Diagnose und der vor
dem Berichtsexport gescheiterte dritte Lauf sind keine Abschlussnachweise.

## 24. Regressionen und Korrekturen

Während der Implementierung wurden erkannt und vor den finalen Läufen
korrigiert: Skriptklammer/Syntax, fehlender CIM-Schlüsselpfad, zwischen
System/Firmware kollidierende Zusatzschlüssel, reine Header- statt
Routenvergleiche in alten Navigationstests sowie der Test-Dispatcher für
asynchronen Abbruch. Eine helle Proofdarstellung benötigte einen tatsächlich
gezeichneten Hintergrund. Regressionstests zeigen weiter das alte Cockpit,
Mainboard und Schoner; keine Datenquelle oder Bestätigung abgeschaltet.

## 25. Offene Abnahmegrenzen

- Vollständige Deep-Dive-Abdeckung gemäß Quellenlücken fehlt.
- NPU-Klassenzuordnung nicht unabhängig bestätigt.
- Eigene Sensorstatistik und gerätegenaue Live-Sensorverknüpfungen fehlen.
- Native Wi-Fi, GPU-Funktions-APIs, SPD und Roh-SMART/NVMe fehlen.
- Echte Hotplug-/Resume-/Langzeit-/Monitorwechselmatrix nicht verifiziert.
- Reale UI-Latenz-/Idle- und Ressourcenabnahme für große Inventare offen.
- Die Oberfläche ist als neue Ansicht geprüft, nicht als neue installierte
  Version abgenommen.

## 26. Meilenstein 4 und Neon Wiki

Stabile Feldschlüssel, Quelleninspektor, Datenschutz und unveränderliche
Beobachtungen können spätere Artikel und belegte Datenbankzuordnungen tragen.
Es gibt noch keine fertige Wiki-Anbindung. Weder Meilenstein 4 noch
Lokalisierungs-/Releasearbeiten wurden begonnen.

**MEILENSTEIN 3 NICHT ABNAHMEBEREIT — STOPP.**
