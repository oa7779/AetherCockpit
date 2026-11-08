# Installation

Beta 38 wird mit Windows-CI und synthetischen Desktopprüfungen getestet.
Eine Installation auf dem Zielrechner und der Hardwarebetrieb sind davon
nicht abgedeckt. Die Pakete bleiben unsigniert; Schutzrichtlinien erhalten.

## Voraussetzungen

AETHER wird als Windows-x64-Anwendung mit grafischem Desktop ausgeliefert.
Das Cockpit verwendet WPF und .NET 10; Setup und Portable-Paket enthalten die
Laufzeit. Windows 11 ist die Zielumgebung. ARM64 wird nicht als native Ausgabe
bereitgestellt.

Der Quellcode und das Setup legen keine eigenständige Liste freigegebener
Windows-Builds fest. Die früher genannte Intune-Mindestversion „Windows 10 1809“
ist daher kein belegter Kompatibilitätsnachweis für die aktuelle Beta.
Hardwareprovider und Windows-Bordmittel können je Build, Edition und Richtlinie
unterschiedlich verfügbar sein.

Das Cockpit fordert per UAC Administratorrechte an. Bei Anmeldung mit einem
anderen Administratorkonto gelten dessen Benutzerpfade und Einstellungen.
Der Bildschirmschoner läuft dagegen ohne UAC; siehe [Benutzerhandbuch](USER-GUIDE.md).

## Download und Prüfsumme

