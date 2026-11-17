using System.Diagnostics;
using System.Text.Json;
using AetherOperations;

namespace SystemCockpit.Services;

public static class TechnicalLog
{
    public static void Write(TechnicalEvent entry)
    {
        var text = JsonSerializer.Serialize(entry.JournalDetails());
        switch (entry.Severity)
        {
            case DiagnosticSeverity.Error:
            case DiagnosticSeverity.Critical:
                Trace.TraceError("{0}", text);
                break;
            case DiagnosticSeverity.Warning:
                Trace.TraceWarning("{0}", text);
                break;
            default:
                Trace.WriteLine(text, entry.Severity.ToString());
                break;
        }
    }
}
