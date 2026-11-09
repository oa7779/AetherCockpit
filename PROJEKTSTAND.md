# AETHER — Projektübergabe

## Stand

RC-Entwicklung beauftragt: Desktop und professioneller Server gemeinsam,
vollständige englische Oberfläche, praktische Installations-/Netzwerkprüfung
in einer noch abzustimmenden isolierten Testumgebung und anschließend
GitHub-Prerelease bei bestandenen Prüfungen. Kein RC veröffentlicht.
Arbeitsstand: Diagnoseexport und Speicherdialog, Oberfläche der
Funktionssuche, Ereignisbrowser-Meldungen, Sensor-Konfliktwarnung,
Speedtest-Meldungen, Frame-CSV-Validierung und Messlog-Auswertung für
Englisch vorbereitet. Die Desktop-Zentralverwaltung unterscheidet nun
ausstehende Freigabe, gesperrtes Gerät und unbekannten Zustand; nur ein
explizit freigegebenes Gerät sendet Berichte. Titel in der Funktionssuche
sollen auch bei schmalem Fenster umbrechen; diese UI-Änderung ist noch
nicht laufzeit- oder bildgeprüft.
Deutsche Ausgabe, Schwärzungsregeln, Ablauf-IDs und Aktionsargumente bleiben
erhalten. Die 25 geführten Arbeitsabläufe in der Funktionssuche haben nun
englische Titel, Wirkungen, Anweisungen, Suchbegriffe und Routenbezeichnungen;
interne Routen bleiben unverändert. Die zusätzlich eingebundenen
Befehlskatalogeinträge enthalten weiterhin deutsche Beschreibungen.
Englisch bleibt deaktiviert: Die Quellbestandsaufnahme zählt noch 824 nicht übersetzte
Textstellen und beweist keine vollständige semantische Abdeckung.
Eine explizite englische Freigabeschranke wird für `-rc.` im Windows-CI
ausgeführt und blockiert diesen CI-Build bis zur vollständigen Prüfung.
Eine manuelle GitHub-Veröffentlichung verhindert sie nicht. Lokal:
Aktueller Releasebuild ohne Warnungen/Fehler und 364 Serverassertions
bestanden. Die neuen Desktop- und UI-Tests konnten lokal nicht laufen:
Windows-Anwendungssteuerung blockiert AetherScreensaver.dll mit
0x800711C7. Die Schutzrichtlinie wurde nicht verändert. Vor dieser letzten
Änderung bestanden 4054 Desktop-/Professional-UI-, 3194 Sprach- und
3527 Modern-/Neon-Assertions. Keine Windows-Aktion oder Installation
ausgeführt.

