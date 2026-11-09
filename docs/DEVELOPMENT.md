# Entwicklung und Paketierung

## Projektstruktur

| Pfad | Aufgabe |
|---|---|
| `src\SystemCockpit` | WPF-Cockpit, Windows-Abfragen, Sensoren, Verwaltung und lokaler Reportingclient |
| `src\AetherScreensaver` | Nicht erhöhter Schonerhost und nativer Windows-SCR-Starter |
| `src\AetherManager.Server` | Separate ASP.NET-Core-Verwaltungs-API |
| `src\AetherClient.Service` | Optionaler Windows-Dienst für Heartbeats, Berichte und feste Remote-Aufträge |
| `src\AetherOperations.Shared` | Gemeinsamer Werkzeugkatalog, sichere Prozessausführung und Verwaltungsprotokoll |
| `tests\SystemCockpit.Tests` | STA-Konsolenrunner mit Assertions und optionalen UI-/Live-Modi |
| `tests\AetherManager.Tests` | Hardwarefreier Konsolenrunner für Verwaltung und Clientfreigaben |
| `tests` | Isolierte Setup-, Recovery-, Uninstall- und Produkt-Smoke-Skripte |
| `build` | Publishskript, Inno-Setup-Definition, Iconerzeugung und Uninstallhilfe |
| `tools` | Originale Messwerkzeuge, Lizenztexte, Quellarchiv und Herkunftsmanifest |
| `docs` | Fachanleitungen ohne Versionsgeschichte |
| `dist` | Bestehende portable Builds, ZIPs, Setups und historische Intune-Ausgabe |

Der Workspace ist mit der bestehenden GitHub-Historie von
`oa7779/AetherCockpit` verbunden; Hauptbranch ist `main` mit
Upstream `origin/main`. Aktueller Arbeitsbranch und freigegebener Auftrag
stehen in der [Projektübergabe](../PROJEKTSTAND.md).
Vor Veröffentlichungen Status und Remote-Stand prüfen.
Keine Release-Verzeichnisse als Arbeitskopie verwenden.

## Voraussetzungen

- Windows x64 mit .NET 10 SDK.
- Für WPF-UI-Tests ein grafischer Desktop.
- Visual C++ x64 Build Tools und Windows SDK für den nativen `Aether.scr`.
- Inno Setup 6 für die Setup-Erzeugung.
- NuGet-Zugriff bei fehlenden SDK-/Runtimepaketen.

Der Server referenziert ASP.NET Core OpenAPI, WindowsServices und MSAL für
die optionale zertifikatsbasierte Microsoft-Graph-Anmeldung.
Die Cockpit-Abhängigkeit und mitgelieferten Messwerkzeuge sind unter
[Drittanbieter-Hinweise](../THIRD-PARTY-NOTICES.md) dokumentiert.
Keine Werkzeuge oder Lizenzen durch handgeschriebene Ersatzdateien ersetzen.

## Build

