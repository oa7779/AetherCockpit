using System.Globalization;
using AetherOperations;

namespace SystemCockpit.Services;

internal static class GuardianTraceProjection
{
    internal static string EventKey(GuardianTraceResult trace, GuardianTraceFact fact) => $"trace:{trace.Id:D}:{fact.Sequence}";
    internal static string ProcessContext(ProcessIdentity identity, IReadOnlyList<GuardianArtifact> artifacts) =>
        string.Join("\n", artifacts.Where(a => a.TraceResult is not null && a.TraceAnalysis is not null).Take(256)
            .SelectMany(a => a.TraceAnalysis!.Facts.Where(f => f.Identity == identity.ToString()).Take(32)
                .Select(f => $"ETW {f.Kind} / {f.Time:O} / Provider {f.Provider:D} / Event {f.EventId} v{f.Version} / " +
                    $"Sensitive / {f.Availability} / {a.TraceAnalysis.State} / Trace {a.TraceResult!.Id:D}"))
            .Take(64));
    private static SourceMetadata Metadata(GuardianTraceFact fact, bool derived = false) =>
        IntelligenceModel.Metadata($"ETW {fact.Provider:D} / Event {fact.EventId} v{fact.Version} / {fact.RenderPath}", fact.Time,
            fact.Availability == "Available" ? DataAvailability.Available : DataAvailability.Unknown,
            derived ? SourceType.Derived : SourceType.Direct);
    internal static IReadOnlyList<TimelineEntry> Timeline(GuardianArtifact artifact)
    {
        if (artifact.TraceResult is not { } trace || artifact.TraceAnalysis is not { } analysis) return [];
        return analysis.Facts.Take(256).Select(f => new TimelineEntry(EventKey(trace, f), f.Time, artifact.Created, "DeepTrace",
            $"ETW — {f.Kind}", EventKey(trace, f), Metadata(f),
            $"Profil {trace.Profile} / Quelle {f.Provider:D} / Event {f.EventId} v{f.Version} / Prozess {f.Identity} / " +
            $"Zeitbasis {trace.TimeBasis} / Auswertung {analysis.State}. Zeitnähe beweist keine Ursache.")).ToArray();
    }
    internal static void Project(GuardianArtifact artifact, List<IntelligenceEntity> entities, List<IntelligenceEdge> edges,
        HashSet<string> ids, int maximumFacts)
    {
        if (artifact.TraceResult is not { } trace || artifact.TraceAnalysis is not { } analysis) return;
        var root = "guardian:" + artifact.Id.ToString("D");
        foreach (var fact in analysis.Facts.Take(maximumFacts))
        {
            if (entities.Count + 2 >= IntelligenceModel.MaximumEntities || edges.Count + 3 >= IntelligenceModel.MaximumEdges) break;
            var id = EventKey(trace, fact);
            if (!ids.Add(id)) continue;
            var fields = new Dictionary<string, string>(fact.Data)
            {
                ["domain"] = "DeepTrace", ["profile"] = trace.Profile, ["kind"] = fact.Kind, ["provider"] = fact.Provider.ToString("D"),
                ["event"] = fact.EventId.ToString(CultureInfo.InvariantCulture), ["version"] = fact.Version.ToString(CultureInfo.InvariantCulture),
                ["identity"] = fact.Identity, ["traceState"] = analysis.State, ["timeBasis"] = trace.TimeBasis,
                ["limits"] = string.Join(" | ", analysis.Limits)
            };
            entities.Add(new(id, IntelligenceKind.Event, $"ETW {fact.Kind} — {fact.Time:O}", fields, Metadata(fact)));
            edges.Add(new(root, id, "Eigene ETL enthält dieses dekodierte Ereignis; kein Ursachenbeweis", Metadata(fact)));
            if (fact.Availability != "Available" || !GuardianTraceDecoder.TryIdentity(fact.Identity, out var identity)) continue;
            var process = "process:" + identity;
            if (!ids.Contains(process) && fact.Kind == "ProcessStart")
            {
                ids.Add(process);
                entities.Add(new(process, IntelligenceKind.Process, fact.Data.GetValueOrDefault("ImageName") ?? "Beobachteter ETW-Prozessstart",
                    new Dictionary<string, string>
                    {
                        ["domain"] = "Process", ["pid"] = identity.Pid.ToString(CultureInfo.InvariantCulture),
                        ["identity"] = identity.ToString(), ["scope"] = "Historischer ETW-Prozessstart; kein aktueller Laufstatus"
                    }, Metadata(fact)));
            }
            if (ids.Contains(process)) edges.Add(new(process, id,
                "ETW-Ereignis gehört zu PID und Erstellungszeit; beobachteter Lebenszyklus, keine Kausalbehauptung",
                Metadata(fact, fact.Kind is not ("ProcessStart" or "ProcessEnd"))));
        }
    }
}
