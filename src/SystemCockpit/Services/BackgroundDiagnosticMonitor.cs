using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

internal sealed class BackgroundDiagnosticMonitor(Action<string> notify, Action<string> statusChanged)
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    internal static string ReportPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AetherCockpit", "Operations", "Reports", "background-latest.json");

    private readonly object _sync = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly HashSet<string> _activeFindingKeys = new(StringComparer.Ordinal);
    private CancellationTokenSource? _activeCycle;
    private bool _enabled;
    private string? _lastFailure;

    internal void Configure(bool enabled)
    {
        CancellationTokenSource? active;
        lock (_sync)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            active = _activeCycle;
        }

        try { active?.Cancel(); }
        catch (ObjectDisposedException) { }
        if (_wake.CurrentCount == 0)
        {
            try { _wake.Release(); }
            catch (SemaphoreFullException) { }
        }
    }

    internal async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!IsEnabled())
                {
                    await _wake.WaitAsync(stoppingToken);
                    continue;
                }

                using var cycle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                lock (_sync) _activeCycle = cycle;
                try
                {
                    statusChanged("Hintergrunddiagnose: Windows, Ereignisse, Geraete, Adapter und Apps werden lesend geprueft.");
                    var json = await CommandRunner.PowerShellAsync(DiagnosticAssistants.CollectionScript, cycle.Token);
                    cycle.Token.ThrowIfCancellationRequested();
                    var report = DiagnosticAssistants.Analyze(DiagnosticAssistants.Parse(json));
                    SaveLatestReport(report);
                    _lastFailure = null;
                    var newFindings = FindNewActionableFindings(report.Findings, _activeFindingKeys);
                    statusChanged($"Hintergrunddiagnose: {report.Findings.Length} Befunde im aktuellen Bericht; Reparaturen bleiben manuell.");
                    if (newFindings.Length > 0)
                        notify(NotificationText(newFindings));
                }
                catch (OperationCanceledException) when (
                    cycle.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                {
                    continue;
                }
                catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or
                    InvalidOperationException or TimeoutException or JsonException or InvalidDataException)
                {
                    Trace.TraceError("AETHER Hintergrunddiagnose: {0}", ex);
                    statusChanged("Hintergrunddiagnose fehlgeschlagen: " + ex.Message);
                    if (!string.Equals(_lastFailure, ex.Message, StringComparison.Ordinal))
                    {
                        _lastFailure = ex.Message;
                        notify("Hintergrunddiagnose fehlgeschlagen. Details stehen im AETHER-Status.");
                    }
                }
                finally
                {
                    lock (_sync)
                        if (ReferenceEquals(_activeCycle, cycle))
                            _activeCycle = null;
                }

                await _wake.WaitAsync(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal static bool IsActionable(DiagnosticFinding finding) =>
        finding.Severity is "KRITISCH" or "HOCH" or "MITTEL" or "HINWEIS";

    internal static DiagnosticFinding[] FindNewActionableFindings(
        IEnumerable<DiagnosticFinding> findings, HashSet<string> activeFindingKeys)
    {
        var current = findings.Where(IsActionable).ToArray();
        var newFindings = current.Where(finding => !activeFindingKeys.Contains(Fingerprint(finding))).ToArray();
        activeFindingKeys.Clear();
        activeFindingKeys.UnionWith(current.Select(Fingerprint));
        return newFindings;
    }

    internal static string NotificationText(IReadOnlyCollection<DiagnosticFinding> findings)
    {
        var examples = string.Join("; ", findings.Take(2).Select(finding => $"{finding.Agent}: {finding.Summary}"));
        var text = $"Neue Befunde: {findings.Count}. {examples} Reparatur nur nach Freigabe.";
        return text.Length <= 240 ? text : text[..237] + "...";
    }

    private bool IsEnabled()
    {
        lock (_sync) return _enabled;
    }

    private static string Fingerprint(DiagnosticFinding finding) => finding.Agent + "\0" + finding.Summary;

    private static void SaveLatestReport(DiagnosticAgentReport report)
    {
        var path = ReportPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
