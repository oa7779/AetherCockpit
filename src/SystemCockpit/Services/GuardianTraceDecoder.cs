using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using AetherOperations;

namespace SystemCockpit.Services;

internal sealed record GuardianTraceFact(int Sequence, Guid Provider, int EventId, int Version, string Opcode,
    DateTimeOffset Time, int? HeaderPid, int? HeaderTid, string Kind, IReadOnlyDictionary<string, string> Data,
    string Identity, string Availability)
{
    public string? OriginalSystemTime { get; init; }
    public string RenderPath { get; init; } = "XML";
    public IReadOnlyDictionary<string, string> TypedValues { get; init; } = new Dictionary<string, string>();
    public int? Task { get; init; }
    public int? Level { get; init; }
    public ulong? Keywords { get; init; }
    public Guid? EventGuid { get; init; }
    public IReadOnlyList<string> PayloadFields { get; init; } = [];
    [JsonIgnore]
    public IReadOnlyList<string> PayloadTypes { get; init; } = [];
}
internal sealed record GuardianTraceRejectionEvidence(int Sequence, Guid Provider, Guid? EventGuid, int EventId,
    int Version, int? Task, string Opcode, int? Level, ulong? Keywords, string Kind, string State, string RenderPath,
    bool OriginalSystemTimePresent, bool ProcessIdPresent, bool ThreadIdPresent, IReadOnlyList<string> PayloadFields,
    IReadOnlyList<string> PayloadTypes, string Reason);
internal sealed class GuardianTraceFactContractException(string message,
    IReadOnlyList<GuardianTraceRejectionEvidence> rejectionEvidence, int droppedEvidence) : JsonException(message)
{
    public IReadOnlyList<GuardianTraceRejectionEvidence> RejectionEvidence { get; } = rejectionEvidence;
    public int DroppedEvidence { get; } = droppedEvidence;
}
internal sealed record GuardianTraceFieldIssue(int Sequence, Guid Provider, int EventId, int Version, string Opcode,
    string Field, string ActualType, string? RawValue, string RenderPath, string ParserPath, string State, bool Required, string? Error);
internal sealed record GuardianTraceDiagnosticAggregate(string Provider, string? ProviderName, Guid? EventGuid,
    int? EventId, int? Version, int? Task, int? Opcode, int? Level, ulong? Keywords, string Field, string ActualType,
    string ParserPath, string State, bool Required, string RenderPath, string ReasonCode, int Count,
    int FirstSequence, int LastSequence)
{
    public string? ThreadIdFormatClass { get; init; }
    public string? ThreadIdWhitespace { get; init; }
    public string? ProviderGuidStructure { get; init; }
}
internal sealed record GuardianTraceSourceComparison(int Sequence, string Identity, string Kind, string PrimarySource,
    string ConfirmationSource, string IdentityState, string TimeState, string? PrimaryTime, string? ConfirmationTime,
    string? PrimaryCreateTime, string? ConfirmationCreateTime, string? PrimaryExitTime, int? HeaderPid,
    string? PayloadPid, bool PrimaryTimeVerified, string Reason);
internal sealed record GuardianTraceXmlEvidence(int Sequence, string Identity, string RenderPath, string Xml, bool OmittedForSize);
internal sealed record GuardianTraceConversion(string Format, string State, long ObservedBytes, double Milliseconds,
    int? ExitCode, string Detail);
internal sealed record GuardianTraceEventType(string Provider, string? ProviderName, string? NativeProvider, string EventId,
    string Version, string Opcode, string Keywords, string Level, string? Time, string RenderPath, string State,
    int FirstSequence, int Count, IReadOnlyList<string> PayloadFields, string? Xml);
