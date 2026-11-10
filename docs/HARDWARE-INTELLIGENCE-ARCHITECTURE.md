# Hardware Intelligence — Architektur

## Einbindung

Die eigenständige Hardwarezentrale liegt unter **Hardware → Hardware
Intelligence** im Cockpit. Bestehende Anzeigen, das Inventar mit Softwareliste,
Mainboard und Messauswertung bleiben erhalten. Links stehen Klassen und
Komponenten, in der Mitte kompakte Eigenschaftstabellen, rechts die
Quelldetails. Vergrößerbare Spalten und Trennlinien erschließen lange Werte.
Der Quelleninspektor zeigt den vollständigen Inhalt einer ausgewählten Zeile.

Die drei Bereiche verwenden AetherNeonFrame statisch. Einzelwerte werden nicht
animiert. Windows Modern übernimmt seine neutralen Ressourcen und deaktiviert
Neonrahmen wie bisher; Retro-Neon bleibt der Standard.

## Datenfluss

`HardwareCimCatalog` beschreibt explizite Klassen und Eigenschaften, Einheiten,
Datenschutz und Aktualisierungsart. `HardwareIntelligenceReader` erzeugt daraus
fest definierte lokale Leseabfragen über den vorhandenen AdminShell/CommandRunner.
Es werden keine vom Benutzer gelieferten Skripte oder Befehle ausgeführt.

Zusätzliche Windows-/CPU-/DXGI-/Netzwerk-/Audio-/Display-Provider liefern eigene,
getrennt bezeichnete Datensätze. Gleich benannte Geräte werden nicht
zusammengelegt. CPU-Topologie und CPUID des ausführenden Prozessors sind
ebenfalls getrennte Quellen; deren Features werden nicht allen Sockeln
unterstellt.

Die Fortsetzung ergänzt folgende lesende Pfade:

- Native Wi-Fi verwendet eigene sicher freigegebene Client-/Antwortpuffer.
  Kein aktiver Scan, keine Verbindungs-/Profiländerung oder Schlüsselabfrage. SSID und
  BSSID werden nur intern zum eindeutigen Frequenzmatch verwendet.
- Adapterraten entstehen aus monotonen Bytezählerdifferenzen je exakter
  Adapteridentität. Erste Probe, Gegenlauf, fehlende Probe und Geräteentfernung
  benötigen eine neue Baseline. Beschriftung als Intervallmittel, nicht als
  momentane Rate.
- Direct3D erstellt ein Gerät für genau den DXGI-Adapter und fragt
  Featurelevel, Shader-/Wave-/Mesh-/Raytracing-Funktionen und je Node
  Speicherarchitektur ab. DXGI-Prozessbudgets werden je so belegtem Node
  abgefragt. Ohne Nodequelle bleibt die explizite Node-0-Sicht.
  ID3D12VideoDevice liefert begrenzt Profil-GUIDs und Inputstreamgrenzen.
  D3DKMT öffnet genau dieselbe LUID für WDDM-/Adapterflags und schließt
  den Windows-Adapterhandle wieder. Scheduler-Engine-Ordinalzahlen sind
  keine Direct3D-Nodes. Kein Render-/Compute-/Decodeauftrag.
  COM- und Abfragepuffer werden freigegeben.
- Audio liest das Shared-Mode-Mixformat und fragt dessen exakte Unterstützung
  mit IsFormatSupported in Shared/Exclusive ab. Näherungsformatpuffer werden
  freigegeben. Kein Initialize/Start und keine erschöpfende Formatmatrix.
- DisplayConfig liefert aktive Zielpfade, Windows-Verbindungsart,
  bevorzugten Zielmodus, Advanced Color und Desktopskalierung.
  Bevorzugter Modus und Advanced Color beweisen für sich weder physisch
  native Panelauflösung noch einen separaten HDR-Modus. Separat abgefragte
  AdvancedColorInfo2 liefert HDR-Unterstützung, Benutzerfreigabe und aktiven
  SDR/WCG/HDR-Modus. Aktive Modusreferenzen müssen zu Typ/Adapter/ID passen.
- EDID wird nur mit WmiGetMonitorRawEEdidV1Block gelesen: bis neun Blöcke,
  gültiger Basiskopf und Blockprüfsumme. Tatsächlicher boolescher
  Windows-CIM-Erfolg und dokumentierter numerischer Erfolg werden typgeprüft;
  falscher Rückgabestatus oder defekte Daten bleiben Fehler. Rohe Blöcke
  sind Sensitive. Deklarierte Erweiterungen und erfasste Anzahl sind sichtbar.
- Windows PDH nutzt englische Counterpfade unabhängig von der UI-Sprache.
  Jede Anfrage öffnet eine frische Query, sammelt Baseline, wartet abbrechbar
  300 ms und sammelt erneut. Prozessorgruppeninstanzen bleiben unverändert;
  geänderte Gruppen-/Prozessoranzahlen verwerfen das Ergebnis. Frequency
  und Leistungsprozent bleiben getrennt; kein eigener Effektivtakt.
