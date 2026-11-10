# System Intelligence – Architektur und Quellen

## Ausgang und Arbeitsprinzip

Abgenommenes Prozesspaket: `c7b6d962dd215575c2ab9b82154eca961dfbf90f`.
Saubere Phase-0-Baseline: `5480685c046f6aa97b3fecce21ccd92aa887dcd5`.
Die bisherigen Hardware- und Prozessnachweise bleiben unverändert.

SEHEN → VERSTEHEN → VERGLEICHEN → HANDELN: Eine gemeinsame Entitätsprojektion
verbindet Fachansichten, Timeline, Vergleich, Suche, Diagnose und System Map.
Beobachtungszeit und Quellenzeit sind getrennt. Zeitliche Nachbarschaft ist
keine Ursache. Fehlende Werte sind keine Nullwerte.

## Wiederverwendung

- `DashboardMetrics`, `NativeMetrics` und vorhandene Ereignisschleife liefern
  gemeinsam genutzte Proben. Kein neuer periodischer Windows-Abfrager.
- Vorhandene M3-Momentaufnahmen werden weitergereicht, nicht heimlich neu gelesen.
- `SystemInventory` liefert dieselben Uninstall-Filter mit zusätzlicher
  Registry-Identität; keine namensbasierte Programmeigentümerschaft.
- `ProcessCorrelation`, minimal berechtigte Prozesshandles und vorhandener
  PowerShell-Runner bleiben die Grundlage der Beziehungen.
- `SourceMetadata`, Verfügbarkeiten und Datenschutzpolicy bleiben gemeinsam.
- Die bestehende Prozess-Snapshotdatei bleibt lesbar und unverändert. Das
  breitere Inventar erhält ein eigenes versioniertes Format, keine stillschweigende
  Umdeutung alter unvollständiger Bestände.

## Daten und Speicherung

Entitäten besitzen typisierte Klassen und quellenspezifische Identitäten.
Quellenabdeckung ist pro Domäne ausdrücklich vollständig oder unvollständig.
Auch ein leerer Bestand braucht einen erfolgreichen Abdeckungsnachweis.
Vergleichsarten: New, Removed, Changed, Unchanged, NotComparable, Incomplete.
Prozessverschwindung bedeutet nur „nicht mehr beobachtet“.

Lokale Speicherung unter dem Einstellungsordner in `Intelligence`, getrennte
Dateitypen für Snapshots, Baselines, Sitzungen, Aufzeichnungen und Einstellungen.
Versionierte Hülle mit SHA-256 erkennt Beschädigung, nicht böswillige Manipulation.
Atomarer Tempdatei-Austausch, serialisierte Dateizugriffe; unbekannte Version
oder beschädigter Datensatz wird einzeln mit sichtbarer Meldung isoliert.
Keine Cloud, keine neue Datenbank und keine neuen Pakete.
Export verwendet explizite Freigabe und vorhandenen CSV-Formelschutz.
Vollständige lokale Dateien sind sensibel und nicht verschlüsselt.

## Vorab festgelegte technische Budgets

Diese Grenzen sind Prüfziele, noch keine Messergebnisse:

| Bereich | Grenze |
|---|---|
| Aktuelles Inventar | 30.000 Entitäten, 60.000 Kanten |
| Timeline im Speicher | 20.000 Einträge; stabile Quellenschlüssel |
| Ressourcen-Kurzverlauf | 120 Proben |
| Watch | höchstens 32 Regeln; keine eigenen Timer |
| Recorder | höchstens 1.800 Proben, 60 Minuten, 20.000 Marker |
| System Map | höchstens 200 sichtbare Knoten, übrige ausdrücklich begrenzt |
| Tabellen | Recycling; weniger als 200 erzeugte Zeilen bei 5.000 Einträgen |
| Erste Darstellung / Filter / Map-Fokus | unter 2.500 ms im synthetischen Test |
| Reguläre Live-Verarbeitung | unter 100 ms bei 5.000 Prozessen |
| Persistenz | Standard 14 Tage, 128 Dateien, 64 MiB insgesamt |
| Konfiguration | 1–90 Tage, 8–512 Dateien, 1–256 MiB |
| Einzeldatei | 8 MiB; Überschreitung expliziter Fehler |
| Zusätzliche periodische Quellenabfragen | 0 |
| Historien-Diskwrite | frühestens alle 30 Sekunden, nur bei Aktivierung/Änderung |
| Inaktive Ansicht | keine UI-Neuprojektion pro Liveprobe |
| Shutdown | eigene laufende Arbeit abbrechen und abwarten |

CPU, Private Bytes, Working Set, Handles, Threads und GC werden vor/nach
kontrollierten Lebenszyklen erfasst. Eine kurze Probe ist keine Dauerfreigabe;
es gibt keinen erfundenen geräteunabhängigen RAM- oder CPU-Messwert.

## Reihenfolge

Zuerst Evidenz, Persistenz und Vergleich. Darauf aufbauend Windows-Inventare,
Reliability und Startup; dann Diagnose/Recorder/Watch. Anschließend gemeinsame
Ansicht, Map, Suche und Navigation; zuletzt echte und synthetische Nachweise.
Diese Abhängigkeiten vermeiden voneinander getrennte Historien.
