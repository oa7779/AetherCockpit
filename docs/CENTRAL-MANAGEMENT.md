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
Der aktuelle Entwicklungsstand verwendet lokale Konten mit Administrator-,
Operator- und Leserrolle; Mehrfaktoranmeldung ist nicht enthalten. Der
Administrator-Token wird nur für den einmaligen Bootstrap und ausdrücklich
freigegebene externe Verwaltungswerkzeuge verwendet.

### Weiterentwicklung zur Infrastrukturverwaltung

Der Entwicklungsbranch `feature/professional-server` ergänzt lokale
AETHER-Konten mit Administrator-, Operator- und Leserrolle sowie eine
animierte Flottenansicht. Dieser Stand wird separat geprüft und ist noch
nicht Bestandteil der veröffentlichten Beta 38.

| Rolle | Geplanter und implementierter Prüfstand |
|---|---|
| Leser | Server-/Geräteübersicht, Berichte, Aufträge und Audit lesen |
| Operator | Zusätzlich feste lesende Diagnoseaufträge erstellen |
| Administrator | Zusätzlich Gerätefreigaben, Änderungsrichtlinien, bestätigte Änderungsaufträge und Benutzerverwaltung |

Neon bleibt Standard; Windows Modern ist als neutrale Weboberfläche
vorbereitet. Die Flottenansicht zeigt bis zu 48 Geräte pro Seite mit Suche,
Filter und Auswahl. Ihre Verbindungen sind illustrativ, keine erfasste
physische Netzwerktopologie. Fehlende aktuelle Heartbeats werden als solche
angezeigt, nicht als Beweis für einen abgeschalteten Rechner.

Die neue Serverüberwachung erfasst lesend Host-/Prozess-CPU, Speicher,
Datenträgerkapazität, Prozessanzahl und Uptime. Die Abtastung erfolgt alle
fünf Sekunden. Nicht verfügbare Temperaturen werden nicht aus Grenzwerten
oder Zufallswerten ersetzt. Ein externer Sensorprovider ist dafür nicht
eingebaut.

Die Benutzerverwaltung verwendet einmaliges Bootstrap mit dem bestehenden
privilegierten Token, kennwortgeschützte Konten und zeitlich begrenzte
Browsersitzungen. Die CI-Rollen-, HTTPS- und Browserprüfung mit 48 synthetischen Geräten
ist bestanden; produktive Installation und Migration sind damit nicht geprüft.
Keine freie Fernkonsole und keine automatische Übernahme
aller lokalen Desktopaktionen.

Der alte Administrator-Token dient dem einmaligen Bootstrap außerhalb des
Browsers. Nach Anlegen des ersten Administrators ist der alte Admin-API-
Zugang standardmäßig aus. `AETHER_MANAGER_LEGACY_ADMIN_API=true` erlaubt
explizit vorhandene Nicht-Browser-Administrationswerkzeuge, nicht den
Browser-Tokenlogin oder erneutes Bootstrap.
Browsersitzungen laufen nach 30 Minuten absolut ab; Konto-, Rollen- und
Kennwortänderungen widerrufen die Sitzungen des betroffenen Kontos.
Der letzte aktive Administrator kann nicht entfernt oder herabgestuft werden.

Der Entwicklungsstand bleibt eine Einzelserverlösung mit geschütztem
JSON-Datenordner. Mehrfaktoranmeldung, Hochverfügbarkeit, Selbstbedienungs-
Kontowiederherstellung und storeübergreifende Transaktionen sind nicht enthalten.

## Separater Server-Installer (lokale Beta 39)

`AetherManager-Setup-0.7.0-beta.39-win-x64.exe` ist zunächst nur lokal
verfügbar und unsigniert. Das Setup benötigt Administratorrechte und kopiert
den selbstenthaltenen Windows-x64-Server nach
`C:\Program Files\AETHER Management Server`. Eine zusätzliche .NET-Installation
ist nicht erforderlich. Der Cockpit-Installer enthält diesen Server nicht.

