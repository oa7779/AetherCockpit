param(
    [Parameter(Mandatory=$true)][string]$InstallDirectory,
    [string]$DataDirectory,
    [string]$ValidationRoot = '',
    [switch]$InspectRecoveryDirectory
)
$ErrorActionPreference = 'Stop'

function Assert-DedicatedDirectory([string]$Path, [string]$Marker) {
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (![IO.Path]::IsPathRooted($resolved) -or $resolved -eq [IO.Path]::GetPathRoot($resolved).TrimEnd('\')) {
        throw "Unsafe cleanup directory: $resolved"
    }
    foreach ($protected in @($env:USERPROFILE, $env:LOCALAPPDATA, $env:APPDATA, $env:ProgramFiles,
        ${env:ProgramFiles(x86)}, $env:windir, [IO.Path]::GetTempPath(),
        [Environment]::GetFolderPath('DesktopDirectory'), [Environment]::GetFolderPath('MyDocuments'),
        [Environment]::GetFolderPath('CommonApplicationData'))) {
        if ($protected -and ($resolved.Equals($protected.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase) -or
            $protected.StartsWith($resolved + '\', [StringComparison]::OrdinalIgnoreCase))) {
            throw "Refusing cleanup of shared directory: $resolved"
        }
    }
    $ancestor = Split-Path $resolved -Parent
    while ($ancestor -and $ancestor -ne [IO.Path]::GetPathRoot($ancestor)) {
        if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Cleanup directory must not reside below a link: $ancestor"
        }
        $ancestor = Split-Path $ancestor -Parent
    }
    if (Test-Path -LiteralPath $resolved) {
        $item = Get-Item -LiteralPath $resolved -Force
        if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Cleanup directory must not be a link or file: $resolved"
        }
        if ($Marker -and (!(Test-Path -LiteralPath (Join-Path $resolved $Marker) -PathType Leaf) -or
            (Get-Content -LiteralPath (Join-Path $resolved $Marker) -Raw).Trim() -ne 'AETHER-7B0C30A1-7DB4-45B7-A872-F94664465F40')) {
            throw "Missing or invalid installation ownership marker: $resolved"
        }
    }
    return $resolved
}

function Assert-NoLinks([string]$Directory) {
    foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Remove the link manually before uninstalling; cleanup will not follow it: $($item.FullName)"
        }
        if ($item.PSIsContainer) { Assert-NoLinks $item.FullName }
    }
}

function Remove-OwnedContents([string]$Directory) {
    foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($item.PSIsContainer) { Remove-OwnedContents $item.FullName }
        Remove-Item -LiteralPath $item.FullName -Force
    }
}

