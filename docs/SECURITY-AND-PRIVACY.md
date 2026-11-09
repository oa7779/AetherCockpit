# Sicherheit und Datenschutz

## Datenschutz für Projekt und Support

Verantwortlich für das AETHER-Projektangebot und dessen Support ist Olaf Weins.
Anschrift und Kontakt stehen im [Impressum](IMPRESSUM.md).

### Kontakt per E-Mail

Bei einer Anfrage werden die E-Mail-Adresse, der Inhalt und die mitgesendeten
Angaben zur Bearbeitung der Anfrage verwendet. Rechtsgrundlage ist
Art. 6 Abs. 1 Buchstabe f DSGVO; das berechtigte Interesse ist die
Beantwortung und Bearbeitung von Anfragen zum Projekt. Nach der Bearbeitung
werden Support-E-Mails umgehend gelöscht, soweit keine gesetzliche
Aufbewahrungspflicht entgegensteht. E-Mails werden über den vom Anbieter
genutzten E-Mail-Dienst von Microsoft verarbeitet.

### GitHub und öffentliche Beiträge

Das Projekt und seine Downloads werden über GitHub bereitgestellt. Bei Issues
und Pull Requests sind übermittelte Inhalte und GitHub-Profilangaben öffentlich
sichtbar. Sie werden zur Bearbeitung von Rückmeldungen und zur Entwicklung von
AETHER verwendet; Rechtsgrundlage ist Art. 6 Abs. 1 Buchstabe f DSGVO.
Das berechtigte Interesse liegt in der Bearbeitung von Rückmeldungen und der
nachvollziehbaren Entwicklung des Projekts. Beiträge und ihre Änderungshistorie
bleiben grundsätzlich im Projekt sichtbar, solange sie für diese Zwecke
benötigt werden. Bitte keine vertraulichen oder nicht zur Veröffentlichung
bestimmten Daten in öffentliche Beiträge aufnehmen.

