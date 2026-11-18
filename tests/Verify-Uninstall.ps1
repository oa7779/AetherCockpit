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
$managedOllamaProcess = $null
$foreignOllamaProcess = $null
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
    $innoForeignAi = Join-Path $root 'inno-foreign-ai'
    $innoForeignModels = Join-Path $innoForeignAi 'models'
    New-Item -ItemType Directory -Path $innoForeignModels -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $innoForeignModels 'foreign-model.bin') -Value 'must survive Inno uninstall'
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES',
        '/NORESTART','/TestRemoveLocalAiConfiguration=1','/TestRemoveLocalAiWeights=1',
        "/TestLocalAiDirectory=`"$innoForeignAi`"","/LOG=`"$uninstallLog`"") -PassThru
    if (!$process.WaitForExit(60000)) { Stop-Process -Id $process.Id -Force; throw 'Isolated uninstall timed out.' }
    if ($process.ExitCode -ne 0) { throw "Isolated uninstall failed: $(Get-Content -LiteralPath $uninstallLog -Raw)" }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ((Test-Path -LiteralPath $app) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if ((Test-Path -LiteralPath $app) -or (Test-Path -LiteralPath $data)) { throw 'Uninstall left installation or user data directory behind.' }
    $uninstallText = Get-Content -LiteralPath $uninstallLog -Raw
    if (!(Test-Path -LiteralPath (Join-Path $innoForeignModels 'foreign-model.bin')) -or
        $uninstallText -notmatch 'AETHER MODEL DATA RETAINED' -or
        $uninstallText -notmatch 'gelöscht' -or $uninstallText -match '�' -or
        $uninstallText -match 'CategoryInfo|FullyQualifiedErrorId|Runtime error') {
        throw 'Inno uninstall did not preserve foreign model data as a successful, clean UTF-8 result.'
    }
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
    'PASS: actual isolated Inno uninstall stopped exact-path fixture, retained foreign models, preserved UTF-8 output, and removed app/data/registration.'
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
    New-Item -ItemType Directory -Path (Join-Path $modelStore 'blobs'),
        (Join-Path $modelStore 'manifests\registry.ollama.ai\library\phi4-mini'),
        (Join-Path $modelStore 'manifests-v2\ollama.com\library\phi4-mini') -Force | Out-Null
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $configText = 'b'
    $layerText = 'c'
    [IO.File]::WriteAllText((Join-Path $root 'config-fixture.bin'), $configText, $utf8)
    [IO.File]::WriteAllText((Join-Path $root 'layer-fixture.bin'), $layerText, $utf8)
    $configDigest = (Get-FileHash -LiteralPath (Join-Path $root 'config-fixture.bin') -Algorithm SHA256).Hash.ToLowerInvariant()
    $layerDigest = (Get-FileHash -LiteralPath (Join-Path $root 'layer-fixture.bin') -Algorithm SHA256).Hash.ToLowerInvariant()
    $configBlob = Join-Path $modelStore "blobs\sha256-$configDigest"
    $layerBlob = Join-Path $modelStore "blobs\sha256-$layerDigest"
    [IO.File]::WriteAllText($configBlob, $configText, $utf8)
    [IO.File]::WriteAllText($layerBlob, $layerText, $utf8)
    New-Item -ItemType Directory -Path (Join-Path $modelStore 'metadata') -Force | Out-Null
    $metadataPath = Join-Path $modelStore "metadata\sha256-$layerDigest.json"
    [IO.File]::WriteAllText($metadataPath, '{"source":"unknown Ollama metadata"}', $utf8)
    $manifestText = @{
        schemaVersion = 2
        config = @{ mediaType = 'application/json'; digest = "sha256:$configDigest"; size = 1 }
        layers = @(@{ mediaType = 'application/vnd.ollama.image.model'; digest = "sha256:$layerDigest"; size = 1 })
    } | ConvertTo-Json -Depth 5 -Compress
    [IO.File]::WriteAllText((Join-Path $root 'manifest-fixture.json'), $manifestText, $utf8)
    $manifestDigest = (Get-FileHash -LiteralPath (Join-Path $root 'manifest-fixture.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestBlob = Join-Path $modelStore "blobs\sha256-$manifestDigest"
    [IO.File]::WriteAllText($manifestBlob, $manifestText, $utf8)
    [IO.File]::WriteAllText(
        (Join-Path $modelStore 'manifests-v2\ollama.com\library\phi4-mini\3.8b'), $manifestText, $utf8)
    $registryManifest = @{
        schemaVersion = 2
        config = @{ mediaType = 'application/json'; digest = "sha256:$configDigest"; size = 1 }
        layers = @(
            @{ mediaType = 'application/vnd.ollama.image.model'; digest = "sha256:$layerDigest"; size = 1 },
            @{ mediaType = 'application/vnd.docker.distribution.manifest.v2+json';
                digest = "sha256:$manifestDigest"; size = [Text.Encoding]::UTF8.GetByteCount($manifestText) }
        )
    } | ConvertTo-Json -Depth 5 -Compress
    [IO.File]::WriteAllText(
        (Join-Path $modelStore 'manifests\registry.ollama.ai\library\phi4-mini\3.8b'), $registryManifest, $utf8)
    @{ schema = 1; modelStorePath = $modelStore; models = @(@{ modelId = 'phi4-mini:3.8b'; digest = $manifestDigest });
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
    if ((Test-Path -LiteralPath $catalogPath) -or !(Test-Path -LiteralPath $manifestBlob) -or
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
    if (!(Test-Path -LiteralPath $metadataPath) -or (Test-Path -LiteralPath $manifestBlob) -or
        (Test-Path -LiteralPath $configBlob) -or (Test-Path -LiteralPath $layerBlob) -or
        (Test-Path -LiteralPath $ownerPath) -or (Test-Path -LiteralPath $catalogPath)) {
        throw 'Explicit AETHER model-weight removal did not delete verified weights while retaining unbound metadata.'
    }
    'PASS: explicit model-weight removal deletes verified AETHER graph files and retains unbound metadata.'

    $foreignAi = Join-Path $root 'foreign-ai'
    $foreignModels = Join-Path $foreignAi 'models'
    New-Item -ItemType Directory -Path $app,$data,$foreignModels -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\build\aether-install.id') -Destination (Join-Path $app 'aether-install.id') -Force
    Set-Content -LiteralPath (Join-Path $data 'keep.txt') -Value 'AETHER data must be removed'
    Set-Content -LiteralPath (Join-Path $foreignModels 'foreign.bin') -Value 'must survive'
    $ErrorActionPreference = 'Continue'
    $retainedOutput = @(powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $helper `
        -InstallDirectory $app -DataDirectory $data -ValidationRoot $root `
        -LocalAiDirectory $foreignAi -RemoveLocalAiConfiguration -RemoveLocalAiWeights 2>&1)
    $retainedExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($retainedExit -ne 0 -or !(Test-Path -LiteralPath (Join-Path $foreignModels 'foreign.bin')) -or
        (Test-Path -LiteralPath (Join-Path $data 'keep.txt')) -or
        (($retainedOutput -join "`n") -notmatch 'AETHER MODEL DATA RETAINED') -or
        (($retainedOutput -join "`n") -notmatch 'gelöscht') -or
        (($retainedOutput -join "`n") -match '�')) {
        throw ("Unowned model data was not retained as a successful, UTF-8-safe uninstall outcome. " +
            "Exit=$retainedExit Output=`n" + ($retainedOutput -join "`n"))
    }
    'PASS: unknown/foreign model data is retained with a warning while AETHER user data is removed successfully.'

    New-Item -ItemType Directory -Path $app,$data,(Join-Path $localAi 'models\blobs') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\build\aether-install.id') -Destination (Join-Path $app 'aether-install.id') -Force
    Set-Content -LiteralPath (Join-Path $foreignModels 'foreign.bin') -Value 'still foreign'
    Set-Content -LiteralPath (Join-Path $data 'keep.txt') -Value 'AETHER data must be removed again'
    Set-Content -LiteralPath (Join-Path $modelStore 'blobs\sha256-unknown-extra') -Value 'mixed-store foreign file'
    Set-Content -LiteralPath $ownerPath -Value '{}'
    Set-Content -LiteralPath $catalogPath -Value '{}'
    $mixedOutput = @(powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $helper `
        -InstallDirectory $app -DataDirectory $data -ValidationRoot $root `
        -LocalAiDirectory $localAi -RemoveLocalAiConfiguration -RemoveLocalAiWeights 2>&1)
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath (Join-Path $modelStore 'blobs\sha256-unknown-extra')) -or
        (Test-Path -LiteralPath (Join-Path $data 'keep.txt')) -or
        (($mixedOutput -join "`n") -notmatch 'AETHER MODEL DATA RETAINED')) {
        throw 'Unknown ownership inside the configured AETHER store was not retained fail-safe.'
    }
    'PASS: unknown mixed-store contents remain while AETHER installation data is removed.'

    $runtimeProject = Join-Path $root 'ollama-runtime-project'
    $runtimeOutput = Join-Path $root 'ollama-runtime-output'
    New-Item -ItemType Directory -Path $runtimeProject | Out-Null
    Set-Content -LiteralPath (Join-Path $runtimeProject 'OllamaUninstallFixture.csproj') -Encoding UTF8 -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>ollama</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
'@
    Set-Content -LiteralPath (Join-Path $runtimeProject 'Program.cs') -Encoding UTF8 -Value @'
using System.Net;
using System.Net.Sockets;
var listener = new TcpListener(IPAddress.Loopback, int.Parse(args[0]));
listener.Start();
Console.WriteLine("READY");
while (true) await Task.Delay(TimeSpan.FromSeconds(1));
'@
    & dotnet build (Join-Path $runtimeProject 'OllamaUninstallFixture.csproj') `
        --configuration Release --nologo --output $runtimeOutput
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the isolated Ollama runtime fixture.' }
    $managedPort = 41000 + (Get-Random -Maximum 10000)
    $foreignPort = 51000 + (Get-Random -Maximum 10000)
    $runtimeExe = Join-Path $runtimeOutput 'ollama.exe'
    $managedOllamaProcess = Start-Process -FilePath $runtimeExe -ArgumentList $managedPort -PassThru -WindowStyle Hidden
    $foreignOllamaProcess = Start-Process -FilePath $runtimeExe -ArgumentList $foreignPort -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ((!(Get-NetTCPConnection -State Listen -LocalPort $managedPort -ErrorAction SilentlyContinue) -or
        !(Get-NetTCPConnection -State Listen -LocalPort $foreignPort -ErrorAction SilentlyContinue)) -and
        [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    $leaseAi = Join-Path $root 'lease-ai'
    $leaseModels = Join-Path $leaseAi 'models'
    New-Item -ItemType Directory -Path $leaseModels,$app,$data -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\build\aether-install.id') -Destination (Join-Path $app 'aether-install.id') -Force
    @{ schema = 1; modelStorePath = $leaseModels;
        models = @(@{ modelId = 'phi4-mini:3.8b'; digest = ('d' * 64) }) } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $leaseAi 'aether-store.json') -Encoding UTF8
    @{
        schema = 1
        processId = $managedOllamaProcess.Id
        processStartTimeUtcTicks = $managedOllamaProcess.StartTime.ToUniversalTime().Ticks
        executablePath = $managedOllamaProcess.Path
        endpoint = "http://127.0.0.1:$managedPort/"
        modelStorePath = $leaseModels
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $leaseAi 'aether-ollama-runtime.json') -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $data 'keep.txt') -Value 'AETHER data must be removed'
    & $helper -InstallDirectory $app -DataDirectory $data -ValidationRoot $root `
        -LocalAiDirectory $leaseAi
    if (!$managedOllamaProcess.HasExited -or $foreignOllamaProcess.HasExited -or
        (Test-Path -LiteralPath (Join-Path $leaseAi 'aether-ollama-runtime.json')) -or
        !(Test-Path -LiteralPath (Join-Path $leaseAi 'models'))) {
        throw 'Runtime lease cleanup did not stop only the AETHER-owned Ollama process.'
    }
    'PASS: uninstall stops the AETHER-leased Ollama PID and preserves an unrelated Ollama fixture process.'
}
finally {
    [Environment]::SetEnvironmentVariable('OLLAMA_MODELS', $oldModelsEnvironment, 'Process')
    [Environment]::SetEnvironmentVariable('OLLAMA_HOST', $oldHostEnvironment, 'Process')
    foreach ($process in @($ownedProcess,$portableProcess,$managedOllamaProcess,$foreignOllamaProcess)) {
        if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
        if ($process) { $process.Dispose() }
    }
    $link = Join-Path $data 'external-link'
    if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link) }
    Remove-Item -LiteralPath $root -Recurse -Force
}
