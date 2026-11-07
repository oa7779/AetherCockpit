param(
    [string]$Compiler = (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    [string]$PreviousRelease = (Join-Path $PSScriptRoot '..\dist\AetherCockpit-0.7.0-beta.17-win-x64')
)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('AetherRecoveryTest-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $root 'AETHER System Control'
$link = Join-Path $app 'linked-files'
New-Item -ItemType Directory -Path $app | Out-Null
try {
    $source = (Resolve-Path (Join-Path $PSScriptRoot '..\build\AetherCockpit.iss')).Path
    $output = & $Compiler '/DSetupValidation' "/O$root" $source 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
    foreach ($name in @('SystemCockpit.exe', 'SystemCockpit.dll')) {
        Copy-Item -LiteralPath (Join-Path $PreviousRelease $name) -Destination $app
    }
    Set-Content -LiteralPath (Join-Path $app 'unrelated-must-survive.txt') -Value 'Preserve during recovery'
    $original = @{}
    foreach ($file in Get-ChildItem -LiteralPath $app -File) {
        $original[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash
    }
    foreach ($scenario in @('declined', 'foreign', 'junction', 'accepted')) {
        if ($scenario -eq 'foreign') {
            Move-Item -LiteralPath (Join-Path $app 'SystemCockpit.exe') -Destination (Join-Path $root 'saved.exe')
            Copy-Item -LiteralPath (Join-Path $env:windir 'System32\notepad.exe') -Destination (Join-Path $app 'SystemCockpit.exe')
        }
        if ($scenario -eq 'junction') {
            $null = New-Item -ItemType Junction -Path $link -Target $root
        }
        $consent = if ($scenario -eq 'declined') { '0' } else { '1' }
        $log = Join-Path $root "$scenario.log"
        $process = Start-Process -FilePath (Join-Path $root 'AetherSetupValidation.exe') -ArgumentList @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=!autostart',
            '/TestRecovery=1', "/TestRecoveryConsent=$consent", "/DIR=`"$app`"", "/LOG=`"$log`""
        ) -PassThru
        if (!$process.WaitForExit(60000)) {
            Stop-Process -Id $process.Id -Force
            throw "Recovery scenario timed out: $scenario"
        }
        $text = Get-Content -LiteralPath $log -Raw
        if ($scenario -eq 'accepted') {
            if ($process.ExitCode -ne 0 -or $text -notmatch 'AETHER RECOVERY INSPECTION PASS' -or
                $text -notmatch 'AETHER RECOVERY CONSENT PASS' -or $text -notmatch 'AETHER SETUP VALIDATION PASS') {
                throw "Confirmed recovery did not complete: $text"
            }
        }
        else {
            if ($process.ExitCode -eq 0 -or (Test-Path -LiteralPath (Join-Path $app 'update-probe.txt')) -or
                (Test-Path -LiteralPath (Join-Path $app 'desktop-defaults.json'))) {
                throw "Rejected recovery changed setup payload: $scenario"
            }
            if ($text -match 'AETHER RECOVERY CONSENT PASS') { throw "Invalid recovery was approved: $scenario" }
        }
        if ($scenario -eq 'foreign') {
            Remove-Item -LiteralPath (Join-Path $app 'SystemCockpit.exe') -Force
            Move-Item -LiteralPath (Join-Path $root 'saved.exe') -Destination (Join-Path $app 'SystemCockpit.exe')
        }
        if ($scenario -eq 'junction') { [IO.Directory]::Delete($link) }
        foreach ($name in $original.Keys) {
            if ((Get-FileHash -LiteralPath (Join-Path $app $name)).Hash -ne $original[$name]) {
                throw "Recovery changed existing fixture file: $name"
            }
        }
        Write-Output "PASS: recovery $scenario; existing files preserved, no production process/task/data changes."
    }
}
finally {
    if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link) }
    Remove-Item -LiteralPath $root -Recurse -Force
}
