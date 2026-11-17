$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scripts = foreach ($directory in @('build', 'tests')) {
    Get-ChildItem -LiteralPath (Join-Path $root $directory) -Recurse -File -Filter '*.ps1'
}
$errors = foreach ($script in $scripts) {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        $script.FullName,
        [ref]$tokens,
        [ref]$parseErrors
    ) | Out-Null
    foreach ($error in $parseErrors) {
        '{0}:{1}: {2}' -f $script.FullName, $error.Extent.StartLineNumber, $error.Message
    }
}
if ($errors) {
    $errors
    exit 1
}
Write-Output "PowerShell parser passed for $($scripts.Count) scripts."
