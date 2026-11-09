# Tests

## Technische Konsolidierung

Der STA-Grundlauf enthält zusätzliche Quellen-/Ergebnis-/Datenschutzassertions
und isolierte Prozessfixtures. Diese starten ausschließlich denselben
.NET-Testhost, keine Windows-Verwaltungswerkzeuge. Geprüft werden die exakte
Ausgabegrenze, Timeout, Abbruch, Start-Progressfehler und Workerfreigabe.
Der interne Schalter `--command-fixture` ist kein Produktstartmodus.

Der Verwaltungsrunner protokolliert bei einem Fehler Zeitpunkt, Testphase,
Fixturepfad und HRESULT zusätzlich zur Exception. Die bekannte intermittierende
Ausgangsabweichung bleibt im
[Konsolidierungsbericht](MILESTONE-1-CONSOLIDATION-REPORT.md) dokumentiert;
ein erfolgreicher Folgelauf erklärt ihre Ursache nicht.

Eine separat aufgerufene synthetische Messung bietet
`--consolidation-baseline` mit absolutem, noch nicht vorhandenem JSON-Ziel.
Sie ist kein Teil des Grundlaufs und kein Leistungsfreigabegate.
[Methode, Messwerte und offene Produktmessungen](PERFORMANCE-BASELINE.md).
Der geplante sprachabhängige RC-Nachfolger ist in der
[Sprachstrategie](RC-LANGUAGE-STRATEGY.md) beschrieben; das aktuelle
Englischgate bleibt unverändert.

## Freigabeschranke Englisch

Englisch bleibt deaktiviert, bis Quellstellen und datenflussabhängige
Anzeigen geprüft und beide Oberflächen zur Laufzeit kontrolliert sind.
Die Bestandsaufnahme ist nicht vollständig und kann die manuelle Prüfung
nicht ersetzen. Für eine englische Veröffentlichung muss zusätzlich diese
Schranke bestehen:

```powershell
python .\tests\desktop_localization_audit.py --require-complete
python -m unittest discover -s .\tests -p test_desktop_localization_audit.py
```

Die Schranke schlägt bei fehlenden Ressourcen, offenen Textstellen,
ungeprüften Anzeigen oder ausstehender Gesamtprüfung fehl. Ohne
`--require-complete` bleibt die Bestandsaufnahme für Entwicklungsstände
ausführbar und bedeutet keine Freigabe. Der Windows-Buildworkflow führt
die Schranke bei Versionsprofilen mit `-rc.` vor dem Build aus; Betaprofile
werden dadurch nicht blockiert.

Der Windows-CI-Lauf prüft außerdem die synthetischen Professional-,
Katalog- und Sensorverlauf-Oberflächen. Die Professional-Prüfung
erzeugt getrennte Workflow-Screenshots mit Testdaten. Die teilweise englische
Funktionssuche enthält weiterhin deutsche Katalogdaten; ihr Screenshot ist
keine Freigabe der englischen Oberfläche. Die separate `--board-ui`-Prüfung
setzt aktivierte Windows-Animationen voraus und läuft nicht auf dem
CI-Runner mit reduzierter Bewegung; diese Systemeinstellung wird nicht geändert.

## Serverentwicklungsbranch

