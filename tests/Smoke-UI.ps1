param(
    [string]$Executable = (Join-Path $PSScriptRoot '..\src\SystemCockpit\bin\Release\net10.0-windows\SystemCockpit.exe'),
    [string]$ScreenshotPath,
    [switch]$EnableSensors,
    [string]$AdvancedScreenshotPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$app = Start-Process (Resolve-Path $Executable).Path -PassThru

function Find-Element([string]$name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $deadline = (Get-Date).AddSeconds(3)
    do {
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Read-Names {
    $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { $_.Current.Name }
}

function Navigate([string]$button, [string]$expected) {
    $element = Find-Element $button
    if ($null -eq $element) {
        throw "Navigation button missing: $button; root $($root.Current.Name); names: $((@(Read-Names) | Select-Object -First 25) -join ' | ')"
    }
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 350
    if ($null -eq (Find-Element $expected)) { throw "Page missing: $expected" }
}

function Capture-Window([string]$path) {
    Add-Type -AssemblyName System.Drawing
    if (-not ('CockpitCapture' -as [type])) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CockpitCapture {
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(IntPtr window, IntPtr context, uint flags);
}
'@
    }
    $bounds = $root.Current.BoundingRectangle
    $bitmap = [System.Drawing.Bitmap]::new([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $context = $graphics.GetHdc()
    try {
        if (-not [CockpitCapture]::PrintWindow([IntPtr]$root.Current.NativeWindowHandle, $context, 2)) {
            throw 'Native window capture failed'
        }
    } finally { $graphics.ReleaseHdc($context) }
    try { $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

try {
    $deadline = (Get-Date).AddSeconds(90)
    do {
        Start-Sleep -Milliseconds 500
        $app.Refresh()
        if ($app.HasExited) { throw "Application exited: $($app.ExitCode)" }
        if ($app.MainWindowHandle -ne 0) {
            $root = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
            $names = @(Read-Names)
            if ($root.Current.Name -like '*Neon Wave Boot*') {
                $continue = Find-Element 'Cockpit oeffnen / Hinweise gelesen'
                if ($continue) { $continue.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
                continue
            }
            if (($names | Where-Object { $_ -like '*CPU-Gesamtzeit*' }) -and
                ($names | Where-Object { $_ -like '*bestandene Pruefungen*' })) { break }
        }
    } while ((Get-Date) -lt $deadline)
    if ($null -eq $root -or -not $app.Responding) { throw 'No responsive native window' }
    if (-not ($names | Where-Object { $_ -like '*bestandene Pruefungen*' })) {
        throw "Windows posture did not arrive: $($names -join ' | ')"
    }
    $toggle = Find-Element 'Messverlauf lokal aufzeichnen'
    $togglePattern = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($togglePattern.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off) {
        throw 'Recording must default to off'
    }
    $sensorToggle = Find-Element 'Hardware-Sensoren / beim Boot initialisiert'
    $sensorPattern = $sensorToggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($EnableSensors) {
        if ($sensorPattern.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) {
            throw 'Boot sensor initialization failed; read boot warning instead of reporting success'
        }
        $sensorSummary = @(Read-Names) | Where-Object { $_ -match '^\d+ / \d+ Sensorwerte lesbar' }
        if (-not $sensorSummary -or $sensorSummary -notmatch '^[1-9]\d* /') { throw 'No readable boot hardware values in UI' }
        if (-not (@(Read-Names) | Where-Object { $_ -like '*RTX 4080 SUPER*' })) { throw 'Discrete RTX identity absent from cockpit' }
        "HARDWARE UI: $sensorSummary"
    }
    Navigate '02   Sensor-Matrix' 'Sensor-Matrix'
    Navigate '03   Sicherheit' 'Sicherheit & Windows-Telemetrie'
    if ($null -eq (Find-Element 'Defender Echtzeitschutz')) { throw 'Defender check is missing' }
    Navigate '04   Laufwerke' 'Storage Observatory'
    if ($null -eq (Find-Element 'PHYSISCHE LAUFWERKE')) { throw 'Storage view missing' }
    Navigate '05   Messauswertung' 'Messauswertung / MIN - MAX - AVG'
    if ($null -eq (Find-Element 'Auswertung als CSV exportieren')) { throw 'Session CSV export not wired' }
    Navigate '08   Taskmanager' 'TASKMANAGER / EINGEBETTET / LIVE'
    Navigate '09   Ereignisse' 'EREIGNISANZEIGE // EVENTMONITOR'
    Navigate '10   Superadmin' 'SUPERADMIN // EINGEBETTETE SYSTEMVERWALTUNG'
    Navigate '11   Operations' 'OPERATIONS // PLANEN · PRUEFEN · BEWUSST AUSFUEHREN'
    Navigate '06   Diagnose' 'Diagnose & Systemkontrolle'
    if ($null -eq (Find-Element 'Task-Manager')) { throw 'System tools are missing' }
    $advancedButton = Find-Element 'Erweiterte Systemkontrolle'
    $mainHandle = $root.Current.NativeWindowHandle
    $advancedButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $windowCondition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'AETHER | Systemkontrolle | Olaf Weins'),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id))
    $deadline = (Get-Date).AddSeconds(8)
    do {
        Start-Sleep -Milliseconds 200
        $advancedRoot = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants, $windowCondition)
    } while ($null -eq $advancedRoot -and (Get-Date) -lt $deadline)
    if ($null -eq $advancedRoot) { throw 'Advanced control window missing' }
    $mainRoot = $root
    $root = $advancedRoot
    foreach ($tab in @('SMART','Netzwerk','Wartung','Autostart','Logs / Gaming','Einstellungen','Schluessel')) {
        $tabCondition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $tab),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::TabItem))
        $tabElement = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCondition)
        if ($null -eq $tabElement) { throw "Advanced module missing: $tab" }
        $tabElement.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 300
        if (-not $tabElement.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) {
            throw "Advanced module not selectable: $tab"
        }
        if ($tab -eq 'Wartung') {
            $search = Find-Element 'Befehlssuche'
            $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('sfc /verifyonly')
            $copy = Find-Element 'sfc /verifyonly'
            if ($null -eq $copy) { throw 'Administration catalog command missing' }
            $copy.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            $input = Find-Element 'Konsolenbefehl'
            $inputPattern = $input.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            if ($inputPattern.Current.Value -ne 'sfc /verifyonly') { throw 'Catalog click does not copy command' }
            # Execute only a harmless literal, never the copied repair/diagnosis command.
            $inputPattern.SetValue("'AETHER UI fixture'")
            $start = Find-Element 'Start / Enter'
            $start.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            $deadline = (Get-Date).AddSeconds(20)
            do {
                Start-Sleep -Milliseconds 200
                $output = Find-Element 'Aktionsausgabe'
                $text = $output.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
                if ($text.Contains('[ENDE]')) { break }
            } while ((Get-Date) -lt $deadline)
            if (-not $text.Contains('AETHER UI fixture') -or -not $text.Contains('[ENDE]') -or $text.Contains('[FEHLER]')) {
                throw 'Free administration shell failed harmless UI fixture'
            }
            (Find-Element 'Autostart').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            if ($output.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne '' -or
                $inputPattern.Current.Value -ne '') { throw 'Free command/output must clear on module change' }
            $tabElement.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('')
        }
        if ($tab -eq 'Einstellungen') {
            $sensorCheck = Find-Element 'Sensor-Spannungen / Drosselung pruefen'
            $sensorCheck.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 300
            $sensorOutput = Find-Element 'Aktionsausgabe'
            $value = $sensorOutput.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
            if ($value -notlike '*Drosselungsindikator*') { throw 'Actual sensor-diagnosis action not wired' }
            if (-not $value.Contains('[START]') -or -not $value.Contains('[ENDE]')) { throw 'Terminal lifecycle missing' }
            $crt = Find-Element 'CRT-Glas'
            if ($null -eq $crt) { throw 'CRT effects toggle missing' }
            $crtPattern = $crt.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            $crtPattern.Toggle()
            $crtPattern.Toggle()
            $expand = Find-Element 'Konsole vergroessern'
            $expand.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 100
            $collapse = Find-Element 'Module anzeigen'
            if ($null -eq $collapse) { throw 'Expanded console cannot restore modules' }
            $collapse.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            $otherTab = Find-Element 'Wartung'
            $otherTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            if ($sensorOutput.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $value) {
                throw 'Normal terminal transcript must survive module changes'
            }
            $tabElement.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            if ($AdvancedScreenshotPath) { Capture-Window $AdvancedScreenshotPath }
        }
    }
    $privateOutput = Find-Element 'Aktionsausgabe'
    if ($privateOutput.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne '') {
        throw 'Private tab must not automatically display keys'
    }
    $root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 500
    $windowsAfter = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id))
    if ($windowsAfter.Count -eq 0) { throw 'Main window must survive closing the advanced window' }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($mainHandle)
    Navigate '07   Cleaner' 'Cleaner / Vorschau vor Kontrolle'
    if ($null -eq (Find-Element 'Angekreuzte Dateien loeschen')) { throw 'Cleaner confirmation entry is missing' }
    $scanButton = Find-Element 'Temp-Dateien pruefen'
    $scanButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $deadline = (Get-Date).AddSeconds(40)
    do {
        Start-Sleep -Milliseconds 300
        $previewStatus = @(Read-Names) | Where-Object { $_ -match '^\d+ Kandidaten /' }
        if ($previewStatus) { break }
    } while ((Get-Date) -lt $deadline)
    if (-not $previewStatus) { throw 'Cleaner preview did not complete in native UI' }
    $deleteButton = Find-Element 'Angekreuzte Dateien loeschen'
    $deleteButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    if (-not (@(Read-Names) | Where-Object { $_ -like 'Keine Dateien angekreuzt.*' })) {
        throw 'Unselected cleanup action must not delete or confirm files'
    }
    Navigate '01   Cockpit' 'SYSTEMPULS'
    $motion = Find-Element 'Scanner-Animation'
    $motionPattern = $motion.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $initialMotion = $motionPattern.Current.ToggleState
    $motionPattern.Toggle()
    if ($motionPattern.Current.ToggleState -eq $initialMotion) { throw 'Scanner animation control not responding' }
    $motionPattern.Toggle()
    if ($ScreenshotPath) {
        Capture-Window $ScreenshotPath
    }
    "PASS: Native UI responsive; seven main pages plus seven advanced modules; boot sensors checked when requested; main survives child close; recording initially off. PID $($app.Id)"
} finally {
    $app.Refresh()
    if (-not $app.HasExited) {
        [void]$app.CloseMainWindow()
        if (-not $app.WaitForExit(15000)) {
            Stop-Process -Id $app.Id
            throw 'Application did not shut down cleanly within 15 seconds'
        }
    }
}
