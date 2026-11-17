using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace AetherOperations.Intelligence;

public static class AetherOllamaExecutable
{
    public static string? Find()
    {
        var candidates = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        candidates.Add(Path.Combine(local, "Programs", "Ollama", "ollama.exe"));
        candidates.Add(Path.Combine(programFiles, "Ollama", "ollama.exe"));
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            candidates.Add(Path.Combine(folder, "ollama.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    public static async Task<(bool Valid, string? Subject)> ReadSignatureAsync(string path, CancellationToken token)
    {
        var escapedPath = path.Replace("'", "''", StringComparison.Ordinal);
        var script = "$ErrorActionPreference='Stop';$s=Get-AuthenticodeSignature -LiteralPath '" + escapedPath +
            "';$subject=if($s.SignerCertificate){$s.SignerCertificate.GetNameInfo(" +
            "[System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false)}else{''};" +
            "[pscustomobject]@{Status=[string]$s.Status;Subject=[string]$subject}|ConvertTo-Json -Compress";
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        startInfo.Environment["PSModulePath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "Modules");
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);
        using var process = Process.Start(startInfo) ??
            throw new Win32Exception("Authenticode-Signatur konnte nicht geprüft werden.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var json = await output.ConfigureAwait(false);
        var errorText = await error.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Authenticode-Prüfung fehlgeschlagen (Exitcode {process.ExitCode}): " +
                errorText[..Math.Min(errorText.Length, 1024)]);
        if (json.Length > 8192)
            throw new InvalidDataException("Authenticode-Antwort überschreitet das Größenlimit.");
        using var document = JsonDocument.Parse(json);
        var status = document.RootElement.GetProperty("Status").GetString();
        var subject = document.RootElement.GetProperty("Subject").GetString();
        return (status == "Valid" && subject?.Contains("Ollama Inc.", StringComparison.OrdinalIgnoreCase) == true, subject);
    }
}
