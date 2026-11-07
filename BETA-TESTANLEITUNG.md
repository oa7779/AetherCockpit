# AETHER System Control - Anleitung fuer Betatester

**Version: 0.7.0-beta.31 / Windows x64**

AETHER verbindet Systembeobachtung, Hardware-Diagnose und bewusst freizugebende
Windows-Verwaltung in einer eigenstaendigen 80er-Retro-Neon-Oberflaeche.
Die Beta ist eine Vorabversion, kein garantiert fehlerfreies Wartungsprodukt.

Copyright (c) 2026 Olaf Weins.
Rechte und Lizenzbedingungen enthaltener Drittanbieter bleiben bestehen.

## Neu in Beta 31: optionale zentrale Verwaltung

Unter **System Control -> Einstellungen -> Zentrale Verwaltung** kann zwischen
**Einzelplatz** (Standard, ohne Serverkontakt) und **Server-Client** gewechselt
werden. Client-Anmeldung per Einmalcode und das Uebertragungs-Opt-in sind
getrennte, ausdrueckliche Schritte. Neue Geraete bleiben bis zur manuellen
Freigabe durch die Serververwaltung gesperrt. Erst nach Freigabe und Opt-in
werden beim App-Start und danach stuendlich Diagnosedaten uebertragen; das
Geraetetoken wird per Windows-DPAPI geschuetzt. Zurueckschalten auf Einzelplatz
stoppt die Abfragen und deaktiviert die Uebertragung.

Der Server lauscht standardmaessig ausschliesslich auf `127.0.0.1:5187`.
Er ist ein separates Quellprojekt, wird nicht vom Cockpit-Setup installiert
und muss separat gestartet werden; siehe
[Server-README](src/AetherManager.Server/README.md).
LAN-Betrieb, Dienstinstallation, Zertifikatsinstallation und Firewallregeln
sind nicht eingerichtet. Nicht-lokale Listener benoetigen HTTPS; vor einem
LAN-Einsatz muessen vertrauenswuerdiges HTTPS, Hostfilter und eingeschraenkte
Zugriffsrechte auf den Datenspeicher separat konfiguriert werden. Der neue
Cockpit-Anmelde-/Reporting-Ablauf wurde noch nicht manuell durchgespielt.

## Neu in Beta 30: Windows-Diagnose und Problemloesung

Im Cockpit auf **Diagnose** klicken und **Windows-Diagnose & Problemlösung**
waehlen. In Operations / **Windows-Assistenten** stehen getrennte Helfer
fuer Systemdateien, Windows Update, Netzwerk und Geraete/Treiber bereit.
Jeder Helfer startet seine lesende Diagnose einzeln; Ergebnisse und naechste
Schritte erscheinen im Fenster, ein lokaler Bericht wird gespeichert.

Der Button **Windows-Reparaturplan vorbereiten** waehlt nur DISM/SFC-
Reparaturschritte im Wartungsplan aus. Er fuehrt sie nicht aus. Erst Plan-
Vorschau bestaetigen, Wiederherstellungspunkt erstellen lassen und danach
jeden einzelnen Aenderungsschritt ausdruecklich freigeben. Die Systemdatei-
Diagnose selbst nutzt DISM CheckHealth und SFC VerifyOnly, keine Reparatur.
Netzwerkdiagnose liest Konfiguration und fragt das konfigurierte Gateway/
DNS gezielt ab. Treiber werden nicht automatisch geladen oder installiert.

## Seit Beta 29: lokales Diagnoseinventar

Windows-Ereignisse, PnP-Geraeteprobleme, Adapterstatus, installierte
Anwendungen und Treiber lassen sich als umfassender lokaler JSON-Bericht
erfassen. Kein automatischer Versand an eine Zentrale.

## Neu in Beta 27: Renderengpass im Strassenraster behoben

Einzelne, begrenzte Fluchtlinien statt eines gemeinsamen, aufwendig zu
berechnenden Linienpfads. Neon-Glow als Vektorlinien statt Blur-Zwischenbilder.
Bewegungen/Farbwechsel bleiben aktiv und gleich schnell; keine verringerte
Aufloesung. Glow etwas schaerfer. Gilt im Mainboard und Bildschirmschoner.
Beim Test auf jedem Monitor auf fluessige Strasse, Luefter und Datenfluss achten.

## Seit Beta 26: Bewegter Horizont

Horizontleuchten wandert und wechselt die Farbe; auch Berge, Bodenverlauf
und Rasterpalette wechseln statt dauerhaft rosa/blau zu bleiben. Gilt im
Mainboard und Schoner, ohne Taumeln der Strasse. Animation muss eingeschaltet
sein. Keine Garantie gegen Einbrennen; Monitorabschaltung/Pixelpflege nutzen.

## Seit Beta 25: Platine ohne verdeckte Ecken

Platine mit Bauteilen proportional an den Platz zwischen den Infoboxen
angepasst; auch die linke Ecke bleibt frei. Gilt im Mainboard und Schoner
und fuer unterschiedliche Laufwerksbestueckung.

## Seit Beta 24: Inventarisierung und bewegter 4K-Schoner

