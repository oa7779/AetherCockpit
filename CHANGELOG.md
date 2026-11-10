# Versionsgeschichte

Neueste Version zuerst. Diese Geschichte fasst die belegten lokalen
Entwicklungsstände zusammen; eine aufgeführte Version ist nicht automatisch
ein veröffentlichter GitHub-Release. Datumsangaben wurden nicht aus der
Reihenfolge abgeleitet.

Buildbefehle stehen in [Entwicklung](docs/DEVELOPMENT.md),
Prüfmodi in [Tests](docs/TESTING.md), aktueller Produktumfang in
[Funktionen](docs/FEATURES.md).

## Unveröffentlicht — Hardware Intelligence

- Eigener Hardwarebereich mit 20 Klassen, kompakten Komponenten-/Wertetabellen,
  M1-Quellenmetadaten und Quelleninspektor.
- Lesende CIM-, CPUID-/Topologie-, DXGI-, Netzwerk-, Display- und Audiopfade;
  exakte Storage-/USB-Beziehungen statt Modellnamen-Matches.
- Klassenweise Inventur, Invalidierung und Stale-Zustände; Sensorübernahme
  aus dem vorhandenen Cockpit ohne neuen Treiberzugriff.
- Native Wi-Fi mit PHY, Signalqualität, Linkraten, Kanal und eindeutig
  zugeordneter Mittenfrequenz; Adapterraten aus echten Bytezählerdifferenzen.
- Rohe Windows-Microcodeangaben, adapterbezogene Direct3D-Featurelevel/
  Raytracing-Tiers, Audioengine-Mixformate sowie Windows-Anzeigeziele,
  bevorzugte Modi, Farbangaben und Desktopskalierung.
- SMART-/NVMe-Health über das vorhandene smartctl ausschließlich lesend:
  dokumentierte Windows-Disknummer und erneute UniqueId-/Pfadprüfung statt
  Modellnamensvergleich oder Gleichsetzung verschiedener Serienkennungen.
- Sensor-Minimum/-Maximum/-Stichprobenmittel seit Messbeginn aus dem
  vorhandenen Statistikhelfer; Lücken, Einheitenwechsel und Grenzwerte
  werden getrennt behandelt. Auch große endliche Gegenwerte bleiben endlich.
- JSON-/CSV-/Textberichte mit ausdrücklicher Sensitive-Freigabe,
  Secret-Ausschluss und CSV-Formelschutz.
- Eigene Fixtures, UI-/DPI-/Themeproofs und freigegebene lokale Read-only-
  Prüfung; keine fremden Datenbanken oder Herstellerlogos übernommen.
- Vollständige M3-Abnahme bleibt wegen fehlender Detailprovider und
  offener realer Langzeit-/Ereignisprüfungen ausstehend.
  [Abschlussbericht](docs/MILESTONE-3-HARDWARE-REPORT.md).
- Keine Versionserhöhung, neue Pakete oder Veröffentlichung.

## 0.7.0-beta.45 — ruhige Neonrahmen und technische Konsolidierung

- Zentrale Neonrahmen mit scharfer Kernkontur, getrenntem innerem/äußerem
  Glow und ruhiger abschaltbarer Atmung ersetzen die umlaufenden
  Panel-Laufspuren in Cockpit, xBar, Diagrammen, Eingaben und Schoner.
- Neun explizite Rahmenzustände, ReducedMotion und kontrollierte Freigabe
  bei Unsichtbarkeit, Minimierung und Unload; Windows Modern bleibt neutral.
- Cockpit-Anordnung, Messwerte, Funktionen, Branding- und Boardanimationen
  bleiben erhalten.
- Kompatible Quellen-/Ergebnis-/Datenschutzgrundlage, additive technische
  Journale sowie korrigierte Ausgabegrenzen und Ressourcenfreigabe.
- Mehrere synthetische Alt-/Neu-Vergleiche, Lifecycle-/DPI-/Bewegungstests;
  keine daraus abgeleitete allgemeine Produktperformancefreigabe.
- Neu revisionierter unsignierter Windows-x64-Installer mit SHA256 für
  eine GitHub-Vorabversion; keine RC- oder Installationsabnahme.

