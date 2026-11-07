# Zentrale Verwaltung

## Server und Geräte

Der separate AETHER Management Server bietet eine Weboberfläche für
Gerätefreigabe, Diagnoseberichte, Onlinezustand, Aufträge und Auditverlauf.
Client und Server verwenden einen festen Aktionskatalog, keine freie Fernkonsole.
Die Geräte initiieren ausgehende Verbindungen; eingehendes WinRM ist dafür
nicht erforderlich.

Netzwerkbetrieb verwendet HTTPS mit normaler Zertifikatsprüfung. Für Internet-
oder VPN-Zugriff DNS, vertrauenswürdiges Serverzertifikat und Zugriffsschutz
einrichten. AETHER erstellt weder Zertifikate noch Firewallfreigaben.
Für öffentliche Erreichbarkeit empfiehlt sich ein vorgeschalteter VPN-Zugang.
Die Webverwaltung verwendet ein Administrator-Token; eigene Benutzerrollen
oder Mehrfaktoranmeldung sind nicht enthalten.

## Desktopclient

Unter **System Control → Einstellungen → Zentrale Verwaltung**:

1. **Server-Client** wählen und die Server-URL speichern.
2. Einen gültigen Einmalcode eingeben und das Gerät anmelden.
3. Geräteidentität im Server prüfen und manuell freigeben.
4. Die gesonderte Berichtsübertragung bestätigen und aktivieren.

Die Anmeldung sendet Rechnername und Assembly-Version. Der Clienttoken liegt
unter `%LOCALAPPDATA%\AetherCockpit\Manager\device-token.bin`, DPAPI-geschützt
für `CurrentUser`. Berichte werden nach Freigabe beim Start und danach mit
einer Stunde Abstand nach erfolgreichem Versand übertragen, solange AETHER läuft.
Ausstehende Freigaben und Fehler werden nach fünf Minuten erneut geprüft.
Anfragezeitlimit: zwei Minuten.

**Einzelplatz** bleibt Standard und nimmt keinen Managerkontakt auf.
Zurückschalten deaktiviert Desktopübertragung; gespeicherte Zugangsdaten bleiben
erhalten. Die lokale Hintergrunddiagnose ist davon unabhängig.

Der Desktopclient allein überträgt Berichte. Für regelmäßige Heartbeats und
Remote-Ausführung den optionalen Clientdienst verwenden. Desktop und Dienst
haben getrennte Identitäten und Freigaben; nicht beide für denselben Rechner
anmelden, wenn kein doppelter Geräteeintrag gewünscht ist.

## Optionaler Clientdienst

Im Setup **Zentralverwaltungsdienst bereitstellen** wählen. Das kopiert die
Dateien, installiert oder startet aber noch keinen Dienst.
Danach die Startmenü-Verknüpfung **AETHER Zentralverwaltung – Clientdienst
einrichten** als Administrator verwenden. Der Konsolenassistent fragt
Server-URL, Einmalcode und die Zustimmung zu Betrieb und Datenübertragung ab.

Ohne Zusatzparameter sind Systemänderungen und automatische Reparaturen aus.
Für diese Freigaben den Assistenten bewusst mit den gewünschten Schaltern starten:

```powershell
& 'C:\Program Files\AETHER System Control\Configure-ManagedClient.ps1' -AllowChanges -AllowAutoRepair
```

Der Dienst `AetherManagedClient` läuft als **LocalSystem**, auch bei geschlossenem
Cockpit. Deshalb sind die ProgramData-Konfiguration und Programmdateien vor
Schreibzugriffen normaler Benutzer zu schützen. Der Assistent erlaubt den Dienst
nur aus einer Installation unter Program Files, lehnt Pfadverknüpfungen ab
und setzt Datenordnerrechte für SYSTEM und Administratoren.
Schreibrechte und Eigentümer von Dienstdateien und ihren Verzeichnissen
werden geprüft. Ungeeignete ACLs werden abgewiesen, nicht still korrigiert.

Konfiguration und ausstehende Ergebnisse:
`%PROGRAMDATA%\AetherCockpit\ManagedClient`.
Der Gerätetoken ist zusätzlich mit DPAPI `LocalMachine` geschützt.
Ein Dienstneustart verwendet dieselbe Identität. Zur Änderung lokaler Freigaben
den Dienst entfernen und den Assistenten erneut durchlaufen; eine neue Anmeldung
benötigt einen neuen Einmalcode. Den alten Eintrag im Server sperren.