[Windows-CI 42504760899](https://github.com/oa7779/AetherCockpit/actions/runs/42504760899)
auf `20b0ae4` bestand 364 Verwaltungsassertions einschließlich
156 HTTPS-/Rollen-/CSRF-/Sitzungsprüfungen, 150 lokaler Kontenprüfungen
und 14 Flotten-/Telemetrieprüfungen zusätzlich zu den bestehenden 44.
Die Browserprüfung bestand mit 48 synthetischen Geräten und zwölf
Screenshots. Kein Clientexecutor oder Windows-Verwaltungsauftrag wurde
ausgeführt. Der Stand liegt auf `feature/professional-server`, nicht im
veröffentlichten Beta-38-Release.

## Prüfstatus Beta 38

[Windows-CI 42493583268](https://github.com/oa7779/AetherCockpit/actions/runs/42493583268)
prüfte Quellcommit `4208eb5`: Build ohne Warnungen/Fehler,
3884 Grundassertions, 44 Verwaltungsassertions, 3048 Ressourcen- und
Kompatibilitätsassertions sowie 3381 deutsche Modern-/Neon-Smoke-Assertions
bestanden. Der Grundlauf bestand zusätzlich mit den publizierten Assemblies.
22 synthetische Hell-/Dunkelbilder wurden erzeugt; die Bildkontrolle führte
zu Korrekturen von Diagrammen, Auswahlfeldern und Katalogkontrasten.
Diese Tests führen keine Reparaturen, Installation oder Hardwareinitialisierung
aus. Hardwarebetrieb und Intune-Rollout sind separat zu prüfen.
Die lokale Code-Integrity-Sperre wurde nicht verändert.

## Prüfstatus Beta 37

Der Versionsbuild ist ohne Warnungen oder Fehler abgeschlossen. Windows
Code Integrity blockiert den neuen Testlauf mit `0x800711C7` (Ereignis 3077:
Testassembly erfüllt die Enterprise-Signaturrichtlinie nicht).
Beta 37 wird auf Nutzerauftrag als ungeprüfte Vorabversion veröffentlicht.
Die neuen Programmpakete sind nicht laufzeitgeprüft; Schutzrichtlinien
bleiben unverändert.

Vor dem Versionsbuild bestand die Schwarz-/Blau-Korrektur 1455 Assertions
mit Interface-, Board- und Schoner-UI. Die zugehörigen Screenshots zeigen
diesen früheren Teststand, nicht einen neuen Pakettest.

Der separate
[GitHub-Actions-Lauf 42420444909](https://github.com/oa7779/AetherCockpit/actions/runs/42420444909)
prüfte Quellcommit `913181a` auf `windows-2022`: Build ohne Warnungen/Fehler,
836 STA- und 44 Verwaltungsassertions bestanden. Weitere 836 STA-Assertions
bestanden mit den dort publizierten Assemblies. ZIPs und Setup wurden als
unsignierte CI-Artefakte erzeugt. Der Lauf umfasst weder grafische UI-Tests
noch Installation oder Intune; er prüft nicht die zuvor lokal erzeugten
Beta-37-Pakete und hebt die lokale Code-Integrity-Sperre nicht auf.

## Testplattform

Der Serverentwicklungsbranch ergänzt Konten-, Rollen-, HTTPS-, Sitzungs-
und Flottenprüfungen im vorhandenen Verwaltungsrunner. Die Browserprüfung
verwendet 48 synthetische Geräte ohne Clientexecutor.
`build\Verify-ManagerBrowser.ps1` ist für den isolierten Windows-CI-Runner
mit `RUNNER_TEMP` vorgesehen. Playwright-Abhängigkeiten stehen im
Testprojekt; Chromium wird ausschließlich als Testwerkzeug installiert.
Fixture-Zugangsdaten und Datenordner werden nicht als Artefakte hochgeladen.
Screenshots verwenden Testdaten, keine produktive Flotte.

`tests\SystemCockpit.Tests` ist ein **STA-Konsolenrunner**, kein
`dotnet test`-Projekt. Er erzeugt die WPF-Ressourcen des Produkts und führt
Assertions aus; Fehler beenden den Prozess mit Exitcode 1.
Die Anzahl hängt von den gewählten Modi ab.

Voraussetzung: Windows x64, .NET 10 SDK und für UI-Modi ein grafischer Desktop.
Ohne optionale Schalter werden Parser, Modelle, Grenzen, Konfiguration,
Sicherungsabläufe und Dateioperationen mit Testdaten geprüft.
Cleaner-/Speicher-Fixtures verwenden eigens angelegte temporäre Dateien.
Der Grundlauf liest außerdem die lokale Registry-Liste installierter Software;
er initialisiert keine Hardware-Sensoren.

```powershell
dotnet run --project .\tests\SystemCockpit.Tests -c Release
```

`tests\AetherManager.Tests` ist ein zusätzlicher Konsolenrunner für Anmeldung,
Richtlinien, einmalige Auftragsübernahme, Ergebnisgrenzen, Persistenz,
Gerätesperre, Client-URL-Prüfung und synthetische Microsoft-Graph-Anfragen.
Er verwendet Testtoken und einen gefälschten HTTP-Handler, benötigt keinen
Tenant und startet keine Windows- oder Graph-Verwaltungsaktion:

```powershell
dotnet run --project .\tests\AetherManager.Tests -c Release
```

Der Verwaltungsrunner startet zusätzlich eine temporäre Loopback-HTTPS-API
mit einem selbstsignierten Testzertifikat im Fixtureordner. Nur der Testclient
pinnt dieses konkrete Zertifikat; der normale Client muss es ablehnen.
Kein Zertifikat wird in Windows installiert.
`tests\Verify-ServiceAcl.ps1` prüft fünf synthetische ACL-Fälle ohne
Dienstregistrierung oder Änderung realer Dateirechte.

`tests\Verify-IntunePackage.ps1` prüft das erzeugte `.intunewin` ohne Installation:
Containerstruktur, MAC, IV, SHA256 des entschlüsselten Quell-ZIPs und
SHA256 des einzigen darin enthaltenen Setups müssen stimmen.

```powershell
powershell -NoProfile -File .\tests\Verify-IntunePackage.ps1 -Package .\dist\intune\0.7.0-beta.38\AetherCockpit-Setup-0.7.0-beta.38-win-x64.intunewin -Setup .\dist\AetherCockpit-Setup-0.7.0-beta.38-win-x64.exe
```

Die Prüfung verwendet einen temporären Fixtureordner und entfernt ihn danach.
Eine tatsächliche Intune-Verteilung wird damit nicht durchgeführt.

`tests\Verify-ManagerApi.ps1` prüft eine eigens gestartete Loopback-API mit
synthetischen Geräten, Berichten und Ergebnissen. Es erzeugt Auftragsdatensätze,
führt sie aber nicht aus. Nur einen isolierten `manager-validation`-Datenordner
verwenden, keinen produktiven Server. Dienstinstallation, reale Reparaturen,
Updates, Treiberwechsel und Firewalländerungen sind keine Routineprüfungen.

## Gezielte UI-Prüfungen

```powershell
dotnet run --project .\tests\SystemCockpit.Tests -c Release -- --professional-ui --catalog-ui --board-ui --sensor-charts-ui
```

| Schalter | Umfang |
|---|---|
| `--professional-ui` | Suche, Zielrouten, Expertensperre, Paketvorschau, synthetische Netzwerkfilter, Cleaner-Auswahl |
| `--catalog-ui` | Katalog, Parameterbedienung, Enter-Routing und Entfernen sensibler Konsoleninhalte |
| `--board-ui` | Synthetische Mainboardgeometrie, Auswahl und Darstellung |
| `--gpu-presentation` | Drei GPU-Lüfter im selben projizierten Kühlflächenraum und ohne Überdeckung durch spätere Komponenten, jeweils bei Tag und Nacht; unabhängig von Windows-Animationen |
| `--sensor-charts-ui` | Sensorverläufe und Lücken mit Testdaten |
| `--xbar-ui` | Diagrammleiste und Anordnung |
| `--branding-ui` | Dekodiertes Logo und sichtbare Abmessungen beim Start/im Dashboard, Chromschriftzug, Animationsfreigaben und Stopp bei Unsichtbarkeit, Layout und xBar-Glow; abgelöste UI ohne Produktdienste |
| `--interface-ui` | Aufgabengruppen und Zielrouten, ursprüngliche Cockpit-Anzeigen ohne zusätzliche Detailklappe, synthetische Vier-Reihen-Datenträgerkarten und Mindestfensterbreite sowie Mainframe, GPU-Lüfterebene, Tag-/Nachtgrenzen und Sekunden-Takt |
| `--desktop-ui` | Gadgets und Desktopparameter |
| `--desktop-language-smoke` | Ressourcenbasierte Desktop-Sprachtexte und Darstellung |
| `--desktop-modern-smoke` / `--desktop-ui-smoke` | Deutsche Windows-Modern-Darstellung in hell/dunkel, System-Schema, Dashboard, Systemkontrolle, Desktopparameter, xBar, Gadget, Start und Aktionsbestätigung; Neon-Kompatibilität |
| `--screensaver-ui` | Synthetische Schonerszene ohne Hardwareinitialisierung |
| `--inventory-ui` | Inventarfenster mit kontrollierten Testdaten |

Optionale Screenshots verwenden `--board-screenshot`, `--sensor-charts-screenshot`,
`--screensaver-screenshot`, `--inventory-screenshot` mit einem Zielpfad.
`--professional-screenshot-prefix` benennt mehrere Workflow-Bilder.
`--branding-screenshot-prefix` erzeugt Boot-, Dashboard- und xBar-Bilder.
`--interface-screenshot-prefix` erzeugt Mainframebilder im Tag-/Nachtmodus
und ein Cockpitbild mit zwei synthetischen Datenträgern (`-disks.png`).
Ein optionaler Pfad direkt nach `--gpu-presentation` erzeugt Tag-/Nachtbilder
mit diesem Präfix. Dieser spezielle Schalter läuft eigenständig und wird auch
in der Windows-CI ausgeführt.
Für `--desktop-modern-smoke` legt `AETHER_SCREENSHOT_DIR` den Screenshotordner fest;
die 22 Bilder heißen `windows-modern-{light|dark}-{Bereich}.png`. Das Cockpit
wird in seiner ursprünglichen Anordnung ohne vorgeschaltete Übersicht erfasst.
Die System-Schema-Bilder verwenden den Bereich `system-schema`.
Dabei werden keine Sensoren,
Reparaturen oder Produktinstallationen gestartet.
Die Sekundenprüfung beobachtet sechs Sekunden lang Property-Änderungen
ohne Sensorzugriff. Nach dem ersten Intervall gilt maximal 1,5 Sekunden Abstand.
`--board-render-profile verify` prüft bei synthetischer WPF-Komposition
ein p95-Budget von 33,4 ms; `4k` fordert ein großes Fenster an und meldet
die tatsächlich erreichte Größe. Diese Callbackzeiten sind keine gemessenen
GPU-Present-FPS. Der Schoner-UI-Test rendert zusätzlich in ein echtes
3840×2160-Bildziel und prüft Hochkant-, Ultrawide- und 8K-Layout.
Screenshots mit realen Namen und Pfaden vor Weitergabe prüfen.

## Lesende lokale Prüfungen

Diese Modi sind nicht vollständig synthetisch:

| Schalter | Zugriff |
|---|---|
| `--live` | Echte Windows-Zustände |
| `--inventory` | Hardware-/Softwareinventar |
| `--board-live` | PnP, Monitore, Audioendpunkte und Laufwerksbusdaten |
| `--tools` | Toolversionen, PowerShell-Transport, eigene Sleep-Prozesse, Encoding und Offline-Zertifikate |
| `--winget-live` | Tatsächliche `winget list/upgrade`-Ausgabe; keine Paketänderungen |
| `--dashboard-ui` | CPU-, Prozess-, Speicher-, Ereignis- und Disk-Zähler |
| `--admin-ui` | PowerShell-Parser und Statusabfragen; keine Anwendung der Verwaltungspläne |
| `--operations-ui` | Skriptparser, lokale COM-Fixtures, gespeicherte WinSAT-/Bereitschaftsdaten und Planvorschau |

```powershell
dotnet run --project .\tests\SystemCockpit.Tests -c Release -- --operations-ui --admin-ui
```

Windows-Features können schon zum Lesen einen erhöhten Host benötigen.
`--operations-ui` startet keine echte Updatesuche, Reparatur,
Remoteverbindung, Wiederherstellungspunkt oder Benchmark.
Für reine Dokumentationsänderungen sind diese Systemabfragen nicht erforderlich.

## Hardware- und Produktstart

Nur nach Freigabe für tatsächlichen Hardware-/Treiberzugriff:

```powershell
dotnet run --project .\tests\SystemCockpit.Tests -c Release -- --hardware --boot-ui
pwsh -File .\tests\Smoke-UI.ps1 -EnableSensors
```

`--hardware` öffnet den Hardwareprovider. `--boot-ui` prüft echte
Sensorinitialisierung, Bootjournal und Cockpitstart im Testhost.
`Smoke-UI.ps1` startet die Produkt-EXE und benötigt eine administrative
PowerShell, weil die Automation UAC nicht selbst bestätigen kann.
Auch ohne `-EnableSensors` ist der normale Produktstart nicht als
hardwarefreie Dokumentationsprüfung zu behandeln.

`--screensaver-process <SCR-Pfad>` und `--screensaver-launcher <SCR-Pfad>`
testen einen realen Vorschauprozess. `--screensaver-fullscreen <SCR-Pfad>`
startet echten Vollbildmodus; nur separat und mit freigegebenem Testprofil
verwenden. `--feed-server`/`--feed-client` sind interne Pipe-Testmodi.

## Renderdiagnose

```powershell
dotnet run --project .\tests\SystemCockpit.Tests -c Release -- --board-render-profile verify
```

Dieser frühe Runnerzweig startet eine synthetische animierte Vorschau,
keine Sensoren. Er misst vier Sekunden WPF-Komposition.
Ohne Folgemodus entstehen zusätzlich sechs 4K-Software-Snapshots.

- `verify`: p95 höchstens 33,4 ms im 1280×720-Testfenster.
- `no-rays`: Rasteranteil isolieren.
- `4k`: 3840×2160-Testfenster anfordern.

Die drei Modi lassen Software-Snapshots aus. Windows kann Fenstergrößen
begrenzen; die tatsächliche Inhaltsgröße wird ausgegeben.
**Der Wert misst WPF-Kompositionsabstände, keine tatsächlich am Monitor
ausgegebenen GPU-FPS und keinen Hardwarebenchmark.**

## Setupregressionen

```powershell
powershell -NoProfile -File .\tests\Verify-Setup.ps1
```

Der Test kompiliert denselben Installer in einer isolierten Ausgabe ohne
Produktinstallation. Er prüft Profile, Aufgaben-XML, Zustimmungen,
Silent-Modi und Updates. Standardmäßig wird keine Aufgabe registriert
oder gelöscht und die App nicht gestartet.
Die getrennte Npcap-Downloadoption darf keine Capturezustimmung verlangen;
automatischer Mitschnitt dagegen schon.

Weitere, separat freizugebende Prüfungen:

- `Verify-Setup.ps1 -RegisterIsolatedTask`: registriert und entfernt eine
  eigene deaktivierte Aufgabe ohne Logontrigger.
- `Verify-Recovery.ps1 -PreviousRelease <Pfad>`: prüft Restordner mit
  kopierten Produktdateien und temporärer Junction. Die Vorgabe Beta 17
  ist im aktuellen `dist` nicht vorhanden; einen bestehenden Release
  gesondert angeben, statt den Test umzuschreiben.
- `Verify-Uninstall.ps1`: erzeugt und deinstalliert eine isolierte
  Testinstallation mit eigenen Registrierungs-/Dateifixtures.

Diese Tests verändern ihre Testumgebung und sind keine notwendigen
Prüfungen einer Dokumentationsbereinigung.

## Paketprüfung

Bei einer neuen Ausgabe Dateiliste, Größen und Hashes sämtlicher
ZIP-Einträge gegen den Publish-Ordner prüfen. Setup-/ZIP-SHA256 mit den
Sidecars vergleichen. Gegen veröffentlichte DLLs nur einen versionsgleichen
Testhost verwenden; Testdateien in einem separaten Prüfverzeichnis halten.
Die Releaseordner nicht als Testarbeitsbaum verändern.

## Nicht als Routineprüfung starten

Reparaturen, reale Paket-/Windows-Updates, SMART-Selbsttests, LAN-/WLAN-Scans,
Mitschnitte, Explorer-/Firmware-Neustarts, Schlüsselabfragen und produktive
Autostartänderungen verlangen einen eigenen Testauftrag.
Vorhandene Fixtures prüfen deren Befehle und Grenzen, nicht den Erfolg
jeder tatsächlichen Windows-Aktion.

Für Dokumentation reichen Link-/Text-/Manifestprüfung und bei geänderten
eingebetteten Texten ein Build sowie passende hardwarefreie Assertions.
[Paketierung](DEVELOPMENT.md).
