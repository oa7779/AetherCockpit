# AETHER

**Lokales Windows-Systemcockpit für Analyse, Diagnose, Verwaltung und
beleggebundene KI-Unterstützung.**

![AETHER-Logo](src/SystemCockpit/Assets/aether-logo.png)

## Problem und Ziel

Systemzustand, Hardwaremessungen, Diagnose und Verwaltungsaufgaben sind
häufig auf unterschiedliche Windows-Ansichten verteilt. Dadurch ist schwer
zu erkennen, welche Daten tatsächlich gemessen, welche nur abgeleitet und
welche Angaben nicht verfügbar sind.

AETHER führt diese Informationen in einem lokalen Systemkontext zusammen.
Das Ziel ist, vom Beobachten über das Verstehen und Diagnostizieren zu einer
informierten Entscheidung und gegebenenfalls einer kontrollierten Aktion zu
führen:

**Beobachten → verstehen → diagnostizieren → entscheiden → kontrolliert handeln.**

AETHER ordnet verfügbare Messungen und Quellen ein. Es unterscheidet
gemessene Werte, abgeleitete Aussagen, Modellformulierungen, validierte
Ergebnisse und nicht verfügbare Daten.

## Was AETHER ausmacht

- **Local first:** Die Cockpitansichten lesen lokale Windows- und
  Hardwarequellen. Der qualifizierte Phi-Pfad führt die Inferenz über den
  lokalen Ollama-Provider aus.
- **Evidence before claims:** Modellbehauptungen werden strukturiert und
  gegen die für die Anfrage verfügbaren Belege geprüft.
- **Nachvollziehbarkeit:** Aussagen bleiben mit ihren Quellen und
  Belegreferenzen verbunden. Eine formale Prüfung ist keine allgemeine
  semantische Wahrheitsgarantie.
- **Kontrollierte Verwaltung:** Die normale Intelligence-Anfrage führt keine
  Systemänderung aus. Geführte Verwaltungsabläufe zeigen Ziel und Wirkung
  und verlangen eine Bestätigung.
- **Ein Systemcockpit:** Systemübersicht, Hardware, Prozesse, Diagnose,
  Wartung und Intelligence teilen eine gemeinsame Arbeitsoberfläche.

## AETHER Intelligence

AETHER Intelligence verbindet lokale Systemmessungen mit dem qualifizierten
Modellpfad **Phi 4 Mini** (`phi4-mini:3.8b`):

**Windows und Hardware → Evidence → kanonische Werte und Claims → lokale
Phi-Inferenz → strukturierte Antwort → Claim-/Evidence-Validator → Anzeige**

Der Validator prüft unter anderem Claim-Typ, Subjekt, Eigenschaft,
kanonischen Wert und Evidence-Referenzen. Nicht belegbare oder
widersprüchliche Claims werden abgelehnt. Fehlende Messwerte bleiben
nicht verfügbar; sie werden nicht durch plausible Modellformulierungen
ersetzt. Der normale Frageablauf erteilt dem Modell keine Befugnis,
Verwaltungsaktionen auszuführen.

Phi wird nach der getrennten Einrichtung, ausdrücklichen Lizenzannahme und
erfolgreichen Hardware- und Integritätsprüfung automatisch für den
qualifizierten lokalen Pfad ausgewählt. Danach verwendet AETHER den
freigegebenen Pfad ohne manuelle Modellwahl für jede Frage (**Zero-Click
Local AI**). Runtime und Modellgewichte werden nicht während einer Frage
heruntergeladen und sind nicht Teil des Portable-Pakets.

## Funktionen

### Systemübersicht

Livewerte für CPU, Arbeitsspeicher, Datenträger und Netzwerk sowie
Windows-Status und verfügbare Hardwareinformationen. Nicht verfügbare
Sensoren bleiben als solche gekennzeichnet.

### Prozesse und Leistung

Prozessübersicht, Leistungsdaten und nachvollziehbare Beziehungen zu
Windows- und Hardwarequellen. Prozessaktionen zeigen ihr Ziel und
verlangen eine bewusste Auswahl.

### Hardware

Hardwareinventar und Sensoransichten mit Quellen- und
Verfügbarkeitsangaben. Grenzwerte werden nicht als Messwerte ausgegeben.

### Diagnose und Wartung

Windows-Status, Ereignisse und geführte Prüfungen. Änderungen werden von
reinen Beobachtungen getrennt; Verwaltungsabläufe zeigen Vorschau,
Bestätigung und Nachprüfung.

### Verwaltung

Ausgewählte Windows-Verwaltungsfunktionen für Prozesse, Dienste, Aufgaben,
Netzwerk und Wartung. Verfügbarkeit und Rücknahme hängen von der jeweiligen
Funktion und den Windows-Rechten ab. Die freie Administratorkonsole führt
eingegebene Befehle unmittelbar aus.

### AETHER Guardian

