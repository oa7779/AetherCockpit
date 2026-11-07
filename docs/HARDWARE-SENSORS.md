# Hardware, Sensoren und Messdaten

## Datenquellen

AETHER kombiniert Windows-Zähler und CIM-Abfragen mit
LibreHardwareMonitorLib 0.9.6. Beim normalen Cockpitstart wird der
Hardwareprovider geöffnet; der Sensorzugriff lässt sich anschließend
abschalten und erneut aktivieren.

Der Provider liefert die auf dem Rechner unterstützten Temperaturen,
Auslastungen, Taktraten, Spannungen, Leistungen, Lüfterdrehzahlen und
weiteren Kanäle. Administratorrechte allein garantieren deren Verfügbarkeit.
Firmware, Treiber und Windows-Sicherheitsregeln wirken mit.

**Nicht verfügbar ist kein Messwert von null.** Ungültige oder widersprüchliche
Werte erscheinen als Lücke. Gleiche Sensor-IDs werden anhand von Hardware,
Kanalname, Typ und Einheit getrennt; identische Meldungen werden zusammengefasst.
Widersprüchliche Werte desselben Kanals bleiben nicht verfügbar.

## Cockpitwerte richtig lesen

- **CPU:** Temperatur bevorzugt `CPU Package`, dann AMD `Core (Tctl/Tdie)`,
  `Core (Tdie)` und schließlich den wärmsten gemessenen CPU-Core. Board-/ACPI-Zonen
  werden nicht als CPU-Temperatur verwendet. Package-Leistung benötigt einen
  passenden gemeldeten Kanal. Null Watt bei gleichzeitig unlesbaren CPU-Takten
  behandelt der Provideradapter als uninitialisiert; positive Werte bleiben erhalten.
- **GPU:** Last, Temperatur, Hotspot, VRAM, Leistung, Takt und Lüfter bleiben
  einem ausgewählten Gerät zugeordnet. Diskrete NVIDIA-/RTX-Werte werden nicht
  mit einer integrierten GPU vermischt. Alle gemeldeten GPUs bleiben in der
  Sensor-Matrix sichtbar.
- **RAM:** Working Sets von Prozessen enthalten gemeinsam genutzte Seiten und
  dürfen nicht zum RAM-Gesamtwert addiert werden. Commit und Limit sind
  eigenständige Windows-Werte.
- **Datenträger:** Read/Write, Aktivzeit aus dem Idle-Zähler und Queue stammen
  aus physischen Windows-Datenträgerzählern. Prozess-I/O kann auch Netzwerk
  und andere Geräte umfassen und ist keine SSD-Bandbreite.
- **Netzwerk:** Durchsatz summiert aktive Adapter; VPNs können Daten mehrfach
  zählen. Die dynamische Skala ist keine Messung der Internetkapazität.

Live-/Prozessmessung pausiert etwa zwei Sekunden zwischen Messungen;
Ereignis-/Datenträgerabfragen etwa 15 Sekunden nach jeder Abfrage.
Abfragelaufzeiten kommen hinzu. Der erste CPU-/Netzpunkt bildet die Baseline.
Cockpit- und xBar-Kurven halten bis zu 90 Punkte im RAM.

Prozente verwenden eine 0–100-Skala, RAM/Commit ihre Limits und andere Werte
beschriftete dynamische Skalen. Eine dynamische Skala bewertet nicht die
Gesundheit der Hardware.

## Sensor-Matrix

**Live-Diagramme** gruppiert Kanäle nach Hardware und Einheit. Kanäle ohne
bekannte Einheit werden zusätzlich nach Typ getrennt. Die Legende erlaubt
das Ein-/Ausblenden einzelner Reihen für diese Sitzung. Der Filter gilt
auch für **Detailmatrix / Grenzwerte**.

Die Diagramme halten 180 Messzyklen, bei zwei Sekunden ungefähr sechs Minuten.
X zeigt Messzeit, Y die Skala der sichtbaren Reihen. Fehlende Daten unterbrechen
die Kurven. Historie bleibt bis zum Auslaufen des Fensters erhalten; vollständig
verschwundene Kanäle werden danach entfernt. Bei rückwärts springender
Systemzeit beginnt die Zeitachse mit Trace-Hinweis neu.

Die Sammlung nutzt den bestehenden Messtakt und löst keinen zusätzlichen
Treiberzugriff oder Datei-Export aus.

## Messwerte, Grenzwerte und Warnungen

NVMe `Warning Temperature` und `Critical Temperature` sind vom Laufwerk
gemeldete **Grenzwerte**, keine Ist-Temperaturen. AETHER kennzeichnet sie
als `TemperatureLimit` und nimmt sie aus Temperaturkurven, Warnungen und
MIN/MAX/AVG aus. Das gilt auch beim Import älterer Messlogs.
Echte hohe Composite- und Einzeltemperaturen bleiben sichtbar.

Die AETHER-Warnschwellen sind Benutzerkonfiguration, keine gemessenen
Hardwarelimits:

| Einstellung | Vorgabe | Einstellbereich |
|---|---|---|
| CPU-Temperatur | 90 °C | 40–115 °C |
| GPU-Temperatur | 85 °C | 40–110 °C |
| Storage-Temperatur | 70 °C | 30–100 °C |
| Freier Speicher | 10 % | 1–50 % |
| ATX-Spannungstoleranz | 5 % | 1–15 % |
| CMOS-Minimum | 2,5 V | 1–3,3 V |

