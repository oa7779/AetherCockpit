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
        "BootIsDegradation", "ErrorCode", "DeviceInstanceId", "ServiceName", "UpdateGuid", "UpdateTitle"
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
        foreach (var item in document.Descendants(ns + "EventData").Elements(ns + "Data"))
        {
            var name = item.Attribute("Name")?.Value;
            if (name is not null && Fields.Contains(name) && item.Value.Length <= 2048) fields.TryAdd(name, item.Value);
        }
        return fields;
    }
    internal static string Category(string provider, int id, int level) =>
        provider == "Application Error" && id == 1000 ? "ApplicationCrash" :
        provider == "Application Hang" && id == 1002 ? "ApplicationHang" :
        provider == "Service Control Manager" && id is 7031 or 7034 ? "ServiceFailure" :
        provider.Contains("WHEA", StringComparison.OrdinalIgnoreCase) ? "DeviceError" :
        level is 1 or 2 ? "WindowsError" : "WindowsEvent";
    internal static IReadOnlyList<IntelligenceEntity> ComparableBoots(IEnumerable<IntelligenceEntity> entities) =>
        entities.Where(e => e.Kind == IntelligenceKind.Boot && e.Fields.GetValueOrDefault("provider") == "Microsoft-Windows-Diagnostics-Performance" &&
            e.Fields.GetValueOrDefault("eventId") == "100" && IntelligenceModel.Number(e, "BootTime") is >= 0 &&
            e.Metadata.Availability == DataAvailability.Available)
            .OrderBy(e => IntelligenceModel.Number(e, "BootTime")).ToArray();
}
