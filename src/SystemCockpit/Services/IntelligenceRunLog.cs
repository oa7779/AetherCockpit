using System.IO;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using AetherOperations.Intelligence;

namespace SystemCockpit.Services;

internal sealed class IntelligenceRunLog : IDisposable
{
    private StreamWriter? _writer;
    private readonly string _fallbackPath;
    private bool _usingFallback;

    internal static FabricRunDiagnostics ProcessContext()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new()
        {
            BuildVersion = typeof(IntelligenceRunLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            ExecutablePath = Environment.ProcessPath,
            ProcessId = Environment.ProcessId,
            ProcessUserSid = identity.User?.Value,
            Elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            LocalAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
    }

    internal static void CaptureStartupException(FabricRunDiagnostics diagnostics, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(exception);
        diagnostics.ExceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        var detail = new StringBuilder();
        for (var depth = 0; exception is not null; exception = exception.InnerException!, depth++)
        {
            detail.AppendLine($"[{depth}] Type: {exception.GetType().FullName ?? exception.GetType().Name}");
            detail.AppendLine($"Message: {exception.Message}");
            detail.AppendLine($"Source: {exception.Source ?? "(none)"}");
            detail.AppendLine($"HResult: 0x{exception.HResult:X8}");
            if (exception is System.Windows.Markup.XamlParseException xaml)
                detail.AppendLine($"XAML line: {xaml.LineNumber}; position: {xaml.LinePosition}");
            detail.AppendLine("StackTrace:");
            detail.AppendLine(exception.StackTrace ?? "(not available)");
        }
        diagnostics.ExceptionDetail = detail.ToString();
    }

    internal IntelligenceRunLog(string root, FabricRunDiagnostics diagnostics, string? fallbackRoot = null,
        bool launch = false)
    {
        var name = $"{(launch ? "launch" : "run")}-{diagnostics.RunId:N}.jsonl";
        _fallbackPath = Path.Combine(fallbackRoot ?? Path.Combine(Path.GetTempPath(), "AETHER", "IntelligenceFabric"),
            "diagnostics", name);
        diagnostics.DiagnosticPath = Path.Combine(root, "diagnostics", name);
        diagnostics.PrimaryDiagnosticPath = diagnostics.DiagnosticPath;
        try { _writer = Open(diagnostics.DiagnosticPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { SwitchToFallback(diagnostics, ex); }
        try { Write(diagnostics); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dispose();
            throw;
        }
    }

    private static StreamWriter Open(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        EvidenceFile.RequireLocalDirectory(directory);
        Directory.CreateDirectory(directory);
        EvidenceFile.RequireLocalDirectory(directory);
        return new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            4096, FileOptions.WriteThrough), new UTF8Encoding(false)) { AutoFlush = true };
    }

    private void SwitchToFallback(FabricRunDiagnostics diagnostics, Exception error)
    {
        diagnostics.DiagnosticWriteError = $"{error.GetType().Name} (0x{error.HResult:X8})";
        Trace.TraceError("AETHER Laufdiagnose: {0}; Ziel: {1}", diagnostics.DiagnosticWriteError, diagnostics.DiagnosticPath);
        if (_usingFallback)
            throw new IOException("AETHER-Laufdiagnose konnte auch im lokalen Ersatzpfad nicht geschrieben werden; " +
                "die Untersuchung wird nicht ohne Diagnose fortgesetzt.", error);
        _usingFallback = true;
        var writer = _writer;
        _writer = null;
        try { writer?.Dispose(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Trace.TraceError("AETHER Laufdiagnose konnte nicht geschlossen werden: {0}", ex.GetType().Name); }
        diagnostics.DiagnosticPath = _fallbackPath;
        try { _writer = Open(_fallbackPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.DiagnosticWriteError += $"; Ersatzpfad: {ex.GetType().Name} (0x{ex.HResult:X8})";
            throw new IOException("AETHER-Laufdiagnose konnte weder am Produktziel noch im lokalen Ersatzpfad angelegt werden.", ex);
        }
    }

    internal void Write(FabricRunDiagnostics diagnostics)
    {
        try { _writer!.WriteLine(JsonSerializer.Serialize(diagnostics)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SwitchToFallback(diagnostics, ex);
            try { _writer!.WriteLine(JsonSerializer.Serialize(diagnostics)); }
            catch (Exception retry) when (retry is IOException or UnauthorizedAccessException)
            {
                diagnostics.DiagnosticWriteError += $"; Schreiben: {retry.GetType().Name} (0x{retry.HResult:X8})";
                throw new IOException("AETHER-Laufdiagnose kann nicht persistiert werden.", retry);
            }
        }
    }

    public void Dispose() => _writer?.Dispose();
}
