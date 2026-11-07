$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$profile = [xml](Get-Content -LiteralPath (Join-Path $root 'src\SystemCockpit\Properties\PublishProfiles\PortableBeta.pubxml') -Raw)
$version = [string]$profile.Project.PropertyGroup.Version
$fileVersion = [string]$profile.Project.PropertyGroup.FileVersion
$destination = Join-Path $root "dist\AetherManager-$version-win-x64"
if (Test-Path -LiteralPath $destination) { throw "Server release directory already exists: $destination" }
& dotnet publish (Join-Path $root 'src\AetherManager.Server\AetherManager.Server.csproj') -c Release -r win-x64 --self-contained true `
    "-p:Version=$version" -p:AssemblyVersion=0.7.0.0 "-p:FileVersion=$fileVersion" -p:DebugType=none -p:DebugSymbols=false -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Server publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-ManagerServer.ps1') -Destination $destination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assert-ServiceFilesProtected.ps1') -Destination $destination
foreach ($name in @('README.md','CHANGELOG.md','LICENSE','THIRD-PARTY-NOTICES.md','BETA-TESTANLEITUNG.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $destination -Recurse
$notices = Join-Path $destination 'tools'
New-Item -Path $notices -ItemType Directory -Force | Out-Null
foreach ($name in @('COPYING.txt','smartmontools-7.5-source.tar.gz','PresentMon-LICENSE.txt','provenance.json')) {
    Copy-Item -LiteralPath (Join-Path $root "tools\$name") -Destination $notices
}
$serverDocs = Join-Path $destination 'src\AetherManager.Server'
New-Item -Path $serverDocs -ItemType Directory -Force | Out-Null
foreach ($name in @('README.md','AetherManager.Server.http')) {
    Copy-Item -LiteralPath (Join-Path $root "src\AetherManager.Server\$name") -Destination $serverDocs
}
$assets = Join-Path $destination 'src\SystemCockpit\Assets'
New-Item -Path $assets -ItemType Directory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'src\SystemCockpit\Assets\aether-logo.png') -Destination $assets
Write-Output "Published separate HTTPS management server: $destination"