## 0.7.0-beta.44 — Datenträgerkarten und Neon-Glow

- Eine rote Karte pro physischem Datenträger bündelt Lesen, Schreiben,
  Aktivität und Queue mit getrennten Werten, Einheiten und Skalen.
- Farbig unterscheidbare Kurven und Balken: Rot, Gold, Türkis und Violett.
  Windows-Performanceabfragen und Polling bleiben unverändert.
- Echter Weichzeichner-Glow auf separaten Rahmenkonturen und Kurvenhalos;
  stärkere pulsierende Leuchtspuren an animierten Rahmen.
- Gemeinsame, zwischengespeicherte Leuchtkontur für das Mainboard im Tool
  und Bildschirmschoner sowie kräftigere Landschafts- und Leiterbahnhalos.
- Texte und Messwerte bleiben scharf, Windows Modern bleibt neutral;
  Animationsfreigaben und Stopp bei Unsichtbarkeit bleiben erhalten.
- Neuer lokaler Build und Testinstaller; kein GitHub-Release.

## 0.7.0-beta.43 — animiertes AETHER-Branding

- Vorhandenes Logo links oben mit sanftem Neon-Puls und wanderndem Lichtreflex.
- Schräger Chromschriftzug in Silber/Violett/Pink unter dem Logo.
- Dasselbe animierte Branding rechts oben im Bildschirmschoner.
- Bewegung folgt Animationsschalter, Windows-Freigabe und Sichtbarkeit;
  Windows Modern bleibt neutral und statisch.
- Instrumente, Kurven und Cockpit-Anordnung unverändert.
- Neuer lokaler Build und Testinstaller; kein GitHub-Release.

## 0.7.0-beta.42 — ursprüngliches Cockpit wiederhergestellt

- Cockpit-Anordnung vor dem Übersichtsumbau wiederhergestellt: Instrumente,
  Kurven, Prozess- und Ereignislisten sowie Systempuls und Datenkontrolle
  stehen wieder direkt auf der Seite.
- Neue Zusammenfassung und zusätzliche Detailklappe entfernt.
- Bisherige Navigation, Messlogik, Wartungsfunktionen und Wake-on-LAN erhalten.
- Lokaler Testinstaller, kein GitHub-Release.

## 0.7.0-beta.41 — lokaler Cockpit-Teststand

- Cockpit-Installer aus dem aktuellen Arbeitsstand; kein GitHub-Release.
- Priorisierte Cockpit-Übersicht mit Funktionensuche, Schnellzugriffen und
  ausklappbaren Detailansichten.
- Wake-on-LAN mit MAC-Prüfung, Übernahme aus der LAN-Auswahl und Bestätigung
  vor dem Magic-Packet-Versand.
- Erweiterter PowerShell-Katalog für Windows-Wartung, Wiederherstellung,
  WinRE, Treibersicherung, Features, Capabilities und Deploymentabfragen.
- Microsoft Graph ist weiterhin Bestandteil des separaten Verwaltungsservers,
  nicht des Cockpit-Installers.

## 0.7.0-beta.38

- Wählbares Windows Modern mit neutralen Desktopfenstern, Diagrammen,
  Gadgets, xBar und Konsolenansichten; Hell-/Dunkelmodus folgt Windows.
- Animiertes System-Schema mit Geräteauswahl statt Neon-Mainframe.
  Bewegung respektiert Sichtbarkeit, Animationsschalter und Windows.
- Im Modern-Modus kein AETHER-Bildschirmschoner; gespeicherte Auswahl
  sperrt auch direkte SCR-Starts und beendet laufende Schonersitzungen.
- Designwechsel nach bestätigtem Neustart; Retro-Neon bleibt Standard.
- Sprachressourcen vorbereitet. Unvollständiges Englisch wird nicht
  angeboten; die Desktopoberfläche bleibt deutsch.
- Synthetische Hell-/Dunkel- und Neon-Regressionsprüfungen sowie
  Screenshots der zentralen Desktopansichten ergänzt.

## 0.7.0-beta.37

- Quellstand auf GitHub verfügbar, noch kein Beta-37-Release veröffentlicht.
  Lokaler Laufzeittest durch Windows Code Integrity blockiert; separater
  CI-Grundlauf bestanden. Prüfstatus in der Testanleitung.