GitHub verarbeitet bei der Nutzung seiner Plattform eigene Daten. Einzelheiten
zu Verarbeitung, Empfängern, internationaler Datenübermittlung und
Speicherdauer durch GitHub stehen in der
[GitHub-Datenschutzerklärung](https://docs.github.com/de/site-policy/privacy-policies/github-general-privacy-statement).
Für den E-Mail-Dienst gelten ergänzend die
[Datenschutzhinweise von Microsoft](https://privacy.microsoft.com/de-de/privacystatement).
Eine Weitergabe an andere Empfänger oder eine Übermittlung in Drittländer
hängt vom jeweiligen Vorgang und den eingesetzten Diensten ab.

### Rechte

Betroffene können unter den gesetzlichen Voraussetzungen Auskunft,
Berichtigung, Löschung, Einschränkung der Verarbeitung und Datenübertragbarkeit
verlangen sowie einer Verarbeitung nach Art. 6 Abs. 1 Buchstabe f DSGVO
widersprechen. Sie können sich außerdem bei einer Datenschutzaufsichtsbehörde
beschweren, etwa beim [Bayerischen Landesamt für Datenschutzaufsicht](https://www.lda.bayern.de/).
Für Anfragen zu diesen Rechten kann die Adresse im Impressum verwendet werden.

### AETHER und selbst betriebene Server

Die folgenden Abschnitte beschreiben die Datenverarbeitung und die
Netzwerkzugriffe der Software. Wer AETHER oder einen Verwaltungsserver selbst
betreibt und dabei personenbezogene Daten anderer verarbeitet, muss die
Verantwortlichkeiten und Informationspflichten für den eigenen Einsatz
gesondert prüfen. Der Projektanbieter erhält nicht allein durch den Einsatz
eines selbst betriebenen Servers dessen Berichte.

## Codesignierung

Die kostenlose Codesignierung über die SignPath Foundation wurde am
07.11.2026 beantragt. Das Antragsformular bestätigt den Eingang.
Die Entscheidung steht aus; ein Zertifikat wurde noch nicht erteilt und
SignPath ist noch nicht in den Build eingebunden. Aktuelle Pakete sind
weiterhin unsigniert.

Der [Windows-CI-Build](DEVELOPMENT.md#github-actions) erzeugt Pakete mit
Prüfsummen und nachvollziehbarer Build-Herkunft. Diese Herkunft ersetzt
keine Codesignatur. Auch eine spätere Signatur bedeutet nicht automatisch
eine Freigabe durch die Sicherheitsrichtlinien eines verwalteten Rechners.

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
Der optional eingerichtete Clientdienst läuft mit separat bestätigten
LocalSystem-Rechten; dies ist nicht der Rechtekontext der Desktop-App.
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
Die lokale M365-/Copilot-Bereitschaftsanzeige meldet keine Tenantdaten und
stellt keine Microsoft-Graph-/Intune-Verbindung her.

Diese Berichte enthalten Rechner-/Windows-Daten, begrenzte Ereignisse,
PnP-Geräte, Adapter, installierte Anwendungen, Treiber und Befunde.
Sie werden vor Übertragung nicht durch die Export-Musterschwärzung gefiltert.
Der Server speichert sie nach Empfang 90 Tage. Clienttoken sind lokal
DPAPI-geschützt; das schützt sie nicht vor berechtigten Prozessen desselben Kontos.
[Anmeldung, Speicher und HTTPS](CENTRAL-MANAGEMENT.md).

Der optionale Clientdienst hat eine eigene Zustimmung zu Übertragung und Betrieb.
Sein Wechsel in Einzelplatz erfolgt durch Entfernen beziehungsweise Stoppen des
Dienstes, nicht durch die Einstellung der Desktop-App. Er meldet Heartbeats,
Inventar und feste Auftragsausgaben auch ohne geöffnetes Cockpit.
Dienstidentität und gepufferte Ergebnisse liegen geschützt unter
`%PROGRAMDATA%\AetherCockpit\ManagedClient`; Token mit DPAPI `LocalMachine`.
SYSTEM und Administratoren behalten Zugriff. Diese Daten bleiben beim Uninstall
erhalten; Gerätezugriff zusätzlich zentral sperren.

Serverrichtlinien und lokales Opt-in begrenzen Systemänderungen; DNS-Cache-
Autoreparatur braucht beide Automatikfreigaben und einen erneut geprüften Befund.
Es gibt keine freie Fernkonsole. Die Webverwaltung verwendet lokale Konten mit
Administrator-, Operator- und Leserrolle; Mehrfaktoranmeldung ist nicht
enthalten. Administratorzugriff nur über HTTPS/VPN verwenden.
Auftragsausgaben können private Pfade und Kontodaten enthalten; keine
Export-Musterschwärzung vor zentraler Speicherung. Aufträge und Audit werden
nicht automatisch gelöscht. Laufende Windows-Aktionen können nach Abbruch
Teilergebnisse oder weiterarbeitende Windows-Dienste hinterlassen.

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

### Optionale Microsoft-Graph-Verbindung des Verwaltungsservers

Nur der gesondert konfigurierte Verwaltungsserver fordert mit seinem
App-Zertifikat Zugriffstoken bei `login.microsoftonline.com` an und ruft
`graph.microsoft.com` auf. Die Anbindung startet nicht automatisch beim
Cockpit und sendet keine Daten an den Projektanbieter. Intune-Geräte- und
Autopatch-Daten werden in der Zentralverwaltung nur auf ausdrücklichen Abruf
geladen. Die Serveranmeldung verwendet den privaten Schlüssel aus
`LocalMachine\My`; Zugriffstoken bleiben im Prozessspeicher und werden nicht
im lokalen Datenbestand abgelegt. Graph-Geräte- und Bereitstellungsdaten
werden von AETHER nicht dauerhaft gespeichert; Audit-Einträge enthalten die
angeforderte Aktion und die betroffene Ressourcen-ID.

Eine Intune-Synchronisierung oder das Pausieren/Fortsetzen einer vorhandenen
Autopatch-Bereitstellung verlangt eine einzelne Bestätigung durch einen
lokalen Administrator und erzeugt einen Audit-Eintrag. Der Graph-Dienst
akzeptiert keine frei wählbaren URLs oder Graph-Befehle. Berechtigungen und
Administratorzustimmung der Entra-App bestimmen den tatsächlichen Zugriff;
Graph-Rollen ersetzen nicht die lokale AETHER-Rollenprüfung.

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
