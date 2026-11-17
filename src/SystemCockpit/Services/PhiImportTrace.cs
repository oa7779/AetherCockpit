using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AetherOperations.Intelligence;

namespace SystemCockpit.Services;

internal sealed class PhiImportTrace : IDisposable
{
    private const int MaximumEvents = 256;
    private readonly Dictionary<string, string> _otherIds = [];
    private readonly object _gate = new();
    private TextWriter? _writer;
    private int _events;
    internal bool Enabled { get { lock (_gate) return _writer is not null; } }

    internal PhiImportTrace() : this(Environment.GetEnvironmentVariable("AETHER_PHI_IMPORT_TRACE") == "1",
        Environment.GetEnvironmentVariable("AETHER_PHI_IMPORT_TRACE_DIRECTORY") ?? AppContext.BaseDirectory)
    { }

    internal PhiImportTrace(bool enabled, string directory, Func<string, TextWriter>? writerFactory = null)
    {
        if (!enabled) return;
        try
        {
            EvidenceFile.RequireLocalDirectory(directory);
            var path = Path.Combine(directory, $"phi-import-trace-{Guid.NewGuid():N}.jsonl");
            _writer = writerFactory is not null ? writerFactory(path) :
                new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception ex) { ReportFailure(ex); }
    }

    internal string Id(string? id)
    {
        lock (_gate)
        {
            if (_writer is null) return "redacted";
            try
            {
                if (id is null) return "automatic";
                if (id == AetherModelRegistry.Fallback.Id) return "deterministic";
                if (id == "phi4-mini:3.8b") return id;
                if (!_otherIds.TryGetValue(id, out var value)) _otherIds.Add(id, value = $"other-{_otherIds.Count + 1}");
                return value;
            }
            catch (Exception ex) { Stop(); ReportFailure(ex); return "redacted"; }
        }
    }

    internal void Write(string stage, object details) => Write(stage, () => details);

    internal void Write(string stage, Func<object> details)
    {
        lock (_gate)
        {
            if (_writer is null) return;
            try
            {
                var payload = details();
                if (_writer is null) return;
                _writer.WriteLine(JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, pid = Environment.ProcessId, stage, details = payload }));
                if (++_events >= MaximumEvents) Stop();
            }
            catch (Exception ex) { Stop(); ReportFailure(ex); }
        }
    }

    private void Stop()
    {
        var writer = _writer;
        _writer = null;
        _otherIds.Clear();
        try { writer?.Dispose(); }
        catch (Exception ex) { ReportFailure(ex); }
    }

    private static void ReportFailure(Exception ex)
    {
        try { Trace.TraceError("AETHER Phi-Import: Nicht blockierender Diagnosefehler ({0}); Trace deaktiviert.", ex.GetType().Name); }
        // Auch ein fehlerhafter Diagnoselistener darf den fachlichen Vorgang nicht unterbrechen.
        catch (Exception) { }
    }

    public void Dispose() { lock (_gate) Stop(); }
}
