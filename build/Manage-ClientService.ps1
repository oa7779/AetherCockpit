param(
    [Parameter(Mandatory=$true)][string]$InstallDirectory,
    [Parameter(Mandatory=$true)][ValidateSet('Stop','Start','Remove')][string]$Operation
)
$ErrorActionPreference = 'Stop'
$name = 'AetherManagedClient'
$expected = '"' + (Join-Path ([IO.Path]::GetFullPath($InstallDirectory)) 'ManagedClient\AetherClient.Service.exe') + '"'
$existing = Get-CimInstance Win32_Service -Filter "Name='$name'"
if (!$existing) { Write-Output 'AETHER CLIENT NOT INSTALLED'; exit 0 }
if ($existing.PathName -ne $expected) { throw 'Clientdienst gehoert zu einer anderen Installation; keine Aenderung vorgenommen.' }
$service = Get-Service -Name $name
if ($Operation -eq 'Start') {
    Start-Service -InputObject $service
    $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
}
else {
    if ($service.Status -ne 'Stopped') {
        Write-Output 'AETHER CLIENT WAS RUNNING'
        Stop-Service -InputObject $service
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    if ($Operation -eq 'Remove') {
        & "$env:WINDIR\System32\sc.exe" delete $name
        if ($LASTEXITCODE -ne 0) { throw 'Clientdienst konnte nicht entfernt werden.' }
    }
}
