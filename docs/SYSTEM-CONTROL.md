# Windows-Verwaltung und Wartung

## Geführter Ablauf

In **Superadmin** zuerst Status lesen, Zielzeile und Aktion wählen, Vorschau
prüfen und die Änderung bestätigen. Vor dem Anwenden liest AETHER die
Zielidentität und den relevanten Zustand erneut. Nach der Aktion wird
der Status aktualisiert.

Das schützt etwa vor PID-Wiederverwendung und einer Rücknahme nach externer
Zustandsänderung. Es ist keine Transaktion: Fehler oder Abbruch können
Teilergebnisse hinterlassen. Windows-Rechte, Editionen und Richtlinien gelten
weiterhin.

## Prozesse, Dienste und Aufgaben

Die Prozessansicht zeigt Last, Working Set, Threads, Handles und I/O.
Details verwenden PID plus Startzeit; geschützte oder beendete Prozesse
können unlesbar bleiben.

Superadmin verwaltet Prozesspriorität/-beenden, Dienstzustand und Starttyp
sowie Aktivierung und Ausführung geplanter Aufgaben. Eine Prioritätsänderung,
ein Dienststarttyp oder Aufgaben-Aktivierungszustand kann unterstützt
zurückgenommen werden; ein beendeter Prozess und bereits ausgelöste
Dienst-/Aufgabenwirkungen nicht.

## Autostart und Einstellungen

Die suchbare Autostart-Tabelle zeigt vollständige Befehle. AETHER kann exakte
64-Bit-HKCU-/HKLM-Run-Zeichenfolgen sichern und deaktivieren. Beim
Wiederherstellen werden geänderte Werte oder Konflikte abgewiesen.
RunOnce, 32-Bit-Einträge, Dienste und weitere Boot-/Shell-Erweiterungen
sind nicht vollständig durch diesen Ablauf abgedeckt.

Sicherungen unter `Backups` enthalten Originalbefehle und möglicherweise
sensible Argumente. Nicht ungeprüft weitergeben.

Acht feste Registry-Einstellungen betreffen Dateiendungen, versteckte und
Systemdateien, Taskleistenanimation, Werbe-ID, vorgeschlagene Inhalte,
personalisierte Diagnose-Erlebnisse und optionale Diagnosedaten.
Original-DWORD oder dessen Abwesenheit wird unter
`Backups\registry-settings.json` gesichert. Eine Windows-Diagnoserichtlinie
ist keine Messung des tatsächlich übertragenen Datenverkehrs.

## Weitere Verwaltungsbereiche

Windows-Features, Energiepläne, Adapter, IPv4-/IPv6-DNS, unterstützte
WinHTTP-Proxy-Konfigurationen, Firewallregeln, lokale Benutzer,
Gruppenmitgliedschaft und ACL-Vererbung haben statusbasierte Abläufe.

Die Rücknahme umfasst jeweils die **letzte unterstützte reversible
Sitzungsaktion im RAM**. DNS unterscheidet automatisch/statisch je
Adressfamilie; Dienststarttypen berücksichtigen `DelayedAutoStart`.
Nicht unterstützte Proxy-Dumps werden als nicht reversibel gekennzeichnet.

ACL-Vererbung verlangt vorheriges Lesen des absoluten Zielpfads.
Original-DACL und Dateierstellzeit werden geprüft; ReparsePoints werden
abgewiesen. Owner, Auditing und rekursive Rechtepresets werden dabei
nicht verändert. Netzwerkunterbrechung oder Aussperren lassen sich durch
eine Rücknahme nicht generell verhindern.

## Software und WinGet

Das Paket-Center liest installierte Pakete, Updateangebote und Suchergebnisse
aus der lokalen WinGet-Installation. Details binden an die exakte ID und
gemeldete Quelle. Abgeschnittene IDs sind nicht aktionierbar.

Installieren, Aktualisieren und Deinstallieren zeigt alle IDs und Quellen
in einer scrollbaren Bestätigung. Pakete laufen seriell. Einzelfehler,
Abbruch und nicht gestartete Folgeschritte bleiben unter **Aufträge**
unterscheidbar. Danach die Paketliste neu laden.

