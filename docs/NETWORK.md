# Netzwerk

## Adapter und Verbindungen

AETHER liest Adapter, IP-Konfiguration, Gateway, DNS, TCP-Verbindungen,
WLAN-Profile und gemeldete Bluetooth-Geräte. TCP-Listen sind begrenzte
Momentaufnahmen. WLAN-Abfragen können eine Windows-Standortfreigabe benötigen.
Die Mainboardansicht nutzt den Windows-Internetstatus, keinen eigenen Online-Test.

Die separat aktivierbare lokale Verbindungsüberwachung zählt TCP-Verbindungen
im Zustand `Established` oder `SynSent`. Sie benötigt keinen Capture-Treiber.
Ein hohes Ergebnis ist ein Hinweis, kein Schadsoftwarebefund.

## Gezielte Prüfungen

Ziel- und Router-Ping starten nach Freigabe; Hostname oder IP selbst wählen.
Der Windows-Netzwerkassistent liest Konfiguration und prüft gezeigte
Gateway-/DNS-Ziele, unter anderem die Auflösung von `www.microsoft.com`.
Diese Abfragen erzeugen Netzwerkverkehr, ändern aber keine Adaptereinstellungen.

Der Captive-Portal-Test ruft nach Freigabe
`http://www.msftconnecttest.com/connecttest.txt` auf. Die eigene IP ist am
Messziel sichtbar. Umleitungen werden angezeigt, nicht automatisch geöffnet.
Zeitlimit zehn Sekunden; Antworten über 1 KiB werden als abweichend gemeldet.

Der Download-Durchsatztest lädt 1–200 MiB (Vorgabe 25 MiB) über eine
HTTPS-Verbindung von `speed.cloudflare.com`. Zeitlimit 120 Sekunden.
Er misst den Anwendungsdurchsatz dieser Verbindung, keinen Upload und
keine zugesicherte Anschlusskapazität. Datenvolumen und sichtbare IP stehen
in der Freigabe.

## LAN-Scan und Herstellerzuordnung

LAN-Scan ist auf private RFC1918-IPv4-Netze `/24` bis `/30` begrenzt:
höchstens 254 Hosts und 16 parallele ICMP-Abfragen.
ARP/MAC ist nur im direkt erreichbaren Segment verfügbar.
Ein Gerät ohne ICMP-Antwort kann trotzdem vorhanden sein.

Auto-Scan braucht eine eigene Bestätigung. Er wartet 60 Sekunden nach
Abschluss eines Durchlaufs und endet beim Ausschalten, Abbrechen oder
Schließen des Fensters. Öffnen der Ansicht startet keinen Scan.

