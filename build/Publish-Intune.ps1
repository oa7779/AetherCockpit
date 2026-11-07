$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$profile = [xml](Get-Content -LiteralPath (Join-Path $root 'src\SystemCockpit\Properties\PublishProfiles\PortableBeta.pubxml') -Raw)
$version = [string]$profile.Project.PropertyGroup.Version
$name = "AetherCockpit-Setup-$version-win-x64.exe"
$setup = Join-Path $root "dist\$name"
$destination = Join-Path $root "dist\intune\$version"
$tool = Join-Path $root 'tools\intune\IntuneWinAppUtil.exe'
if (!(Test-Path -LiteralPath $setup -PathType Leaf)) { throw "Setup fehlt: $setup" }
if (!(Test-Path -LiteralPath $tool -PathType Leaf)) { throw "Microsoft Win32 Content Prep Tool separat bereitstellen: $tool" }
if (Test-Path -LiteralPath $destination) { throw "Intune-Ausgabe existiert bereits: $destination" }
$source = Join-Path ([IO.Path]::GetTempPath()) ('AetherIntuneSource-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -Path $source -ItemType Directory | Out-Null
    Copy-Item -LiteralPath $setup -Destination $source
    & $tool -c $source -s $name -o $destination -q
    if ($LASTEXITCODE -ne 0) { throw 'Intune-Paketierung fehlgeschlagen.' }
    $package = Join-Path $destination ([IO.Path]::ChangeExtension($name, 'intunewin'))
    if (!(Test-Path -LiteralPath $package -PathType Leaf)) { throw 'Intune-Paket wurde nicht erzeugt.' }
    $line = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($package)
    [IO.File]::WriteAllText($package + '.sha256', $line + "`n", (New-Object Text.UTF8Encoding($false)))
    Copy-Item -LiteralPath (Join-Path $root 'docs\INTUNE-DEPLOYMENT.md') -Destination $destination
    Write-Output "Intune-Paket erzeugt: $package"
}
finally {
    if (Test-Path -LiteralPath $source) { Remove-Item -LiteralPath $source -Recurse -Force }
}