- Beim ersten Inventar steht deutlich **DEIN RECHNER WIRD UNTERSUCHT** mit
  laufender Anzeige und Abfragestufe. Erst nach Abschluss ist Cockpit starten
  freigegeben. Bei Fehler erneut Inventar aktualisieren; kein Ueberspringen.
- Vektor-Schoner skaliert auf 4K und die verfuegbare Monitorflaeche.
  Gerade Fluchtlinien, zum Horizont laufende Querlinien, wandernde
  Himmelsverlaeufe, sanfte Bildverschiebung und farbige Legendenleitungen.
- Mainboard und Schoner wechseln Beschreibung/Rundinstrument alle sechs
  Sekunden; Kanaele rotieren vollstaendig, auch MHz/RPM/Temperatur soweit
  vorhanden. Fehlende Sensoren bleiben `--`, Grenzen sind keine Messwerte.
  Skalen sind Anzeige-Referenzen, keine Hardwaregrenzwerte.
- Diese Bewegung garantiert keinen Einbrennschutz. OLED-Pixelpflege und
  automatisches Ausschalten des Monitors weiterhin nutzen.
- Optional mitinstallierter Schoner steht direkt in der Windows-Auswahlliste
  als **AETHER Mainframe 1986**. Nur Musik an/aus in seinen Einstellungen.

## Seit Beta 23: Finden statt Werkzeugflut

- **FUNKTION FINDEN / Strg+K:** Arbeitsbereich waehlen, mehrere Begriffe suchen,
  Wirkung und Schrittfolge lesen. Oeffnet nur den passenden Bereich.
- **Pakete:** Details/Quelle pruefen, AETHER-Paketlisten exportieren/importieren.
  Import zeigt nur Wunschpakete; Versionen sind keine Pins. Quellen muessen
  lokal vorhanden sein. Erst eigene Auswahl in scrollbarer Vorschau freigeben.
  **Auftraege** zeigt Einzelbefunde; nach Paketlauf Liste neu laden. Weitere
  Paketmanager nur optional im bewusst gestarteten, bereits installierten UniGetUI.
- **Netzwerk:** Live-Analyse von Erfassung/Alarmen getrennt. Filter beeinflussen
  nur bis 200 Live-Zeilen, nie die PCAP-Datei. Einzelpaket markieren fuer
  Details; Metadaten-CSV enthaelt IP-Adressen. Nach Stop kann eine PCAP-Datei
  bestaetigt an installiertes Wireshark uebergeben werden.
- **Messauswertung:** Statistik filtern; CSV speichert die sichtbaren Reihen.
  Unter Sensoren ist ein eigener Momentaufnahme-CSV moeglich. Duplikate werden
  auch in Statistik und Logvergleich nicht mehr unbemerkt vermischt.
- **Cleaner:** Pfadfilter ist nur eine Ansicht. Versteckte angekreuzte Dateien
  bleiben Teil der genannten Gesamtmenge; Keine ankreuzen leert alles.
  Grosse Dateien finden ist rein lesend, ohne Loeschfunktion. Auswahl und
  Scanlimits/Lesehinweise beachten; Pfad-CSV vor Weitergabe pruefen.
- **Autostart:** suchbare Tabelle und kompletter Befehl statt kurzer Auswahlliste.
- Die Systemkontroll-Konsole ist anfangs verborgen, bei Auftrag/Wartung sichtbar.
  **AUSGABE EIN/AUS** schafft Platz; die freie Expertenkonsole bleibt maechtig.

Sicherer erster Test: Suche/Kategorien, vorhandene synthetische oder selbst
gewaehlte Logdatei und Paketlisten-Vorschau pruefen. Import startet keine
Installation. Netzwerkfenster oeffnen startet keinen Mitschnitt; Speicheranalyse
oeffnen startet keinen Scan. Keine Reparatur/Update/Loeschung nur zum Ausprobieren.
Der [Funktionsabgleich](FUNKTIONSABGLEICH.md) nennt Quellen und Grenzen;
AETHER ersetzt Wireshark, UniGetUI oder Autoruns nicht vollstaendig.

## 1. Voraussetzungen und wichtige Hinweise

- Windows x64 mit grafischem Desktop; die benoetigte .NET-Laufzeit ist enthalten.
  Nicht fuer Windows ARM64 als native Ausgabe oder Linux/macOS bestimmt.
- AETHER benoetigt Administratorrechte. Windows-UAC muss beim Start bewusst
  bestaetigt werden. Bei Verwendung eines anderen Administratorkontos liegen
  Einstellungen und Autostart im Kontext dieses Kontos.
- Das Paket ist nicht signiert. SmartScreen kann warnen. Nur Pakete aus einer
  vertrauenswuerdigen Quelle verwenden und SHA256 mit der separat erhaltenen
  Pruefsumme vergleichen. Sicherheitsfunktionen nicht pauschal abschalten.
- Vor Tests aktuelles Backup wichtiger Daten anlegen. Veraendernde Funktionen
  zuerst auf einem Testrechner erproben, nicht auf produktionskritischen Systemen.
