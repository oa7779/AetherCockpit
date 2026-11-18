param(
    [Parameter(Mandatory=$true)][string]$InstallDirectory,
    [string]$DataDirectory,
    [string]$ValidationRoot = '',
    [string]$LocalAiDirectory = '',
    [switch]$InspectRecoveryDirectory,
    [switch]$RemoveLocalAiConfiguration,
    [switch]$RemoveLocalAiWeights
)
$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom

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

function Get-LocalAiOwnership([string]$OwnerFile, [string]$ModelsDirectory) {
    if (!(Test-Path -LiteralPath $OwnerFile)) {
        return [pscustomobject]@{
            State = 'unknown'
            Reason = 'Kein AETHER-Herkunftsnachweis vorhanden.'
            Owner = $null
        }
    }
    try {
        $item = Get-Item -LiteralPath $OwnerFile -Force
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            $item.Length -gt 65536) {
            return [pscustomobject]@{
                State = 'unknown'
                Reason = 'Der Herkunftsnachweis ist kein reguläres, kleines Dateiobjekt.'
                Owner = $null
            }
        }
        $owner = Get-Content -LiteralPath $OwnerFile -Raw | ConvertFrom-Json -ErrorAction Stop
        $ownerModelsPath = [IO.Path]::GetFullPath([string]$owner.modelStorePath).TrimEnd('\')
        $expectedModelsPath = [IO.Path]::GetFullPath($ModelsDirectory).TrimEnd('\')
        if ($owner.schema -ne 1 -or !$ownerModelsPath.Equals(
                $expectedModelsPath, [StringComparison]::OrdinalIgnoreCase) -or
            !@($owner.models).Count) {
            return [pscustomobject]@{
                State = 'unknown'
                Reason = 'Der Herkunftsnachweis ist ungültig oder zeigt nicht auf den AETHER-Modellstore.'
                Owner = $null
            }
        }
        return [pscustomobject]@{ State = 'aether-owned'; Reason = ''; Owner = $owner }
    }
    catch {
        return [pscustomobject]@{
            State = 'unknown'
            Reason = "Der Herkunftsnachweis konnte nicht sicher gelesen werden: $($_.Exception.Message)"
            Owner = $null
        }
    }
}

function Add-ExpectedOwnedFile([Collections.Generic.HashSet[string]]$Files, [string]$BaseDirectory,
    [string]$RelativePath) {
    $fullPath = [IO.Path]::GetFullPath((Join-Path $BaseDirectory $RelativePath)).TrimEnd('\')
    if (!$fullPath.StartsWith(
            [IO.Path]::GetFullPath($BaseDirectory).TrimEnd('\') + '\',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe owned model path: $RelativePath"
    }
    $null = $Files.Add($fullPath)
}

function Remove-VerifiedOwnedFiles([string[]]$Files, [string]$Root) {
    foreach ($file in $Files) {
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            Remove-Item -LiteralPath $file -Force
        }
    }
    foreach ($directory in @(Get-ChildItem -LiteralPath $Root -Directory -Recurse -Force |
        Sort-Object FullName -Descending)) {
        if (!(Get-ChildItem -LiteralPath $directory.FullName -Force | Select-Object -First 1)) {
            Remove-Item -LiteralPath $directory.FullName -Force
        }
    }
    if (!(Get-ChildItem -LiteralPath $Root -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $Root -Force
    }
}

function Test-AetherOwnedModelStore([string]$ModelsDirectory, $Owner) {
    if (!(Test-Path -LiteralPath $ModelsDirectory -PathType Container)) {
        return [pscustomobject]@{ Owned = $false; Reason = 'Der Modellstore ist nicht als Verzeichnis vorhanden.' }
    }
    try {
        $rootItem = Get-Item -LiteralPath $ModelsDirectory -Force
        if ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            return [pscustomobject]@{ Owned = $false; Reason = 'Der Modellstore ist eine Umleitung.' }
        }
        Assert-NoLinks $ModelsDirectory
        $expected = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        $retainedMetadata = New-Object 'System.Collections.Generic.HashSet[string]' (
            [StringComparer]::OrdinalIgnoreCase)
        foreach ($model in @($Owner.models)) {
            $modelId = [string]$model.modelId
            $digest = [string]$model.digest
            if ($modelId -notmatch '^[A-Za-z0-9._-]+:[A-Za-z0-9._-]+$' -or $digest -notmatch '^[0-9a-f]{64}$') {
                return [pscustomobject]@{ Owned = $false; Reason = 'Der Modellvertrag enthält eine ungültige Modell-ID oder Prüfsumme.' }
            }
            $parts = $modelId.Split(':', 2)
            $manifestRelative = Join-Path 'manifests\registry.ollama.ai\library' (Join-Path $parts[0] $parts[1])
            $manifestV2Relative = Join-Path 'manifests-v2\ollama.com\library' (Join-Path $parts[0] $parts[1])
            Add-ExpectedOwnedFile $expected $ModelsDirectory $manifestRelative
            Add-ExpectedOwnedFile $expected $ModelsDirectory $manifestV2Relative
            $manifestBlobRelative = "blobs\sha256-$digest"
            Add-ExpectedOwnedFile $expected $ModelsDirectory $manifestBlobRelative
            $manifestBlob = Join-Path $ModelsDirectory $manifestBlobRelative
            if (!(Test-Path -LiteralPath $manifestBlob -PathType Leaf)) {
                return [pscustomobject]@{ Owned = $false; Reason = 'Ein im Herkunftsnachweis genanntes Modellmanifest fehlt.' }
            }
            $item = Get-Item -LiteralPath $manifestBlob -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -gt 1048576) {
                return [pscustomobject]@{ Owned = $false; Reason = 'Das Modellmanifest ist nicht als sichere reguläre Datei lesbar.' }
            }
            if (!((Get-FileHash -LiteralPath $manifestBlob -Algorithm SHA256).Hash).Equals(
                    $digest, [StringComparison]::OrdinalIgnoreCase)) {
                return [pscustomobject]@{ Owned = $false; Reason = 'Der Modellmanifest-Digest widerspricht dem AETHER-Herkunftsnachweis.' }
            }
            $manifestText = Get-Content -LiteralPath $manifestBlob -Raw
            $manifest = $manifestText | ConvertFrom-Json -ErrorAction Stop
            $references = @($manifest.config) + @($manifest.layers)
            $expectedLayerDigests = New-Object 'System.Collections.Generic.HashSet[string]' (
                [StringComparer]::OrdinalIgnoreCase)
            foreach ($modelLayer in @($manifest.layers)) {
                if ([string]$modelLayer.digest -match '^sha256:([0-9a-f]{64})$') {
                    $null = $expectedLayerDigests.Add($Matches[1])
                }
                else {
                    return [pscustomobject]@{ Owned = $false; Reason = 'Das Modellmanifest enthält einen ungültigen Layer-Verweis.' }
                }
            }
            foreach ($reference in $references) {
                $blobDigest = [string]$reference.digest
                if ($blobDigest -notmatch '^sha256:([0-9a-f]{64})$') {
                    return [pscustomobject]@{ Owned = $false; Reason = 'Das Modellmanifest enthält eine ungültige Blob-Prüfsumme.' }
                }
                $blobHash = $Matches[1]
                $blobRelative = "blobs\sha256-$blobHash"
                Add-ExpectedOwnedFile $expected $ModelsDirectory $blobRelative
                $null = $retainedMetadata.Add(
                    [IO.Path]::GetFullPath((Join-Path $ModelsDirectory "metadata\sha256-$blobHash.json")))
                $blobPath = Join-Path $ModelsDirectory $blobRelative
                if (!(Test-Path -LiteralPath $blobPath -PathType Leaf)) {
                    return [pscustomobject]@{ Owned = $false; Reason = 'Ein vom Modellmanifest referenzierter Blob fehlt.' }
                }
                $blobItem = Get-Item -LiteralPath $blobPath -Force
                if (($blobItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
                    $blobItem.Length -ne [int64]$reference.size -or
                    !((Get-FileHash -LiteralPath $blobPath -Algorithm SHA256).Hash).Equals(
                        $blobHash, [StringComparison]::OrdinalIgnoreCase)) {
                    return [pscustomobject]@{ Owned = $false; Reason = 'Ein referenzierter Blob besitzt nicht die belegte Größe und Prüfsumme.' }
                }
            }
            $manifestV2Path = Join-Path $ModelsDirectory $manifestV2Relative
            if (!(Test-Path -LiteralPath $manifestV2Path -PathType Leaf) -or
                (Get-Content -LiteralPath $manifestV2Path -Raw) -cne $manifestText) {
                return [pscustomobject]@{ Owned = $false; Reason = 'Der Ollama-Manifestpfad stimmt nicht mit dem bestätigten AETHER-Manifest überein.' }
            }
            $registryManifestPath = Join-Path $ModelsDirectory $manifestRelative
            $registryItem = Get-Item -LiteralPath $registryManifestPath -Force -ErrorAction Stop
            if ($registryItem.PSIsContainer -or ($registryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
                $registryItem.Length -gt 1048576) {
                return [pscustomobject]@{ Owned = $false; Reason = 'Der Ollama-Registry-Manifestpfad ist kein reguläres, kleines Dateiobjekt.' }
            }
            $registryManifest = Get-Content -LiteralPath $registryManifestPath -Raw |
                ConvertFrom-Json -ErrorAction Stop
            if ([string]$registryManifest.config.digest -cne [string]$manifest.config.digest -or
                [int64]$registryManifest.config.size -ne [int64]$manifest.config.size) {
                return [pscustomobject]@{ Owned = $false; Reason = 'Das Registry-Manifest verweist auf eine andere Modellkonfiguration.' }
            }
            $registryOwnedDigests = New-Object 'System.Collections.Generic.HashSet[string]' (
                [StringComparer]::OrdinalIgnoreCase)
            $registryChildCount = 0
            $registryInvalid = $false
            foreach ($layer in @($registryManifest.layers)) {
                $layerDigest = [string]$layer.digest
                if ($layerDigest -notmatch '^sha256:([0-9a-f]{64})$') {
                    $registryInvalid = $true
                    continue
                }
                if ($Matches[1] -eq $digest) {
                    $registryChildCount++
                }
                else {
                    $null = $registryOwnedDigests.Add($Matches[1])
                }
            }
            if ($registryInvalid -or $registryOwnedDigests.Count -ne $expectedLayerDigests.Count -or
                !$registryOwnedDigests.SetEquals($expectedLayerDigests) -or $registryChildCount -ne 1) {
                return [pscustomobject]@{ Owned = $false; Reason = 'Das Registry-Manifest verweist nicht exakt auf den bestätigten Modellgraphen.' }
            }
        }
        foreach ($file in Get-ChildItem -LiteralPath $ModelsDirectory -File -Recurse -Force) {
            $fullPath = [IO.Path]::GetFullPath($file.FullName)
            if (!$expected.Contains($fullPath) -and !$retainedMetadata.Contains($fullPath)) {
                return [pscustomobject]@{
                    Owned = $false
                    Reason = "Der Store enthält mindestens eine nicht eindeutig zugeordnete Datei: $($file.FullName)"
                }
            }
        }
        foreach ($expectedFile in $expected) {
            if (!(Test-Path -LiteralPath $expectedFile -PathType Leaf)) {
                return [pscustomobject]@{ Owned = $false; Reason = "Eine im Modellmanifest referenzierte AETHER-Datei fehlt: $expectedFile" }
            }
        }
        return [pscustomobject]@{
            Owned = $true
            Reason = ''
            Files = @($expected)
            RetainedFiles = @($retainedMetadata | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
        }
    }
    catch {
        return [pscustomobject]@{
            Owned = $false
            Reason = "Der Modellstore konnte nicht vollständig verifiziert werden: $($_.Exception.Message)"
        }
    }
}

function Stop-AetherManagedOllama([string]$LocalAiRoot, [string]$ModelsDirectory) {
    $leasePath = Join-Path $LocalAiRoot 'aether-ollama-runtime.json'
    if (!(Test-Path -LiteralPath $leasePath)) { return }
    try {
        $null = Assert-DedicatedDirectory $LocalAiRoot ''
        $rootItem = Get-Item -LiteralPath $LocalAiRoot -Force -ErrorAction Stop
        $leaseItem = Get-Item -LiteralPath $leasePath -Force -ErrorAction Stop
        if (!$rootItem.PSIsContainer -or ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            $leaseItem.PSIsContainer -or ($leaseItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            $leaseItem.Length -gt 4096) {
            Write-Output 'AETHER UNINSTALL WARNING: Eine nicht reguläre Ollama-Runtime-Lease wurde nicht verwendet.'
            return
        }
        $lease = Get-Content -LiteralPath $leasePath -Raw | ConvertFrom-Json -ErrorAction Stop
        $leaseStore = [IO.Path]::GetFullPath([string]$lease.modelStorePath)
        $leaseExecutable = [IO.Path]::GetFullPath([string]$lease.executablePath)
    }
    catch {
        Write-Output "AETHER UNINSTALL WARNING: Runtime-Lease konnte nicht sicher gelesen werden. $($_.Exception.Message)"
        return
    }
    $ownershipResult = Get-LocalAiOwnership (Join-Path $LocalAiRoot 'aether-store.json') $ModelsDirectory
    $endpoint = $null
    if ($ownershipResult.State -ne 'aether-owned' -or [int]$lease.schema -ne 1 -or
        [int]$lease.processId -le 0 -or
        !$leaseStore.Equals([IO.Path]::GetFullPath($ModelsDirectory), [StringComparison]::OrdinalIgnoreCase) -or
        ![Uri]::TryCreate([string]$lease.endpoint, [UriKind]::Absolute, [ref]$endpoint) -or
        $endpoint.Scheme -ne 'http' -or $endpoint.Host -ne '127.0.0.1' -or
        $endpoint.AbsolutePath -ne '/' -or $endpoint.Query -or $endpoint.Fragment -or
        $endpoint.Port -le 0 -or $endpoint.Port -ge 65536) {
        Write-Output 'AETHER UNINSTALL WARNING: Eine nicht eindeutig zuordenbare Ollama-Runtime wurde nicht beendet.'
        return
    }
    $process = Get-Process -Id ([int]$lease.processId) -ErrorAction SilentlyContinue
    if (!$process -or !$process.Path) {
        Write-Output 'AETHER UNINSTALL WARNING: Der Prozess zur Ollama-Runtime-Lease ist nicht mehr vorhanden.'
        return
    }
    if (![IO.Path]::GetFileName($process.Path).Equals('ollama.exe', [StringComparison]::OrdinalIgnoreCase) -or
        !$leaseExecutable.Equals([IO.Path]::GetFullPath($process.Path), [StringComparison]::OrdinalIgnoreCase)) {
        Write-Output 'AETHER UNINSTALL WARNING: Die Prozessdatei zur Ollama-Runtime-Lease stimmt nicht überein.'
        return
    }
    if ([Math]::Abs([long]$lease.processStartTimeUtcTicks -
            $process.StartTime.ToUniversalTime().Ticks) -gt ([TimeSpan]::FromSeconds(2).Ticks)) {
        Write-Output 'AETHER UNINSTALL WARNING: Die Startzeit der Ollama-Runtime-Lease stimmt nicht überein.'
        return
    }
    $listener = @(Get-NetTCPConnection -State Listen -LocalPort $endpoint.Port -ErrorAction SilentlyContinue |
        Where-Object { $_.OwningProcess -eq $process.Id })
    if (!$listener.Count) {
        Write-Output 'AETHER UNINSTALL WARNING: Die Ollama-Runtime besitzt den erwarteten Listener nicht und wurde nicht beendet.'
        return
    }
    Stop-Process -Id $process.Id -Force -ErrorAction Stop
    if (!$process.WaitForExit(10000)) {
        throw "AETHER Ollama process did not stop: $($process.Id)"
    }
    Remove-Item -LiteralPath $leasePath -Force
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
if ($LocalAiDirectory -and !$ValidationRoot) {
    throw 'A local AI directory override is allowed only for isolated validation.'
}
$localAiRoot = [IO.Path]::GetFullPath($(if ($LocalAiDirectory) {
    $LocalAiDirectory
} else {
    Join-Path $env:LOCALAPPDATA 'AETHER\LocalAI'
})).TrimEnd('\')
if ($RemoveLocalAiConfiguration) {
    $localAiRoot = Assert-DedicatedDirectory $localAiRoot ''
}
if ($LocalAiDirectory) {
    $validationRootPath = [IO.Path]::GetFullPath($ValidationRoot).TrimEnd('\')
    if (!$localAiRoot.StartsWith($validationRootPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Validation local AI directory must be a dedicated child of the validation root.'
    }
}
$modelStore = Join-Path $localAiRoot 'models'
$catalog = Join-Path $localAiRoot 'model-catalog.json'
$ownerPath = Join-Path $localAiRoot 'aether-store.json'
$ownership = $null
$localAiRetentionReason = ''
$ownedModelFiles = @()
$retainedModelFiles = @()
if ($RemoveLocalAiWeights -and !$RemoveLocalAiConfiguration) {
    throw 'Model weights may only be removed when local AI configuration removal was also explicitly selected.'
}
if ($RemoveLocalAiConfiguration) {
    $ownershipResult = Get-LocalAiOwnership $ownerPath $modelStore
    if ($ownershipResult.State -eq 'aether-owned') { $ownership = $ownershipResult.Owner }
    if (Test-Path -LiteralPath $catalog) {
        $catalogItem = Get-Item -LiteralPath $catalog -Force
        if ($catalogItem.PSIsContainer -or ($catalogItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'AETHER-Modellkatalog ist ein Link oder Verzeichnis; er wird nicht entfernt.'
        }
    }
    if ($RemoveLocalAiWeights) {
        if (Test-Path -LiteralPath $modelStore) {
            if (!$ownership) {
                $localAiRetentionReason = $ownershipResult.Reason
            }
            else {
                $storeVerification = Test-AetherOwnedModelStore $modelStore $ownership
                if (!$storeVerification.Owned) {
                    $localAiRetentionReason = $storeVerification.Reason
                }
                else {
                    $ownedModelFiles = @($storeVerification.Files)
                    $retainedModelFiles = @($storeVerification.RetainedFiles)
                }
            }
        }
        if (!$localAiRetentionReason -and $ownership -and (Test-Path -LiteralPath $modelStore)) {
            $modelStore = Assert-DedicatedDirectory $modelStore ''
        }
    }
}
Stop-AetherManagedOllama $localAiRoot $modelStore
if (!$ValidationRoot) {
    $serviceHelper = Join-Path $install 'Manage-ClientService.ps1'
    if (Test-Path -LiteralPath $serviceHelper -PathType Leaf) {
        & $serviceHelper -InstallDirectory $install -Operation Remove
        if (!$?) { throw 'AETHER-Clientdienst konnte nicht entfernt werden.' }
    }
}

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
if ($RemoveLocalAiConfiguration) {
    if (Test-Path -LiteralPath $catalog) {
        Remove-Item -LiteralPath $catalog -Force
    }
    if ($ownership) {
        $environmentTarget = if ($ValidationRoot) { 'Process' } else { 'User' }
        $currentModels = [Environment]::GetEnvironmentVariable('OLLAMA_MODELS', $environmentTarget)
        if ($currentModels -and $currentModels.Equals($modelStore, [StringComparison]::OrdinalIgnoreCase)) {
            [Environment]::SetEnvironmentVariable('OLLAMA_MODELS', $ownership.previousModelsEnvironment, $environmentTarget)
        }
        $currentHost = [Environment]::GetEnvironmentVariable('OLLAMA_HOST', $environmentTarget)
        if ($currentHost -and $currentHost.Equals('127.0.0.1:11434', [StringComparison]::OrdinalIgnoreCase)) {
            [Environment]::SetEnvironmentVariable('OLLAMA_HOST', $ownership.previousHostEnvironment, $environmentTarget)
        }
    }
    if ($RemoveLocalAiWeights) {
        if (!$localAiRetentionReason -and $ownership -and (Test-Path -LiteralPath $modelStore)) {
            Remove-VerifiedOwnedFiles $ownedModelFiles $modelStore
        }
        if (!$localAiRetentionReason -and $ownership -and (Test-Path -LiteralPath $ownerPath)) {
            Remove-Item -LiteralPath $ownerPath -Force
        }
    }
}
if ($retainedModelFiles.Count) {
    Write-Output 'AETHER MODEL DATA RETAINED'
    Write-Output 'AETHER UNINSTALL WARNING: Nicht eindeutig AETHER zuordenbare Ollama-Metadaten wurden bewusst beibehalten.'
}
if ($localAiRetentionReason) {
    Write-Output 'AETHER MODEL DATA RETAINED'
    Write-Output "AETHER UNINSTALL WARNING: Ownership=unknown. Vorhandene Modelldateien wurden nicht gelöscht. $localAiRetentionReason"
}
Write-Output 'AETHER cleanup preflight and current-account data removal completed.'