- Nacht-Hintergründe in Hauptfenster, Mainframe und Bildschirmschoner sind
  schwarz statt blau/violett. Tag-Hintergründe bleiben blau.
- Mainframe-Himmel, Rastergrund, Binärberge und Gerätedetails folgen der
  Farbwahl; farbige Neonlinien, Glow und Bewegung bleiben erhalten.
- Automatischer Zeitplan und manuelle Übersteuerung unverändert.
- Setup, Portable-Client, Server und Intune neu paketiert; GitHub-Galerie
  auf den aktuellen Schwarz-/Blau-Stand aktualisiert.

## 0.7.0-beta.36

### Bedienung und Darstellung

- Funktionen nach Übersicht, Hardware, Diagnose, Wartung, Verwaltung und Hilfe
  gruppiert; aktive Gruppe und Ziel bleiben sichtbar, vorhandene Routen erhalten.
- Mainframezeichnung und Gerätedetails getrennt; Legende ausklappbar.
  Einpassansicht und 75-/100-/125-Prozent-Zoom mit getrenntem Scrollen.
- GPU-Lüfter mit gebogenen Rotorblättern auf der Kühlerseite statt auf der
  schmalen Oberseite; Naben, Ringe und Rotoren in derselben Projektionsebene.
- Stärkerer Glow an Platine, CPU, RAM und GPU mit statischen Vektorhalos.
- Tag-/Nachtmodus in Hauptfenster, Mainframe und Schoner: lokale Uhrzeit
  07–19 Uhr für Tag, sonst Nacht, mit gespeicherter manueller Übersteuerung.
- Schonerstatus und Sekundenanzeige stehen über der Szene statt darüber.

### Aktualisierung und Leistung

- Uhrsekunden unabhängig von Hardwareproben, Uptime zwischen Proben fortgeführt.
- Unveränderte Listenmitgliedschaft wird ohne Zurücksetzen der WPF-Ansicht
  aktualisiert; Gerätauswahl bleibt bei Erscheinungswechsel erhalten.
- Instrumentkanäle je Liveframe zwischengespeichert; keine mehrfachen
  Animationsneustarts bei gleichem Zustand. Unsichtbare Boards halten
  Animation und Readouttimer an; Windows-Animationsfreigabe wird beachtet.
- Neue synthetische Navigations-, Themen-, GPU- und Sekunden-Taktprüfungen.
- Setup, Portable, Server und Intune auf versionsgleichen Stand gebracht.

## 0.7.0-beta.35

### Paketierung und Veröffentlichung

- Setup, Portable-Client und separater Verwaltungsserver vollständig neu gebaut.
- Aktuelles Intune-Win32-Paket aus genau demselben Setup ergänzt, mit
  versionsgleicher Installations-/Erkennungsanleitung und SHA256-Prüfsummen.
- Reproduzierbares Intune-Publishskript mit getrennten, geschützten Ausgaben.
- GitHub-Downloadziel auf die aktuelle Beta gesetzt statt auf Beta 32.

### Darstellung

- AETHER-Logo beim Start und im Dashboard über die eingebettete Ressource
  wiederhergestellt; Dashboardlogo vergrößert und ohne negativen Abstand.
- xBar mit stärkerem Neonrahmen, leuchtender Überschrift und mehrlagigen
  Verlaufskurven. Energiebewegung beachtet den Animationsschalter und Windows.
- Diagnose-, Sensor- und Verwaltungslogik unverändert.

## 0.7.0-beta.34

### Neu

- Stärkerer Neon-Glow in Hauptfenster, Dashboard und Schoner mit
  abschaltbaren Energie- und Pulsanimationen.
- Gemeinsamer Katalog mit 25 Windows-Aktionen, Verfügbarkeitsprüfung,
  begrenzter Konsolenausgabe und Bestätigung vor Systemänderungen.
- Optionale Microsoft-Werkzeuge mit lokalen Start-/Downloadangeboten,
  ohne automatische Installation oder Lizenzannahme.
- HTTPS-Webverwaltung für Gerätefreigabe und Sperre, Heartbeats, Diagnoseberichte,
  Geräterichtlinien, feste Remote-Aufträge und Audit.
