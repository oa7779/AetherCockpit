# Funktionsübersicht

AETHER bündelt Beobachtung, Auswertung, Diagnose und Verwaltung. Diese Übersicht
beschreibt den aktuellen Quellstand einschließlich unveröffentlichter Hardware- und Prozessarbeit;
der veröffentlichte Beta-Stand steht in der [Produktübersicht](../README.md).
Bedienabläufe stehen im [Benutzerhandbuch](USER-GUIDE.md).

## Beobachtung und Darstellung

| Bereich | Umfang |
|---|---|
| Cockpit | Übersicht mit Windows-Prüfstatus, gemeldeten Auffälligkeiten, CPU-/RAM-/GPU-/Download-Kernwerten und Schnellzugriffen; ausklappbare Live-Details zu Kernen, Sensoren, Datenträgern, Prozessen, Ereignissen und Aufzeichnung |
| Prozesse (Erweiterung unveröffentlicht) | Virtualisierte Liste und Prozessbaum, identitätsgebundene Details und Aktionen, Intervall-CPU, Working Set/private Bytes, Threads, Handles, Prozess-I/O, Kurzzeithistorie und Systembeziehungen |
| Hardware Intelligence (unveröffentlicht) | 20 Klassen, Quelleninspektor, exakte Sensorgerätebeziehungen, WLAN, Windows-Takte, WDDM/Direct3D/Video, SMART/NVMe/TRIM/Controller, EDID/HDR und Audioformatprüfungen; M3 mit dokumentierten Grenzen abgenommen |
| Sensor-Matrix | Alle gemeldeten Sensorkanäle, Filter, Verlaufskurven nach Hardware und Einheit, gekennzeichnete Grenzwerte |
| Laufwerke | Belegung, Windows-Storage-Status, ATA-/NVMe-SMART-Diagnose und Rohdaten |
| Virtuelles Mainboard | Anklickbare PnP-Gruppen und Laufwerke, Monitor-/Audiozuordnung, Windows-Status und schematische Neon-Zeichnung |
| Desktop | Sechs xBar-Diagrammgruppen, Gadgets mit Position und Deckkraft, Tray, Temperatur-Overlay und Multi-Monitor-Bildschirmschoner |
| Oberfläche | Retro-Neon als Standard, optionale Skins und Windows Modern nach Windows-Hell-/Dunkelwahl, unabhängige Sekundenanzeige, Animation ein/aus, aufgabenbezogene Navigation mit handlungsbezogenen Modulnamen, Arbeitsplatzsuche und lokale Modulhilfe |
| Neon-Effekte | Verstärkte Halos, Pulslicht und Energiebewegung in Schoner, Hauptfenster und Dashboard; Bewegung abschaltbar |

Windows Modern verwendet ein animiertes **System-Schema**, neutrale Diagramme,
Gadgets und xBar. In diesem Modus ist der AETHER-Bildschirmschoner gesperrt.
Die Desktopoberfläche bleibt deutsch; Englisch wird noch nicht angeboten.
Serveroberfläche und Installer erhalten in diesem Schritt kein neues Design.

Sensoren werden nur angezeigt, wenn sie der Provider meldet. Das Mainboard ist
keine elektrische Topologie; Gruppenmaxima und dynamische Skalen sind
entsprechend gekennzeichnet. [Messwerte richtig lesen](HARDWARE-SENSORS.md).

## Prozesszentrale und Systembeziehungen

Community Request: Prozesse können jetzt direkt über ihr Kontextmenü
kontrolliert beendet werden. Der Dialog nennt das identitätsgeprüfte Ziel,
bekannte Kinder und das Datenverlustrisiko; **Abbrechen** ist Standard.
**Fenster schließen** sendet getrennt eine reguläre Schließanforderung.
Prozessbäume benötigen eine bestätigte Zielmenge; geänderte Bäume werden
nicht still erweitert. Die bisherigen Cockpitinstrumente bleiben erhalten.

Ein gemeinsamer Erfassungspfad speist Cockpit und Prozesszentrale.
Bis zu 120 Proben je Identität zeigen CPU, RAM und verfügbares Prozess-I/O
mit MIN/MAX/AVG und Lücken. Nachvollziehbare Regeln nennen ihre Schwellen,
stellen aber keine Sicherheitsdiagnose. Datei-, Symbol-, Token- und
Offline-Vertrauensdetails werden gezielt nachgeladen.

Auf Anforderung werden Windows-Dienste, TCP-/UDP-Endpunkte, ausführbare
Run-/Aufgaben-/AppX-Pfade und qualifizierte Application-Ereignisse verbunden.
Adapter benötigen eine eindeutige lokale Unicastadresse, GPU-Engines eine
PID/LUID-Quelle. Der Hardwarebereich bietet dieselben belegbaren Beziehungen
in umgekehrter Richtung. Socketbesitz ist keine Netzwerkdatenrate;
Prozess-I/O ist kein Beweis physischer Datenträgeraktivität.

