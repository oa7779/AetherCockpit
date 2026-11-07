# AETHER Management Server

Separate .NET-10-API für Geräteanmeldung, manuelle Freigabe und
Diagnoseberichte. Das Cockpit-Setup installiert diesen Server nicht.
Verwaltungsoberfläche und automatisch eingerichteter LAN-/Dienstbetrieb
sind noch nicht umgesetzt.

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

Die API-Beschreibung ist unter `/openapi/v1.json` erreichbar.
[HTTP-Beispiele](AetherManager.Server.http) enthalten alle Endpunkte.

Die vollständige Anleitung für Clientmodus, Token, Berichte,
Aufbewahrung und HTTPS steht unter
[Zentrale Verwaltung](../../docs/CENTRAL-MANAGEMENT.md).
HTTP ist nur für Loopback vorgesehen. LAN-Betrieb setzt vertrauenswürdiges
HTTPS, Hostfilter und geschützte Datenverzeichnisrechte voraus.