**Die Dateiinstallation startet noch keinen Server.** Am Ende kann der
Dienstassistent ausdrücklich ausgewählt werden; alternativ die
Startmenü-Verknüpfung **Serverdienst einrichten (als Administrator)** über
**Als Administrator ausführen** öffnen. Hostname und Zertifikatfingerabdruck
werden im PowerShell-Fenster abgefragt. Für einen abweichenden Port:

```powershell
& 'C:\Program Files\AETHER Management Server\Install-ManagerServer.ps1' `
    -HostName 'aether.example.net' -CertificateThumbprint '<SHA1-Fingerabdruck>' -Port 5187
```

Benötigt wird ein gültiges Serverzertifikat mit privatem Schlüssel im
Zertifikatspeicher **Lokaler Computer / Eigene Zertifikate**. DNS/SAN,
Vertrauenskette und Schlüsselzugriff für LocalSystem separat prüfen.
Erst die Eingabe `JA` installiert und startet den Dienst
`AetherManagerServer` als **LocalSystem** mit verzögertem automatischem Start.
Es werden keine Zertifikate erzeugt, Firewallregeln angelegt oder
Schutzrichtlinien beziehungsweise PowerShell-Ausführungsrichtlinien umgangen.
Blockiert die lokale Richtlinie das Skript, bleibt der Server unkonfiguriert;
die Freigabe muss nach den eigenen Administrationsrichtlinien erfolgen.
VPN, DNS und erlaubten HTTPS-Zugriff separat einrichten.

Geschützte Daten bleiben unter `%PROGRAMDATA%\AetherCockpit\Manager`.
Vor Updates eine Sicherung dieses Verzeichnisses erstellen. Solange ein
Serverdienst registriert ist, verweigert das Setup das Ersetzen von Dateien.
Den bisherigen Dienst aus dessen Installationsordner als Administrator entfernen:

```powershell
& 'C:\Program Files\AETHER Management Server\Install-ManagerServer.ps1' `
    -HostName localhost -CertificateThumbprint unused -Remove
```

Die beiden Platzhalterparameter werden bei der Dienstentfernung nicht für
eine neue Verbindung oder Zertifikatsprüfung verwendet. Danach das Setup
ausführen und den Dienst mit dem tatsächlichen Hostnamen, Zertifikat und
gegebenenfalls abweichendem Port wieder einrichten. Ein transparentes
In-place-Dienstupdate ist noch nicht implementiert.

Bei interaktiver Deinstallation fragt der Uninstaller vor dem Stoppen und
Entfernen eines zu dieser Installation gehörenden Dienstes ausdrücklich nach.
Fremde Dienste werden nicht entfernt; bei Fehlern bleiben die Programmdateien
erhalten. Für eine stille Deinstallation muss der Dienst vorher explizit
entfernt sein. Berichte, Konten, Audit, Zugangsdaten und Zertifikate bleiben
auch nach der Deinstallation erhalten und müssen bei einer endgültigen
Stilllegung separat gesichert, widerrufen oder gelöscht werden.

Der Installer wurde kompiliert und das Serverpaket mit synthetischen
Verwaltungs-/HTTPS-Tests geprüft. Eine tatsächliche Installation,
Dienstregistrierung oder Deinstallation ist damit noch nicht nachgewiesen.

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

Das separate `AetherManager-0.7.0-beta.38-win-x64.zip` enthält den Server
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

### Optionale Microsoft-Graph-Anbindung

Der Server kann nach ausdrücklicher Einrichtung Microsoft Graph mit
App-only-Zertifikatsauthentifizierung verwenden. Die Weboberfläche lädt
Intune-Geräte und Windows-Autopatch-Bereitstellungen erst nach Auswahl von
**Graph-Daten abrufen**. Administratoren können anschließend für ein
ausgewähltes Gerät eine Intune-Synchronisierung anfordern und bestehende
Autopatch-Bereitstellungen einzeln pausieren oder fortsetzen. Diese Aktionen
werden im lokalen Kontenaudit erfasst. Operatoren und Leser können Daten
anzeigen, aber keine Graph-Änderungen auslösen.