Guardian befindet sich in Entwicklung und ist keine freigegebene
Produktionsfunktion.

## Gestaltung

AETHER verwendet ein eigenständiges Retro-Neon-Design mit dunkler
Cockpitoberfläche, Cyan-, Magenta- und Grünakzenten sowie technischer
Instrumentenästhetik. Die Oberfläche ordnet dichte Informationen nach
Aufgaben; Animationen lassen sich abschalten. Die Darstellung soll
Funktionen unterstützen, nicht Messwerte oder Grenzen verdecken.

## Screenshots

Eine Galerie mit geprüften aktuellen Produktbildern kann hier ergänzt
werden. Derzeit werden keine historischen oder synthetischen Aufnahmen als
Beta-53-Ansichten dargestellt.

## Lokale KI

Die lokale KI-Einrichtung ist ein separater, bestätigter Setup-Ablauf.
Er prüft die Systemfähigkeit, zeigt die Modelllizenz an und verlangt deren
ausdrückliche Annahme. Der normale Frageablauf lädt weder Runtime noch
Modellgewichte herunter. Die einzelnen Schritte und Grenzen stehen in der
[Anleitung zur lokalen Modellinstallation](docs/INTELLIGENCE-MODEL-INSTALLATION.md).

## Sicherheit und Datenschutz

Der qualifizierte Phi-Pfad nutzt den lokalen Ollama-Provider; er benötigt
keine Cloud-Inferenz. Das optionale Client-/Serverangebot ist ein davon
getrennter Bedienablauf mit eigener Anmeldung, Gerätefreigabe und
Übertragungszustimmung. Berichte, Diagnosen und Screenshots können
Systemdetails enthalten; Inhalte vor einer Weitergabe prüfen.

Weitere Einzelheiten stehen unter
[Sicherheit und Datenschutz](docs/SECURITY-AND-PRIVACY.md).

## Systemvoraussetzungen

- Windows x64 mit grafischem Desktop; Windows 11 ist die Zielumgebung.
- Das Portable-Paket enthält die benötigte .NET-Laufzeit.
- Hardware- und Sensorabdeckung hängen von Gerät, Treiber und
  Windows-Konfiguration ab.
- Die lokale Phi-Funktion erfordert den getrennten Setup-Ablauf und ein
  geeignetes, ausdrücklich freigegebenes System.

## Installation und Download

Beta 53 ist ein lokaler Releasekandidat und noch nicht veröffentlicht.
Vorgesehen ist ausschließlich das Portable-Paket
`AETHER-0.7.0-beta.53-Portable-win-x64.zip`. Ein öffentlicher Downloadlink
erscheint erst nach Freigabe auf der GitHub-Release-Seite; derzeit gibt es
keinen Beta-53-Download. Ein Installer ist nicht freigegeben.

Das Portable-ZIP muss vollständig entpackt werden; DLLs, Laufzeit und
Begleitdateien bleiben zusammen. Vor einem Start Herkunft und SHA-256-
Prüfsumme prüfen. Das Paket ist unsigniert; Schutzfunktionen wegen einer
Warnung nicht deaktivieren.

## Dokumentation

| Thema | Anleitung |
|---|---|
| Produktumfang | [Funktionen](docs/FEATURES.md) |
| Bedienung | [Benutzerhandbuch](docs/USER-GUIDE.md) |
| Installation und Updates | [Portable-Installation](docs/INSTALLATION.md) |
| Hardware und Sensoren | [Messwerte und Quellen](docs/HARDWARE-SENSORS.md) |
| AETHER Intelligence | [Architektur](docs/INTELLIGENCE-ARCHITECTURE.md) |
| Modellpfad | [Lokale Modellinstallation](docs/INTELLIGENCE-MODEL-INSTALLATION.md) |
| Sicherheit und Datenschutz | [Sicherheitsgrenzen](docs/SECURITY-AND-PRIVACY.md) |
| Drittanbieter | [Lizenzen und Herkunft](THIRD-PARTY-NOTICES.md) |
| Versionsgeschichte | [Changelog](CHANGELOG.md) |

## Beta-Status

AETHER 0.7.0 Beta 53 ist eine Vorabversion. Der manuelle Startup- und
Branding-Smoke-Test des lokalen Kandidaten ist bestanden; der qualifizierte
Portable-Realtest mit lokalem Phi, erfolgreicher Validatorprüfung und ohne
Fallback bleibt belegt. Screenshots sind in diesem Sprint bewusst
zurückgestellt. Die lokale Paketprüfung und öffentliche Freigabe erfolgen
getrennt; ein öffentlicher Download ist noch nicht verfügbar. Der Installer
bleibt zurückgehalten.

## Lizenz

AETHER-eigener Quellcode und Dokumentation stehen unter der
[MIT-Lizenz](LICENSE). Drittanbieter behalten ihre eigenen Rechte und
Lizenzbedingungen.