Lokale Systemsnapshots vergleichen Identitäten, Dienstzustände und Starttypen,
Autostarts, Verbindungen und bereits gelesene Hardwareverfügbarkeit.
JSON/CSV/Text redigieren sensible Daten standardmäßig; vollständige lokale
Snapshotdateien bleiben nach eigener Bestätigung unverschlüsselt.
`Strg+K` findet Werkzeuge und aktuelle Prozesse. Favoriten, letzte Werkzeuge
und bis zu 16 benannte Prozessansichten sind lokal gespeichert.
[Bedienung](USER-GUIDE.md#prozesszentrale-unveröffentlichter-arbeitsstand),
[Schutz und Datenschutz](SECURITY-AND-PRIVACY.md#prozessaktionen-und-systemsnapshots),
[Prüfbericht](PROCESS-COMMAND-CENTER-REPORT.md).

## Auswertung und Diagnose

- MIN/MAX/AVG, Statistikfilter und CSV-Export, Sensor-Momentaufnahme,
  optionale JSONL-Aufzeichnung und Vergleich zweier Messlogs.
- PresentMon-Capture für einen Prozess und Frame-CSV-Auswertung je Swapchain.
- Windows-Schutz- und Systemstatus, vorhandene Speicherdiagnose-Ergebnisse,
  Inventar und Diagnose-Export mit Vorschau und Musterschwärzung.
- Ereignisanzeige mit Kanalwahl, Zeitraum, Stufenfilter, Volltext und XML;
  begrenzter System-/Application-Ereignismonitor.
- Windows-Assistenten für Systemdateien/Komponenten, Updatezustand,
  Netzwerk, Geräte/Treiber und WinRE/Systemwiederherstellung.
- PowerShell-Katalog für Windows-Features und Capabilities, Treibersicherung,
  lokale Images, Wiederherstellungspunkte und Recovery-Einstellungen;
  Änderungen sind als solche gekennzeichnet.
- Einschaltbare 30-Minuten-Hintergrunddiagnose mit lokalem Bericht und
  Tray-Hinweisen auf neue Befunde.
- Zertifikatsprüfung ohne Online-Sperrprüfung und lesende Shellbag-Auswertung.

Inventare und Ereignisse liefern Anhaltspunkte, keine vollständige
Funktionsprüfung aller Programme. Reparaturen werden separat vorbereitet
und einzeln bestätigt. [Diagnose und Wartung](SYSTEM-CONTROL.md).

## Systemverwaltung und Speicherplatz

- WinGet-Paketlisten, Suche und Details; Installation, Updates und Entfernung
  mit exakten IDs und Quellen, serieller Ausführung und Auftragsstatus.
- Import/Export einer AETHER-Paketwunschliste, ohne automatische Installation.
- Suchbare Autostart-Tabelle; Sicherung und konfliktgeprüfte Änderung
  unterstützter 64-Bit-Run-Einträge.
- Statusbasierte Verwaltung von Prozessen, Diensten, Aufgaben, Windows-Features,
  Energieplänen, Adaptern, DNS, WinHTTP-Proxy, Firewallregeln, lokalen
  Benutzern/Gruppen und ACL-Vererbung.
- Acht feste Explorer-/Datenschutzeinstellungen mit Sicherung.
- 17 Wartungskategorien mit Planvorschau, Wiederherstellungspunkt und
  Einzelbestätigung; Windows-Updates mit GUID, Revision und EULA-Prüfung.
- Durchsuchbarer Windows-Befehlskatalog und freie PowerShell-Administratorkonsole.
- 25 feste Windows-Werkzeugaktionen mit Verfügbarkeitsprüfung und Konsolenausgabe;
  optionale Microsoft-Werkzeuge mit lokalen Start-/Downloadangeboten.
- Cache-Bereinigung mit Kategorie-, Datei- und Löschvorschau; große Dateien
  separat und lesend finden.

Rücknahme ist nur für unterstützte Aktionen möglich. Die freie Konsole
startet Befehle unmittelbar; geführte Aktionen behalten ihre Bestätigungen.
Die Cache-Bereinigung löscht endgültig ohne Papierkorb.

## Netzwerk und Geräteverwaltung

- Adapter-/IP-/TCP-Diagnose, Ziel- und Router-Ping, WLAN-/Bluetooth-Status,
  Captive-Portal-Probe und Download-Durchsatztest nach Freigabe.
- Privater IPv4-LAN-Scan mit ARP/MAC und optional importierter OUI-Datenbank.
- Bestätigtes Wake-on-LAN-Magic-Packet für eine MAC-Adresse im lokalen Netz.
- Bestätigtes Wake-on-LAN-Magic-Packet für eine MAC-Adresse im lokalen Netz;
  UDP-Broadcast, ohne Empfangsbestätigung.
- Separat aktivierbare TCP-Verbindungszählung.
- Npcap-Mitschnitt in PCAP, begrenzte Live-Metadaten, kombinierte Filter,
  CSV und regelbasierte Warnungen.
- Bestätigte Übergabe gestoppter Mitschnitte an ein installiertes Analyseprogramm.
- Lesende WinRM-Diagnose für eingetragene Windows-Ziele.
- Optionaler Clientmodus mit Geräteanmeldung und Berichtsübertragung an die
  separate Verwaltungs-API.
- HTTPS-Webverwaltung mit Gerätefreigaben, Onlinezustand, Richtlinien, Aufträgen
  und Audit; optionaler Windows-Clientdienst für Betrieb ohne geöffnetes Cockpit.
- Eng begrenzte DNS-Cache-Autoreparatur mit lokalem und zentralem Opt-in.

[Netzwerkdetails](NETWORK.md) · [Zentrale Verwaltung](CENTRAL-MANAGEMENT.md)

## Produktgrenzen

AETHER bietet keine pauschale automatische Heilung, Treiberdownloads,
Schutzabschaltung, Registry-/RAM-Bereinigung, pauschales Debloating oder
unbelegte Gesundheitsnoten. Die Paketansicht ist kein IDS und keine vollständige
Offline-Protokollanalyse. Externe Programme behalten ihre eigenen
Einstellungen, Freigaben und Protokolle.

Zertifikate, Netzwerkerreichbarkeit und Firewall werden vom Administrator
eingerichtet. M365-/Copilot-Anzeigen prüfen lokale Voraussetzungen,
keine Tenant-Daten oder Lizenzen. Microsoft-Graph-/Intune-Autopatch-API-
Anbindung ist noch nicht integriert.
