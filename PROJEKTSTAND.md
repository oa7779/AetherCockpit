# AETHER — Projektübergabe

## Stand

Workspace: `D:\Projekte\SystemCockpit`, mit GitHub-Historie verbunden.
Branch `main`, Remote `origin`: `https://github.com/oa7779/AetherCockpit.git`.
Lokaler Release **0.7.0-beta.33**, Dateiversion **0.7.0.33**.
Letzter veröffentlichter GitHub-Release: **0.7.0-beta.32**.
Der Quellstand enthält die anschließende Dokumentationsüberarbeitung und
die Entfernung der Schoner-Musikfunktion. Die Versionsnummer bleibt unverändert;
bestehende Releasepakete werden nicht ersetzt.

[Produktübersicht](README.md) · [Versionsgeschichte](CHANGELOG.md)
· [Entwicklung und Befehle](docs/DEVELOPMENT.md) · [Tests](docs/TESTING.md)

## Lokale Pakete

| Ausgabe | SHA256 |
|---|---|
| [Setup Beta 33](dist/AetherCockpit-Setup-0.7.0-beta.33-win-x64.exe) | `6d2883c75db1fccd863c9da044112453b47dd45371a07241e5241f403ae53a44` |
| [Portable Beta 33](dist/AetherCockpit-0.7.0-beta.33-win-x64.zip) | `c74afbe55f045f6f403429eb44d5b309168ca7a95499c5de1056fc03d2876ec3` |
| [Setup Beta 32](dist/AetherCockpit-Setup-0.7.0-beta.32-win-x64.exe) | `7fcce670cb746b8f8f8f18bd3bf7fdaa338b5ab6371f790d97d800f26b5df5b6` |
| [Portable Beta 32](dist/AetherCockpit-0.7.0-beta.32-win-x64.zip) | `ee2326eb79f22c800c143bda84f94e0204f972bc5d14f8eb3e3fedcbe24004c5` |

[Beta-33-Programm](dist/AetherCockpit-0.7.0-beta.33-win-x64/SystemCockpit.exe)
· [Veröffentlichter Beta-32-Release](https://github.com/oa7779/AetherCockpit/releases/tag/v0.7.0-beta.32)

Der bekannte veröffentlichte Quellcode-Commit zu Beta 32 ist `3a6abdb`.
Bestehende `dist`-Ausgaben bleiben unverändert. Das aktuelle Intune-Paket
gehört weiterhin zu Beta 28; für Beta 33 wurde keines erstellt.
Die alten Pakete enthalten ältere Dokumentation und werden nicht nachträglich
umgeschrieben. Künftige Publish-Ausgaben übernehmen die neue Dokumentstruktur.

## Funktionsstand

- Hintergrunddiagnose ist standardmäßig aus. Nach Aktivierung lesender Lauf
  und 30 Minuten Pause; letzter Bericht und neue Befunde im Tray.
  Windows-Anmeldestart braucht die separat genehmigte erhöhte Aufgabe.
- Reparaturen bleiben mit Vorschau und Einzelbestätigung manuell.
  Npcap wird extern installiert; das Setup bietet nur seine Downloadseite an.
- GPU-Zeichnung mit Kartenkörper, drei Lüfterringen, Blende und Kontakten.
- Einzelplatz nimmt keinen Managerkontakt auf. Clientmodus verlangt
  Einmalcode, manuelle Freigabe und getrenntes Übertragungs-Opt-in.
- Server ist ein separates API-Projekt mit Loopback-Standard
  `127.0.0.1:5187`; keine fertige Verwaltungsoberfläche oder LAN-/Dienstinstallation.
- Schoner ohne Musikdateiauswahl, Wiedergabe oder Audiokonfiguration.
  `/c` zeigt nur einen Hinweis auf fehlende eigene Einstellungen.
  Grafik, `/s`, `/p` und Windows-Audiogeräteinventar bleiben erhalten.

## Bisherige Verifikation

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
2. Bei einem späteren Release die neue Dokumentationsauslieferung prüfen;
   bestehende Beta-33-Pakete nicht überschreiben.
3. Client-Anmeldung und Reporting auf einem Testgerät separat erproben.
4. Vor LAN-Betrieb HTTPS, Hostfilter, Dienstkonto und Datenverzeichnis-ACLs
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
