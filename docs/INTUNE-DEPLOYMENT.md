# Intune-Deployment

## Paketstand

Aktuelles Win32-Paket: **0.7.0-beta.38**.
`AetherCockpit-Setup-0.7.0-beta.38-win-x64.intunewin` wird aus dem
gleichnamigen Beta-38-Setup erzeugt und mit SHA256-Prüfsumme auf
[GitHub](https://github.com/oa7779/AetherCockpit/releases/tag/v0.7.0-beta.38) veröffentlicht.
Lokale Ausgabe: `dist\intune\0.7.0-beta.38`.
Der Desktopquellstand wird in Windows-CI geprüft. Die Intune-Paketintegrität
ersetzt keine Installation oder Prüfung auf verwalteten Zielgeräten.
Vor einem produktiven Rollout eine autorisierte Testgruppe verwenden.
Die alten Beta-27-/28-Pakete und ihr historisches Begleitblatt bleiben unverändert.

## Neues Paket vorbereiten

Das Microsoft Win32 Content Prep Tool separat von Microsoft beziehen und unter
`tools\intune\IntuneWinAppUtil.exe` bereitstellen. Es ist nicht Teil des
GitHub-Quellstands. Einen eigenen Quellordner verwenden,
der nur das geprüfte Setup enthält, und einen neuen Ausgabeordner wählen:

```powershell
powershell -NoProfile -File .\build\Publish-Intune.ps1
```

Das Skript liest die Version aus dem Publishprofil, kopiert nur das zugehörige
Setup in einen temporären Quellordner, erzeugt `.intunewin` und Prüfsumme
und kopiert dieses Deploymentblatt in die neue Ausgabe.
Vorhandene Release-Ausgaben werden abgewiesen.
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

Intune-Befehlszeile für das Beta-38-Paket:

```text
AetherCockpit-Setup-0.7.0-beta.38-win-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="%WINDIR%\Temp\AetherCockpit-Setup.log"
```

Die vorausgewählte Autostart-Aufgabe wird ohne Zustimmung übersprungen
(`AETHER SILENT AUTOSTART SKIPPED`). Startmenü und Standard-Desktopprofil
werden angelegt; bei Neuinstallation bleiben Desktop-Link, Schoner,
Mitschnitt und App-Start aus.

| Parameter | Wirkung |
|---|---|
| `/MERGETASKS="screensaver"` | Schoner mitinstallieren, nicht aktivieren |
| `/MERGETASKS="managedclient"` | Dienstdateien bereitstellen; installiert oder startet noch keinen Clientdienst |
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

Für Beta 38:

| Regel | Wert |
|---|---|
| Dateipfad | `C:\Program Files\AETHER System Control` |
| Datei | `SystemCockpit.exe` |
| Versionsvergleich | größer oder gleich `0.7.0.38`, 64-Bit-Zuordnung |
| Alternativer Registry-Pfad | `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7B0C30A1-7DB4-45B7-A872-F94664465F40}_is1` |
| Registry-Wert | `DisplayVersion` gleich `0.7.0-beta.38`, 64-Bit-Zuordnung |

Die Registry-Erkennung muss für jede neue Beta auf deren Versionszeichenfolge
angepasst werden. Installationsziel kann abweichen; eine Dateiregel
am Standardpfad passt dann nicht.

Bestehende Intune-App auf das neue `.intunewin` umstellen oder mit
Supersedence als erhaltendes Update verteilen, nicht zuerst deinstallieren.
Installationsbefehl, App-Version und Erkennung gemeinsam aktualisieren.
Den Clientdienst nur bereitstellen, wenn er gewünscht ist; Serveranmeldung und
lokale Rechtefreigabe bleiben im separaten Assistenten und werden nicht durch
einen SYSTEM-Installationslauf impliziert.

### Rückgabecodes

`0` bedeutet Erfolg. Die bisherige Intune-Konfiguration behandelt `3010`
als Soft-Reboot. Das Setup setzt in seinem Quellstand keinen eigenen
`/RESTARTEXITCODE`; ein tatsächlicher Neustartcode hängt von der
Inno-Setup-Konfiguration ab. Nicht von einem immer gelieferten `3010` ausgehen.
Weitere Fehler anhand des Setup-Logs prüfen.

Beta-27-Pakete hatten eingeschränkten Silent-Install und keinen
unbeaufsichtigten Uninstall; für neue Verteilung nicht verwenden.

[Installation](https://github.com/oa7779/AetherCockpit/blob/main/docs/INSTALLATION.md)
· [Paketierung](https://github.com/oa7779/AetherCockpit/blob/main/docs/DEVELOPMENT.md)
