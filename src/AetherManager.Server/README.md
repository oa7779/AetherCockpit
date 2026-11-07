# AETHER Manager server prototype

The source release for Beta 31 includes this .NET 10 Minimal API and the
cockpit's opt-in reporting client. The server is a separate project; it is not
installed with the cockpit and must be started separately. It remains a
local-first prototype: it listens on loopback by default, and LAN deployment
is not configured or recommended without trusted HTTPS, restrictive
data-directory ACLs, and an allowed-host setting.

## Run locally

From the repository root:

```powershell
$env:AETHER_MANAGER_URLS = "http://127.0.0.1:5187"
dotnet run --project .\src\AetherManager.Server\AetherManager.Server.csproj
```

By default, local development data is stored in
`%LOCALAPPDATA%\AetherCockpit\Manager`. Set `AETHER_MANAGER_DATA` to use a
different directory. The first run creates `admin-token.txt` and
`enrollment-code.txt` in that directory. Treat both as secrets. The initial
enrollment code is single-use and expires after 24 hours. Admin-created codes
are returned once by the API.

OpenAPI is served at `http://127.0.0.1:5187/openapi/v1.json`; ready-to-use
requests are in [AetherManager.Server.http](./AetherManager.Server.http).

## Enrollment and report flow

1. A cockpit submits its machine name, version, and one-time code to
   `POST /api/v1/enroll`.
2. The response includes a per-device bearer token; store it securely on the
   client. The new device is `pending` and cannot upload reports.
3. An administrator lists devices, verifies the request out-of-band, and
   approves the device using `POST /api/v1/admin/devices/{id}/approve`.
4. After the user separately enables central reporting, the cockpit checks
   approval at startup and every five minutes while pending. Once approved,
   it submits the first JSON diagnostic report and then sends one hourly while
   the application is running.
5. Administrators list and read reports with the authenticated
   `/api/v1/admin/reports` endpoints.
   Report listing is paginated with `limit` (1-500) and `offset`; the response
   includes `totalCount` and `nextOffset`.

The administrator bearer token is stored in `admin-token.txt`. Reports,
devices, and enrollment-code hashes are stored as JSON under the data
directory. Expired reports are removed at startup and every 24 hours, after
90 days from receipt. Requests are limited to 10 MiB; enrollment and report
submission endpoints are rate-limited. The client stores its bearer token
encrypted with Windows DPAPI for the current Windows user.

The cockpit settings are under **System Control -> Einstellungen ->
Zentrale Verwaltung**. Choose **Server-Client**, enter the server URL and
one-time code, then enroll the device and wait for administrator approval.
**Einzelplatz** is the default and makes no server requests. Switching back
to Einzelplatz stops polling and disables report submission; the enrollment
token remains stored, but is not used. Reports remain off until the separate
checkbox is explicitly enabled and confirmed. The report includes
events from the preceding 24 hours, device/driver data, network adapters,
installed applications, and findings. Unchecking the option stops subsequent
reporting.

## HTTPS and service deployment

The default listener is `http://127.0.0.1:5187`. The process rejects any
non-loopback HTTP listener. Do not expose the prototype to a LAN until a
trusted HTTPS certificate, service-account access to its private key, and
restrictive ACLs on the server data directory have been configured.

For a TLS listener, configure the Kestrel default certificate and host filter
in the service environment and bind an HTTPS URL, for example:

```powershell
$env:ASPNETCORE_Kestrel__Certificates__Default__Path = "C:\Aether\certs\manager.pfx"
$env:ASPNETCORE_Kestrel__Certificates__Default__Password = "<provide securely>"
$env:ASPNETCORE_ALLOWEDHOSTS = "manager.example"
$env:AETHER_MANAGER_URLS = "https://manager.example:5187"
```

Do not put the certificate password in source control or a shared shell
history. `UseWindowsService` support is wired, but service registration,
certificate installation, firewall rules, and LAN deployment are not
implemented. No service is installed by this project.
