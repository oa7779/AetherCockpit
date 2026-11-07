# Zentrale Verwaltung

## Aktueller Stand

`src\AetherManager.Server` ist eine separate ASP.NET-Core-.NET-10-API.
Sie nimmt Geräteanmeldungen und Diagnoseberichte entgegen, erlaubt
manuelle Freigaben und stellt Geräte-/Berichtslisten bereit.
Der Cockpit-Installer installiert den Server nicht.

Der Standardlistener ist `http://127.0.0.1:5187`.
Eine Verwaltungsoberfläche, automatische Netzwerkerkennung,
Remote-Reparaturpläne, Dienstregistrierung, Zertifikatsinstallation und
Firewallkonfiguration sind noch nicht umgesetzt. `UseWindowsService` ist
vorbereitet, richtet aber keinen Windows-Dienst ein.

## Einzelplatz und Client

Unter **System Control → Einstellungen → Zentrale Verwaltung**:

1. **Server-Client** wählen und die Server-URL speichern.
2. Einen gültigen Einmalcode eingeben und das Gerät anmelden.
3. Geräteidentität bei der Serververwaltung prüfen und manuell freigeben lassen.
4. Die getrennte Berichtsübertragung aktivieren und bestätigen.

Eine Anmeldung sendet Rechnername und die vom Client ermittelte
Assembly-Version. Der Server antwortet mit Geräte-ID und Gerätetoken;
das Gerät beginnt im Zustand `pending`. Das Token liegt lokal unter
`Manager\device-token.bin`, verschlüsselt per Windows-DPAPI für `CurrentUser`.

Solange Übertragung nicht aktiviert ist, erfolgen keine regelmäßigen
Statusabfragen. Mit aktivierter Übertragung wird beim Start beziehungsweise
bei Konfigurationsänderung geprüft; ausstehende Freigaben und Fehler werden
im Fünf-Minuten-Abstand erneut abgefragt. Nach Freigabe sendet der Client
den ersten Bericht und anschließend mit einer Stunde Abstand nach erfolgreichem
Versand, solange AETHER läuft. Anfragezeitlimit: zwei Minuten.

**Einzelplatz** ist Standard. Zurückschalten stoppt Abfragen und deaktiviert
Übertragung. Das gespeicherte Token bleibt erhalten, wird in diesem Modus
aber nicht verwendet. Ausschalten des Übertragungs-Opt-ins stoppt Folgeberichte;
bereits empfangene Serverberichte werden dadurch nicht gelöscht.

Die lokale 30-Minuten-Hintergrunddiagnose ist unabhängig von diesem
stündlichen Reporting.

## Berichtsinhalte

Ein Clientbericht enthält Rechnername, Windows-Version/Build,
System-/Application-Ereignisse der letzten 24 Stunden (bis zu 100 je Kanal),
vorhandene PnP-Geräte, Netzwerkadapter, Registry-Appinventar, PnP-Treiber und
abgeleitete Befunde. Das Inventar ist keine Prüfung jeder Anwendung.

Die Text-Export-Schwärzung wird hier nicht angewendet.
Paketmitschnitte, freie Konsolenverläufe und sensible Schlüsselanzeigen
werden durch diesen Collector nicht übertragen.

## Server lokal starten

Vom Workspace-Stamm:

```powershell
$env:AETHER_MANAGER_URLS = 'http://127.0.0.1:5187'
dotnet run --project .\src\AetherManager.Server\AetherManager.Server.csproj
```

Die API-Beschreibung liegt unter `/openapi/v1.json`.
[HTTP-Beispiele](../src/AetherManager.Server/AetherManager.Server.http)
zeigen sämtliche Endpunkte und den Freigabeablauf. Sie gehören zum
Quellprojekt; die Beispieldaten sind keine tatsächlichen Messberichte.

## Token und Datenspeicher

Standard bei lokalem Start:
`%LOCALAPPDATA%\AetherCockpit\Manager`.
Bei Betrieb als Windows-Dienst wäre der Standard
`%PROGRAMDATA%\AetherCockpit\Manager`.
`AETHER_MANAGER_DATA` überschreibt das Datenverzeichnis.