- Ein inaktiver `IHardwareSpdProvider`-Vertrag verlangt genaue Gerätekennung,
  begrenzte Rohdaten, Direct-/Sensitive-Herkunft sowie Lizenz/Dokumentation.
  Er wird nicht automatisch gesucht oder gestartet. SMBIOS bleibt SMBIOS.

`HardwareFact` enthält `SourceObservation<string>` und unverändert die
M1-`SourceMetadata`. Der String erhält die vom Provider gelieferte skalare
Darstellung beziehungsweise eine explizit beschriftete Code-/Arraydarstellung.
JSON-Berichtsformat `aether.hardware/1` verwendet daher Zeichenfolgen als
Feldwerte, nicht implizit typisierte Messzahlen. Die Wireformate der Verwaltung
werden nicht erweitert oder ersetzt.

Der Beobachtungsvertrag erhält Wertanwesenheit, Einheit, Quelle, SourceType,
Timestamp, Confidence, Availability, RequiresElevation, IsEstimated,
IsCalculated, IsSensitive, Classification, Error und DocumentationLink.
Berechnung, Datenbankversion und Match besitzen eigene Detailfelder.
`Direct` bezeichnet die direkte Übernahme aus der angegebenen Quelle,
nicht eine unabhängige Bestätigung des physikalischen Sachverhalts.

## Aktualität und Lebensdauer

- **Inventory:** auf Anforderung, ohne periodische Komplettinventur.
- **Dynamic:** nach einer Minute als Stale behandeln; erneute Abfrage bei
  Auswahl einer veralteten Klasse oder manueller Aktualisierung.
- **Live:** bestehender Cockpit-Sensorpfad; nach zehn Sekunden Stale.

Ein leichter UI-Timer kontrolliert nur die Ablaufzustände angezeigter Werte.
Er startet keine Abfragen. Der Status im Export wird ebenfalls zum
Exportzeitpunkt ausgewertet. Fehlgeschlagene Gesamtaktualisierungen erhalten
alte Werte als Stale und einen zusätzlichen sichtbaren Providerfehler.
Ein einzelner fehlerhafter CIM-Provider liefert stattdessen seinen eigenen
Fehlerdatensatz, nicht „Hardware fehlt“.

Resume invalidiert die gelesenen Gruppen. Display-/Netzwerkereignisse betreffen
nur die entsprechenden Klassen. Nicht näher zuordenbare PnP-Ereignisse
invalidieren Geräteklassen, nicht CPU/Firmware. Nach 750 ms gebündelten
Ereignissen wird nur die sichtbare ausgewählte Klasse erneut gelesen;
die anderen bleiben bis zur Auswahl veraltet. Änderungen während einer
Abfrage verhindern über Versionszähler die Veröffentlichung als frisch.

Beim Verlassen der Ansicht werden Ereignisse, Fensterhook und Timer entfernt
und laufende Abfragen abgebrochen. Da währenddessen keine Ereignisüberwachung
besteht, werden gespeicherte Gruppen beim Wiederöffnen nicht unbesehen als
aktuell ausgegeben. Der normale Programmabschluss wartet auf die laufende
Hardwareabfrage. Keine neuen Sensorcomputer oder eigenen Kerneltreiber.
Die lesende SMART-Abfrage nutzt das vorhandene smartctl und den bestehenden
Windows-Storage-Treiberweg auf ausdrücklichen Aufruf der Storageklasse.

Die Sensorübersicht verwendet eine eigene Sitzung des vorhandenen
`SessionStatistics`-Aggregators. Pro exaktem Kanal werden nur endliche
Stichproben aggregiert; aktuelle Lücken bleiben Lücken. Minimum, Maximum,
Stichprobenmittel und Anzahl sind Derived und behalten ersten/letzten
tatsächlichen Messzeitpunkt. Keine Zeitgewichtung oder Interpolation.
Grenzwerte werden nicht aggregiert. Entfernte oder in Typ/Einheit veränderte
Kanäle werden entfernt, deaktivierter Zugriff setzt die Sitzung zurück.
Providergeräte- und PnP-Kennung sind Teil der Kanalidentität; ein anderes
Gerät übernimmt nicht die Statistik eines gleich benannten Vorgängers.
Maximal 2.048 aktive Reihen; Überschreitung und rückläufige/gleiche
Zeitstempel erzeugen einen sichtbaren Fehler statt falscher Statistik.

## Identitäten und Beziehungen

CIM-Schlüssel beziehungsweise die katalogisierte Identität identifizieren eine
Instanz. Interne Schlüssel werden domänenspezifisch gehasht, aber nicht als
anonymisierte öffentliche Hardwarekennungen behandelt: Im Export erscheinen
nur laufende Berichtsaliase.

Storage zeigt MSFT_PhysicalDisk → eindeutig zugeordneten MSFT_Disk →
MSFT_Partition → MSFT_Volume mit Dateisystem. Der zusätzliche Windows-Diskknoten
bildet die OS-Sicht ab. UniqueId, DiskNumber und Volume.Path/AccessPaths werden
nur exakt und eindeutig verknüpft. Nicht passende Geräte bleiben getrennt.
Storage-Reliability-Zähler gehören zur tatsächlich abgefragten PhysicalDisk.
USB-Eltern stammen aus DEVPKEY_Device_Parent; keine erfundene Porttopologie.

