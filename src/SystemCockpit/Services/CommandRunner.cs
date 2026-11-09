using System.Diagnostics;
using System.IO;
using System.Text;

namespace SystemCockpit.Services;

public sealed record CommandResult(int ExitCode, string Output, string Error)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool OutputTruncated { get; init; }
}

public static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(string executable, IEnumerable<string> arguments,
        TimeSpan timeout, CancellationToken token, IProgress<string>? progress = null, bool utf8 = false,
        Encoding? outputEncoding = null, bool closeInput = false, string? standardInput = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = closeInput || standardInput is not null
            }
        };
        if (utf8 || outputEncoding is not null)
        {
            process.StartInfo.StandardOutputEncoding = outputEncoding ?? Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = outputEncoding ?? Encoding.UTF8;
        }
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidOperationException("Werkzeug konnte nicht gestartet werden.");
        if (closeInput && standardInput is null) process.StandardInput.Close();
        progress?.Report($"[PROCESS] PID {process.Id} | {Path.GetFileName(executable)} | " +
            (timeout == Timeout.InfiniteTimeSpan ? "Kein Zeitlimit / geordnetes Prozessende abwarten" : $"Timeout {timeout.TotalMinutes:0.##} min") + Environment.NewLine);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout);
        var output = ReadAsync(process.StandardOutput, progress);
        var error = ReadAsync(process.StandardError, progress, errorStream: true);
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), limit.Token);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(limit.Token);
            var stdout = await output;
            var stderr = await error;
            progress?.Report($"{Environment.NewLine}[PROCESS] Exitcode {process.ExitCode}{Environment.NewLine}");
            return new(process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"Zeitlimit erreicht: {Path.GetFileName(executable)}");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await Task.WhenAll(output, error);
        }
    }

    private static async Task<string> ReadAsync(StreamReader reader, IProgress<string>? progress, bool errorStream = false)
    {
        var result = new StringBuilder();
        var truncated = false;
        var buffer = new char[2048];
        int read;
        // SFC/DISM can flush progress with carriage returns and no newline. Do not wait for ReadLine.
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            if (result.Length < 2_000_000)
            {
                var chunk = new string(buffer, 0, Math.Min(read, 2_000_000 - result.Length));
                result.Append(chunk);
                progress?.Report(errorStream ? $"[STDERR] {chunk}" : chunk);
            }
            else if (!truncated)
            {
                result.AppendLine("[Ausgabe ueber 2 MB gekuerzt]");
                progress?.Report("\n[Ausgabe ueber 2 MB gekuerzt]\n");
                truncated = true;
            }
        }
        return result.ToString();
    }

    public static async Task<string> PowerShellAsync(string script, CancellationToken token)
    {
        var prefix = "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; $WarningPreference='Stop'; " +
            "[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false); ";
        var result = await RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe"),
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(prefix + script))],
            TimeSpan.FromSeconds(60), token, utf8: true);
        var error = WindowsProbe.ReadErrorStream(result.Error);
        if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException($"PowerShell Exit {result.ExitCode}: {error}");
        return result.Output;
    }
}