Spannungsbewertung gilt nur für bekannte 12-/5-/3,3-V-Schienen und CMOS.
VCore und DRAM haben keine allgemein abgeleiteten Sollwerte.
Throttling wird nur anhand gemeldeter Thermal-/Throttling-Indikatoren bewertet;
hohe Temperatur allein beweist es nicht.
Storage-Warnungen nennen Laufwerk, Kanal, Provider und AETHER-Schwelle.

## Kernisolierung und PawnIO

Der **CPU-/Sensorproviderbericht** nennt den Kernelzugriffsstatus.
Bei HVCI/Speicherintegrität kann Windows den klassischen WinRing0-Treiber
blockieren; CPU-Temperatur und Package-Leistung können dann fehlen.

LibreHardwareMonitor kann den separat installierten
[PawnIO-Treiber](https://pawnio.eu) nutzen. AETHER installiert ihn nicht.
Bei fehlenden Werten zunächst Providerbericht, Hardwareunterstützung und
Treiberstatus prüfen; Windows-Schutzfunktionen dafür nicht abschalten.
Die lokale PawnIO-/HVCI-Erkennung ist ein Hinweis, keine Garantie erfolgreicher
Sensorabfragen.

## SMART

Windows Storage Health zeigt den von Windows gemeldeten Zustand. Für
erweiterte ATA-/NVMe-Daten im SMART-Modul erst Geräte erkennen und anschließend
Diagnose lesen. `smartctl` liefert Fähigkeiten, Attribute, Fehler- und
Selbsttestlogs als JSON; Rohdaten können exportiert werden.

Alle SMART-Aktionen verlangen die vorgesehene Bestätigung und Gerätezugriff.
Short/Long nur bei Geräteunterstützung verwenden. Conveyance, Selective,
AAM und APM sind ATA-spezifisch und für NVMe nicht definiert.

| Aktion | Parameter |
|---|---|
| Selective | `StartLBA-EndLBA`, Start nicht größer als Ende |
| AAM | 128–254 |
| APM | 1–254 |
| Selbsttest stoppen | `abort` |

Ein gestarteter Selbsttest läuft im Laufwerk weiter, auch wenn der
App-Befehl abgebrochen wird. `abort` beendet ihn gesondert.
smartctl-Exitcodes sind Bitmasken: Bits 0–2 betreffen Befehls-/Zugriffsfehler,
Bits 3–7 Health-/Logbefunde. Ein Nichtnullwert ist nicht pauschal ein Toolfehler.

NVMe `percentage_used` beschreibt verbrauchte Ausdauer, keine berechenbare
Restlebenszeit. NVMe Error-Log-Zähler sind keine ATA-Sektorfehlerzähler.
Ein SMART-Gesamtstatus ersetzt keine Datensicherung.

## Statistik, Aufzeichnung und Export

MIN/MAX/AVG verwendet gültige Werte aus bis zu 2048 Reihen.
**Anhalten** friert die Statistik ein, nicht das Cockpit. Sensor-Momentaufnahme
und sichtbare Statistikreihen haben getrennte CSV-Exporte.
CSV verwendet invariant formatierte Zahlen, Quoting und Schutz gegen
Formelanfänge.

Die JSONL-Aufzeichnung ist standardmäßig aus. Intervall: 2–60 Sekunden,
Vorgabe fünf Sekunden, an den Messtakt angepasst. Speicherung:
`%LOCALAPPDATA%\AetherCockpit\History`.

Es bleiben höchstens sieben Tagesdateien mit je 16 MiB erhalten. **Das ist
eine Dateianzahl, keine garantierte Löschung nach sieben Kalendertagen.**
Bei vollem Tagesbudget stoppt die Aufzeichnung mit Hinweis und muss
für einen neuen Tag manuell aktiviert werden.

Messlogs enthalten Zeit, native Werte und Hardware-/Sensornamen, aber keine
zusätzlich erfassten Verbindungsziele, Konten oder Seriennummern.
Der Import begrenzt Dateien auf 16 MiB, Zeilen auf 1 MiB, Messzyklen auf
43.200 und Reihen auf 2048. Beschädigte Zeilen werden mit Zeilennummer
gemeldet; mehr als 100 Fehler/Hinweise führen zum Abbruch.
Ausgedünnte Kurven ändern nicht die zugrunde liegende Statistik.

## PresentMon

Für Frame-Capture einen exakten laufenden Prozessnamen ohne Pfad eingeben,
etwa `Spiel.exe`, und 5–600 Sekunden wählen. AETHER startet die mitgelieferte
Console-EXE mit eigener ETW-Sitzung und deaktiviertem Inputtracking.
Es wird kein zusätzlicher PresentMon-Dienst installiert.

Die Auswertung bleibt pro Prozess und Swapchain getrennt:

- AVG FPS = `1000 / mittlere Framezeit`.
- P99 = Nearest-Rank-Quantil der Framezeiten.
- 1%-low = `1000 / Mittelwert der langsamsten 1 % der Frameintervalle`.

Importlimit: 64 MiB, eine Million Zeilen und 256 Prozess-/Swapchain-Gruppen.
Nullintervalle werden ausgelassen, ungültige Werte gemeldet.
Die Werte beschreiben die erfassten Frameintervalle, nicht zwingend die
tatsächlich am Monitor ausgegebenen FPS. Capture ist kein CPU-/GPU-Stresstest.
ETW-Rechte und Treibereinschränkungen bleiben maßgeblich.

[Komponenten und Lizenzen](../THIRD-PARTY-NOTICES.md)
· [Sicherheit und Daten](SECURITY-AND-PRIVACY.md)
