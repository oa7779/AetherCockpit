param([string]$Compiler = (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('AetherUninstallTest-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $root 'app'
$data = Join-Path $root 'data'
$outside = Join-Path $root 'external-file.txt'
$oldModelsEnvironment = [Environment]::GetEnvironmentVariable('OLLAMA_MODELS', 'Process')
$oldHostEnvironment = [Environment]::GetEnvironmentVariable('OLLAMA_HOST', 'Process')
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
    $fixtureProjectDirectory = Join-Path $root 'fixture-project'
    $fixtureOutput = Join-Path $root 'fixture-output'
    New-Item -ItemType Directory -Path $fixtureProjectDirectory | Out-Null
    Set-Content -LiteralPath (Join-Path $fixtureProjectDirectory 'AetherUninstallFixture.csproj') -Encoding UTF8 -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>SystemCockpit</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
'@
    Set-Content -LiteralPath (Join-Path $fixtureProjectDirectory 'Program.cs') -Encoding UTF8 -Value @'
using System.Threading;
Thread.Sleep(TimeSpan.FromMinutes(2));
'@
    & dotnet build (Join-Path $fixtureProjectDirectory 'AetherUninstallFixture.csproj') `
        --configuration Release --nologo --output $fixtureOutput
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the isolated uninstall process fixture.' }
    Copy-Item -Path (Join-Path $fixtureOutput '*') -Destination $app -Recurse -Force
    $portable = Join-Path $root 'portable'
    New-Item -ItemType Directory -Path $portable | Out-Null
    Copy-Item -Path (Join-Path $fixtureOutput '*') -Destination $portable -Recurse -Force
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

    $localAi = Join-Path $root 'local-ai'
    $modelStore = Join-Path $localAi 'models'
    $ownerPath = Join-Path $localAi 'aether-store.json'
    $catalogPath = Join-Path $localAi 'model-catalog.json'
    New-Item -ItemType Directory -Path (Join-Path $modelStore 'blobs') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $modelStore 'blobs\sha256-fixture') -Value 'fixture model bytes'
    @{ schema = 1; modelStorePath = $modelStore; models = @(@{ modelId = 'phi4-mini:3.8b'; digest = ('a' * 64) });
        previousModelsEnvironment = 'C:\fixture\previous-models'; previousHostEnvironment = '127.0.0.1:12345' } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ownerPath -Encoding UTF8
    Set-Content -LiteralPath $catalogPath -Value '{}'
    [Environment]::SetEnvironmentVariable('OLLAMA_MODELS', $modelStore, 'Process')
    [Environment]::SetEnvironmentVariable('OLLAMA_HOST', '127.0.0.1:11434', 'Process')
    New-Item -ItemType Directory -Path $app,$data -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\build\aether-install.id') -Destination (Join-Path $app 'aether-install.id') -Force
    Set-Content -LiteralPath (Join-Path $data 'keep.txt') -Value 'fixture'
    & $helper -InstallDirectory $app -DataDirectory $data -ValidationRoot $root `
        -LocalAiDirectory $localAi -RemoveLocalAiConfiguration
    if ((Test-Path -LiteralPath $catalogPath) -or !(Test-Path -LiteralPath (Join-Path $modelStore 'blobs\sha256-fixture')) -or
        !(Test-Path -LiteralPath $ownerPath) -or
        [Environment]::GetEnvironmentVariable('OLLAMA_MODELS', 'Process') -ne 'C:\fixture\previous-models' -or
        [Environment]::GetEnvironmentVariable('OLLAMA_HOST', 'Process') -ne '127.0.0.1:12345') {
        throw 'Removing local AI configuration did not preserve model weights and restore only managed environment values.'
    }
    'PASS: local AI configuration removal preserves owned model weights and restores prior environment values.'

    New-Item -ItemType Directory -Path $app,$data -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\build\aether-install.id') -Destination (Join-Path $app 'aether-install.id') -Force
    Set-Content -LiteralPath $catalogPath -Value '{}'
    [Environment]::SetEnvironmentVariable('OLLAMA_MODELS', $modelStore, 'Process')
    [Environment]::SetEnvironmentVariable('OLLAMA_HOST', '127.0.0.1:11434', 'Process')
    & $helper -InstallDirectory $app -DataDirectory $data -ValidationRoot $root `
        -LocalAiDirectory $localAi -RemoveLocalAiConfiguration -RemoveLocalAiWeights
    if ((Test-Path -LiteralPath $modelStore) -or (Test-Path -LiteralPath $ownerPath) -or
        (Test-Path -LiteralPath $catalogPath)) {
        throw 'Explicit AETHER model-weight removal left owned store contents.'
    }
    'PASS: explicit model-weight removal deletes only the marked AETHER model store.'

    $foreignAi = Join-Path $root 'foreign-ai'
    $foreignModels = Join-Path $foreignAi 'models'
    New-Item -ItemType Directory -Path $app,$data,$foreignModels -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\build\aether-install.id') -Destination (Join-Path $app 'aether-install.id') -Force
    Set-Content -LiteralPath (Join-Path $data 'keep.txt') -Value 'must survive foreign-model rejection'
    Set-Content -LiteralPath (Join-Path $foreignModels 'foreign.bin') -Value 'must survive'
    $foreignRejected = $false
    try {
        & $helper -InstallDirectory $app -DataDirectory $data -ValidationRoot $root `
            -LocalAiDirectory $foreignAi -RemoveLocalAiConfiguration -RemoveLocalAiWeights
    } catch { $foreignRejected = $true }
    if (!$foreignRejected -or !(Test-Path -LiteralPath (Join-Path $foreignModels 'foreign.bin')) -or
        !(Test-Path -LiteralPath (Join-Path $data 'keep.txt'))) {
        throw 'Unowned model data was removed or validation failed after destructive cleanup.'
    }
    'PASS: unowned model-store data rejects removal before AETHER user-data cleanup.'
}
finally {
    [Environment]::SetEnvironmentVariable('OLLAMA_MODELS', $oldModelsEnvironment, 'Process')
    [Environment]::SetEnvironmentVariable('OLLAMA_HOST', $oldHostEnvironment, 'Process')
    foreach ($process in @($ownedProcess,$portableProcess)) {
        if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
        if ($process) { $process.Dispose() }
    }
    $link = Join-Path $data 'external-link'
    if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link) }
    Remove-Item -LiteralPath $root -Recurse -Force
}