Herstellerzuordnung nutzt eine selbst importierte
[IEEE-MA-L-OUI-CSV](https://standards-oui.ieee.org/oui/oui.csv) mit den Spalten
`Assignment` und `Organization Name`. Importlimit: 16 MiB und 100.000
Zuordnungen. Ohne Datenbank bleibt die Zuordnung unbekannt.
Lokal/randomisierte MAC-Adressen erhalten keinen Hersteller.
Eine OUI nennt die Registrierung, nicht zwingend den Gerätehersteller.

## Npcap installieren

Paketmitschnitt benötigt separat installiertes und passend lizenziertes
[Npcap](https://npcap.com/#download). AETHER bündelt es nicht, lädt es nicht
selbst herunter und akzeptiert keine Fremdlizenz.

Setup und Netzwerkfenster können die offizielle Downloadseite auf Auswahl
öffnen. Ohne OEM-Redistributionslizenz wird Npcap nicht in AETHER-Pakete
aufgenommen. Technischer Hintergrund:
[Npcap-Entwicklerhandbuch](https://npcap.com/guide/npcap-devguide.html).

## Mitschnitt und PCAP

1. Schnittstellen lesen.
2. Eine Ethernet-kompatible Npcap-Schnittstelle wählen.
3. Berechtigung und Datenhinweis prüfen, dann Mitschnitt bestätigen.
4. Aufnahme stoppen und Datei erst anschließend weiterverwenden.

Der Mitschnitt verwendet keinen Promiscuous-Modus. Andere Linktypen, etwa
Loopback oder WLAN-Rohformate, werden nicht als Ethernet interpretiert.

| Grenze | Wert |
|---|---|
| Aufgenommene Bytes je Paket | höchstens 128 |
| PCAP-Datei einschließlich Header | höchstens 100.000.000 Bytes, also 100 MB |
| Laufzeit | höchstens 15 Minuten |
| Live-Paketansicht | höchstens 200 Zeilen |

Die Datei wird im standardisierten PCAP-Format unter
`%LOCALAPPDATA%\AetherCockpit\Captures` gespeichert. Auch 128 Bytes können
IP-Adressen, DNS-Metadaten oder vertrauliche Nutzdaten enthalten.
Die Begrenzung anonymisiert nicht.

Automatischer Mitschnitt bei Anmeldung ist standardmäßig aus. Er verlangt
separate Zustimmung, Schnittstellenauswahl und eine installierte
Autostart-Konfiguration. Er ist auf einen Mitschnitt je Anmeldung begrenzt,
ohne automatische Verlängerung. Später im Netzwerkfenster deaktivieren.

## Live-Analyse und Übergabe

Die lokale Auswertung erkennt Ethernet/VLAN, IPv4/IPv6, TCP/UDP/ICMP und
begrenzte DNS-Fehlerköpfe. Text, Protokoll, Port, SYN und DNS-Fehler lassen
sich kombinieren. Ein ungültiger Filter wird abgewiesen; der bisherige bleibt
aktiv. CSV exportiert nur sichtbare Live-Metadaten, einschließlich IP-Adressen.
Die PCAP-Datei bleibt von Anzeigefiltern unberührt.

Bei hoher Last kann die Live-Ansicht Pakete auslassen. Ihr Auslassungszähler
misst keine Treiberverluste und bescheinigt keine vollständige Aufnahme.
Es gibt keine Prozesszuordnung, TCP-Reassembly, TLS-Entschlüsselung oder
vollständige Offline-Protokollanalyse.

Nach dem Stoppen kann AETHER die PCAP-Datei nach Bestätigung an ein
installiertes Wireshark übergeben. Dieses ist ein separates Analyseprogramm
mit eigenen Einstellungen; AETHER lädt die Datei dabei nicht in einen
Cloud-Dienst hoch.

## Alarmregeln

Regeln beobachten viele unterschiedliche SYN-Zielports je Sender/Ziel und
Minutenfenster, DNS-Fehlerantworten je Empfänger und Minutenfenster sowie
hohe lokale TCP-Verbindungszahlen.

Vorgaben: 30 SYN-Ports, 30 DNS-Fehler und 100 TCP-Verbindungen;
einstellbar jeweils 5–10.000. Die Regeln liefern Indizien und können
Fehlalarme erzeugen. Sie blockieren nichts und führen keine Reparatur aus.

Hinweise erscheinen ohne Ton im Tray und im lokalen Alarmverlauf.
Benachrichtigungen sind auf eine je 30 Sekunden begrenzt; Windows kann
sie unterdrücken. Die Oberfläche hält höchstens 200 Hinweise.

## Aufbewahrung

Eigene `aether-*.pcap`-Dateien älter als sieben Tage werden beim nächsten
Aufbewahrungslauf entfernt: vor Mitschnittstart und stündlich im Leerlauf.
Vor einer neuen Aufnahme wird vorhandener Bestand auf 400 MB reduziert;
zusammen mit der neuen Datei bleiben höchstens 500 MB eigener Bestand.

Alarmdateien unter `%LOCALAPPDATA%\AetherCockpit\NetworkAlerts` werden nach
sieben Tagen beim nächsten Lauf entfernt. Ab einer Tagesdateigröße von
2 MiB wird weiteres Schreiben verweigert; die letzte angehängte Zeile kann
die Grenze geringfügig überschreiten. Der Alarm bleibt sichtbar, ein
Protokollfehler wird gemeldet.

Die allgemeine Diagnoseaufzeichnung und zentrale Berichtsübertragung sind
getrennte Funktionen. [Daten und Freigaben](SECURITY-AND-PRIVACY.md)
· [Zentrale Verwaltung](CENTRAL-MANAGEMENT.md).