```powershell
& 'C:\Program Files\AETHER System Control\Configure-ManagedClient.ps1' -Remove
```

Entfernung des Dienstes beziehungsweise Cockpit-Deinstallation behält die
geschützten ProgramData-Daten. Serverzugriff separat sperren. Bei Updates stoppt
das Setup nur den zu dieser Installation gehörenden Dienst und startet ihn wieder,
wenn er zuvor lief. Portable Nutzung installiert keinen Dienst.

## Überwachung und Reparaturfreigaben

Der Dienst sendet einen Heartbeat vor jedem Zyklus und wartet danach 30 Sekunden.
Lange Diagnoseläufe verlängern diesen Abstand; ein fehlender aktueller Heartbeat
ist daher kein Beweis für einen abgeschalteten Rechner.
Inventarberichte folgen mit 30 Minuten Abstand nach erfolgreicher Übertragung.
Sie enthalten Windows-/Builddaten, Geräte, Adapter, Treiberversionen, Anwendungen,
Ereignisse und weitere vom Collector gemeldete Statusdaten.
HKCU-Abfragen beziehen sich im Dienst auf SYSTEM, nicht auf alle Benutzerprofile.

Lesende Katalogaufträge benötigen ein freigegebenes Gerät. Systemänderungen
brauchen zusätzlich **lokales Änderungs-Opt-in**, **Server-Gerätefreigabe für
Änderungen** und die Bestätigung des konkreten Auftrags im Web.

Automatische Reparatur ist auf **DNS-Cache leeren** begrenzt:
beide Automatikfreigaben müssen aktiv sein. Der DNS-Assistent muss einen
Fehler des Standardresolvers bei erfolgreicher direkter Abfrage eines
konfigurierten DNS-Servers erkennen. Vor Ausführung wird der Befund erneut
geprüft; der Server erlaubt höchstens einen automatischen Auftrag pro Gerät
in 24 Stunden. Disconnected-Adapter oder alte Treiberdaten lösen keine
Reparatur aus. DNS-Prüfungen kontaktieren `www.microsoft.com` und konfigurierte
DNS-Server. Das Leeren des Caches ist keine garantierte Ursachenbehebung.

Updates, Treiberinstallation, Neustarts und destruktive Aktionen sind keine
automatischen Heilungsschritte. Im Web zuerst einen Updatesuchauftrag ausführen,
die vollständige Ausgabe öffnen, ein Angebot auswählen und dessen Lizenz lesen.
Die Installation benötigt eine bestätigte Einzelwahl aus einer erfolgreichen,
höchstens eine Stunde alten Suche desselben Geräts. GUID, Revision und
SHA256 des Lizenztexts werden vor Ausführung erneut mit der Updatequelle
verglichen. Treiberangebote aus Windows Update sind eingeschlossen.
Abweichende Angebote oder Lizenzen werden abgewiesen; kein automatischer Neustart.
Die lokalen Update-/WinGet-Pläne bleiben unverändert.
[Werkzeuge und Assistenten](SYSTEM-CONTROL.md).

## Aufträge und Audit

Aufträge bestehen aus einer festen Aktions-ID, niemals einem übermittelten
PowerShell-Skript. Verfügbarkeit wird auf dem Zielgerät geprüft.
Ausgaben sind auf 65.536 Zeichen begrenzt; Abschneiden wird gemeldet.
Ein Aktionslauf hat höchstens 30 Minuten Laufzeit.

Der Server gibt einen Auftrag nur einmal aus. Der Client speichert die
Übernahme vor Ausführung und puffert Ergebnisse bis zur Bestätigung.
Nach einem Absturz wird ein unklarer Lauf als unbekannt/fehlgeschlagen gemeldet,
nicht erneut ausgeführt. Ein nach der Übernahme dauerhaft verlorener Client
kann einen Auftrag im Zustand `running` hinterlassen: Gerät prüfen, bevor
ein neuer Änderungsauftrag erstellt wird.

Nur noch nicht übernommene Aufträge lassen sich stornieren. Gerätesperre stoppt
weitere Abfragen und storniert wartende Aufträge; bereits gestartete
Windows-Prozesse werden nicht rückgängig gemacht. Gesperrte Geräte erfordern
eine neue Anmeldung. Freigaben, Richtlinien und Auftragsübergänge werden
im Audit dokumentiert.