Für die Prüfung hat der Nutzer den separaten öffentlichen Testzweig
`test/rc-localization` und GitHub-Windows-CI freigegeben, keinen Merge oder
Release. CI-Lauf 42571561513 auf `3547c16` baute erfolgreich und bestand
Grund-, Sprach-, Server- und synthetische Modern-Tests; die zusätzliche
Board-UI-Prüfung schlug fehl, weil sie aktive Windows-Animationen erwartet.
Der CI-Runner verwendet reduzierte Bewegung. Die Systemeinstellung bleibt
unverändert; die separate Board-UI-Prüfung bleibt offen.
Lauf 42571925970 prüfte anschließend den Arbeitsplatztitel-Umbruch und
deckte auf, dass der implizite Textstil vom Listentemplate nicht übernommen
wurde. Das korrigierte explizite Texttemplate bestand in
[CI 42572283885](https://github.com/oa7779/AetherCockpit/actions/runs/42572283885)
auf `bc9939c`: Build ohne Warnungen/Fehler, 4060 Grund- und publizierte,
3224 Sprach-, 3557 Modern-/Neon-, 4098 Professional-/Katalog-/Sensor-UI-
und 364 Serverassertions; Browserprüfung mit 48 synthetischen Geräten.
Neon-Arbeitsplatz und Paketansicht visuell kontrolliert. Der englische
Arbeitsplatz zeigt weiterhin deutsche Katalogdaten; Englisch bleibt gesperrt.
Die CI-Artefakte tragen noch Beta-38-Versionsnummern und sind kein RC.
Die RC-Sprachschranke wurde im Betaprofil erwartungsgemäß nicht ausgeführt.
Installation, produktive Geräte und die Board-Animation bei aktivierter
Windows-Bewegung wurden nicht geprüft.

Neuer Serverauftrag: Zielgruppe Administratoren und Semi-Administratoren
kleiner/mittlerer Infrastrukturen. Professionelle Zentralverwaltung auf der
bestehenden HTTPS-/Clientbasis; animierte Ansicht für 40+ Geräte, lesende
Serverüberwachung, Neon als Standard und neutrale Windows-Modern-Alternative.
Der Nutzer hat lokale AETHER-Konten mit Administrator-/Operator-/Leserrollen
gewählt. Keine freie Fernkonsole; zentrale Aktionen bleiben im kontrollierten
Katalog mit bisherigen Geräte- und Änderungsfreigaben. Desktopfunktionen
ohne sichere Remoteanbindung werden nicht als zentral ausführbar ausgegeben.
Implementierung, Rollen- und Flottenprüfungen laufen; noch keine neue Version
oder Behauptung einer produktionsfertigen Netzwerkverwaltung.
Der freigegebene separate Testbranch `feature/professional-server` ist
hochgeladen; `main` und Beta 38 bleiben unverändert.
[Windows-CI 42504760899](https://github.com/oa7779/AetherCockpit/actions/runs/42504760899)
auf `20b0ae4` erfolgreich: null Buildwarnungen/-fehler,
364 Verwaltungsassertions (44 bestehende, 156 HTTPS/Konten/Rollen/CSRF/
Sitzungen, 150 lokale Konten, 14 Flotte/Telemetrie), Browserprüfung mit
48 synthetischen Geräten und zwölf Screenshots bestanden.
Desktopregression: 3884 Grund-/publizierte Assertions, 3048 Ressourcen,
3381 Modern-/Neon-Smoke bestanden. Keine Windows-Aktion ausgeführt.
Neon-Flotte, Modern hell, mobile Ansicht und Operatoransicht visuell gelesen.
Konten verwenden Identity-Kennworthash; Sitzungen sind zeitlich begrenzt,
HttpOnly/SameSite Strict und mutationsseitig CSRF-geschützt.
Nach Bootstrap ist der alte Admin-API-Zugang standardmäßig aus; explizite
Kompatibilität nur für Nicht-Browser-Werkzeuge. Bootstrap/Migration muss
vor produktiver Übernahme geplant werden.
Einzelserver-JSON-Persistenz, keine HA oder storeübergreifende Transaktionen,
keine MFA oder vollständige Übernahme aller Desktopfunktionen.
Noch kein Merge nach `main` oder neuer Release.

Beta 38 veröffentlicht: Windows Modern zunächst deutsch,
Retro-Neon bleibt Standard. Englisch-Auswahl entfernt, Ressourcen als
Entwicklungsstand erhalten. Neutrale Hell-/Dunkelansichten, animiertes
System-Schema, Schoner-Sperre und bestätigter Neustart implementiert.
Windows-CI 42493583268 auf `4208eb5` erfolgreich: Build ohne Warnungen/Fehler,
3884 Grund-/publizierte Assertions, 44 Verwaltung, 3048 Ressourcen,
3381 Modern-/Neon-Smoke-Assertions. 22 synthetische Desktopbilder erzeugt.
Mehrere visuelle Durchläufe korrigierten echte Neonreste und Kontrastfehler.
Keine lokale Schutzrichtlinie verändert; keine Hardware-/Installationstests.
Der Nutzer hat getrennte Beta-38-Pakete mit Setup, Portable, Server,
Intune und Screenshots beauftragt. Pakete stammen aus diesem Windows-CI-Lauf;
SignPath-Annahme steht aus, Dateien bleiben unsigniert.
Veröffentlicht am 08.11.2026 um 16:10:04 UTC (17:10:04 lokal):
[Beta 38](https://github.com/oa7779/AetherCockpit/releases/tag/v0.7.0-beta.38).
Prerelease, Beta 36 bleibt Latest. Tag und getesteter Paketquellcommit:
`4208eb5f5923cd8511ff9ee431ff56e3f179084b`.
33 Release-Assets mit Dateigröße und GitHub-SHA256-Digest gegen lokale
Dateien verifiziert: vier Pakete, vier Einzelprüfsummen, Gesamtmanifest,
Intune-Anleitung, Build-Herkunft und 22 Screenshots.
Intune MAC/IV, entschlüsselter ZIP-SHA256 und byteidentisches Setup geprüft.
2691 vor diesem Import vorhandene dist-Dateien unverändert erhalten;
die zuvor fehlende ältere Historie bleibt weiterhin ungeklärt.
Setup-Dateiversion und beide Desktopassemblies 0.7.0.38.
README und Repositorybeschreibung aktualisiert; Galerie zeigt Bilder direkt
aus dem Repository. GitHub-README-Blob gegen lokalen Git-Blob geprüft.
Paketdokumente entsprechen dem CI-Buildzeitpunkt; GitHub-Dokumentation enthält
die abschließenden Prüfergebnisse. Arbeitsbranch nach Fast-forward `main`.

Neuer Auftrag am 08.11.2026: zusätzlicher Desktopmodus „Windows Modern“
mit neutralen Oberflächen und Hell-/Dunkelwahl nach Windows-Einstellung.
Retro-Neon bleibt Standard. Im Modern-Modus heißt das Mainframe
„System-Schema“, erhält eine sachliche Schemazeichnung und bietet keinen
Bildschirmschoner. Deutsch bleibt Standard; Englisch soll alle vom Desktop-
Cockpit erzeugten Oberflächentexte einschließlich Dialogen, Gadgets, xBar
und Assistenten umfassen. Externe Werkzeugausgaben bleiben im Original.
Der Nutzer hat einen bestätigten App-Neustart nach Design-/Sprachwahl
gewählt. Serveroberfläche und Installer werden später umgestellt.
Die Umsetzung dieses Auftrags ist im obigen Beta-38-Stand zusammengefasst.
Nach der Übersetzungsprüfung hat der Nutzer den Umfang geändert:
zuerst Windows Modern vollständig prüfen und veröffentlichen. Die
unvollständige Englisch-Auswahl darf noch nicht angeboten werden.
Sprachressourcen bleiben Entwicklungsarbeit; die freigegebene Oberfläche
bleibt zunächst deutsch. Die synthetische Modern-Laufzeitprüfung ist inzwischen bestanden.

Vorheriger, nicht als Release veröffentlichter Beta-37-Stand: Nacht-Hintergründe schwarz
statt blau/violett, Tag-Hintergründe blau. Hauptfenster, Mainframe-Himmel,
Rastergrund, Binärberge und Schonerhintergrund angepasst; farbige Neonlinien
und Glow bleiben erhalten. Der Nutzer hat alle Pakete und GitHub-Aktualisierung
beauftragt. Neue Ausgaben getrennt als 0.7.0-beta.37, Dateiversion 0.7.0.37.
Releasebuild ohne Warnungen/Fehler; 1455 Assertions mit Interface-, Board-
und Schoner-UI bestanden. Schwarz als exakter Farbwert in Hintergründen
geprüft; Tag-/Nacht- und Schonerbilder visuell kontrolliert.
Beta-37-Versionsbuild ebenfalls ohne Warnungen/Fehler. Der neue Testlauf
ist durch Windows Code Integrity blockiert: 0x800711C7, Ereignis 3077,
fehlende Enterprise-Signaturfreigabe für SystemCockpit.Tests.dll.
Keine Sicherheitsrichtlinie geändert oder umgangen. Nach anfänglichem Stopp
hat der Nutzer die Veröffentlichung als ungeprüfte Beta bestätigt.
Paketierung, Commit und GitHub-Prerelease werden mit deutlichem Prüfstatus
ausgeführt; keine erneute Ausführung der blockierten Testassembly.
Dokumente und Versionsquellen sind lokal auf Beta 37 vorbereitet; veröffentlichte
Beta 36 unverändert. 7826 historische dist-Dateien vor Paketierung gehasht.

Beta-37-Client-/Serverordner, Setup und Intune am 07.11.2026 lokal erstellt.
Setup-Dateiversion 0.7.0.37; Intune-MAC, IV, entschlüsselter ZIP-SHA256 und
identisches eingebettetes Setup geprüft. Keine Laufzeitprüfung der neuen Pakete.
Drei Bilder aus dem zuvor bestandenen Schwarz-/Blau-UI-Lauf unter
docs/screenshots/beta37-* kopiert. README und Paketdokumente kennzeichnen
die Ausgabe als ungeprüfte Vorabversion.
Der historische Dateiabgleich wurde unterbrochen: 6266 Einträge der
7826-Dateien-Basis fehlen inzwischen, mehrere ältere dist-Ordner sind
nicht mehr vorhanden. Ursache nicht geklärt; in diesem Durchlauf nicht gelöscht.
Keine Behauptung unveränderter vollständiger Historie möglich.
Lokal noch kein Beta-37-ZIP oder GitHub-Release. Der aktuelle Quellstand
ist inzwischen auf dem separaten CI-Branch veröffentlicht (siehe unten).
Der Nutzer hat auf die Nachfrage zur Historie mit dem Auftrag umgelenkt,
ein kostenloses SignPath-Zertifikat zu beschaffen. SignPath-Antragsformular
im Browser geöffnet, offizielle Bedingungen geprüft; nichts eingereicht.
Kostenloses OSS-Signing verlangt Annahme durch die Foundation, nachweisbare
Build-Herkunft und Projekt-Reputation. Kein Zertifikat erhalten und keine
Signierung konfiguriert. Signatur allein belegt keine Freigabe durch die
vorhandene Windows-Enterprise-Richtlinie.

SignPath-Antrag im Browser als Entwurf ausgefüllt; keine Einreichung oder
Zustimmung zu Vertragsbedingungen. Der Nutzer hat zuerst GitHub Actions
beauftragt. Neuer Windows-Buildworkflow verwendet bestehende Konsolenrunner,
Publishskripte und Inno Setup; unsigned Artefakte mit Hashmanifest und
Repository-/Commit-/Run-Herkunft. Keine SignPath-Signierung oder Release-
Automatik. Der Workflow-Grundlauf ersetzt die grafischen UI-Tests nicht.
Remote-Prüfung erfolgreich am 07.11.2026:
[Windows-Build 42420444909](https://github.com/oa7779/AetherCockpit/actions/runs/42420444909)
auf Branch `ci/windows-build`, Quellcommit `913181a`.
Build ohne Warnungen/Fehler; 836 STA- und 44 Verwaltungsassertions bestanden,
zusätzlich 836 STA-Assertions gegen die publizierten Assemblies.
Client-/Server-ZIPs, Setup, Hashmanifest und Herkunft als unsigniertes
CI-Artefakt erzeugt. Keine grafischen Tests, Intune-Paketierung oder
Installation in diesem Lauf; lokale Code-Integrity-Sperre bleibt bestehen.
Zu diesem CI-Prüfzeitpunkt waren `main`, Releasetags und bestehende
GitHub-Releases unverändert.
Git verwendet nun die vom Nutzer freigegebene GitHub-CLI-Anmeldung mit
Workflow-Berechtigung.

Das geteilte SignPath-Formular zeigt inzwischen „Form submitted“ und
„Thank you, we'll be in touch soon.“ Der Antrag ist eingereicht, wurde
aber nicht durch den Assistenten abgesendet. Keine Bestätigung des genauen
eingereichten Formularstands; die neue CI-Herkunft konnte vorher nicht
nachgetragen werden. Annahme, Zertifikat und Signing-Anbindung stehen aus.
Der Nutzer hat die Beantragung bestätigt und die Veröffentlichung des
Status beauftragt. README, Sicherheitsdokumentation und Buildanleitung
nennen den Antrag mit ausstehender Entscheidung; keine Signaturzusage.
Der geprüfte Quell-/CI-Stand wurde samt Dokumentation per Fast-forward auf
`main` übernommen (`26afc27`). Keine neue Paketierung oder Releaseveröffentlichung.
Die drei Beta-37-Screenshots sind im Repository enthalten. Die README-Galerie
verlinkt sie direkt unter `docs/screenshots`, statt auf den noch nicht
veröffentlichten Beta-37-Release zu verweisen.

Beta 36 veröffentlicht mit sechs aufgabenbezogenen Navigationsgruppen,
getrennten Mainframe-/Gerätebereichen und Board-Zoom; GPU-Lüfter auf der
sichtbaren Kühlerseite. Lokaler Tagmodus 07–19 Uhr, Nacht außerhalb, mit
manueller Übersteuerung in Desktop-Einstellungen und Übernahme im Schoner.
Uhrsekunden unabhängig von Hardwareproben. Animationsneustarts, Halo-Clocks
und unnötige Collection-Resets reduziert. Der Nutzer hat neue Beta-Pakete
für Setup, Portable, Server und Intune samt GitHub-Veröffentlichung bestätigt.
Quellbuild: null Warnungen/Fehler; 1543 STA- und 44 Verwaltungsassertions
bestanden, einschließlich Feed-Kompatibilität, ungültiger Modi, Mindestgröße
und validierter Desktop-Speichercallbacks ohne echte Einstellungsänderung.
1543 Assertions mit den tatsächlich gepackten Cockpit-/Schoner-/Shared-DLLs
bestanden. Sekundenprüfung dort maximal 1,095 s Abstand über sechs Sekunden.
Synthetische WPF-Komposition bei 1265×683 p95 20,9 ms (Budget 33,4 ms);
keine GPU-FPS-Angabe. Vorherige Beta 35 im identischen Profil ebenfalls
p95 20,9 ms; animierte Polylines von 69 auf 27 reduziert, keine allgemeine
FPS- oder CPU-Verbesserung behauptet. 742 Client-/378 Server-ZIP-Dateien
hashverglichen, 50 Dokumente und 217 Links geprüft. Intune MAC, IV,
entschlüsselter ZIP-SHA256 und byteidentisches Setup geprüft; kein Tenant-Rollout.
4439 historische dist-Dateien und sämtliche Beta-34-/35-Paketprüfsummen unverändert.
Setup erstellt am 07.11.2026 um 22:16:50 lokal; Dateiversion 0.7.0.36.
Veröffentlicht am 07.11.2026 um 22:21:53 lokal:
[GitHub-Beta 36](https://github.com/oa7779/AetherCockpit/releases/tag/v0.7.0-beta.36).
Quellcode-Commit und Releasetag: `66e6ae5`.
Alle neun GitHub-Assetgrößen und SHA256-Digests gegen lokale Dateien geprüft;
Latest und Tag zeigen auf Beta 36 beziehungsweise den genauen Quellcommit.
Regulärer GitHub-Release für Latest, Produktstatus weiterhin BETA.

GitHub-Präsentation nach Veröffentlichung ergänzt: vier neue Screenshots
aus dem aktuellen Beta-36-UI-Test für Dashboard, Mainframe Tag/Nacht und
Schoner unter `docs/screenshots`; README und Releasebeschreibung zeigen sie.
Dashboard ohne Messwerte, Board-/Schonerbilder mit gekennzeichneten Testdaten.
Screenshotlauf mit 1172 Assertions bestanden, Bilder visuell geprüft.
Keine Programmänderung, neue Version oder erneute Paketierung; veröffentlichte
Setup-/Portable-/Server-/Intune-Dateien und ihre Prüfsummen bleiben unverändert.
Die Paketdokumentation entspricht weiterhin dem geprüften Releasezeitpunkt;
die neue Galerie ist eine Ergänzung der GitHub-Präsentation.

Workspace: `D:\Projekte\SystemCockpit`, mit GitHub-Historie verbunden.
Arbeitsbranch `main`, Remote `origin`: `https://github.com/oa7779/AetherCockpit.git`.
Der separate Branch `ci/windows-build` enthält den geprüften CI-Quellstand.
Veröffentlichte Beta **0.7.0-beta.36**, Dateiversion **0.7.0.36**.
Vorherige [GitHub-Beta 35](https://github.com/oa7779/AetherCockpit/releases/tag/v0.7.0-beta.35)
bleibt erhalten. Die folgenden Angaben dokumentieren ihre Veröffentlichung:
[GitHub-Beta 35](https://github.com/oa7779/AetherCockpit/releases/tag/v0.7.0-beta.35)
mit Setup, Portable-Client, separatem Server, aktuellem Intune-Paket
und Prüfsummen. Quellcode-Commit und Releasetag: `ea79802`.
Veröffentlicht am 07.11.2026 um 21:42:25 lokal.
Alle neun Assetgrößen und GitHub-SHA256-Digests stimmen mit den lokalen Dateien
überein; `/releases/latest` zeigt geprüft auf `v0.7.0-beta.35`.
Für den Latest-Link als regulärer GitHub-Release markiert; Produktstatus
und Releasebeschreibung bleiben BETA, keine Produktionsfreigabe.
Beta 35 enthält außerdem die Logo-Reparatur für Boot und Dashboard sowie
stärkeren xBar-Glow. Eingebettete Bildressource und Dokumentationskopie sind
getrennt; UI verwendet den komponentengebundenen Ressourcenpfad.
Beta 34 bleibt unter Tag `v0.7.0-beta.34`, Quellcode-Commit `32783f9`, erhalten.
Frühere Pakete bleiben unverändert.
Der Nutzer hat für diesen
Durchlauf Entwicklung, Paketierung, Commit und GitHub-Release beauftragt.
Der zuvor veröffentlichte Quellstand ist `591a142`; er enthält Beta 33,
die Dokumentationsüberarbeitung und die vollständige Schoner-Musikentfernung.

[Produktübersicht](README.md) · [Versionsgeschichte](CHANGELOG.md)
· [Entwicklung und Befehle](docs/DEVELOPMENT.md) · [Tests](docs/TESTING.md)

## Lokale Pakete

| Ausgabe | SHA256 |
|---|---|
| [Setup Beta 36](dist/AetherCockpit-Setup-0.7.0-beta.36-win-x64.exe) | `cc4252b8b4fd850f6f852093cc0b73f5cf732f75cb119a3e0813f98bdd527725` |
| [Portable Beta 36](dist/AetherCockpit-0.7.0-beta.36-win-x64.zip) | `7235f2d59b7539e3ed52c4fb1ffe7bca333648d93a269aac6a6f93e8eee9181d` |
| [Server Beta 36](dist/AetherManager-0.7.0-beta.36-win-x64.zip) | `7e17c3c4682e58f1f6ef41e175cb3f6c48309bf91438fc3dcbea6f7a6b8c68b2` |
| [Intune Beta 36](dist/intune/0.7.0-beta.36/AetherCockpit-Setup-0.7.0-beta.36-win-x64.intunewin) | `8694a9a9a2dd06c4a5b363f929481629792f61dbaf99b103931c229b0f985812` |
| [Setup Beta 35](dist/AetherCockpit-Setup-0.7.0-beta.35-win-x64.exe) | `ac516529ce584db58e959a04691219939edf29d9c134568f0e6d4c205af492c8` |
| [Portable Beta 35](dist/AetherCockpit-0.7.0-beta.35-win-x64.zip) | `ff6378aa56bb7fe6466158d4120f285e70360de87042f88ed9db21fb680630a5` |
| [Server Beta 35](dist/AetherManager-0.7.0-beta.35-win-x64.zip) | `2815a130595d96465494732f6a9b2584f6aa15b78924cdf9c65646e947f83e5a` |
| [Intune Beta 35](dist/intune/0.7.0-beta.35/AetherCockpit-Setup-0.7.0-beta.35-win-x64.intunewin) | `d8181149501cf2bf12552413155f9d88666efb7648634497116dcbd49b50c7c9` |
| [Setup Beta 34](dist/AetherCockpit-Setup-0.7.0-beta.34-win-x64.exe) | `79e6d0500634f8cdf00e127ca7a8a12f5930905dd3ab5455d9704e961c6f6ccc` |
| [Portable Beta 34](dist/AetherCockpit-0.7.0-beta.34-win-x64.zip) | `c3db3278299f573777156d74697be29064260a56daab5ad2a67b47d9509025be` |
| [Server Beta 34](dist/AetherManager-0.7.0-beta.34-win-x64.zip) | `0ac29200133f91a540eeb78ae1e75f4cda2439dec2253517a5d2119435e33b6b` |
| [Setup Beta 33](dist/AetherCockpit-Setup-0.7.0-beta.33-win-x64.exe) | `6d2883c75db1fccd863c9da044112453b47dd45371a07241e5241f403ae53a44` |
| [Portable Beta 33](dist/AetherCockpit-0.7.0-beta.33-win-x64.zip) | `c74afbe55f045f6f403429eb44d5b309168ca7a95499c5de1056fc03d2876ec3` |
| [Setup Beta 32](dist/AetherCockpit-Setup-0.7.0-beta.32-win-x64.exe) | `7fcce670cb746b8f8f8f18bd3bf7fdaa338b5ab6371f790d97d800f26b5df5b6` |
| [Portable Beta 32](dist/AetherCockpit-0.7.0-beta.32-win-x64.zip) | `ee2326eb79f22c800c143bda84f94e0204f972bc5d14f8eb3e3fedcbe24004c5` |

[Beta-33-Programm](dist/AetherCockpit-0.7.0-beta.33-win-x64/SystemCockpit.exe)
· [Veröffentlichter Beta-32-Release](https://github.com/oa7779/AetherCockpit/releases/tag/v0.7.0-beta.32)

Der bekannte veröffentlichte Quellcode-Commit zu Beta 32 ist `3a6abdb`.
Bestehende `dist`-Ausgaben bleiben unverändert. Intune wurde für Beta 35
aus dem aktuellen Setup neu erzeugt; die historischen Pakete bleiben erhalten.
Die alten Pakete enthalten ältere Dokumentation und werden nicht nachträglich
umgeschrieben. Künftige Publish-Ausgaben übernehmen die neue Dokumentstruktur.

## Funktionsstand

- Hintergrunddiagnose ist standardmäßig aus. Nach Aktivierung lesender Lauf
  und 30 Minuten Pause; letzter Bericht und neue Befunde im Tray.
  Windows-Anmeldestart braucht die separat genehmigte erhöhte Aufgabe.
- Änderungen bleiben mit Vorschau und Einzelbestätigung manuell, abgesehen
  vom separat freigegebenen DNS-Cache-Assistenten.
  Npcap wird extern installiert; das Setup bietet nur seine Downloadseite an.
- GPU-Zeichnung mit Kartenkörper, drei Lüfterringen, Blende und Kontakten.
- Einzelplatz nimmt keinen Managerkontakt auf. Clientmodus verlangt
  Einmalcode, manuelle Freigabe und getrenntes Übertragungs-Opt-in.
- Server mit HTTPS-Webverwaltung, Gerätefreigaben und Sperre, Heartbeats,
  festen Aufträgen, Richtlinien und Audit. Standard im Entwicklerbetrieb:
  `127.0.0.1:5187`. Dienstpaket benötigt vorhandenes Zertifikat und Hostfilter.
- Optionaler LocalSystem-Clientdienst mit gesonderter Installations- und
  Übertragungsfreigabe; lokale und zentrale Änderungsrechte.
  Eng begrenzte DNS-Cache-Autoreparatur nach doppeltem Opt-in und erneutem Befund.
- Verstärkte Neon-Halos, Pulslicht und Energiebewegung in Schoner,
  Hauptfenster und Dashboard; Bewegung weiterhin abschaltbar.
- Gemeinsamer Windows-Werkzeugkatalog und optionale Microsoft-Werkzeuge.
  Updates und Treiber bleiben einzeln bestätigt, mit Identitäts- und Lizenzprüfung.
- Schoner ohne Musikdateiauswahl, Wiedergabe oder Audiokonfiguration.
  `/c` zeigt nur einen Hinweis auf fehlende eigene Einstellungen.
  Grafik, `/s`, `/p` und Windows-Audiogeräteinventar bleiben erhalten.

## Bisherige Verifikation

Beta 35: Releasebuild ohne Warnungen/Fehler; 1447 STA-Assertions einschließlich
synthetischer Boot-/Dashboard-Logo- und xBar-Glow-Prüfung, zusätzlich 44
Verwaltungs-/HTTPS-Assertions bestanden. Ein HTTPS-Durchlauf hatte einen
Start-Zeitablauf; der erneute vollständige Lauf bestand.
Die 1447 Assertions bestanden auch mit den tatsächlich publizierten Cockpit-
und Shared-DLLs aus einem separaten Fixtureordner ohne externe Logodatei.
Die Testassembly wurde dabei mit der Release-Assemblyversion gebaut.
Setup neu erstellt am 07.11.2026 um 21:41:09 lokal.
Intune: MAC, IV, entschlüsselter ZIP-SHA256 und byteidentisches eingebettetes
Setup geprüft. Kein Tenant-Rollout und keine produktive Installation.
742 Client- und 378 Server-ZIP-Dateien vollständig hashverglichen.
50 Quell-/Paketdokumente und 217 relative Links geprüft; Paketdokumente
entsprechen dem Quellstand. 4439 historische `dist`-Dateien sowie die drei
Beta-34-Paketprüfsummen unverändert.

Vor der Dokumentationsüberarbeitung: Releasebuild ohne Warnungen/Fehler,
Publish und Setup erfolgreich; 1086 STA-Assertions mit Operations-/Board-UI,
Setup-Zustimmungs-/Update-/Silentprüfungen sowie ZIP-Abgleich aller 499
Dateien bestanden. Keine produktive Installation zur Paketprüfung.

Die lokale Server-API wurde zuvor auf Anmeldung, manuelle Freigabe,
Berichte, Persistenz, Pagination, Neustart und HTTPS-Sperre geprüft.
Der Cockpit-Anmelde-/Reporting-Ablauf wurde noch nicht vollständig manuell
auf einem Testgerät durchgespielt.

## Fortsetzung

1. Vor weiteren Veröffentlichungen Quelländerungen und Remote-Stand mit Git prüfen.
2. Neue Versionen separat publizieren; vorhandene Beta-Pakete nicht überschreiben.
3. Client-Anmeldung und Reporting auf einem Testgerät separat erproben.
4. Vor Internet-/VPN-Betrieb HTTPS, Hostfilter, Dienstkonto und Datenverzeichnis-ACLs
   einrichten; keine automatische Firewall- oder Zertifikatsänderung.

Hardwarezugriffe, Reparaturen, Updates, Benchmarks und Remoteänderungen
sind keine Routinevalidierung. Das externe Referenzprojekt bleibt unberührt.

## Dokumentationsüberarbeitung abgeschlossen

14 bestehende Dateien überarbeitet, `CHANGELOG.md` und elf Fachdokumente
unter `docs` neu angelegt. Die frühere Funktionsdatei wurde in die
Funktionsübersicht übernommen; historische Entwicklung steht im Changelog.
Hilfe, Einführung, EULA-Datenhinweise und Operations-Ausgaben wurden
textlich korrigiert. Die Content-Einträge übernehmen künftig Dokumente,
Logo und Serverbeispiele mit passenden relativen Pfaden.

Prüfungen im lokalen Arbeitsbaum:

- Releasebuild mit `--no-restore`: keine Warnungen oder Fehler.
- STA-Runner mit `--professional-ui --catalog-ui --inventory-ui`:
  808 Assertions bestanden, keine Hardware-Sensorinitialisierung.
  Der Grundlauf liest die lokale Registry-Softwareliste.
- 17 Markdown-Dokumente und 71 interne Links geprüft.
- 26 ausgewertete Publish-Content-Einträge in einem getrennten
  Dokumentations-Prüfordner nachgebildet: 16 Markdown-Dokumente und
  62 interne Links gültig; Prüfordner anschließend entfernt.
- Alle 4439 bisherigen Dateien unter `dist` per SHA256 gegen die
  vorab erfasste Vergleichsbasis unverändert. AETHER-Lizenz, Werkzeuge
  und originale Drittanbieter-Lizenztexte ebenfalls unverändert.
- Vergleichssprache und Editorreste im Kontext geprüft: echte XAML-/HTML-
  Attribute, technische Kommentare und Freigabe-/Lizenzhinweise bleiben bestehen.

Keine funktionalen Abläufe, Versionen, Abhängigkeiten oder Testlogik geändert.
EULA-Zustimmung bleibt Version 1.1; geändert wurden erklärende Texte,
nicht Zustimmungsspeicherung oder Gewährleistungs-/Haftungsklauseln.
Keine Sensor-, Reparatur-, Update-, Installations-, Netzwerk- oder Remoteprüfung,
kein Publish, Commit, Tag, Push oder neuer Release.

Offen bleiben die Zuordnung früherer unspezifischer „Beta 8/9“-Angaben,
eine allgemeine Windows-Build-/Hardwarefreigabe und der vollständige
manuelle Cockpit-Clienttest. Vorhandene Pakete enthalten weiterhin alte Dokumente.

## Zweite Qualitätskontrolle

Die folgenden Angaben beschreiben die Prüfung vor der späteren Git-Anbindung.

Unabhängiger Abgleich sämtlicher Fachanleitungen und eingebetteter Hilfe mit
Quellcode und alten Paketdokumenten abgeschlossen. README auf 595 Wörter
gekürzt; doppelte Testprotokolle aus dem Changelog entfernt, historische
Messwerte und Versionshinweise erhalten. Die Installationsbeispiele kennzeichnen
Beta 33 jetzt als lokalen Paketstand. Der EULA-Kommentar beschreibt nur
die lokale Zustimmungsdatei; die Hilfe nennt wieder die am Netzwerkziel
sichtbare öffentliche IP. Ein Terminalvergleich im Codekommentar wurde gekürzt.

Ein echter Git-Diff ist ohne Git-Repository nicht verfügbar. Für 13 der
14 ursprünglichen geänderten/entfernten Dateien konnten bytegleiche Originale
aus Paketkopien und Sitzungsbelegen mit der vorab erfassten SHA256-Basis
rekonstruiert werden. Zusammen mit dem vor dem zweiten Durchlauf gesicherten
Kommentarquelltext sind 26 No-Index-Dateivergleiche einschließlich neuer
Dokumente möglich. Der alte vollständige Übergabetext ist nicht rekonstruierbar;
seine unveränderte Hashbasis und die bisherigen Übergabezusammenfassungen
bleiben als Nachweis erhalten.

Die unabhängige Prüfung meldete maskierte Authorization-Zeilen der HTTP-Beispiele.
Eine direkte Prüfung der Datei bestätigte jedoch gültige Bearer-Templates
für Admin- und Gerätetoken; daran wurde nichts geändert.
Entfernte Vorbildnamen werden nicht als Suchalias wieder eingeführt.
Die Suchlogik bleibt gleich; Treffer für solche Namen entfallen gewollt
mit der bereinigten Dokumentation.

EULA-Version 1.1 bleibt unverändert. Ob die korrigierte Beschreibung optionaler
Übertragung bei einem künftigen Release eine erneute EULA-Annahme verlangt,
ist gesondert zu entscheiden; das würde das Zustimmungsverhalten ändern
und gehört nicht in diese Dokumentationsaufgabe.

Abschließende Prüfung nach diesen Korrekturen: Releasebuild ohne Warnungen
oder Fehler, 808 STA-Assertions bestanden. Alle 18 aktiven Markdown-Dateien
(einschließlich operativer Projektanweisungen), 73 interne Links und
26 Publish-Content-Einträge geprüft; 16 Markdown-Dateien werden ausgeliefert.
No-Index-Whitespaceprüfung, C#-Code-/Literalkontrolle, XAML-Struktur und
unveränderte Projektmetadaten bestanden. Alle 4439 Release-Dateien bleiben
bytegleich. Keine produktive App, Sensoren, Installation, Reparatur oder
Remoteaktion gestartet; GitHub ausschließlich lesend geprüft.

## Entfernung der Schoner-Musik und Git-Anbindung

Die bestehende GitHub-Historie wurde ohne Checkout über die lokalen Dateien
übernommen. Ausgangsstand und gefetchter GitHub-Stand sind unter getrennten
lokalen `refs/backup` gesichert. Der Veröffentlichungsauftrag betrifft
ausschließlich den geprüften Quellstand auf `main`, keine Tags, Releases
oder Paketartefakte. Die vorherigen Beta-33- und Dokumentationsänderungen
gehören zum maßgeblichen lokalen Stand und werden mit übernommen.

`SaverAudio.cs`, `SaverConfigurationWindow.cs` und die bisherigen Musiktests
wurden nach Verwendungsprüfung entfernt. Sitzung und Szene enthalten keine
Wiedergabe-, Dateiauswahl-, Konfigurations- oder Musikfehlerlogik mehr.
Externe Dateien und alte Kontoeinstellungen wurden nicht geöffnet oder verändert.
Andere Audiofunktionen, insbesondere Windows-Endpunkte und Hardwareinventar,
bleiben unverändert. Vollbild, Vorschau und Animation behalten ihren Ablauf.

Releasebuild mit `--no-restore`: keine Warnungen oder Fehler.
STA-Runner mit `--screensaver-ui --board-ui --professional-ui --catalog-ui
--inventory-ui`: 1371 Assertions bestanden. Enthalten sind synthetische
Schoner-/Mainboardtests und Prüfungen auf entfernte Musiktypen sowie den
`/c`-Hinweis. Keine Hardwareinitialisierung, Wiedergabe oder administrative
Produktaktion ausgeführt. Bestehende `dist`-Pakete bleiben unverändert und
enthalten weiterhin ihren alten Funktionsstand.

Der vollständige vorgemerkte Git-Diff wurde unabhängig geprüft; keine
wesentlichen Probleme gefunden. Der Abgleich mit der lokalen Sicherung bestätigt
die auf Schoner-Musik und zugehörige Tests und Texte begrenzten neuen Änderungen.
Das Intune-Paketwerkzeug wird lokal separat bereitgestellt und nicht mitveröffentlicht.

## Beta 34 — Implementierung und Paketprüfung

- Gemeinsamer Katalog mit 25 Aktionen und streng typisierter
  Windows-/Treiberupdateauswahl; lokale und zentrale Lizenzprüfung,
  Einzelinstallation und kein automatischer Neustart.
- Webverwaltung, Gerätesperre, Richtlinien, Heartbeats und auditierte Aufträge.
  Der Clientdienst speichert Übernahme und Ergebnisse vor Folgeaktionen
  mit WriteThrough/Datenträgerflush. Unklare Läufe werden nicht erneut gestartet.
  Inventar-/DNS-Fehler blockieren nicht andere Verwaltungsstufen.
- Dienstassistenten prüfen Programm- und Daten-ACLs, Besitzer und Links.
  Die Setup-Dienstoption stellt nur Dateien bereit; Anmeldung und Rechte
  werden im separaten Konsolenassistenten bestätigt.
- Releasebuild ohne Warnungen oder Fehler. 1433 STA-Assertions, 44
  Verwaltungsassertions einschließlich isolierter HTTPS-Zertifikatprüfung,
  21 HTTP-/Web-/API-Assertions am selbstenthaltenen Serverpaket und fünf
  synthetische Dienst-/Daten-ACL-Fälle bestanden.
- Realer Setup-Compiler und nativer SCR-Build erfolgreich. Inno Setup
  meldet weiterhin den vorhandenen Hinweis zum Architekturidentifier `x64`.
  Gemeinsame Bibliothek in Cockpit, Clientdienst und Server bytegleich.
- Client und Server in separaten neuen Ausgaben erzeugt; bestehende
  Beta-Pakete bleiben erhalten. ZIPs und SHA256-Dateien gehören zum
  beauftragten GitHub-Releaseziel `v0.7.0-beta.34`.
- Beide ZIPs vollständig gegen die Ausgabe geprüft: 742 Client- und
  378 Serverdateien bytegleich. 50 Quell-/Paketdokumente und 216 relative
  Links gültig. Alle 4439 bisherigen Release-Dateien SHA256-unverändert.
- Publish übergibt Version, Assembly- und Dateiversion auch an die gemeinsame
  Bibliothek. Dadurch verwenden Cockpit, Schoner, Dienst und Server dieselbe
  Bibliothek; der bestehende Cockpit-/Schoner-Hashabgleich bleibt aktiv.

Keine produktive Dienstinstallation, Reparatur, Updateinstallation,
Treiberänderung, Firewall- oder Zertifikatsinstallation zur Validierung.
Internet-/VPN-Erreichbarkeit, Zertifikat/DNS, Geräteanmeldung und Freigaben
müssen auf den vorgesehenen Zielrechnern eingerichtet und abgenommen werden.
Die Beta bietet keine eigene Mehrbenutzerverwaltung oder MFA.
