# AETHER: Funktionsabgleich und sichere Ausbaustufe

Stand: 03.11.2026 / AETHER 0.7.0-beta.23.

Abgleich oeffentlicher Herstellerbeschreibungen mit dem vorhandenen AETHER-Code.
Keine Installation oder Ausfuehrung der Vergleichsprodukte; kein gemessener
Leistungs-/Qualitaetsvergleich und kein Anspruch auf vollstaendige Gleichheit.
Marketingaussagen zu Beschleunigung oder Sicherheit sind nicht nachgewiesen.
Alle fuenf gewuenschten Arbeitsbereiche wurden betrachtet und erweitert.
Ein kompletter nativer Ersatz fuer saemtliche Spezialwerkzeuge ist diese
Ausbaustufe ausdruecklich nicht.

## Einordnung der Produkte

- Gemeint ist **MB-NEXUS-COCKPIT**, nicht MC Nexus.
- **WingetUI** (oft WinGetUI geschrieben) wurde in **UniGetUI** umbenannt:
  dieselbe Produktlinie, nicht zwei unabhaengige Funktionsvorbilder.
- Das genannte Toolkit ist **Sioni Windows Toolkit Pro / SWT Pro**.
- Wise wurde anhand von **Wise Care 365** eingeordnet; andere Wise-Produkte
  koennen einen anderen Funktionsumfang haben.
- Als weitere Referenzen dienen **Microsoft PC Manager** und
  **Microsoft Sysinternals Autoruns**.

## Vergleich nach Arbeitsbereich

