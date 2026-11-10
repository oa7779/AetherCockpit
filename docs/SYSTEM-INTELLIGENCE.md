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

Diese Grenzen wurden vor den Proofläufen festgelegt. Tatsächliche Messwerte
und Geltungsbereich stehen in der
[Performance-Baseline](PERFORMANCE-BASELINE.md#system-intelligence).

| Bereich | Grenze |
|---|---|
| Aktuelles Inventar | 30.000 Entitäten, 60.000 Kanten |
| Timeline im Speicher | 20.000 Einträge; stabile Quellenschlüssel |
| Ressourcen-Kurzverlauf | 120 Proben |
| Watch | höchstens 32 Regeln; keine eigenen Timer |
| Recorder | höchstens 1.800 Proben, 60 Minuten, 20.000 Marker; zusätzliche Bytebudgets siehe unten |
| System Map | höchstens 200 sichtbare Knoten, übrige ausdrücklich begrenzt |
| Tabellen | Recycling; weniger als 200 erzeugte Zeilen bei 5.000 Einträgen |
| Erste Darstellung / Filter / Map-Fokus | unter 2.500 ms im synthetischen Test |
| Reguläre Live-Verarbeitung | unter 100 ms bei 5.000 Prozessen |
| Vergleich / Pfadsuche / globale Suchprojektion | jeweils unter 2.500 ms bei 5.000 Entitäten |
| Watch-/Recorder-Verarbeitung | unter 100 ms bei 5.000 Prozessen und 32 Regeln im synthetischen Test |
| Persistenz | Standard 14 Tage, 128 Dateien, 64 MiB insgesamt |
| Konfiguration | 1–90 Tage, 8–512 Dateien, 1–256 MiB |
| Einzeldatei | 8 MiB; Überschreitung expliziter Fehler |
| Zusätzliche periodische Quellenabfragen | 0 bei ausgeschalteter Inventarbeobachtung; bei ausdrücklicher Aktivierung höchstens eine serielle Kohorte in der vorhandenen 60-s-Abfrageschleife |
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

Die optionale Inventarbeobachtung erweitert die vorhandene Probe-Schleife,
nicht die Zahl ihrer Timer. Sie liest Software, Dienste, Aufgaben, Endpunkte,
Treiber und begrenzte Ereignisfenster neu. Laufende Arbeit wird nicht
überlappt; Auslassungen werden als Lücken dokumentiert. Watch-Regeln für
Inventaränderungen werden ausschließlich durch tatsächliche Folgeinventare
ausgewertet. Ohne Aktivierung sind das manuelle Aufnahmen.

## Identität und Vergleich

Snapshots enthalten Schema, GUID, Name, Zeitpunkt, Entitäten, Kanten,
Domänenabdeckung und optionalen Kontextbezug. Entitäten enthalten ID, Klasse,
Felder und gemeinsame `SourceMetadata`; Kanten enthalten beide existierenden
Endpunkte, Beziehungsart und dieselben Quellenattribute.
Validierung weist doppelte Identitäten, lose Kanten, unbekannte Schemas oder
Enums, ungültige verschachtelte Inhalte und Secret-Daten zurück.

Der Vergleichskontext ist SHA-256 aus gültiger Windows-MachineGuid und
aktuellem Benutzer-SID. Rohwerte werden nicht gespeichert. Der sensible Hash
ist kein Authentifizierungsnachweis. Ein anderer oder fehlender Kontext
erzeugt `NotComparable`. Der Legacy-Prozessimport ergänzt weder Kontext
noch Vollständigkeit; bekannte Hardwareverfügbarkeiten bleiben erhalten.

`New` und `Removed` brauchen vollständige Domänen auf beiden Seiten.
Bekannte passende Identitäten können auch unter partieller Domäne verglichen
werden. Stale oder nicht verfügbare Entitäten sind nicht vergleichbar.
Gleichheit umfasst Klasse, Namen und Felder; Quellenfehler erhalten eigene
`Incomplete`-Zeilen. Kanten werden inspiziert, aber nicht als gesonderter
Kantenänderungsbericht verglichen.

## Datenquellen und belastbare Verknüpfungen

| Bereich | Quelle und Schlüssel |
|---|---|
| Prozesse / Ressourcen | vorhandene Dashboard-/NativeMetrics; PID mit Erstellungszeit, Probenzeit und verfügbare Intervalldaten |
| Dateien | verifiziertes aktuelles Prozessimage oder explizit konfigurierte Executable; keine Ordnerähnlichkeit |
| Software | vorhandene Uninstall-Registryfilter; Hive, Registryansicht und Schlüssel; AppX-Paketidentität und deklarierte Executables |
| Dienste / Autostart / Tasks | vorhandene Prozesskorrelation; interne Dienstnamen, Run-Schlüssel und Taskidentitäten |
| Netzwerk | strukturierte TCP-/UDP-Endpunkte; Owner nur während erneut geprüfter Prozesslebenszeit; Adapter nur über eindeutige lokale Unicastadresse |
| GPU / Hardware / Sensoren | vorhandene PDH-PID-/LUID-Probe und M3-Aliasse; keine aus Adapterraten erfundenen Prozessbytes oder Sensorursachen |
| Geräte / Treiber | `Win32_PnPEntity`, `Win32_PnPSignedDriver`, `Win32_SystemDriver`; exakte Geräteinstanz, Treiberdienst und eindeutig auflösbarer lokaler Treiberpfad |
| Adapterinventar | `Get-NetAdapter -IncludeHidden`, InterfaceGuid, PnPDeviceID und numerisches NDIS-Medium |
| Windows-Stabilität | `Win32_ReliabilityStabilityMetrics`; ausschließlich Windows-Wert |
| Boot / Anmeldung | `Win32_OperatingSystem` und interaktive `Win32_LogonSession`; LogonId zusammen mit tatsächlichem Sitzungsbeginn |
| Ereignisse | Windows Event Log; Kanal, RecordId und Zeitpunkt; bekannte Provider-/ID-Schemata für App- und Gerätesubjekte |

Der zusätzliche Inventarparser erwartet genau sechs Quelldomänen und passende
Entitätsklassen. Fehlende/mehrdeutige PnP-IDs, abgeschnittene Antworten oder
fehlende Erfolgsmarker ergeben keine vollständigen Bestände.

Die Treibergruppe folgt der gemeldeten PnP-Klasse. WLAN ist eine abgeleitete
Gruppierung: genau eine Adapterzeile derselben PnPDeviceID und gemeldetes
NDIS-Medium 1 oder 9. Mehrdeutigkeit wird **vor** der Medienfilterung geprüft.
`functionalGroupSource` hält den Zusatzbeleg; die ursprünglichen
Treibermetadaten bleiben erhalten. Die Windows-Klasse `System` ist kein
Nachweis einer eigenständigen Chipsatzidentität.

Sieben Ereigniskanäle werden auf sieben Tage und jeweils 128 Einträge
begrenzt: Application, System, Diagnostics-Performance/Operational,
WindowsUpdateClient/Operational, Kernel-PnP/Configuration,
NetworkProfile/Operational und WLAN-AutoConfig/Operational.
XML ist auf 256 KiB und erlaubte Felder begrenzt; DTD und externe Auflösung
sind aus. Erreichtes Eintragslimit bleibt unvollständige Abdeckung.
Kernel-PnP-Geräteinstanzen und bekannte Application-EventData-PIDs können
exakt verbunden werden. Der Logger-PID aus `System.Execution` ist kein
belegter Fehlerprozess.

## Timeline, Reliability und Startup

Timeline trennt Ereigniszeit und Beobachtungszeit. Identitäten verhindern
Doppelzählung; spätere strukturierte Subjekte dürfen einen generischen
Eintrag ergänzen. Prozessverschwindung ist kein bestätigtes Prozessende.
Inventaränderungen erscheinen erst nach tatsächlichem Vergleich.
Filter nach Kategorie, Zeitraum und Text ändern nur die Darstellung.

Reliability gruppiert Provider, Event-ID, Kanal, Kategorie und gegebenenfalls
strukturiertes App-/Dienstsubjekt. Ein Anzeigename wird nicht zur installierten
Softwareidentität. Ein vorhandener Snapshot vor dem ersten Auftreten kann
als Vergleich gewählt werden; ohne ihn wird kein historischer Zustand erfunden.

Windows-BootTime wird nur innerhalb derselben verfügbaren OS-/Ereignisschema-/
Startartgruppe mit mindestens zwei Läufen verglichen. AETHER beobachtet
getrennt zehn aufeinanderfolgende gültige gemeinsame Proben mit CPU unter
10 Prozent und höchstens sechs Sekunden Abstand. Ein nachträglicher Appstart
liefert keine durchgängige Boot→Login→Desktop-Messung.
Rückwärts laufende Quellenzeit beginnt ein neues unvollständiges Intervall.

## Diagnose, Recorder und Watch

Die Slow-PC-Diagnose zeigt gemessene Prozessspitzen, Ressourcen,
Adapter-/Sensorwerte, Quellenzeit, Kurzverlauf und Ereignisnachbarn.
Prozess-I/O ist kein physischer Datenträgerwert; beobachtete Last ist
keine automatisch bewiesene Ursache.

Recorderprofile: Ressourcen, Ressourcen und Timeline, benutzerdefiniert.
Umfangsflags trennen Ressourcen, Prozesse, Netzwerk, Sensoren, Systemkontext
und Ereignisse. Höchstens 16 verschiedene CPU-/RAM-Spitzenprozesse,
acht Adapter mit je zwei Raten und 16 Sensoren je Probe.
Probenbudget 1 MiB, Markerbudget 512 KiB, Vorher-/Nachherkontext je 512 KiB.
Zu großer Startkontext wird abgewiesen; zu großer Endkontext bleibt ausdrücklich
unvollständig. Budgetauslassungen und Messlücken stehen im Bericht.
Monotone Dauer steuert das Ende; UTC-Uhrsprünge werden ausgewiesen, nicht
nachträglich geglättet. Automatisches Ende folgt der nächsten gemeinsamen Probe.
Stoppen wirkt auch bei laufender Inventur sofort, wartet vor dem Speichern
aber deren Abbruch ab. Verwerfen speichert die Aufnahme nicht.

Watch nutzt CPU, RAM, exakte Prozessidentitäten, vorhandene GPU-/Sensorwerte,
Freiplatz, Prozessauftreten/-verschwinden, frische Crash-/Hangereignisse und
belegte Inventaränderungen. Dauer 0–3.600 s, Cooldown 1–86.400 s;
Prozentgrenzen 0–100, diskrete Ereignisse Dauer 0 und Schwelle mindestens 1,
einzelner Crash Schwelle genau 1.
Numerische Quellen dürfen höchstens zehn Sekunden alt sein. Fehlende,
NaN-, doppelte, rückwärts laufende oder über sechs Sekunden unterbrochene
Proben erfüllen keine Dauerschwelle. Nach Auslösung bleibt die Bedingung
bis zum Unterschreiten verriegelt; Cooldown überlebt Regelbearbeitung/Neustart,
die noch nicht erfüllte Dauer nicht.

Eine zielgebundene Crash-Regel vergleicht das exakte strukturierte
Windows-Subjekt, nicht Nachrichtentext oder ähnliche Dateinamen.
Ohne Subjekt bleibt sie unerfüllt. Ungezielte Regeln erfassen alle passenden
frischen Ereignisse; historische/duplizierte Einträge lösen nicht aus.
Aktionen sind ausschließlich Benachrichtigung, Timeline, Snapshot und
Diagnosemarker. Automatische Snapshots sind sensible lokale Dateien,
unabhängig von der optionalen Sitzungsspeicherung.

## Speicherlebenszyklus und Bedienung

Nur exakt erkannte Dateinamen aus Typ, GUID-N und `.json` beziehungsweise
`.json.tmp`/`.json.corrupt` gehören der Retention und Löschung.
Fremde Dateien bleiben unberührt. Temp und Quarantäne zählen zum Gesamtbudget.
Genau ein höchstens 64 KiB großer Einstellungsbestand ist altersunabhängig,
zählt aber weiter zu Datei- und Bytegrenze.
Automatisches Speichern und Wiederholungsabstand verwenden monotone Zeit.
Manuelle Aktionen und Shutdown sind davon getrennt.

Softwareaktivität verwendet ausschließlich Software→Datei←Prozess-Kanten.
Bis zu acht letzte Snapshots und der aktuelle Bestand zeigen belegte Instanzen;
die vorhandene Prozesshistorie liefert höchstens 16 Instanzen mit je
32 letzten Proben. Ein separat gekennzeichneter Spitzenprozess-Kurzverlauf
ist keine vollständige Ausführungschronik.

System Map filtert auf verfügbare belegte Kanten, begrenzt die Darstellung auf
200 Knoten und bietet Zoom, Pan, Nachbarn, Reset und ungerichtete Pfadsuche.
Ein Pfad ist keine Kausalkette; lange Pfade werden mit tatsächlicher Gesamtlänge
begrenzt angezeigt. Die bestehende Palette findet Entitäten, Timeline,
Snapshots, Aufnahmen und Regeln. Navigation verwendet exakte Identitäten;
verschwundene Ziele werden nicht durch ähnlich benannte ersetzt.

Gemeinsame Fachansicht statt zwölf unabhängiger Sammler. Theme-Ressourcen,
Recycling, Screenreader-Namen, Tab-/Enter-Bedienung und Reduced Motion werden
wiederverwendet. Root-DPI-Proofs ersetzen keine physischen Monitorwechsel.
[Nachweise, Grenzen und Abnahmeempfehlung](SYSTEM-INTELLIGENCE-REPORT.md).
