# AETHER: Funktionsumfang und Grenzen

Diese Uebersicht beschreibt eigenstaendige AETHER-Funktionen und ihre
bewussten Grenzen. Sie ist kein Produkt- oder Leistungsvergleich.

| Bereich | Funktionen | Bewusste Grenzen |
|---|---|---|
| Hardware und Speicher | Live-Sensoren, SMART-Diagnose, Laufwerke, Mainboard, Snapshot- und Messdatenexport | Nur gemeldete Sensorwerte; keine erfundenen Messwerte, Restlebensdauerprognosen oder rein temperaturbasierten Throttling-Beweise |
| Messung und Gaming | Live-Kurven, JSONL-Aufzeichnung, MIN/MAX/AVG, Logvergleich und Frame-Capture | Keine synthetischen Belastungstests oder Stabilitaetsgarantie; Framezeiten sind nicht automatisch angezeigte FPS |
| Windows-Diagnose und Reparatur | Windows-Status, gezielte Diagnoseassistenten, Risikokatalog, Vorschau, Berichte und einzeln freizugebende Wartungsaktionen | Keine pauschalen Reparaturen; Kompatibilitaet optionaler Windows-Befehle wird lokal erkannt |
| Softwareverwaltung | Paketlisten, Suche, exakte IDs und Quellen, bestaetigte Installationen/Updates/Deinstallationen, Auftragsverlauf und Import-/Exportvorschau | Aktionen erfordern Auswahl und Bestaetigung; externe Paketverwaltungen bleiben separate Programme mit eigener Steuerung |
| Netzwerk | Adapter- und Verbindungsdiagnose, freizugebender Mitschnitt, begrenzte Live-Analyse, Warnungen und PCAP-Export | Kein IDS, keine TLS-Entschluesselung, TCP-Reassembly oder vollstaendige Offline-Protokollanalyse; externe Dateiuebergabe erfolgt nur nach Bestaetigung |
| Speicherplatz | Cache-Kategorien, Dateivorschau und ausdruecklich bestaetigtes Loeschen; grosse Dateien rein lesend finden | Keine pauschale Laufwerksbereinigung und kein automatisches Loeschen persoenlicher Dateien |
| Autostart und Verwaltung | Suchbare Autostart-Verwaltung, Sicherungen, Konfliktschutz und einzeln bestaetigte Aenderungen | Keine vollstaendige Erfassung aller Boot-, Shell- oder Treibererweiterungen und keine automatische Reputationsbewertung |
| Desktop | Retro-Neon-Oberflaeche, Gadgets, Seitenleiste, Tray und konfigurierbarer Bildschirmschoner | Keine Werbungssperre oder automatische Hintergrundoptimierung |

## Sichere Produktlinie

Registry-Cleaner, Registry-Defragmentierung, RAM-Cleaner, pauschales
Debloating, Dienstabschaltungspakete, Schutzabschaltungen, automatische
Treiberdownloads, Dateischredder und unbelegte Leistungs- oder
Gesundheitsscores sind nicht Teil des Produktumfangs.

Gefuehrte Aenderungen bleiben einzeln bestaetigungspflichtig. Die freie
Administratorkonsole ist eine bewusste Ausnahme: Befehle starten direkt und
liegen in der Verantwortung der nutzenden Person. Externe Programme behalten
ihre eigenen Einstellungen und Schutzregeln; AETHER kontrolliert diese nicht.

## Arbeitsablaeufe

1. **Finden:** Arbeitsbereich und Suchbegriffe waehlen; Wirkung und Grenzen
   lesen, bevor der passende Bereich geoeffnet wird.
2. **Software:** Paketdetails und Quelle pruefen, Auswahl und vollstaendige
   Vorschau bestaetigen; nach einem Lauf die Paketliste neu laden.
3. **Support:** Symptomzeit notieren, Ereignisse und Messwerte vergleichen,
   Bericht vor Weitergabe pruefen und erst danach Reparaturen freigeben.
4. **Netzwerk:** Erfassung separat freigeben und Live-Anzeige filtern.
   Mitschnitte koennen sensible Daten enthalten; lokale Dateiuebergaben
   erfolgen ausdruecklich und nach dem Stoppen.
5. **Speicherplatz:** grosse Dateien erst finden und bewerten; Cache-Aktionen
   getrennt auswaehlen und die Gesamtmenge vor dem Loeschen bestaetigen.

Auftraege sind ein auf 200 Eintraege begrenzter RAM-Verlauf dieses
Systemkontrollfensters, kein globaler Scheduler. Pakete laufen seriell;
Fehler, Abbruch und nicht ausgefuehrte Folgeschritte bleiben unterscheidbar.
Sensible Anzeigen und freie Befehle erscheinen nicht im Verlauf. Abbruch
macht bereits erfolgte Aenderungen nicht rueckgaengig.