Der persistente Bestand erlaubt 10.000 Aufträge insgesamt, 100 wartende pro
Gerät und 100.000 Auditeinträge. Er wird nicht still gelöscht; bei Erreichen
der Grenze erfolgt ein Fehler. Vor einer Offline-Archivierung den Server
stoppen und den vollständigen Datenordner sichern.
Web-/API-Listen zeigen die neuesten 500 Aufträge beziehungsweise Auditeinträge.

## Serverpaket und HTTPS-Dienst

Das separate `AetherManager-0.7.0-beta.34-win-x64.zip` enthält den Server
mit Runtime, Weboberfläche und Installationsskript. Nach
`C:\Program Files\AETHER Manager` entpacken und ein gültiges Serverzertifikat
mit privatem Schlüssel in `LocalMachine\My` bereitstellen. DNS/SAN,
Vertrauenskette und Schlüsselzugriff für das Dienstkonto prüfen.
Dateien und Verzeichnisse dürfen nur von SYSTEM, Administratoren oder
TrustedInstaller geändert werden und müssen einem dieser Konten gehören.
Der Installationsassistent prüft dies vor der Dienstregistrierung.

```powershell
& 'C:\Program Files\AETHER Manager\Install-ManagerServer.ps1' -HostName aether.example.net -CertificateThumbprint $thumbprint
```

Der Assistent fragt eine Installationsbestätigung ab und richtet
`AetherManagerServer` als LocalSystem-Dienst mit HTTPS auf Port 5187 ein;
`-Port` wählt einen anderen Port. Keine automatische Firewalländerung.
Die Weboberfläche ist an der Server-URL erreichbar. Das Administrator-Token
liegt geschützt in `admin-token.txt` im Datenordner. Es bleibt beim
Weblogin nur im Seitenspeicher, nicht in Browser-Storage oder URL.

## Entwicklung, Zugangsdaten und API

Loopbackstart vom Workspace-Stamm:

```powershell
$env:AETHER_MANAGER_URLS = 'http://127.0.0.1:5187'
dotnet run --project .\src\AetherManager.Server\AetherManager.Server.csproj
```

Standarddaten: `%LOCALAPPDATA%\AetherCockpit\Manager`, als Dienst
`%PROGRAMDATA%\AetherCockpit\Manager`; `AETHER_MANAGER_DATA` überschreibt den Ort.
Der erste Start erzeugt Administrator-Token und einen Einmalcode.
Codes sind einmal verwendbar und 24 Stunden gültig.
Geräte, Code-Hashes, Berichtsindex und Verwaltungs-/Auditstatus liegen als JSON
im Datenordner, Berichtspayloads unter `reports`.
Berichte werden nach 90 Tagen beim Start und anschließend alle 24 Stunden bereinigt.
Zugangsdaten, Aufträge und Auditeinträge haben keine automatische Löschfrist.

HTTP ist nur für Loopback erlaubt. Netzwerklistener brauchen konkrete
Hostnamen in `ASPNETCORE_ALLOWEDHOSTS`; Wildcards werden abgewiesen.
`AETHER_MANAGER_CERT_THUMBPRINT` wählt ein gültiges Zertifikat aus
`LocalMachine\My`. Alternativ sind die normalen Kestrel-Zertifikatparameter
für eine vorhandene PFX-Datei möglich; Passwörter nicht in geteilte
Shell-Historie oder Repository schreiben.

Anfragekörper: höchstens 10 MiB. Anmeldung: fünf Anfragen pro IP und Minute.
Berichte und Verwaltungsendpunkte: jeweils 120 Anfragen pro IP und Minute.
Berichtslisten erlauben `limit` 1–500 und `offset`.
API-Beschreibung: `/openapi/v1.json`.
[HTTP-Beispiele](../src/AetherManager.Server/AetherManager.Server.http)
decken Geräteanmeldung, Freigabe, Sperre, Berichte, Richtlinien, Heartbeats,
Katalog, Aufträge, Ergebnisse, Stornierung und Audit ab.

Der bestehende lesende WinRM-Geräteabgleich bleibt ein unabhängiger lokaler
Operations-Ablauf. Keine TrustedHosts-Wildcards oder Zertifikatsumgehung.

[Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md)