Für die Einrichtung im Prozessumfeld des Servers sind drei Werte nötig:

```powershell
$env:AETHER_GRAPH_TENANT_ID = '<Tenant-GUID>'
$env:AETHER_GRAPH_CLIENT_ID = '<App-Registrierungs-GUID>'
$env:AETHER_GRAPH_CERT_THUMBPRINT = '<SHA1-Fingerabdruck>'
```

Das Zertifikat mit privatem Schlüssel muss im Zertifikatspeicher
**Lokaler Computer / Eigene Zertifikate** liegen und für das Dienstkonto
verwendbar sein. `AETHER_GRAPH_CERT_THUMBPRINT` ist unabhängig von
`AETHER_MANAGER_CERT_THUMBPRINT`, das ausschließlich das HTTPS-Serverzertifikat
bestimmt. Änderungen an der Prozessumgebung erfordern einen Serverneustart.
Das öffentliche Zertifikat muss zusätzlich in der Entra-App-Registrierung
hinterlegt sein. Die Anbindung verwendet derzeit die Microsoft Public Cloud,
nicht Sovereign-Cloud-Endpunkte.
Zertifikatsdateien, private Schlüssel und Zugriffstoken werden nicht in der
Weboberfläche oder im Repository hinterlegt; MSAL hält Zugriffstoken nur im
Arbeitsspeicher des Servers.

Die App-Registrierung braucht Administratorzustimmung für die verwendeten
Anwendungsberechtigungen:

| Funktion | Microsoft-Graph-Berechtigung |
|---|---|
| Intune-Geräte lesen | `DeviceManagementManagedDevices.Read.All` |
| Intune-Synchronisierung anfordern | `DeviceManagementManagedDevices.PrivilegedOperations.All` |
| Autopatch-Bereitstellungen lesen | `WindowsUpdates.Read.All` |
| Bestehende Autopatch-Bereitstellung pausieren/fortsetzen | `WindowsUpdates.ReadWrite.All` |

Die Intune-Graph-API setzt eine aktive Intune-Lizenz voraus. Autopatch verlangt
zusätzlich die jeweiligen Microsoft-Voraussetzungen. Die Statusanzeige prüft
lokale Einstellungen und Zertifikat; die Anmeldung, Berechtigungen und
Zustimmung werden erst beim tatsächlichen Graph-Aufruf geprüft. Nicht
eingerichtete oder ungültige Verbindungen werden ausdrücklich gemeldet.

Die Autopatch-Steuerung verwendet derzeit Microsoft-Graph-**Beta**-Endpunkte.
Microsoft weist darauf hin, dass Beta-APIs Änderungen unterliegen und nicht
für Produktionsanwendungen unterstützt werden. AETHER legt keine neuen
Rollouts an: Geräteeinschreibung, Zielgruppe und Zeitplan benötigen eine
gesonderte tenantbezogene Freigabe. Es gibt keinen freien Graph-Proxy oder
eine Fernkonsole.

Microsoft-Dokumentation:
[MSAL-Client-Credentials](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/web-apps-apis/client-credential-flows),
[Intune-Geräteliste](https://learn.microsoft.com/en-us/graph/api/intune-devices-manageddevice-list?view=graph-rest-1.0),
[Intune-Synchronisierung](https://learn.microsoft.com/en-us/graph/api/intune-devices-manageddevice-syncdevice?view=graph-rest-1.0),
[Autopatch-Bereitstellungen](https://learn.microsoft.com/en-us/graph/api/adminwindowsupdates-list-deployments?view=graph-rest-beta) und
[Autopatch-Zustandsänderung](https://learn.microsoft.com/en-us/graph/api/windowsupdates-deployment-update?view=graph-rest-beta) sowie
[Autopatch-Steuerung](https://learn.microsoft.com/en-us/windows/deployment/windows-autopatch/manage/windows-autopatch-windows-feature-update-programmatic-controls).

Der bestehende lesende WinRM-Geräteabgleich bleibt ein unabhängiger lokaler
Operations-Ablauf. Keine TrustedHosts-Wildcards oder Zertifikatsumgehung.

[Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md)