**Alle aktualisieren** verwendet genau die geladene und angezeigte Liste,
kein `winget upgrade --all`. Paket-/Quelllizenzen werden mit der Freigabe
akzeptiert; Herstellerinstaller können eigene Fenster anzeigen.
Entfernung erzwingt kein `--silent`.

Paketlistenimport/-export verwendet das AETHER-Format mit höchstens 100
Paketen und 128 KiB. Import erzeugt eine Wunschliste, keine Installation oder
Ist-Liste. Versionen sind Hinweise, keine Pins; Quellen müssen bereits
vorhanden sein. Quellenlose Exporteinträge werden sichtbar ausgeschlossen.

Ein installiertes UniGetUI/WingetUI kann separat geöffnet werden.
Dessen weitere Paketmanager, Einstellungen und Aktionen werden nicht von
AETHER gesteuert oder im AETHER-Auftragsverlauf erfasst.

## Windows-Assistenten und Hintergrunddiagnose

**Diagnose → Windows-Diagnose & Problemlösung** öffnet vier Helfer:

| Assistent | Prüfung |
|---|---|
| Systemdateien/Komponenten | Windows-Daten, DISM `/CheckHealth`, SFC `/verifyonly` |
| Windows Update | Dienststatus, Neustartindikatoren und bis zu 100 Update-Ereignisse aus sieben Tagen |
| Netzwerk | Adapter/IP/DNS, gezielte Gateway- und DNS-Abfragen |
| Geräte/Treiber | PnP-Problemcodes und eine begrenzte installierte Treiberliste |

Jeder startet einzeln und schreibt einen lokalen Bericht. Ein gestoppter
Dienst, ein getrenntes virtuelles Interface oder ein Treiberdatum allein
beweist keinen Fehler. Der Netzwerkassistent erzeugt gezielte Prüfverbindungen;
er ändert die Konfiguration nicht.

**Windows-Reparaturplan vorbereiten** wählt DISM RestoreHealth und SFC
Scannow im Plan aus, führt sie aber nicht aus.

Die Hintergrunddiagnose ist standardmäßig aus. In den Desktop-Einstellungen
aktiviert, prüft sie sofort und danach mit 30 Minuten Pause nach jedem Lauf.
Bei erneutem App-Start läuft sie mit gespeicherter Aktivierung weiter.
Ein Start zur Windows-Anmeldung setzt die separat genehmigte Autostart-Aufgabe
voraus.

Sie liest System-/Application-Ereignisse der letzten 24 Stunden
(höchstens 100 je Kanal), vorhandene PnP-Geräte, Adapter sowie
Registry-App- und PnP-Treiberinventar. Der letzte Bericht ersetzt
`Operations\Reports\background-latest.json`.
Neue handlungsrelevante Befunde lösen einen Tray-Hinweis aus; ein Klick öffnet
die Assistenten. Unveränderte Befunde werden innerhalb des laufenden Monitors
nicht erneut gemeldet. Nach Neustart kann ein Befund wieder erscheinen.

Es gibt keinen automatischen Reparaturmodus. Das Inventar ist keine
Funktionsprüfung aller Anwendungen und keine Prüfung auf die jeweils
neueste Treiberversion.

## Wartungspläne

Operations enthält 17 Kategorien: Inventar, Ereignisse, Autostart/Dienste/Aufgaben,
Komponentenanalyse, NTFS-Onlinescan, DISM-Reparatur, SFC, Cleaner,
Komponentenbereinigung, Laufwerksoptimierung, Defender-Signaturen,
Schnell-/Vollscan, Windows-Updatesuche/-installation und WinGet-Angebot/-updates.

Vier Planvorlagen sind verfügbar; **Nur Diagnose** ist Standard.
Planvorschau startet nichts. Den Lauf bestätigen und jeden Änderungsschritt
einzeln freigeben. Der erste Fehler stoppt die Folgeschritte.

