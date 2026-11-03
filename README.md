# AETHER System Control

**Kostenloses, lokales Windows-Cockpit** fuer Monitoring, Diagnose und Wartung -
von Olaf Weins.

> **Grundregel:** Lesen passiert automatisch. Aendern niemals ohne deine
> ausdrueckliche Bestaetigung. Keine Cloud, kein Tracking - alles bleibt
> auf deinem Rechner.

## Download

Die aktuelle portable Version gibt es unter
**[Releases](../../releases/latest)** - ZIP herunterladen, vollstaendig
entpacken, `SystemCockpit.exe` starten. Keine Installation noetig; die
.NET-Laufzeit ist enthalten.

Neben jedem Release liegt die **SHA256-Pruefsumme** (`.sha256`-Datei).
Pruefen vor dem Entpacken:

```powershell
Get-FileHash .\AetherCockpit-Portable.zip -Algorithm SHA256
```

## Funktionen

- **Live-Cockpit**: CPU, RAM, GPU, Netzwerk, Temperaturen und Systempuls in
  Echtzeit - im Retro-Neon-Design, plus Overlay-Panel fuers Gaming
- **Taskmanager** mit Graph je CPU-Kern, Sensor-Matrix
- **SMART-Diagnose** und Selbsttests fuer SSDs/Festplatten (smartmontools)
- **Mess-Aufzeichnung** mit MIN/MAX/AVG und PresentMon-FPS-Auswertung
- **Cleaner** mit Vorschau - loescht nur ausdruecklich angekreuzte Dateien
- **Paket-Center** (WinGet): installieren, aktualisieren, deinstallieren -
  mit exakten Paket-IDs und Bestaetigungsliste
- **Befehls-Katalog**: 103 Windows-Befehle mit Risikoanzeige (LESEND /
  NETZWERK / AENDERND), Einsteiger- und Expertenmodus, CRT-Konsole
- **Diagnose**: lesende Windows-Pruefungen, Speicherdiagnose-Ergebnisse,
  Diagnosebericht-Export mit Schwaerzung persoenlicher Angaben
- **Rechner-Inventar**: Hardware und installierte Software auf einen Blick
- **Netzwerk**: LAN-Scan, Ping, WLAN, Captive-Portal-Check, Speedtest
- **Vier Skins** (Retro-Neon, Vista Aero, Candy Glossy, Windows 11 Fluent)
  und ein durchsuchbares Hilfe-Center

## Systemvoraussetzungen

- Windows 10 oder 11, 64-Bit (x64)
- Administratorrechte (UAC-Abfrage beim Start; noetig fuer Hardware-Sensoren)

## Hinweis zu Windows-Warnungen

Die EXE ist nicht codesigniert. Windows zeigt daher ggf. "Unbekannter
Herausgeber" bzw. eine SmartScreen-Warnung - ueber *Weitere Informationen ->
Trotzdem ausfuehren* startet das Programm. Virenscanner koennen wegen der
tiefen Hardware-/PowerShell-Zugriffe per Heuristik warnen (False Positive).
Im Zweifel die ZIP mit der Pruefsumme verifizieren.

## Lizenz

Die Nutzung setzt die Zustimmung zur **EULA** voraus (liegt dem Download bei
und wird beim ersten Start angezeigt). Kostenlos fuer private Nutzung;
keine Gewaehrleistung, Nutzung auf eigene Verantwortung.

Enthaltene Fremdkomponenten unter eigenen Lizenzen:

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) - MPL 2.0
- [smartmontools](https://www.smartmontools.org/) - GPL v2
- [PresentMon](https://github.com/GameTechDev/PresentMon) - Intel/MIT, Lizenz im Paket

(c) 2026 Olaf Weins
