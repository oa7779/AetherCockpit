# Funktionsübersicht

AETHER bündelt Beobachtung, Auswertung, Diagnose und Verwaltung. Diese Übersicht
beschreibt den aktuellen Quellstand Beta 36; sie enthält keine Versionsgeschichte.
Bedienabläufe stehen im [Benutzerhandbuch](USER-GUIDE.md).

## Beobachtung und Darstellung

| Bereich | Umfang |
|---|---|
| Cockpit | CPU-Gesamtlast und logische Kerne, RAM/Commit/Cache, GPU, Netzwerkdurchsatz, Temperaturen und physische Datenträgerzähler |
| Prozesse | Such- und sortierbare Tabelle, Intervall-CPU, Working Set, Threads, Handles, I/O und identitätsgesicherte Details |
| Sensor-Matrix | Alle gemeldeten Sensorkanäle, Filter, Verlaufskurven nach Hardware und Einheit, gekennzeichnete Grenzwerte |
| Laufwerke | Belegung, Windows-Storage-Status, ATA-/NVMe-SMART-Diagnose und Rohdaten |
| Virtuelles Mainboard | Anklickbare PnP-Gruppen und Laufwerke, Monitor-/Audiozuordnung, Windows-Status und schematische Neon-Zeichnung |
| Desktop | Sechs xBar-Diagrammgruppen, Gadgets mit Position und Deckkraft, Tray, Temperatur-Overlay und Multi-Monitor-Bildschirmschoner |
| Oberfläche | Vier Skins, Tag-/Nachtmodus, unabhängige Sekundenanzeige, LED-Instrumente, Animation ein/aus, aufgabenbezogene Navigation, Arbeitsplatzsuche und lokale Modulhilfe |
| Neon-Effekte | Verstärkte Halos, Pulslicht und Energiebewegung in Schoner, Hauptfenster und Dashboard; Bewegung abschaltbar |

Sensoren werden nur angezeigt, wenn sie der Provider meldet. Das Mainboard ist
keine elektrische Topologie; Gruppenmaxima und dynamische Skalen sind
entsprechend gekennzeichnet. [Messwerte richtig lesen](HARDWARE-SENSORS.md).

## Auswertung und Diagnose

- MIN/MAX/AVG, Statistikfilter und CSV-Export, Sensor-Momentaufnahme,
  optionale JSONL-Aufzeichnung und Vergleich zweier Messlogs.
- PresentMon-Capture für einen Prozess und Frame-CSV-Auswertung je Swapchain.
- Windows-Schutz- und Systemstatus, vorhandene Speicherdiagnose-Ergebnisse,
  Inventar und Diagnose-Export mit Vorschau und Musterschwärzung.
- Ereignisanzeige mit Kanalwahl, Zeitraum, Stufenfilter, Volltext und XML;
  begrenzter System-/Application-Ereignismonitor.
- Windows-Assistenten für Systemdateien/Komponenten, Updatezustand,
  Netzwerk sowie Geräte/Treiber.
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
- Cache-Cleaner mit Kategorie-, Datei- und Löschvorschau; große Dateien
  separat und lesend finden.

Rücknahme ist nur für unterstützte Aktionen möglich. Die freie Konsole
startet Befehle unmittelbar; geführte Aktionen behalten ihre Bestätigungen.
Der Cleaner löscht endgültig ohne Papierkorb.

## Netzwerk und Geräteverwaltung

- Adapter-/IP-/TCP-Diagnose, Ziel- und Router-Ping, WLAN-/Bluetooth-Status,
  Captive-Portal-Probe und Download-Durchsatztest nach Freigabe.
- Privater IPv4-LAN-Scan mit ARP/MAC und optional importierter OUI-Datenbank.
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
keine Tenant-Daten oder Lizenzen.
