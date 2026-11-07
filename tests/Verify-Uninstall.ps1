param([string]$Compiler = (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('AetherUninstallTest-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $root 'app'
$data = Join-Path $root 'data'
$outside = Join-Path $root 'external-music.wav'
New-Item -ItemType Directory -Path $root | Out-Null
$ownedProcess = $null
$portableProcess = $null
try {
    $source = (Resolve-Path (Join-Path $PSScriptRoot '..\build\AetherCockpit.iss')).Path
    $output = & $Compiler '/DSetupValidation' '/DSetupUninstallValidation' "/O$root" $source 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
    $setup = Join-Path $root 'AetherSetupValidation.exe'
    $process = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',
        '/TASKS=!autostart',"/DIR=`"$app`"","/LOG=`"$(Join-Path $root 'install.log')`"") -PassThru
    if (!$process.WaitForExit(60000)) { Stop-Process -Id $process.Id -Force; throw 'Isolated install timed out.' }
    if ($process.ExitCode -ne 0) { throw 'Isolated install failed.' }
    $uninstaller = Join-Path $app 'unins000.exe'
    if (!(Test-Path -LiteralPath $uninstaller)) { throw 'Isolated uninstaller missing.' }
    New-Item -ItemType Directory -Path (Join-Path $data 'Backups'),(Join-Path $app 'generated') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $data 'settings.json') -Value '{}'
    Set-Content -LiteralPath (Join-Path $data 'Backups\backup.txt') -Value 'test'
    Set-Content -LiteralPath (Join-Path $app 'generated\leftover.txt') -Value 'test'
    Set-Content -LiteralPath $outside -Value 'external fixture must survive'
    $marker = Join-Path $app 'aether-install.id'
    Set-Content -LiteralPath $marker -Value 'invalid-test-marker'
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES',
        '/NORESTART',"/LOG=`"$(Join-Path $root 'rejected.log')`"") -PassThru
    if (!$process.WaitForExit(60000)) { Stop-Process -Id $process.Id -Force; throw 'Rejected uninstall timed out.' }
    if ($process.ExitCode -eq 0 -or !(Test-Path -LiteralPath (Join-Path $data 'settings.json')) -or
        !(Test-Path -LiteralPath (Join-Path $app 'generated\leftover.txt')) -or !(Test-Path -LiteralPath $uninstaller)) {
        throw 'Pre-uninstall failure did not prevent Inno file and data deletion.'
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\build\aether-install.id') -Destination $marker -Force
    'PASS: rejected pre-uninstall aborts Inno before file/data removal.'
    $fixtureExe = Join-Path $app 'SystemCockpit.exe'
    Add-Type -TypeDefinition 'public static class AetherUninstallFixture { public static void Main() { System.Threading.Thread.Sleep(120000); } }' `
        -OutputAssembly $fixtureExe -OutputType ConsoleApplication
    $portable = Join-Path $root 'portable'
    New-Item -ItemType Directory -Path $portable | Out-Null
    Copy-Item -LiteralPath $fixtureExe -Destination $portable
    $ownedProcess = Start-Process -FilePath $fixtureExe -PassThru -WindowStyle Hidden
    $portableProcess = Start-Process -FilePath (Join-Path $portable 'SystemCockpit.exe') -PassThru -WindowStyle Hidden
    $uninstallLog = Join-Path $root 'uninstall.log'
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES',
        '/NORESTART',"/LOG=`"$uninstallLog`"") -PassThru
    if (!$process.WaitForExit(60000)) { Stop-Process -Id $process.Id -Force; throw 'Isolated uninstall timed out.' }
    if ($process.ExitCode -ne 0) { throw "Isolated uninstall failed: $(Get-Content -LiteralPath $uninstallLog -Raw)" }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ((Test-Path -LiteralPath $app) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if ((Test-Path -LiteralPath $app) -or (Test-Path -LiteralPath $data)) { throw 'Uninstall left installation or user data directory behind.' }
    if (!(Test-Path -LiteralPath $outside)) { throw 'External file was incorrectly removed.' }
    if (!$ownedProcess.HasExited -or $portableProcess.HasExited) { throw 'Exact-path process shutdown did not preserve unrelated portable process.' }
    foreach ($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryHive]::LocalMachine)) {
        foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)) {
            $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$view)
            try {
                $key = $registry.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\AetherUninstallValidation_is1')
                if ($key) { $key.Dispose(); throw 'Uninstall registration survived removal.' }
            } finally { $registry.Dispose() }
        }
    }
    'PASS: actual isolated Inno uninstall stopped exact-path fixture, preserved same-name portable process, removed app/data/registration; external file survived.'
    $helper = (Resolve-Path (Join-Path $PSScriptRoot '..\build\Uninstall-Aether.ps1')).Path
    New-Item -ItemType Directory -Path $app,$data | Out-Null
    Set-Content -LiteralPath (Join-Path $data 'keep.txt') -Value 'must survive rejected cleanup'
    $ErrorActionPreference = 'Continue'
    $rejected = & powershell -NoProfile -File $helper -InstallDirectory $app -DataDirectory $data -ValidationRoot $root 2>&1
    $rejectedExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($rejectedExit -eq 0 -or !(Test-Path -LiteralPath (Join-Path $data 'keep.txt'))) {
        throw 'Missing ownership marker did not safely reject cleanup.'
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\build\aether-install.id') -Destination $app
    $link = Join-Path $data 'external-link'
    $null = New-Item -ItemType Junction -Path $link -Target (Join-Path $root 'app')
    $ErrorActionPreference = 'Continue'
    $rejected = & powershell -NoProfile -File $helper -InstallDirectory $app -DataDirectory $data -ValidationRoot $root 2>&1
    $rejectedExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($rejectedExit -eq 0 -or !(Test-Path -LiteralPath (Join-Path $data 'keep.txt'))) {
        throw 'Linked directory did not safely reject cleanup.'
    }
    [IO.Directory]::Delete($link)
    'PASS: missing marker and directory junction reject all cleanup before data deletion.'
}
finally {
    foreach ($process in @($ownedProcess,$portableProcess)) {
        if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
        if ($process) { $process.Dispose() }
    }
    $link = Join-Path $data 'external-link'
    if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link) }
    Remove-Item -LiteralPath $root -Recurse -Force
}