Vor Änderungen ist standardmäßig ein bestätigter Wiederherstellungspunkt
Pflicht. Scheitert er, beginnt keine Wartungsänderung. AETHER aktiviert
Computerschutz nicht heimlich; Abschalten der Pflicht verlangt eine weitere
Freigabe. Ein Wiederherstellungspunkt ersetzt kein Backup.

Wichtige Wirkungen:

- CHKDSK `/scan` kann Onlinekorrekturen auslösen. Im Wartungsplan ist es
  deshalb als Änderung eingestuft; `/f`, `/r` oder erzwungenes Aushängen
  werden nicht automatisch eingesetzt.
- DISM `/RestoreHealth` kann Windows Update kontaktieren. SFC `/scannow`
  ersetzt Systemdateien.
- Komponentenbereinigung verwendet kein `ResetBase`; eine einfache Rücknahme
  ist dennoch nicht verfügbar.
- `defrag /O` überlässt Windows die mediengerechte Optimierung.
- Defender-Scans können gemäß vorhandener Richtlinie Bedrohungen behandeln.

**Anhalten** stoppt nach dem laufenden Schritt, nicht gewaltsam DISM, SFC,
Installer oder Scan. Schließen wartet den Prozess ebenfalls ab. Planläufe
haben kein erzwungenes Prozesszeitlimit. Ein lokaler Exklusivschutz sperrt
eigene parallele Operations-Läufe, nicht fremde Windows-Wartung.

### Windows-Updates

Suche erfolgt auf Anforderung über die vorhandene Windows-/WSUS-Quelle
und schließt Treiberupdates aus. Ergebnisse sind nicht vorangekreuzt.
EULA je Update lesen und akzeptieren. GUID, Revision und Lizenztext werden
vor Download und Installation erneut kontrolliert; Einzelresultate werden
ausgewertet. AETHER startet nicht automatisch neu.

Operations-WinGet-Pläne verlangen eine exakte ID je Zeile; das ist ein
eigener Ablauf neben der Mehrfachauswahl im Paket-Center.

### Leistungsindex

Die Anzeige liest eine vorhandene `Win32_WinSAT`-Bewertung. Nur gültige
Scores zwischen 1 und 9,9 werden angezeigt. Dateizeit des DataStore ist
kein garantierter Messzeitpunkt. Gaming-/D3D-Sentinel moderner Windows-Versionen
wird nicht als aktuelle GPU-Bewertung ausgegeben.

`WinSAT.exe formal` startet nur nach Last-/Dateischreibfreigabe.
Es ist ein Benchmark, kein Bestandteil der Hintergrunddiagnose.

## Cleaner und große Dateien

**Große Dateien finden** scannt einen gewählten Ordner lesend:
höchstens 100.000 Einträge, 32 Ebenen und 1000 größte Treffer.
Links/Junctions werden nicht verfolgt; Fehler und Limits stehen im Ergebnis.
CSV enthält Pfade. **Im Explorer markieren** öffnet nicht die Datei.

Der Cleaner verwendet feste Temp-, Browser-, App-, CrashDump- und
WER-Cachekategorien. Prefetch braucht eine separate Freigabe.
Nicht vorhandene Kategorien liefern keine erfundenen Pfade.
Cookies, Lesezeichen, Profileinstellungen, Downloads, Spiele und
Offline-Medien sind nicht Teil der gezielten Cachepfade.

1. Kategorie und Stufe wählen.
2. Vorschau mit Anzahl, Größe und Katalogrisiko A–D prüfen.
3. Dateien in **Löschen?** ankreuzen.
4. Gesamtmenge bestätigen.

Zeilenmarkierung allein gibt keine Löschung frei. Ein Pfadfilter verändert
nur die Ansicht: versteckte angekreuzte Kandidaten bleiben ausgewählt.
**Keine ankreuzen** leert die gesamte Auswahl.

