# Sicherheit und Datenschutz

## Rechte und Freigaben

Das Cockpit startet mit `requireAdministrator` und verlangt Windows-UAC.
Bei Freigabe mit einem anderen Administratorkonto gelten HKCU, Benutzerpfade
und lokale Daten dieses Kontos. Hardware-Sensorzugriff kann einen
Low-Level-Treiber laden; AETHER schaltet dafür keinen Windows-Schutz ab.

Geführte Verwaltungsaktionen zeigen Ziel und Wirkung vor der Bestätigung.
Wartungspläne verlangen standardmäßig einen Wiederherstellungspunkt und
anschließend Einzelbestätigungen. Die **freie Administratorkonsole führt
Befehle mit Enter direkt aus**; sie ist nicht durch die Schutzregeln der
geführten Module eingeschränkt.

Einsteiger-/Expertenmodus ist eine Bedienhilfe, keine zusätzliche Windows-
Rechtegrenze. Die App umgeht weder SYSTEM-/TrustedInstaller-Rechte noch GPOs.
Vor Änderungen aktuelle Daten sichern; Abbruch und Wiederherstellungspunkt
sind keine vollständige Rücknahme.

## Lokale Speicherung

Basis: `%LOCALAPPDATA%\AetherCockpit` des ausführenden Kontos.

| Daten | Ort und Aufbewahrung |
|---|---|
| Einstellungen | `settings.json`; keine automatische Löschfrist |
| EULA-Zustimmung | `eula-consent.json` |
| Autostart-/Registry-Sicherungen | `Backups`; keine automatische Löschfrist |
| Messaufzeichnung | `History`; höchstens sieben Tagesdateien mit je 16 MiB |
| Aktionsjournal | `Actions`; höchstens 30 Tagesdateien mit je 16 MiB |
| Wartungs-/WinRM-Berichte | `Operations\Reports`; keine automatische Löschfrist |
| Letzte Hintergrunddiagnose | `Operations\Reports\background-latest.json`; wird ersetzt |
| Paketmitschnitte | `Captures`; sieben Tage bis zum nächsten Aufbewahrungslauf, begrenzter eigener Gesamtbestand |
| Netzwerkalarme | `NetworkAlerts`; sieben Tage bis zum nächsten Aufbewahrungslauf |
| Clienttoken | `Manager\device-token.bin`; DPAPI-Schutz für das aktuelle Windows-Konto |

Dateianzahl bei History/Actions ist keine garantierte kalendarische Löschfrist.
Exporte werden im vom Nutzer gewählten Pfad gespeichert. Operations-Berichte
können über längere Zeit wachsen und sollten selbst verwaltet werden.
Die entfernte Musikfunktion erzeugt keine Audiokonfiguration mehr.
Eine aus älteren Versionen verbliebene `screensaver-audio.json` wird vom
Bildschirmschoner nicht gelesen, geändert oder gelöscht; externe Musikdateien
werden nicht geöffnet. Der allgemeine Uninstaller entfernt weiterhin
AETHER-Kontodaten nach seinem bestehenden Ablauf.

**Der aktuelle Uninstaller entfernt den Installationsordner und die gesamten
AETHER-Kontodaten des ausführenden Kontos.** Externe Dateien und andere
Windows-Profile bleiben bestehen. Portable Kopien teilen dieselben Kontodaten.
[Installation und Datensicherung](INSTALLATION.md).

## Sensible Anzeigen

Schlüssel werden nicht automatisch abgefragt. WLAN-Schlüssel,
BitLocker-RecoveryPassword und vorhandener OEM-Firmwarekey erfordern
Warndialog und Administratorrechte. Eine digitale Windows-Lizenz wird
nicht als Firmwarekey dargestellt.

Die Schlüsselanzeige hat keine AETHER-Speicher-/Exportfunktion und wird
bei Tabwechsel und Schließen geleert. Das verhindert keine Bildschirmaufnahme,
Clipboard-Nutzung, Speicherforensik oder Windows-Protokollierung.
Bildschirmfreigabe und Recording vor solchen Abfragen beenden.

Ereignisse, TCP-Listen, Inventar, ACL-Pfade, Shellbags und Backups können
Benutzer-, Rechner- und Gerätenamen, Ziele, Argumente oder private Pfade
enthalten. Der Shellbag-Zeitstempel gehört zum Registry-Schlüssel und ist
nicht zwingend der letzte Ordnerbesuch.

