# Intune-Deployment

## Paketstand

Das vorhandene Win32-Paket gehört zu **0.7.0-beta.28**, nicht zu Beta 33.
Sein historisches Deploymentblatt liegt im Workspace unter
`dist\intune\INTUNE-DEPLOYMENT.md`.
Es enthält:

- `AetherCockpit-Setup-0.7.0-beta.28-win-x64.intunewin`
- Paket-SHA256:
  `a01a831826b8acfa777983c69c252143d5466aea35be1285427b1b772992a651`
- zugrunde liegendes Setup-SHA256:
  `a89721c0e78daf030934f8956f13d370978c9233452bf3df30be46ae95a5fe42`

Dieses Paket und sein Begleitblatt bleiben als Release-Unterlagen unverändert.
Die folgende Anleitung beschreibt die aktuellen Parameter. Für Beta 33 wurde
noch kein `.intunewin` erzeugt.

## Neues Paket vorbereiten

Das Microsoft Win32 Content Prep Tool separat von Microsoft beziehen und unter
`tools\intune\IntuneWinAppUtil.exe` bereitstellen. Es ist nicht Teil des
GitHub-Quellstands. Einen eigenen Quellordner verwenden,
der nur das geprüfte Setup enthält, und einen neuen Ausgabeordner wählen:

```powershell
.\tools\intune\IntuneWinAppUtil.exe -c C:\AetherPackage\Source -s AetherCockpit-Setup-0.7.0-beta.33-win-x64.exe -o C:\AetherPackage\Output -q
```

Vorhandene Release-Ausgaben nicht überschreiben.
Das Setup enthält die Runtime und die vom Publish übernommenen Dokumente.

## App-Konfiguration

Name **AETHER System Control**, Herausgeber **Olaf Weins**, Architektur **x64**.
App-Version und Erkennung an das tatsächlich verpackte Setup anpassen.
Windows 11 ist die Zielumgebung; die alte Angabe „Windows 10 1809“ ist kein
validierter Mindeststand für die aktuelle .NET-10-Beta.

Für maschinenweite Installation **System** wählen. Die App selbst startet
weiterhin mit UAC unter einem Administratorkonto und führt dort den
Erststart-Assistenten aus.

### Installation

Intune-Befehlszeile für ein neu erzeugtes Beta-33-Paket:

```text
AetherCockpit-Setup-0.7.0-beta.33-win-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="%WINDIR%\Temp\AetherCockpit-Setup.log"
```

Die vorausgewählte Autostart-Aufgabe wird ohne Zustimmung übersprungen
(`AETHER SILENT AUTOSTART SKIPPED`). Startmenü und Standard-Desktopprofil
werden angelegt; bei Neuinstallation bleiben Desktop-Link, Schoner,
Mitschnitt und App-Start aus.

| Parameter | Wirkung |
|---|---|
| `/MERGETASKS="screensaver"` | Schoner mitinstallieren, nicht aktivieren |
| `/AutostartConsent=1` | Neue erhöhte Anmeldeaufgabe für das ausführende Konto genehmigen |
| `/RecoveryConsent=1` | Erkannte AETHER-Reste ohne Installationseintrag zur Wiederherstellung freigeben |
| `/LOG="Pfad"` | Setup-Protokoll schreiben |

**`/AutostartConsent=1` nicht im SYSTEM-Kontext verwenden.** Die Aufgabe
wäre dem SYSTEM-Konto statt dem gewünschten Benutzer zugeordnet.
Anmeldestart ist nur im passenden Administratorkontext sinnvoll.
Ohne `/RecoveryConsent=1` stoppt eine Silent-Wiederherstellung kontrolliert.

Ein erhaltendes Update verwendet die erkannte Installation und behält
Konfiguration sowie bestehende Autostart-Aufgabe bei. Die EULA wird beim
ersten manuellen App-Start bestätigt, nicht durch Silent-Setup übersprungen.

### Deinstallation

```text
"%ProgramFiles%\AETHER System Control\unins000.exe" /VERYSILENT /NORESTART
```

Die Silent-Kommandozeile gilt als Bestätigung. Der Uninstaller beendet die
Prozesse dieser Installation und entfernt Installationsordner sowie
AETHER-Daten des **ausführenden Kontos**. SYSTEM-Deinstallation bereinigt
nicht automatisch die Daten aller Benutzerprofile.
Benötigte Daten vorher sichern.

### Erkennung

Für Beta 33:

| Regel | Wert |
|---|---|
| Dateipfad | `C:\Program Files\AETHER System Control` |
| Datei | `SystemCockpit.exe` |
| Versionsvergleich | größer oder gleich `0.7.0.33`, 64-Bit-Zuordnung |
| Alternativer Registry-Pfad | `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7B0C30A1-7DB4-45B7-A872-F94664465F40}_is1` |
| Registry-Wert | `DisplayVersion` gleich `0.7.0-beta.33`, 64-Bit-Zuordnung |

Für das vorhandene Beta-28-Paket bleiben `0.7.0.28` beziehungsweise
`0.7.0-beta.28` maßgeblich. Installationsziel kann abweichen; eine Dateiregel
am Standardpfad passt dann nicht.

### Rückgabecodes

`0` bedeutet Erfolg. Die bisherige Intune-Konfiguration behandelt `3010`
als Soft-Reboot. Das Setup setzt in seinem Quellstand keinen eigenen
`/RESTARTEXITCODE`; ein tatsächlicher Neustartcode hängt von der
Inno-Setup-Konfiguration ab. Nicht von einem immer gelieferten `3010` ausgehen.
Weitere Fehler anhand des Setup-Logs prüfen.

Beta-27-Pakete hatten eingeschränkten Silent-Install und keinen
unbeaufsichtigten Uninstall; für neue Verteilung nicht verwenden.

[Installation](INSTALLATION.md) · [Paketierung](DEVELOPMENT.md)
