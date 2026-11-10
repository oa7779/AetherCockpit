param([switch]$NoRestore)

$ErrorActionPreference = 'Stop'
$restoreArguments = @()
if ($NoRestore) { $restoreArguments += '--no-restore' }
$root = Split-Path $PSScriptRoot -Parent
$profile = [xml](Get-Content -LiteralPath (Join-Path $root 'src\SystemCockpit\Properties\PublishProfiles\PortableBeta.pubxml') -Raw)
$version = [string]$profile.Project.PropertyGroup.Version
$fileVersion = [string]$profile.Project.PropertyGroup.FileVersion
$destination = Join-Path $root "dist\AetherCockpit-$version-win-x64"
if (Test-Path -LiteralPath $destination) { throw "Release directory already exists: $destination" }
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('AetherSaverPublish-' + [guid]::NewGuid().ToString('N'))
try {
    & dotnet publish (Join-Path $root 'src\SystemCockpit\SystemCockpit.csproj') @restoreArguments -c Release -p:PublishProfile=PortableBeta `
        "-p:Version=$version" -p:AssemblyVersion=0.7.0.0 "-p:FileVersion=$fileVersion" -o $destination
    if ($LASTEXITCODE -ne 0) { throw 'AETHER publish failed.' }
    & dotnet publish (Join-Path $root 'src\AetherClient.Service\AetherClient.Service.csproj') @restoreArguments -c Release -r win-x64 --self-contained true `
        "-p:Version=$version" -p:AssemblyVersion=0.7.0.0 "-p:FileVersion=$fileVersion" -p:DebugType=none -p:DebugSymbols=false -o (Join-Path $destination 'ManagedClient')
    if ($LASTEXITCODE -ne 0) { throw 'Managed client publish failed.' }
    foreach ($script in @('Configure-ManagedClient.ps1', 'Manage-ClientService.ps1', 'Assert-ServiceFilesProtected.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination $destination
    }
    & dotnet publish (Join-Path $root 'src\AetherScreensaver\AetherScreensaver.csproj') @restoreArguments -c Release -r win-x64 --self-contained true `
        "-p:Version=$version" -p:AssemblyVersion=0.7.0.0 "-p:FileVersion=$fileVersion" -p:DebugType=none -p:DebugSymbols=false -o $temporary
    if ($LASTEXITCODE -ne 0) { throw 'Screensaver publish failed.' }
    foreach ($name in @('AetherScreensaver.dll', 'AetherScreensaver.deps.json', 'AetherScreensaver.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $temporary $name) -Destination $destination
    }
    Copy-Item -LiteralPath (Join-Path $temporary 'AetherScreensaver.exe') -Destination (Join-Path $destination 'AetherMainboard.scr')
    if ((Get-FileHash -LiteralPath (Join-Path $destination 'SystemCockpit.dll')).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $temporary 'SystemCockpit.dll')).Hash) {
        throw 'Screensaver was compiled against a different AETHER binary.'
    }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (!(Test-Path -LiteralPath $vswhere)) { throw 'Visual C++ Build Tools/vswhere required for the Windows SCR discovery launcher.' }
    $visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (!$visualStudio) { throw 'Visual C++ x64 tools not found for SCR discovery launcher.' }
    $environment = Join-Path $visualStudio 'VC\Auxiliary\Build\vcvars64.bat'
    $source = Join-Path $root 'src\AetherScreensaver'
    & $env:ComSpec /c "call `"$environment`" >nul && cd /d `"$source`" && rc /nologo /fo `"$temporary\Launcher.res`" Launcher.rc && cl /nologo /O1 /MT /W4 /WX /DUNICODE /D_UNICODE /Fo`"$temporary\Launcher.obj`" Launcher.c `"$temporary\Launcher.res`" /link /SUBSYSTEM:WINDOWS /OUT:`"$destination\Aether.scr`" user32.lib advapi32.lib"
    if ($LASTEXITCODE -ne 0) { throw 'Native Windows SCR discovery launcher build failed.' }
    Write-Output "Published AETHER and unprivileged SCR sharing the self-contained runtime: $destination"
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
