$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$profile = [xml](Get-Content -LiteralPath (Join-Path $root 'src\SystemCockpit\Properties\PublishProfiles\PortableBeta.pubxml') -Raw)
$version = [string]$profile.Project.PropertyGroup.Version
$fileVersion = [string]$profile.Project.PropertyGroup.FileVersion
if ($version -notmatch '^\d+\.\d+\.\d+-(beta|rc)\.\d+$' -or $fileVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw 'Invalid server installer version.'
}
$publish = Join-Path $root "dist\AetherManager-$version-win-x64"
$output = Join-Path $root "dist\AetherManager-Setup-$version-win-x64.exe"
if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath "$output.sha256")) {
    throw "Installer output already exists: $output"
}
foreach ($name in @('AetherManager.Server.exe', 'AetherManager.Server.dll',
    'Install-ManagerServer.ps1', 'Assert-ServiceFilesProtected.ps1')) {
    if (!(Test-Path -LiteralPath (Join-Path $publish $name) -PathType Leaf)) {
        throw "Publish the complete server first; missing $name"
    }
}
foreach ($name in @('AetherManager.Server.exe', 'AetherManager.Server.dll')) {
    $info = (Get-Item -LiteralPath (Join-Path $publish $name)).VersionInfo
    if (($info.ProductVersion -split '\+')[0] -ne $version -or $info.FileVersion -ne $fileVersion) {
        throw "Published server version mismatch: $name"
    }
}
foreach ($name in @('Install-ManagerServer.ps1', 'Assert-ServiceFilesProtected.ps1')) {
    if ((Get-FileHash -LiteralPath (Join-Path $publish $name)).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $name)).Hash) {
        throw "Published service script differs from source; publish a new server version: $name"
    }
}
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if (!(Test-Path -LiteralPath $iscc)) {
    $iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
}
if (!(Test-Path -LiteralPath $iscc)) { throw 'Inno Setup 6 is required.' }
& $iscc "/DAppVersion=$version" "/DAppFileVersion=$fileVersion" "/DPublishDir=$publish" (Join-Path $PSScriptRoot 'AetherManager.iss')
if ($LASTEXITCODE -ne 0) { throw 'Server installer compilation failed.' }
if (!(Test-Path -LiteralPath $output -PathType Leaf)) { throw 'Server installer output missing.' }
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($output))" | Set-Content -LiteralPath "$output.sha256" -Encoding ascii
Write-Output "Built local server installer: $output"