Sensoren tragen lokale, vom bereits offenen IHardware-Objekt gelesene
Identifier-/Parent-Kennungen. Diese werden nicht in Feed-/Snapshot-JSON
ergänzt. Jeder Sensor kann sein konkretes Providergerät als Elternknoten
besitzen. Ein Windows-Gerätejoin benötigt eine exakt gleiche, eindeutige,
verfügbare vollständige GenericGpu.DeviceId/PNPDeviceID; widersprüchliche,
fehlende oder doppelte Beziehungen bleiben unzugeordnet. Kennungen sind
Sensitive, die Joinbegründung ist Derived/ProviderReported. Es gibt keine
Sockel-/Slot-/Index-/Modellnamensheuristik.

Win32_DiskDrive.Index wird nur eindeutig mit MSFT_Disk.Number verknüpft.
Controller stammen aus Win32_SCSIControllerDevice/Win32_IDEControllerDevice,
nicht aus einem ähnlich benannten PnP-Elternknoten. Controllerkennungen
bleiben Sensitive. Der native Storagepfad öffnet den exakten verfügbaren
MSFT_Disk.Path ohne Lese-/Schreibzugriffsrechte, verifiziert dessen
Windows-Disknummer und verwendet ausschließlich Query-IOCTLs.
TrimEnabled, RemovableMedia und BusType werden getrennt behandelt;
fehlende TRIM-Unterstützung verliert nicht die anderen Felder.

SMART-/NVMe-Rohdaten werden an den Windows-Diskknoten angehängt:
`MSFT_Disk.Number → smartctl /dev/pdN`, zusätzlich muss `device.name` exakt
der Anfrage entsprechen. Nach der Abfrage werden Number, UniqueId und Path
erneut über CIM geprüft; unklare, entfernte oder gewechselte Geräte
erhalten weder neue Healthwerte noch TRIM-/Wechselmedium-/Busangaben.
Das gilt auch bei fehlendem smartctl oder fehlenden Healthwerten, wenn native
Storagewerte vorliegen. Die Prüfung ist keine atomare Hardwareaufnahme.
PnP-Invalidierung während der Gesamtinventur entwertet weiterhin deren
Versionsstand. Keine Gleichsetzung einer Windows-Storage-Kennung mit
einer anders definierten NVMe-Controller-Seriennummer.

Nur das vorhandene `-x -j -d auto` wird verwendet: keine Selbsttests,
Einstellungen, Firmware-/TRIM-Änderungen oder erzwungenen USB-Transporte.
Eine lesende Abfrage kann Geräte aktivieren und erzeugt Gerätezugriffe;
sie läuft nicht als permanenter Hintergrundpoller. Fehlendes Werkzeug,
Lesefehler und fehlende Attribute bleiben explizit. ATA-Rohwerte werden
nicht herstellerübergreifend als gleichbedeutend ausgelegt.

Cancellation wird zwischen nativen Aufrufen und während PDH-Wartephase/
Kindprozessen berücksichtigt. Synchrone Windows-/Treiber-API-Aufrufe werden
nicht durch freigegebene Hintergrundthreads oder vorzeitig freigegebene
Puffer „abgebrochen“; ihre Laufzeit hängt vom Windows-Treiber ab.
Die Abnahmematrix prüft die tatsächliche CIM-/PDH-Freigabe und separat
langsame injizierte Provider. Physische Geräteentfernung ist kein
dadurch nachgewiesener Test.

## Berichte und Grenzen

JSON, CSV und Text enthalten nur bereits gelesene Klassen. Der Dialog weist
darauf hin. Ohne ausdrückliche Zustimmung sind Sensitive-Werte redigiert.
Secret-Zeilen werden vollständig ausgelassen. Anzeigenamen und interne
Komponenten-/Elternschlüssel gelangen nicht als Identitäten in den Bericht.
CSV-Zellen sind gequotet und gegen Formelauswertung abgesichert.
Es gibt keinen Uploadpfad.

Nicht integrierte Felder bleiben mit technischer Erklärung sichtbar.
Unconfirmed wird für nicht unabhängig bestätigte NPU-Klassifizierung
verwendet; PnP-Klasse/GUID beweisen keine Telemetrie oder AI-Funktion.
Database ist darstellbar und getestet, aber es ist keine
Hardwaredatenbank aktiviert. Neon Wiki hat einen sichtbaren Platzhalter;
Artikel, Navigation und Datenbankzuordnungen sind nicht vorgetäuscht.

[Datenquellen und Lücken](HARDWARE-DATA-SOURCES.md) ·
[Datenbankrichtlinie](HARDWARE-DATABASE-POLICY.md) ·
[Brandingrichtlinie](HARDWARE-BRANDING-AND-LOGO-POLICY.md) ·
[Prüfbericht](MILESTONE-3-HARDWARE-REPORT.md)
