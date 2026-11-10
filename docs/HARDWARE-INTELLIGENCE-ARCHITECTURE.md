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
Hardwareabfrage. Keine neuen Sensorcomputer, Kernelzugriffe oder Treiber.

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

## Berichte und Grenzen

JSON, CSV und Text enthalten nur bereits gelesene Klassen. Der Dialog weist
darauf hin. Ohne ausdrückliche Zustimmung sind Sensitive-Werte redigiert.
Secret-Zeilen werden vollständig ausgelassen. Anzeigenamen und interne
Komponenten-/Elternschlüssel gelangen nicht als Identitäten in den Bericht.
CSV-Zellen sind gequotet und gegen Formelauswertung abgesichert.
Es gibt keinen Uploadpfad.

Nicht integrierte Felder bleiben mit technischer Erklärung sichtbar.
Database/Unconfirmed sind darstellbar und getestet, aber es ist keine
Hardwaredatenbank aktiviert. Neon Wiki hat einen sichtbaren Platzhalter;
Artikel, Navigation und Datenbankzuordnungen sind nicht vorgetäuscht.

[Datenquellen und Lücken](HARDWARE-DATA-SOURCES.md) ·
[Datenbankrichtlinie](HARDWARE-DATABASE-POLICY.md) ·
[Brandingrichtlinie](HARDWARE-BRANDING-AND-LOGO-POLICY.md) ·
[Prüfbericht](MILESTONE-3-HARDWARE-REPORT.md)