if ($InspectRecoveryDirectory) {
    $install = Assert-DedicatedDirectory $InstallDirectory ''
    if ([IO.Path]::GetFileName($install) -ne 'AETHER System Control') {
        throw 'Recovery requires a dedicated AETHER System Control directory.'
    }
    Assert-NoLinks $install
    $exe = Join-Path $install 'SystemCockpit.exe'
    $dll = Join-Path $install 'SystemCockpit.dll'
    if (!(Test-Path -LiteralPath $exe -PathType Leaf) -or !(Test-Path -LiteralPath $dll -PathType Leaf)) {
        throw 'Recovery requires the previous AETHER executable and application library.'
    }
    foreach ($file in @($exe, $dll)) {
        $identity = [Diagnostics.FileVersionInfo]::GetVersionInfo($file)
        if ($identity.ProductName -ne 'AETHER System Control' -or
            $identity.CompanyName -ne 'Olaf Weins' -or $identity.OriginalFilename -ne 'SystemCockpit.dll') {
            throw "Recovery rejected an unknown product: $file"
        }
    }
    Write-Output 'AETHER RECOVERY INSPECTION PASS (read-only; no process or data changes).'
    exit 0
}
if (!$DataDirectory) { throw 'DataDirectory is required for uninstall cleanup.' }
$install = Assert-DedicatedDirectory $InstallDirectory 'aether-install.id'
$data = [IO.Path]::GetFullPath($DataDirectory).TrimEnd('\')
if ($ValidationRoot) {
    $root = [IO.Path]::GetFullPath($ValidationRoot).TrimEnd('\')
    if ([IO.Path]::GetFileName($root) -notlike 'AetherUninstallTest-*' -or
        !$install.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) -or
        !$data.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Validation may only clean dedicated children of an AetherUninstallTest fixture.'
    }
}
elseif (!$data.Equals((Join-Path $env:LOCALAPPDATA 'AetherCockpit'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Only the current Windows account AetherCockpit data directory may be removed.'
}
$data = Assert-DedicatedDirectory $data ''
Assert-NoLinks $install
if (Test-Path -LiteralPath $data) { Assert-NoLinks $data }

& {
    # Match the exact installation path; never stop unrelated portable releases by name.
    $names = @('SystemCockpit.exe', 'AetherMainboard.scr', 'AetherScreensaver.exe', 'Aether.scr')
    $processes = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        $_.ExecutablePath -and $names -contains [IO.Path]::GetFileName($_.ExecutablePath) -and
        [IO.Path]::GetDirectoryName($_.ExecutablePath).Equals($install, [StringComparison]::OrdinalIgnoreCase)
    })
    foreach ($entry in $processes) {
        $process = Get-Process -Id $entry.ProcessId -ErrorAction SilentlyContinue
        if (!$process) { continue }
        if (!$process.Path.Equals($entry.ExecutablePath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Process identity changed before shutdown: $($entry.ProcessId)"
        }
        $null = $process.CloseMainWindow()
    }
    foreach ($entry in $processes) {
        $process = Get-Process -Id $entry.ProcessId -ErrorAction SilentlyContinue
        if (!$process) { continue }
        if (!$process.WaitForExit(5000)) {
            if (!$process.Path.Equals($entry.ExecutablePath, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Process identity changed before termination: $($entry.ProcessId)"
            }
            Stop-Process -Id $entry.ProcessId -Force -ErrorAction Stop
            if (!$process.WaitForExit(10000)) { throw "AETHER process did not stop: $($entry.ProcessId)" }
        }
    }
    foreach ($entry in Get-CimInstance Win32_Process -ErrorAction Stop) {
        if ($entry.ExecutablePath -and $names -contains [IO.Path]::GetFileName($entry.ExecutablePath) -and
            [IO.Path]::GetDirectoryName($entry.ExecutablePath).Equals($install, [StringComparison]::OrdinalIgnoreCase)) {
            throw "AETHER restarted during uninstall: $($entry.ProcessId)"
        }
    }
}
if (!$ValidationRoot) {
    $desktop = Get-ItemProperty -LiteralPath 'HKCU:\Control Panel\Desktop' -ErrorAction Stop
    $ownedSavers = @((Join-Path $install 'AetherMainboard.scr'), (Join-Path $install 'Aether.scr'))
    $registration = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SOFTWARE\AetherCockpit')
    try {
        if ($registration -and [string]::Equals([string]$registration.GetValue('InstallPath'), $install, [StringComparison]::OrdinalIgnoreCase)) {
            $ownedSavers += Join-Path ([Environment]::GetFolderPath('System')) 'Aether.scr'
        }
    }
    finally { if ($registration) { $registration.Dispose() } }
    if ($desktop.'SCRNSAVE.EXE' -and $ownedSavers -contains ([string]$desktop.'SCRNSAVE.EXE').Trim('"')) {
        Remove-ItemProperty -LiteralPath 'HKCU:\Control Panel\Desktop' -Name 'SCRNSAVE.EXE' -ErrorAction Stop
    }
}
if (Test-Path -LiteralPath $data) {
    Remove-OwnedContents $data
    Remove-Item -LiteralPath $data -Force
}
Write-Output 'AETHER cleanup preflight and current-account data removal completed.'