Veröffentlichte Pakete stehen bei den
[Beta-38-Downloads](https://github.com/oa7779/AetherCockpit/releases/tag/v0.7.0-beta.38).
Vor dem Start die
SHA256-Prüfsumme mit der zugehörigen `.sha256`-Datei vergleichen:

Beispiel für die Beta-38-Dateien:

```powershell
Get-FileHash .\AetherCockpit-Setup-0.7.0-beta.38-win-x64.exe -Algorithm SHA256
Get-FileHash .\AetherCockpit-0.7.0-beta.38-win-x64.zip -Algorithm SHA256
Get-FileHash .\AetherManager-0.7.0-beta.38-win-x64.zip -Algorithm SHA256
```

Die Beta-Pakete sind nicht signiert. SmartScreen oder Schutzsoftware können
warnen. Herkunft und Prüfsumme prüfen; eine Warnung nicht pauschal als
Fehlalarm behandeln und Schutzfunktionen nicht abschalten.

## Setup

1. Die passende `AetherCockpit-Setup-<Version>-win-x64.exe` starten und UAC bestätigen.
2. Zielordner und erkannte Vorinstallation prüfen. Standard:
   `C:\Program Files\AETHER System Control`.
3. Diagrammgruppen und Desktop-Gadgets wählen.
4. Optional Desktop-Verknüpfung, Anmeldestart, Bildschirmschoner oder
   Bereitstellung des Zentralverwaltungsdienstes wählen.
5. AETHER einmal manuell starten und die EULA bestätigen.

Die Startmenü-Verknüpfung wird angelegt. Der Anmeldestart ist eine erhöhte
Windows-Aufgabe für das angezeigte Administratorkonto und benötigt eine
gesonderte Zustimmung. Er startet Tray und Gadgets mit `--tray`, nicht das
Hauptfenster. Ohne bestätigte EULA startet die App im Tray-Modus nicht.

Die Dienstoption kopiert nur die Dateien. Anmeldung, Datenübertragung und
Dienstinstallation brauchen anschließend den separat bestätigten
Konsolenassistenten aus dem Startmenü. Der Server wird als eigenes ZIP geliefert.
[Dienst, HTTPS und Gerätefreigaben](CENTRAL-MANAGEMENT.md).

Die Npcap-Auswahl bietet nach der Installation die
[offizielle Downloadseite](https://npcap.com/#download) an. Download,
Installation und Lizenzannahme erfolgen separat durch den Nutzer.
Automatischer Paketmitschnitt braucht zusätzlich eine installierte
Ethernet-kompatible Schnittstelle, eingerichteten Autostart und eine eigene
Mitschnittfreigabe. [Netzwerk und Aufbewahrung](NETWORK.md).

## Portable Nutzung

Das ZIP vollständig in einen neuen Ordner entpacken und `SystemCockpit.exe`
starten. DLLs, Laufzeit, Werkzeuge und Dokumente zusammen behalten. Der
portable Start richtet keine Autostart-Aufgabe ein.

Portable und installierte Kopien verwenden dasselbe Kontodatenverzeichnis.
Nicht mehrere Versionen gleichzeitig betreiben. Der Schoner bleibt mit
`AetherMainboard.scr`, `AetherScreensaver.*` und der Laufzeit im Paketordner;
die einzelne SCR nicht nach System32 kopieren.

## Update

**Nicht vorher deinstallieren.** AETHER über das Tray-Menü
**AETHER vollständig beenden** schließen; das X am Hauptfenster blendet es
nur aus. Dann das neue Setup starten.

Das Setup erkennt Installation, Version und Ordner am stabilen
Windows-Installationseintrag. Es ersetzt die Programmdateien am bisherigen
Ort und erhält standardmäßig Setup-Profil, Kontodaten und bestehende
Autostart-Aufgabe. **Konfiguration neu festlegen** fragt die Setup-Auswahl
erneut ab; neue Autostart- und Mitschnittfreigaben bleiben erforderlich.
Eine portable Kopie hat keinen solchen Installationseintrag.

Bei veralteten smartctl-, PresentMon- oder OUI-Pfaden bleiben die übrigen
Einstellungen erhalten. Unter **System Control → Einstellungen** den
fehlenden Pfad neu auswählen oder leeren, um die vorhandene
Werkzeugerkennung zu nutzen.

### Bekannte Altinstallationen

- Beta 9 hatte Fehler bei der Profilgenerierung und beim App-Start am
  Setup-Ende. Ein aktuelles Setup als Update verwenden.
- Nach dem Autostartfehler in Beta 12 beim Update **Konfiguration neu festlegen**
  wählen und Autostart erneut genehmigen. Ein erhaltendes Update legt keine
  fehlende Aufgabe neu an.
- Startabbrüche durch doppelte GPU-Sensor-IDs in Beta 18–20 wurden in Beta 21
  behoben. Die Einstellungsdatei muss dafür nicht gelöscht werden.

## Unvollständige Installation wiederherstellen

Fehlen Uninstaller oder Installationseintrag, obwohl Programmdateien verblieben
sind, kann das Setup einen eigenen Ordner `AETHER System Control` anhand der
EXE-/DLL-Produktmetadaten erkennen. Diese Erkennung ist keine Signaturprüfung.

Laufendes AETHER zuerst beenden. Die angebotene Wiederherstellung nur für einen
ausschließlich für AETHER bestimmten Ordner bestätigen. Sie ersetzt
Programmdateien und stellt den Uninstaller wieder her; Kontodaten und
Zusatzdateien bleiben dabei erhalten. Die neue Setup-Auswahl wird angewendet.
Ein späterer Uninstall entfernt allerdings den gesamten Zielordner.

Systemordner, fremde Produktdateien und Ordnerlinks/Junctions werden
abgewiesen. Werden die Reste nicht erkannt, einen neuen eigenen leeren
Ordner wählen. Silent-Wiederherstellung erfordert `/RecoveryConsent=1`.

## Deinstallation und Datensicherung

Benötigte Berichte, Mitschnitte und Einstellungen vorher außerhalb der
AETHER-Ordner sichern. Bei alten Installationen zuerst das aktuelle Setup
als Update verwenden, anschließend über **Installierte Apps** deinstallieren.

Der aktuelle Uninstaller entfernt die Autostart-Aufgabe, beendet App, Tray,
Gadgets und Schoner dieser Installation und löscht den Installationsordner
sowie `%LOCALAPPDATA%\AetherCockpit` des ausführenden Kontos. Das umfasst
Einstellungen, Logs, Backups und Mitschnitte sowie verbliebene Kontodaten
älterer Versionen.

Externe Dateien und andere portable Programmordner bleiben bestehen.
Portable Kopien verlieren jedoch die gemeinsam genutzten Kontodaten.
Andere Windows-Profile werden nicht bereinigt. Im SYSTEM-Kontext gilt das
SYSTEM-Profil, nicht automatisch das Profil des angemeldeten Benutzers.

Die Windows-Schonerauswahl wird nur entfernt, wenn sie auf diese SCR zeigt.
Fehlende Installationsmarkierungen, ReparsePoints oder Fehler der
Deinstallationshilfe führen zu einer Fehlermeldung statt zur blinden
Verzeichnisbereinigung.

## Silent-Installation

Die folgende Kommandozeile verwendet das aktuelle Beta-38-Setup.
Bei einem veröffentlichten Download dessen tatsächlichen Dateinamen einsetzen.

```powershell
.\AetherCockpit-Setup-0.7.0-beta.38-win-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

Ohne `/AutostartConsent=1` wird keine neue erhöhte Aufgabe eingerichtet.
Npcap-Angebot, Mitschnitt und App-Start bleiben silent aus. Eine vorhandene
Konfiguration wird bei einem erhaltenden Update beibehalten.

```powershell
& 'C:\Program Files\AETHER System Control\unins000.exe' /VERYSILENT /NORESTART
```

Die Silent-Kommandozeile gilt als Uninstall-Bestätigung; sie entfernt dieselben
Daten wie der interaktive Ablauf. Parameter, Kontext und Erkennung:
[Intune-Deployment](INTUNE-DEPLOYMENT.md).

## Lokale Daten

Die App speichert unter `%LOCALAPPDATA%\AetherCockpit`, auch bei portabler
Nutzung. Eine vollständige Übersicht über Einstellungen, Berichte,
Aufbewahrung und sensible Inhalte bietet
[Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md).