- Beim normalen Start werden Hardware-Sensoren initialisiert. Die verwendete
  Sensorbibliothek kann einen lokalen Low-Level-Treiber laden. Nicht jede
  Hardware oder Windows-Sicherheitskonfiguration erlaubt alle Messwerte.
  Fehlende Messwerte sind kein Beweis fuer einen Hardwaredefekt.
- Keine automatischen Reparaturen, Updates, Benchmarks oder Blockierungen.
  Gefuehrte Aenderungen erfordern eine Bestaetigung. Die freie
  Administratorkonsole ist dagegen ein maechtiges Werkzeug: Befehle dort
  koennen unmittelbar Windows und Daten veraendern.

## 2. Installation, Aktualisierung und portable Nutzung

### Windows-Setup

1. `AetherCockpit-Setup-0.7.0-beta.31-win-x64.exe` ausfuehren.
2. UAC bestaetigen und Installation bzw. erkanntes Update pruefen.
3. Bei Neuinstallation Diagrammgruppen und Desktop-Gadgets auswaehlen.
4. Autostart nur aktivieren, wenn die erhoehte Aufgabe fuer das angezeigte
   Administratorkonto ausdruecklich gewuenscht ist.
5. Den optionalen Paketmitschnitt im ersten Test ausgeschaltet lassen.
6. AETHER nach Abschluss starten und die EULA lesen/bestaetigen.

