param(
    [Parameter(Mandatory=$true)][string]$Package,
    [Parameter(Mandatory=$true)][string]$Setup
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packagePath = (Resolve-Path -LiteralPath $Package).Path
$setupPath = (Resolve-Path -LiteralPath $Setup).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('AetherIntuneVerify-' + [Guid]::NewGuid().ToString('N'))
New-Item -Path $fixture -ItemType Directory | Out-Null
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $metadata = @($archive.Entries | Where-Object FullName -like '*/Metadata/Detection.xml')
    $payload = @($archive.Entries | Where-Object FullName -like '*/Contents/IntunePackage.intunewin')
    if ($metadata.Count -ne 1 -or $payload.Count -ne 1) { throw 'Unexpected Intune container layout.' }
    $reader = New-Object IO.StreamReader($metadata[0].Open())
    try { $document = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    $info = $document.ApplicationInfo
    if ($info.SetupFile -ne [IO.Path]::GetFileName($setupPath)) { throw 'Intune setup filename does not match release setup.' }
    $encrypted = Join-Path $fixture 'payload.bin'
    [IO.Compression.ZipFileExtensions]::ExtractToFile($payload[0], $encrypted)
    $encryption = $info.EncryptionInfo
    $key = [Convert]::FromBase64String($encryption.EncryptionKey)
    $hmacKey = [Convert]::FromBase64String($encryption.MacKey)
    $iv = [Convert]::FromBase64String($encryption.InitializationVector)
    $stream = [IO.File]::OpenRead($encrypted)
    try {
        $mac = New-Object byte[] 32
        if ($stream.Read($mac,0,32) -ne 32) { throw 'Incomplete Intune MAC.' }
        $storedIv = New-Object byte[] 16
        if ($stream.Read($storedIv,0,16) -ne 16) { throw 'Incomplete Intune IV.' }
        if ([Convert]::ToBase64String($storedIv) -ne [Convert]::ToBase64String($iv)) { throw 'Intune IV does not match metadata.' }
        $stream.Position = 32
        $hmac = New-Object Security.Cryptography.HMACSHA256
        try {
            $hmac.Key = $hmacKey
            $actual = $hmac.ComputeHash($stream)
        } finally { $hmac.Dispose() }
        if ([Convert]::ToBase64String($actual) -ne [Convert]::ToBase64String($mac) -or
            [Convert]::ToBase64String($actual) -ne [string]$encryption.Mac) { throw 'Intune payload MAC verification failed.' }
        $stream.Position = 48
        $aes = [Security.Cryptography.Aes]::Create()
        $clear = Join-Path $fixture 'source.zip'
        try {
            $aes.Key=$key; $aes.IV=$iv; $aes.Mode='CBC'; $aes.Padding='PKCS7'
            $decrypt = New-Object Security.Cryptography.CryptoStream($stream, $aes.CreateDecryptor(), [Security.Cryptography.CryptoStreamMode]::Read)
            $output = [IO.File]::Create($clear)
            try { $decrypt.CopyTo($output) } finally { $output.Dispose(); $decrypt.Dispose() }
        } finally { $aes.Dispose() }
    } finally { $stream.Dispose() }
    if ([string]$encryption.FileDigestAlgorithm -ne 'SHA256') { throw 'Unexpected digest algorithm.' }
    $hash = [Security.Cryptography.SHA256]::Create()
    $clearStream = [IO.File]::OpenRead($clear)
    try {
        if ([Convert]::ToBase64String($hash.ComputeHash($clearStream)) -ne [string]$encryption.FileDigest) { throw 'Intune decrypted ZIP digest mismatch.' }
    } finally { $clearStream.Dispose(); $hash.Dispose() }
    $source = [IO.Compression.ZipFile]::OpenRead($clear)
    try {
        $files = @($source.Entries | Where-Object { !$_.FullName.EndsWith('/') })
        if ($files.Count -ne 1 -or $files[0].FullName -ne [IO.Path]::GetFileName($setupPath)) { throw 'Intune source must contain exactly the release setup.' }
        $hash = [Security.Cryptography.SHA256]::Create()
        $embedded = $files[0].Open()
        try { $actualSetup = ([BitConverter]::ToString($hash.ComputeHash($embedded))).Replace('-','') }
        finally { $embedded.Dispose(); $hash.Dispose() }
        if ($actualSetup -ne (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash) { throw 'Intune contains a different setup binary.' }
    } finally { $source.Dispose() }
    Write-Output "PASS Intune MAC, IV, decrypted ZIP SHA256 and exact embedded setup identity: $($info.SetupFile)"
}
finally {
    $archive.Dispose()
    if ($key) { [Array]::Clear($key,0,$key.Length) }
    if ($hmacKey) { [Array]::Clear($hmacKey,0,$hmacKey.Length) }
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
