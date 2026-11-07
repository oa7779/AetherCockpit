param(
    [string]$ServerUrl,
    [switch]$AllowChanges,
    [switch]$AllowAutoRepair,
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'
$serviceName = 'AetherManagedClient'
$exe = Join-Path $PSScriptRoot 'ManagedClient\AetherClient.Service.exe'
$data = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'AetherCockpit\ManagedClient'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (!(New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Der Assistent muss als Administrator gestartet werden.'
}
function Assert-NoReparse([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Verknuepfungen sind im Dienstpfad nicht erlaubt: $current"
        }
        $current = Split-Path $current -Parent
    }
}
Assert-NoReparse $data
Assert-NoReparse $exe
$existing = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($existing -and $existing.PathName -ne ('"' + $exe + '"')) {
    throw 'Ein AETHER-Clientdienst einer anderen Installation existiert. Diese Installation wird nicht veraendert.'
}
if ($Remove) {
    if ($existing) {
        $service = Get-Service -Name $serviceName
        if ($service.Status -ne 'Stopped') { Stop-Service -InputObject $service; $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }
        & "$env:WINDIR\System32\sc.exe" delete $serviceName
        if ($LASTEXITCODE -ne 0) { throw 'Dienst konnte nicht entfernt werden.' }
    }
    Write-Output 'Clientdienst entfernt. Geschuetzte Kontodaten bleiben fuer eine bewusste erneute Anmeldung erhalten.'
    exit 0
}
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Clientdienst fehlt: $exe" }
if (!$exe.StartsWith($env:ProgramFiles.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Den erhoehten Clientdienst nur aus einer geschuetzten Installation unter Program Files einrichten.'
}
if ($existing) { throw 'Dienst bereits eingerichtet. Erst entfernen, bevor Server oder lokale Freigaben neu eingerichtet werden.' }
. (Join-Path $PSScriptRoot 'Assert-ServiceFilesProtected.ps1')
Assert-ServiceFilesProtected $exe
if (Test-Path -LiteralPath (Join-Path $data 'pending-result.json')) {
    throw 'Ein ausstehender Auftrag muss vor einer erneuten Anmeldung geklaert werden; kein Auftrag wird still ueberschrieben.'
}
if (Test-Path -LiteralPath $data) {
    foreach ($item in Get-ChildItem -LiteralPath $data -Force) {
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Unerwartetes Unterverzeichnis oder Link im Clientdatenordner.'
        }
    }
}
if ($AllowAutoRepair -and !$AllowChanges) { throw 'Automatische Reparatur braucht auch die Freigabe fuer Systemaenderungen.' }
Write-Host 'AETHER-Clientdienst laeuft als LocalSystem, auch ohne geoeffnetes Cockpit.'
Write-Host 'Er uebertraegt Rechnerinventar, Ereignisse und Auftragsausgaben an die gewaehlte Zentralverwaltung.'
Write-Host "Systemaenderungen erlaubt: $AllowChanges / DNS-Cache-Autoreparatur erlaubt: $AllowAutoRepair"
if ((Read-Host 'Zur Einrichtung und Datenuebertragung JA eingeben') -cne 'JA') { throw 'Einrichtung nicht freigegeben.' }
if (!$ServerUrl) { $ServerUrl = Read-Host 'HTTPS-Server-URL, z.B. https://aether.example.net:5187/' }
$uri = $null
if (![Uri]::TryCreate($ServerUrl, [UriKind]::Absolute, [ref]$uri) -or
    $uri.Scheme -notin @('https','http') -or (!$uri.IsLoopback -and $uri.Scheme -ne 'https') -or
    $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $uri.AbsolutePath -ne '/') {
    throw 'HTTPS verwenden; HTTP nur auf Loopback. URL ohne Pfad, Query oder Zugangsdaten.'
}
$secureCode = Read-Host 'Einmalcode der Zentralverwaltung' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureCode)
try {
    $code = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
    $body = @{ code=$code; machineName=$env:COMPUTERNAME; cockpitVersion=$version } | ConvertTo-Json -Compress
    $enrollment = Invoke-RestMethod -Method Post -Uri ([Uri]::new($uri, 'api/v1/enroll')) -ContentType 'application/json' -Body $body
}
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $code=$null; $body=$null }
if ($enrollment.state -ne 'pending' -or !$enrollment.deviceToken -or !$enrollment.deviceId) { throw 'Ungueltige Serveranmeldung.' }
New-Item -Path $data -ItemType Directory -Force | Out-Null
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
$acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
    $account = New-Object Security.Principal.SecurityIdentifier($sid)
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($account, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
}
Set-Acl -LiteralPath $data -AclObject $acl
Add-Type -AssemblyName System.Security
$entropy = [Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes('AETHER Managed Client v1'))
$clear = [Text.Encoding]::UTF8.GetBytes($enrollment.deviceToken)
try { $protected = [Security.Cryptography.ProtectedData]::Protect($clear, $entropy, [Security.Cryptography.DataProtectionScope]::LocalMachine) }
finally { [Array]::Clear($clear, 0, $clear.Length); $enrollment.deviceToken=$null }
$config = @{serverUrl=$uri.AbsoluteUri; deviceId=$enrollment.deviceId; protectedToken=[Convert]::ToBase64String($protected); allowChanges=[bool]$AllowChanges; allowAutoRepair=[bool]$AllowAutoRepair}
$temporaryConfig = Join-Path $data ([Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::WriteAllText($temporaryConfig, ($config | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $temporaryConfig -Destination (Join-Path $data 'client.json') -Force
}
finally { if (Test-Path -LiteralPath $temporaryConfig) { Remove-Item -LiteralPath $temporaryConfig -Force } }
$configPath = Join-Path $data 'client.json'
$configAcl = Get-Acl -LiteralPath $configPath
$configAcl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
Set-Acl -LiteralPath $configPath -AclObject $configAcl
Assert-ManagedDataProtected $data
& "$env:WINDIR\System32\sc.exe" create $serviceName binPath= ('"' + $exe + '"') start= delayed-auto DisplayName= 'AETHER Managed Client'
if ($LASTEXITCODE -ne 0) { throw 'Clientdienst konnte nicht angelegt werden; angemeldetes Geraet im Server pruefen.' }
& "$env:WINDIR\System32\sc.exe" description $serviceName 'AETHER: freigegebene Diagnoseberichte und feste Verwaltungsaktionen; keine freie Fernkonsole.'
if ($LASTEXITCODE -ne 0) { throw 'Dienstbeschreibung konnte nicht gesetzt werden.' }
Start-Service -Name $serviceName
Write-Output "Geraet $($enrollment.deviceId) angemeldet. Jetzt Identitaet im Server pruefen und manuell freigeben."
