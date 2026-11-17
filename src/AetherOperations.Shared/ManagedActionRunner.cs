using System.Diagnostics;
using System.Text;

namespace AetherOperations;

public sealed record ManagedActionResult(int ExitCode, string Output, bool Truncated);

public static class ManagedActionRunner
{
    public const int MaximumOutputCharacters = 8 * 1024 * 1024;
    public const int DefaultOutputCharacters = 65536;
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    public static Task<ManagedActionResult> RunAsync(ManagedAction action, CancellationToken cancellationToken) =>
        RunAsync(action, cancellationToken, DefaultOutputCharacters);

    public static async Task<ManagedActionResult> RunAsync(ManagedAction action, CancellationToken cancellationToken,
        int maxOutputCharacters)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (maxOutputCharacters < 1 || maxOutputCharacters > MaximumOutputCharacters)
            throw new ArgumentOutOfRangeException(nameof(maxOutputCharacters));
        var trusted = ManagedActionCatalog.Get(action.Id);
        if (action != trusted && (action.Name != trusted.Name || action.Category != trusted.Category ||
            action.ChangesSystem != trusted.ChangesSystem || action.CanAutoRepair != trusted.CanAutoRepair ||
            action.Script != trusted.Script || action.RequiredCommands is null ||
            !action.RequiredCommands.SequenceEqual(trusted.RequiredCommands)))
            throw new ArgumentException("Only unchanged catalog actions are executable.", nameof(action));
        if (trusted.Id == "windows-update-install")
            throw new ArgumentException("An exact approved WindowsUpdateSelection is required; use RunUpdateAsync.", nameof(action));
        return await ExecuteAsync(trusted.Script, trusted.RequiredCommands, cancellationToken, maxOutputCharacters).ConfigureAwait(false);
    }

    public static Task<ManagedActionResult> RunUpdateAsync(WindowsUpdateSelection selection, CancellationToken cancellationToken) =>
        ExecuteAsync(BuildUpdateScript(selection), ["New-Object"], cancellationToken, DefaultOutputCharacters);

    private static string BuildUpdateScript(WindowsUpdateSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.UpdateId == Guid.Empty || selection.Revision < 0 || !selection.AcceptEula ||
            selection.EulaHash is null || selection.EulaHash.Length != 64 ||
            selection.EulaHash.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("An exact update GUID/revision and approved SHA256 EULA hash are required.", nameof(selection));
        return "$approvedId='" + selection.UpdateId.ToString("D") + "'\n$approvedRevision=" +
            selection.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "\n$approvedHash='" + selection.EulaHash.ToUpperInvariant() + "'\n" + """
            $session=New-Object -ComObject Microsoft.Update.Session
            $session.ClientApplicationID='AETHER exact approved update'
            $searcher=$session.CreateUpdateSearcher()
            $searcher.ServerSelection=0
            $search=$searcher.Search("IsInstalled=0 and IsHidden=0 and UpdateID='$approvedId' and RevisionNumber=$approvedRevision")
            if($search.ResultCode -ne 2){throw "Exact update search incomplete: $($search.ResultCode)"}
            $matches=@($search.Updates | Where-Object {
                $_.Identity.UpdateID -eq $approvedId -and $_.Identity.RevisionNumber -eq $approvedRevision
            })
            if($matches.Count -ne 1){throw 'Exact reviewed update/revision is no longer uniquely offered by the configured source.'}
            $update=$matches[0]
            $sha=[Security.Cryptography.SHA256]::Create()
            try {$actualHash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([string]$update.EulaText)))).Replace('-','')}finally{$sha.Dispose()}
            if($actualHash -cne $approvedHash){throw 'EULA changed; search again and obtain a new approval before installation.'}
            if($update.InstallationBehavior.CanRequestUserInput){throw 'Interactive update is not eligible for noninteractive installation.'}
            $installer=$session.CreateUpdateInstaller()
            $installer.AllowSourcePrompts=$false
            $installer.ForceQuiet=$true
            if($installer.RebootRequiredBeforeInstallation){throw 'An existing reboot is required before installation; no reboot was initiated.'}
            if(!$update.EulaAccepted){$update.AcceptEula()}
            $collection=New-Object -ComObject Microsoft.Update.UpdateColl
            [void]$collection.Add($update)
            $downloader=$session.CreateUpdateDownloader()
            $downloader.Updates=$collection
            $download=$downloader.Download()
            $downloadItem=$download.GetUpdateResult(0)
            $report=[ordered]@{updateId=$approvedId;revision=$approvedRevision;title=$update.Title;type=[int]$update.Type;source='Configured Windows Update/WSUS';serverSelection=0;eulaHash=$actualHash;downloadResultCode=[int]$download.ResultCode;downloadHResult=$download.HResult;downloadUpdateResultCode=[int]$downloadItem.ResultCode;downloadUpdateHResult=$downloadItem.HResult;installResultCode=$null;installHResult=$null;installUpdateResultCode=$null;installUpdateHResult=$null;rebootRequired=$false;rebootRequested=$false}
            if($download.ResultCode -ne 2 -or $downloadItem.ResultCode -ne 2 -or $download.HResult -ne 0 -or $downloadItem.HResult -ne 0){
                [pscustomobject]$report | ConvertTo-Json -Depth 5 -Compress
                exit 1
            }
            $installer.Updates=$collection
            $installed=$installer.Install()
            $item=$installed.GetUpdateResult(0)
            $report.installResultCode=[int]$installed.ResultCode
            $report.installHResult=$installed.HResult
            $report.installUpdateResultCode=[int]$item.ResultCode
            $report.installUpdateHResult=$item.HResult
            $report.rebootRequired=[bool]($installed.RebootRequired -or $item.RebootRequired)
            [pscustomobject]$report | ConvertTo-Json -Depth 5 -Compress
            if($installed.ResultCode -ne 2 -or $item.ResultCode -ne 2 -or $installed.HResult -ne 0 -or $item.HResult -ne 0){exit 1}
            """;
    }

    private static async Task<ManagedActionResult> ExecuteAsync(string actionScript, string[] requiredCommands,
        CancellationToken cancellationToken, int maxOutputCharacters)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows PowerShell is required.");
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var executable = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new FileNotFoundException("Trusted Windows PowerShell executable unavailable.", executable);
        var requirements = string.Join(",", requiredCommands.Select(c => "'" + c.Replace("'", "''") + "'"));
        var script = """
            $ErrorActionPreference='Stop'
            $WarningPreference='SilentlyContinue'
            $ProgressPreference='SilentlyContinue'
            $InformationPreference='SilentlyContinue'
            [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
            $OutputEncoding=[Console]::OutputEncoding
            """ + "\n" + $"foreach($required in @({requirements})){{" +
            "if($required.EndsWith('.exe')){$path=Join-Path ([Environment]::SystemDirectory) $required;if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw ('Required system tool unavailable: '+$required)}}" +
            "elseif(!(Get-Command -Name $required -CommandType Cmdlet,Function -ErrorAction SilentlyContinue)){throw ('Required command unavailable: '+$required)}}\n" +
            "try {\n" + actionScript + "\n} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }\n";
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = systemDirectory
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                     "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            start.ArgumentList.Add(argument);
        // Do not allow inherited module-search paths to override stock Windows commands.
        start.Environment["PSModulePath"] = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "Modules");
        start.Environment["SystemRoot"] = Directory.GetParent(systemDirectory)!.FullName;
        start.Environment["WINDIR"] = start.Environment["SystemRoot"];
        using var process = new Process { StartInfo = start };
        using var deadline = new CancellationTokenSource(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        if (!process.Start()) throw new InvalidOperationException("PowerShell process did not start.");
        var output = new StringBuilder();
        var errors = new StringBuilder();
        var gate = new object();
        var truncated = false;
        async Task Capture(StreamReader reader, StringBuilder target)
        {
            var buffer = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), linked.Token).ConfigureAwait(false)) > 0)
            {
                lock (gate)
                {
                    var take = Math.Min(read, maxOutputCharacters - target.Length);
                    target.Append(buffer, 0, take);
                    if (take < read) truncated = true;
                }
            }
        }
        var stdout = Capture(process.StandardOutput, output);
        var stderr = Capture(process.StandardError, errors);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            if (process.ExitCode != 0 && errors.Length > 0)
            {
                var combined = output.ToString() + "\n[STDERR]\n" + errors;
                truncated |= combined.Length > maxOutputCharacters;
                return new(process.ExitCode, combined[..Math.Min(combined.Length, maxOutputCharacters)], truncated);
            }
            return new(process.ExitCode, output.ToString(), truncated);
        }
        catch (OperationCanceledException)
        {
            KillOwnTree(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch (OperationCanceledException) { }
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw new TimeoutException($"Managed action exceeded {Timeout.TotalMinutes:0} minutes. Partial changes may remain.");
        }
        finally { KillOwnTree(process); }
    }

    private static void KillOwnTree(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Trace.TraceError("AETHER managed process cleanup failed: PID {0}, native error {1}, HRESULT 0x{2:X8}.",
                process.Id, ex.NativeErrorCode, ex.HResult);
        }
    }
}