| Bereich / Referenz | AETHER schon vor diesem Ausbau | In Beta 23 ergaenzt | Bewusste Grenze / verbleibender Unterschied |
|---|---|---|---|
| Hardware und SMART / MB-NEXUS | LibreHardwareMonitor, echtes SMART/smartctl, Hotspot/VRAM/Fans/Spannungen soweit geliefert, Sensorwarnungen, Laufwerke, Mainboard | Gemeinsame Suche fuehrt zu passenden Mess- und Diagnoseschritten; Sensor-Snapshot als CSV | Keine erfundenen Sensoren, Lebensdauerprognosen oder rein temperaturbasierten Throttling-Beweise. Kein neuer Sensortreiber |
| Messung und Gaming / MB-NEXUS, Sioni | Live-Kurven, JSONL, MIN/MAX/AVG, Logvergleich, PresentMon, gespeicherter Windows-Leistungsindex | Filterbare Statistik mit Export der sichtbaren Reihen; kollidierende Sensor-IDs nun auch in Statistik und Logvergleich sauber getrennt; widerspruechliche Duplikate unbekannt statt gemittelt | Keine synthetischen Belastungstests oder neue Stabilitaetsgarantie. Present-Framezeiten sind nicht automatisch angezeigte FPS |
| Windows-Diagnose und Reparatur / Sioni | Risikokatalog mit 103 Befehlen, Einsteiger-/Expertenmodus, SFC/DISM, Windows-Status, Wartungsplaene mit Vorschau/Einzelfreigabe, Berichte, Diagnoseexport | Aufgabenorientierter Arbeitsplatz mit gemeinsamer Mehrwortsuche, Schrittfolge und Grenzen; gesamter Katalog auffindbar, kein Start aus der Suche; Auftragsverlauf | Keine pauschale Reparatur oder exakte Gleichheit mit Sionis Assistenten. Kompatibilitaet optionaler Windows-Befehle wird weiterhin vom lokalen Werkzeug gemeldet; keine neue per-Build-Kompatibilitaetsdatenbank |
| Software / UniGetUI | WinGet-Listen, Suche, Mehrfachauswahl mit exakten IDs, bestaetigte Installationen/Updates/Deinstallationen | Paketdetails, Bindung an gemeldete Quelle, AETHER-Paketlistenimport/-export, serielle Auftraege mit Einzelstatus und Journal; alle IDs/Quellen in scrollbarer Vorschau | Native Verwaltung bleibt WinGet. Scoop/Chocolatey/pip/npm usw. bei Bedarf im bewusst gestarteten, bereits installierten UniGetUI. Keine externe Aktion wird als AETHER-Auftrag ausgegeben |
| Softwaretransfer / UniGetUI | Softwareinventar | Strikt validierte AETHER-Wunschliste mit Quelle und ID; Import ist nur Vorschau, Installieren separat freigeben | Eigenes Format, kein UniGetUI-/WinGet-Bundleformat. Max. 100 Pakete/128 KiB. Keine Quelleneinrichtung, Versionspins, Installer-/Lizenz-/Einstellungsbackups oder automatische Wiederherstellung. Quellenlose Eintraege werden beim Export sichtbar ausgeschlossen |
| Netzwerk / Wireshark, MB-NEXUS | Gateway/Ping/LAN/WLAN/Speedtest, optionale Npcap-Aufzeichnung, begrenzte IPv4/IPv6/TCP/UDP/ICMP/DNS-Analyse, Schwellenalarme | Eigene Register fuer Analyse, Erfassung und Warnungen; kombinierte Text-/Protokoll-/Port-/SYN-/DNS-Fehlerfilter, Paketdetails, Metadaten-CSV, bestaetigte Dateiuebergabe an installiertes Wireshark | Keine Wireshark-Displayfilter, Deep-Protocol-Dissektoren, TCP-Reassembly, TLS-Entschluesselung oder native PCAP-Offlineanalyse. PCAP wird bei Bedarf extern geoeffnet. Auch Wireshark selbst ist kein IDS |
| Speicherplatz / Wise, Microsoft PC Manager | Cachekatalog, Kategorien, gepruefte Dateivorschau, Auswahl, erneute Pruefung vor bestaetigter Loeschung | Pfadfilter, sichtbare Kandidaten gezielt ankreuzen, explizite Gesamt-/versteckte Auswahl; rein lesende Suche nach grossen Dateien und CSV | Keine pauschale Laufwerksbereinigung, keine Downloads/Dokumente automatisch loeschen. Grosse-Dateien-Suche hat keine Loeschfunktion; max. 100.000 Eintraege/32 Ebenen/1.000 groesste Treffer, keine Links/Junctions, Leseprobleme sichtbar |
| Autostart / Wise, Autoruns | Reversible Verwaltung von 64-Bit-HKCU-/HKLM-Run-Zeichenfolgen mit Sicherung und Konfliktschutz; Dienste/Aufgaben separat | Suchbare Tabelle statt Kombinationsliste, voller Befehl und Quelle in Details und Bestaetigung, unveraenderter Zustand wird nicht nochmals angewendet | Kein Autoruns-Vollersatz: RunOnce, alle Shell-/Boot-/Treiber-Erweiterungen, andere Konten und Signatur-/Reputationspruefungen sind hier nicht zusammengefuehrt |
| Desktop / Microsoft PC Manager | xBar-Diagrammleiste, frei verteilbare Gadgets, Deckkraft, Tray, freigegebener Autostart, Schoner | Einheitlich ueber Aufgaben-/Funktionssuche erreichbar | Keine Werbung-/Popup-Blockierung oder automatische Hintergrundoptimierung |
| Oberflaeche / alle Referenzen | Eigenes Retro-Neon, Navigation mit Verlauf, Hilfecenter, Tooltips | Strg+K/Funktion finden in beiden Hauptfenstern; Ablauf- und Risikohinweise; dunkle lesbare Tabellenkoepfe; Konsole anfangs platzsparend verborgen, bei Auftrag/Wartung sichtbar, manuell umschaltbar; Fenster bis 680 DIP Hoehe verkleinerbar | Kein UI-Clone. Konsole bleibt ein bewusst maechtiges Werkzeug: Expertenbefehle starten dort weiterhin direkt mit Enter. Der Suchdialog fuehrt nichts aus und aktiviert keinen Expertenmodus |