Vom Workspace-Stamm:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build .\SystemCockpit.slnx -c Release
```

Bei vorhandenen Restore-Ergebnissen kann `--no-restore` verwendet werden.
Ein Build startet keine produktive App und keine Sensoren.
[Tests und ihre Nebenwirkungen](TESTING.md) sind separat beschrieben.

## GitHub Actions

Der Workflow [Windows build](../.github/workflows/windows-build.yml) baut
auf `windows-2022` mit .NET 10 bei Pushes nach `main` und `ci/windows-build`,
bei Pull Requests nach `main`
oder manuellem Start. Er liest die Versionswerte aus dem Publishprofil,
führt beide Konsolenrunner ohne optionale Live-/UI-Schalter aus und erzeugt
Client, Server sowie Setup mit den vorhandenen Publishskripten.
Der STA-Grundlauf wird zusätzlich mit den publizierten Assemblies wiederholt.
Ein synthetischer Desktop-Smoke-Test prüft Windows Modern und erzeugt
System-Schema-Bilder für hell und dunkel als eigenes Screenshot-Artefakt.

ZIPs, Setup, SHA256-Manifest und Build-Herkunft werden als unsignierte
Workflow-Artefakte für 14 Tage gespeichert. Der Workflow erstellt keinen
GitHub-Release, installiert kein Produkt und enthält noch keine SignPath-
Anbindung. Der Desktop-Smoke-Test ersetzt nicht die vollständigen
grafischen UI-/Hardwareprüfungen oder die
Intune-Paketierung. SignPath-Annahme, Freigaberollen und Signing-Konfiguration
müssen vor einer Zertifikatsnutzung separat eingerichtet werden.
Der Antrag bei der SignPath Foundation ist eingereicht, die Entscheidung
steht aus. [Aktueller Signaturstatus](SECURITY-AND-PRIVACY.md#codesignierung).

## Versionsquelle

Nach Änderungen an AETHER auf Nutzerauftrag jeweils einen neuen lokalen Build
und Installer mit neuer Revision erstellen und prüfen. Bestehende Ausgaben
bleiben erhalten; eine Veröffentlichung oder Installation folgt daraus nicht.
Ausdrückliche Auftragsgrenzen gehen vor: Meilenstein 1 ist ohne Versions-
oder Paketänderung konsolidiert; siehe
[Abschlussbericht](MILESTONE-1-CONSOLIDATION-REPORT.md). Der separate
[Messmodus](PERFORMANCE-BASELINE.md) benötigt nur einen Releasebuild.

Die Release-Version steht in
`src\SystemCockpit\Properties\PublishProfiles\PortableBeta.pubxml`:
aktuell lokal `0.7.0-beta.44`, Dateiversion `0.7.0.44`,
Assembly-Version `0.7.0.0`.

Beta 44 ist ein lokaler Cockpit-Testinstaller mit gebündelten Datenträgerkarten,
stärkerem Neon-Glow, animiertem Branding, ursprünglicher Cockpit-Anordnung,
Wake-on-LAN und erweitertem Wartungskatalog,
kein veröffentlichter Release.
Beta 38 bleibt die öffentliche Version.
Die folgenden Beta-38-Pfade sind historische Paketierungsbeispiele; sie dürfen
nicht überschrieben werden. Neue Ausgaben verwenden die aktuelle Profilversion.

`build\AetherCockpit.iss` muss dazu passen: `AppVersion`, `PublishDir`,
`VersionInfoVersion` und `VersionInfoProductVersion`.
Das normale Projektbuild ohne Publishprofil setzt diese Releasewerte
nicht selbst. Nicht aus einer gewöhnlichen Build-DLL auf den
veröffentlichten Beta-Stand schließen.

Der Client meldet beim Enrollment die Assembly-Version, nicht automatisch
den vollständigen Beta-Tag. Die Dokumentation verspricht daher keine
Beta-genaue Geräteversionsanzeige.

## Publish mit Bildschirmschoner

```powershell
powershell -NoProfile -File .\build\Publish-Beta.ps1
```

Das Skript publiziert Cockpit und Schoner selbstenthalten für `win-x64`.
Es prüft, dass beide dieselbe Cockpit-DLL verwenden, übernimmt die
Schonerdateien und baut den nativen SCR-Starter mit Visual C++.
Der Host heißt in der Ausgabe `AetherMainboard.scr`, der Discovery-Starter
`Aether.scr`. Beide teilen die Cockpit-Runtime.

Der Ausgabeordner wird aus der Profilversion gebildet:
`dist\AetherCockpit-0.7.0-beta.38-win-x64`.
**Existiert er bereits, bricht das Skript ab.** Beta 38 liegt im aktuellen
Workspace bereits vor. Vorhandene Releases nicht überschreiben.
Für eine spätere Ausgabe Versionsänderung und neuen Zielpfad als eigenen
Release-Schritt planen. Der Publish kopiert den optionalen Clientdienst unter
`ManagedClient` und seine Einrichtungs-/Lebenszyklusskripte ins Paket.

Das separate selbstenthaltene Serverpaket erzeugen:

```powershell
powershell -NoProfile -File .\build\Publish-Manager.ps1
```

Ziel: `dist\AetherManager-0.7.0-beta.38-win-x64`. Auch dieses Skript verweigert
vorhandene Ausgaben. Die Dienstassistenten werden zur Buildprüfung nicht ausgeführt.

Nach Erzeugung des passenden Setups das Intune-Paket separat bauen:

```powershell
powershell -NoProfile -File .\build\Publish-Intune.ps1
```

Das offizielle Microsoft Win32 Content Prep Tool muss lokal unter
`tools\intune\IntuneWinAppUtil.exe` bereitstehen. Das Skript verwendet nur das
Setup als Quelle und verweigert vorhandene versionsbezogene Intune-Ausgaben.
[Deployment und Erkennungsregeln](INTUNE-DEPLOYMENT.md).

## ZIP, Setup und Prüfsummen

### Separater Server-Installer

Nach `build\Publish-Manager.ps1`:

```powershell
& .\build\Publish-ManagerSetup.ps1
```

Das Skript liest Version und Dateiversion aus dem bestehenden Publishprofil,
prüft die Serverbinärversionen und die Übereinstimmung der Dienstskripte mit
dem Quellstand und kompiliert `build\AetherManager.iss` mit Inno Setup 6.
Ziel ist `dist\AetherManager-Setup-<Version>-win-x64.exe` mit SHA256-Datei.
Vorhandene Installer werden nicht überschrieben; weder Installation noch
Upload werden ausgeführt. Die lokale Beta 39 ist noch nicht veröffentlicht.
Dienstfreigaben, Updategrenzen und Datenerhaltung stehen unter
[Zentrale Verwaltung](CENTRAL-MANAGEMENT.md#separater-server-installer-lokale-beta-39).

### Cockpit-Installer

Für einen neu erzeugten, noch nicht archivierten Release:

```powershell
$release = (Resolve-Path .\dist\AetherCockpit-0.7.0-beta.38-win-x64).Path
[System.IO.Compression.ZipFile]::CreateFromDirectory($release, "$release.zip")
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if (-not (Test-Path -LiteralPath $iscc)) {
    $iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
}
& $iscc .\build\AetherCockpit.iss
Get-FileHash "$release.zip" -Algorithm SHA256
Get-FileHash .\dist\AetherCockpit-Setup-0.7.0-beta.38-win-x64.exe -Algorithm SHA256
```

Die obigen Namen beschreiben Beta 38; vorhandene ZIP-/Setup-Dateien vorher
nicht überschreiben. `ISCC` schützt bestehende Ausgaben nicht wie das
Publishskript. Prüfsummen als `.sha256` mit Hash und Dateiname speichern.
ZIP-Einträge, Größen und SHA256 jeweils gegen den Publish-Ordner vergleichen.
Buildwerkzeuge und Laufzeitpakete beeinflussen Binärdateien; ein erneut
gebautes Paket ist nicht zwangsläufig bytegleich.

## Dokumentationsübernahme

Die Content-Einträge in `SystemCockpit.csproj` übernehmen README, Changelog,
Betatester-Anleitung, Fachdokumente, AETHER-Lizenz und Drittanbieter-Hinweise.
Das vorhandene Logo wird mit seinem relativen Pfad mitgeliefert.
Server-README und HTTP-Beispiele werden als Dokumentation übernommen,
nicht als installierter Server.

Inno Setup kopiert den Publish-Ordner rekursiv; ZIP archiviert ihn vollständig.
Änderungen an Quellen allein aktualisieren **keine bereits erzeugten
Release-Pakete**. Dokumentationsübernahme vor einer neuen Ausgabe statisch
prüfen. Historische `dist`-Dateien und deren Prüfsummen bleiben unverändert.

## Serverentwicklung

Serverstart, Listener, Token und Datenordner:
[Zentrale Verwaltung](CENTRAL-MANAGEMENT.md).
Der Server wird separat gebaut und gestartet; er ist kein automatisch
laufender Bestandteil der Desktopinstallation.

## Technische Invarianten

- Fehlende Sensoren bleiben fehlend; NVMe-Temperaturgrenzen sind keine Messwerte.
- GPU-Werte bleiben einem Gerät zugeordnet; Kanalidentitäten berücksichtigen
  doppelte Provider-IDs.
- Prozessdetails sichern PID mit Startzeit; Prozess-I/O bleibt von Disk-I/O getrennt.
- Geführte Änderungen behalten Vorschau, Bestätigung und Zustandsprüfung.
- Operations verwendet für SFC den vorhandenen UTF-16-Ausgabehelper;
  lange PowerShell-Skripte werden über Standardinput transportiert.
- Bei Operations-Journalfehlern Ausgabe weiter drainen und Folgeschritte stoppen.
- Paketaktionen verwenden vollständige IDs/Quellen; Import startet keine Installer.
- Einzelplatzmodus bleibt ohne Managerkontakt; Berichts- und Capturefreigaben
  sind voneinander unabhängig.

Im Workspace hält `PROJEKTSTAND.md` lokale Ausgaben und offene Arbeiten fest.
Die Übergabe wird nicht in Benutzerpakete übernommen.