- Einzelne Windows- und Treiberupdateangebote mit GUID, Revision und
  Lizenzabgleich vor bestätigter Installation; kein automatischer Neustart.
- Optionaler Windows-Clientdienst mit eigenem Anmelde- und Übertragungs-Opt-in,
  getrennten Änderungsfreigaben und dauerhaft gepufferten Ergebnissen.
- Gerätebezogen freigegebener DNS-Assistent: Cachekorrektur nur bei passendem
  Resolverbefund, erneuter Prüfung und höchstens einem automatischen Auftrag pro Tag.
- Separates selbstenthaltenes Serverpaket mit Dienstinstallationsassistent.

### Entfernt

- Optionale Musikwiedergabe des Bildschirmschoners einschließlich
  Dateiauswahl, WAV-Unterstützung, Lautstärke und gespeicherter Audiokonfiguration.
  AETHER verwendet und verwaltet keine Musikdateien mehr.
- Die frühere Schonerkonfiguration entfällt; `/c` informiert stattdessen,
  dass keine eigenen Einstellungen verfügbar sind. Vollbild und Vorschau
  sowie Windows-Audiogeräteinventar bleiben erhalten.

### Dokumentation

- README auf Produktbeschreibung, Einstieg und aktuelle Grenzen gekürzt.
- Installation, Bedienung, Hardware, Verwaltung, Netzwerk, Datenschutz,
  Server/Client, Intune, Entwicklung und Tests in Fachdokumente getrennt.
- Historische Beschreibungen aus README, Betaanleitung und Übergabe hier
  zusammengeführt. Die frühere Funktionsdatei heißt jetzt `docs/FEATURES.md`.
- Maßeinheiten, Aufbewahrungsregeln und lokale/optionale Datenübertragung
  anhand des Quellcodes korrigiert. Eingebettete Hilfe und Einführungen überarbeitet.
- Dokumentationsübernahme für künftige Builds ergänzt; vorhandene Release-Pakete
  bleiben unverändert.

## 0.7.0-beta.33 — lokaler Build

### Neu

- Einschaltbare Hintergrunddiagnose: sofortiger lesender Lauf und
  anschließend 30 Minuten Pause; letzter lokaler Bericht und Tray-Hinweise
  auf neue handlungsrelevante Befunde.
- Setup und Netzwerkfenster bieten bei Auswahl die offizielle
  Npcap-Downloadseite an. Npcap bleibt eine externe Installation.

### Geändert und behoben

- GPU-Zeichnung mit deutlicherem Kartenkörper, drei Lüfterringen,
  Anschlussblende und PCIe-Kontakten.
- Setupregression an getrennte Download-, Mitschnitt- und Zustimmungsoptionen
  angepasst.

### Bekannte Einschränkungen

- Hintergrunddiagnose ist standardmäßig aus. Anmeldestart braucht die
  separat genehmigte Aufgabe; Reparaturen bleiben manuell.

## 0.7.0-beta.32

### Dokumentation

- Produktbezogene Vergleichsnennungen aus Funktions- und Projektbeschreibungen
  entfernt; tatsächliche optionale Integrationen erhalten.
- Setup und Portable-ZIP auf GitHub veröffentlicht. Dies bleibt der letzte
  veröffentlichte Release.

## 0.7.0-beta.31

### Neu

- Separate Verwaltungs-API und optionaler Server-Clientmodus.
- Geräteanmeldung per Einmalcode, manuelle Freigabe, separates Reporting-Opt-in
  und DPAPI-geschütztes Gerätetoken.
- Stündliche Berichte aus laufendem AETHER; Einzelplatzmodus ohne Managerkontakt.

### Sicherheit

- Loopback als Serverstandard, HTTPS-Pflicht für Netzwerkziele,
  10-MiB-Anfragegrenze, Rate Limits und 90-Tage-Berichtsaufbewahrung.
- Verwaltungsoberfläche und eingerichteter LAN-/Dienstbetrieb fehlen.
  Cockpit-Anmeldung/Reporting noch nicht vollständig manuell geprüft.

## 0.7.0-beta.30

### Neu

