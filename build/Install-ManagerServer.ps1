param(
    [Parameter(Mandatory=$true)][string]$HostName,
    [Parameter(Mandatory=$true)][string]$CertificateThumbprint,
    [ValidateRange(1,65535)][int]$Port = 5187,
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'
$name = 'AetherManagerServer'
$exe = Join-Path $PSScriptRoot 'AetherManager.Server.exe'
$data = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'AetherCockpit\Manager'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administratorrechte erforderlich.' }
if ($HostName -notmatch '^[a-zA-Z0-9][a-zA-Z0-9.-]{0,252}$') { throw 'Konkreten DNS-Hostnamen verwenden.' }
$existing = Get-CimInstance Win32_Service -Filter "Name='$name'"
if ($existing -and $existing.PathName -ne ('"' + $exe + '"')) { throw 'Serverdienst gehoert zu einer anderen Installation.' }
if ($Remove) {
    if ($existing) {
        $service = Get-Service -Name $name
        if ($service.Status -ne 'Stopped') { Stop-Service -InputObject $service; $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }
        & "$env:WINDIR\System32\sc.exe" delete $name
        if ($LASTEXITCODE -ne 0) { throw 'Serverdienst konnte nicht entfernt werden.' }
    }
    Write-Output 'Serverdienst entfernt; Berichte, Audit und Zugangsdaten bleiben erhalten.'
    exit 0
}
if ($existing) { throw 'Serverdienst bereits eingerichtet.' }
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Serverpaket vollstaendig entpacken.' }
if (!$exe.StartsWith($env:ProgramFiles.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Serverpaket vor der Dienstinstallation nach Program Files kopieren und dort vor Schreibzugriffen normaler Benutzer schuetzen.'
}
$current = $exe
while ($current) {
    if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Dienstpfad darf keine Verknuepfung enthalten: $current" }
    $current = Split-Path $current -Parent
}
$thumb = $CertificateThumbprint.Replace(' ', '')
. (Join-Path $PSScriptRoot 'Assert-ServiceFilesProtected.ps1')
Assert-ServiceFilesProtected $exe
if ($thumb -notmatch '^[0-9a-fA-F]{40}$') { throw 'Ungueltiger SHA1-Zertifikatfingerabdruck.' }
$cert = Get-Item -LiteralPath "Cert:\LocalMachine\My\$thumb"
if (!$cert.HasPrivateKey -or $cert.NotAfter -le (Get-Date) -or $cert.NotBefore -gt (Get-Date)) { throw 'Gueltiges Serverzertifikat mit privatem Schluessel erforderlich.' }
Write-Host "HTTPS-Verwaltung auf https://${HostName}:$Port / Dienstkonto LocalSystem."
Write-Host 'DNS/SAN, Vertrauenskette, Schluesselrechte und Zugang ueber VPN/Firewall separat einrichten.'
if ((Read-Host 'Zur Dienstinstallation JA eingeben') -cne 'JA') { throw 'Installation nicht freigegeben.' }
$current = $data
while ($current) {
    if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Datenpfad darf keine Verknuepfung enthalten: $current" }
    $current = Split-Path $current -Parent
}
New-Item -Path $data -ItemType Directory -Force | Out-Null
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
$acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($sid)), 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
}
Set-Acl -LiteralPath $data -AclObject $acl
Assert-ManagedDataProtected $data
& "$env:WINDIR\System32\sc.exe" create $name binPath= ('"' + $exe + '"') start= delayed-auto DisplayName= 'AETHER Management Server'
if ($LASTEXITCODE -ne 0) { throw 'Serverdienst konnte nicht angelegt werden.' }
New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$name" -Name Environment -PropertyType MultiString -Value @(
    "AETHER_MANAGER_URLS=https://0.0.0.0:$Port", "ASPNETCORE_ALLOWEDHOSTS=$HostName",
    "AETHER_MANAGER_DATA=$data", "AETHER_MANAGER_CERT_THUMBPRINT=$thumb") | Out-Null
Start-Service -Name $name
Write-Output "Serverdienst gestartet. Administrator-Token liegt geschuetzt unter $data\admin-token.txt. HTTPS-Erreichbarkeit pruefen."
