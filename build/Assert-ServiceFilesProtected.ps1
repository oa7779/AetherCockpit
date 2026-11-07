function Assert-PrivilegedItem([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Dienstpfad darf keine Verknuepfung enthalten: $Path" }
    $trustedInstaller = ([Security.Principal.NTAccount]'NT SERVICE\TrustedInstaller').Translate([Security.Principal.SecurityIdentifier]).Value
    $privileged = @('S-1-5-18','S-1-5-32-544',$trustedInstaller)
    $write = [Security.AccessControl.FileSystemRights]::Write -bor
        [Security.AccessControl.FileSystemRights]::Delete -bor
        [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
        [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
        [Security.AccessControl.FileSystemRights]::TakeOwnership
    $acl = Get-Acl -LiteralPath $Path
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $privileged) {
        throw "Dienstdateien brauchen SYSTEM, Administratoren oder TrustedInstaller als Eigentuemer: $Path"
    }
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and
            !($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -and
            ($rule.FileSystemRights -band $write) -and $rule.IdentityReference.Value -notin $privileged) {
            throw "Nicht privilegierter Schreibzugriff auf Dienstdateien: $Path / $($rule.IdentityReference.Value)"
        }
    }
}
function Assert-PrivilegedChildren([string]$Path) {
    foreach ($item in Get-ChildItem -LiteralPath $Path -Force) {
        Assert-PrivilegedItem $item.FullName
        if ($item.PSIsContainer) { Assert-PrivilegedChildren $item.FullName }
    }
}
function Assert-ServiceFilesProtected([string]$ExecutablePath) {
    $programFiles = [IO.Path]::GetFullPath($env:ProgramFiles).TrimEnd('\')
    $directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ExecutablePath))
    if (!$directory.StartsWith($programFiles + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Erhoehte Dienste erfordern ein geschuetztes Programmverzeichnis unter Program Files.'
    }
    $ancestor = $directory
    while ($ancestor -and $ancestor.StartsWith($programFiles, [StringComparison]::OrdinalIgnoreCase)) {
        Assert-PrivilegedItem $ancestor
        $ancestor = Split-Path $ancestor -Parent
    }
    Assert-PrivilegedChildren $directory
}
function Assert-ManagedDataProtected([string]$Path) {
    $base = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'AetherCockpit'
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($resolved -notin @((Join-Path $base 'Manager'), (Join-Path $base 'ManagedClient'))) {
        throw 'Nur dedizierte AETHER-Datenverzeichnisse pruefen.'
    }
    Assert-PrivilegedItem $resolved
    Assert-PrivilegedChildren $resolved
    $privileged = @('S-1-5-18','S-1-5-32-544')
    $items = @(Get-Item -LiteralPath $resolved) + @(Get-ChildItem -LiteralPath $resolved -Force -Recurse)
    foreach ($item in $items) {
        Assert-PrivilegedItem $item.FullName
        foreach ($rule in (Get-Acl -LiteralPath $item.FullName).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
            if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin $privileged -and
                !($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly)) {
                throw "Dienstzugangsdaten duerfen nur SYSTEM und Administratoren lesen: $($item.FullName)"
            }
        }
    }
}