## Diagnose und Exporte

Der Text-Diagnoseexport verwendet bereits gelesene Cockpitwerte. Seine
standardmäßig aktive Schwärzung ersetzt Muster für Rechner-/Benutzernamen,
Benutzerpfade, IPv4 und MAC. **Die Vorschau zeigt den zu speichernden Text:
vor dem Teilen selbst prüfen.** IPv6, freie Ereignisnachrichten und andere
Identifikatoren können außerhalb dieser Muster liegen.

Die Musterschwärzung gilt nicht automatisch für Inventare, Operations-Berichte,
PCAP, CSV oder Serverberichte. Auch gekürzte Paketdaten können vertraulich sein.
Messlogs enthalten Hardware-/Sensornamen und Zeitpunkte.

## Einzelplatz und Clientmodus

AETHER hat keine allgemeine App-Telemetrie oder automatische Cloud-Anmeldung.
**Einzelplatz** nimmt keinen Kontakt zum Verwaltungsserver auf.
Ein eingerichteter Client sendet Diagnoseberichte nur nach Anmeldung,
manueller Gerätefreigabe und gesondertem Übertragungs-Opt-in.

Diese Berichte enthalten Rechner-/Windows-Daten, begrenzte Ereignisse,
PnP-Geräte, Adapter, installierte Anwendungen, Treiber und Befunde.
Sie werden vor Übertragung nicht durch die Export-Musterschwärzung gefiltert.
Der Server speichert sie nach Empfang 90 Tage. Clienttoken sind lokal
DPAPI-geschützt; das schützt sie nicht vor berechtigten Prozessen desselben Kontos.
[Anmeldung, Speicher und HTTPS](CENTRAL-MANAGEMENT.md).

## Tatsächliche Netzwerkzugriffe

Lokale Verarbeitung bedeutet nicht, dass jede Funktion offline arbeitet:

- Ping, DNS, LAN-Scan und Captive-Portal-Probe kontaktieren gewählte Ziele.
- Der Downloadtest verwendet `speed.cloudflare.com`.
- WinGet und Windows-/Defender-Updates kontaktieren ihre konfigurierten Quellen.
  DISM RestoreHealth kann Windows Update nutzen.
- Freigegebene WinRM-Diagnose kontaktiert eingetragene Geräte.
- Die Npcap-Downloadseite wird nur nach Auswahl im Browser geöffnet.
- Freie Konsolenbefehle und externe Programme können selbst Daten senden.

Die jeweiligen Netzwerkwerkzeuge zeigen ihre Wirkung vor dem Start.
Verwaltungsserver, Netzwerkmonitor und Messaufzeichnung haben getrennte
Einstellungen und Freigaben.

## Prüfungen richtig einordnen

Ein grüner Status bestätigt die einzelne gemeldete Prüfung, nicht die
Sicherheit des gesamten Rechners. Fehlende Daten bleiben unbekannt.
`Get-HotFix` zeigt keine vollständige Aktualität; Defender-Status bewertet
kein alternatives Antivirusprodukt.

Zertifikatsprüfung liest CurrentUser/LocalMachine `Root` und `My`,
höchstens 1000 Zertifikate pro Speicher. Ketten-/Datumsprüfung erfolgt offline,
ohne Downloads oder Online-Sperrprüfung. Lokales Root-Vertrauen beweist
keine Unbedenklichkeit.

`AllowTelemetry` ist ein Richtlinienwert, keine Messung von Windows-Verkehr;
Wert 0 schaltet auf Windows Pro nicht sämtliche Diagnosedaten ab.
Treiber- und App-Versionen beweisen weder Fehlerfreiheit noch Aktualität.

## Drittanbieter und Protokolle

Hardwareprovider, smartctl, PresentMon und optional Npcap haben eigene
Zugriffsvoraussetzungen und Lizenzen. Windows-Audit und gestartete Programme
können unabhängig von AETHER protokollieren.

Freie Befehle, deren Verlauf und Ausgabe werden nicht von AETHER gespeichert
und beim Modulwechsel gelöscht. Geführte Aktions-/Wartungsprotokolle bleiben
getrennt erhalten. Ein Protokollfehler wird sichtbar gemeldet und stoppt
Folgeaktionen, soweit der jeweilige Ablauf ein Journal verlangt.

[Drittanbieter-Hinweise](../THIRD-PARTY-NOTICES.md)
· [Netzwerkgrenzen](NETWORK.md)