- Vier Windows-Assistenten für Systemdateien/Komponenten, Windows Update,
  Netzwerk und Geräte/Treiber mit eigenen lokalen Berichten.
- Systemprüfung über DISM CheckHealth und SFC VerifyOnly.
- Reparaturplan-Vorbereitung wählt getrennte DISM-/SFC-Schritte, ohne sie auszuführen.

## 0.7.0-beta.29

### Neu

- Lokaler Diagnosecollector für Windows-Ereignisse, PnP-Probleme,
  Adapter und App-/Treiberinventar.
- Befunde mit Belegen, Handlungshinweisen und gekennzeichneten Datenlücken.
- Noch ohne zentralen Versand oder automatische Reparaturen.

## 0.7.0-beta.28

### Neu und behoben

- Vollständig unbeaufsichtigte Installation und Deinstallation.
- Silent-Setup überspringt neue Autostartaufgaben ohne Zustimmung,
  statt deshalb abzubrechen.
- `/AutostartConsent=1` und `/RecoveryConsent=1`.
- Intune-Win32-Paket und kontextbezogene Installations-/Erkennungsanleitung.

### Sicherheit

- Silent-Uninstall gilt als Bestätigung der vollständigen Entfernung.
  Mitschnitt und App-Start bleiben silent aus.

## 0.7.0-beta.27

### Behoben

- Raster-Rendering auf einzelne, eingefrorene und am Boden begrenzte
  Fluchtlinien umgestellt, um den bisherigen Renderengpass zu beheben.
- Neon-Glow als Vektorlagen statt großflächiger Blur-Zwischenbilder.
  Animation, Auflösung und Bewegungsgeschwindigkeit blieben erhalten.
- WPF-Abbruchrennen bei zerstörter eingebetteter Vorschau:
  direkte Kindmessung statt Top-Level-Window-MinMax-Logik.

### Historische Messwerte

- Synthetischer 1280×720-Test: Kompositionsmedian 62,5 auf 16,7 ms,
  p95 66,7 auf 20,9 ms. Ein angefordertes 4K-Fenster war von Windows
  auf 3825×1420 Inhalt begrenzt.
- Diese Zahlen sind WPF-Kompositionsabstände, keine Monitor-FPS.
  Isolierter Renderprofilmodus und Vorschau-Lebenszyklustests ergänzt.

## 0.7.0-beta.26

### Geändert

- Bewegter Lichtschwerpunkt und Farbwechsel an Horizont, Bergen, Boden und
  Raster, gemeinsam in Mainboard und Schoner.
- Animationsschalter pausiert auch diese Bewegung. Monitorabschaltung und
  OLED-Pixelpflege bleiben erforderlich.

## 0.7.0-beta.25

### Behoben

- Platine und Bauteile proportional zwischen Infospalten eingepasst,
  damit Platinenumriss und linke Ecke frei bleiben.

## 0.7.0-beta.24

### Neu und geändert

- Sichtbare Erstinventarisierung mit Abfragestufen, gesperrtem Weiter während
  der Erfassung und erneutem Versuch bei Fehler.
- Vektorschoner auf Monitorfläche/4K angepasst, gerades Perspektivraster,
  wandernde Himmelsfarben, sanfte Bildverschiebung und farbige Legendenpfade.
- Rotierende Beschreibungen und Instrumente für zuordenbare Kanäle.
- Optionaler nativer `Aether.scr` für die Windows-Schonerliste.
- Schonerkonfiguration auf Musik an/aus reduziert; vorhandene WAV/Lautstärke erhalten.

## 0.7.0-beta.23

### Neu

- Arbeitsplatzsuche **Funktion finden / Strg+K**, Bereichsfilter und Arbeitsabläufe.
- Paketdetails mit Quellbindung, AETHER-Paketlistenimport/-export,
  serielle Paketaufträge mit Einzelstatus und begrenztem Sitzungsverlauf.
- Getrennte Live-Netzwerkanalyse, kombinierte Filter, Metadaten-CSV und
  bestätigte PCAP-Übergabe.
- Sensor-Snapshot-CSV, Statistikfilter, große Dateien lesend finden.
- Suchbare Autostart-Tabelle und ein-/ausblendbare Ausgabekonsole.

