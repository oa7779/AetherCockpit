# Portable-Installation

AETHER wird als vollständiges Portable-Verzeichnis bereitgestellt. Für Beta
53 ist kein Installer freigegeben.

## Voraussetzungen

- Windows x64 mit grafischem Desktop.
- Die passende Runtime ist im Portable-Paket enthalten.
- Hardware- und Sensorinformationen hängen von Gerät, Treiber und
  Windows-Konfiguration ab.

Die lokale KI-Funktion benötigt zusätzlich den getrennten Setup-Ablauf für
Runtime und Modell. Sie ist keine Voraussetzung für die übrigen
Cockpitansichten.

## Download und erster Start

1. Das Portable-ZIP aus einer offiziellen AETHER-Bezugsquelle laden.
2. Die veröffentlichte SHA-256-Prüfsumme mit `SHA256SUMS.txt` vergleichen.
   Die Datei kann zum Beispiel so geprüft werden:

   ```powershell
   Get-FileHash .\AETHER-0.7.0-beta.53-Portable-win-x64.zip -Algorithm SHA256
   ```

3. Das vollständige ZIP in einen eigenen Ordner entpacken. Keine einzelne
   EXE aus dem Verzeichnis herauslösen.
4. `SystemCockpit.exe` starten. Windows kann für einzelne Funktionen eine
   erhöhte Ausführung anfordern.

Die Beta-53-Datei ist unsigniert. Eine Sicherheitswarnung nicht umgehen und
keinen Schutz deaktivieren. Bei einer Warnung die Bezugsquelle und
Prüfsumme kontrollieren.

## Lokale KI einrichten

Die Modellinstallation wird separat aus AETHER gestartet. Der Ablauf prüft
die Systemfähigkeit, zeigt die Lizenz an und benötigt deren ausdrückliche
Annahme, bevor Runtime oder Modell eingerichtet werden. Modellgewichte
sind nicht Teil des Portable-ZIPs. Einzelheiten stehen in der
[Anleitung zur lokalen Modellinstallation](INTELLIGENCE-MODEL-INSTALLATION.md).

## Aktualisieren

Vor einem Austausch AETHER schließen. Die neue Version in einen separaten
Ordner entpacken, Integrität und Prüfsumme prüfen und danach Einstellungen
und benötigte lokale Daten nach der jeweiligen Anleitung übernehmen.
Alte Programmdateien nicht in den neuen Ordner mischen.

## Entfernen

AETHER schließen und den entpackten Programmordner entfernen. Gesondert
gespeicherte Nutzerdaten oder lokale Modellgewichte werden dadurch nicht
zwangsläufig entfernt. Vor einer manuellen Bereinigung benötigte Daten
sichern und die betroffenen Verzeichnisse in AETHER beziehungsweise den
Windows-Benutzerdaten prüfen.

## Status

Beta 53 ist ein lokaler Portable-Kandidat; eine öffentliche Bezugsquelle
steht noch aus. Es gibt keinen freigegebenen Beta-53-Installer.
