$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$logs = Join-Path $env:RUNNER_TEMP 'AetherManagerBrowser'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$output = Join-Path $logs 'fixture.stdout.log'
$errors = Join-Path $logs 'fixture.stderr.log'
$runner = Join-Path $root 'tests\AetherManager.Tests\bin\Release\net10.0-windows\AetherManager.Tests.dll'
$process = $null
$fixtureRoot = $null
try {
    $process = Start-Process dotnet -ArgumentList "`"$runner`" --serve-fixture --seconds 600" `
        -WorkingDirectory $root -RedirectStandardOutput $output -RedirectStandardError $errors -PassThru
    $deadline = (Get-Date).AddSeconds(90)
    $credentials = $null
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "Browser fixture exited: $($process.ExitCode)" }
        if (Test-Path -LiteralPath $output) {
            $ready = Get-Content -LiteralPath $output |
                Where-Object { $_.StartsWith('FIXTURE_READY ') } | Select-Object -Last 1
            if ($ready) { $credentials = $ready.Substring(14).Trim() }
        }
        if (!$credentials) { Start-Sleep -Milliseconds 500 }
    } until ($credentials -or (Get-Date) -ge $deadline)
    if (!$credentials -or !(Test-Path -LiteralPath $credentials)) { throw 'Browser fixture readiness timed out.' }
    $fixtureRoot = Split-Path $credentials -Parent
    & node (Join-Path $root 'tests\AetherManager.Tests\browser-smoke.mjs') $credentials (Join-Path $root 'tests\browser-screenshots')
    if ($LASTEXITCODE -ne 0) { throw "Browser smoke failed: $LASTEXITCODE" }
}
finally {
    if ($process) {
        $process.Refresh()
        if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
    if ($fixtureRoot -and (Test-Path -LiteralPath $fixtureRoot)) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
    foreach ($path in @($output, $errors)) {
        if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Tail 80 }
    }
}