internal sealed record GuardianTraceAnalysis(int Schema, Guid Session, string Profile, string EtlSha256, string State,
    int TotalEvents, int UnknownEvents, int RejectedProviders, IReadOnlyList<GuardianTraceFact> Facts,
    IReadOnlyList<string> Limits, double DecodeMilliseconds)
{
    public IReadOnlyList<GuardianTraceBucket> Activity { get; init; } = [];
    public GuardianTraceDecodeStatus? Decoder { get; init; }
}
internal sealed record GuardianTraceDecodeStatus(bool DecoderInvocationPassed, bool XmlProduced, bool XmlValidated,
    int EventsParsed, int FactsProduced, bool FactLimitReached, bool EventLimitReached, int UnknownProvidersSkipped,
    int UnknownSchemasSkipped, int MalformedFields, bool DecodeIncomplete)
{
    public string TimeSource { get; init; } = "XML SystemTime: unverändert geparst, keine Offsetkorrektur";
    public IReadOnlyList<string> SkippedProviders { get; init; } = [];
    public IReadOnlyList<string> TimeSamples { get; init; } = [];
    public IReadOnlyList<GuardianTraceFieldIssue> FieldIssues { get; init; } = [];
    public IReadOnlyList<GuardianTraceSourceComparison> Sources { get; init; } = [];
    public int MissingFields { get; init; }
    public IReadOnlyList<GuardianTraceXmlEvidence> OwnXml { get; init; } = [];
    public IReadOnlyDictionary<string, int> ProviderEvents { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<GuardianTraceEventType> EventTypes { get; init; } = [];
    public int EventTypeDiagnosticsDropped { get; init; }
    public IReadOnlyList<GuardianTraceDiagnosticAggregate> EventDiagnostics { get; init; } = [];
    public int MalformedAggregateIssuesDropped { get; init; }
    public int RejectedEventDiagnosticsDropped { get; init; }
    public int UnknownSchemaDiagnosticsDropped { get; init; }
}
internal sealed record GuardianTraceBucket(DateTimeOffset Second, string Kind, int Count);
internal sealed record GuardianTraceProcessHistory(string Identity, IReadOnlyList<ProcessSample> Samples);

internal static class GuardianTraceDecoder
{
    internal const int MaximumXmlBytes = 64 * 1024 * 1024, MaximumEvents = 10000, MaximumFacts = 2000;
    internal const int MaximumRejectionEvidence = 4;
    internal const int MaximumMalformedAggregateGroups = 64;
    private static readonly Guid HeaderProvider = new("68fdd900-4a3e-11d1-84f4-0000f80464e3");
    private static readonly Guid ObservedCpuSampleProvider = new("9e814aad-3204-11d2-9a82-006008a86939");
    private static readonly Guid ObservedCpuSampleEventGuid = new("ce1dbfb4-137e-4da6-87b0-3f59aa102cbc");
    private static readonly Guid ManifestCpuSampleProvider = new("ce1dbfb4-137e-4da6-87b0-3f59aa102cbc");
    private static readonly string[] ObservedCpuSampleFields = ["InstructionPointer", "ThreadId", "Count", "Reserved"];
    private static readonly HashSet<string> MalformedReasonCodes = new(StringComparer.Ordinal)
    {
        "ProviderGuidMissing", "ProviderGuidInvalid", "ProviderNotAllowed", "EventGuidMismatch", "DescriptorMismatch",
        "UnsupportedSchema", "PayloadSchemaInvalid",
        "EventIdInvalid", "SystemTimeInvalid",
        "PayloadValueTooLong", "PayloadFieldDuplicate", "PayloadFieldMissing", "PayloadFieldInvalid", "PayloadFieldUnexpected",
        "InstructionPointerInvalid", "ThreadIdInvalid", "CountInvalid", "ReservedInvalid", "IdentityFieldInvalid"
    };
    private static readonly HashSet<string> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProcessID", "ParentProcessID", "ThreadID", "CreateTime", "ExitTime", "ImageName", "ImageFileName", "FileName",
        "FileObject", "FileKey", "IOSize", "IoSize", "TransferSize", "DiskNumber", "ByteOffset", "IssuingThreadId",
        "Status", "PID", "TID", "ThreadId", "ProcessId", "ParentId", "saddr", "daddr", "sport", "dport", "size",
        "connid", "seqnum", "InterfaceIndex", "Protocol", "UniqueProcessKey"
    };
    private readonly record struct MalformedAggregateKey(string Provider, string? ProviderName, Guid? EventGuid,
        int? EventId, int? Version, int? Task, int? Opcode, int? Level, ulong? Keywords, string Field,
        string ActualType, string ParserPath, string State, bool Required, string RenderPath, string ReasonCode,
        string? ThreadIdFormatClass, string? ThreadIdWhitespace, string? ProviderGuidStructure);
    private sealed class MalformedAggregateAccumulator
    {
        private readonly Dictionary<MalformedAggregateKey, GuardianTraceDiagnosticAggregate> _groups = [];
        internal int IssuesDropped { get; private set; }
        internal int RejectedEventsDropped { get; private set; }
        internal int UnknownSchemaEventsDropped { get; private set; }
        internal IReadOnlyList<GuardianTraceDiagnosticAggregate> Snapshot() =>
            _groups.Values.OrderBy(group => group.FirstSequence).ToArray();
        private void CountDropped(string state, int count)
        {
            if (state == "Malformed") IssuesDropped += count;
            else if (state == "Skipped") RejectedEventsDropped += count;
            else UnknownSchemaEventsDropped += count;
        }
        internal void Add(XElement element, XElement? system, string renderPath, int sequence, string field,
            string actualType, string parserPath, bool required, string reasonCode, string state = "Malformed",
            (string Format, string Whitespace)? threadFormat = null)
        {
            if (!MalformedReasonCodes.Contains(reasonCode) || state is not ("Malformed" or "Skipped" or "UnknownSchema"))
                throw new InvalidOperationException("Unbekannter Guardian-Malformed-ReasonCode.");
            string? Attribute(string node, string key) =>
                system?.Elements().FirstOrDefault(e => e.Name.LocalName == node)?.Attribute(key)?.Value;
            string? Value(string node) => system?.Elements().FirstOrDefault(e => e.Name.LocalName == node)?.Value;
            var rawProvider = Attribute("Provider", "Guid");
            var provider = string.IsNullOrWhiteSpace(rawProvider) ? "(missing)" :
                Guid.TryParse(rawProvider, out var parsedProvider) ? parsedProvider.ToString("D") : "(invalid)";
            var rawName = Attribute("Provider", "Name");
            var providerName = rawName is null ? null : DiagnosticToken(rawName, "(other)");
            var eventGuids = element.Elements().Where(e => e.Name.LocalName == "ExtendedTracingInfo")
                .SelectMany(e => e.Elements().Where(child => child.Name.LocalName == "EventGuid"))
                .Select(e => Guid.TryParse(e.Value, out var parsed) ? parsed : (Guid?)null).ToArray();
            var eventGuid = eventGuids.Length == 1 ? eventGuids[0] : null;
            int? Number(string? value) =>
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= 0 ? number : null;
            var eventId = Number(Value("EventID"));
            var version = Number(Value("Version"));
            var task = Number(Value("Task"));
            var opcode = Number(Value("Opcode"));
            var level = Number(Value("Level"));
            ulong? keywords = TryEventKeywords(Value("Keywords"), out var parsedKeywords) ? parsedKeywords : null;
            var safeField = DiagnosticToken(field, "(other)");
            var safeType = actualType switch
            {
                "XML attribute" or "XML element" or "XML string (kein typisierter Variantwert)" or "XML string" or
                "Missing" or "Duplicate" or "System.Boolean" or "System.Byte" or "System.SByte" or "System.Char" or
                "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or
                "System.UInt64" or "System.Single" or "System.Double" or "System.Decimal" or "System.String" or
                "System.DateTime" or "System.DateTimeOffset" or "System.TimeSpan" or "System.Guid" or
                "System.Byte[]" => actualType,
                _ => "(other)"
            };
            var safePath = parserPath switch
            {
                "ParseXml / Guid.TryParse" or "ParseXml / InvariantInteger" or "ParseXml / TryXmlTime" or
                "ParseXml / PayloadField" or "ParseXml / IdentityField" or "ParseXml / SampledProfilePayload" or
                "ParseXml / ExactSampledProfilePayload" or "ParseXml / DuplicateSampledProfilePayload" or
                "ParseXml / ProviderAllowlist" or "ParseXml / CpuSampleDescriptor" or "ParseXml / EventSchema" or
                "ParseXml / CpuSampleSchema" => parserPath,
                "ParseXml / ThreadId format" => parserPath,
                _ when parserPath.StartsWith("ParseXml / ", StringComparison.Ordinal) &&
                    ObservedCpuSampleFields.Any(name => parserPath == "ParseXml / " + name + " format") => parserPath,
                _ => "ParseXml / other"
            };
            var safeRenderPath = renderPath is "Windows EvtRender XML" or "tracerpt XML" or "XML" ? renderPath : "XML";
            string? format = null, whitespace = null, providerStructure = null;
            if (reasonCode == "ThreadIdInvalid" && threadFormat is { } classified)
                (format, whitespace) = classified;
            if (reasonCode is "ProviderGuidMissing" or "ProviderGuidInvalid")
                providerStructure = ClassifyProviderGuidStructure(system);
            var key = new MalformedAggregateKey(provider, providerName, eventGuid, eventId, version, task, opcode,
                level, keywords, safeField, safeType, safePath, state, required, safeRenderPath, reasonCode,
                format, whitespace, providerStructure);
            if (_groups.TryGetValue(key, out var existing))
            {
                _groups[key] = existing with { Count = existing.Count + 1, LastSequence = sequence };
                return;
            }
            if (_groups.Count >= MaximumMalformedAggregateGroups)
            {
                var evicted = _groups.Where(group => state switch
                    {
                        "Malformed" => group.Value.State != "Malformed",
                        "UnknownSchema" => group.Value.State == "Skipped",
                        _ => false
                    })
                    .OrderBy(group => group.Value.State == "Skipped" ? 0 : 1)
                    .ThenBy(group => group.Value.Count)
                    .ThenBy(group => group.Value.FirstSequence)
                    .FirstOrDefault();
                if (evicted.Value is null)
                {
                    CountDropped(state, 1);
                    return;
                }
                _groups.Remove(evicted.Key);
                CountDropped(evicted.Value.State, evicted.Value.Count);
            }
            _groups.Add(key, new(provider, providerName, eventGuid, eventId, version, task, opcode, level, keywords,
                safeField, safeType, safePath, state, required, safeRenderPath, reasonCode, 1, sequence, sequence)
            {
                ThreadIdFormatClass = format, ThreadIdWhitespace = whitespace, ProviderGuidStructure = providerStructure
            });
        }
    }

    internal static async Task<GuardianTraceAnalysis> DecodeAsync(GuardianTraceResult trace, CancellationToken token,
        ProcessIdentity? preferredIdentity = null, Action<string, GuardianTraceAnalysis?>? progress = null,
        Action<GuardianTraceConversion>? conversionProgress = null, bool diagnoseEventTypes = false)
    {
        Validate(trace);
        EvidenceFile.RequireLocalDirectory(Path.GetDirectoryName(trace.Path)!);
        var marker = trace.Path + ".session";
        if (new FileInfo(marker).Length > 65536 || new FileInfo(marker).Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            new FileInfo(trace.Path).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Trace-Marker/ETL ungültig oder umgeleitet.");
        var stored = EvidenceFile.Decode<GuardianTraceResult>("guardian-trace-result", await File.ReadAllTextAsync(marker, token).ConfigureAwait(false));
        if (JsonSerializer.Serialize(trace, IntelligenceModel.Json) != JsonSerializer.Serialize(stored, IntelligenceModel.Json))
            throw new InvalidDataException("ETL besitzt nicht den ausgewählten eigenen Sessionvertrag.");
        // Keep the owned ETL immutable while Windows opens it for decoding.
        await using var lease = new FileStream(trace.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (lease.Length != trace.Bytes || await GuardianTrace.HashAsync(trace.Path, token).ConfigureAwait(false) != trace.Sha256)
            throw new InvalidDataException("Eigene ETL wurde nach Aufnahme verändert.");
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tracerpt.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Windows-ETL-Decoder tracerpt.exe fehlt.", executable);
        var conversionDirectory = Path.Combine(Path.GetDirectoryName(trace.Path)!, "AetherDecode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(conversionDirectory);
        var xml = Path.Combine(conversionDirectory, "events.xml");
        var evtx = Path.Combine(conversionDirectory, "events.evtx");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        stop.CancelAfter(TimeSpan.FromSeconds(30));
        var watch = Stopwatch.StartNew();
        try
        {
            progress?.Invoke("tracerpt XML conversion", null);
            await ConvertAsync("XML", xml).ConfigureAwait(false);
            await using var stream = new FileStream(xml, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            var original = ParseXml(stream, trace, stop.Token, preferredIdentity, renderPath: "tracerpt XML",
                diagnoseEventTypes: diagnoseEventTypes);
            original = original with { Decoder = original.Decoder! with { DecoderInvocationPassed = true } };
            progress?.Invoke("tracerpt XML parsed", original);
            // EvtRender supplies the Windows event timestamp, independently of tracerpt's XML offset formatting.
            progress?.Invoke("tracerpt EVTX conversion", null);
            await ConvertAsync("EVTX", evtx).ConfigureAwait(false);
            progress?.Invoke("Windows EvtRender", null);
            var rendered = Path.Combine(conversionDirectory, "rendered.xml");
            using (var events = new EventLogReader(new EventLogQuery(evtx, PathType.FilePath)))
            using (var output = XmlWriter.Create(rendered, new XmlWriterSettings { Encoding = System.Text.Encoding.UTF8 }))
            {
                output.WriteStartElement("Events");
                var count = 0;
                while (true)
                {
                    stop.Token.ThrowIfCancellationRequested();
                    using var entry = events.ReadEvent(TimeSpan.FromSeconds(2));
                    if (entry is null) break;
                    count++;
                    var value = entry.ToXml();
                    if (value.Length > MaximumXmlBytes) throw new InvalidDataException("Einzelnes EVTX-Ereignis überschreitet das XML-Budget.");
                    var document = XElement.Parse(value);
                    if (diagnoseEventTypes && entry.ProviderId is { } nativeProvider)
                        document.SetAttributeValue("AetherNativeProvider", nativeProvider.ToString("D"));
                    var payload = document.Descendants().Where(e => e.Name.LocalName == "Data").ToArray();
                    var properties = entry.Properties;
                    if (payload.Length == properties.Count)
                        for (var index = 0; index < payload.Length; index++)
                        {
                            var field = payload[index].Attribute("Name")?.Value;
                            if (!diagnoseEventTypes && field is not ("ProcessID" or "CreateTime" or "ExitTime")) continue;
                            var nativeValue = properties[index].Value;
                            payload[index].SetAttributeValue("AetherType", nativeValue?.GetType().FullName ?? "NULL");
                            if (field is not ("ProcessID" or "CreateTime" or "ExitTime")) continue;
                            if (nativeValue is DateTime { Kind: not DateTimeKind.Unspecified } date)
                                payload[index].SetAttributeValue("AetherTypedValue", date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                            else if (nativeValue is uint or int or ulong or long)
                                payload[index].SetAttributeValue("AetherTypedValue", Convert.ToString(nativeValue, CultureInfo.InvariantCulture));
                        }
                    var systemTime = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "TimeCreated");
                    if (systemTime is not null && entry.TimeCreated is { Kind: not DateTimeKind.Unspecified } eventTime)
                        systemTime.SetAttributeValue("AetherTypedValue", eventTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                    output.WriteRaw(document.ToString(SaveOptions.DisableFormatting));
                    output.Flush();
                    if (new FileInfo(rendered).Length > MaximumXmlBytes) throw new InvalidDataException("Gerendertes EVTX überschreitet 64 MiB.");
                    if (count > MaximumEvents) break;
                }
                output.WriteEndElement();
            }
            conversionProgress?.Invoke(new("RenderedXML", "Completed", new FileInfo(rendered).Length,
                watch.Elapsed.TotalMilliseconds, null, "EventLogReader und XmlWriter geschlossen; Größe vor Cleanup"));
            await using var nativeXml = new FileStream(rendered, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            var analysis = ParseXml(nativeXml, trace, stop.Token, preferredIdentity,
                incompleteSource: original.State != "DecodedObservedInterval", renderPath: "Windows EvtRender XML",
                diagnoseEventTypes: diagnoseEventTypes);
            analysis = ConfirmSources(trace, analysis, original, preferredIdentity);
            var samples = original.Facts.Where(f => f.Kind is "ProcessStart" or "ProcessEnd" && TryIdentity(f.Identity, out _)).Take(8).Select(f =>
            {
                var match = analysis.Facts.Where(n => n.Provider == f.Provider && n.EventId == f.EventId &&
                    n.Identity == f.Identity && TryIdentity(n.Identity, out _)).ToArray();
                return $"XML SystemTime={f.OriginalSystemTime}; Parsed={f.Time:O}; Identity={f.Identity}; EvtRender=" +
                    (match.Length == 1 ? match[0].Time.ToString("O", CultureInfo.InvariantCulture) : "nicht eindeutig") +
                    "; CreateTime=" + f.Data.GetValueOrDefault("CreateTime")?.Trim();
            }).ToArray();
            if (await GuardianTrace.HashAsync(trace.Path, token).ConfigureAwait(false) != trace.Sha256)
                throw new InvalidDataException("ETL-Identitätsprüfung nach Dekodierung fehlgeschlagen.");
            var decoded = analysis with
            {
                DecodeMilliseconds = watch.Elapsed.TotalMilliseconds,
                Limits = analysis.Limits.Concat(original.Limits).Distinct(StringComparer.Ordinal).ToArray(),
                Decoder = analysis.Decoder! with
                {
                    DecoderInvocationPassed = true,
                    TimeSource = "Windows tracerpt EVTX / Windows EvtRender SystemTime; keine eigene QPC- oder Offsetkorrektur",
                    TimeSamples = samples
                }
            };
            ValidateAnalysis(trace, decoded);
            progress?.Invoke("Completed", decoded);
            return decoded;
        }
        catch (EventLogException ex) { throw new InvalidDataException("Windows EvtRender / EventLogReader: " + ex.Message, ex); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && stop.IsCancellationRequested)
        { throw new TimeoutException("Windows-Decoderzeitlimit von insgesamt 30 Sekunden erreicht."); }
        finally
        {
            DeleteConversionDirectory(conversionDirectory);
            conversionProgress?.Invoke(new("Cleanup", "Removed", 0, watch.Elapsed.TotalMilliseconds, null,
                "Eigene Konverterdateien und eigenes Unterverzeichnis entfernt"));
        }
        async Task ConvertAsync(string format, string path)
        {
            await ConvertBoundedAsync(format, path, stop, ct => CommandRunner.RunAsync(executable,
                [trace.Path, "-o", path, "-of", format, "-y"], TimeSpan.FromSeconds(30), ct,
                closeInput: true, workingDirectory: conversionDirectory, processLifecycle: (pid, exited) =>
                    conversionProgress?.Invoke(new(format, exited ? "ProcessExited" : "ProcessStarted", 0,
                        watch.Elapsed.TotalMilliseconds, null, "Eigener Konverter PID=" + pid))),
                conversionProgress).ConfigureAwait(false);
        }
    }
    internal static void DeleteConversionDirectory(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
    }
    internal static async Task ConvertBoundedAsync(string format, string path, CancellationTokenSource stop,
        Func<CancellationToken, Task<CommandResult>> run, Action<GuardianTraceConversion>? report = null)
    {
        var clock = Stopwatch.StartNew(); long observed = 0; var exceeded = false; int? exit = null;
        var state = "Failed";
        long Observe()
        {
            if (File.Exists(path)) observed = Math.Max(observed, new FileInfo(path).Length);
            return observed;
        }
        try
        {
            var conversion = run(stop.Token);
            while (!conversion.IsCompleted)
            {
                if (Observe() > MaximumXmlBytes)
                {
                    if (!exceeded) report?.Invoke(new(format, "BudgetExceeded", observed, clock.Elapsed.TotalMilliseconds, null,
                        "64MiB-Schutzgrenze überschritten; eigenen Konverter abbrechen und geordnet abwarten"));
                    exceeded = true; stop.Cancel();
                }
                await Task.WhenAny(conversion, Task.Delay(25, CancellationToken.None)).ConfigureAwait(false);
            }
            if (Observe() > MaximumXmlBytes && !exceeded)
            {
                exceeded = true;
                report?.Invoke(new(format, "BudgetExceeded", observed, clock.Elapsed.TotalMilliseconds, null,
                    "64MiB-Schutzgrenze bei abschließender Größenprüfung überschritten"));
            }
            CommandResult result;
            try { result = await conversion.ConfigureAwait(false); }
            catch (OperationCanceledException) when (exceeded)
            { throw new IOException("Windows-" + format + "-Ausgabe überschreitet 64 MiB; Konverter beendet."); }
            exit = result.ExitCode;
            if (Observe() > MaximumXmlBytes || exceeded)
                throw new InvalidDataException("Windows-" + format + "-Ausgabe überschreitet 64 MiB.");
            if (result.ExitCode != 0 || result.OutputTruncated)
                throw new InvalidDataException("Windows-" + format + "-Konvertierung fehlgeschlagen: Exit " + result.ExitCode + ".");
            state = "Completed";
        }
        finally
        {
            Observe();
            report?.Invoke(new(format, state, observed, clock.Elapsed.TotalMilliseconds, exit,
                exit is null ? "Kein normaler Konverter-Exit geliefert; nach Abbruch vollständig abgewartet" :
                "Beobachtete Größe, kein kontinuierlicher Peaknachweis"));
        }
    }
    private static bool IsObservedCpuSampleDescriptor(GuardianTraceResult trace, Guid provider, XElement element, XElement? system)
    {
        if (provider != ObservedCpuSampleProvider || !trace.Components.Contains("cpu", StringComparer.Ordinal))
            return false;
        var eventGuids = element.Elements().Where(e => e.Name.LocalName == "ExtendedTracingInfo")
            .SelectMany(e => e.Elements().Where(child => child.Name.LocalName == "EventGuid")).ToArray();
        if (eventGuids.Length != 1 || !Guid.TryParse(eventGuids[0].Value, out var eventGuid) ||
            eventGuid != ObservedCpuSampleEventGuid)
            return false;
        return IsObservedCpuSampleEventDescriptor(system);
    }
    private static bool IsObservedCpuSampleEventDescriptor(XElement? system)
    {
        string? Value(string node) => system?.Elements().FirstOrDefault(e => e.Name.LocalName == node)?.Value;
        return int.TryParse(Value("EventID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var eventId) && eventId == 0 &&
            int.TryParse(Value("Version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) && version == 2 &&
            int.TryParse(Value("Task"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var task) && task == 0 &&
            int.TryParse(Value("Opcode"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var opcode) && opcode == 46 &&
            int.TryParse(Value("Level"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) && level == 0 &&
            TryEventKeywords(Value("Keywords"), out var keywords) && keywords == 0;
    }
    private static string GetRejectedProviderReason(GuardianTraceResult trace, Guid provider, XElement element, XElement? system)
    {
        if (provider != ObservedCpuSampleProvider || !trace.Components.Contains("cpu", StringComparer.Ordinal))
            return "ProviderNotAllowed";
        var eventGuids = element.Elements().Where(e => e.Name.LocalName == "ExtendedTracingInfo")
            .SelectMany(e => e.Elements().Where(child => child.Name.LocalName == "EventGuid")).ToArray();
        if (eventGuids.Length != 1 || !Guid.TryParse(eventGuids[0].Value, out var eventGuid) ||
            eventGuid != ObservedCpuSampleEventGuid)
            return "EventGuidMismatch";
        return IsObservedCpuSampleEventDescriptor(system) ? "ProviderNotAllowed" : "DescriptorMismatch";
    }
    private static bool TryEventKeywords(string? value, out ulong keywords)
    {
        var text = value?.Trim();
        if (text?.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true) text = text[2..];
        return ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out keywords);
    }
    private static bool IsObservedCpuSampleFact(GuardianTraceResult trace, GuardianTraceFact fact)
        => GetObservedCpuSampleFactRejectionReason(trace, fact) is null;
    private static string? GetObservedCpuSampleFactRejectionReason(GuardianTraceResult trace, GuardianTraceFact fact)
    {
        if (!trace.Components.Contains("cpu", StringComparer.Ordinal) || fact.Provider != ObservedCpuSampleProvider)
            return "ProviderMismatch";
        if (fact.EventGuid != ObservedCpuSampleEventGuid) return "EventGuidMismatch";
        if (fact.EventId != 0 || fact.Version != 2 || fact.Task != 0 || fact.Opcode != "46" ||
            fact.Level != 0 || fact.Keywords != 0)
            return "DescriptorMismatch";
        if (fact.PayloadFields is null || fact.PayloadTypes is null ||
            fact.PayloadFields.Count != ObservedCpuSampleFields.Length ||
            ObservedCpuSampleFields.Any(field => fact.PayloadFields.Count(name =>
                name.Equals(field, StringComparison.OrdinalIgnoreCase)) != 1))
            return "PayloadSchemaMismatch";
        if (fact.Kind != "CpuSample") return "KindMismatch";
        var thread = fact.Data.FirstOrDefault(p => p.Key.Equals("ThreadId", StringComparison.OrdinalIgnoreCase));
        if (thread.Key is null) return "ThreadIdMissing";
        return TryObservedCpuSampleThreadId(thread.Value, out _) ? null : "ThreadIdInvalid";
    }
    private static bool TryObservedCpuSampleThreadId(string? value, out uint threadId)
    {
        threadId = 0;
        return value is not null && uint.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out threadId) &&
            threadId > 0;
    }
    internal static (string Format, string Whitespace) ClassifyThreadIdFormat(string? value)
    {
        if (value is null || value.Length == 0) return ("Empty", "None");
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return ("WhitespaceOnly", "None");
        var whitespace = new List<string>(3);
        if (char.IsWhiteSpace(value[0])) whitespace.Add("LeadingWhitespace");
        if (char.IsWhiteSpace(value[^1])) whitespace.Add("TrailingWhitespace");
        if (trimmed.Any(char.IsWhiteSpace)) whitespace.Add("EmbeddedWhitespace");
        var whitespaceClass = whitespace.Count == 0 ? "None" : string.Join("+", whitespace);
        if (trimmed.Any(char.IsWhiteSpace)) return ("Other", whitespaceClass);
        static bool Digits(string text) => text.Length > 0 && text.All(c => c is >= '0' and <= '9');
        static bool Hex(string text) => text.Length > 0 && text.All(Uri.IsHexDigit);
        if (Digits(trimmed))
            return (uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number == 0 ? "DecimalZero" : "DecimalPositive" : "UIntOverflow", whitespaceClass);
        if (trimmed.Length > 1 && trimmed[0] is '+' or '-' && Digits(trimmed[1..]))
            return (trimmed[0] == '+' ? "SignedPositive" : "SignedNegative", whitespaceClass);
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && Hex(trimmed[2..]))
            return ("HexPrefixed", whitespaceClass);
        if (Hex(trimmed)) return ("HexWithoutPrefix", whitespaceClass);
        return (trimmed.Any(char.IsLetter) ? "NonNumeric" : "Other", whitespaceClass);
    }
    private static string ClassifyProviderGuidStructure(XElement? system)
    {
        var provider = system?.Elements().FirstOrDefault(e => e.Name.LocalName == "Provider");
        if (provider is null) return "ProviderElementMissing";
        var attribute = provider.Attribute("Guid");
        if (attribute is null) return "ProviderGuidAttributeMissing";
        return string.IsNullOrWhiteSpace(attribute.Value) ? "ProviderGuidAttributeEmpty" : "ProviderGuidSyntaxInvalid";
    }
    private static string DiagnosticToken(string? value, string fallback)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 ||
            value.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '.' or ':' or '-')))
            return fallback;
        return value;
    }
    private static (IReadOnlyList<string> Fields, IReadOnlyList<string> Types) GetObservedCpuPayloadMetadata(XElement element)
    {
        var payload = element.Elements().Where(e => e.Name.LocalName is "EventData" or "UserData")
            .SelectMany(e => e.Descendants()).Where(e => !e.HasElements).Take(64).ToArray();
        var fields = payload.Select(item => DiagnosticToken(item.Attribute("Name")?.Value ?? item.Name.LocalName, "(other)")).ToArray();
        var types = payload.Select(item => DiagnosticToken(item.Attribute("AetherType")?.Value ??
            item.Attributes().FirstOrDefault(a => a.Name.LocalName == "type")?.Value ??
            (item.Attributes().Any(a => a.Name.LocalName == "nil" && a.Value == "true")
                ? "NULL" : "XmlString"), "(other)")).ToArray();
        return (fields, types);
    }
    private static GuardianTraceRejectionEvidence CreateRejectionEvidence(GuardianTraceFact fact, string reason)
    {
        var fields = fact.PayloadFields?.Take(64).Select(name => DiagnosticToken(name, "(other)")).ToArray() ?? [];
        var types = fact.PayloadTypes?.Take(64).Select(type => DiagnosticToken(type, "(other)")).ToArray() ?? [];
        var opcode = int.TryParse(fact.Opcode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOpcode)
            ? parsedOpcode.ToString(CultureInfo.InvariantCulture) : "(other)";
        return new(fact.Sequence, fact.Provider, fact.EventGuid, fact.EventId, fact.Version, fact.Task, opcode,
            fact.Level, fact.Keywords, fact.Kind is "CpuSample" or "Unknown" ? fact.Kind : "(other)",
            fact.Availability is "Available" or "Unknown" ? fact.Availability : "(other)",
            fact.RenderPath is "Windows EvtRender XML" or "tracerpt XML" or "XML" ? fact.RenderPath : "(other)",
            fact.OriginalSystemTime is not null,
            fact.HeaderPid is not null || fact.Data.Keys.Any(k =>
                k.Equals("ProcessID", StringComparison.OrdinalIgnoreCase) || k.Equals("ProcessId", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("PID", StringComparison.OrdinalIgnoreCase)),
            fact.HeaderTid is not null || fact.Data.Keys.Any(k =>
                k.Equals("ThreadID", StringComparison.OrdinalIgnoreCase) || k.Equals("ThreadId", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("TID", StringComparison.OrdinalIgnoreCase)),
            fields, types, reason);
    }
    private static bool ValidateObservedCpuSamplePayload(XElement element, int sequence, Guid provider, string renderPath,
        List<GuardianTraceFieldIssue> issues, Action<string, string, string, bool, string> recordMalformed)
    {
        var payload = element.Elements().Where(e => e.Name.LocalName is "EventData" or "UserData")
            .SelectMany(e => e.Descendants()).Where(e => !e.HasElements).ToArray();
        string Name(XElement item) => item.Attribute("Name")?.Value ?? item.Name.LocalName;
        string Type(XElement item)
        {
            var value = item.Attribute("AetherType")?.Value ??
            item.Attributes().FirstOrDefault(a => a.Name.LocalName == "type")?.Value ??
            (item.Attributes().Any(a => a.Name.LocalName == "nil" && a.Value == "true")
                ? "NULL" : "XML string (kein typisierter Variantwert)");
            return value[..Math.Min(value.Length, 256)];
        }
        var valid = true;
        void Issue(string field, string actualType, bool required, string parser)
        {
            valid = false;
            var reason = parser switch
            {
                "SampledProfilePayload" => "PayloadFieldMissing",
                "DuplicateSampledProfilePayload" => "PayloadFieldDuplicate",
                "ExactSampledProfilePayload" => "PayloadFieldUnexpected",
                _ => field switch
                {
                    "InstructionPointer" => "InstructionPointerInvalid",
                    "ThreadId" => "ThreadIdInvalid",
                    "Count" => "CountInvalid",
                    "Reserved" => "ReservedInvalid",
                    _ => "PayloadFieldInvalid"
                }
            };
            recordMalformed(field, actualType, "ParseXml / " + parser, required, reason);
            if (issues.Count < 32)
                issues.Add(new(sequence, provider, 0, 2, "46", field, actualType, null, renderPath,
                    "ParseXml / " + parser, "Malformed", required, "Feld verletzt das beobachtete CPU-Sample-Schema"));
        }
        foreach (var field in ObservedCpuSampleFields)
        {
            var matches = payload.Where(item => Name(item).Equals(field, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
            {
                Issue(field, matches.Length == 0 ? "Missing" : "Duplicate", true,
                    matches.Length == 0 ? "SampledProfilePayload" : "DuplicateSampledProfilePayload");
                continue;
            }
            var item = matches[0];
            var text = item.Value.Trim();
            var fieldValid = field switch
            {
                "InstructionPointer" => TryHexUInt64(text),
                "ThreadId" => TryObservedCpuSampleThreadId(text, out _),
                "Count" => uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0,
                "Reserved" => uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _),
                _ => false
            };
            if (!fieldValid) Issue(field, Type(item), true, field + " format");
        }
        foreach (var item in payload.Where(item => !ObservedCpuSampleFields.Contains(Name(item), StringComparer.OrdinalIgnoreCase)))
            Issue(Name(item)[..Math.Min(Name(item).Length, 128)], Type(item), false, "ExactSampledProfilePayload");
        return valid;
    }
    private static bool TryHexUInt64(string value)
    {
        var text = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return text.Length is > 0 and <= 16 &&
            ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _);
    }
    internal static void Validate(GuardianTraceResult trace)
    {
        if (trace is null || trace.Profile is null || trace.Components is null || trace.Path is null || trace.Sha256 is null)
            throw new InvalidDataException("ETL-Ergebnisvertrag fehlt.");
        var profile = GuardianTraceProfiles.Get(trace.Profile);
        GuardianTraceProfiles.Resolve(profile, trace.Components);
        if (!GuardianTrace.Owns(trace.Id, trace.Id, trace.Session) || !Path.IsPathFullyQualified(trace.Path) ||
            Path.GetFileName(trace.Path) != trace.Id.ToString("N") + ".etl" ||
            trace.State is not ("Completed" or "Cancelled" or "Incomplete") || trace.Started == default || trace.Ended == default ||
            !double.IsFinite(trace.ElapsedMilliseconds) || trace.ElapsedMilliseconds < 0 ||
            trace.Bytes is <= 0 || trace.Bytes > profile.MaximumMiB * 1024L * 1024 ||
            trace.QpcStart <= 0 || trace.QpcEnd < trace.QpcStart || trace.QpcFrequency <= 0 ||
            trace.Sha256.Length != 64 || trace.Sha256.Any(c => !Uri.IsHexDigit(c)) || string.IsNullOrWhiteSpace(trace.Host))
            throw new InvalidDataException("Ungültiger eigener ETL-Ergebnisvertrag; ungeprüfte oder alte Rohdatei nicht importiert.");
    }
    internal static GuardianTraceAnalysis ParseXml(Stream stream, GuardianTraceResult trace, CancellationToken token,
        ProcessIdentity? preferredIdentity = null, bool incompleteSource = false, string renderPath = "XML",
        bool diagnoseEventTypes = false)
    {
        Validate(trace);
        if (stream.CanSeek && stream.Length > MaximumXmlBytes) throw new InvalidDataException("Dekodierte ETL zu groß.");
        var providers = GuardianTraceProfiles.Resolve(GuardianTraceProfiles.Get(trace.Profile), trace.Components);
        var allowed = providers.SelectMany(p => p.EventProviders).ToHashSet();
        var selected = new PriorityQueue<GuardianTraceFact, (int Priority, int Order)>();
        var limits = new List<string>(); var seenKinds = new HashSet<string>();
        var skipped = new HashSet<Guid>();
        var providerEvents = new Dictionary<string, int>();
        var eventTypes = new Dictionary<string, GuardianTraceEventType>();
        var eventTypeDiagnosticsDropped = 0;
        var issues = new List<GuardianTraceFieldIssue>(); var missing = 0;
        var malformedAggregates = new MalformedAggregateAccumulator();
        var ownXml = new List<GuardianTraceXmlEvidence>();
        var liveOwnPids = new HashSet<int>(); var ownThreads = new HashSet<int>();
        var unknown = 0; var rejected = 0; var malformed = 0; var total = 0;
        var factLimit = false; var eventLimit = false;
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlBytes,
            IgnoreWhitespace = false,
            CloseInput = false
        });
        reader.MoveToContent();
        if (reader.LocalName != "Events") throw new InvalidDataException("Windows-ETL-XML besitzt keinen Events-Wurzelknoten.");
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Event") continue;
            if (total <= MaximumEvents) total++;
            if (total > MaximumEvents) { eventLimit = true; continue; }
            using var subtree = reader.ReadSubtree();
            var element = XElement.Load(subtree, LoadOptions.None);
            var system = element.Elements().SingleOrDefault(e => e.Name.LocalName == "System");
            (string Format, string Whitespace)? threadFormat = null;
            if (Guid.TryParse(system?.Elements().FirstOrDefault(e => e.Name.LocalName == "Provider")?.Attribute("Guid")?.Value,
                out var shapeProvider) && IsObservedCpuSampleDescriptor(trace, shapeProvider, element, system))
            {
                var threads = element.Elements().Where(e => e.Name.LocalName is "EventData" or "UserData")
                    .SelectMany(e => e.Descendants()).Where(e => !e.HasElements &&
                        (e.Attribute("Name")?.Value ?? e.Name.LocalName).Equals("ThreadId", StringComparison.OrdinalIgnoreCase))
                    .Take(2).ToArray();
                if (threads.Length == 1) threadFormat = ClassifyThreadIdFormat(threads[0].Value);
            }
            // Restore the previous IgnoreWhitespace behavior after recording the bounded shape.
            foreach (var text in element.DescendantNodes().OfType<XText>().Where(t => t is not XCData &&
                t.Value.All(c => c is ' ' or '\t' or '\r' or '\n') &&
                t.Parent?.AncestorsAndSelf().Select(e => (string?)e.Attribute(XNamespace.Xml + "space"))
                    .FirstOrDefault(value => value is not null) != "preserve").ToArray())
                text.Remove();
            string? Attribute(string node, string key) => system?.Elements().FirstOrDefault(e => e.Name.LocalName == node)?.Attribute(key)?.Value;
            string? Value(string node) => system?.Elements().FirstOrDefault(e => e.Name.LocalName == node)?.Value;
            void RecordMalformed(string field, string actualType, string parserPath, bool required, string reasonCode)
            {
                malformed++;
                malformedAggregates.Add(element, system, renderPath, total, field, actualType, parserPath, required, reasonCode,
                    threadFormat: threadFormat);
            }
            string? diagnosticKey = null;
            if (diagnoseEventTypes)
            {
                string Bound(string? text) => text is null ? "" : text[..Math.Min(text.Length, 128)];
                var rawProvider = Attribute("Provider", "Guid") ?? "(missing)";
                var name = Attribute("Provider", "Name");
                var validProvider = Guid.TryParse(rawProvider, out var diagnosticProvider);
                var key = string.Join("|", Bound(rawProvider), Bound(name), Bound(Value("EventID")), Bound(Value("Version")),
                    Bound(Value("Opcode")), Bound(Value("Keywords")), Bound(Value("Level")));
                if (!validProvider) key += "|sequence=" + total;
                diagnosticKey = key;
                if (eventTypes.TryGetValue(key, out var previous)) eventTypes[key] = previous with { Count = previous.Count + 1 };
                else if (eventTypes.Count < 64 && (!validProvider ||
                    eventTypes.Values.Count(t => !t.State.StartsWith("Malformed", StringComparison.Ordinal)) < 56))
                {
                    var fields = element.Elements().Where(e => e.Name.LocalName is "EventData" or "UserData")
                        .SelectMany(e => e.Descendants()).Where(e => !e.HasElements)
                        .Select(e => e.Attribute("Name")?.Value ?? e.Name.LocalName).Distinct(StringComparer.Ordinal)
                        .Take(64).Select(Bound).ToArray();
                    var xmlEvidence = !validProvider || !allowed.Contains(diagnosticProvider);
                    var raw = xmlEvidence && eventTypes.Values.Count(t => t.Xml is not null &&
                        t.State.StartsWith("Malformed", StringComparison.Ordinal) == !validProvider) < 8 ?
                        element.ToString(SaveOptions.DisableFormatting) : null;
                    eventTypes[key] = new(Bound(rawProvider), Bound(name), Bound(element.Attribute("AetherNativeProvider")?.Value),
                        Bound(Value("EventID")), Bound(Value("Version")), Bound(Value("Opcode")), Bound(Value("Keywords")),
                        Bound(Value("Level")), Bound(Attribute("TimeCreated", "SystemTime")), renderPath,
                        !validProvider ? "Malformed: missing/invalid Provider.Guid" :
                        diagnosticProvider == HeaderProvider ? "NotApplicable: ETL header" :
                        allowed.Contains(diagnosticProvider) ? "Allowed: schema/fields checked separately" : "Skipped: outside allowlist",
                        total, 1, fields, raw is { Length: <= 8192 } ? raw : null);
                }
                else eventTypeDiagnosticsDropped++;
            }
            if (!Guid.TryParse(Attribute("Provider", "Guid"), out var provider))
            {
                var rawProvider = Attribute("Provider", "Guid");
                RecordMalformed("Provider.Guid", "XML attribute", "ParseXml / Guid.TryParse", true,
                    string.IsNullOrWhiteSpace(rawProvider) ? "ProviderGuidMissing" : "ProviderGuidInvalid");
                if (issues.Count < 32) issues.Add(new(total, Guid.Empty,
                    int.TryParse(Value("EventID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var invalidId) ? invalidId : -1,
                    int.TryParse(Value("Version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var invalidVersion) ? invalidVersion : -1,
                    Value("Opcode") is { } invalidOpcode ? invalidOpcode[..Math.Min(invalidOpcode.Length, 128)] : "",
                    "Provider.Guid", "XML attribute",
                    Attribute("Provider", "Guid") is { } raw ? raw[..Math.Min(raw.Length, 4096)] : null,
                    renderPath, "ParseXml / Guid.TryParse", "Malformed", true, "Ungültige Provider-GUID"));
                continue;
            }
            var providerKey = provider.ToString("D");
            if (providerEvents.ContainsKey(providerKey) || providerEvents.Count < 16)
                providerEvents[providerKey] = providerEvents.GetValueOrDefault(providerKey) + 1;
            if (provider == HeaderProvider) continue;
            var observedCpuSampleDescriptor = IsObservedCpuSampleDescriptor(trace, provider, element, system);
            if (!allowed.Contains(provider) && !observedCpuSampleDescriptor)
            {
                var reason = GetRejectedProviderReason(trace, provider, element, system);
                var field = reason switch
                {
                    "EventGuidMismatch" => "ExtendedTracingInfo.EventGuid",
                    "DescriptorMismatch" => "EventDescriptor",
                    _ => "Provider.Guid"
                };
                var type = reason == "EventGuidMismatch" ? "XML element" : reason == "DescriptorMismatch" ? "XML element" : "XML attribute";
                malformedAggregates.Add(element, system, renderPath, total, field, type,
                    reason == "ProviderNotAllowed" ? "ParseXml / ProviderAllowlist" : "ParseXml / CpuSampleDescriptor",
                    true, reason, "Skipped");
                rejected++;
                if (skipped.Count < 16) skipped.Add(provider);
                continue;
            }
            var originalTime = Attribute("TimeCreated", "SystemTime");
            if (!int.TryParse(Value("EventID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var eventId))
            {
                RecordMalformed("EventID", "XML element", "ParseXml / InvariantInteger", true, "EventIdInvalid");
                if (issues.Count < 32) issues.Add(new(total, provider, -1, -1, Value("Opcode") ?? "", "EventID", "XML element",
                    Value("EventID") is { } raw ? raw[..Math.Min(raw.Length, 4096)] : null,
                    renderPath, "ParseXml / InvariantInteger", "Malformed", true, "Ungültige Event-ID"));
                continue;
            }
            if (!TryXmlTime(originalTime, out var time))
            {
                RecordMalformed("System.TimeCreated", "XML attribute", "ParseXml / TryXmlTime", true, "SystemTimeInvalid");
                if (issues.Count < 32) issues.Add(new(total, provider, eventId, -1, Value("Opcode") ?? "", "System.TimeCreated",
                    "XML attribute", originalTime is null ? null : originalTime[..Math.Min(originalTime.Length, 4096)],
                    renderPath, "ParseXml / TryXmlTime", "Malformed", true, "Expliziter gültiger XML-Zeitoffset erforderlich"));
                continue;
            }
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var typed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Attribute("TimeCreated", "AetherTypedValue") is { } systemValue) typed["SystemTime"] = systemValue;
            foreach (var item in element.Elements().Where(e => e.Name.LocalName is "EventData" or "UserData").SelectMany(e => e.Descendants()))
            {
                var name = item.Attribute("Name")?.Value ?? item.Name.LocalName;
                if (!Fields.Contains(name) || item.HasElements) continue;
                var value = observedCpuSampleDescriptor && name.Equals("ThreadId", StringComparison.OrdinalIgnoreCase)
                    ? item.Value.Trim() : item.Value;
                if (value.Length > 4096)
                {
                    var type = item.Attribute("AetherType")?.Value ?? item.Attributes().FirstOrDefault(a => a.Name.LocalName == "type")?.Value ??
                        (item.Attributes().Any(a => a.Name.LocalName == "nil" && a.Value == "true") ? "NULL" : "XML string (kein typisierter Variantwert)");
                    RecordMalformed(name, type, "ParseXml / PayloadField", true, "PayloadValueTooLong");
                    continue;
                }
                if (!data.TryAdd(name, value))
                {
                    var type = item.Attribute("AetherType")?.Value ?? item.Attributes().FirstOrDefault(a => a.Name.LocalName == "type")?.Value ??
                        (item.Attributes().Any(a => a.Name.LocalName == "nil" && a.Value == "true") ? "NULL" : "XML string (kein typisierter Variantwert)");
                    RecordMalformed(name, type, "ParseXml / PayloadField", true, "PayloadFieldDuplicate");
                    continue;
                }
                types[name] = item.Attribute("AetherType")?.Value ?? item.Attributes().FirstOrDefault(a => a.Name.LocalName == "type")?.Value ??
                    (item.Attributes().Any(a => a.Name.LocalName == "nil" && a.Value == "true") ? "NULL" : "XML string (kein typisierter Variantwert)");
                if (item.Attribute("AetherTypedValue")?.Value is { Length: <= 128 } typedValue) typed[name] = typedValue;
            }
            int? Number(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= 0 ? number : null;
            var observedCpuPayloadValid = !observedCpuSampleDescriptor ||
                ValidateObservedCpuSamplePayload(element, total, provider, renderPath, issues, RecordMalformed);
            var kind = provider == GuardianTrace.ProcessProvider ? eventId switch
            { 1 => "ProcessStart", 2 => "ProcessEnd", 3 => "ThreadStart", 4 => "ThreadEnd", 5 => "ImageLoad", 6 => "ImageUnload", _ => "Unknown" } :
                provider == GuardianTraceProfiles.Providers.Single(p => p.Id == "file").Guid ? "FileIO" :
                provider == GuardianTraceProfiles.Providers.Single(p => p.Id == "disk").Guid ? "DiskIO" :
                provider == GuardianTraceProfiles.Providers.Single(p => p.Id == "network").Guid ? "NetworkMetadata" :
                (provider == ManifestCpuSampleProvider && Value("Opcode") == "46" ||
                 observedCpuSampleDescriptor && observedCpuPayloadValid) ? "CpuSample" : "Unknown";
            var version = Number(Value("Version")) ?? -1;
            if (provider == GuardianTrace.ProcessProvider && (version < 0 ||
                version > (eventId == 1 ? 4 : eventId == 2 ? 2 : eventId is 3 or 4 ? 1 : 0))) kind = "Unknown";
            if (kind == "CpuSample" && version != 2) kind = "Unknown";
            if (diagnosticKey is not null && eventTypes.TryGetValue(diagnosticKey, out var diagnosticType))
                eventTypes[diagnosticKey] = diagnosticType with
                { State = kind == "Unknown" ? "Skipped: unsupported event/schema/opcode/version" : "Decoded schema: " + kind };
            if (kind == "Unknown")
            {
                unknown++;
                var invalidCpuPayload = observedCpuSampleDescriptor && !observedCpuPayloadValid;
                malformedAggregates.Add(element, system, renderPath, total, invalidCpuPayload ? "PayloadSchema" : "EventSchema",
                    "XML element", invalidCpuPayload ? "ParseXml / CpuSampleSchema" : "ParseXml / EventSchema",
                    true, invalidCpuPayload ? "PayloadSchemaInvalid" : "UnsupportedSchema", "UnknownSchema");
            }
            if (observedCpuSampleDescriptor && !observedCpuPayloadValid) continue;
            var identity = "Unbestätigt: PID allein ist keine dauerhafte Prozessidentität";
            var identityFieldsValid = true;
            if (provider == GuardianTrace.ProcessProvider && kind is "ProcessStart" or "ProcessEnd" &&
                Number(data.GetValueOrDefault("ProcessID")) is > 0 and var pid && TryCreation(data.GetValueOrDefault("CreateTime"), out var created))
                identity = new ProcessIdentity(pid, created.UtcTicks).ToString();
            if (provider == GuardianTrace.ProcessProvider && kind is "ProcessStart" or "ProcessEnd")
            {
                Field("ProcessID", data.GetValueOrDefault("ProcessID"), value => Number(value) is > 0, true);
                Field("CreateTime", data.GetValueOrDefault("CreateTime"), value => TryCreation(value, out _), true);
                if (kind == "ProcessEnd") Field("ExitTime", data.GetValueOrDefault("ExitTime"), value => TryCreation(value, out _), false);
            }
            void Field(string field, string? raw, Func<string?, bool> valid, bool required)
            {
                var absent = string.IsNullOrWhiteSpace(raw) || types.GetValueOrDefault(field) == "NULL" || raw.Trim() is "NULL" or "(null)";
                var normalized = field is "CreateTime" or "ExitTime" ? NormalizeRenderedFileTime(raw, renderPath) : raw;
                var type = types.GetValueOrDefault(field);
                var typedWrong = type?.StartsWith("System.", StringComparison.Ordinal) == true &&
                    (field == "ProcessID" ? type is not ("System.UInt32" or "System.Int32" or "System.String") :
                        type is not ("System.DateTime" or "System.UInt64" or "System.Int64" or "System.String"));
                var good = !absent && !typedWrong && valid(normalized);
                if (!good && required) identityFieldsValid = false;
                if (!good)
                {
                    if (absent) missing += required ? 1 : 0;
                    else RecordMalformed(field, type ?? "Missing", "ParseXml / IdentityField", required, "IdentityFieldInvalid");
                }
                if (issues.Count < 32 && (!good || raw != normalized))
                    issues.Add(new(total, provider, eventId, version, Value("Opcode") ?? "", field,
                        types.GetValueOrDefault(field) ?? "Missing", raw, renderPath,
                        "ParseXml / " + (field == "ProcessID" ? "Number(InvariantInteger)" : "NormalizeRenderedFileTime / TryCreation(XmlConvert|FILETIME)"),
                        absent ? required ? "Missing" : "NotApplicable" : good ? "Available" : "Malformed", required,
                        absent || good ? null : "FormatException: Wert verletzt den Feldvertrag"));
            }
            if (!identityFieldsValid) identity = "Unbestätigt: Pflichtfeld der Prozessidentität fehlt oder ist fehlerhaft";
            if (identityFieldsValid && identity.StartsWith("Unbestätigt", StringComparison.Ordinal) && provider == GuardianTrace.ProcessProvider &&
                kind is "ProcessStart" or "ProcessEnd" && Number(data.GetValueOrDefault("ProcessID")) is > 0 and var renderedPid &&
                TryCreation(NormalizeRenderedFileTime(data.GetValueOrDefault("CreateTime"), renderPath), out var renderedCreated))
                identity = new ProcessIdentity(renderedPid, renderedCreated.UtcTicks).ToString();
            IReadOnlyList<string> payloadFields = [];
            IReadOnlyList<string> payloadTypes = [];
            if (observedCpuSampleDescriptor)
                (payloadFields, payloadTypes) = GetObservedCpuPayloadMetadata(element);
            var fact = new GuardianTraceFact(total, provider, eventId, version, Value("Opcode") ?? "", time,
                Number(Attribute("Execution", "ProcessID")), Number(Attribute("Execution", "ThreadID")), kind, data, identity,
                kind == "Unknown" || !identityFieldsValid ? "Unknown" : "Available")
            {
                OriginalSystemTime = originalTime,
                RenderPath = renderPath,
                TypedValues = typed,
                Task = Number(Value("Task")),
                Level = Number(Value("Level")),
                Keywords = TryEventKeywords(Value("Keywords"), out var parsedKeywords) ? parsedKeywords : null,
                EventGuid = observedCpuSampleDescriptor ? ObservedCpuSampleEventGuid : null,
                PayloadFields = payloadFields,
                PayloadTypes = payloadTypes
            };
            // Retention preference never establishes identity: only the provider's PID + CreateTime does.
            var own = preferredIdentity is { IsKnown: true } preferred && identity == preferred.ToString();
            if (own && kind is "ProcessStart" or "ProcessEnd" && ownXml.Count < 4)
            {
                var rawXml = element.ToString(SaveOptions.DisableFormatting);
                ownXml.Add(new(total, identity, renderPath, rawXml.Length <= 16384 ? rawXml : "", rawXml.Length > 16384));
            }
            var payloadPid = Number(data.GetValueOrDefault("ProcessID")) ?? Number(data.GetValueOrDefault("PID"));
            var payloadTid = Number(data.GetValueOrDefault("ThreadID")) ?? Number(data.GetValueOrDefault("IssuingThreadId"));
            if (kind == "ProcessStart" && payloadPid is { } startPid)
            {
                liveOwnPids.Remove(startPid);
                if (preferredIdentity?.Pid == startPid) ownThreads.Clear();
                if (own) liveOwnPids.Add(startPid);
            }
            if (kind == "ThreadStart" && payloadTid is { } reusedTid) ownThreads.Remove(reusedTid);
            var ownContext = payloadPid is { } owner && liveOwnPids.Contains(owner) ||
                payloadPid is null && payloadTid is { } thread && ownThreads.Contains(thread);
            if (kind == "ThreadStart" && ownContext && payloadTid is { } tid) ownThreads.Add(tid);
            // These are retention classes only, never PID-only identity assignments.
            var priority = own && kind == "ProcessStart" ? 6 : own && kind == "ProcessEnd" ? 5 :
                ownContext && kind is "ThreadStart" or "ThreadEnd" or "ImageLoad" or "ImageUnload" ? 4 :
                ownContext && kind is "FileIO" or "DiskIO" or "NetworkMetadata" or "CpuSample" ? 3 :
                kind is "ProcessStart" or "ProcessEnd" ? 2 : kind != "Unknown" && seenKinds.Add(kind) ? 1 : 0;
            selected.Enqueue(fact, (priority, -total));
            if (selected.Count > MaximumFacts) { selected.Dequeue(); factLimit = true; }
            if (kind == "ProcessEnd" && payloadPid is { } endedPid) liveOwnPids.Remove(endedPid);
            if (kind == "ThreadEnd" && payloadTid is { } endedTid) ownThreads.Remove(endedTid);
        }
        var facts = selected.UnorderedItems.Select(p => p.Element).OrderBy(f => f.Sequence).ToArray();
        var capped = factLimit || eventLimit;
        if (trace.LostEvents != 0 || trace.LostBuffers != 0) limits.Add($"Verluste: Events {trace.LostEvents}, Buffer {trace.LostBuffers}.");
        if (trace.State != "Completed") limits.Add("Aufnahme nicht vollständig abgeschlossen: " + trace.State);
        if (capped) limits.Add("Auswertung begrenzt: höchstens 10.000 Ereignisse / 2.000 Fakten; keine Vollständigkeit.");
        if (unknown > 0 || rejected > 0 || malformed > 0)
            limits.Add($"Unbekannte Schemata {unknown}; fehlerhafte Felder {malformed}; nicht erlaubte Provider {rejected} kontrolliert übersprungen.");
        if (total == 0) limits.Add("Keine Ereignisse dekodiert; kein Funktionsnachweis.");
        if (incompleteSource) limits.Add("Vorausgehende tracerpt-XML-Auswertung unvollständig; EvtRender behauptet dadurch keine Vollständigkeit.");
        var qpcSeconds = (trace.QpcEnd - trace.QpcStart) / (double)trace.QpcFrequency;
        var clockJump = Math.Abs((trace.Ended - trace.Started).TotalSeconds - qpcSeconds) > 1;
        if (clockJump)
            limits.Add("UTC-/QPC-Grenzanker weichen ab (möglicher Uhrsprung); keine zeitliche Vergleichbarkeit zugesichert.");
        limits.Add("XML-SystemTime unverändert übernommen. Nativer Decoder verwendet Windows-EvtRender-Zeitwerte und protokolliert den Vergleich zur tracerpt-XML-Ausgabe. Keine lineare QPC-Eigenrekonstruktion oder Uhrsprungkorrektur.");
        limits.Add("Nur beobachtetes Aufnahmeintervall, kein vollständiger Boot-/Login-/Crashverlauf. CPU-Samples sind statistisch; Datei-, Prozess- und Datenträger-I/O getrennt.");
        var state =
            trace.State == "Completed" && !incompleteSource && !capped && unknown == 0 && rejected == 0 && malformed == 0 && missing == 0 && total > 0 &&
            trace.LostEvents == 0 && trace.LostBuffers == 0 && !clockJump ? "DecodedObservedInterval" : "Incomplete";
        var eventDiagnosticRows = malformedAggregates.Snapshot();
        if (eventDiagnosticRows.Where(aggregate => aggregate.State == "Malformed").Sum(aggregate => (long)aggregate.Count) +
                malformedAggregates.IssuesDropped != malformed ||
            eventDiagnosticRows.Where(aggregate => aggregate.State == "Skipped").Sum(aggregate => (long)aggregate.Count) +
                malformedAggregates.RejectedEventsDropped != rejected ||
            eventDiagnosticRows.Where(aggregate => aggregate.State == "UnknownSchema").Sum(aggregate => (long)aggregate.Count) +
                malformedAggregates.UnknownSchemaEventsDropped != unknown)
            throw new InvalidOperationException("Guardian-Ereignisdiagnostik stimmt nicht mit den Decode-Zählern überein.");
        return new(1, trace.Id, trace.Profile, trace.Sha256, state,
            total, unknown, rejected, BindObservedIdentities(facts, state == "DecodedObservedInterval"), limits, 0)
        {
            Decoder = new(false, true, true, Math.Min(total, MaximumEvents), facts.Length, factLimit, eventLimit,
                rejected, unknown, malformed, state == "Incomplete")
            {
                SkippedProviders = skipped.Select(p => p.ToString("D")).ToArray(),
                FieldIssues = issues,
                MissingFields = missing,
                OwnXml = ownXml,
                ProviderEvents = providerEvents, EventTypes = eventTypes.Values.ToArray(),
                EventTypeDiagnosticsDropped = eventTypeDiagnosticsDropped,
                EventDiagnostics = eventDiagnosticRows,
                MalformedAggregateIssuesDropped = malformedAggregates.IssuesDropped,
                RejectedEventDiagnosticsDropped = malformedAggregates.RejectedEventsDropped,
                UnknownSchemaDiagnosticsDropped = malformedAggregates.UnknownSchemaEventsDropped
            },
            Activity = facts.GroupBy(f => (Second: new DateTimeOffset(f.Time.UtcTicks - f.Time.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero), f.Kind))
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Second).Take(256)
                .Select(g => new GuardianTraceBucket(g.Key.Second, g.Key.Kind, g.Count())).ToArray()
        };
    }
    private static IReadOnlyList<GuardianTraceFact> BindObservedIdentities(IReadOnlyList<GuardianTraceFact> facts, bool complete)
    {
        if (!complete) return facts;
        var processes = new Dictionary<int, string>(); var threads = new Dictionary<int, string>();
        var result = new Dictionary<int, GuardianTraceFact>();
        foreach (var fact in facts.OrderBy(f => f.Time).ThenBy(f => f.Sequence))
        {
            int? Payload(string name) => int.TryParse(fact.Data.GetValueOrDefault(name), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
            var identity = fact.Identity;
            if (fact.Kind == "ProcessStart" && Payload("ProcessID") is { } pid)
            {
                if (processes.TryGetValue(pid, out var previous))
                    foreach (var thread in threads.Where(t => t.Value == previous).Select(t => t.Key).ToArray()) threads.Remove(thread);
                if (TryIdentity(identity, out _)) processes[pid] = identity;
                else processes.Remove(pid);
            }
            if (fact.Kind is "ThreadStart" or "ThreadEnd" && Payload("ThreadID") is { } tid && Payload("ProcessID") is { } owner)
            {
                if (fact.Kind == "ThreadStart") threads.Remove(tid);
                if (fact.Kind == "ThreadStart" && processes.TryGetValue(owner, out var process)) threads[tid] = process;
                if (threads.TryGetValue(tid, out var processKey) && TryIdentity(processKey, out var threadOwner) && threadOwner.Pid == owner)
                    identity = processKey;
                if (fact.Kind == "ThreadEnd") threads.Remove(tid);
            }
            else if (fact.Kind is "FileIO" or "DiskIO" or "NetworkMetadata" or "ImageLoad" or "ImageUnload" &&
                (Payload("ProcessID") ?? Payload("PID")) is { } eventPid && processes.TryGetValue(eventPid, out var processKey))
                identity = processKey;
            else if (fact.Kind is "CpuSample" or "FileIO" or "DiskIO" &&
                (Payload("ThreadID") ?? Payload("IssuingThreadId")) is { } eventTid && threads.TryGetValue(eventTid, out var threadKey))
                identity = threadKey;
            if (fact.Kind == "ProcessEnd" && Payload("ProcessID") is { } ended)
            {
                if (processes.Remove(ended, out var endedKey))
                    foreach (var thread in threads.Where(t => t.Value == endedKey).Select(t => t.Key).ToArray()) threads.Remove(thread);
            }
            result[fact.Sequence] = fact with { Identity = identity };
        }
        return facts.Select(f => result[f.Sequence]).ToArray();
    }
    internal static bool TryIdentity(string? value, out ProcessIdentity identity)
    {
        identity = default;
        if (value is null) return false;
        var parts = value.Split('@');
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
            pid <= 0 || ticks <= 0 || ticks > DateTime.MaxValue.Ticks) return false;
        identity = new(pid, ticks); return true;
    }
    internal static string? NormalizeRenderedFileTime(string? value, string renderPath) =>
        renderPath == "Windows EvtRender XML" ? value?.Replace("\u200e", "", StringComparison.Ordinal).Trim() : value?.Trim();
    internal static GuardianTraceAnalysis ConfirmSources(GuardianTraceResult trace, GuardianTraceAnalysis primary,
        GuardianTraceAnalysis confirmation, ProcessIdentity? preferredIdentity = null)
    {
        var sources = new List<GuardianTraceSourceComparison>();
        foreach (var fact in primary.Facts.Where(f => f.Kind is "ProcessStart" or "ProcessEnd")
            .OrderByDescending(f => preferredIdentity is { IsKnown: true } preferred && f.Identity == preferred.ToString())
            .ThenByDescending(f => TryIdentity(f.Identity, out _)).Take(32))
        {
            var peers = confirmation.Facts.Where(f => f.Provider == fact.Provider && f.EventId == fact.EventId &&
                f.Version == fact.Version && f.Opcode == fact.Opcode && f.Identity == fact.Identity &&
                TryIdentity(f.Identity, out _)).ToArray();
            var validIdentity = TryIdentity(fact.Identity, out var identity);
            var peer = peers.Length == 1 ? peers[0] : null;
            var identityState = !validIdentity ? "Missing" : peer is null ? "Missing" : "Confirmed";
            if (peer is null)
            {
                var sameRecord = confirmation.Facts.Where(f => f.Sequence == fact.Sequence && f.Provider == fact.Provider &&
                    f.EventId == fact.EventId && f.Version == fact.Version && f.Opcode == fact.Opcode &&
                    f.Data.GetValueOrDefault("ProcessID")?.Trim() == fact.Data.GetValueOrDefault("ProcessID")?.Trim()).ToArray();
                if (sameRecord.Length == 1)
                {
                    peer = sameRecord[0];
                    if (TryIdentity(peer.Identity, out _) && validIdentity && peer.Identity != fact.Identity) identityState = "Conflict";
                }
            }
            var typedConflict = fact.TypedValues.TryGetValue("CreateTime", out var typedCreate) &&
                (!TryCreation(typedCreate, out var typedCreated) || !validIdentity || typedCreated.UtcTicks != identity.StartTicks);
            if (fact.TypedValues.TryGetValue("ProcessID", out var typedPid) &&
                (!int.TryParse(typedPid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) || pid != identity.Pid))
                typedConflict = true;
            if (typedConflict) identityState = "Conflict";
            var eventField = fact.Kind == "ProcessStart" ? "CreateTime" : "ExitTime";
            var providerTimeValid = TryCreation(NormalizeRenderedFileTime(fact.Data.GetValueOrDefault(eventField), fact.RenderPath), out var providerTime);
            var providerConflict = peer is not null &&
                TryCreation(peer.Data.GetValueOrDefault(eventField), out var confirmationProviderTime) &&
                providerTimeValid && confirmationProviderTime.UtcTicks != providerTime.UtcTicks;
            if (fact.TypedValues.TryGetValue(eventField, out var typedEventTime) &&
                (!TryCreation(typedEventTime, out var typedProviderTime) || !providerTimeValid || typedProviderTime.UtcTicks != providerTime.UtcTicks))
                providerConflict = true;
            var typedTimeConflict = fact.TypedValues.TryGetValue("SystemTime", out var typedTime) &&
                (!TryXmlTime(typedTime, out var nativeTime) || nativeTime.UtcTicks != fact.Time.UtcTicks);
            var verified = validIdentity && identityState == "Confirmed" && !typedTimeConflict && !providerConflict &&
                providerTimeValid && Math.Abs((fact.Time - providerTime).TotalSeconds) <= 1 &&
                fact.Time >= trace.CaptureBegan && fact.Time <= trace.CaptureEnded;
            sources.Add(new(fact.Sequence, fact.Identity, fact.Kind, fact.RenderPath, "tracerpt XML",
                identityState, peer is null ? "Missing" : !providerConflict && !typedTimeConflict && peer.Time.UtcTicks == fact.Time.UtcTicks ? "Confirmed" : "Conflict",
                fact.OriginalSystemTime, peer?.OriginalSystemTime, fact.Data.GetValueOrDefault("CreateTime"),
                peer?.Data.GetValueOrDefault("CreateTime"), fact.Data.GetValueOrDefault("ExitTime"), fact.HeaderPid,
                fact.Data.GetValueOrDefault("ProcessID"), verified,
                "Primary event is kept intact. PID+CreateTime must agree with confirmation; primary event time must match provider " +
                eventField + " within 1s and observed capture; typed EvtRender values must not conflict. No field substitution."));
        }
        var unverified = sources.Where(s => !s.PrimaryTimeVerified).Select(s => s.Sequence).ToHashSet();
        return primary with
        {
            State = unverified.Count == 0 ? primary.State : "Incomplete",
            Facts = primary.Facts.Select(f => unverified.Contains(f.Sequence) ? f with { Availability = "Unknown" } : f).ToArray(),
            Decoder = primary.Decoder! with { Sources = sources, DecodeIncomplete = primary.State == "Incomplete" || unverified.Count != 0 },
            Limits = primary.Limits.Concat(sources.Any(s => s.TimeState == "Conflict") ?
                ["Zeitkonflikt: tracerpt-XML und Windows-EvtRender bewahrt; nur unabhängig provider-/capturebestätigter Primary-Instant verwendbar."] :
                Array.Empty<string>()).ToArray()
        };
    }
    private static bool TryCreation(string? value, out DateTimeOffset created)
    {
        value = value?.Trim();
        if (TryXmlTime(value, out created) && created.Year >= 1970) return true;
        var hex = value?.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true;
        if (long.TryParse(hex ? value![2..] : value, hex ? NumberStyles.HexNumber : NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var filetime) && filetime > 0)
        {
            try { created = new DateTimeOffset(DateTime.FromFileTimeUtc(filetime)); return created.Year >= 1970; }
            catch (ArgumentOutOfRangeException) { return false; }
        }
        return false;
    }
    private static bool TryXmlTime(string? value, out DateTimeOffset time)
    {
        time = default;
        if (value is null || value.Length is < 20 or > 128 || !value.Contains('T') ||
            !(value.EndsWith('Z') || value.Length >= 6 && value[^6] is '+' or '-' && value[^3] == ':')) return false;
        if (!value.EndsWith('Z') &&
            (!int.TryParse(value.AsSpan(value.Length - 5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
             !int.TryParse(value.AsSpan(value.Length - 2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
             hours > 14 || minutes > 59 || hours == 14 && minutes != 0)) return false;
        try { time = XmlConvert.ToDateTimeOffset(value); return true; }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException) { return false; }
    }
    internal static GuardianArtifact Artifact(GuardianTraceResult trace, GuardianTraceAnalysis analysis, GuardianContext context,
        IntelligenceSnapshot? snapshot = null, ProcessHistory? history = null)
    {
        Validate(trace);
        if (context.Host != trace.Host || analysis.Session != trace.Id || analysis.EtlSha256 != trace.Sha256 || analysis.Schema != 1)
            throw new InvalidDataException("Traceauswertung und Host-/Sessionkontext stimmen nicht überein.");
        return GuardianVault.Artifact(RecoveryKind.DiagnosticEvidence, "Deep Trace — " + GuardianTraceProfiles.Get(trace.Profile).Name, context,
            "Dekodierte technische ETW-Fakten. Roh-ETL separat Sensitive; keine Kausalbehauptung, keine automatische Aktion.",
            new Dictionary<string, string>
            {
                ["state"] = analysis.State,
                ["traceCounts"] = JsonSerializer.Serialize(analysis.Facts.GroupBy(f => f.Kind).ToDictionary(g => g.Key, g => g.Count()), IntelligenceModel.Json)
            }, snapshot) with
        {
            TraceResult = trace,
            TraceAnalysis = analysis,
            TraceHistory = history?.Rows.Where(p => analysis.Facts.Any(f => f.Identity == p.Key.ToString())).Take(8)
                .Select(p => new GuardianTraceProcessHistory(p.Key.ToString(), p.Value.Samples.Where(s =>
                    s.Timestamp >= trace.CaptureBegan && s.Timestamp <= trace.CaptureEnded).TakeLast(32).ToArray())).ToArray()
        };
    }
    internal static GuardianArtifact RawArtifact(GuardianTraceResult trace, GuardianContext context)
    {
        Validate(trace);
        if (context.Host != trace.Host) throw new InvalidDataException("Rohtrace gehört zu einem anderen Rechner.");
        return GuardianVault.Artifact(RecoveryKind.DiagnosticEvidence, "Deep Trace — Rohaufnahme / " + trace.Profile, context,
            "Eigene sensible ETL beendet. Auswertung noch nicht abgeschlossen; Rohdatei getrennt erhalten, kein Decoder-/Ursachennachweis.",
            new Dictionary<string, string> { ["state"] = "RawOnly", ["captureState"] = trace.State }) with
        { TraceResult = trace };
    }
    private static bool IsValidEventDiagnostic(GuardianTraceDiagnosticAggregate aggregate) =>
        aggregate is not null &&
        (aggregate.Provider is "(missing)" or "(invalid)" || Guid.TryParse(aggregate.Provider, out _)) &&
        (aggregate.ProviderName is null || aggregate.ProviderName.Length <= 128 &&
            DiagnosticToken(aggregate.ProviderName, "") == aggregate.ProviderName) &&
        (aggregate.EventId is null or >= 0) &&
        (aggregate.Version is null or >= 0 and <= byte.MaxValue) &&
        (aggregate.Task is null or >= 0 and <= ushort.MaxValue) &&
        (aggregate.Opcode is null or >= 0 and <= byte.MaxValue) &&
        (aggregate.Level is null or >= 0 and <= byte.MaxValue) &&
        aggregate.Field is { Length: > 0 and <= 128 } &&
        aggregate.ActualType is { Length: > 0 and <= 128 } &&
        aggregate.ParserPath is { Length: > 0 and <= 128 } &&
        aggregate.State is "Malformed" or "Skipped" or "UnknownSchema" &&
        aggregate.RenderPath is "Windows EvtRender XML" or "tracerpt XML" or "XML" &&
        MalformedReasonCodes.Contains(aggregate.ReasonCode) &&
        (aggregate.ThreadIdFormatClass is null && aggregate.ThreadIdWhitespace is null ||
         aggregate.State == "Malformed" && aggregate.ReasonCode == "ThreadIdInvalid" &&
         aggregate.ThreadIdFormatClass is "Empty" or "WhitespaceOnly" or "DecimalPositive" or "DecimalZero" or
             "SignedPositive" or "SignedNegative" or "HexPrefixed" or "HexWithoutPrefix" or "NonNumeric" or "UIntOverflow" or "Other" &&
         aggregate.ThreadIdWhitespace is "None" or "LeadingWhitespace" or "TrailingWhitespace" or
             "LeadingWhitespace+TrailingWhitespace" or "EmbeddedWhitespace" or "LeadingWhitespace+EmbeddedWhitespace" or
             "TrailingWhitespace+EmbeddedWhitespace" or "LeadingWhitespace+TrailingWhitespace+EmbeddedWhitespace") &&
        (aggregate.ProviderGuidStructure is null ||
         aggregate.State == "Malformed" && aggregate.ReasonCode is "ProviderGuidMissing" or "ProviderGuidInvalid" &&
         aggregate.ProviderGuidStructure is "ProviderElementMissing" or "ProviderGuidAttributeMissing" or
             "ProviderGuidAttributeEmpty" or "ProviderGuidSyntaxInvalid") &&
        aggregate.Count > 0 && aggregate.FirstSequence is > 0 and <= MaximumEvents &&
        aggregate.LastSequence >= aggregate.FirstSequence && aggregate.LastSequence <= MaximumEvents;
    internal static void ValidateAnalysis(GuardianTraceResult trace, GuardianTraceAnalysis analysis)
    {
        Validate(trace);
        if (analysis is null || analysis.Schema != 1 || analysis.Session != trace.Id || analysis.Profile != trace.Profile ||
            analysis.EtlSha256 != trace.Sha256 || analysis.State is not ("DecodedObservedInterval" or "Incomplete") ||
            analysis.TotalEvents is < 0 or > MaximumEvents + 1 || analysis.UnknownEvents < 0 || analysis.RejectedProviders < 0 ||
            !double.IsFinite(analysis.DecodeMilliseconds) || analysis.DecodeMilliseconds < 0 ||
            analysis.Facts is null || analysis.Facts.Count > MaximumFacts || analysis.Facts.Count > analysis.TotalEvents ||
            analysis.Facts.Any(f => f is null) || analysis.Facts.Select(f => f.Sequence).Distinct().Count() != analysis.Facts.Count ||
            analysis.Limits is null || analysis.Limits.Count > 32 || analysis.Limits.Any(l => l is null || l.Length > 4096) ||
            analysis.Activity is null || analysis.Activity.Count > 256 || analysis.Activity.Any(b =>
                b is null || b.Second == default || b.Kind is null || b.Kind.Length > 64 || b.Count is <= 0 or > MaximumFacts) ||
            analysis.Facts.Any(f => f is null || f.Sequence <= 0 || f.Sequence > MaximumEvents || f.Time == default ||
                f.EventId < 0 || f.Version < -1 || f.Kind is null || f.Kind.Length > 64 || f.Opcode is null || f.Opcode.Length > 128 ||
                f.Task is < 0 or > ushort.MaxValue || f.Level is < 0 or > byte.MaxValue ||
                f.OriginalSystemTime is { Length: > 128 } ||
                f.Identity is null || f.Identity.Length > 256 || f.Availability is not ("Available" or "Unknown") ||
                f.Data is null || f.Data.Count > Fields.Count || f.Data.Any(d => !Fields.Contains(d.Key) || d.Value is null || d.Value.Length > 4096) ||
                f.PayloadFields is null || f.PayloadFields.Count > 64 || f.PayloadFields.Any(field => field is null || field.Length > 128)))
            throw new JsonException("Ungültige ETL-Auswertung.");
        var allowed = GuardianTraceProfiles.Resolve(GuardianTraceProfiles.Get(trace.Profile), trace.Components).SelectMany(p => p.EventProviders).ToHashSet();
        var rejectedFacts = analysis.Facts.Where(f => !allowed.Contains(f.Provider))
            .Select(f => (Fact: f, Reason: GetObservedCpuSampleFactRejectionReason(trace, f)))
            .Where(item => item.Reason is not null).ToArray();
        if (rejectedFacts.Length > 0)
        {
            var evidence = rejectedFacts.Take(MaximumRejectionEvidence)
                .Select(item => CreateRejectionEvidence(item.Fact, item.Reason!)).ToArray();
            throw new GuardianTraceFactContractException(
                "ETL-Fakt außerhalb Provider-Allowlist / des exakten CPU-Sample-Vertrags.",
                evidence, rejectedFacts.Length - evidence.Length);
        }
        if (analysis.Decoder is { } decoder &&
            (decoder.EventsParsed is < 0 or > MaximumEvents || decoder.FactsProduced != analysis.Facts.Count ||
             decoder.UnknownProvidersSkipped != analysis.RejectedProviders || decoder.UnknownSchemasSkipped != analysis.UnknownEvents ||
             decoder.MalformedFields < 0 || decoder.DecodeIncomplete != (analysis.State == "Incomplete") ||
             decoder.TimeSource is null || decoder.TimeSource.Length > 1024 ||
             decoder.SkippedProviders is null || decoder.SkippedProviders.Count > 16 || decoder.SkippedProviders.Any(p => !Guid.TryParse(p, out _)) ||
             decoder.TimeSamples is null || decoder.TimeSamples.Count > 8 || decoder.TimeSamples.Any(s => s is null || s.Length > 4096) ||
             decoder.MissingFields < 0 || decoder.FieldIssues is null || decoder.FieldIssues.Count > 32 ||
             decoder.EventDiagnostics is null || decoder.EventDiagnostics.Count > MaximumMalformedAggregateGroups ||
             decoder.EventDiagnostics.Any(aggregate => !IsValidEventDiagnostic(aggregate)) ||
             decoder.MalformedAggregateIssuesDropped < 0 ||
             decoder.MalformedAggregateIssuesDropped > MaximumXmlBytes ||
             decoder.RejectedEventDiagnosticsDropped < 0 || decoder.RejectedEventDiagnosticsDropped > MaximumEvents ||
             decoder.UnknownSchemaDiagnosticsDropped < 0 || decoder.UnknownSchemaDiagnosticsDropped > MaximumEvents ||
             decoder.EventDiagnostics.Select(aggregate => new MalformedAggregateKey(aggregate.Provider,
                 aggregate.ProviderName, aggregate.EventGuid, aggregate.EventId, aggregate.Version, aggregate.Task,
                 aggregate.Opcode, aggregate.Level, aggregate.Keywords, aggregate.Field, aggregate.ActualType,
                 aggregate.ParserPath, aggregate.State, aggregate.Required, aggregate.RenderPath, aggregate.ReasonCode,
                 aggregate.ThreadIdFormatClass, aggregate.ThreadIdWhitespace, aggregate.ProviderGuidStructure))
                 .Distinct().Count() != decoder.EventDiagnostics.Count ||
             decoder.EventDiagnostics.Where(aggregate => aggregate.State == "Malformed").Sum(aggregate => (long)aggregate.Count) +
                 decoder.MalformedAggregateIssuesDropped != decoder.MalformedFields ||
             decoder.EventDiagnostics.Where(aggregate => aggregate.State == "Skipped").Sum(aggregate => (long)aggregate.Count) +
                 decoder.RejectedEventDiagnosticsDropped != decoder.UnknownProvidersSkipped ||
             decoder.EventDiagnostics.Where(aggregate => aggregate.State == "UnknownSchema").Sum(aggregate => (long)aggregate.Count) +
                 decoder.UnknownSchemaDiagnosticsDropped != decoder.UnknownSchemasSkipped ||
             decoder.ProviderEvents is null || decoder.ProviderEvents.Count > 16 ||
             decoder.ProviderEvents.Any(p => !Guid.TryParse(p.Key, out _) || p.Value is <= 0 or > MaximumEvents) ||
             decoder.EventTypes is null || decoder.EventTypes.Count > 64 || decoder.EventTypes.Any(e => e is null ||
                 e.FirstSequence is <= 0 or > MaximumEvents || e.Count is <= 0 or > MaximumEvents ||
                 e.Provider is null || e.Provider.Length > 128 ||
                 e.ProviderName is { Length: > 128 } || e.NativeProvider is { Length: > 128 } ||
                 e.EventId is null || e.EventId.Length > 128 || e.Version is null || e.Version.Length > 128 ||
                 e.Opcode is null || e.Opcode.Length > 128 || e.Keywords is null || e.Keywords.Length > 128 ||
                 e.Level is null || e.Level.Length > 128 || e.Time is { Length: > 128 } ||
                 e.State is null || e.State.Length > 256 || e.RenderPath is null || e.RenderPath.Length > 128 ||
                 e.PayloadFields is null || e.PayloadFields.Count > 64 || e.PayloadFields.Any(f => f is null || f.Length > 128) ||
                 e.Xml is { Length: > 8192 }) ||
             decoder.EventTypeDiagnosticsDropped is < 0 or > MaximumEvents ||
             decoder.EventTypes.Count(e => e.Xml is not null) > 16 ||
             decoder.OwnXml is null || decoder.OwnXml.Count > 4 || decoder.OwnXml.Any(x => x is null ||
                 x.Xml is null || x.Xml.Length > 16384 || !TryIdentity(x.Identity, out _) ||
                 x.RenderPath is null || x.RenderPath.Length > 128 || x.Sequence is <= 0 or > MaximumEvents) ||
             decoder.FieldIssues.Any(i => i is null || i.Sequence <= 0 || i.Sequence > MaximumEvents ||
                 i.RawValue is { Length: > 4096 } || i.Field is null || i.Field.Length > 128 ||
                 i.ActualType is null || i.ActualType.Length > 256 || i.RenderPath is null || i.RenderPath.Length > 128 ||
                 i.ParserPath is null || i.ParserPath.Length > 512 || i.Error is { Length: > 1024 } ||
                 i.State is not ("Missing" or "NotApplicable" or "Available" or "Malformed")) ||
             decoder.Sources is null || decoder.Sources.Count > 32 || decoder.Sources.Any(s => s is null ||
                 s.Identity is null || s.Identity.Length > 256 || s.Reason is null || s.Reason.Length > 2048 ||
                 s.PrimaryTime is { Length: > 128 } || s.ConfirmationTime is { Length: > 128 } ||
                 s.PrimaryCreateTime is { Length: > 4096 } || s.ConfirmationCreateTime is { Length: > 4096 } ||
                 s.PrimaryExitTime is { Length: > 4096 } ||
                 s.IdentityState is not ("Confirmed" or "Missing" or "Conflict") ||
                 s.TimeState is not ("Confirmed" or "Missing" or "Conflict"))))
            throw new JsonException("Ungültige ETL-Decoderdiagnose.");
        if (analysis.Facts.Any(f => f.RenderPath is null || f.RenderPath.Length > 128 ||
            f.TypedValues is null || f.TypedValues.Count > 4 || f.TypedValues.Any(p =>
                p.Key is not ("ProcessID" or "CreateTime" or "ExitTime" or "SystemTime") ||
                p.Value is null || p.Value.Length > 128)))
            throw new JsonException("Ungültige typisierte EvtRender-Werte.");
    }
}
