# Installation

AETHER Beta 54 wird als vollständiges Portable-Verzeichnis, als
Windows-Installer und mit einer getrennten Server-Komponente bereitgestellt.

## Voraussetzungen

- Windows x64 mit grafischem Desktop.
- Die passende Runtime ist im Portable-Paket enthalten.
- Hardware- und Sensorinformationen hängen von Gerät, Treiber und
  Windows-Konfiguration ab.

Die lokale KI-Funktion benötigt zusätzlich den getrennten Setup-Ablauf für
Runtime und Modell. Sie ist keine Voraussetzung für die übrigen
Cockpitansichten. Der Server ist nur für die optionale Zentralverwaltung
erforderlich.

## Portable: Download und erster Start

1. Das Portable-ZIP aus einer offiziellen AETHER-Bezugsquelle laden.
2. Die veröffentlichte SHA-256-Prüfsumme mit `SHA256SUMS.txt` vergleichen.
   Die Datei kann zum Beispiel so geprüft werden:

   ```powershell
   Get-FileHash .\AETHER-0.7.0-beta.54-Portable-win-x64.zip -Algorithm SHA256
   ```

3. Das vollständige ZIP in einen eigenen Ordner entpacken. Keine einzelne
   EXE aus dem Verzeichnis herauslösen.
4. `SystemCockpit.exe` starten. Windows kann für einzelne Funktionen eine
   erhöhte Ausführung anfordern.

Die Beta-54-Datei ist unsigniert. Eine Sicherheitswarnung nicht umgehen und
keinen Schutz deaktivieren. Bei einer Warnung die Bezugsquelle und
Prüfsumme kontrollieren.

## Setup-Installer

Den Windows-Installer
`AETHER-0.7.0-beta.54-Setup-win-x64.exe` aus den offiziellen Release-Assets
laden, die Prüfsumme vergleichen und den Dialogen folgen. Das Setup erkennt
eine bestehende AETHER-Installation und aktualisiert sie im vorhandenen
Vertrag. Nach erfolgreicher Hauptinstallation kann **Lokale KI für AETHER
einrichten** ausgewählt werden. Wird die Option übersprungen, bleiben die
übrigen AETHER-Funktionen startfähig; der Assistent ist anschließend über
das Startmenü erneut erreichbar.

## Separater Server

`AETHER-0.7.0-beta.54-Server-win-x64.zip` enthält die optionale
Management-Komponente. Das Archiv vollständig entpacken und die
mitgelieferte Serverdokumentation lesen, bevor eine Dienstinstallation oder
Netzwerkkonfiguration vorgenommen wird. Der Server ist für den normalen
lokalen Einzelplatzbetrieb nicht erforderlich.

## Lokale KI einrichten

Die Modellinstallation wird über den mitgelieferten AETHER-Assistenten
gestartet. Der Ablauf prüft die Systemfähigkeit, zeigt die Lizenz an und
benötigt deren ausdrückliche Annahme, bevor Runtime oder Modell eingerichtet
werden. Modelldateien sind nicht Teil der Programmpakete. Einzelheiten stehen in der
[Anleitung zur lokalen Modellinstallation](INTELLIGENCE-MODEL-INSTALLATION.md).

## Aktualisieren

Vor einem Austausch AETHER schließen. Die neue Version in einen separaten
Ordner entpacken, Integrität und Prüfsumme prüfen und danach Einstellungen
und benötigte lokale Daten nach der jeweiligen Anleitung übernehmen.
Alte Programmdateien nicht in den neuen Ordner mischen.

## Entfernen

AETHER schließen und den entpackten Programmordner entfernen. Gesondert
gespeicherte Nutzerdaten oder lokale Modelldateien werden dadurch nicht
zwangsläufig entfernt. Vor einer manuellen Bereinigung benötigte Daten
sichern und die betroffenen Verzeichnisse in AETHER beziehungsweise den
Windows-Benutzerdaten prüfen.

## Status

Beta 54 ist eine Vorabversion. Aktuelle Produktbilder stehen in der
[README](../README.md).
