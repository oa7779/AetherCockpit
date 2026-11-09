using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

internal sealed class BackgroundDiagnosticMonitor(Action<string> notify, Action<string> statusChanged) : IDisposable
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
    private bool _disposed;

    internal void Configure(bool enabled)
    {
        CancellationTokenSource? active;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_enabled == enabled) return;
            _enabled = enabled;
            active = _activeCycle;
        }

        try { active?.Cancel(); }
        catch (ObjectDisposedException) { }
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_wake.CurrentCount == 0) _wake.Release();
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
                    statusChanged(DesktopUiText.Get("Background.Checking"));
                    var json = await CommandRunner.PowerShellAsync(DiagnosticAssistants.CollectionScript, cycle.Token);
                    cycle.Token.ThrowIfCancellationRequested();
                    var report = DiagnosticAssistants.Analyze(DiagnosticAssistants.Parse(json));
                    SaveLatestReport(report);
                    _lastFailure = null;
                    var newFindings = FindNewActionableFindings(report.Findings, _activeFindingKeys);
                    statusChanged(DesktopUiText.Format("Background.FindingCount", report.Findings.Length));
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
                    statusChanged(DesktopUiText.Format("Background.Failure", ex.Message));
                    if (!string.Equals(_lastFailure, ex.Message, StringComparison.Ordinal))
                    {
                        _lastFailure = ex.Message;
                        notify(DesktopUiText.Get("Background.FailureNotice"));
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
        var examples = string.Join("; ", findings.Take(2).Select(finding => $"{finding.AgentDisplay}: {finding.Summary}"));
        var text = DesktopUiText.Format("Background.Notification", findings.Count, examples);
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

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _wake.Dispose();
            _disposed = true;
        }
    }
}
