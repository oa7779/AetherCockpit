param(
    [string]$Compiler = (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    [switch]$RegisterIsolatedTask
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path (Join-Path $PSScriptRoot '..\build\AetherCockpit.iss')).Path
$setupText = Get-Content -LiteralPath $source -Raw
if ($setupText -notmatch 'Source: "\{#PublishDir\}\\Aether\.scr"; DestDir: "\{sys\}"; Tasks: screensaver; Flags: ignoreversion' -or
    $setupText -notmatch 'Root: HKLM64; Subkey: "Software\\AetherCockpit"; ValueType: string; ValueName: "InstallPath"; ValueData: "\{app\}"; Tasks: screensaver; Flags: uninsdeletevalue uninsdeletekeyifempty') {
    throw 'Discoverable Windows SCR must be opt-in, installation-bound and removed on uninstall.'
}
Write-Output 'PASS: production SCR discovery file and matching uninstallable HKLM64 path are both gated by screensaver consent.'
$launch = Get-Content -LiteralPath $source |
    Where-Object { $_ -match '^Filename: "\{app\}\\SystemCockpit\.exe"; Description:' }
if (@($launch).Count -ne 1 -or $launch -notmatch '\brunascurrentuser\b') {
    throw 'The post-install application launch must retain the elevated setup token.'
}
$directory = Join-Path ([System.IO.Path]::GetTempPath()) ('AetherSetupTest-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
try {
    $defines = @('/DSetupValidation')
    if ($RegisterIsolatedTask) { $defines += '/DSetupTaskRegistrationValidation' }
    $output = & $Compiler @defines "/O$directory" $source 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($output -join [Environment]::NewLine) }
    $scenarios = @(
        @{Name='enabled'; Tasks='autostart,screensaver'; Upgrade=0; Reconfigure=0; Expected='TASK_XML_VALIDATION passed'},
        @{Name='disabled'; Tasks='!autostart'; Upgrade=0; Reconfigure=0; Expected='task deletion skipped'},
        @{Name='silent-noconsent'; Tasks='autostart'; Upgrade=0; Reconfigure=0; PageConsent=0; Expected='task deletion skipped'; Also='AETHER SILENT AUTOSTART SKIPPED'},
        @{Name='silent-param-consent'; Tasks='autostart'; Upgrade=0; Reconfigure=0; PageConsent=0; AutostartConsent=1; Expected='TASK_XML_VALIDATION passed'},
        @{Name='update-preserve'; Tasks='autostart'; Upgrade=1; Reconfigure=0; Expected='AETHER UPDATE PRESERVATION PASS'},
        @{Name='update-reconfigure'; Tasks='autostart'; Upgrade=1; Reconfigure=1; Expected='TASK_XML_VALIDATION passed'}
    )
    if ($RegisterIsolatedTask) {
        $scenarios = @(@{Name='isolated-registration'; Tasks='autostart'; Upgrade=0; Reconfigure=0; Expected='AETHER TASK CLEANUP PASS'})
    }
    foreach ($scenario in $scenarios) {
        $name = $scenario.Name
        $log = Join-Path $directory "$name.log"
        $testTaskName = 'AETHER Setup Regression ' + [guid]::NewGuid().ToString('N')
        $pageConsent = if ($scenario.ContainsKey('PageConsent')) { $scenario.PageConsent } else { 1 }
        $arguments = @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/TASKS=$($scenario.Tasks)",
            "/TestUpgrade=$($scenario.Upgrade)", "/TestReconfigure=$($scenario.Reconfigure)",
            "/TestPageConsent=$pageConsent",
            "/TestTaskName=`"$testTaskName`"", "/LOG=`"$log`""
        )
        if ($scenario.ContainsKey('AutostartConsent')) { $arguments += "/AutostartConsent=$($scenario.AutostartConsent)" }
        $process = Start-Process -FilePath (Join-Path $directory 'AetherSetupValidation.exe') -ArgumentList $arguments -PassThru
        if (!$process.WaitForExit(60000)) {
            Stop-Process -Id $process.Id -Force
            throw "Setup validation timed out: $name"
        }
        $process.Refresh()
        $text = Get-Content -LiteralPath $log -Raw
        if ($process.ExitCode -ne 0 -or $text -notmatch 'AETHER SETUP VALIDATION PASS' -or
            $text -notmatch 'AETHER FILE REPLACEMENT PASS' -or $text -notmatch 'AETHER OPTIONAL SCREENSAVER PASS' -or
            $text -match 'Runtime error|raised an exception') {
            throw "Setup validation failed ($name):`n$text"
        }
        $expected = $scenario.Expected
        if (!$text.Contains($expected)) {
            throw "Setup validation did not execute the expected autostart branch: $name"
        }
        if ($scenario.ContainsKey('Also') -and !$text.Contains($scenario.Also)) {
            throw "Setup validation did not log the expected silent-consent marker: $name"
        }
        if ($RegisterIsolatedTask) {
            if ($text -notmatch 'AETHER TASK REGISTRATION PASS') { throw 'Isolated registration was not verified.' }
            $scheduler = New-Object -ComObject 'Schedule.Service'
            $scheduler.Connect()
            $remaining = @($scheduler.GetFolder('\').GetTasks(1)) |
                Where-Object { $_.Name -eq $testTaskName }
            if ($remaining) { throw "Isolated task cleanup failed: $testTaskName" }
            Write-Output 'PASS: isolated disabled task registered and removed; no logon trigger, production task unchanged.'
        }
        else {
            Write-Output "PASS: production post-install profile, upgrade and consent checks: $name (no task registered/deleted)."
        }
    }
    Write-Output 'PASS: elevated post-install launch flag is present (application was not launched).'
}
finally {
    Remove-Item -LiteralPath $directory -Recurse -Force
}