Kandidaten müssen älter als sieben Tage sein; pro Kategorie höchstens
10.000 Einträge und 32 Ebenen. Vor jedem Löschen werden Zugehörigkeit,
Pfad, Alter, Größe, Änderungsdatum, Attribute und Hardlinkanzahl am
Windows-Dateihandle geprüft. Geänderte, schreibgeschützte, gesperrte,
verlinkte oder mehrfach verknüpfte Dateien werden nicht gelöscht.
Ordner bleiben bestehen.

**Dateien werden endgültig ohne Papierkorb gelöscht.** Abbruch lässt
bereits gelöschte Dateien gelöscht. Ein Protokollfehler stoppt weitere
Löschaktionen. Browser/Apps vorher schließen; erneutes Laden ihres Caches
kann Netzwerkverkehr erzeugen.

Delivery Optimization verwendet separat
`Delete-DeliveryOptimizationCache` mit eigener Freigabe, weil Windows
dafür keine identische Dateivorschau bereitstellt.

## Freie Administratorkonsole

Der Katalog erklärt Windows-Befehle, Parameter und Risiko: lesend,
Netzwerk oder ändernd. Ein Eintrag kopiert den Befehl in die Eingabe.
Platzhalter vor dem Start ersetzen.

**Enter oder Start führt direkt aus, ohne zusätzlichen Bestätigungsdialog.**
Im Einsteigermodus ist der freie Start gesperrt und der Katalog auf lesende
Befehle gefiltert. Expertenmodus ist die gespeicherte Standardvorgabe;
geführte Aktionen behalten in beiden Modi ihre Freigaben.

Jeder Start verwendet eine neue nichtinteraktive Windows-PowerShell-5.1-Sitzung.
`cd`, Variablen und Imports gelten nicht beim nächsten Start. Zusammenhängende
Schritte in einen Befehl schreiben. Es gibt keine interaktive Standardeingabe
oder PTY; Installer benötigen passende Parameter.

Zeitlimit: 1–1440 Minuten, Vorgabe 120. Abbruch beendet den eigenen
Prozessbaum, macht Änderungen aber nicht rückgängig und stoppt nicht
zwingend an Windows-Dienste übergebene Arbeit.

Die CRT-Konsole zeigt Befehl, Rechte, PID, Laufzeit, letzte Ausgabe,
STDOUT, STDERR und Exitcode. Wagenrücklauf-Fortschritte aktualisieren eine
Zeile. `sfc` nutzt den UTF-16-Ausgabehelper; direkte `sfc.exe`-Pfadaufrufe
umgehen ihn. Werkzeuge können Ausgabe puffern.
Maximal zwei Millionen Anzeigezeichen; Limits werden gemeldet.

Bis zu 100 freie Befehle bleiben flüchtig im Verlauf. Eingabe, Verlauf und
freie Ausgabe werden beim Modulwechsel gelöscht und nicht von AETHER
persistiert oder exportiert. Windows-Audit und aufgerufene Programme können
dennoch selbst protokollieren, schreiben oder senden.

## Berichte und Protokolle

Unter `%LOCALAPPDATA%\AetherCockpit`:

- `Actions`: bis zu 30 Tagesdateien mit je 16 MiB für Aktionstitel und
  Dateilöschversuche, Erfolge und Fehler. Ein volles/unlesbares Journal
  verhindert weitere journalpflichtige Aktionen.
- `Operations\Reports`: eindeutige Laufordner mit `Plan.json` oder
  `Targets.json`, `Live.log`, `Ergebnis.json` und HTML-Bericht.
  Planberichte haben keine automatische Löschfrist.
- **Aufträge:** höchstens 200 Einträge im RAM der Systemkontrollfenstersitzung;
  kein globaler Scheduler. Freie Befehle und sensible Anzeigen sind ausgenommen.

Operations zeigt bis zu einer Million Live-Ausgabezeichen; die Berichtsausgabe
läuft darüber hinaus weiter. Schreibfehler stoppen Folgewartung, ohne
die Ausgabe eines laufenden Prozesses zu blockieren.
Berichte enthalten möglicherweise Geräte, Benutzerpfade und Ereignisse.
[Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md).
