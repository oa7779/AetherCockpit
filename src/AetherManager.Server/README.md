# AETHER Management Server

Separate .NET-10-API mit HTTPS-Webverwaltung für Geräteanmeldung,
manuelle Freigabe, Diagnoseberichte, Heartbeats, feste Verwaltungsaufträge
und Audit. Das Cockpit-Setup installiert diesen Server nicht.
Das Serverpaket enthält einen gesondert zu bestätigenden Dienstassistenten.
Für die lokale Beta 39 gibt es zusätzlich einen separaten grafischen
Server-Installer. Er kopiert die Dateien nach Program Files; Dienstinstallation
und Start erfolgen erst im optionalen, ausdrücklich bestätigten Assistenten.
Zertifikat, DNS und Firewall werden nicht automatisch eingerichtet.
Updates erfordern vorheriges Entfernen des Dienstes; die Deinstallation
behält die geschützten ProgramData-Daten.
[Setup und Dienstlebenszyklus](../../docs/CENTRAL-MANAGEMENT.md#separater-server-installer-lokale-beta-39).

## Lokaler Start

Vom Workspace-Stamm:

```powershell
$env:AETHER_MANAGER_URLS = 'http://127.0.0.1:5187'
dotnet run --project .\src\AetherManager.Server\AetherManager.Server.csproj
```

Standarddatenordner: `%LOCALAPPDATA%\AetherCockpit\Manager`.
Der erste Start erzeugt `admin-token.txt` und `enrollment-code.txt`.
Beide Dateien enthalten Zugangsdaten und müssen geschützt bleiben.
Einmalcodes sind 24 Stunden gültig; Geräte benötigen vor dem
Berichtsversand manuelle Freigabe und separates Client-Opt-in.

Die Webverwaltung liegt unter `/`; Administrator-Token nur im Passwortfeld eingeben.
Die API-Beschreibung ist unter `/openapi/v1.json` erreichbar.
[HTTP-Beispiele](AetherManager.Server.http) enthalten alle Endpunkte.

Die vollständige Anleitung für Clientmodus, Token, Berichte,
Aufbewahrung und HTTPS steht unter
[Zentrale Verwaltung](../../docs/CENTRAL-MANAGEMENT.md).
HTTP ist nur für Loopback vorgesehen. LAN-Betrieb setzt vertrauenswürdiges
HTTPS, Hostfilter und geschützte Datenverzeichnisrechte voraus.

Optional kann die Webverwaltung Microsoft Graph für Intune-Geräte und
Windows-Autopatch anbinden. Einrichtung, Zertifikatsspeicher,
Anwendungsberechtigungen und Beta-API-Grenzen stehen in der
[Anleitung zur Zentralverwaltung](../../docs/CENTRAL-MANAGEMENT.md#optionale-microsoft-graph-anbindung).
