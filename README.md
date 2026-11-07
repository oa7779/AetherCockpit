# AETHER / Retro Neon System Control v0.7

Autor: **Olaf Weins**. Copyright (c) 2026 Olaf Weins.
Autorenschaft betrifft AETHER; Rechte und Namensnennungen eingebundener
Drittanbieter bleiben unveraendert.

**Beta 0.7.0-beta.32**: Windows x64, als selbstenthaltenes Setup oder portable
Version inklusive .NET-Laufzeit, Hardwarebibliotheken und portablen
Messwerkzeugen verfuegbar. Das Setup installiert nach `Program Files` und
erstellt eine Startmenue-Verknuepfung; optional kommt eine
Desktop-Verknuepfung hinzu. Die App fordert beim Start weiterhin
UAC-/Administratorrechte.

**Portable Nutzung**: ZIP vollstaendig entpacken und `SystemCockpit.exe`
starten. Keine Installation erforderlich. Den gesamten Ordner behalten; es
handelt sich nicht um eine einzelne EXE. Einstellungen, Sicherungen und Logs
liegen weiterhin unter
`%LOCALAPPDATA%\AetherCockpit`, nicht auf einem eventuell schreibgeschuetzten USB-Stick.
Setup und portable ZIP sind Beta-Ausgaben, keine signierten stabilen Releases;
Windows kann eine SmartScreen-Warnung anzeigen. Herkunft und Pruefsumme vor
dem Start pruefen. Ab Beta 19 entfernt die Deinstallation nach Bestaetigung
den gesamten Installationsordner und `%LOCALAPPDATA%\AetherCockpit` des
ausfuehrenden Windows-Kontos, inklusive Einstellungen, Sicherungen und Logs.
Vorher benoetigte Daten extern sichern. Externe Musikdateien bleiben erhalten.

Native Windows-Kommandozentrale mit WPF / .NET 10 x64. Eigenstaendiges
Retro-Neon-Design in Lila, Tuerkis und Blau: segmentierte Rundinstrumente,
Live-Kurven, Konsolen und abschaltbare Scanner-Animation.

## Start