## Sichere Produktlinie

Nicht uebernommen werden Registry-Cleaner/-Defragmentierung, RAM-Cleaner,
pauschales Debloating, Dienstabschaltungspakete, Schutzabschaltungen,
automatische Treiberdownloads, Dateischredder und angebliche Leistungs- oder
Gesundheitsscores. Vorhandene gezielte Windows-Aktionen behalten ihre
individuellen Warnungen, Sicherungs-/Ruecknahmeregeln und Bestaetigungen.

Gefuehrte Aenderungen bleiben explizit. Die freie Expertenkonsole bleibt die
dokumentierte Ausnahme: direkter Start, eigene Verantwortung, keine
automatische Protokollierung ihrer Inhalte. Externe Programme verwenden
eigene Einstellungen und Schutzregeln; AETHER kontrolliert diese nicht.

## Neue Arbeitsablaeufe

1. **Finden:** links FUNKTION FINDEN oder Strg+K. Arbeitsbereich und Suchbegriffe
   kombinieren. Wirkung/Grenzen lesen, dann Zielbereich oeffnen.
2. **Software:** WinGet-Liste laden, Paketdetails/Quelle pruefen, Auswahl und
   vollstaendige Vorschau bestaetigen. Einzelbefunde unter Auftraege lesen.
   Nach einem Lauf ist die Liste veraltet und muss neu geladen werden.
3. **Support:** Symptomzeit notieren, Ereignisse und Messwerte korrelieren,
   Bericht mit Schwaerzung pruefen, erst danach Reparatur freigeben.
4. **Netzwerk:** Erfassung separat freigeben, danach Live-Anzeige filtern.
   Metadaten-CSV enthaelt IP-Adressen. Fuer volle Protokollanalyse Mitschnitt
   stoppen und die lokale Datei bewusst an Wireshark uebergeben.
5. **Speicherplatz:** grosse Dateien erst finden und bewerten; Cache-Cleaner
   separat verwenden. Ansichtsfilter sind keine Loeschfreigabe. Versteckte
   angekreuzte Kandidaten bleiben Teil der ausdruecklich genannten Gesamtmenge.

Auftraege sind ein auf 200 Eintraege begrenzter RAM-Verlauf dieses
Systemkontrollfensters, kein globaler Scheduler. Pakete laufen seriell;
Fehler, kein passendes Upgrade, Abbruch und nicht ausgefuehrte Folgeschritte
bleiben unterscheidbar. Sensible Anzeigen und freie Befehle erscheinen nicht
im Verlauf. Bestehende Wartungsplanberichte und das lokale Aktionsjournal
bleiben davon unabhaengig. Abbruch macht erfolgte Aenderungen nicht rueckgaengig.

## Quellen

Offizielle Produkt-/Dokumentationsseiten, am 03.11.2026 abgerufen:

- [MB-NEXUS-COCKPIT](https://bmbaer.hier-im-netz.de/)
- [UniGetUI, Funktionen und Umbenennung](https://www.marticliment.com/unigetui/)
- [UniGetUI, aktuelles offizielles Repository](https://github.com/Devolutions/UniGetUI)
- [Wireshark: Funktionsumfang und ausdrueckliche Nicht-IDS-Grenze](https://www.wireshark.org/docs/wsug_html_chunked/ChapterIntroduction.html)
- [Sioni Windows Toolkit Pro](https://sioni.de/download.php?slug=sioni-windows-toolkit-pro)
- [Wise Care 365](https://www.wisecleaner.com/wise-care-365.html)
- [Microsoft PC Manager](https://pcmanager.microsoft.com/)
- [Microsoft Sysinternals Autoruns](https://learn.microsoft.com/en-us/sysinternals/downloads/autoruns)

Keine Quelltexte, Bilder oder Installationspakete dieser Produkte uebernommen.