### Sicherheit

- Paketimport bleibt Wunschliste; versteckte angekreuzte Cleaner-Dateien
  bleiben in der Gesamtbestätigung enthalten.

## 0.7.0-beta.22

### Geändert

- Gemeinsame dunkelblau/violette Palette, Magenta-Horizont,
  Cyan-/Pink-Raster und Neon-Glow für Mainboard/Schoner.
- Zwei seitliche Binärbergketten lassen die Perspektivmitte frei;
  Beschriftungen bleiben deckend und ohne Glow.

## 0.7.0-beta.21

### Behoben

- Startabbruch durch doppelte NVIDIA-Sensor-IDs.
  Kanalidentität umfasst ID, Hardware, Name, Typ und Einheit;
  identische Meldungen vereinigt, Konflikte als Lücke gekennzeichnet.
- Schoner auf eindeutig interne Laufwerke begrenzt; externe/unklare Busse
  ausgeschlossen, Storage-Werte nur eindeutig zugeordnet.
- Linienenden, Textabstände und lange Gerätenamen lesbar angeordnet.

## 0.7.0-beta.20

### Neu

- Wiederherstellung erkannter AETHER-Reste nach unvollständigem Uninstall.
- EXE-/DLL-Produktprüfung im eigenen Installationsordner; separate Zustimmung,
  Erhalt von Kontodaten und Zusatzdateien, Wiederherstellung des Uninstallers.

### Sicherheit

- Fremde Dateien, Systemordner und ReparsePoints bleiben abgewiesen.
  Produktmetadaten sind keine digitale Signaturprüfung.

## 0.7.0-beta.19

### Geändert

- Schoner auf obere Szene ohne lange Geräteübersicht reduziert.
- Live-Werte direkt an Gruppen, Gruppenmaxima gekennzeichnet;
  veraltete Messwerte verworfen.
- Uninstaller entfernt nach Bestätigung Installation und AETHER-Kontodaten,
  beendet nur Prozesse dieser Installation und prüft Pfade/Markierungen.

## 0.7.0-beta.18

### Neu

- Sensor-Matrix mit Live-Diagrammen als Standardansicht.
- Getrennte Hardware-/Einheitsskalen, Legendenwahl, Filter und 180 Messzyklen.
- Datenlücken, verschwundene Kanäle und Zeitrücksprünge berücksichtigt;
  Detailmatrix mit Grenzwerten erhalten.

## 0.7.0-beta.17

### Neu

- Optionale externe WAV-Musik im Schoner, einmal für alle Monitore,
  mit sofortigem Stop bei Beenden und stummer Vorschau.
- Kontobezogene Musikeinstellung; keine WAV oder privaten Musikpfade im Paket.
- Sichtbare Musikfehler ohne Abbruch der Grafik.

## 0.7.0-beta.16

### Neu und geändert

- Binärberge und einzelne Laufwerke auf der Platine.
- Aktive Monitorzuordnung, EDID-Namen und Core-Audio-Endpunkte mit Standardrollen
  und Sitzungsstatus.
- Einzelne Peripheriesymbole; weitere Windows-Schnittstellen bleiben in Gruppendetails.
- Hardwareansicht ist Momentaufnahme, kein Pegel- oder physischer Gehäusezähler.

## 0.7.0-beta.15

### Behoben

- Veraltete optionale smartctl-/PresentMon-/OUI-Pfade blockieren Appstart,
  Profilübernahme und andere Einstellungen nicht mehr.
- Pfade bleiben erhalten und werden als fehlend gemeldet,
  statt durch andere Dateien ersetzt zu werden.

## 0.7.0-beta.14

### Neu

- Einzelansichten erkannter Laufwerke, dekorative Lüfter und Binärtext.
- Nicht erhöhter Multi-Monitor-Schonerhost mit `/s`, `/c` und `/p`.
- Lesende lokale Named Pipe für Werte desselben Kontos und derselben Sitzung.
- Ohne Verbindung Hardware-Momentaufnahme statt veralteter Live-Werte.

## 0.7.0-beta.13

### Behoben

- Aufgabenregistrierung nach Beta-12-Laufzeitfehler auf `schtasks.exe`
  mit korrekt codierter XML und ausgewertetem Exitcode umgestellt.
