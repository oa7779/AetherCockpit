$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'build\Assert-ServiceFilesProtected.ps1')
$script:AclFixture = $null
function Get-Item { param([string]$LiteralPath, [switch]$Force); [pscustomobject]@{Attributes=[IO.FileAttributes]::Normal;FullName=$LiteralPath;PSIsContainer=$false} }
function Get-Acl { param([string]$LiteralPath); $script:AclFixture }
function Get-ChildItem { param([string]$LiteralPath, [switch]$Force, [switch]$Recurse); @() }
function Fixture([string]$UserRights) {
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
    foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($sid)), 'FullControl', 'Allow')))
    }
    if ($UserRights) { $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')), $UserRights, 'Allow'))) }
    $script:AclFixture = $acl
}
function Reject([scriptblock]$Action) {
    try { & $Action } catch { return }
    throw 'Expected ACL rejection.'
}
Fixture 'ReadAndExecute'
Assert-PrivilegedItem 'synthetic-service-file'
Fixture 'Modify'
Reject { Assert-PrivilegedItem 'synthetic-writable-service-file' }
Fixture ''
$script:AclFixture.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')))
Reject { Assert-PrivilegedItem 'synthetic-untrusted-owner' }
Fixture 'ReadAndExecute'
$data = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'AetherCockpit\ManagedClient'
Reject { Assert-ManagedDataProtected $data }
Fixture ''
Assert-ManagedDataProtected $data
Write-Output 'PASS 5 mocked service/data ACL cases; no permissions or services changed.'