Der erste Start erzeugt `admin-token.txt` und `enrollment-code.txt`.
Beides sind Zugangsdaten und darf nicht ins Repository, in geteilte
Protokolle oder Screenshots gelangen. API-erzeugte Codes werden nur einmal
zurückgegeben. Codes sind einmal verwendbar und 24 Stunden gültig.

Geräte, Code-Hashes und Berichtsindex liegen als JSON im Datenordner;
Berichtspayloads unter `reports`. Berichte werden 90 Tage
nach Empfang beim Start und anschließend alle 24 Stunden bereinigt.
Die Frist betrifft Berichte, nicht automatisch Geräte oder Zugangsdaten.

Administratorzugriff verwendet das Admintoken; Gerätezugriff das jeweilige
Gerätetoken als Bearer-Token. Neue Geräte können bis zur Freigabe keine
Berichte abgeben. Anfragekörper sind auf 10 MiB begrenzt.
Anmeldung ist auf fünf, Berichtseingang auf 120 Anfragen je IP und Minute
begrenzt. Berichtslisten verwenden `limit` (1–500) und `offset`, mit
`totalCount` und `nextOffset`.

## API-Überblick

| Endpunkt | Aufgabe |
|---|---|
| `GET /health` | Erreichbarkeit ohne Geräte-/Berichtsdaten |
| `POST /api/v1/enroll` | Anmeldung mit Einmalcode |
| `GET /api/v1/device` | Eigener Freigabestatus |
| `POST /api/v1/reports` | Bericht eines freigegebenen Geräts |
| `POST /api/v1/admin/enrollment-codes` | Neuer Einmalcode |
| `GET /api/v1/admin/devices` | Geräteliste |
| `GET /api/v1/admin/devices/{id}` | Ein Gerät |
| `POST /api/v1/admin/devices/{id}/approve` | Manuelle Freigabe |
| `GET /api/v1/admin/reports` | Paginierte Berichtsliste |
| `GET /api/v1/admin/reports/{id}` | Bericht mit Payload |

## HTTPS und LAN-Betrieb

HTTP ist nur für Loopback vorgesehen; nicht lokale HTTP-Listener und
Clientziele werden abgewiesen. Für Netzwerkbetrieb sind vertrauenswürdiges
HTTPS, eingeschränkte Datenverzeichnis-ACLs, Zugriff des Dienstkontos auf den
privaten Zertifikatsschlüssel und ein Hostfilter erforderlich.

Relevante Umgebungsvariablen:

- `AETHER_MANAGER_URLS`: Listener, mehrere URLs mit Semikolon.
- `ASPNETCORE_Kestrel__Certificates__Default__Path`: vorhandene PFX-Datei.
- `ASPNETCORE_Kestrel__Certificates__Default__Password`: sicher bereitgestelltes Passwort.
- `ASPNETCORE_ALLOWEDHOSTS`: erlaubte Hostnamen.
- `AETHER_MANAGER_DATA`: geschützter Datenordner.

Keine Zugangsdaten in gemeinsame Shell-Historie schreiben.
Diese Konfiguration allein installiert weder Dienst noch Zertifikat oder
Firewallregel. Der lokale API-Ablauf wurde in der bisherigen Entwicklung
geprüft; der Cockpit-Anmelde-/Reporting-Ablauf ist noch nicht vollständig
manuell auf einem Testgerät durchgespielt.

## Bestehende WinRM-Gerätediagnose

Operations kann unabhängig vom Server bis zu 500 eingetragene Windows-Ziele
seriell und lesend abfragen. Hostname oder IPv4 selbst angeben;
WinRM und Berechtigungen müssen vorhanden sein.

HTTPS/5986 mit Negotiate und normaler Zertifikatsprüfung ist bevorzugt;
HTTP/5985 wird gesondert gewählt. AETHER richtet keine
TrustedHosts-Wildcards, Basic-Authentifizierung, Firewallausnahmen oder
Zertifikatsumgehung ein. Das Ergebnis enthält Inventar, Top-Prozesse und
begrenzte Dienste-/Ereignissnapshots im lokalen Laufbericht.

M365-/Copilot-Bereitschaftsanzeigen lesen nur lokale Windows-, Office-,
Modul- und WinRM-Voraussetzungen. Sie melden keine Tenant-Anmeldung
und bestätigen keine Cloud-Lizenz.

[Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md)