Hinweis fuer Softwareverteilung: Ab Beta 28 laeuft das Setup auch
unbeaufsichtigt (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`). Dabei wird
keine Autostart-Aufgabe eingerichtet und kein Mitschnitt aktiviert; Details
stehen in der README unter 0.7.0-beta.31.

### Vorhandene Installation aktualisieren

**Startabbruch nach Initialisierung in Beta 18-20:** Beta 21 behebt den
bestaetigten Absturz durch mehrfach gemeldete NVIDIA-Sensor-IDs. Kein
Loeschen der Einstellungen noetig; aktuelles Setup als Update verwenden.
Verschiedene Kanalnamen bleiben im Verlauf getrennt; widerspruechliche Werte
desselben Kanals werden mit Hinweis als nicht verfuegbar markiert.

**Nicht vorher deinstallieren.** AETHER im Symbolmenue bei der Uhr ueber
**AETHER vollstaendig beenden** schliessen. Das X am Hauptfenster reicht nicht.

Das neue Setup erkennt eine bisherige AETHER-Setup-Installation anhand ihres
Windows-Installationseintrags, zeigt Version und Zielordner und ersetzt die
Programmdateien am bisherigen Ort. Die Anwendung darf dabei nicht weiterlaufen.
Windows kann zum Schliessen noch geoeffneter Dateien auffordern.

Standardmaessig bleiben Desktop-/Diagrammwahl, bestehende Autostart-Aufgabe,
Benutzereinstellungen und lokale Daten erhalten. Nur wenn im Update-Dialog
**Konfiguration neu festlegen** gewaehlt wird, werden diese Setup-Auswahlen
erneut abgefragt. Fuer neu einzurichtenden Autostart/Mitschnitt ist wieder eine
ausdrueckliche Freigabe erforderlich. Keine automatische Online-Updatesuche.

**Nach Autostart-Fehler in Beta 12:** Das Dateikopieren kann trotz des
anschliessenden Laufzeitfehlers erfolgreich gewesen sein. Beta 13 korrigiert
die Aufgabenregistrierung. Beim Update **Konfiguration neu festlegen**
auswaehlen und Autostart erneut genehmigen; ein Update mit unveraenderter
Konfiguration legt bewusst keine fehlende Aufgabe neu an.
Nicht zuvor deinstallieren. Es wird kein Windows-Kennwort abgefragt oder gespeichert.

Eine portable Kopie ohne Installationseintrag wird nicht als installierte
Vorversion erkannt. Portable Updates in einen neuen Ordner entpacken; alte
Version vorher beenden. Nicht mehrere Versionen parallel betreiben.

### Restordner nach fehlgeschlagener alter Deinstallation

Falls Beta 19 meldet, dass eine Neuinstallation einen leeren Ordner braucht:
altes Setup abbrechen, AETHER ueber das Tray vollstaendig beenden und
das neue Beta-20-Setup starten. Wenn noetig im Task-Manager als Administrator
den verbliebenen AETHER-Prozess anhand seines Installationspfads pruefen/beenden.

Beta 20 prueft vorhandene EXE und DLL auf AETHER-Produktmetadaten und bietet
im eigenen Ordner `AETHER System Control` eine ausdruecklich zu bestaetigende
Wiederherstellung an. Diese ersetzt Programmdateien und stellt den Uninstaller
wieder her; Kontodaten und Zusatzdateien bleiben bei diesem Vorgang erhalten.
Die hier getroffene Setup-Konfiguration wird angewendet. Keine manuelle
Loeschung des Restordners erforderlich.

Nur bestaetigen, wenn der Zielordner ausschliesslich fuer AETHER verwendet wird:
bei einer spaeteren bestaetigten Deinstallation wird sein gesamter Inhalt
entfernt. Fremde Programme, Systemordner und Links/Junctions bleiben blockiert.
Keine automatische Zustimmung bei stillen Installationen. Werden die
Altdateien nicht erkannt, einen anderen eigenen leeren Zielordner waehlen.

### Portable ZIP

ZIP vollstaendig entpacken und `SystemCockpit.exe` aus diesem Ordner starten.
Alle Dateien und Unterordner muessen zusammenbleiben. Es ist keine einzelne
portable EXE. Ein portabler Start legt keine Windows-Autostart-Aufgabe an.

### Daten und Deinstallation

**Fehlende optionale Werkzeuge nach einem Update:** Seit Beta 15 verhindern
veraltete smartctl-/PresentMon-/OUI-Pfade weder Start noch Uebernahme des
Setup-Profils. Warnungen benennen die fehlenden Dateien; die Pfade und alle
uebrigen Einstellungen bleiben erhalten. Unter Systemkontrolle -> Einstellungen
die Dateien neu auswaehlen oder den jeweiligen Pfad leeren. Kein Loeschen der
gesamten Einstellungsdatei erforderlich. Ein fehlendes Werkzeug wird nicht
ausgefuehrt und nicht automatisch durch eine andere EXE ersetzt.

Einstellungen, Verlaeufe, Logs und Sicherungen liegen unter
`%LOCALAPPDATA%\AetherCockpit`. **Ab Beta 19 werden diese Daten samt
Installationsordner nach einer ausdruecklichen Bestaetigung vollstaendig geloescht.**
Vorher benoetigte Einstellungen, Mitschnitte und Backups ausserhalb sichern!
Auch die lokale Musikauswahl wird entfernt, nicht aber die externe WAV.
Andere portable Programmordner bleiben bestehen, nutzen jedoch dieselben
jetzt geloeschten Kontoeinstellungen. Andere Windows-Profile werden nicht angefasst;
bei anderem UAC-Konto gilt das Profil dieses Administratorkontos.

Zuerst das neue Setup als Update installieren, danach dessen Uninstaller
unter Windows -> Installierte Apps ausfuehren. Er entfernt die Autostart-Aufgabe
und beendet laufende App, Tray, Gadgets und Schoner genau dieser Installation.
Windows-Schonerauswahl wird nur entfernt, wenn sie auf diese SCR zeigt.
Bei Links/Junctions im Installations-/Datenordner oder fehlender
Installationsmarkierung wird vor der Dateiloeschung mit Fehler abgebrochen;
keine blinde Bereinigung fremder Verzeichnisse. Neuinstallationen brauchen einen
eigenen leeren Ordner oder die ab Beta 20 bestaetigte Wiederherstellung
erkannter AETHER-Reste. Updates am bisherigen Ort behalten weiterhin alle Daten.

### Optionaler Mainboard-Bildschirmschoner

Im Setup **AETHER Mainboard-Bildschirmschoner mitinstallieren** auswaehlen.
Bei einem Update ggf. zuerst **Konfiguration neu festlegen** auswaehlen.
Dadurch werden SCR/Host im App-Ordner und ein kleiner nativer `Aether.scr`-
Starter im Windows-Systemordner bereitgestellt. Er verweist ueber eine
installationsgebundene Registrierung auf den App-Ordner. Keine automatische
Aktivierung; Deinstallation entfernt Starter und Registrierung wieder.

Windows **Bildschirmschoner aendern** oeffnen, **AETHER Mainframe 1986**
aus der Liste waehlen und **Uebernehmen**. Im Windows-Dialog
selbst Wartezeit und **Anmeldeseite bei Reaktivierung** waehlen.
Die Windows-Vorschau wird unterstuetzt. `AetherMainboard.scr /c` oeffnet Einstellungen;
`AetherMainboard.scr /s` startet eine Vollbildansicht (ebenso je nach Windows-
Dateizuordnung der Doppelklick). Maus/Tastatur beendet sie.
Jeder angeschlossene Monitor zeigt das vollstaendige Mainboard.
Bei Bildschirmwechsel/Hotplug endet der laufende Schoner.

Ab Beta 19 erscheint nur die obere Szene mit aufgesteckten Laufwerken,
bewegtem Raster und Binaerbergen, ohne die lange untere Geraeteuebersicht.
Auslastung und verfuegbare Temperaturen stehen an CPU/RAM/GPU sowie
Speicher/Chipsatz; Netzwerk zeigt RX/TX. Gruppenmaxima sind als solche
bezeichnet, keine behaupteten Einzel-SSD-Werte. Ohne Sensor bleibt `--`.
Die normale Cockpit-Ansicht behaelt ihre vollstaendige Geraeteuebersicht.

Ab Beta 21 erscheinen im Schoner nur intern erkannte Laufwerke, einschliesslich
weiterer interner SSDs/HDDs neben dem Windows-Laufwerk. Keine USB-Sticks,
USB/UASP-Plattengehaeuse, SD/MMC oder optischen Laufwerke. Unbekannte Bustypen
werden nicht als intern angenommen; die vollstaendige Cockpit-Inventarliste
bleibt unveraendert. Storage-Messwerte nur bei exaktem eindeutig internem
Modellnamen, sonst `--`, keine geratenen Datentraegernummern.
Beschriftungen verdecken dahinterliegende Linien und sind voneinander
getrennt; lange Namen enden nach ganzen Zeilen mit Auslassung. Vollstaendige
Namen stehen weiterhin im Tooltip bzw. in den Cockpit-Geraetedetails.

Ab Beta 22 nutzen Mainboard-Modul und Schoner dieselbe kraeftige
Synthwave-Farbgebung: dunkelblau/violett, Magenta-Horizont, Cyan-/Pink-Raster
und Neon-Glow an Bergkonturen, Platinenrand und Datenpfaden. Zwei seitliche
Binaer-Bergketten rahmen den offenen Fluchtpunkt ein; der Boden davor bleibt
frei. Die Berge stehen fest am Horizont, ihre Binaerzeilen laufen weiter.
Beschriftungen bleiben scharf, deckend und ohne Glow; Animationsschalter
pausiert weiterhin die Bewegung. Keine Aenderung an Messwerten oder Musik.

Die SCR startet ohne Administratorrechte und initialisiert keine Sensoren
oder Treiber. Fuer echte Live-Werte muss AETHER im selben Windows-Konto und
derselben Sitzung laufen, auch minimiert im Tray. Lokaler, lesender Austausch,
keine Netzwerkuebertragung. Ohne Verbindung werden Hardware und Status
rein lesend als Momentaufnahme erfasst; oben steht **KEINE LIVE-MESSWERTE**.
Fehlende/veraltete Messwerte werden nicht erfunden oder als live weiterbenutzt.
Bei anderem Administratorkonto sind Live-Werte nicht freigegeben.

**Optionale Musik (ab Beta 17):** Im Windows-Bildschirmschoner-Dialog
**Einstellungen** waehlen oder `AetherMainboard.scr /c` aufrufen. Einzige Option:
**Musik an / aus**. Vorhandene lokale WAV und Lautstaerke bleiben erhalten;
bei noch nicht zugeordneter/fehlender Datei fragt Speichern mit Musik an
einmalig nach einer WAV. Neue Zuordnungen verwenden 35 % Lautstaerke.
Vollbild spielt die WAV in Endlosschleife, einmal fuer alle Monitore.
Jeder Abbruch stoppt die Musik sofort; die kleine Vorschau bleibt stumm.
Windows-Systemlautstaerke wird nicht geaendert. Das Fenster spielt beim
Konfigurieren keinen Testton.

Musik ist fuer neue Konten ausgeschaltet. Einstellungen liegen je Konto in
`%LOCALAPPDATA%\AetherCockpit\screensaver-audio.json`. WAV bleibt extern und
wird nicht in die Beta kopiert; bei Weitergabe selbst passende Musik mit
entsprechenden Rechten auswaehlen. OneDrive-Dateien vorher lokal verfuegbar
halten. Fehlt die Datei oder ist sie nicht abspielbar, meldet der Schoner
den Musikfehler sichtbar und bleibt grafisch nutzbar. Andere EXEs oder
Netzwerkpfade werden dafuer nicht geoeffnet.

Im Portable-Paket bleibt die SCR zusammen mit allen DLLs und Laufzeitdateien
im entpackten Ordner. **Nicht die einzelne SCR nach System32 kopieren.**
Nicht als Ersatz fuer die Windows-Sperre einsetzen. Wird ein ausgewaehlter
Schoner deinstalliert/verschoben, anschliessend in Windows einen anderen
Bildschirmschoner waehlen; das Setup veraendert diese Auswahl nicht.

## 3. Funktionen im Ueberblick

| Bereich | Was kann getestet werden? |
|---|---|
| Cockpit | Live-Instrumente und Kurven fuer CPU, RAM, GPU, Netzwerk, Temperaturen, CPU-Kerne und Datentraeger; Umfang abhaengig von Hardware und verfuegbaren Windows-Zaehlern. |
| Mainboard / 1986 | Isometrische Neon-Platinenskizze mit stilisierten Bauteilen, Typnamen, Windows-Status, symbolischem Datenfluss und separaten echten Live-Messwerten. |
| Taskmanager | Prozessuebersicht und CPU-Kern-Diagramme; angezeigte CPU-Anteile beziehen sich auf die Maschine. |
| Sensor-Matrix | Live-Verlaufskurven nach Hardware/Einheit, Sensorenauswahl und Filter; umschaltbare Detailmatrix inkl. gekennzeichneter Grenzwerte. |
| Laufwerke / SMART | Belegung und Windows-Storage-Status; erweiterte smartctl-Diagnose und gezielt freizugebende Selbsttests im SMART-Modul. |
| Messauswertung | Min-/Max-/Durchschnittswerte, optionale lokale Aufzeichnung, Vergleich und Export. |
| Diagnose / Sicherheit | Lesende Windows-Pruefungen, Rechner-Inventar, Defender-/Firewall-/Secure-Boot-/BitLocker-Status und Diagnose-Export. Kein Malware-Scanner-Ersatz. |
| Ereignisse | Windows-Ereignisprotokolle nach Zeitraum/Kanal lesen und Einzelheiten ansehen. |
| Cleaner | Vorschau, bewertete Kandidaten und explizite Auswahl vor endgueltigem Loeschen. Keine automatische Bereinigung. |
| Systemkontrolle | Paket-/WinGet-Center, Befehlskatalog, Netzwerkwerkzeuge, SMART und Einstellungen. Paketaktionen und Tests nur bewusst starten. |
| Superadmin / Operations | Gezielte Verwaltung und Wartungsablaeufe mit Risikoanzeige und Bestaetigungen. Nicht als Routine-Betatest ausfuehren. |
| Hilfe / Darstellung | Durchsuchbare Modulhilfe, Navigation mit Zurueck/Vor, Skins und abschaltbare Scanner-Animation. |

### Sensor-Matrix als Diagramme

Unter **Sensor-Matrix -> LIVE-DIAGRAMME** werden die letzten 180 Messzyklen
gezeigt (bei 2s etwa sechs Minuten). Pro Hardware und Einheit gibt es eine
eigene Skala; gleichnamige SSDs bleiben anhand ihrer ID getrennt. Uhrzeiten
unten sind echte Messzeitpunkte, die Y-Skala passt sich den sichtbaren
Messreihen an. Sensornamen/Farbe und aktueller Wert stehen in der Legende;
per Haken einzelne Kurven fuer diese Sitzung ein-/ausblenden.

Der Suchfilter gilt fuer Diagramme und Tabelle. Auf **DETAILMATRIX /
GRENZWERTE** umschalten, um die bisherige vollstaendige Tabelle zu sehen.
NVMe Warning/Critical Temperature sind Grenzen, keine Ist-Messwerte und
erscheinen nicht als Temperaturkurve. Nicht verfuegbare Werte, deaktivierter
Sensorzugriff und Abfragefehler erzeugen echte Luecken statt Nullwerte.
Historie bleibt sichtbar, der aktuelle Wert wird aber als nicht verfuegbar
bezeichnet. Kanaele, die das ganze Verlaufsfenster verschwunden sind,
werden entfernt.

Nur Arbeitsspeicher, keine neue Aufzeichnung und kein zusaetzlicher
Sensor-/Treiberzugriff. Auch waehrend andere Seiten offen sind, sammelt der
bestehende Live-Takt weiter. Vor dem ersten Messwert oder bei Filter ohne
Treffer erscheint ein ausdruecklicher Hinweis statt erfundener Kurven.

### Das virtuelle Retro-Mainboard

Links unter **Cockpit -> Mainboard / 1986** oeffnen. Die App erfasst lokal
vorhandene Windows-PnP-Geraete und von Windows gemeldete RAM-Baenke.
Stilisierte Bauteile (CPU-Kuehler, RAM-Riegel, GPU, Chipsatz, Buchsen und SSD)
ersetzen die frueheren rechteckigen Gruppenkacheln. Hinweislinien zeigen
Typbezeichnungen und Windows-Status. Bauteil oder Beschriftung anklicken zeigt **alle** darin
erfassten Komponenten mit Namen, Windows-Status und verfuegbarer Eltern-ID.
Controller oder Laufwerke mit SATA-Bezug erscheinen nur, wenn Windows diese
tatsaechlich meldet; NVMe-Systemen wird kein SATA-Geraet hinzuerfunden.
Deaktivierte/problematische, aber vorhandene Geraete behalten ihren gemeldeten
Status. Weitere System-/Peripheriegeraete sind nicht ausgeblendet, sondern
in der entsprechenden Sammelgruppe erreichbar.

Auf der Platine werden alle erkannten Laufwerke einzeln dargestellt, auch
mehrere SSDs desselben Modells. Die virtuelle Platine waechst bei Bedarf.
Ein Klick auf eine Laufwerksbeschriftung zeigt genau dieses Windows-Geraet.
CPU-/GPU-Luefter drehen langsam und symbolisch, ohne Bezug zur echten Drehzahl.
Das Raster bewegt sich nach hinten; mehrere Bergsilhouetten aus sieben
laufenden Binaerbaendern liegen auf derselben bewegten Rasterebene
(`AETHER / Olaf Weins / ` als UTF-8-Binaertext). Alle Animationen folgen
dem Bewegungsschalter und stoppen bei ausgeblendeter Ansicht.

Monitore und bereite Audio-Endpunkte erscheinen einzeln, ebenso gemeldete
Tastatur-/Maus-/Kamera-/Drucker- und physische Bluetooth-Geraeteknoten.
Das koennen mehrere Windows-Schnittstellen desselben physischen Geraets sein.
Bluetooth-Dienste, USB-Controller und sonstige Systemeintraege bleiben in den
Gruppen-Details, statt zusaetzliche physische Gehaeuse zu behaupten.
Bildschirme zeigen soweit verfuegbar EDID-Modell, aktive Desktop-Zuordnung,
Hauptbildschirm und Aufloesung. Audio nennt Windows-Standardrollen
(Wiedergabe/Aufnahme fuer Konsole, Multimedia und Kommunikation) sowie aktive
Audiositzungen. Gerade verwendete Nicht-Standard-Ausgaenge stehen vor nur
bereiten Geraeten; GELB bedeutet aktive Sitzung, GRUEN bedeutet Standardrolle.
Eine aktive Sitzung kann auch still sein. Es wird kein Ton aufgenommen,
abgespielt und keine Lautstaerke geaendert. Abgetrennte/deaktivierte
Audio-Endpunkte bleiben in den Details, ohne als angeschlossenes Geraet zu
erscheinen. Fehler bei der Erkennung werden ausdruecklich angezeigt.

Nach Anschluss-/Ausgabewechsel **HARDWARE NEU LESEN** waehlen: Modelle,
Zuweisungen und Audionutzung sind Momentaufnahmen, keine kontinuierliche
Pegelueberwachung. Der verbundene Bildschirmschoner erhaelt Inventarupdates
etwa einmal pro Minute; ohne AETHER bleibt es die gekennzeichnete
Hardware-Momentaufnahme ohne Live-Messwerte.

Die Platine ist **eine raeumliche schematische Zeichnung**, kein ausgelesener
elektrischer Schaltplan: Leiterbahnen, Bus-Anordnung, Chippositionen, Kuehler
und Gehaeuseformen sind stilisiert. Eine GPU-Zeichnung beweist beispielsweise
keine echte Steckkarte statt integrierter Grafik. Die Anzahl gezeichneter
Formen ist keine vollstaendige physische Stueckliste (RAM-Darstellung maximal
acht Riegel); alle Windows-Geraeteeintraege bleiben in Details erreichbar.
Animierte gestrichelte Linien illustrieren **schematischen Datenfluss**.
Sie beweisen keine physische Verkabelung, PCIe-Lane-Belegung, Richtung oder Datenrate.
Der Schalter **Animation: Datenfluss, Luefter, Raster und Binaertext** nutzt denselben
Animationszustand wie Scanner-Animation. Im Hintergrund wird nicht weiteranimiert.
Oberhalb stehen separat die echten vorhandenen Live-Messwerte fuer
CPU/RAM/GPU, Upload/Download und CPU-Temperatur; keine Werte pro Leiterbahn.
Gemeldete Windows-Elternbeziehungen sind dagegen reale Betriebssystemdaten,
soweit verfuegbar. Der Internetstatus stammt von Windows; die Ansicht startet
keinen eigenen Internettest und keinen Netzwerkscan.

Nach Ein-/Ausstecken eines USB-Geraets **HARDWARE NEU LESEN** waehlen.
Die Ansicht ist eine Momentaufnahme, keine automatische Hotplug-Ueberwachung.
Nicht gemeldete Hardware bleibt leer; Fehler/Teilabfragen werden angezeigt.
Virtuelle Geraete koennen auftauchen, wenn Windows sie als vorhanden meldet.

### xBar und Desktop-Gadgets

- **Diagnose -> AETHER xBar AN/AUS** oeffnet die Diagrammleiste.
- Zahnrad: CPU, RAM, GPU, Temperaturen, Netzwerk und Laufwerksbelegung
  unabhaengig schalten. Laufwerke werden als Belegt-/Frei-Torten dargestellt.
- Im FLOAT-Modus kann die Leiste frei platziert werden; PIN erlaubt Andocken.
- Ueber **Einstellungen -> Desktop-Gadgets** oder das Tray-Symbol bei der Uhr
  Diagramme als einzelne Desktop-Gadgets anzeigen.
- Gadgets am Titel verschieben. Sie liegen hinter normalen App-Fenstern auf
  der Explorer-Desktop-Ebene. Position und Deckkraft werden gespeichert.
- Hauptfenster, xBar und Gadgets besitzen getrennte Deckkraftregler (20-100 %);
  jedes Gadget kann zusaetzlich einzeln geregelt werden.
- Das Tray-Menue kann Gadgets minimieren/wiederherstellen oder schliessen.
  **X blendet das Hauptfenster nur aus. Vollstaendiges Beenden erfolgt im Tray.**
- Optionaler installierter Anmeldestart startet Gadgets und Tray ohne
  Hauptfenster. Vorher mindestens einmal manuell starten und EULA bestaetigen.

### Optionale Netzwerkueberwachung

Die lokale TCP-Verbindungszaehlung ist ohne Capture-Treiber separat aktivierbar.
Bei hoher Verbindungszahl kann ein konfigurierter Hinweis erscheinen.

Paketmitschnitte benoetigen **separat installiertes und passend lizenziertes
Npcap**. AETHER liefert Npcap nicht mit, installiert es nicht und akzeptiert
keine Fremdlizenz. Nur eigenen oder ausdruecklich freigegebenen Verkehr erfassen.

- Erst Schnittstellen lesen, dann passende Ethernet-kompatible Schnittstelle
  waehlen und Mitschnitt ausdruecklich freigeben.
- Maximal 128 Bytes je Paket, 100.000.000 Bytes oder 15 Minuten pro Mitschnitt.
- Lokale Wireshark-kompatible PCAP-Dateien; sieben Tage Aufbewahrung beim
  naechsten Aufbewahrungslauf, begrenzter eigener Gesamtbestand.
- Regeln fuer viele SYN-Zielports, DNS-Fehler und TCP-Verbindungen.
  Tray-Hinweise und lokale Logs, kein Ton und keine automatische Blockierung.
- Keine vollstaendige Wireshark-Protokollanalyse, TLS-Entschluesselung oder
  garantierte Angriffserkennung. Fehlalarme sind moeglich.
- Auch gekuerzte Pakete koennen sensible Daten enthalten. Mitschnitte nicht
  ungeprueft weitergeben. Die begrenzte Live-Anzeige kann Pakete auslassen;
  ihr Zaehler misst keine Verluste im Capture-Treiber.

## 4. Empfohlener, nicht veraendernder Testrundgang

1. Erststart, EULA und Darstellung bei eigener Bildschirmaufloesung pruefen.
2. Cockpit, Taskmanager, Sensoren und Laufwerke ansehen; fehlende Werte notieren.
3. Mainboard oeffnen: Stimmen Boardname, RAM-Baenke und Geraetenamen?
   Bauteile/Beschriftungen anklicken und Animation ein-/ausschalten.
   Ein optionales USB-Testgeraet ein-/ausstecken,
   aktualisieren und die Aenderung pruefen. Keine Komponenten deaktivieren.
4. Zwischen Cockpit und Mainboard sowie mit Zurueck/Vor wechseln.
5. xBar-Diagramme schalten, Laufwerkstorten und freie Positionierung pruefen.
6. Gadgets verschieben, Transparenz einstellen, Hauptfenster per X ausblenden;
   anschliessend alles ueber das Tray wieder oeffnen bzw. beenden.
7. App erneut starten: Bleiben Einstellungen und Gadgetpositionen erhalten?
8. Falls eine Vorversion installiert ist: nach vollstaendigem Beenden Update
   ohne Deinstallation pruefen. Version/Zielordner und erhaltende Einstellungen
   kontrollieren. Autostart nur bei ausdruecklich gewuenschtem Test aktivieren.
9. Hilfe, lesendes Inventar und Diagnose-Export mit Vorschau ausprobieren.
10. Reparaturen, Bereinigungen, Paketaktionen, Benchmarks, Remote-Befehle und
    reale Mitschnitte sind **keine Voraussetzung** fuer diesen Betatest.

## 5. Bekannte Grenzen und Datenschutz

Hardwareerkennung und Sensoren unterscheiden sich je Mainboard, Treiber,
virtueller Maschine, Windows-Version und Sicherheitskonfiguration.
Gemischte DPI-/Mehrmonitor-Desktops und Explorer-Desktop-Gadgets bitte
besonders pruefen. Windows kann Vordergrundaktivierung und Benachrichtigungen
einschraenken. Nicht verfuegbar bedeutet nicht automatisch defekt.

AETHER hat keine eigene Telemetrie oder Cloud-Uebertragung der Messdaten.
Explizit gestartete Netzwerkwerkzeuge, WinGet, Updates und Downloads benoetigen
jedoch Netzwerkzugriff. Inventare, Screenshots und Diagnose-Exporte koennen
Rechner-/Benutzernamen, Geraete-IDs, Dateipfade oder Netzwerkdaten enthalten.
Exportvorschau und Schwaerzung verwenden; keine pauschale Anonymisierung
annehmen. Keine Kennwoerter, Schluessel, persoenlichen Logs oder unbereinigten
Paketmitschnitte fuer Fehlermeldungen versenden.

Diese Beta wurde mit automatischen Assertions, WPF-UI-Tests, Paket-Hashvergleich
und isolierten Setup-/Update-Codepruefungen getestet. Das ersetzt keine
vollstaendige Installations-/Deinstallationspruefung auf jedem Zielsystem.
Reale Autostart-Registrierung und Live-Npcap funktionieren abhaengig von der
jeweiligen Umgebung und muessen kontrolliert getestet werden.

## 6. Fehler oder Vorschlaege melden

Bitte folgende Angaben zusammenstellen:

```text
AETHER-Version:
Installierte oder portable Ausgabe:
Windows-Version / Build:
CPU / GPU / RAM (optional, ohne Seriennummern):
Bildschirmaufloesung / Skalierung / Zahl der Monitore:
Neuinstallation oder Update; vorherige Version:
Schritte zum Nachstellen:
Erwartetes Ergebnis:
Tatsaechliches Ergebnis / genauer Fehlertext:
Immer oder gelegentlich:
Bereinigter Screenshot vorhanden:
```

Bei Setup-Problemen kann es mit `/LOG` gestartet werden, um ein
Installationsprotokoll zu erzeugen. Dieses vor Weitergabe auf private Pfade und
Kontonamen pruefen. Keine Sicherheitsfunktionen abschalten, um einen Fehler
zu umgehen. Aenderungen nur vornehmen, wenn Zweck und Rueckweg klar sind.

**Danke fuers Testen!**
