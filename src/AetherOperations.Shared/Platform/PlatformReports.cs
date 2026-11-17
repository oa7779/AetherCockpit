using System.Net;
using System.Text;
using System.Text.Json;

namespace AetherOperations.Platform;

public static class PlatformReports
{
    public static string Json(PlatformJob job, bool sensitiveConsent = false)
    {
        PlatformStore.Validate(job);
        return JsonSerializer.Serialize(new
        {
            schema = 1, jobId = job.Preview.Id, action = job.Preview.ActionId, actionVersion = job.Preview.ActionVersion,
            actionHash = job.Preview.ActionHash, previewHash = job.PreviewHash, job.Preview.RunbookId, job.Preview.RunbookHash,
            state = job.State.ToString(),
            classification = sensitiveConsent ? "Sensitive" : "Redacted",
            targets = job.Preview.Targets.Select((item, index) => new
            {
                name = sensitiveConsent ? item.Target.Name : $"Ziel {index + 1}",
                simulated = item.Target.Simulated,
                state = job.Results.SingleOrDefault(r => r.TargetId == item.Target.Id)?.State.ToString() ?? "Nicht gestartet",
                evidence = job.Results.SingleOrDefault(r => r.TargetId == item.Target.Id)?.Facts.Where(pair =>
                    DiagnosticDataPolicy.CanShare(pair.Value.Metadata.Classification, sensitiveConsent))
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
            }),
            audit = job.Audit.Select(entry => new
            {
                entry.Sequence, entry.At, actor = sensitiveConsent ? entry.Actor : "Nicht freigegeben",
                entry.Event, target = sensitiveConsent ? entry.Target : null, entry.PreviousHash, entry.Hash
            }),
            notice = "Hashkette ist kein unabhängiger Revisionssicherheitsnachweis. Simulation ist kein Realnetznachweis."
        }, PlatformJson.Options);
    }

    public static string Csv(PlatformJob job, bool sensitiveConsent = false)
    {
        PlatformStore.Validate(job);
        static string Cell(string value)
        {
            if (value.Length > 0 && ("=+-@\t\r\n".Contains(value[0]))) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var output = new StringBuilder("Ziel,Simulation,Status\r\n");
        foreach (var (target, index) in job.Preview.Targets.Select((t, i) => (t, i)))
            output.Append(Cell(sensitiveConsent ? target.Target.Name : $"Ziel {index + 1}")).Append(',')
                .Append(target.Target.Simulated ? "Ja" : "Nein").Append(',')
                .Append(Cell(job.Results.SingleOrDefault(r => r.TargetId == target.Target.Id)?.State.ToString() ?? "Nicht gestartet")).Append("\r\n");
        return output.ToString();
    }

    public static string Html(PlatformJob job, bool sensitiveConsent = false) =>
        "<!doctype html><html lang=\"de\"><meta charset=\"utf-8\"><title>AETHER Auftragsbericht</title>" +
        "<h1>AETHER Auftragsbericht</h1><p>Druckbare lokale Ansicht; keine externen Ressourcen.</p><pre>" +
        WebUtility.HtmlEncode(Json(job, sensitiveConsent)) + "</pre></html>";
}
