using System.IO;
using System.Text;

namespace SystemCockpit.Services;

public static class AdminShell
{
    public static string BuildScript(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        // PowerShell's native decoder must match SFC's redirected UTF-16 output.
        return """
            $ErrorActionPreference='Continue'; $ProgressPreference='SilentlyContinue'
            [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
            $OutputEncoding=[Console]::OutputEncoding
            function global:sfc {
                $previous=[Console]::OutputEncoding
                try {
                    [Console]::OutputEncoding=[Text.Encoding]::Unicode
                    & "$env:windir\System32\sfc.exe" @args | ForEach-Object {
                        [Console]::OutputEncoding=$previous
                        [Console]::WriteLine($_)
                        [Console]::OutputEncoding=[Text.Encoding]::Unicode
                    }
                    $global:LASTEXITCODE=$LASTEXITCODE
                } finally { [Console]::OutputEncoding=$previous }
            }
            $global:LASTEXITCODE=$null
            $script:aetherHadError=$false
            try {
                & {
            """ + Environment.NewLine + command + Environment.NewLine + """
                } 2>&1 | ForEach-Object {
                    if ($_ -is [Management.Automation.ErrorRecord]) {
                        $script:aetherHadError=$true
                        [Console]::Error.WriteLine($_.ToString())
                    } else { $_ }
                } | Out-String -Stream -Width 200 | ForEach-Object { [Console]::WriteLine($_) }
                if ($null -ne $global:LASTEXITCODE) { exit $global:LASTEXITCODE }
                if ($script:aetherHadError) { exit 1 }
                exit 0
            } catch { [Console]::Error.WriteLine($_.ToString()); exit 1 }
            """;
    }

    public static Task<CommandResult> RunAsync(string command, TimeSpan timeout, CancellationToken token,
        IProgress<string>? progress = null)
    {
        var script = BuildScript(command);
        var large = script.Length > 8000;
        return CommandRunner.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe"),
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", large ?
                "& ([scriptblock]::Create([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([Console]::In.ReadToEnd()))))" : script],
            timeout, token, progress, utf8: true, closeInput: true,
            standardInput: large ? Convert.ToBase64String(Encoding.UTF8.GetBytes(script)) : null);
    }
}
