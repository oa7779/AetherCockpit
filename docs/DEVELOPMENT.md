# Entwicklung

## Voraussetzungen

- Windows x64 und .NET 10 SDK.
- Ein grafischer Desktop für WPF- und Interfaceprüfungen.
- Visual C++ x64 Build Tools und Windows SDK für native
  Bildschirmschonerkomponenten.
- NuGet-Zugriff zum Wiederherstellen fehlender Abhängigkeiten.

## Projektübersicht

| Pfad | Aufgabe |
|---|---|
| `src\SystemCockpit` | WPF-Cockpit, Windows- und Hardwareansichten |
| `src\AetherOperations.Shared` | Gemeinsam genutzte lokale Operations- und Intelligence-Komponenten |
| `src\AetherLocalAiSetup` | Separater Assistent für die qualifizierte lokale Modellinstallation |
| `src\AetherManager.Server` | Optionaler, separat betriebener Verwaltungsserver |
| `src\AetherClient.Service` | Optionaler Clientdienst für den getrennten Verwaltungsablauf |
| `src\AetherScreensaver` | Optionaler Windows-Bildschirmschoner |
| `tests` | Konsolenrunner und gezielte Prüfungen |
| `build` | Build- und Portable-Publishskripte |

## Build

Vom Repository-Stamm:

```powershell
dotnet build .\SystemCockpit.slnx -c Release
```

Einzelne Projekte lassen sich direkt mit `dotnet build` bauen. Für
Prüfmodi, UI-Voraussetzungen und deren Grenzen siehe
[Tests](TESTING.md).

## Separater Portable-Publish

Das Publishskript verlangt einen neuen Ausgabepfad. Bestehende
`dist`-Ausgaben nicht überschreiben:

```powershell
$outputRoot = Join-Path $env:LOCALAPPDATA ("AETHER\Builds\dev-" + [guid]::NewGuid().ToString("N"))
$publishDirectory = Join-Path $outputRoot "publish"
.\build\Publish-Beta.ps1 -OutputDirectory $publishDirectory
```

Der Befehl erzeugt ein lokales Buildartefakt, veröffentlicht nichts und
erstellt keinen Installer. Releasewerte und Paketinhalt vor einer
Weitergabe gesondert prüfen.

## Dokumentation und Paketinhalt

Die Desktopprojektdatei legt die mit dem Portable-Paket ausgelieferten
Dokumente fest. Interne Übergaben, lokale Pfade und
Releasevorbereitungen gehören nicht in ein öffentliches Paket. Vor einem
Publish sowohl die ausgewerteten Projektdateien als auch
das fertige Ausgabeverzeichnis prüfen.

## Drittanbieter

Abhängigkeiten, Werkzeuge und Lizenzbedingungen sind in
[Drittanbieterhinweisen](../THIRD-PARTY-NOTICES.md) dokumentiert. Keine
Werkzeuge oder Lizenztexte durch eigene Ersatzdateien austauschen.