- Gleicher genehmigter `--tray`-Anmeldestart ohne Kennwort.
- Separater Test für deaktivierte, triggerlose Aufgabenregistrierung ergänzt.

## 0.7.0-beta.12

### Geändert

- Isometrische Platine statt Blockdiagramm, anklickbare Hardwaregruppen,
  schematischer Datenfluss und separat angezeigte Live-Werte.

### Bekannter Fehler

- Die damalige reale Autostartregistrierung konnte trotz bestandener
  XML-Definitionsprüfung scheitern; in Beta 13 behoben.

## 0.7.0-beta.11

### Neu

- Virtuelles Mainboard mit vorhandenen PnP-Gruppen und Windows-Elternbeziehungen.
- Erkanntes Setup-Update mit erhaltenem Profil, Daten und Autostart;
  separate Neukonfiguration.
- Betatester-Anleitung und isolierte Setup-/Updateprüfungen.

## 0.7.0-beta.10

### Behoben

- Beta-9-`Type Mismatch` bei Profil-ID-Erzeugung.
- Fehler 740 beim angebotenen Appstart durch Beibehaltung des erhöhten Setuptokens.

## 0.7.0-beta.9

### Neu

- Desktop-Gadgets, getrennte Deckkraft, Tray-Lebenszyklus und Instanzaktivierung.
- Optionale TCP-Verbindungszählung, Npcap-Mitschnitt, PCAP und Schwellenalarme.
- Gesondert genehmigter Anmeldestart.

### Bekannte Fehler

- Profilgenerierung und Appstart am Setup-Ende fehlerhaft; in Beta 10 behoben.

## 0.7.0-beta.8

### Geändert

- xBar auf sechs konfigurierbare Diagrammgruppen reduziert.
- Vorherige Launcher-/Dateifunktionen nicht mehr angezeigt oder ausgeführt;
  alte Einstellungen kompatibel erhalten.

## 0.7.0-beta.7

### Behoben

- xBar-Position bei Windows-Skalierung und Arbeitsbereich.
- Freies FLOAT-Ziehen und Speicherung beider Achsen; kein Auto-hide in FLOAT.

## 0.7.0-beta.6

### Neu

- Historische Launcher-xBar mit Verknüpfungen, Windows-Werkzeugen und Sensorwidgets.
- NVMe Warning/Critical Temperature als Grenzwertkanäle getrennt.
  Die Launcheransicht wurde später in Beta 8 durch Diagramme ersetzt.

## 0.7.0-beta.5

### Neu

- Andockbares Sensorpanel und selbstenthaltenes Windows-x64-Setup mit Startmenü
  und optionaler Desktop-Verknüpfung.
- Automatische Sensorinitialisierung beim Boot.
- Damaliger Uninstaller erhielt Kontodaten; seit Beta 19 gilt vollständige Entfernung.

## 0.7.0-beta.4

### Dokumentation

- Produktbeschreibung, Arbeitsabläufe, Module und lokale Datenhaltung erweitert.

## 0.7.0-beta.3

### Behoben

- CLIXML-Fehlerstrom zu Klartext inklusive Escapes dekodiert.
- BitLocker-RecoveryPassword-Prüfung nach vollständigem Protektordurchlauf
  statt vorzeitigem Abbruch.
- Gemischtes ZIP nach transienter Dateisperre neu erstellt und per
  vollständigem Datei-/Hashvergleich kontrolliert.

## 0.7.0-beta.2

### Behoben

- Skinwechsel wirkt sofort über DynamicResource auf offene Fenster.
- Überlagerungen werden ersetzt, nicht gestapelt; Erststart bleibt Retro-Neon.

## 0.7.0-beta.1

### Neu

- Erststart-Assistent mit EULA-Zustimmung und lesendem Hardware-/Softwareinventar.
- Lokale Zustimmungsdatei und Inventar-Textausgabe.

## 0.6.0-beta.2

### Behoben

- Wagenrücklauf-Fortschritte aktualisieren die laufende Konsolenzeile,
  statt identische Prozentzeilen anzuhängen.
- Geteilte Ausgabechunks, abgeschlossene Zeilen und Anzeigelimit berücksichtigt.