Die aktuelle Portable-Version gibt es im [GitHub-Release](https://github.com/oa7779/AetherCockpit/releases/latest).
ZIP vollstaendig entpacken und `SystemCockpit.exe` starten.

## Downloads

- [Aktuelle Downloads und SHA256-Pruefsummen](https://github.com/oa7779/AetherCockpit/releases/latest)
- [Ausfuehrliche Anleitung fuer Betatester](BETA-TESTANLEITUNG.md)
- [Funktionsumfang und Grenzen](FUNKTIONSABGLEICH.md)
- [MIT-Lizenz fuer AETHER](LICENSE) und [Drittanbieter-Hinweise](THIRD-PARTY-NOTICES.md)

Den gesamten Ausgabeordner zusammen behalten. Die bisherige Entwicklerversion
benoetigt die .NET 10 Windows Desktop Runtime x64; die Portable Beta enthaelt sie.
Die gesamte App fordert beim Start per `requireAdministrator`-Manifest
Administratorrechte ueber Windows-UAC an. Ohne Freigabe startet sie nicht.
Bei getrennten Administrator-/Benutzerkonten beziehen sich HKCU, Benutzerpfade
und `%LOCALAPPDATA%` auf das freigegebene Administratorkonto.
**Ab Beta 5 werden Hardware-Sensoren beim Boot automatisch initialisiert**,
wie fuer diesen Rechner freigegeben. Der Low-Level-Zugriff von
LibreHardwareMonitor kann einen lokalen Treiber laden; Windows-Schutzfunktionen
werden nicht abgeschaltet. Messaufzeichnung und automatische LAN-Scans bleiben
aus. Keine automatische Schutzabschaltung, keine eigene Dienstinstallation.
Die freie Administratorkonsole kann beliebige Aenderungen ausfuehren; deren
Befehle sind nicht durch die Schutzregeln der gefuehrten Module eingeschraenkt.

## Funktionsumfang

### 0.7.0-beta.30: Windows-Diagnose- und Problemloesungsassistenten

- Unter Cockpit / Diagnose gibt es jetzt den direkten Einstieg
  **Windows-Diagnose & Problemloesung**. In Operations stehen vier getrennte
  Helfer bereit: Systemdateien, Windows Update, Netzwerk sowie Geraete und
  Treiber. Dazu bleibt das umfassende lokale Diagnoseinventar verfuegbar.
- Jeder Assistent fuehrt nur seine eingegrenzte Diagnose aus und speichert
  die Ausgabe lokal. Systemdateien werden mit DISM CheckHealth und
  SFC /verifyonly geprueft; Update-Dienste/-Ereignisse und Neustarthinweise,
  Adapter/IP/Gateway/DNS sowie PnP-Problemcodes werden getrennt angezeigt.
- Das Vorbereiten des Systemreparaturplans startet nichts: erst Wartungs-
  vorschau und Wiederherstellungspunkt, danach einzeln bestaetigte DISM-
  und SFC-Reparaturschritte. Treiber werden nicht automatisch installiert;
  Netzwerk-/Updatekonfiguration wird durch die Diagnose nicht veraendert.
- Keine Hintergrundagenten oder automatische Reparatur. Fehlende Daten und
  nicht eindeutige Zustaende bleiben Hinweise; eine zentrale Uebermittlung ist
  ausschliesslich im angemeldeten Server-Clientmodus nach Freigabe und Opt-in moeglich.

### 0.7.0-beta.31: Zentrale Verwaltung mit Einzelplatz- und Clientmodus

- Der Quellcode enthaelt das separate Projekt `src/AetherManager.Server`; es
  wird nicht mit dem Cockpit-Installer installiert und muss separat gestartet
  werden. Es stellt eine lokale
  ASP.NET-Core-API bereit: Einmalcode-Anmeldung, manuelle Freigabe,
  Geraeteliste sowie authentifizierte Diagnoseberichte. Das Cockpit bietet
  zwei gespeicherte Betriebsmodi: **Einzelplatz** (Standard; kein
  Serverkontakt) und **Server-Client** mit Server-URL und Geraeteanmeldung.
  Die Anmeldung und das Uebertragungs-Opt-in sind separate Schritte.
- Nach ausdruecklicher Aktivierung fragt das Cockpit den Freigabestatus ab und
  sendet nach Freigabe beim App-Start sowie danach stuendlich einen
  Diagnosebericht. Das Geraetetoken wird mit Windows-DPAPI geschuetzt; ohne
  Clientmodus, Anmeldung, Freigabe oder Opt-in findet keine Uebertragung statt.
  Zurueckschalten auf Einzelplatz stoppt die Serverabfrage sofort und deaktiviert
  das Opt-in; die lokale Diagnose und Bedienung bleibt verfuegbar.
- Der Standardbetrieb bindet nur an `127.0.0.1:5187`. Andere als Loopback
  konfigurierte Listener werden nur mit HTTPS akzeptiert. Eine LAN-Adresse,
  ein Zertifikat und Windows-Firewallfreigaben sind noch nicht eingerichtet;
  fuer den Hostfilter muss bei LAN-Betrieb ausserdem `AllowedHosts` gesetzt sein.
- Berichte werden lokal als JSON gespeichert und nach 90 Tagen geloescht;
  JSON-Anfragen sind auf 10 MiB begrenzt. Administrator- und Geraetezugriff
  erfolgt per Bearer-Token; Anmeldecodes sind einmalig und laufen nach
  24 Stunden ab. Neue Geraete bleiben bis zur manuellen Freigabe gesperrt.
- Lokaler Start, Datenspeicher, HTTPS-Konfiguration und API-Aufrufe:
  [Server-README](src/AetherManager.Server/README.md) und
  [HTTP-Beispiele](src/AetherManager.Server/AetherManager.Server.http).
- **Noch nicht umgesetzt:** zentrale Verwaltungsoberflaeche und
  Windows-Dienstinstallation. Der API-Ablauf wurde lokal Ende-zu-Ende
  getestet; Gesamtbuild und STA-Tests bestehen, aber der neue Cockpit-
  Anmelde-/Reporting-Pfad wurde noch nicht manuell durchgespielt.
  Im Einzelplatzmodus uebertraegt Beta 31 keine Daten.

### 0.7.0-beta.32: Eigenstaendige Produktdokumentation

- Vergleichs- und Vorbildnennungen aus der Funktionsuebersicht und den
  Projektbeschreibungen entfernt. Funktionsumfang und Grenzen sind jetzt
  eigenstaendig beschrieben.
- Optionale Integrationen mit installierten Drittprogrammen bleiben erhalten;
  ihr Einsatz erfolgt weiterhin bewusst und nach Bestaetigung.

### 0.7.0-beta.29: Lokale Diagnose-Agenten

- Neuer lokaler, lesender Diagnose-Lauf fuer Windows-Ereignisse,
  Plug-and-Play-Problemgeraete, Netzwerkadapter sowie installierte
  Anwendungen und PnP-Treiber. Er verwendet Windows-PowerShell/CIM und
  schreibt einen lokalen JSON-Bericht unter `%LOCALAPPDATA%\AetherCockpit`.
- Wiederholte Anwendungs-/Systemereignisse und gemeldete PnP-Probleme werden
  als pruefbare Hinweise zusammengefasst. Adapter ohne Status `Up` sind nur
  ein Hinweis; fehlende Daten bleiben unbekannt. Versionsnummern allein
  gelten nicht als Fehler- oder Aktualitaetsnachweis.
- Die Agenten fuehren keine Reparatur aus und senden nichts ins Netzwerk.
  Eine LAN-Zentrale, Geraeteanmeldung und Reportuebermittlung sind noch nicht
  implementiert. Reparaturvorschlaege bleiben manuell bestaetigungspflichtig.

### 0.7.0-beta.28: Silent-Installation fuer Softwareverteilung (z.B. Intune)

- Setup und Uninstaller sind jetzt vollstaendig unbeaufsichtigt nutzbar.
  Install: `AetherCockpit-Setup-0.7.0-beta.28-win-x64.exe /VERYSILENT
  /SUPPRESSMSGBOXES /NORESTART`. Die vorausgewaehlte erhoehte
  Autostart-Aufgabe wird dabei ohne ausdrueckliche Zustimmung NICHT
  eingerichtet, statt die Installation abzubrechen; das Setup protokolliert
  das Ueberspringen. Interaktiv bleibt die Zustimmungsseite unveraendert.
- Neuer Parameter `/AutostartConsent=1` erteilt die ausdrueckliche
  Autostart-Zustimmung per Kommandozeile (nur sinnvoll im Kontext des
  Administratorkontos, fuer das die Aufgabe gelten soll - nicht im
  SYSTEM-Kontext einer Softwareverteilung). Neuer Parameter
  `/RecoveryConsent=1` bestaetigt silent die Wiederherstellung eines
  erkannten AETHER-Restordners; ohne ihn bricht die Silent-Installation
  dort weiterhin kontrolliert ab. Npcap-Mitschnitt bleibt silent immer aus.
- Silent-Deinstallation: `unins000.exe /VERYSILENT /NORESTART`. Die
  Silent-Kommandozeile des Administrators gilt als ausdrueckliche
  Bestaetigung; interaktiv fragt der Uninstaller unveraendert nach, bevor
  Installationsordner und AETHER-Kontodaten entfernt werden.
- Intune-Paket (`.intunewin`) mit Installations-/Erkennungs-/
  Deinstallationswerten: [dist/intune/INTUNE-DEPLOYMENT.md](dist/intune/INTUNE-DEPLOYMENT.md).
  EULA-Bestaetigung erfolgt weiterhin beim ersten manuellen Start; die App
  startet nach einer Silent-Installation nicht automatisch.
- Setup-Regression um Silent-Szenarien erweitert (Autostart uebersprungen
  ohne Zustimmung, Autostart mit Kommandozeilen-Zustimmung); isolierte
  Uninstall-Regression prueft jetzt den produktiven Silent-Bestaetigungspfad.
  Keine Aenderung an App, Sensorik oder Design; Programmdateien identisch
  zur Beta-27-Logik bis auf Versionsnummern.

### 0.7.0-beta.27: Renderengpass im Strassenraster behoben

- Fluchtlinien werden einzeln und auf den sichtbaren Strassenbereich begrenzt
  gezeichnet. Der vorherige gemeinsame Linienpfad mit vielen Kreuzungen am
  Fluchtpunkt blockierte die WPF-Darstellung auch auf leistungsfaehiger Hardware.
- Neonleuchten als mehrschichtige Vektorlinien statt grossflaechiger
  Blur-Zwischenbilder, auch fuer Platinenrand, Berge und Datenfluss.
  Animationen, Farbwechsel, Aufloesung und Animationsgeschwindigkeiten bleiben
  erhalten. Glow wirkt etwas schaerfer, die Strassenperspektive bleibt gerade.
- Synthetischer Vergleich gegen Beta26: typischer WPF-Kompositionsabstand
  etwa 63 ms vorher / 17 ms nachher bei 1280x720. Das ist keine Messung der
  tatsaechlich ausgegebenen GPU-Bildrate auf beliebigen Monitoren.

### 0.7.0-beta.26: Bewegter Horizont

- Auch Horizontleuchten, Bergfuellungen/-konturen und Boden-/Rasterfarben
  wechseln langsam ihre Farben. Der farbige Lichtschwerpunkt wandert entlang
  des Horizonts; kein dauerhaft rosa/blau stehender Streifen mehr.
  Gemeinsame Darstellung im Mainboard und Schoner, ohne Taumeln der Strasse.
- Animationsschalter pausiert auch diese Bewegungen. Die Animation reduziert
  statische Bildinhalte, garantiert aber keinen Einbrennschutz; automatische
  Monitorabschaltung und vorhandene OLED-Pixelpflege weiterhin nutzen.

### 0.7.0-beta.25: Freie Platinenecken

- Platine samt Bauteilen, Lueftern und Laufwerksbeschriftungen proportional
  verkleinert und zwischen den Infospalten positioniert. Keine Infobox
  ueberdeckt mehr die linke Ecke oder andere Teile des Platinenumrisses.
  Gemeinsame Korrektur fuer Mainboard und Schoner, auch bei vielen Laufwerken.

### 0.7.0-beta.24: Inventarfreigabe und bewegte 4K-Mainboard-Szene

- Erstinventarisierung mit deutlich sichtbarer Status-/Fortschrittsanzeige und
  echten Abfragestufen. Weiter/Zurueck sind waehrenddessen gesperrt; Cockpitstart
  erst nach Abschluss. Bei Fehler bleibt Weiter gesperrt, Aktualisieren erlaubt
  einen erneuten Versuch. Fehlende einzelne Hardwarewerte bleiben gekennzeichnet.
- Schoner ohne Scrollmessung: Vektorszene wird an die verfuegbare Monitorflaeche
  angepasst und auch auf 3840x2160 hochskaliert. Bei breiten Monitoren werden
  Himmel/Boden seitlich erweitert, nicht die Platine verzerrt oder abgeschnitten.
- Gerade Strassenperspektive: feste Fluchtlinien, nur Querlinien laufen nahtlos
  zum Horizont. Himmel mit wandernden Verlaufspunkten und langsam wechselnden
  Farben; sanfte Bildverschiebung und farbige, laufende Legendenverbindungen.
- In Mainboard UND Schoner wechseln Komponentenbeschreibungen alle sechs
  Sekunden mit Rundinstrumenten. Alle zuordenbaren Sensorkanaele werden
  reihum gezeigt, einschliesslich Takt, Temperatur, RPM, Leistung/Spannung
  soweit verfuegbar. Kanal und Hardware stehen am Instrument; dessen Skala
  ist keine gemessene Hardwaregrenze. Widersprueche/fehlende Werte bleiben `--`.
- Die Bewegung reduziert feste Bildinhalte, ist aber keine Garantie gegen
  OLED-Einbrennen. Monitor-Pixelpflege, geringe Helligkeit und automatisches
  Abschalten bleiben wichtig. Windows-/AETHER-Animationsschalter werden beachtet.
- Optionales Setup installiert einen kleinen nativen `Aether.scr`-Starter in
  den Windows-Systemordner. Dadurch steht **AETHER Mainframe 1986** in der
  Windows-Bildschirmschonerliste, ohne automatische Aktivierung. Runtime/Host
  bleiben im App-Ordner; Deinstallation entfernt Starter und Registrierung.
- Schoner-Einstellungen enthalten nur **Musik an / aus**. Bestehende externe
  WAV und Lautstaerke bleiben erhalten; fehlt eine Zuordnung, wird beim
  Einschalten/Speichern einmalig nach einer lokalen WAV gefragt. Kein Musik-
  oder privater Pfad im Paket. Vorschau bleibt stumm.

### 0.7.0-beta.23: Arbeitsplatz und sichere Profi-Arbeitsablaeufe

- FUNKTION FINDEN / Strg+K in beiden Hauptfenstern: aufgabenorientierte Suche
  plus kompletter Befehlskatalog, Bereichsfilter, Wirkung und Schrittfolge.
  Oeffnet nur das Ziel; startet nichts und entsperrt keinen Expertenmodus.
- Paketdetails, exakte Quellbindung und lokale AETHER-Paketlisten mit striktem
  Import/Export (100 Pakete/128 KiB). Import ist Wunschliste, keine Installation
  und keine Ist-Liste; Versionen sind keine Pins. Quellenlose Eintraege werden
  beim Export sichtbar ausgeschlossen. Weitere Paketmanager optional im
  bewusst gestarteten bereits installierten UniGetUI, nicht nativ in AETHER.
- Pakete seriell mit Einzelstatus, Exitcode und Aktionsjournal; alle IDs/Quellen
  in scrollbarer Freigabe mit expliziter Zustimmung. Nach Lauf Liste neu laden.
  Auftraege zeigt bis 200 Sitzungseintraege; keine sensiblen/freien Befehle.
- Netzwerk trennt Live-Analyse, Erfassung und Warnungen. Kombinierte Filter,
  Metadatendetails und CSV nur fuer das begrenzte Live-Fenster. PCAP-Datei
  bleibt ungefiltert; nach Stop bestaetigte Uebergabe an installiertes Wireshark.
  Keine vollstaendige Offline-Protokollanalyse, kein automatisches
  Angriffsschutzprodukt.
- Sensor-Snapshot-CSV, filterbare Statistik und CSV sichtbarer Statistikreihen.
  Dieselbe kollisionssichere Kanalzuordnung wie Live-Charts gilt nun auch fuer
  MIN/MAX/AVG und alte/neue Messlogs; widerspruechliche Duplikate werden nicht
  gemittelt. Fehlende Werte und Temperaturgrenzen bleiben korrekt getrennt.
- Cleaner-Pfadfilter, sichtbare Kandidaten gezielt ankreuzen, Gesamtmenge und
  versteckte Auswahl explizit anzeigen/bestaetigen. Grosse-Dateien-Suche ist
  nur lesend im gewaehlten Ordner: max. 100.000 Eintraege/32 Ebenen/1.000
  groesste Treffer, keine Links/Junctions, Leseprobleme und Grenzen sichtbar.
  Keine Loeschfunktion in der Speicheranalyse. Autostart als suchbare Tabelle
  mit vollstaendigem Befehl und unveraenderter Sicherung/Konfliktpruefung.
- Systemkontroll-Konsole anfangs platzsparend verborgen; bei Auftrag/Wartung
  sichtbar, ueber AUSGABE EIN/AUS schaltbar. Dunkle Tabellenkoepfe und kleinere
  Fensterhoehen unterstuetzt. Retro-Neon, Schoner und bisherige Freigaben bleiben.
- [Funktionsumfang und Grenzen](FUNKTIONSABGLEICH.md).
  Kein Registry-/RAM-Cleaner, pauschales Debloating oder Schutzabschalten.

### 0.7.0-beta.22: Synthwave-Farben, Neon-Glow und Horizontberge

- Gemeinsame Darstellung im Mainboard-Modul und im Bildschirmschoner:
  dunkelblauer/violetter Himmel, kraeftiges Magenta am Horizont und leuchtendes
  Cyan-/Pink-Perspektivraster. Die Platine bleibt dunkelblau mit cyanfarbener
  Neon-Kontur; symbolische Datenpfade erhalten einen farbigen Glow.
- Die Binaerberge stehen links und rechts am Horizont, nicht mehr als
  durchgehender Streifen auf dem Rasterboden. Die Mitte bleibt als
  perspektivischer Korridor offen. Drahtgitter und leuchtende Bergkonturen
  ergaenzen die sieben weiterlaufenden Binaerzeilen.
- Bergfuesse bleiben am Horizont verankert; nur Raster und Binaertext bewegen
  sich. Bestehender Animationsschalter pausiert weiterhin Raster, Text,
  Luefter und Datenfluss. Glow ist statisch, ohne neue Timer oder Messungen.
- Beschriftungen maskieren weiterhin dahinterliegende Linien, jetzt mit dem
  jeweils passenden deckenden Himmelsverlauf statt schwarzen Rechtecken.
  Kein Glow auf der kleinen Schrift. Messwerte, Laufwerksfilter, Musik und
  Administrationsfreigaben bleiben unveraendert. Keine Referenzbilder verpackt.

### 0.7.0-beta.21: Startabbruch und Mainboard-Beschriftungen korrigiert

- Bestaetigten Startabbruch nach Initialisierung behoben: LibreHardwareMonitor
  meldet auf dieser NVIDIA-GPU eine Sensor-ID mehrfach. Die Verlaufskurven
  verwenden jetzt ID plus Hardware/Kanalname/Typ/Einheit statt alleiniger ID.
  Verschiedene Kanaele bleiben getrennt, identische Meldungen werden vereinigt;
  widerspruechliche Werte desselben Kanals sind eine gekennzeichnete Luecke.
  Hinweise stehen im Sensoren-Tab und Trace; rohe Detailmatrix bleibt erhalten.
  Solche Argumentfehler im Startablauf werden zudem sichtbar gemeldet.
- Im Schoner nur intern erkannte Systemlaufwerke (auch weitere interne
  SSDs/HDDs, nicht nur C:). Windows-Bustyp plus USB-Elternbeziehungen schliessen
  USB-Sticks, USB/UASP-Gehaeuse, SD/MMC und optische Medien aus. Unbekannter
  Bustyp wird nicht als intern geraten. Das normale Cockpit behaelt alle Geraete.
  Ohne zuordenbaren Storage-Sensor bleibt dessen Messwert `--`: nur exakte
  Modellnamen interner Laufwerke, keine namensgleiche externe Gegenstelle,
  keine vermutete Zuordnung aus einer laufenden Nummer.
- Beschriftungen liegen deckend vor den Leitungen; Hinweislinien enden am
  Rand statt mitten im Text. Vertikale Abstaende verhindern Ueberlappungen;
  lange Namen werden nach ganzen Textzeilen mit Auslassung gekuerzt und bleiben
  vollstaendig im Tooltip/Detailbereich. Gilt fuer Cockpit und Schoner.
- Regressionen fuer gemeldete NVIDIA-ID, widerspruechliche Werte,
  Kanalreihenfolge/-ausfall, USB/UASP/SD/optische/unklare Laufwerke,
  Linienenden und Beschriftungsgrenzen sowie mehrere Vorschaugroessen.
  Echter Initialisierungsablauf bis zum geoeffneten Cockpit im Testhost geprueft,
  inklusive realer GPU-Sensoren; keine Reparaturen, Updates oder Benchmarks.
  Lesender Inventartest: fuenf Laufwerke, davon drei intern und zwei USB.

### 0.7.0-beta.20: Wiederherstellung nach unvollstaendiger Deinstallation

- Ein alter Uninstaller kann verschwunden sein, obwohl gesperrte Programmdateien
  noch im Installationsordner liegen. Beta 19 blockierte dann auch die erneute
  Installation wegen des nicht leeren Ordners.
- Das Setup erkennt jetzt solche Reste anhand beider AETHER-Programmdateien
  (EXE und DLL mit passendem Produkt/Hersteller/Originaldateinamen) in einem
  eigenen Ordner namens `AETHER System Control`. Gemeinsame Systemordner,
  Ordnerlinks/Junctions und fremde Programme werden weiterhin abgewiesen.
  Diese Erkennung ist eine Produktpruefung, keine digitale Signaturpruefung.
- **Altes Setup abbrechen, AETHER im Tray vollstaendig beenden, neues Setup
  starten und die angebotene Wiederherstellung bestaetigen.** Sie ersetzt
  Programmdateien und richtet den Uninstaller neu ein. Keine vorherige manuelle
  Ordnerloeschung erforderlich. Kontodaten und vorhandene Zusatzdateien werden
  dabei nicht geloescht; die gewaehlte Setup-Konfiguration wird angewendet.
- Der Dialog weist ausdruecklich darauf hin, dass eine spaetere bestaetigte
  Deinstallation den ganzen Zielordner und die Kontodaten entfernt. Nur einen
  ausschliesslich fuer AETHER bestimmten Ordner bestaetigen. Ohne Zustimmung
  und bei unbeaufsichtigter Installation wird nicht wiederhergestellt.
  Bei nicht erkennbaren Resten einen neuen leeren AETHER-Ordner waehlen.
- Regression: `powershell -NoProfile -File .\tests\Verify-Recovery.ps1`.
  Testet mit kopierten Beta-17-Produktdateien im Temp-Verzeichnis Zustimmung,
  Ablehnung, fremde EXE und Junction, jeweils mit Hashpruefung der Altdateien.
  Keine produktive Installation oder Prozess-/Aufgaben-/Nutzerdatenaenderung.

### 0.7.0-beta.19: Kompakter Schoner und vollstaendige Deinstallation

- Der Schoner zeigt nur die obere isometrische Szene inklusive Laufwerken,
  Raster und Binaerbergen, ohne die lange Geraeteuebersicht darunter.
  Im normalen Cockpit bleibt die vollstaendige Geraeteansicht erhalten.
- CPU-/RAM-/GPU-Auslastung und verfuegbare Temperaturen stehen direkt an den
  Komponentengruppen; Netzwerk zeigt RX/TX. Speicher-/Chipsatz-/RAM-/GPU-
  Temperaturen sind ausdruecklich Gruppenmaxima, SSD-Aktivitaet ebenfalls.
  Keine geratenen Zuordnungen zu einzelnen SSDs. Fehlende Sensoren bleiben `--`;
  Temperaturgrenzen sind keine Messwerte. Veraltete/fehlende Verbindung
  entfernt die Live-Werte. Updates setzen die laufenden Animationen nicht zurueck.
- Deinstallation entfernt zuerst die Autostart-Aufgabe, beendet App, Tray,
  Gadgets und SCR nur aus genau diesem Installationsordner und loescht nach
  ausdruecklicher Bestaetigung Installation samt Daten des ausfuehrenden Kontos.
  Andere portable Programmordner und externe Musik bleiben unberuehrt;
  portable Kopien verwenden jedoch dasselbe jetzt geloeschte Kontodatenverzeichnis.
  Andere Windows-Profile werden nicht bereinigt. Bei anderem UAC-Administratorkonto
  gilt dessen Profil. Aktive Windows-Schonerauswahl wird nur fuer diese SCR entfernt.
- Alte Installationen zuerst mit diesem Setup aktualisieren; erst der neue
  Uninstaller enthaelt die Bereinigung. Updates erhalten weiterhin Nutzerdaten.
  Neuinstallation verlangt einen eigenen leeren Zielordner (ab Beta 20 mit
  bestaetigter Wiederherstellung erkannter Altinstallationen). Ordnerlinks oder
  fehlende Installationsmarkierung brechen die Deinstallation vor Dateiloeschung
  mit einer Fehlermeldung ab. Fehler der Hilfsskripte werden im Inno-Log protokolliert.
- Sichere Regression: `powershell -NoProfile -File .\tests\Verify-Uninstall.ps1`.
  Erstellt eine isolierte Inno-Testinstallation mit eigenem Registrierungsschluessel
  und temporaeren Daten; prueft realen Uninstaller, Prozesspfadabgleich,
  erhaltene externe Datei, vollstaendige Ordner-/Registrierungsentfernung und
  Abbruch vor Loeschung. Keine produktive Installation oder Autostart-Aenderung.

### 0.7.0-beta.18: Sensor-Matrix mit Live-Verlaufskurven

- **Sensor-Matrix -> LIVE-DIAGRAMME** ist die neue Standardansicht.
  Verlaufskurven werden nach Hardware-ID und Einheit gruppiert: Temperatur,
  Auslastung, Spannung, Luefterdrehzahl usw. besitzen getrennte dynamische
  Y-Skalen. Gleichnamige Laufwerke bleiben getrennt. Kanaele ohne bekannte
  Einheit werden zusaetzlich nach Sensortyp getrennt.
- Farbige Legenden mit Haken erlauben die Auswahl einzelner Sensoren fuer
  diese Sitzung. Der aktuelle Wert steht direkt am Sensornamen. Filter wirkt
  gleichzeitig auf Diagramme und Detailmatrix (Hardware, Name, Typ, Einheit, ID).
- Letzte 180 Messzyklen, beim ueblichen 2s-Takt etwa sechs Minuten. Echte
  Messzeitpunkte auf der X-Achse, ausschliesslich im Arbeitsspeicher; keine
  zusaetzlichen Sensorabfragen, Treiber oder Aufzeichnungen. Kurven sammeln
  sich auch bei anderer geoeffneter Seite weiter.
- Fehlende/ungueltige Werte, deaktivierter Sensorzugriff und Abfragefehler
  unterbrechen Kurven; keine Nullersatzwerte oder Verbindung ueber fehlende
  Messpunkte. Historie bleibt bis zum Auslaufen des Fensters erhalten,
  aktuelle Werte werden ausdruecklich als nicht verfuegbar bezeichnet.
  180 Zyklen lang verschwundene Kanaele werden entfernt. Bei rueckwaerts
  springender Systemzeit beginnt die Zeitachse mit Trace-Warnung neu.
- **DETAILMATRIX / GRENZWERTE** behaelt die bisherige lesende Tabelle mit
  allen Eintraegen. NVMe-Temperaturgrenzen bleiben gekennzeichnete Grenzen
  und werden niemals als Ist-Temperaturkurven gezeichnet.

### 0.7.0-beta.17: Lokale Musik zum Bildschirmschoner

- Optionale lokale WAV in Endlosschleife, genau eine Wiedergabe fuer alle
  Monitore. Beim ersten Beenden der Vollbild-Sitzung stoppt Musik sofort,
  bevor noch laufende Hardwareabfragen abgewartet werden. Vorschau bleibt stumm.
- Windows-Bildschirmschoner -> **Einstellungen** oder `AetherMainboard.scr /c`:
  ab Beta 24 nur Musik an/aus; vorhandene WAV/Lautstaerke werden beibehalten
  (neue Zuordnung: einmalige Dateiauswahl, Vorgabe 35 %).
  Windows-Systemlautstaerke bleibt unveraendert.
- Neue Konten starten ohne Musik. Auswahl liegt je Windows-Konto separat in
  `%LOCALAPPDATA%\AetherCockpit\screensaver-audio.json`, unabhaengig von den
  erhoehten Cockpit-Einstellungen. Lokale Dateien bleiben extern; Musik und
  private Musikpfade werden nicht in Setup/ZIP aufgenommen.
- Fehlende OneDrive-/lokale Datei oder Decoderfehler erscheinen im Schoner
  als Musikfehler und im Trace, ohne die Grafik zu beenden oder Ersatzton
  abzuspielen. OneDrive-Datei vorher lokal verfuegbar halten. Keine Downloads
  durch die App; keine UNC-Netzwerkpfade. Nur Musik verwenden, fuer deren
  Nutzung/Weitergabe die entsprechenden Rechte bestehen.

### 0.7.0-beta.16: Binaerberge, Laufwerke auf der Platine und aktive Geraete

- Die laufende Binaersignatur bildet mehrere Bergsilhouetten auf dem
  Perspektivraster. Berge und Raster teilen dieselbe Bewegung zum Horizont;
  die sieben Zahlenbaender laufen innerhalb der Konturen weiter.
- Jedes erkannte DiskDrive-/CDROM-Geraet sitzt einzeln auf der isometrischen
  Platine, mit Modell/Status, anklickbarer Einzelansicht und schematischer
  Busverbindung. Keine Laufwerkskarten unter dem Board, keine Begrenzung oder
  Zusammenfassung identischer Modelle. Die virtuelle Platine waechst bei Bedarf;
  unbekannte Bauformen bleiben schematisch, ohne daraus sicher SSD/HDD abzuleiten.
- Eigene Monitorgruppe: EDID-Modellnamen soweit verfuegbar und Zuordnung aller
  aktiven Desktop-Monitorpfade zu ihren Windows-Geraete-IDs. Hauptbildschirm,
  weitere aktive Bildschirme, Aufloesung und Windows-Anzeigename werden benannt.
  Keine Auswahl nur des alphabetisch ersten Monitors.
- Audio ueber Windows Core Audio: bereite, deaktivierte und abgetrennte
  Endpunkte, Standardrollen fuer Wiedergabe/Aufnahme (Konsole, Multimedia,
  Kommunikation) sowie aktive Audiositzungen. Auch ein gerade verwendeter
  Nicht-Standard-Ausgang erscheint vorn. GELB = aktive Sitzung, GRUEN =
  Standardrolle; eine aktive Sitzung kann still sein, sie ist keine Pegelmessung.
  Keine Aufnahme, Wiedergabe, Lautstaerkeaenderung oder Erfassung von Prozessnamen.
- Monitore, bereite Audio-Endpunkte, Tastatur-/Maus-/Kamera-/Drucker- und
  physische Bluetooth-Geraeteknoten erhalten einzelne anklickbare Symbole.
  Abgetrennte Audio-Endpunkte werden nicht als angeschlossen gezeichnet.
  Bluetooth-Protokoll-/Dienstknoten, USB-Controller und uebrige PnP-Eintraege
  bleiben vollstaendig in den jeweiligen Gruppen-Details erhalten, statt als
  zusaetzliche physische Geraete dargestellt zu werden.
- Windows-Inventar ist keine Garantie fuer die Anzahl physischer Gehaeuse:
  ein Geraet kann mehrere Schnittstellen melden. Nutzung/Standardrollen sind
  Momentaufnahmen: nach Anschluss-/Ausgabewechsel **HARDWARE NEU LESEN**.
  Im verbundenen SCR wird die Hardwareansicht spaetestens nach der naechsten
  Feed-Inventarisierung (etwa eine Minute) aktualisiert; offline bleibt sie eine
  ausdruecklich markierte Momentaufnahme.
- Gleiche Szene in Cockpit und Multi-Monitor-SCR. Der Animationsschalter und
  das Stoppen aller Uhren bei Ausblenden/Beenden bleiben erhalten.

### 0.7.0-beta.15: Veraltete Werkzeugpfade blockieren kein Upgrade

- Fehlende optionale smartctl-/PresentMon-EXEs oder OUI-Dateien sind ein
  Verfuegbarkeitsproblem, keine ungueltige Gesamtkonfiguration. Die App startet,
  uebernimmt das Setup-Profil und behaelt alle anderen Einstellungen.
- Gespeicherte absolute Pfade bleiben unveraendert. Keine automatische
  Ersetzung durch andere EXEs; Warnungen im Bootprotokoll, Footer und
  Systemkontrolle -> Einstellungen benennen Werkzeug und Pfad. Dort Pfad
  neu auswaehlen oder leeren, um wieder die vorhandene Werkzeugerkennung zu nutzen.
- Relative/ungueltige EXE-Pfade und ungueltige Warnschwellen bleiben Fehler.
  Fehlende Werkzeuge lassen sich weiterhin nicht ausfuehren. Bei Aenderung
  anderer Einstellungen werden fehlende optionale Dateien erneut als Warnung
  gemeldet, ohne die Speicherung zu blockieren.
- Kein Loeschen der Einstellungsdatei oder Neuinstallieren erforderlich.

### 0.7.0-beta.14: Bewegtes Mainboard und Multi-Monitor-Bildschirmschoner

- Alle erkannten Windows-DiskDrive-/CDROM-Geraete erscheinen zusaetzlich
  einzeln als stilisierte Laufwerke unter der Platine, mit Modell und Status.
  Keine Begrenzung auf eine SSD; identische Modelle bleiben separate Geraete.
  Anklicken zeigt genau dieses Geraet. Unbekannte Bauformen bleiben symbolisch;
  eine PnP-Momentaufnahme garantiert keine echte physische SSD-/HDD-Klassifizierung.
- CPU-/GPU-Luefter drehen langsam in der isometrischen Ebene (18 Sekunden
  je Umdrehung), rein dekorativ, keine RPM-Messung. Perspektivraster laeuft
  nach hinten; sieben Zeilen bewegen sich von rechts nach links mit
  UTF-8-Binaerdarstellung von `AETHER / Olaf Weins / `.
  Gemeinsamer Animationsschalter; bei Ausblenden, Entladen oder Beenden
  werden auch diese Animationsuhren entfernt.
- `AetherMainboard.scr` nutzt dieselbe Zeichnung auf jedem angeschlossenen
  Bildschirm, jeweils komplett, nicht als gespannte Flaeche. Physische
  Monitorgrenzen und PerMonitorV2-DPI; bei geaenderter Bildschirmkonfiguration
  endet die aktuelle Sitzung. Maus/Tastatur beendet Vollbild.
- Eigenstaendiger SCR-Host mit `asInvoker`, **kein UAC**, keine neue
  Sensor-/Treiberinitialisierung. `/s` startet Vollbild, `/c` zeigt Hinweise,
  `/p <Handle>` oder `/p:<Handle>` bettet die Windows-Vorschau ein.
- Messwerte stammen ausschliesslich aus laufendem AETHER desselben Kontos
  und derselben Windows-Sitzung, ueber eine lesende lokale Named Pipe.
  Explizite SID-Zugriffsregel erlaubt die nicht erhoehte Anzeige; Netzwerkzugriff
  und andere Konten sind ausgeschlossen, der Client prueft den Eigentumer.
  Keine Steuerbefehle, Kennwoerter oder dauerhafte Messwertdateien.
- Ohne Verbindung zeigt der SCR eine rein lesend erfasste Hardwareansicht
  und **KEINE LIVE-MESSWERTE**; alte Messwerte werden nicht weiter als live
  angezeigt. Fehlende Werte bleiben `--`. Internetstatus ist eine Momentaufnahme.
  Windows-Animationseinstellung wird beachtet; bei Verbindung auch der
  AETHER-Bewegungsschalter.
- Optionales Setup-Haekchen **Bildschirmschoner mitinstallieren**; beim Update
  ggf. **Konfiguration neu festlegen** waehlen, damit die Auswahlseite erscheint.
  Keine automatische Aktivierung oder Aenderung der Windows-Sperre.
  Ab Beta 24 erscheint **AETHER Mainframe 1986** direkt in der Windows-Liste.
  Dort bewusst auswaehlen und mit **Uebernehmen** aktivieren;
  Wartezeit und **Anmeldeseite bei Reaktivierung** dort selbst festlegen.
  Das ist kein Ersatz fuer eine Windows-Sperre und keine eigene Sperrsoftware.
- Portable: gesamtes ZIP entpacken, SCR im Ordner belassen. Sie braucht
  `AetherScreensaver.*`, die AETHER-DLLs und die mitgelieferte Laufzeit neben sich.
  Einzelne SCR-Datei nicht nach System32 kopieren.

Release inklusive SCR reproduzierbar veroeffentlichen:
`powershell -NoProfile -File .\build\Publish-Beta.ps1`.
Gezielte UI-Tests:
`dotnet run --project .\tests\SystemCockpit.Tests -c Release -- --xbar-ui --desktop-ui --board-ui --screensaver-ui`.

### 0.7.0-beta.13: Setup-Autostart korrigiert

- Fehler `Runtime error (at 55:5699)` nach erfolgreichem Dateikopieren:
  Die COM-Aufgabenregistrierung uebergab ein leeres Kennwort statt
  ausgelassener Anmeldedaten fuer `InteractiveToken`. Reine
  Definitionsvalidierung pruefte diese echte Registrierung nicht.
- Registrierung jetzt mit Windows `schtasks.exe` und korrekt codierter
  Aufgaben-XML, ohne Kennwort. Gleiche erhoehte Anmeldeaufgabe, gleicher
  `--tray`-Start und weiterhin ausdrueckliche Zustimmung. Exitcode und
  Windows-Fehlermeldung werden ausgewertet und im Setup-Protokoll festgehalten.
- Nach fehlgeschlagener Autostart-Einrichtung in Beta 12: AETHER im Tray
  vollstaendig beenden, neues Setup starten, im Update-Dialog
  **Konfiguration neu festlegen** auswaehlen und Autostart erneut genehmigen.
  Nicht vorher deinstallieren. Ein erhaltendes Update repariert absichtlich
  keine Aufgabe ohne erneute Freigabe.
- Standardtest: `powershell -NoProfile -File .\tests\Verify-Setup.ps1`
  prueft Profil, XML, Update und Zustimmung ohne Aufgabenregistrierung.
  Nur mit ausdruecklicher Freigabe:
  `powershell -NoProfile -File .\tests\Verify-Setup.ps1 -RegisterIsolatedTask`
  registriert eine eindeutig benannte deaktivierte Testaufgabe ohne Trigger,
  prueft sie und entfernt sie wieder. Bestehende AETHER-Aufgabe bleibt unberuehrt.

### 0.7.0-beta.12: Raeumliche Hardware-Skizze statt Blockdiagramm

- **Cockpit -> Mainboard / 1986** zeichnet jetzt eine isometrische Platine:
  CPU-Sockel mit stilisiertem Kuehler, bestueckte RAM-Riegel, GPU mit Lueftern,
  Chipsatz/Kuehlrippen, Netzwerk-/USB-Buchsen, Audio, SSD und bei gemeldetem
  SATA/AHCI passende Anschluesse. Eigenstaendige Vektorgrafik, kein fremdes Bild.
  Keine rechteckigen Gruppenkacheln mehr; Typnamen und Windows-Status stehen
  an Hinweislinien. Zeichnung und Beschriftung sind anklickbar fuer alle Details.
- Formen, Kuehler und Anordnung sind symbolisch und beweisen weder Bauform
  noch physische Bestueckung/Verkabelung (z.B. integrierte vs. Steckkarten-GPU).
  Gruppen werden anhand erkannter Hardware bestueckt, nicht erfunden.
  Sammelgruppen und begrenzte Repraesentationen bleiben vollstaendig in Details.
- Animierte gestrichelte Pfade zeigen **schematischen Datenfluss**, keine
  gemessenen Paketwege/Richtung/Geschwindigkeit. Der Animationsschalter gilt
  gemeinsam mit Scanner-Animation; unsichtbare/geschlossene Ansichten stoppen
  ihre Animationsuhren. WAN-Pfad nur bei Windows-Meldestatus Internet verbunden.
- Echte CPU-/RAM-/GPU-Auslastung, Upload/Download und CPU-Temperatur stehen
  separat ueber der Zeichnung und stammen aus den vorhandenen Live-Messwerten.
  Unverfuegbare Werte bleiben unverfuegbar. Keine Messung pro Leiterbahn.

### 0.7.0-beta.11: Retro-Mainboard und erkannte Update-Installation

- **Cockpit -> Mainboard / 1986**: eigene umschaltbare Neon-Platinenansicht.
  Lokal vorhandene Windows-PnP-Geraete, gemeldete RAM-Baenke und Mainboardname.
  CPU, RAM, GPU, Controller, Netzwerk/Internet, USB, Storage, Sound und weitere
  Peripherie sind gruppiert anklickbar; alle erfassten Geraete bleiben in
  den Gruppendetails erreichbar. SATA wird nicht fuer NVMe-only-Systeme erfunden.
  Fehlerstatus bleibt sichtbar; leere Gruppen gelten nicht als erkannte Hardware.
- Stilisierte Leiterbahnen sind **logische** Verbindungen, keine gemessene
  elektrische Topologie/PCIe-Lane-Belegung und keine Datenflussanimation.
  Gemeldete Windows-Elternbeziehungen werden separat angezeigt.
  Internetstatus kommt von Windows, ohne eigenen Online-Test.
  **HARDWARE NEU LESEN** aktualisiert die Momentaufnahme, z.B. nach USB-Hotplug.
- Setup erkennt vorige Setup-Installation samt Version/Ordner anhand des
  stabilen Windows-Installationseintrags. **Vorher nicht deinstallieren**,
  aber AETHER im Tray vollstaendig beenden. Programmdateien werden am bisherigen
  Ort ersetzt. Standardmaessig bleiben Desktop-Profil, Nutzerdaten und
  Autostart-Aufgabe erhalten. Eine separate Update-Auswahl erlaubt ausdruecklich
  neue Diagramm-/Autostart-/Mitschnitt-Konfiguration.
  Keine Online-Updatesuche. Portable Kopien haben keinen Installationseintrag.
- Neue [Betatester-Anleitung](BETA-TESTANLEITUNG.md) beschreibt Funktionen,
  Installations-/Updateweg, sicheren Testrundgang, Grenzen und Fehlerberichte.
  Sie liegt auch dem Portable-Paket und der installierten Ausgabe bei.
- Setup-Regression umfasst Neuinstallation mit/ohne Autostart sowie Update
  mit Beibehalten/Neukonfigurieren. Keine echten Aufgabenregistrierungen.
  Board-Tests: Gruppenvollstaendigkeit, fehlende Hardware, SATA-Status,
  Windows-Eltern-ID und synthetischer WPF-Sichttest.

### 0.7.0-beta.10: Setup-Korrektur

- Behebt `Runtime error (at 46:350): Type Mismatch` am Installationsende:
  Die Konfigurations-ID verwendet gueltige einzelne Datumstrennzeichen.
- Der angebotene App-Start am Setup-Ende behaelt den erhoehten
  Administrator-Kontext des Setups; dadurch entfaellt `CreateProcess failed;
  code 740`. Die App fordert weiterhin Administratorrechte an.
- Bei einer unvollstaendigen Beta-9-Installation zuerst AETHER ueber das
  Tray-Menue vollstaendig beenden, dann das Beta-10-Setup erneut ausfuehren.
  Keine vorherige Deinstallation noetig; Benutzereinstellungen bleiben erhalten.
- Regressionstest: `powershell -NoProfile -File .\tests\Verify-Setup.ps1`.
  Kompiliert denselben Setup-Code in einer isolierten Testausgabe ohne
  Produktdateien, Verknuepfungen oder Deinstallationseintrag. Prueft
  Profilgenerierung, Zustimmungen und Aufgaben-XML mit `TASK_VALIDATE_ONLY`,
  ohne eine Aufgabe anzulegen/zu loeschen oder die App/Sensoren zu starten.
  Echter erhoehter App-Start und reale Aufgabenregistrierung bleiben
  separate manuelle Installationstests.

### 0.7.0-beta.9: Desktop-Gadgets, Transparenz, Tray und Netzwerkbeobachtung

**Bedienung:** Symbol bei der Uhr -> Desktop / Transparenz / Parameter.
Alternativ: Erweiterte Systemkontrolle -> Einstellungen -> Desktop-Gadgets.
Hauptfenster, xBar und Gadgets haben getrennte Deckkraftregler (20-100 %).
Die xBar hat einen eigenen Regler im Zahnrad-Menue; jedes Gadget hat ebenfalls
einen individuellen Regler. Die Mindestdeckkraft verhindert unsichtbare,
nicht mehr bedienbare Fenster.

Die ausgewaehlten CPU-/RAM-/GPU-, Temperatur-, Netzwerk- und Laufwerksdiagramme
koennen als einzelne frei verteilbare Desktop-Gadgets angezeigt werden.
Sie nutzen dieselben Retro-Neon-Diagrammvorlagen wie die xBar, sitzen auf der
Explorer-Desktop-Ebene hinter normalen Apps und behalten Position/Deckkraft.
Am Gadget-Titel ziehen. Jede Temperatur, jede Netzwerkrichtung und jedes
Laufwerk bekommt ein eigenes Gadget. Explorer muss die Desktop-Ebene anbieten;
bei fehlender Unterstuetzung wird der Fehler gemeldet, kein Topmost-Ersatz
vorgegeben. Explorer-Neustarts werden bei aktiven Gadgets erneut angebunden.

**Wichtig: X am Hauptfenster blendet es jetzt nur aus.** Messungen und Gadgets
laufen weiter. Das Tray-Menue kann Gadgets minimieren/wiederherstellen,
dauerhaft schliessen oder **AETHER vollstaendig beenden**.
Manuelle Starts versuchen das Fenster in den Vordergrund zu holen; Windows
kann Fokuswechsel einschraenken. Es wird nicht dauerhaft Topmost erzwungen.
Ein weiterer manueller Start aktiviert die bereits laufende Instanz desselben
Kontos in derselben Windows-Sitzung, statt weitere Sensorprozesse zu erzeugen.

**Setup und Autostart:** Das Setup fragt die Diagrammgruppen und Gadgets ab.
Ausgewaehlte Werte werden beim naechsten Start einmalig uebernommen; alle anderen
Einstellungen bleiben erhalten. Die optionale Autostart-Aufgabe startet bei
Benutzeranmeldung nur Gadgets/Tray, ohne Bootfenster oder Hauptfenster.
Sie benoetigt eine separate ausdrueckliche Zustimmung im Setup, laeuft
erhoeht fuer das dort angezeigte Administratorkonto und verwendet keine
Kennwortspeicherung und keinen Windows-Dienst. Bei abweichenden Benutzer-/
Administratorkonten ist dies kein Autostart fuer das andere Benutzerkonto.
Ein Upgrade mit abgewaehltem Autostart oder die Deinstallation entfernt die
Aufgabe. Autostart und Aufgabenerstellung werden nicht bei portablem Start
eingerichtet. Einmal manuell starten und die aktualisierte EULA bestaetigen,
bevor der unbeaufsichtigte Anmeldestart arbeitet.

**Netzwerk:** Tray -> Netzwerkueberwachung (auch in den Einstellungen erreichbar).
Die separat aktivierbare lokale TCP-Verbindungszaehlung benoetigt keinen
Capture-Treiber. Bei konfigurierter Schwelle erscheint ein Hinweis.
Optional kann ein **bereits separat installiertes/lizenziertes Npcap** genutzt
werden. AETHER liefert Npcap nicht mit, installiert keinen Treiber und akzeptiert
keine Fremdlizenz stellvertretend. Details: https://npcap.com/ und
https://npcap.com/guide/npcap-devguide.html .

- Npcap-Schnittstellen lesen, eine Ethernet-kompatible Schnittstelle waehlen,
  Mitschnitt ausdruecklich bestaetigen. Kein Promiscuous-Modus.
- Maximal **128 Bytes je Paket**, **100 MB (100.000.000 Bytes) oder 15 Minuten pro Mitschnitt**.
  Keine automatische Verlaengerung. Die Begrenzung ist keine Anonymisierung:
  IP-Adressen, DNS-Metadaten und vertrauliche Nutzdaten koennen enthalten sein.
- Speicherung als Wireshark-kompatible **PCAP** unter
  `%LOCALAPPDATA%\AetherCockpit\Captures`. Keine Cloud-Uebertragung.
  Eigene Mitschnitte aelter als sieben Tage werden beim naechsten
  Aufbewahrungslauf entfernt (stuendlich im Leerlauf bzw. vor Mitschnittstart).
  Vor einem neuen Mitschnitt wird der Altbestand zusaetzlich auf 400 MB
  reduziert; einschliesslich laufender Datei maximal 500 MB.
- Automatischer Mitschnitt bei Anmeldung ist standardmaessig aus und verlangt
  separate Zustimmung, Schnittstellenauswahl und eine installierte Autostart-
  Konfiguration. Spaeter in Netzwerkueberwachung deaktivierbar.
- Begrenzte lokale Auswertung von Ethernet/VLAN, IPv4/IPv6, TCP/UDP/ICMP und
  DNS-Fehlerkoepfen. Nicht unterstuetzte Linktypen wie Loopback werden
  ausdruecklich abgelehnt. Keine TLS-Entschluesselung, keine vollstaendige
  Offline-Protokollanalyse,
  keine Prozesszuordnung der Pakete und keine Garantie, Angriffe zu erkennen.
- Regelalarme: viele verschiedene TCP-SYN-Zielports je Sender/Ziel und Minute,
  viele DNS-Fehlerantworten je Empfaenger und Minute sowie hohe lokale
  TCP-Verbindungszahl. Schwellen sind einstellbar. Das sind Indizien, keine
  Angriffsnachweise; Fehlalarme sind moeglich.
- Hinweise erscheinen im Tray ohne Ton und in einem lokalen, begrenzten
  Sieben-Tage-Alarmprotokoll (max. 2 MiB pro Tag). Benachrichtigungen werden auf
  eine je 30 Sekunden begrenzt; Windows kann sie unterdruecken.
  Es gibt keine automatische Blockierung oder Reparatur.
- Die Live-Anzeige ist auf 200 Pakete begrenzt und kann bei Last Pakete
  auslassen, ohne die PCAP-Aufzeichnung zu beschneiden. Treiberverluste werden
  damit nicht gemessen; vollstaendige Verkehrserfassung wird nicht garantiert.

### 0.7.0-beta.8: Reine, konfigurierbare xBar-Diagrammleiste

**DIAGNOSE -> AETHER xBar AN/AUS**, dann das Zahnrad fuer die Konfiguration.
Die Leiste zeigt ausschliesslich grafische Messwidgets; keine App-/Datei-/
Ordnerlisten, Windows-Schnellzugriffe oder Systemaktionen mehr.
Sechs Diagrammgruppen sind unabhaengig ein-/ausblendbar und werden gespeichert:

- **CPU, RAM, GPU:** jeweils Retro-Neon-Instrument plus Live-Verlauf.
- **Temperaturen:** eigene Kurven fuer CPU, GPU und den heissesten gueltigen
  SSD-/Storage-Temperaturkanal. Grenzwerte sind keine Messwerte.
- **Netzwerk:** Download und Upload mit dynamisch skalierten Live-Kurven.
- **Laufwerksbelegung:** je Laufwerk ein Tortendiagramm; lila = belegt,
  tuerkis = frei, Prozentwerte und Legende direkt am Diagramm.

Die Diagramme verwenden die echten Cockpitmessungen, halten maximal 90
Messpunkte im RAM und loeschen ihren Verlauf bei fehlenden Messwerten.
Nicht verfuegbare Belegungsdaten erscheinen als neutraler Kreis mit
entsprechender Beschriftung, niemals als 0 oder 100 Prozent.
Bei vielen eingeschalteten Diagrammen laesst sich die Leiste scrollen.
PIN/FLOAT, Monitorwahl, freie Positionsspeicherung und die Beta-7-DPI-Korrektur
bleiben erhalten; Auto-hide bleibt auf angehefteten Einzelmonitorbetrieb
beschraenkt. Alte Shortcut-Einstellungen bleiben kompatibel gespeichert,
werden aber nicht mehr angezeigt oder ausgefuehrt.

### 0.7.0-beta.7: xBar-Position und freies Verschieben korrigiert

Die Leiste beruecksichtigt die Windows-Skalierung und den Arbeitsbereich
einschliesslich Taskleiste, damit sie vollstaendig auf dem Zielmonitor bleibt.
**PIN -> FLOAT** schaltet auf freie Positionierung um: am Titel ziehen,
loslassen, kein automatisches Andocken. Beide Positionsachsen werden gespeichert
und beim naechsten Oeffnen wiederhergestellt. Ein zuvor aktiviertes Auto-hide
bleibt im FLOAT-Modus wirkungslos. Vollbildanwendungen koennen die Leiste
verdecken, ohne ihre gespeicherte Position zu veraendern.

### 0.7.0-beta.5: Andockbare Sensorleiste und Windows-Setup

Beta 5 ergaenzte ein kleines, links/rechts andockbares Sensorpanel und das
Windows-Setup. Das war noch kein vollwertiger Launcher mit App-, Datei- und
Ordnerkategorien oder den Systemsteuerungsfunktionen dieser Version.

### 0.7.0-beta.6: AETHER xBar-Seitenleiste (historisch)

Beta 6 fuehrte eine Launcher-Seitenleiste mit eigenen Verknuepfungen,
Windows-Werkzeugen, Messwertwidgets und bestaetigungspflichtigen Systemaktionen
ein. Ab Beta 8 wurde dieser Inhalt auf Nutzerwunsch durch reine Diagramme
ersetzt. Das bestehende AETHER-Retro-Neon-Design bleibt erhalten.

Das selbstenthaltene Setup installiert nach `Program Files`, erstellt eine
Startmenue-Verknuepfung und bietet optional eine Desktop-Verknuepfung an. Die
Deinstallation entfernte damals nur Programmdateien; ab Beta 19 gilt die oben
beschriebene vollstaendige Entfernung inklusive Kontodaten. Alte portable
Programmordner bleiben erhalten.

### 0.7.0-beta.3: Lesbare PowerShell-Fehlertexte; BitLocker-Abfragefix

PowerShell-Fehlermeldungen (CLIXML-Datenstrom) werden jetzt als lesbarer
Klartext angezeigt statt als rohe `<S S="Error">`-XML-Zeilen mit
`_x000D_`-Escapes - z.B. die ehrliche Meldung "Kein OEM-Firmware-
Produktschluessel vorhanden" bei digitaler Lizenz. Inhaltlich unveraendert:
AETHER erfindet weiterhin keinen Schluessel.
Zusaetzlich behoben: Die BitLocker-Abfrage brach faelschlich mit "keine
Protektoren" ab, sobald der erste Protektor eines Volumes kein
RecoveryPassword war; die Pruefung laeuft jetzt nach dem vollstaendigen
Durchlauf aller Volumes/Protektoren.

### 0.7.0-beta.2: Skins wirken sofort; Erststart immer im Original

Der Skin-Wechsel (Systemkontrolle -> Einstellungen -> Skin) wirkt jetzt
**sofort auf alle offenen Fenster** und wird automatisch gespeichert -
kein Neustart mehr noetig. Technisch beziehen alle Oberflaechen die
Theme-Brushes per DynamicResource; ein Wechsel ersetzt die Ueberlagerung,
Retro-Neon stellt das unveraenderte Original wieder her.
Der **Erststart-Assistent laeuft bewusst immer im Original-Design**;
der gespeicherte Skin wird erst nach Abschluss des Assistenten angewendet.

### 0.7.0-beta.1: Erststart-Assistent, EULA und Rechner-Inventar

Fuer die Weitergabe an Tester: Beim allerersten Start fuehrt ein kurzer
Assistent durch drei Schritte.

- **Funktionsuebersicht** - eine Einfuehrung, was AETHER kann.
- **Endnutzer-Lizenzvereinbarung (EULA)** mit Pflicht-Zustimmung: ohne
  Zustimmung wird das Programm beendet. Die Zustimmung wird einmalig lokal
  gespeichert (`%LOCALAPPDATA%\AetherCockpit`); der Assistent erscheint
  danach nicht erneut. Die EULA nennt Kostenfreiheit, Nutzung auf eigenes
  Risiko (kein Gewaehrleistungsausschluss zwingender Rechte), Administrator-
  rechte, lokale Verarbeitung und die Fremdlizenzen (MPL 2.0 / GPL v2).
- **Rechner-Inventar** - lesende Momentaufnahme aus Hardware (ueber die
  Windows-Abfrage) und installierter Software (Registry), als Textdatei
  speicherbar. Spaeter jederzeit ueber DIAGNOSE -> RECHNER-INVENTAR.

Das Inventar ist rein lesend; nicht verfuegbare Werte bleiben als solche
gekennzeichnet. Es bleibt lokal und kann Rechnernamen/Seriennummern
enthalten - nur bewusst teilen.

### 0.6.0-beta.2: Einzeiliger Fortschrittsbalken in der Konsole

Wagenruecklauf-Fortschritte (z.B. der DISM-Prozentbalken) aktualisieren in
der CRT-Konsole jetzt **eine einzelne Zeile an Ort und Stelle**, wie in einem
echten Terminal - statt den Verlauf mit Wiederholungszeilen zu fluten.
Abgeschlossene Zeilen bleiben unveraendert; auch ueber mehrere
Ausgabestuecke verteilte Fortschrittsmeldungen werden korrekt zusammengefuegt.
Am Transport aendert sich nichts: STDOUT/STDERR werden weiter vollstaendig
gelesen, das 2-Millionen-Zeichen-Limit bleibt.

### 0.6.0-beta.1: Entschachtelt + vier Skins

**Weniger Verschachtelung - alles einen Klick entfernt:**

- Die linke Leiste hat eine neue Gruppe **SYSTEMKONTROLLE / DIREKT**:
  `SK Pakete / WinGet`, `SK Befehls-Katalog`, `SK SMART / Laufwerke` und
  `SK Netzwerk / Speedtest` oeffnen die Systemkontrolle direkt im richtigen
  Modul - der Umweg Diagnose -> Fenster -> Tab entfaellt.
- Kontextknoepfe springen jetzt zielgenau: "Warnschwellen / ATX" ->
  Einstellungen, "SMART-Diagnose" -> SMART, "Autostart verwalten" ->
  Autostart-Modul.
- Behobener Nebenfund: "Einstellungen speichern" setzte den gewaehlten
  Einsteiger-/Expertenmodus auf Standard zurueck; er wird jetzt uebernommen.

**Vier Skins (Systemkontrolle -> Einstellungen -> Skin):**

- **Retro-Neon (Original, Standard)** - unveraendert.
- **Vista Aero Glas (dunkel)** - blaues Glas mit hellen Glanzkanten.
- **Candy Glossy (dunkel)** - Bonbon-Optik mit starken Glanzlichtern.
- **Windows 11 26H2 Fluent (dunkel)** - flach, dezent, Mica-artig.

Skins stylen die Fensteroberflaeche (Hintergruende, Panels, Seitenleiste,
Knoepfe) ueber zentrale Theme-Brushes; die Neon-Akzentfarben der Inhalte
bleiben in allen Skins erhalten, weshalb alle Skins dunkle Varianten sind.
Der gewaehlte Skin wird nach dem Speichern beim naechsten Programmstart
angewendet; bei unlesbaren Einstellungen bleibt Retro-Neon aktiv.

### 0.5.0-beta.4: GodMode-Knopf windowsbuild-bewusst

Microsoft hat die "Alle Aufgaben"-Ansicht (GodMode-CLSID `ED7BA470-...`)
auf neueren Windows-11-Builds entfernt; der direkte Aufruf erzeugte dort
eine Fehlermeldung bzw. einen leeren Ordner. Der Knopf prueft jetzt lesend
die Registrierung: Ist die Ansicht vorhanden, oeffnet er sie ueber die
klassische GodMode-Ordnermethode (Ordner unter `%LOCALAPPDATA%\AetherCockpit`).
Ist sie entfernt, oeffnet er stattdessen **Alle Systemsteuerungselemente**
und erklaert das in der Fusszeile - nichts wird vorgetaeuscht.

### 0.5.0-beta.3: Freie Navigation ohne Hauptleiste

Neue Navigationsleiste direkt ueber der Inhaltsflaeche - immer sichtbar,
auch tief in Untermenues:

- **ZURUECK / VOR** folgen dem eigenen Besuchsverlauf wie im Browser
  (maximal 100 Schritte, rein im RAM); ein neuer Besuch nach Zurueck
  verwirft den Vorwaerts-Zweig.
- **COCKPIT** springt direkt zur Hauptebene, **HILFE** oeffnet das
  Hilfe-Center; die Brotkrumen-Zeile zeigt die aktuelle Position.
- Ueberall funktionieren die **Maus-Daumentasten** (Zurueck/Vor) und
  **Alt+Pfeil links/rechts** - im Hauptfenster und in der erweiterten
  Systemkontrolle.
- Die **Systemkontrolle** hat ihre eigene Leiste (Verlauf ueber die Module
  SMART/Pakete/Wartung/...) plus **HAUPTFENSTER**-Knopf.
- Linke Rubrikenleiste folgt jetzt exakt der Seitenreihenfolge und
  enthaelt **12 Hilfe**; die Knoepfe springen per Seitenname, nicht per
  Index. Kopfbereich kompakter, damit die Inhaltsflaeche (z.B. alle
  24 Core-Graphen) ihre gepruefte Groesse behaelt.

### 0.5.0-beta.2: Kernelzugriffs-Diagnose (CPU-Sensoren unter Kernisolierung)

Fehlen CPU-Temperatur und CPU-Package-Leistung, waehrend GPU-Werte normal
kommen, liegt das haeufig an aktiver Kernisolierung/Speicherintegritaet
(HVCI): Der klassische WinRing0-Treiber steht auf der Microsoft-Blockliste
und kann nicht laden. AETHER erklaert das jetzt selbst:

- Der **CPU-/Sensorproviderbericht** beginnt mit einer
  `[KERNELZUGRIFF]`-Zeile (PawnIO vorhanden / HVCI aktiv und PawnIO fehlt).
- Die **CPU-Kachel** nennt bei komplett fehlenden CPU-Werten die Ursache
  und die Loesung direkt im Detailtext.
- **Loesung ohne Schutzabschaltung:** den signierten, HVCI-kompatiblen
  **PawnIO-Treiber** installieren (https://pawnio.eu), danach AETHER neu
  starten. LibreHardwareMonitorLib 0.9.6 nutzt PawnIO automatisch.
  Kernisolierung nicht abschalten; AETHER installiert den Treiber nicht
  selbst. Hilfe-Thema "Sensor-Matrix und Grenzwerte" beschreibt den Weg.

### 0.5.0-beta.1: Risikoanzeige / Einsteigermodus / Hilfe-Center / Export / neue Struktur

**Risikoanzeige im Befehlskatalog:** Jeder der 103 Katalogbefehle traegt eine
farbige Stufe - RISIKO: LESEND (gruen), NETZWERK (gelb) oder AENDERND
(orange) - direkt aus der Impact-Kennzeichnung abgeleitet. Der Detailbereich
zeigt zusaetzlich je Kategorie SINNVOLL (wann der Befehl hilft) und VORSICHT
(was zu beachten ist) sowie das erkannte Windows (Produkt, Version, Build).

**Einsteiger-/Expertenmodus (Wartung):** Ein gespeicherter Schalter.
Einsteiger sieht nur die lesende, sichere Kernliste; der freie Konsolenstart
ist gesperrt und erklaert das. Experte (Standard, bisheriges Verhalten)
behaelt den vollen Katalog mit direkter Enter-Ausfuehrung. Gefuehrte
Aktionen behalten in beiden Modi ihre Bestaetigungsdialoge.

**Hilfe-Center (neuer Haupttab):** Lokale, durchsuchbare Hilfe mit ueber 18
Themen - je Modul eine Schritt-fuer-Schritt-Anleitung, Sicherheitsregeln und
ehrliche Grenzen. Keine Online-Verbindung.

**Speicherdiagnose-Viewer (Diagnose):** liest vorhandene Ergebnisse der
Windows-Speicherdiagnose aus dem System-Ereignislog. Einen neuen RAM-Test
planst du selbst per `mdsched.exe`; AETHER startet ihn nie automatisch.

**Diagnosebericht-Export (Diagnose):** Textbericht aus den aktuellen
Cockpit-Werten mit Vorschau. Die standardmaessig aktive Schwaerzung ersetzt
Rechnername, Benutzername, Benutzerpfade sowie IPv4-/MAC-Adressen durch
Platzhalter - fuer Foren/Support. Schwaerzung ist Mustererkennung, keine
Garantie: Vorschau vor dem Teilen pruefen.

**Neue Struktur und Bedienung:** Haupttabs folgen jetzt dem Arbeitsfluss
Beobachten (Cockpit, Taskmanager, Sensoren, Laufwerke) - Auswerten
(Messauswertung) - Pruefen (Diagnose, Sicherheit) - Eingreifen (Cleaner,
Ereignisse, Superadmin, Operations) - Hilfe. Jeder Haupt- und
Systemkontroll-Tab erklaert sich per Tooltip.

### 0.4.0-beta.1: Paket-Center / Overlay / Spuren / Speedtest / Bewertung / Schnellzugriffe

**Paket-Center (Modul "Pakete" in der erweiterten Systemkontrolle):**
Installierte Pakete, verfuegbare Updates und Katalogsuche ueber die lokale
WinGet-Installation; Installieren, Aktualisieren und Deinstallieren per
Mehrfachauswahl. Jede Aktion verwendet ausschliesslich exakte Paket-IDs mit
`--exact` und wird vor dem Start mit der vollstaendigen ID-Liste bestaetigt.
Der Knopf **Alle aktualisieren** nutzt genau die zuletzt geladene und
angezeigte Update-Liste - niemals ein blindes `winget upgrade --all` an der
Anzeige vorbei. Die winget-Tabellenausgabe ist konsolenbreitenorientiert:
Eintraege mit abgeschnittener ID (Ellipse) sind als "nicht aktionierbar"
markiert und werden uebersprungen statt geraten. Fehler einzelner Pakete
stoppen die weiteren IDs nicht und werden pro ID als STDERR gemeldet.
Quell-/Paketlizenzen werden durch die Bestaetigung akzeptiert; Fremdinstaller
koennen eigene Fenster zeigen. Deinstallationen laufen ohne erzwungenes
`--silent`. Exitcode 0 ersetzt keine Funktionspruefung der Programme.

**Overlay-Panel (Knopf im Modul Diagnose):** kompaktes Always-on-top-Panel
mit CPU-/GPU-/SSD-Temperatur und CPU-Last aus denselben Modellwerten wie das
Cockpit. Warnschwellen pro Komponente stammen aus den bestehenden
Einstellungen; bei Ueberschreitung faerben sich Werte rot, ein Banner nennt
die betroffenen Komponenten und ein einmaliger Hinweiston ertoent beim
Uebergang in den Warnzustand. Nicht verfuegbare Sensoren bleiben
"Nicht verfuegbar". Exklusives Vollbild kann das Overlay verdecken; das ist
eine Plattformgrenze. Kein automatischer Start des Overlays.

**Shellbag-Analyse (Modul "Spuren"):** liest die Explorer-Ordnerspuren
(BagMRU) aus HKCU ausschliesslich lesend - auch Spuren geloeschter Ordner.
Namensdekodierung ist Best Effort (UTF-16-Langname aus BEEF0004 ab Version 7,
sonst Kurzname); nicht dekodierbare Elemente werden ausdruecklich als
unbekannt angezeigt, nie erfunden. Limit 2000 Eintraege. Der Zeitstempel
gehoert zum Registry-Schluessel, nicht zwingend zum letzten Ordnerbesuch.
AETHER bietet bewusst keine Shellbag-Loeschung an.

**Speedtest (Modul Netzwerk):** expliziter Download-Durchsatztest
(1..200 MiB, Standard 25) gegen `speed.cloudflare.com` ueber eine
HTTPS-Verbindung, nur nach Freigabe mit Datenvolumen-Hinweis; die eigene IP
wird dem Messziel sichtbar. Ergebnis ist der Anwendungsdurchsatz dieser einen
Verbindung, kein Upload-Test und keine Tarif- oder Leitungsgarantie.
Zeitlimit 120 Sekunden.

**Bereinigungsbewertung (Cleaner):** die Vorschau fasst Stufe (A risikoarm
bis D systemnah, aus dem Cache-Katalog), Dateianzahl, MiB und den
Katalog-Hinweis zusammen. Keine Magie-Punktzahl: alle Zahlen sind die realen
Summen der aktuellen Vorschau; an den bestehenden Loeschregeln aendert sich
nichts.

**Schnellzugriffe (Diagnose):** zusaetzliche Direktstarts fuer
Aufgabenplanung, Dienste und den Windows-GodMode-Ordner ("Alle Aufgaben"
im Explorer). Es oeffnen sich die originalen Windows-Werkzeuge.

Hinweis zur bisherigen Linie: Fuer das Paket-Center wurde die Regel
"nur einzeln eingetippte exakte IDs" bewusst zu "Mehrfachauswahl/alle aus der
angezeigten Liste, mit vollstaendiger ID-Liste im Bestaetigungsdialog"
erweitert. Die Operations-Wartungsplaene behalten ihre bisherigen strengeren
Regeln unveraendert.

### Beta 9: Lesbare LED-Ziffern / kontrastreiches Icon

LED-Anzeigen respektieren jetzt ihre tatsaechliche Feldhoehe statt mindestens
32 Pixel zu beanspruchen. Dadurch bleiben auch in den kompakten Core-, RAM-
und Disk-Feldern die unteren Ziffernsegmente sichtbar. Core-, Disk- und
Detailgraph-Zahlen sind ausserdem vergroessert. Rastertests pruefen
alle verwendeten LED-Hoehen bei 100, 125, 150 und 200 Prozent Skalierung.
Das gemeinsame EXE-/Fenster-Icon verwendet ein deutliches cyan/magenta
AETHER-Symbol in sieben Groessen. Reproduzierbar mit `build\New-AppIcon.ps1`.

### Beta 8: LED-Instrumente / Operations / Arbeitsgruppen

Klassische sieben-segmentige LED-Ziffern ersetzen normale Zahlenschrift bei
den Hauptinstrumenten, Detailgraphen, Core-Prozenten, Disk-Instrumenten,
Uhr und Leistungsindex. CPU-Cores leuchten gruen, Disk-Instrumente und Uhr
rot; RAM/weitere Kanaele bleiben neon-blau, cyan und magenta.
Ziffern bleiben auch ueber Windows-Accessibility als Text abfragbar.

**Taskmanager:** alle logischen CPU-Graphen teilen sich einen proportional
vergroessernden Bereich ohne ScrollViewer. Spaltenzahl wird aus der Core-Anzahl
abgeleitet; Prozessliste scrollt weiterhin separat. Fuer die 24 logischen CPUs
dieses Rechners sind alle Graphen im maximierten Fenster sowie bei
1920x1080 und 1440x940 gemeinsam auf Sichtbarkeit und Mindestgroesse geprueft.
Keine Garantie beliebig vieler lesbarer Graphen auf beliebig kleinen Displays.

**Cockpit:** CPU-Identitaet, Gesamtlast-Rundinstrument, RAM/Commit/Cache,
CPU-Temperatur/Package-Leistung, alle Cores und kompakte Read-/Write-/
Aktivzeit-/Queue-Diagramme je physischem Datentraeger stehen direkt in der
oberen Performance-Uebersicht. Nicht lesbare Sensoren bleiben unverfuegbar.
Nichtprozentwerte verwenden beschriftete dynamische Skalen; keine erfundene
Disk-Gesundheitsnote. Das umfangreiche Cockpit bleibt insgesamt scrollbar.

**Cleaner:** explizite `LOESCHEN?`-Checkboxen, `Sichtbare ankreuzen`, `Keine ankreuzen`
und `Markierte Zeilen ankreuzen`. Markieren/Sortieren allein ist KEINE
Loeschfreigabe. Auswahlzaehler zeigt Anzahl/MiB; alle Dateien starten
unangekreuzt. Erst danach bestaetigte, erneut verifizierte Loeschung.
Kein Papierkorb oder Wiederherstellungsversprechen fuer geloeschte Dateien.

**Operations** ist die elfte Hauptseite:

- **Leistungsindex:** gespeicherte `Win32_WinSAT`-Bewertung mit Zustand,
  CPU/RAM/Desktopgrafik/Systemdisk und dem von Windows gemeldeten Basisscore.
  Alte/ungueltige/hardware-inkonsistente oder fehlende Messungen liefern `--`,
  keine Null- oder erfundenen Noten. WinSAT ist auf neueren Windows-Versionen
  eingeschraenkt; Gaming/D3D wird nach Windows 8.1 nicht neu berechnet und
  wird hier deshalb nicht als echter Benchmark angezeigt. Moderne Skala
  bis 9,9, nicht die alte Windows-7-Obergrenze 7,9. DataStore-Dateizeit wird
  getrennt und nicht als garantierte Messzeit bezeichnet.
  `WinSAT.exe formal` startet nur nach ausdruecklicher Last-/Dateischreib-
  Bestaetigung. Kein Benchmark beim Boot.
  Quelle: [Microsoft Win32_WinSAT](https://learn.microsoft.com/en-us/windows/win32/winsat/win32-winsat).
- **Wartungsplan:** selektiver Ansatz - jede Kategorie wird einzeln gewaehlt,
  in der Vorschau geprueft und einzeln bestaetigt.
  17 Kategorien: Inventar, Ereignisse, Autostart/Dienste/Aufgaben,
  Komponentenanalyse, NTFS-Onlinescan, DISM-Reparatur, SFC, gefuehrter Cleaner,
  Komponentenbereinigung, mediengerechte Laufwerksoptimierung, Defender-
  Signaturen/Schnell-/Vollscan, Windows-Updatesuche/-auswahl und exakte
  WinGet-App-Updates. Der Cleaner ist bewusst ein separater Vorschauablauf,
  keine unbeaufsichtigte Planloeschung.
  Vier Vorlagen; `Nur Diagnose` ist Standard. Jede Aufgabe erklaert Wirkung
  und Befehl. Planvorschau fuehrt nichts aus. Lauf bestaetigen, dann jeden
  veraendernden Schritt nochmals freigeben. Beim ersten Fehler stoppen alle
  Folgeschritte. Fortschrittsbalken zaehlt protokollierte Aufgaben, nicht
  Gesamtrestzeit; Laufzeit/letzte Ausgabe und PID/Exitcode stehen bereit.
- **Absicherung:** Wiederherstellungspunkt ist vor Aenderungen standardmaessig
  Pflicht und wird kontrolliert. Scheitert er, folgt keine Wartungsaenderung.
  Kein heimliches Aktivieren des Computerschutzes. Abschalten der Pflicht
  braucht eine weitere Freigabe. Ein Wiederherstellungspunkt ist kein Backup.
  `Anhalten` bei einem Wartungsplan stoppt vor dem naechsten Schritt, nicht
  gewaltsam einen laufenden DISM/SFC/Installer/Scan. Auch Fensterschliessen
  wartet diesen Prozess ab; fuer solche Plaene gibt es kein erzwungenes
  Prozesszeitlimit. Eigene Operations-Wartungslaeufe werden lokal exklusiv
  gesperrt; fremde Windows-/Drittanbieterwartung wird dadurch nicht gesperrt.
- **Updates:** keine Treiber, kein `upgrade --all`, kein automatischer Neustart.
  Windows-Updatequelle laut vorhandener Windows-/WSUS-Richtlinie; Suche erst
  bewusst anstossen. Ergebnisse sind nicht vorangekreuzt. EULA je Update lesen
  und akzeptieren. Vor Download/Installation werden ID **und Revision** sowie
  Lizenztext erneut geprueft. Einzel-Download-/Installationsresultate werden
  kontrolliert. WinGet-IDs eine je Zeile; Paket-/Quelllizenzen werden mit
  Schrittfreigabe akzeptiert. Fremdinstaller unterliegen ihrem Hersteller.
- **Berichte:** `%LOCALAPPDATA%\AetherCockpit\Operations\Reports` mit eindeutigen
  Laufordnern, `Plan.json` bzw. `Targets.json`, `Live.log`, `Ergebnis.json`
  und HTML-Bericht. Keine automatische Logloeschung. Live-Ausgabe bis 1 MB in
  der GUI, Bericht bei Berichtslaeufen weiter geschrieben. Ein Schreibfehler
  wird sichtbar gemeldet und stoppt Folgewartung, ohne das Ausgaberohr eines
  laufenden Reparaturprozesses zu blockieren. Inventar/Berichte koennen private
  Benutzernamen, Geraetenamen, Pfade und Ereignisse enthalten; vor Weitergabe
  pruefen.
- **Geraete / Konsolen:** bis 500 ausdruecklich eingetragene Windows-Ziele,
  einzeln ankreuzbar; serielle, transparente Diagnose, kein Netzwerkscan.
  Windows-/DNS-Hostname oder IPv4; WinRM **muss bereits eingerichtet sein**.
  Aktuelles Admin-Konto, Negotiate, HTTPS/5986 mit normaler Zertifikatspruefung
  bevorzugt; HTTP/5985 nur bewusst. AETHER setzt keine TrustedHosts-Wildcards,
  Basic-Authentifizierung, Firewallfreigaben oder Zertifikatsausnahmen.
  In Arbeitsgruppen koennen Konten-/WinRM-/TrustedHosts-Regeln Verbindungen
  verhindern; Fehler werden je Ziel gezeigt, nicht umgangen.
  Remote-Inventar, Top-Prozesse, begrenzte Dienst-/Ereignissnapshots und
  lokale Laufberichte. Diagnose braucht vorhandene Berechtigungen und ist
  kein Gesundheitssiegel. Diagnoseabbruch beendet die eigene Abfrage;
  Remote-Aenderungsplaene, fremde Betriebssysteme und herstellerspezifische
  Geraeteprotokolle sind **noch nicht** implementiert.
- **M365 / Copilot / Server:** in diesem freigegebenen Ausbau ausschliesslich
  lokale Voraussetzungen (Windows, Office Click-to-Run, installierte Module,
  WinRM-Status). Keine Tenant-Anmeldung, keine Graph-Abfrage und keine Daten
  an Copilot. Eine echte Cloud-/Intune-/Serverorchestrierung benoetigt separat
  festgelegte Identitaeten, Rollen, Lizenzen, minimale Scopes und passende APIs.
  Lokale Office-Praesenz beweist keine M365-/Copilot-Lizenz.

Die Ziel-/Planstruktur ist der konkrete erste Arbeitsgruppen-Ausbau, kein
Versprechen einer schon vollstaendigen universellen RMM fuer alle Programme,
Geraete und Microsoft-Dienste. Reale Remote-Zugriffe, Updates, Reparaturen
und Benchmarks wurden bei der Entwicklung nicht ungefragt ausgefuehrt.

### Neon-Wave-Logo / CRT-Boot / Glossy Glass

Eigenes AETHER-Logo mit Chrom-A, Pulswelle und Cyan-/Magenta-Neon ist in
Bootscreen, Seitenleiste, Fenster- und EXE-Icon eingebunden. Hochaufloesendes
PNG und daraus erzeugtes Windows-Icon sind Programmressourcen.
Das Icon enthaelt ab Beta 7 sieben Groessen: 16, 24, 32, 48, 64, 128 und
256 Pixel fuer EXE, Taskleiste und Verknuepfungen.
Panels, Instrumentkarten und Buttons haben glaenzende Lichtkanten,
Chromrahmen und Glasreflexe. Das ist eine Gestaltungssimulation, keine
echte Windows-Acrylic-/Desktop-Transparenz.
Wandernde Cyan-/Magenta-Lichtsegmente beleben die Panelrahmen. Der Schalter
`Scanner-Animation` deaktiviert auch diese Animationen; Windows-
Animationseinstellungen werden respektiert. Digitale Uhr und Windows-Uptime
werden aus echten Messzeitpunkten alle etwa zwei Sekunden aktualisiert.

Nach UAC zeigt der Bootscreen eine gruen-schwarze CRT-Konsole, Synthwave-
Perspektivraster, Logo und wandernde Lichtreflexion. Die Animation folgt der
Windows-Einstellung fuer Client-Animationen; kein schnelles Flackern.
Bootmeldungen kommen aus der echten Initialisierung: native CPU/RAM-
Baselines, LibreHardwareMonitor, erkannte Hardware, jeder gemeldete Sensor
mit Messwert oder `UNAVAILABLE`, Performance-Baselines und geladene WPF-Seiten.
Erweiterte Module werden ausdruecklich als **on demand registriert**
bezeichnet, nicht als bereits ausgefuehrte Aktionen. Ereignis-/Windowsabfragen
sind eingeplant und starten erst mit dem Cockpit; Reparaturen, SMART-Tests,
LAN-Scans, Frame-Captures und Geheimnisabfragen starten nicht beim Boot.
Der Balken zeigt Initialisierungsphasen, keine erfundene Sensor-Prozentmessung.
Fehlender GPU-/Performancezugriff bleibt als Warnung bis zur Bestaetigung
sichtbar. Nullwerte anderer Sensoren werden explizit im Bootjournal angezeigt.
Schliessen fordert Abbruch an; laufende native Initialisierung muss erst
zurueckkehren, damit Sensorzugriff geordnet geschlossen wird.
Das Bootjournal bleibt im RAM und erscheint beim Oeffnen der erweiterten
Aktionskonsole; kein automatisches Bootlog auf der Platte.
Ab Beta 7 sind Sensorzeilen-Pacing und Modul-Pacing verdoppelt (maximal
24 ms je Sensorzeile / 70 ms je Modul). Die Mindestanzeige bei erfolgreichem
Boot wurde von 1,8 auf 3,6 Sekunden verdoppelt. Echte Hardwareinitialisierung
kann laenger dauern; Warnungen bleiben weiterhin bis zur Bestaetigung sichtbar.

### Eingebetteter Taskmanager / Ereignisanzeige / Superadmin

Elf Hauptseiten: die bisherigen sieben plus `Taskmanager`, `Ereignisse`,
`Superadmin` und `Operations`. Taskmanager- und Ereignisanzeige-Buttons im Diagnosebereich
wechseln zu den eingebetteten Ansichten statt die externen Windows-Programme
zu starten. Geraetemanager, Datentraegerverwaltung und Windows Security bleiben
bewusste Windows-Aufrufe; AETHER ersetzt nicht deren gesamten Funktionsumfang.

- **Taskmanager:** Live-Prozesstabelle und Graphen je logischem CPU-Prozessor,
  PID, Startzeit-gesicherte Details, Intervall-CPU, RAM, Threads, Handles und I/O.
  Dienste, Aufgaben und Prozessprioritaeten werden direkt in Superadmin verwaltet.
- **Ereignisse:** frei waehlbare lokale Kanaele, Kanalinventar, Zeitraum 1..30
  Tage und Limit 1..2000. System/Application/Security/Setup sowie Defender-
  und PowerShell-Operational sind vorbelegt. Such-/Stufenfilter, optionales
  15s-Refresh, Einzelereignis-Volltext und XML. Tabellenmeldungen sind auf
  400 Zeichen gekuerzt; Volltext wird getrennt gelesen. Zugriffs-/Providerfehler
  bleiben sichtbar. Keine Log-Loeschung, keine Events ins App-Journal kopiert.
- **Superadmin:** statusbasierte Tabellen mit Bereichs-/Aktionsauswahl,
  Parameterfeld, lesbarer Vorschau, ausdruecklicher Bestaetigung, Live-Konsole
  und Abbruch. Bereiche: Prozesse, Dienste/Starttypen, geplante Aufgaben,
  Windows-Features, WinGet/Installationsinventar, Updatehistorie, Energieplaene,
  Adapter/DNS, WinHTTP-Proxy, Firewallregeln, lokale Benutzer/Gruppen und ACLs.
  WinGet benoetigt eine explizite Paket-ID, keine automatisch geratene Zuordnung.
  Updates werden gelesen, nicht automatisch installiert oder neugestartet.
- **Explorer/Datenschutz/Tweaks:** acht feste AN/AUS-Einstellungen fuer
  Dateiendungen, versteckte/Systemdateien, Taskleistenanimationen, Werbe-ID,
  vorgeschlagene Inhalte, personalisierte Diagnose-Erlebnisse und optionale
  Diagnosedaten. Erlaeuterungen zeigen Versions-/Editionsgrenzen; Diagnose-
  Richtlinie 1 begrenzt auf erforderliche Daten, ist keine komplette
  Windows-Telemetrieabschaltung.

Vorschau fuehrt keinen Aenderungsbefehl aus. Vor Anwendung werden Zielzustand
und Identitaet erneut gelesen; keine PID-Wiederverwendung oder blinde
Ruecknahme extern veraenderter Zustaende. Ruecknahme der **letzten reversiblen
Sitzungsaktion** ist im RAM verfuegbar: Prioritaet, Dienststarttyp einschliesslich
DelayedAutoStart, Aufgabenaktivierung, Featurezustand, Energieplan,
Adapteraktivierung, IPv4-/IPv6-DNS mit getrenntem automatisch/statisch-Modus,
unterstuetzte WinHTTP-Proxy-Dumps, Firewallregel, Benutzerstatus, Mitgliedschaft
und ACL-Vererbung. ACLs verlangen vorheriges Lesen des absoluten Zielpfads;
Original-DACL/Dateierstellzeit werden geprueft, ReparsePoints verweigert.
ACL-Owner/Auditing und rekursive Rechtepresets werden nicht veraendert.
Das originale DWORD bzw. seine Abwesenheit fuer feste Registry-Tweaks wird
zusaetzlich dauerhaft unter `%LOCALAPPDATA%\AetherCockpit\Backups\registry-settings.json`
gesichert, auch bei mehrmaligem Umschalten oder abgebrochener Schreibphase.

Prozessbeenden, vergangene Dienst-/Aufgabenwirkungen und WinGet-Aenderungen
sind nicht automatisch rueckgaengig. Nicht unterstuetzte Proxy-Sicherungen
werden als nicht reversibel angezeigt. Ruecknahmen sind keine Transaktion;
Abbruch/Fehler koennen Teilaenderungen hinterlassen. Downloads, entfernte
Abhaengigkeiten, Netzwerkunterbrechungen und Aussperren koennen nicht generell
rueckgaengig gemacht werden. Keine pauschale Schutzabschaltung, kein
TrustedInstaller-/SYSTEM-Bypass. Windows-GPOs/Editionen/Rechte bleiben massgeblich.

CPU-Anzeigen bevorzugen einen echten CPU-Package-Kanal, bei AMD Tctl/Tdie/Tdie
und sonst den waermsten gemessenen CPU-Core. Board-/ACPI-Zonen werden nicht als
CPU-Werte ausgegeben. Unlesbare Kanaele bleiben explizit unverfuegbar; der Button
`CPU-/Sensorproviderbericht lesen` zeigt den lokalen Bibliotheksbericht
in der lesbaren Aktionskonsole. Administratorrechte garantieren keine
Unterstuetzung neuer Hardware oder erlaubten Low-Level-Treiberzugriff.

Die diskrete RTX wird als ein Geraet fuer Last, Temperatur, Hotspot,
VRAM, Leistung/Takt/Luefter ausgewaehlt. RTX hat Vorrang vor integrierter
Grafik; Sensoren verschiedener GPUs werden nicht vermischt.
Ein fehlender RTX-Temperaturwert wird nicht durch einen Intel-Wert ersetzt.
Alle GPU-Sensoren bleiben zusaetzlich in der Matrix sichtbar.
Sensorzugriff kann im Cockpit deaktiviert und bewusst erneut aktiviert werden.

### Detailliertes Live-Dashboard

Taskmanager-aehnliche Anzeigen ergaenzen das Neon-Cockpit:

- Verlaeufe/Balken fuer jeden logischen CPU-Prozessor (Windows-Kernzeiten),
  RAM-Belegung, Commit/Limit, Systemcache und Upload.
- Durchsuchbare, sortierbare Prozessliste: CPU-Anteil der gesamten Maschine
  im Messintervall, Working Set in MB, Threads/Handles und Prozess-Read/Write-I/O.
  Working Sets enthalten gemeinsam genutzte Seiten; nicht zum RAM-Gesamtwert
  addieren. Prozess-I/O umfasst auch Netzwerk-/Geraetezugriffe, nicht nur Disks.
  Geschuetzte/beendete Prozesse und erste Baselines zeigen `--` samt Status.
- RX/TX-Verlaeufe je aktivem Nicht-Loopback-Adapter, Volumebelegung und
  optionale Graphen fuer alle gemeldeten Sensoren, etwa
  Last/Temperatur/Leistung/Takt/Fan/VRAM/Spannung und Datendurchsatz.
- Windows-Datentraegerzaehler: Read/Write MB/s, Aktivzeit aus gueltigem
  Idle-Prozentwert sowie aktuelle I/O-Warteschlange. Kein SMART-Ersatz.
- Lokaler Ereignismonitor fuer System/Application, alle 15 Sekunden neu
  abgefragt. Letzte 24 Stunden, maximal **200 Ereignisse je Kanal**.
  Fehler/Warnungen/Informationen, 24 Stundenbuckets, Quellen/IDs, Suchfilter,
  Stufenfilter und lesbare Nachrichtendetails (max. 2000 Zeichen je Nachricht).
  Zaehler/Histogramm beschreiben **nur den begrenzten Snapshot**, nicht alle
  Windows-Ereignisse oder eine Malware-/Fehlerfreiheitspruefung. Keine Treffer
  und Zugriffsausfaelle werden unterschieden; Kanal-/Providerfehler angezeigt.

Live-/Prozessmessung: 2s Pause zwischen Messungen; Ereignisse/Disk-I/O: 15s
Pause nach jeder Abfrage. Laufzeiten der Abfragen kommen hinzu. Graphen:
maximal 90 Messpunkte, Prozentwerte mit 0..100-Skala, RAM/Commit mit ihrem
Limit, sonst dynamische Skalen mit Beschriftung. Dynamische Balken sind keine
Gesundheits-/Grenzwertbewertung. Unlesbare Werte werden nicht als 0 dargestellt.
Sektionen lassen sich ausklappen; Sensorzugriff ist ab Beta 5 beim Boot aktiv
und kann ausgeschaltet werden.
Neue Prozess-/Ereignisdaten bleiben fluechtig im RAM, ohne automatischen Export.
Die bisherige optionale Messaufzeichnung und gefuehrte Aktionen bleiben erhalten.

Alle Dropdowns verwenden einen gemeinsamen dunklen Popup-/Auswahlstil mit
heller Schrift und Neon-Markierung. Autostart ist ab Beta 23 eine suchbare
Tabelle mit vollstaendigem Befehl statt einer kurzen Dropdown-Auswahl.

Die sieben Hauptseiten enthalten Cockpit, Sensor-Matrix, Sicherheit, Laufwerke,
Messauswertung, Diagnose und Cleaner. **Erweiterte Systemkontrolle** in Diagnose
oeffnet sieben weitere Module. Verlinkungen befinden sich auch auf den Sensor-,
Laufwerks- und Messauswertungsseiten.

| Modul | Implementiert |
| --- | --- |
| Hardware | CPU/RAM/GPU/Netzwerk, Hotspot, VRAM, GPU-Leistung/Takt/Luefter, alle von LibreHardwareMonitor gemeldeten Sensoren; konfigurierbare CPU/GPU/Storage-Warnschwellen, ATX-/CMOS-Pruefung und explizite Thermal-/Throttling-Indikatoren |
| SMART | Geraeteerkennung, ATA-/NVMe-Diagnose, komplette JSON-Rohdaten mit Faehigkeiten/Fehler-/Selbsttestlogs, JSON-Export; Short/Long/Conveyance/Selective/Abort sowie AAM/APM mit Parameterpruefung |
| Messung / Gaming | MIN/MAX/AVG, anhalten/fortsetzen/zuruecksetzen, CSV-Export, lokale JSONL-Aufzeichnung, Messlog-Import, waehlbare Sensorkurven und scrollbare Messpunkte, Vergleich zweier Logs, PresentMon-Frame-Capture und Frame-CSV-Auswertung/-Vergleich |
| Cleaner | Schnell/Standard/Tief/Experte, Kategorieauswahl, Browser-/App-/Temp-/Diagnosecache-Vorschau, explizite Dateiauswahl, bestaetigtes Loeschen, Abbruch, per-Datei-Aktionsprotokoll; separate offizielle Delivery-Optimization-Bereinigung |
| Netzwerk / WLAN | Adapter/TCP-Prozessdiagnose, Ziel-/Router-Ping, privater LAN-Scan und ausdruecklich freigegebener Auto-Scan, MAC per ARP, optionale offline OUI-Herstellerzuordnung, WLAN-Netze/Profile, Bluetooth-Abfrage, Captive-Portal-Probe und ARP-Cache-Leerung |
| Sicherheit / Diagnose | Defender, Signaturen, Firewall, Secure Boot, BitLocker-Status, lokale Update-/Neustartindikatoren, Windows-Diagnosedatenrichtlinie, Crash-/WHEA-/Kernel-/Bugcheck-Ereignisse, Zertifikats-/Root-Speicher mit offline Ketten-/Ablaufpruefung |
| Systemkontrolle | Reversible HKCU/HKLM-Run-Autostartverwaltung, SFC Verify/Scan, DISM Scan/Restore, CHKDSK Online-Scan, UEFI-Neustart mit Abbruchmoeglichkeit, Explorer-Neustart der aktuellen Sitzung und direkte Windows-Verwaltungswerkzeuge |
| Sensible Anzeige | Separat bestaetigte lokale WLAN-Profil-, BitLocker-Recovery- und OEM-Firmware-Schluesselanzeige; keine automatische Abfrage, Speicherung, Exportfunktion oder Uebertragung |

Die Funktionsgruppen sind eigenstaendig umgesetzt. Es gibt keinen geratenen
Health-Score, keine Restlebensdauerprognose, keine pauschale
Registry-Optimierung und keine Garantie, dass jeder Sensor oder Laufwerkstest
auf jeder Hardware verfuegbar ist.

## Messwerkzeuge und Lizenzen

Nach ausdruecklicher Freigabe wurden portable originale Messwerkzeuge eingebunden:

- `tools\smartctl.exe`: smartmontools 7.5 r5714 x64. GPL-Lizenz in
  `tools\COPYING.txt`, zugehoeriger originaler Quelltext in
  `tools\smartmontools-7.5-source.tar.gz` einschliesslich Builddateien.
- `tools\PresentMon.exe`: PresentMon Console 2.6.0 x64, Lizenz in
  `tools\PresentMon-LICENSE.txt`. Kein PresentMon-Dienst/MSI.
- Herkunft und SHA-256-Pruefsummen: `tools\provenance.json`.
- LibreHardwareMonitorLib 0.9.6: MPL-2.0, inklusive transitiver Paket-Lizenzen.
  https://github.com/LibreHardwareMonitor/LibreHardwareMonitor

Originalquellen:
https://github.com/smartmontools/smartmontools/releases/tag/RELEASE_7_5
und https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0 .
Werkzeuge und Lizenzen werden in Build-/Publish-Ausgaben mitkopiert.
In Einstellungen koennen bewusst andere vorhandene EXE-Pfade ausgewaehlt werden.
Keine automatische Installation oder Ausfuehrung beim App-Start.

## Hardware, SMART und Warnungen

**NVMe-Temperaturen ab Beta 6:** LibreHardwareMonitor liefert auch
`Warning Temperature` und `Critical Temperature` als Temperaturkanaele.
Diese Werte sind vom Laufwerk gemeldete **Grenzwerte, keine Ist-Temperaturen**.
AETHER kennzeichnet sie als `TemperatureLimit` mit sichtbarem Grenzwertzusatz
in der Sensor-Matrix und im Bootjournal. Sie erzeugen keine Systempuls-
Temperaturwarnungen und werden nicht in Live-Graphen oder Mess-MIN/MAX/AVG
verwendet. Auch beim Import aelterer Messlogs werden diese Kanaele ausgeschlossen.
Die Rohwerte werden nicht pauschal korrigiert oder nach Hoehe weggefiltert.
Echte hohe Composite-/Einzelsensortemperaturen warnen weiterhin.
Storage-Warnungen nennen Laufwerk, genauen Sensorkanal, Provider und
die separat konfigurierte AETHER-Warnschwelle.

Administratorrechte garantieren keine Sensoren: Hardware, Firmware, Treiber,
Kernisolierung und Bibliotheksunterstuetzung entscheiden mit.
LibreHardwareMonitor kann beim freigegebenen Bootzugriff mit erhoehten Rechten
einen Low-Level-Treiber laden. Schutzfunktionen werden nicht abgeschaltet.
Nicht verfuegbar ist kein Messwert von 0. Bei diesem Rechner werden vom Provider
gelieferte CPU-Leistungswerte von 0 W bei gleichzeitig nicht lesbaren CPU-Takten
als uninitialisiert behandelt; positive Messwerte bleiben erhalten.

Spannungen werden nur bei bekannten 12-/5-/3.3-V-Schienen und CMOS gegen
konfigurierbare Grenzen geprueft. Fuer VCore/DRAM werden keine Sollwerte geraten.
Throttling wird nur anhand ausdruecklich gemeldeter Thermal-/Throttling-Faktoren
bewertet; Temperatur allein ist kein Beweis. Fehlender Indikator bleibt unbekannt.
Windows Storage Health ersetzt kein vollstaendiges SMART; dafuer SMART-Modul nutzen.

SMART-Aktionen erfordern Bestaetigung und Administratorrechte. Faehigkeiten
zuerst lesen. ATA-spezifische Conveyance/Selective/AAM/APM-Aktionen sind fuer
NVMe nicht definiert; Short/Long ebenfalls nur bei Laufwerksunterstuetzung.
Ein gestarteter Selbsttest laeuft im Laufwerk weiter, auch nach App-Abbruch;
**abort** beendet ihn explizit. AAM 128..254, APM 1..254, Selective als
`StartLBA-EndLBA`. Kein unkontrolliertes Setzen weiterer SMART-Optionen.
smartctl-Exitcodes sind Bitmasken: 0..2 Befehls-/Zugriffsfehler, 3..7
Health-/Logbefunde, keine pauschale Behandlung jedes Nichtnullwerts als Toolfehler.
NVMe Error-Log-Zaehler sind keine ATA-Sektorzaehler; percentage_used ist
verbrauchte Ausdauer, keine berechenbare Restlebenszeit.

## Verlaeufe, Auswertung und FPS

Live-Messung etwa alle zwei Sekunden; erster CPU-/Netzpunkt ist Baseline.
Netzwerkdurchsatz summiert aktive Adapter: VPN kann Daten mehrfach zaehlen.
Die Netzwerkinstrumentskala ist dynamisch, keine gemessene Internetkapazitaet.
Cockpit-Kurven halten maximal 90 Punkte. MIN/MAX/AVG nur aus gueltigen
Messpunkten, maximal 2048 Reihen. Anhalten friert Statistik ein, nicht das Cockpit.

Aufzeichnung unter `%LOCALAPPDATA%\AetherCockpit\History`: standardmaessig aus,
konfigurierbar 2..60 Sekunden (Standard 5s, auf das 2s-Sampling aufgerundet).
Maximal sieben Tagesdateien mit je 16 MiB. Bei vollem Tagesbudget stoppt die
Aufzeichnung sichtbar und muss fuer einen neuen Tag manuell aktiviert werden.
JSONL enthaelt Zeit, Messwerte und Hardware-/Sensornamen, keine Verbindungsziele,
Konten oder Seriennummern. Import begrenzt Dateigroesse, Reihen und Zeilen;
beschaedigte Zeilen werden explizit angezeigt. Kurven sind zeitbasiert und bei
vielen Punkten ausgeduennt; Tabelle und Statistik behalten die gueltigen Punkte.
CSV exportiert invariant formatierte Zahlen, Quoting und Schutz gegen Formelinhalte.

PresentMon-Capture: exakter laufender Prozessname, z.B. `Spiel.exe`, 5..600 Sekunden.
Keine separate Rechteeskalation und kein Dienst; die App ist bereits erhoeht,
zusaetzliche ETW-/Treiberbeschraenkungen bleiben moeglich.
Eigene eindeutige ETW-Sitzung, keine Beeinflussung anderer PresentMon-Captures;
Inputtracking ist abgeschaltet. CSV pro Prozess/Swapchain auswerten:
AVG FPS = 1000 / mittlere Framezeit, P99 Framezeit als Nearest-Rank-Quantil,
1%-low = 1000 / mittlere langsamste 1% der Frameintervalle.
Kein Zusammenwerfen verschiedener Swapchains. Diese Metriken sind
Present-Framezeiten, keine Garantie real angezeigter FPS oder ein synthetischer
CPU-/GPU-Stresstest. Capture-CSV-Limit fuer Import: 64 MiB / eine Million Zeilen.

## Cleaner und Systemaktionen

### Retro-Aktionskonsole

Die erweiterte Systemkontrolle hat eine vergroesserbare gruen-schwarze
Phosphor-Konsole mit Glasrahmen, dezenter Reflexion, Scanlines und Randabdunklung.
CRT-Effekte sind abschaltbar; keine flackernden Animationen. Dies ist eine
Glas-/Acrylsimulation, keine echte transparente Windows-Acrylic-Oberflaeche.
Autoscroll kann fuer das Lesen aelterer Ausgabe ausgeschaltet werden, die
Konsolenhoehe laesst sich ziehen oder die Konsole auf den Fensterinhalt vergroessern.

Bei SFC/DISM und anderen direkten Systembefehlen zeigt sie den genauen Befehl,
Rechtestatus, PID, Zeitlimit, Laufzeit, letzte Ausgabe, Live-STDOUT,
gekennzeichnete STDERR-Ausgabe und Exitcode. SFC-Umleitung wird als UTF-16
decodiert; Fortschritt ohne Zeilenumbruch wird sofort gelesen.
Wagenruecklauf-Fortschritte (z.B. DISM-Prozentbalken) aktualisieren eine
einzelne Terminalzeile an Ort und Stelle statt den Verlauf mit
Wiederholungszeilen zu fluten; abgeschlossene Zeilen bleiben unveraendert.
SFC/DISM-
Detailprotokollpfade werden angezeigt. Prozentwerte werden nicht erfunden.
Werkzeugseitige Pufferung kann trotzdem Ausgabepausen verursachen; Laufzeit und
Zeit seit letzter Ausgabe laufen weiter. Prozessende ist kein Gesundheitsbeweis.
Fehler/Abbruch erhalten bereits empfangene Ausgabe. Maximal zwei Millionen
Anzeigezeichen, danach expliziter Limit-Hinweis; keine unbeschraenkte Sammlung.
Normale Ausgabe bleibt bei Modulwechsel erhalten, sensible Schluesselausgabe
wird weiterhin beim Wechsel entfernt.

### Freie Administratorkonsole und Befehlskatalog

Unter **Wartung / Verwaltung** gibt es einen durchsuchbaren Katalog fuer
WinGet, DISM, SFC, Get-EventLog/Get-WinEvent, Systeminformationen, Prozesse,
Dienste, Aufgaben, Datentraeger, Treiber, Netzwerk, Defender, Firewall,
BitLocker-Status und Windows-Verwaltungswerkzeuge. Jeder Eintrag erklaert
Funktion, Auswirkung und relevante Parameter. Befehl anklicken kopiert ihn in
die gruen-schwarze Eingabe; Parameter anklicken fuegt ihn an der Schreibposition
ein bzw. ersetzt die Textauswahl. Platzhalter, alternative/mehrfach eingefuegte
Parameter und Pfade vor dem Start selbst anpassen. **Enter oder Start fuehrt
direkt aus, ohne zusaetzlichen Bestaetigungsdialog**, auch bei Aenderungen.
Die bisherigen gefuehrten Aktionen behalten ihre Bestaetigungen.

Beliebige eigene Windows-PowerShell-5.1-Befehle und Pipelines sind moeglich.
`Get-Command`, `Get-Help BEFEHL -Full`, `Get-Command BEFEHL -Syntax` und
Werkzeughilfe (`--help`, `/?`) erschliessen die vollstaendige lokal verfuegbare
Funktionalitaet. Der Katalog ist kein Anspruch auf alle optionalen Module,
Versionen, Treiber oder Drittanbieterkommandos. Fehlende Tools/Module und
unzulaessige Parameter melden Fehler, statt Erfolg vorzutaeuschen.

Jeder Start verwendet eine **neue nichtinteraktive PowerShell-Sitzung**:
Variablen, `cd`, Imports und Funktionen bleiben nicht fuer den naechsten Start
erhalten. Zusammenhaengende Schritte in einen Befehl/Pipeline schreiben.
Keine interaktive Standardeingabe, kein PTY/Windows-Terminal-Ersatz; Installer,
Read-Host, DISM-Quellen oder interaktive Programme muessen vorab passende
Parameter bekommen. GUI-Verwaltungstools koennen eigene Fenster oeffnen.
Zeitlimit: 1..1440 Minuten, Standard 120. Abbruch beendet den gestarteten
Prozessbaum, macht Aenderungen nicht rueckgaengig und stoppt nicht zwingend
bereits an Windows-Dienste uebergebene Aufgaben. Vor Reparaturen sichern.

Pfeil hoch/runter ruft bis zu 100 Befehle aus dem **fluechtigen** Verlauf ab.
Freie Befehle, ihre Ausgabe und ihr Verlauf werden nicht durch AETHER
persistiert/exportiert. Da sie Geheimnisse enthalten koennen, werden Eingabe,
Verlauf und freie Ausgabe bei Modulwechsel geloescht. Windows-/PowerShell-
Audit, aufgerufene Werkzeuge und bewusst eingegebene Exportbefehle koennen
selbst protokollieren oder Daten senden; kein Schutz gegen Bildschirmaufnahme.
Die Schreibweise `sfc` im Katalog nutzt einen UTF-16-Decoder fuer dessen
native Ausgabe. Direkte Pfadaufrufe oder `sfc.exe` umgehen diesen Helfer.

Feste, vorhandene Cache-Kategorien statt beliebiger frei eingegebener Loeschpfade:
Benutzer-/Windows-Temp, Chrome/Edge/Brave/Opera/Firefox-Cache, bekannte
Steam/Spotify/Teams/Discord/Zoom/Adobe/VS-Code-Cachepfade, CrashDumps,
WER-Archive und separat freizugebendes Prefetch. Nicht vorhandene Cachepfade
werden nicht erfunden. Browser/App vorher schliessen; Cache-Neuladung benoetigt
ggf. Netzwerk. Keine Cookies, Lesezeichen, Profileinstellungen, Downloads,
Spielinstallationen oder Offline-Medien. Expertenmodus ist kein Leistungsversprechen.

Nur Dateien aelter als sieben Tage, maximal 10000 Eintraege und 32 Ebenen pro
Kategorie. Erst Vorschau, dann STRG/SHIFT-Zeilenauswahl, dann Dialog mit Anzahl,
Groesse und Hinweis auf **endgueltige Loeschung ohne Papierkorb**.
Keine Ordnerloeschung. Vorschauzugehoerigkeit, Pfad, Alter, Groesse,
Aenderungsdatum, Attribute und Hardlinkanzahl werden vor jedem Loeschen am
Windows-Dateihandle geprueft. Junctions/Symlinks, Hardlinks, schreibgeschuetzte,
gesperrte oder geaenderte Dateien werden nicht geloescht.
Abbruch meldet Teilstand; bereits geloeschte Dateien bleiben geloescht.
Protokollfehler stoppen weitere Loeschaktionen.

Delivery Optimization nutzt separat `Delete-DeliveryOptimizationCache` nach
besonderer Freigabe: Windows bietet dabei keine identische Datei-Vorschau.
Keine eigenmaechtigen Dienststopps, keine Eventlog-/Registry-Loeschungen,
keine beim Neustart geplanten Dateiloeschungen und keine Schutzabschaltung.

Autostart: exakte 64-Bit HKCU/HKLM-Run-Zeichenfolgen werden vor Deaktivierung
lokal gesichert. Geaenderte Werte und Aktivierungskonflikte werden verweigert.
Sicherungen unter `AetherCockpit\Backups` enthalten Originalbefehle und koennen
sensible Befehlsargumente enthalten: lokal schuetzen und nicht ungeprueft teilen.
Weitere Aufgaben, RunOnce, Dienste und 32-Bit-Eintraege ueber Windows verwalten.
SFC/DISM-Reparaturen veraendern Systemdateien und koennen bereits Teilaktionen
ausgefuehrt haben, wenn ein Abbruch erfolgt. Vorher sichern. DISM RestoreHealth
kann Windows Update kontaktieren. CHKDSK verwendet `/scan`, niemals ungefragt
`/f` oder erzwungenes Aushangen. UEFI-Neustart: 30s Vorlauf, Abbruchaktion vorhanden.
Explorer-Neustart beendet nur Explorer-Prozesse der aktuellen Sitzung.

Aktionsprotokoll unter `%LOCALAPPDATA%\AetherCockpit\Actions`: maximal 30
Tagesdateien, je 16 MiB. Enthalten sind Aktionstitel und per-Datei-Loeschversuche,
Erfolge und Fehler, keine Schluesselinhalte. Bei vollem/unlesbarem Protokoll werden
keine neuen bestaetigungspflichtigen Aktionen ungeprueft gestartet.
UI zeigt die ersten zehn Cleaner-Fehler mit Hinweis auf weitere;
vollstaendiges per-Datei-Protokoll bleibt lokal.

## Netzwerk, Zertifikate und sensible Daten

LAN-Scan nur private RFC1918-IPv4-Netze /24.. /30, maximal 254 Hosts und 16
parallele ICMP-Abfragen. ARP/MAC nur soweit im lokalen Segment erreichbar.
Nicht antwortende Geraete sind nicht ausgeschlossen. Auto-Scan benoetigt separate
Bestaetigung, laeuft alle 60s nach Abschluss und endet beim Ausschalten,
Abbrechen oder Fensterschliessen. OUI-Herstellerzuordnung benoetigt eine bewusst
importierte IEEE-MA-L-CSV mit `Assignment` und `Organization Name`; Datenquelle:
https://standards-oui.ieee.org/oui/oui.csv . Ohne Datenbank bleibt Hersteller
unbekannt; lokal/randomisierte MACs erhalten keine geratene Herstellerzuordnung.
Keine Portscans, Paketinhalte oder automatischen LAN-Scans beim Start.
WLAN kann Windows-Standortfreigabe erfordern.

Ziel-/Router-Ping erfolgt nur nach Freigabe. Captive-Portal-Test sendet nach
Freigabe eine HTTP-Probe an `www.msftconnecttest.com`: dort wird deine IP sichtbar.
Keine Anmeldung, keine Weitergabe von Cockpit-Daten, keine automatische Umleitung.
Zeitlimit zehn Sekunden, Antwort maximal ein KiB. Windows-Abfrage etwa alle 60s,
Timeout 45s. Crash-Events: Application IDs 1000/1001, maximal 30 aus 30 Tagen;
Stabilitaet: maximal 100 System-Events zu WHEA/Kernel-Power/Bugchecks.
TCP-/Autostartdiagnose sind begrenzte Momentaufnahmen, keine vollstaendige
Schadsoftware-/Persistenzanalyse.

Zertifikate: CurrentUser/LocalMachine Root und My, maximal 1000 pro Speicher.
Offline Ketten-/Datumpruefung, keine Downloads oder Online-Sperrpruefung.
Ein lokal vertrauter Root ist kein Beweis fuer Unbedenklichkeit.
Gruen bestaetigt nur die einzelne gemeldete Pruefung; Unbekannt ist niemals sicher.
Get-HotFix beweist keine vollstaendige Aktualitaet, Defender bewertet kein
alternatives AV, AllowTelemetry ist ein Richtlinienwert, nicht gemessener
Diagnosedatenverkehr. Wert 0 bedeutet auf Windows Pro keine komplette Abschaltung.

Schluesseltab fragt nichts automatisch ab. Jeder Zugriff erfordert Warnungsdialog
und Adminrechte. WLAN-Profilname wird als Datenargument behandelt, kein Skriptcode.
BitLocker liest nur vorhandene RecoveryPassword-Protektoren; OEM-Windows-Key nur
bei tatsaechlichem Firmwareeintrag. Kein erfundener Schluessel fuer digitale Lizenzen.
Keine Speicherung/Exportfunktion/Cloud-Uebertragung; Ausgabe wird bei Tabwechsel
und Schliessen entfernt. Das ist keine Garantie fuer Speicherforensik,
Clipboard, Bildschirmaufnahmen oder vorhandene System-Protokollierung.
Bildschirmfreigabe/Recording vorher beenden.

## Entwicklung und Tests

Portable Beta reproduzierbar kompilieren (benoetigt .NET 10 SDK und beim ersten
Publish Zugriff auf NuGet fuer die Windows-x64-Laufzeitpakete; fuer den
kleinen nativen SCR-Starter auch Visual C++ x64 Build Tools und Windows SDK):

```powershell
powershell -NoProfile -File .\build\Publish-Beta.ps1
$release = (Resolve-Path .\dist\AetherCockpit-0.7.0-beta.32-win-x64).Path
[System.IO.Compression.ZipFile]::CreateFromDirectory($release, "$release.zip")
Get-FileHash "$release.zip" -Algorithm SHA256
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) { $iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
& $iscc .\build\AetherCockpit.iss
Get-FileHash .\dist\AetherCockpit-Setup-0.7.0-beta.32-win-x64.exe -Algorithm SHA256
```

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
dotnet build .\SystemCockpit.slnx -c Release
dotnet run --project .\tests\SystemCockpit.Tests -c Release -- --live --tools --catalog-ui
pwsh -File .\tests\Smoke-UI.ps1
dotnet publish .\src\SystemCockpit -c Release -o .\dist\AetherCockpit
```

Tests pruefen Schwellen, Parsersicherheit, Log-/Frame-Formate, Befehlsargumente,
Dateiloeschschutz, Junctions/Hardlinks, Fensterlebenszyklus und alle Module.
Loeschtests verwenden ausschliesslich eigens angelegte Testdateien.
UI-Test liest eine echte Temp-Vorschau, loescht aber keine Benutzerdateien.
`--tools` prueft Toolversionen, lesenden PowerShell-Transport, Abbruch eines
eigenen Sleep-Prozesses, UTF-16/Umlaut-Streaming ohne Zeilenumbruch vor Prozessende,
Wagenruecklauf-Fortschritte, STDERR/Exitcode, freie Shell mit harmlosen
Literalen/Fehlern, Sitzungsisolation, Zeitlimit, nichtinteraktive Eingabe,
SFC-Decoder mit simuliertem UTF-16-Kommando und offline Zertifikate;
kein Selbsttest oder Frame-Capture.
`--live` liest echte Windows-Statusdaten.
`--winget-live` liest die reale winget-Ausgabe (list/upgrade) und prueft den
Tabellenparser des Paket-Centers; es wird nichts installiert oder entfernt.
`--catalog-ui` prueft das sichtbare WPF-Layout, wiederholte Befehlsklicks,
Parameterbuttons, Enter-Routing und das Entfernen sensibler Eingabe in einem
nicht erhoehten Testhost. Es umgeht nicht die Startanforderung der Produkt-EXE.
`--dashboard-ui` liest echte CPU-/Prozess-/Speicher-/Ereignis-/Disk-Zaehler,
prueft das gebundene Dashboard und die geoeffnete dunkle SMART-Geraeteauswahl
in einem nicht erhoehten Testhost, ohne Autostarts zu aendern.
`--professional-ui` prueft Arbeitsplatzsuche, echte Zielrouten, Experten-Sperre,
Paketdetails-/Transferbedienung, scrollbare Freigabe mit 100 IDs, Netzwerkfilter
mit synthetischen Paketen, Autostartdetails und Cleaner-Auswahl bei 1100x760.
Kein Sensorstart, Capture, Update oder Scan von Benutzerordnern. Die
Paketwarteschlange wird mit injizierter Testausfuehrung und Testjournal auf
Erfolg, Fehler, Fortsetzung, Abbruch und Journalfehler getestet; kein Installer
wird gestartet. Speicheranalyse nutzt nur eigene Temp-Fixtures.
`--board-render-profile` startet ausschliesslich eine synthetische animierte
Schoner-Vorschau ohne Sensoren/Musik und misst vier Sekunden WPF-Komposition
sowie sechs 4K-Software-Snapshots. Nicht mit GPU-Present-FPS gleichsetzen.
Optionaler Folgewert `verify` prueft p95 <= 33.4 ms im 1280x720-Testfenster,
`no-rays` isoliert das Raster und `4k` fordert ein 3840x2160-Testfenster an.
Windows kann dessen Groesse begrenzen; die tatsaechliche Inhaltsgroesse wird
ausgegeben. Diese drei Modi lassen die Software-Snapshots aus.
Beispiel: `dotnet run --project .\tests\SystemCockpit.Tests -c Release -- --board-render-profile verify`.
Nur gezielte Renderdiagnose, kein Hardwarebenchmark oder echter Vollbildstart.
`--admin-ui` prueft neue Verwaltungsplaene mit dem echten PowerShell-Parser,
lesende Statusabfragen, GUI-Vorschau ohne Ausfuehrung, Ereignisdetails und
Vorhandensein/Abschalten animierter Rahmen. Windows-Features benoetigen zum
Online-Lesen einen erhoehten Host; das wird im nicht erhoehten Test angezeigt.
Registry-Backup-Phasen und DNS-/Proxy-Ruecknahme werden mit Fixtures getestet;
keine echten Registry-, Dienst-, Netzwerk-, Konto- oder ACL-Aenderungen.
`--operations-ui` prueft native Wartungs-/WinRM-/WinSAT-Skripte mit dem
PowerShell-Parser, grosse Skripte ueber Standardinput, Update-EULA-Aenderungen
mit einem lokalen COM-Fixture, echte gespeicherte WinSAT-/lokale Readiness-
Abfragen, unverbindliche Planvorschau, Checkbox-Bindung/Loeschschutz und die
Sichtbarkeit jedes der 24 Core-Graphen im maximierten Fenster sowie in
1920x1080 und 1440x940. Keine echten Remoteverbindungen, Windows-Updatesuche/
-installation, Reparaturen, Wiederherstellungspunkte oder Benchmarks im Test.
Der separate native `Smoke-UI.ps1` muss ab Beta 3 aus einer administrativen
PowerShell ausgefuehrt werden; ein nicht erhoehter automatisierter Start kann
die Windows-UAC-Abfrage nicht selbst bestaetigen.

Nur nach ausdruecklicher Freigabe fuer Hardwarezugriff:

```powershell
dotnet run --project .\tests\SystemCockpit.Tests -c Release -- --hardware
dotnet run --project .\tests\SystemCockpit.Tests -c Release -- --boot-ui
pwsh -File .\tests\Smoke-UI.ps1 -EnableSensors
```

`--boot-ui` prueft automatische echte Sensorinitialisierung, diskrete RTX-
Cockpitwerte, CRT-Bootjournal, Logo-Ressourcen, Warnungsbestaetigung und
Abbruch im WPF-Testhost. Die Produkt-EXE verlangt weiterhin Windows-UAC.

Reparaturen, SMART-Selbsttests, LAN-/WLAN-Scans, Explorer-/Firmware-Neustarts,
Autostart-Aenderungen und Schluesselabfragen werden bei der Entwicklung nicht
ungefragt auf dem echten System ausgefuehrt. Deren tatsaechliche Wirkung ist
hardware-/rechteabhaengig und muss im jeweiligen Modul bewusst freigegeben werden.
Keine App-Cloud oder App-Telemetrie. NuGet und SDK haben unabhaengige Netz-/SDK-
Telemetrie; `DOTNET_CLI_TELEMETRY_OPTOUT=1` deaktiviert SDK-Nutzungsdaten.
