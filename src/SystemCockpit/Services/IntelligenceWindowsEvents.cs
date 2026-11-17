using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using AetherOperations;

namespace SystemCockpit.Services;

internal static class IntelligenceWindowsEvents
{
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "BootTime", "MainPathBootTime", "BootPostBootTime", "BootStartTime", "BootEndTime", "SystemBootInstance",
        "BootIsDegradation", "BootType", "ErrorCode", "DeviceInstanceId", "ServiceName", "UpdateGuid", "UpdateTitle"
    };
    internal static (IReadOnlyList<IntelligenceEntity> Entities, IReadOnlyList<SourceCoverage> Coverage) Read(CancellationToken token)
    {
        var entities = new List<IntelligenceEntity>(); var coverage = new List<SourceCoverage>();
        foreach (var channel in new[] { "Application", "System", "Microsoft-Windows-Diagnostics-Performance/Operational",
            "Microsoft-Windows-WindowsUpdateClient/Operational", "Microsoft-Windows-Kernel-PnP/Configuration",
            "Microsoft-Windows-NetworkProfile/Operational", "Microsoft-Windows-WLAN-AutoConfig/Operational" })
        {
            token.ThrowIfCancellationRequested();
            var domain = "Event/" + channel;
            try
            {
                using var reader = new EventLogReader(new EventLogQuery(channel, PathType.LogName,
                    "*[System[TimeCreated[timediff(@SystemTime) <= 604800000]]]") { ReverseDirection = true });
                var count = 0;
                for (; count < 128; count++)
                {
                    token.ThrowIfCancellationRequested();
                    using var record = reader.ReadEvent(TimeSpan.FromMilliseconds(100));
                    if (record is null) break;
                    if (record.TimeCreated is not { } created || record.RecordId is not { } recordId) continue;
                    var fields = ReadFields(record.ToXml());
                    fields["channel"] = channel; fields["recordId"] = recordId.ToString(CultureInfo.InvariantCulture);
                    fields["eventId"] = record.Id.ToString(CultureInfo.InvariantCulture); fields["provider"] = record.ProviderName ?? "Nicht verfügbar";
                    fields["level"] = (record.Level ?? 0).ToString(CultureInfo.InvariantCulture);
                    fields["eventVersion"] = (record.Version ?? 0).ToString(CultureInfo.InvariantCulture);
                    fields["osVersion"] = Environment.OSVersion.Version.ToString();
                    var time = new DateTimeOffset(created.ToUniversalTime());
                    var isBoot = record.Id == 100 && record.ProviderName == "Microsoft-Windows-Diagnostics-Performance";
                    entities.Add(IntelligenceSources.Entity($"event:{channel}:{recordId}:{time.UtcTicks}", isBoot ? IntelligenceKind.Boot : IntelligenceKind.Event,
                        $"{record.ProviderName} / {record.Id}", domain, channel + " / RecordId " + recordId, time, fields));
                }
                coverage.Add(new(domain, count < 128, IntelligenceModel.Metadata(channel + " / letzte 7 Tage, höchstens 128 Einträge", DateTimeOffset.UtcNow)));
            }
            catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or XmlException)
            {
                Trace.TraceWarning("Intelligence-Ereigniskanal {0}: {1}", channel, ex.Message);
                coverage.Add(new(domain, false, new SourceMetadata(channel, SourceType.Direct, DateTimeOffset.UtcNow,
                    DataConfidence.Unknown, ex is UnauthorizedAccessException ? DataAvailability.AccessDenied : DataAvailability.ProviderError,
                    classification: DataClassification.Sensitive, error: ex.Message)));
            }
        }
        return (entities, coverage);
    }
    internal static Dictionary<string, string> ReadFields(string xml)
    {
        using var reader = XmlReader.Create(new System.IO.StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 256 * 1024, XmlResolver = null });
        var document = XDocument.Load(reader);
        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var provider = document.Root?.Element(ns + "System")?.Element(ns + "Provider")?.Attribute("Name")?.Value;
        var eventId = document.Root?.Element(ns + "System")?.Element(ns + "EventID")?.Value;
        var faultFields = provider == "Application Error" && eventId == "1000" || provider == "Application Hang" && eventId == "1002";
        var serviceFields = provider == "Service Control Manager" && eventId is "7031" or "7034";
        foreach (var item in document.Descendants(ns + "EventData").Elements(ns + "Data"))
        {
            var name = item.Attribute("Name")?.Value;
            if (name is not null && item.Value.Length <= 2048 &&
                (Fields.Contains(name) || faultFields && name is "AppName" or "AppVersion" or "AppPath" or "ExeFileName" or "ProcessId" or "ProcessID" || serviceFields && name == "param1"))
                fields.TryAdd(name, item.Value);
        }
        var subject = serviceFields ? fields.GetValueOrDefault("param1") : faultFields ?
            fields.GetValueOrDefault("AppPath") ?? fields.GetValueOrDefault("ExeFileName") ?? fields.GetValueOrDefault("AppName") : null;
        if (!string.IsNullOrWhiteSpace(subject)) fields["eventSubject"] = subject + " / " + fields.GetValueOrDefault("AppVersion");
        return fields;
    }
    internal static bool ProcessTarget(IntelligenceEntity entry, out int pid)
    {
        pid = 0;
        var known = entry.Fields.GetValueOrDefault("provider") == "Application Error" && entry.Fields.GetValueOrDefault("eventId") == "1000" ||
            entry.Fields.GetValueOrDefault("provider") == "Application Hang" && entry.Fields.GetValueOrDefault("eventId") == "1002";
        var lower = entry.Fields.GetValueOrDefault("ProcessId"); var upper = entry.Fields.GetValueOrDefault("ProcessID");
        return known && (lower is null || upper is null || lower == upper) && (lower ?? upper) is { } value &&
            ProcessCorrelation.TryPid(value, out pid) && pid > 0;
    }
    internal static string Category(string provider, int id, int level) =>
        provider == "Microsoft-Windows-Diagnostics-Performance" && id == 100 ? "Boot" :
        provider == "Microsoft-Windows-WindowsUpdateClient" ? "WindowsUpdate" :
        provider == "Microsoft-Windows-Kernel-PnP" ? "DeviceEvent" :
        provider is "Microsoft-Windows-NetworkProfile" or "Microsoft-Windows-WLAN-AutoConfig" ? "NetworkEvent" :
        provider == "Application Error" && id == 1000 ? "ApplicationCrash" :
        provider == "Application Hang" && id == 1002 ? "ApplicationHang" :
        provider == "Service Control Manager" && id is 7031 or 7034 ? "ServiceFailure" :
        provider.Contains("WHEA", StringComparison.OrdinalIgnoreCase) ? "DeviceError" :
        level is 1 or 2 ? "WindowsError" : "WindowsEvent";
    internal static IReadOnlyList<IntelligenceEntity> ComparableBoots(IEnumerable<IntelligenceEntity> entities) =>
        entities.Where(e => e.Kind == IntelligenceKind.Boot && e.Fields.GetValueOrDefault("provider") == "Microsoft-Windows-Diagnostics-Performance" &&
            e.Fields.GetValueOrDefault("eventId") == "100" && IntelligenceModel.Number(e, "BootTime") is >= 0 &&
            e.Metadata.Availability == DataAvailability.Available && e.Fields.ContainsKey("eventVersion") &&
            e.Fields.ContainsKey("osVersion") && e.Fields.ContainsKey("BootType"))
            .GroupBy(e => (e.Fields["eventVersion"], e.Fields["osVersion"], e.Fields["BootType"]))
            .Where(g => g.Count() >= 2).OrderByDescending(g => g.Max(e => e.Metadata.Timestamp))
            .Select(g => (IReadOnlyList<IntelligenceEntity>)g.OrderBy(e => IntelligenceModel.Number(e, "BootTime")).ToArray()).FirstOrDefault() ?? [];
}