## 0.6.0-beta.1

### Neu und behoben

- Direkte Systemkontrollnavigation und zielgenaue Kontextbuttons.
- Vier Skins über gemeinsame Theme-Ressourcen.
- Expertenmodus beim Speichern anderer Einstellungen erhalten.

## 0.5.0-beta.4

### Behoben

- Schnellzugriff prüft die Registrierung der Windows-„Alle Aufgaben“-Ansicht.
  Fehlt sie, öffnet er die Systemsteuerung mit erklärendem Hinweis.
  Der lokale Befund belegt keine Entfernung auf jedem Windows-11-Build.

## 0.5.0-beta.3

### Neu

- Zurück/Vor, Cockpit/Hilfe, Breadcrumbs, Maus-Daumentasten und Alt-Pfeile.
- Getrennte lokale Navigationsverläufe im Haupt- und Systemkontrollfenster.

## 0.5.0-beta.2

### Neu

- Lesender HVCI-/PawnIO-Kernelzugriffshinweis im Providerbericht und in der Hilfe.
- CPU-Sensorzugriff auf dem damaligen Testrechner nach separat genehmigter
  PawnIO-Installation bestätigt; Kernisolierung blieb aktiv.

## 0.5.0-beta.1

### Neu

- Risikoanzeige, Einsteiger-/Expertenmodus, lokale Modulhilfe,
  Speicherdiagnose-Ergebnisse und Diagnoseexport mit Musterschwärzung.
- Hauptseiten in Arbeitsflussreihenfolge mit Tooltips angeordnet.

## 0.4.0-beta.1

### Neu

- WinGet-Paket-Center mit Mehrfachauswahl und vollständiger ID-Bestätigung.
- Temperatur-Overlay, lesende Shellbag-Analyse, Downloadtest,
  Cachebewertung und Windows-Verwaltungsschnellzugriffe.

## Frühere Entwicklungsstände — Versionszuordnung offen

Die früheren Hauptdokumente bezeichneten weitere Änderungen nur als
„Beta 9“, „Beta 8“ oder mit einem Funktionsnamen. Sie lassen sich nicht
zuverlässig einem vollständigen Versionsprofil zuordnen und werden
nicht mit `0.7.0-beta.9` beziehungsweise `0.7.0-beta.8` gleichgesetzt:

- Lesbare LED-Ziffern bei kompakten Höhen und unterschiedlichen DPI-Stufen;
  gemeinsames kontrastreiches Icon mit sieben Größen.
- LED-Instrumente, Operations mit 17 Wartungskategorien, Windows-Updates
  mit Revision/EULA, explizite WinRM-Ziele und lokale M365-Bereitschaft.
- AETHER-Logo, CRT-Bootjournal, Glasgestaltung und animierte Rahmen.
- Integrierte Prozess-/Ereignisansichten, Superadmin, reversible
  Zustandsänderungen und feste Registry-Einstellungen.
- Live-CPU-Kerne, RAM/Commit/Cache, physische Disk-Zähler, Ereignismonitor,
  SMART, Messlogs, Cleaner und Administratorkonsole.

## Historische Prüfsummen

Aktuelle und erhaltene Releaseprüfsummen liegen in den jeweiligen
`.sha256`-Dateien unter `dist`. Folgende ältere Werte waren in der Übergabe
dokumentiert; nicht alle damaligen Pakete liegen noch im Workspace:

| Ausgabe | SHA256 |
|---|---|
| Setup 0.7.0-beta.5 | `754d5e1491bbdc7774acf0c57e43ea4c5642b923a3d17d7af71eb1a9a82436bb` |
| Portable 0.7.0-beta.5 | `d5b1401f0674e7345ee0338d3e5f2a5943ffdc13b2897d7ae97cb8106aac0548` |
| Historisches `AetherCockpit-Portable.zip` | `fae86c90ddcddf0bced252dd4b034be1306d79ede1f384f7eb72f3c1a6bd2a33` |

Das historische Portable-ZIP enthielt den Beta-4-Build mit zusätzlicher
`LIESMICH.txt` und `EULA.txt`; es ist kein aktuelles Beta-33-Paket.
