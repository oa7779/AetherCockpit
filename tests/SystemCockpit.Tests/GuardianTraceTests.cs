using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using AetherOperations;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class GuardianTraceTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Deep Trace: " + message); _checks++; }
    internal static int Run()
    {
        _checks = 0;
        var root = Path.Combine(Path.GetTempPath(), "AetherTraceTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Profiles(root); NativeAsync(root).GetAwaiter().GetResult(); Decoder(root); MalformedAggregates(root);
            MatrixDiagnostics(root); SourceFixtures(root); CpuPrivilege();
            CustomContractsAsync(root).GetAwaiter().GetResult(); RepeatResourcesAsync(root).GetAwaiter().GetResult(); Ui(root);
            return _checks;
        }
        finally { foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.Delete(file); Directory.Delete(root, true); }
    }
    private static void Reject(Action action, string message)
    {
        try { action(); throw new InvalidOperationException("Erwartete Abweisung fehlt: " + message); }
        catch (Exception ex) when (ex is ArgumentException or IOException or JsonException or XmlException)
        { Check(true, message); }
    }
    private static void Profiles(string root)
    {
        var privilege = GuardianTraceProfiles.ReadProfilePrivilege();
        Check(privilege.NativeCode == 0 && privilege.Present is not null && privilege.Enabled is not null &&
            (privilege.Enabled != true || privilege.Present == true), "profile privilege read-only token diagnostic has explicit native status");
        Check(GuardianTraceProfiles.All.Count == 7 && GuardianTraceProfiles.All.All(p => !p.Released), "all seven profiles remain separately gated");
        foreach (var profile in GuardianTraceProfiles.All)
        {
            var providers = GuardianTraceProfiles.Resolve(profile);
            Check(providers.Count > 0 && providers.All(p => p.Guid != Guid.Empty && p.Keywords != 0 && p.Level is >= 1 and <= 5 &&
                p.Documentation.Length > 10 && p.Expected.Length > 10 && p.Limitation.Length > 10), profile.Id + " fixed source contract");
            Check(profile.DefaultSeconds <= profile.MaximumSeconds && profile.MaximumSeconds <= 30 && profile.MaximumMiB <= 8 &&
                profile.Privacy.Contains("Sensitive", StringComparison.Ordinal) && profile.Stop.Length > 20 &&
                profile.Rights.Contains("keine Selbsterhöhung", StringComparison.Ordinal), profile.Id + " bounded privacy/rights contract");
        }
        Reject(() => GuardianTraceProfiles.Get("untrusted-provider"), "unknown profile");
        Reject(() => GuardianTraceProfiles.Resolve(GuardianTraceProfiles.Get("process") with { MaximumMiB = 99 }), "modified catalog instance");
        Reject(() => GuardianTraceProfiles.Resolve(GuardianTraceProfiles.Get("custom"), ["arbitrary-guid"]), "provider allowlist");
        Reject(() => GuardianTraceProfiles.Resolve(GuardianTraceProfiles.Get("custom"), ["process", "process"]), "duplicate provider");
        Reject(() => GuardianTraceProfiles.Resolve(GuardianTraceProfiles.Get("process"), ["network"]), "keywords cannot be switched indirectly");
        Reject(() => new GuardianTrace().RunProfileCoreAsync(Path.Combine(root, "over-budget"), GuardianTraceProfiles.Get("process"), 31, 8, null, default)
            .GetAwaiter().GetResult(), "duration before effects");
        Check(!Directory.Exists(Path.Combine(root, "over-budget")), "invalid duration has no filesystem/native side effect");
        try { new GuardianTrace().RunReleasedProfileAsync(Path.Combine(root, "gated"), "process", 1, default).GetAwaiter().GetResult(); Check(false, "gated start"); }
        catch (InvalidOperationException) { Check(!Directory.Exists(Path.Combine(root, "gated")), "profile gate before effects"); }
        if (!GuardianTraceProfiles.IsAdministrator())
        {
            try { new GuardianTrace().RunProfileAcceptanceAsync(Path.Combine(root, "denied-token"), "process", 1, default).GetAwaiter().GetResult(); Check(false, "nonadmin start"); }
            catch (UnauthorizedAccessException) { Check(!Directory.Exists(Path.Combine(root, "denied-token")), "token failure before effects"); }
        }
    }
    private sealed class Api(string directory, string mode) : IGuardianTraceNative
    {
        internal bool Active; internal int Stops, Queries;
        private string _file = "";
        public uint Start(out ulong session, string name, IntPtr properties)
        {
            session = 42;
            if (mode == "denied") return 5;
            Active = true;
            _file = Path.Combine(directory, name[16..] + ".etl");
            if (mode != "missing") File.WriteAllBytes(_file, new byte[1024]);
            if (mode == "soft-size") File.WriteAllBytes(_file, new byte[524288]);
            return 0;
        }
        public uint Enable(ulong session, Guid provider, byte level, ulong keywords) =>
            mode is "provider" or "keywords" ? 87u :
            mode == "cpu-privilege" && provider == GuardianTraceProfiles.Providers.Single(p => p.Id == "cpu").Guid ? 1314u : 0;
        public uint Control(ulong session, string name, IntPtr properties, uint control)
        {
            if (!Active) return 4201;
            if (control == 1)
            {
                Stops++;
                if (mode == "stop") return 5;
                if (mode != "present") Active = false;
                if (mode == "already-ended") return 4201;
                if (mode == "size") File.WriteAllBytes(_file, new byte[1024 * 1024]);
                if (mode == "loss-events") Marshal.WriteInt32(properties, 88, 3);
                if (mode == "loss-buffers") Marshal.WriteInt32(properties, 96, 2);
                return 0;
            }
            Queries++;
            if (mode == "auto-size")
            {
                File.WriteAllBytes(_file, new byte[1024 * 1024]); Active = false; return 4201;
            }
            if (mode == "query" && Queries == 1 || mode == "query-persistent") return 87;
            if (mode == "foreign") Marshal.StructureToPtr(Guid.Parse("00000000-1111-2222-3333-444444444444"), properties + 24, false);
            return 0;
        }
    }
    private static async Task NativeAsync(string root)
    {
        foreach (var mode in new[] { "denied", "provider", "keywords", "query", "query-persistent", "stop", "present", "foreign", "missing", "already-ended", "loss-events", "loss-buffers", "time", "size", "cancel", "shutdown" })
        {
            var directory = Path.Combine(root, mode); var api = new Api(directory, mode);
            var balance = 0; var steps = new List<string>();
            using var cancel = new CancellationTokenSource();
            if (mode is "cancel" or "shutdown") cancel.CancelAfter(60);
            GuardianTraceResult? result = null; Exception? error = null;
            try
            {
                result = await new GuardianTrace(api).RunProfileCoreAsync(directory, GuardianTraceProfiles.Get("process"), 1,
                    mode == "size" ? 1 : 8, null, cancel.Token, (name, code) =>
                    {
                        if (name == "PropertiesBufferAllocated") balance++;
                        if (name == "PropertiesBufferFreed") balance--;
                        steps.Add(name);
                    });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException) { error = ex; }
            Check(balance == 0, mode + " native property buffer released");
            if (mode is "time" or "size" or "cancel" or "shutdown" or "loss-events" or "loss-buffers" or "already-ended")
            {
                Check(error is null && result is not null && !api.Active && api.Stops == 1, mode + " lifecycle / ownership");
                Check(result!.State == (mode == "time" ? "Completed" : mode is "cancel" or "shutdown" ? "Cancelled" : "Incomplete"), mode + " state");
                Check(steps.Contains("SessionAbsentAfterStop") && result.Sha256.Length == 64 && result.QpcEnd >= result.QpcStart, mode + " immutable own result anchors");
                if (mode == "loss-events") Check(result.LostEvents == 3, "lost event count");
                if (mode == "loss-buffers") Check(result.LostBuffers == 2, "lost buffer count");
                if (mode == "time")
                {
                    using (var locked = new FileStream(result.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        try { await GuardianTrace.DeleteOwnedAsync(result, default, api); Check(false, "locked ETL deleted"); }
                        catch (IOException) { Check(File.Exists(result.Path) && Directory.Exists(directory), "cleanup failure retains directory and sensitive ETL explicitly"); }
                    }
                    try { await GuardianTraceDecoder.DecodeAsync(result, default); Check(false, "corrupt ETL accepted"); }
                    catch (InvalidDataException) { Check(!Directory.EnumerateDirectories(directory).Any(), "Windows decoder rejects corrupt own ETL and removes isolated conversion output"); }
                    await File.WriteAllTextAsync(result.Path + ".session", "corrupt");
                    Reject(() => GuardianTraceDecoder.DecodeAsync(result, default).GetAwaiter().GetResult(), "corrupt marker rejected");
                }
            }
            else
            {
                Check(error is not null && result is null, mode + " explicit failure, never success-shaped");
                Check(mode switch
                {
                    "query-persistent" or "stop" or "present" => api.Active,
                    "foreign" => api.Active && api.Stops == 0,
                    _ => !api.Active
                }, mode + " ambiguous or foreign session is not silently discarded");
            }
        }
        var orphanDirectory = Path.Combine(root, "orphan"); Directory.CreateDirectory(orphanDirectory);
        var orphan = new Api(orphanDirectory, "time") { Active = true };
        var id = Guid.NewGuid();
        Check(GuardianTrace.InspectOrStop(id, orphanDirectory, true, orphan).Contains("beendet", StringComparison.Ordinal) && !orphan.Active &&
            GuardianTrace.InspectOrStop(id, orphanDirectory, false, orphan) == "Nicht aktiv", "own orphan stop then absence");
        var foreign = new Api(orphanDirectory, "foreign") { Active = true };
        try { GuardianTrace.InspectOrStop(id, orphanDirectory, true, foreign); Check(false, "foreign stop"); }
        catch (InvalidOperationException) { Check(foreign.Stops == 0 && foreign.Active, "foreign identity never stopped"); }
        var retention = Path.Combine(root, "retention"); Directory.CreateDirectory(retention);
        for (var index = 0; index < 8; index++) File.WriteAllText(Path.Combine(retention, index + ".own"), "fixture");
        Reject(() => new GuardianTrace(new Api(retention, "time")).RunProfileCoreAsync(retention,
            GuardianTraceProfiles.Get("process"), 1, 8, null, default).GetAwaiter().GetResult(), "separate file budget before native start");
        var autoDirectory = Path.Combine(root, "auto-size"); var auto = new Api(autoDirectory, "auto-size");
        var autoSteps = new List<(string Name, uint Code)>();
        var automatic = await new GuardianTrace(auto).RunProfileCoreAsync(autoDirectory, GuardianTraceProfiles.Get("process"), 2, 1, null,
            default, (name, code) => autoSteps.Add((name, code)));
        Check(automatic.State == "Incomplete" && automatic.AutomaticEndCode == 4201 && automatic.EndReason.Contains("Größe", StringComparison.Ordinal) &&
            automatic.Bytes == 1048576 && !auto.Active && auto.Stops == 0, "automatic size stop before QueryStatus is a bounded own result, not a WMI exception");
        Check(autoSteps.Contains(("SessionAbsentAfterStop", 4201)) && !automatic.LossCountersVerified,
            "automatic absence must be confirmed; missing final counters stay unknown in injected backend");
        var cpuDirectory = Path.Combine(root, "cpu-privilege"); var cpuApi = new Api(cpuDirectory, "cpu-privilege");
        var cpuSteps = new List<(string Name, uint Code)>();
        try
        {
            await new GuardianTrace(cpuApi).RunProfileCoreAsync(cpuDirectory, GuardianTraceProfiles.Get("cpu"), 1, 8, null, default,
                (name, code) => cpuSteps.Add((name, code)));
            Check(false, "CPU privilege error");
        }
        catch (Win32Exception ex)
        {
            Check(ex.NativeErrorCode == 1314 && cpuSteps.Contains(("StartTrace", 0)) &&
                cpuSteps.Contains(("EnableTraceEx2", 1314)) && cpuSteps.Contains(("Provider:cpu", 1314)) &&
                cpuApi.Stops == 1 && !cpuApi.Active, "CPU system provider privilege failure isolated at EnableTraceEx2/1314 with own cleanup");
        }
        var softDirectory = Path.Combine(root, "soft-size"); var softApi = new Api(softDirectory, "soft-size");
        var controlled = await new GuardianTrace(softApi).RunProfileCoreAsync(softDirectory, GuardianTraceProfiles.Get("process"), 2, 8,
            null, default, softStopBytes: 524288);
        Check(controlled.State == "Incomplete" && controlled.SoftStopBytes == 524288 && controlled.Bytes < 8 * 1048576 &&
            controlled.AutomaticEndCode is null && controlled.LostBuffers == 0 && softApi.Stops == 1 && !softApi.Active,
            "controlled soft size stop precedes hard cap, preserves stop and zero-loss requirements");
        var shutdownDirectory = Path.Combine(root, "cpu-optin-shutdown"); var shutdownApi = new Api(shutdownDirectory, "time");
        var shutdownToken = new TokenFixture(true, false); var shutdownJournal = new List<GuardianCpuPrivilege.Entry>();
        using (var cancellation = new CancellationTokenSource())
        using (GuardianCpuPrivilege.Enable(true, shutdownJournal, shutdownToken))
        {
            var result = await new GuardianTrace(shutdownApi).RunProfileCoreAsync(shutdownDirectory,
                GuardianTraceProfiles.Get("cpu"), 1, 8, null, cancellation.Token,
                (name, code) => { if (name == "Provider:cpu" && code == 0) cancellation.Cancel(); });
            Check(result.State == "Cancelled" && !shutdownApi.Active && shutdownApi.Stops == 1,
                "shutdown while explicit CPU scope is active stops the own session after successful provider activation");
        }
        Check(!shutdownToken.Enabled && shutdownToken.Disposed && shutdownJournal.Any(e => e.Step == "ReadRestored" && e.Succeeded),
            "CPU shutdown finally restores privilege and closes token");
    }
    private static GuardianTraceResult Trace(string root)
    {
        var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var qpc = Stopwatch.GetTimestamp();
        return new(id, "AETHER-Guardian-" + id.ToString("N"), Path.Combine(root, id.ToString("N") + ".etl"), "Completed", now, 2000, 1024, 0, 0, "Zeitlimit", "Own synthetic fixture")
        {
            Ended = now.AddSeconds(2),
            CaptureBegan = now,
            CaptureEnded = now.AddSeconds(2),
            QpcStart = qpc,
            QpcEnd = qpc + 2 * Stopwatch.Frequency,
            QpcFrequency = Stopwatch.Frequency,
            Sha256 = new('A', 64),
            Host = "synthetic"
        };
    }
    private static XElement Event(GuardianTraceResult trace, int id, double offset, Dictionary<string, string>? data = null,
        Guid? provider = null, int version = 0)
    {
        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        return new XElement(ns + "Event", new XElement(ns + "System",
            new XElement(ns + "Provider", new XAttribute("Guid", (provider ?? GuardianTrace.ProcessProvider).ToString("B"))),
            new XElement(ns + "EventID", id), new XElement(ns + "Version", version), new XElement(ns + "Opcode", 0),
            new XElement(ns + "TimeCreated", new XAttribute("SystemTime", trace.Started.AddSeconds(offset).ToString("O"))),
            new XElement(ns + "Execution", new XAttribute("ProcessID", "999"), new XAttribute("ThreadID", "888"))),
            new XElement(ns + "EventData", (data ?? []).Select(pair => new XElement(ns + "Data", new XAttribute("Name", pair.Key), pair.Value))));
    }
    private static XElement SampledProfileEvent(GuardianTraceResult trace, double offset, string threadId = "61")
    {
        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        var sample = Event(trace, 0, offset, new()
        {
            ["InstructionPointer"] = "0x0000000000001000",
            ["ThreadId"] = threadId,
            ["Count"] = "1",
            ["Reserved"] = "0"
        }, new("9e814aad-3204-11d2-9a82-006008a86939"), 2);
        var system = sample.Elements().Single(e => e.Name.LocalName == "System");
        system.Elements().Single(e => e.Name.LocalName == "Opcode").Value = "46";
        system.Add(new XElement(ns + "Task", "0"), new XElement(ns + "Keywords", "0x0"), new XElement(ns + "Level", "0"));
        sample.Add(new XElement(ns + "ExtendedTracingInfo",
            new XElement(ns + "EventGuid", "{ce1dbfb4-137e-4da6-87b0-3f59aa102cbc}")));
        return sample;
    }
    private static GuardianTraceAnalysis Parse(GuardianTraceResult trace, params XElement[] events)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new XElement("Events", events).ToString()));
        return GuardianTraceDecoder.ParseXml(stream, trace, default);
    }
    private static void Decoder(string root)
    {
        var trace = Trace(root); var first = trace.Started.AddSeconds(-1); var second = trace.Started.AddMilliseconds(600);
        Dictionary<string, string> Process(DateTimeOffset created) => new() { ["ProcessID"] = "51", ["ParentProcessID"] = "7", ["CreateTime"] = created.ToString("O"), ["ImageName"] = "sensitive-private.exe", ["CommandLine"] = "DO_NOT_STORE_SECRET" };
        var firstKey = new ProcessIdentity(51, first.UtcTicks).ToString(); var secondKey = new ProcessIdentity(51, second.UtcTicks).ToString();
        var analysis = Parse(trace, Event(trace, 1, .1, Process(first)), Event(trace, 3, .2, new() { ["ProcessID"] = "51", ["ThreadID"] = "61" }),
            Event(trace, 2, .5, Process(first)), Event(trace, 1, .7, Process(second)), Event(trace, 5, .8, new() { ["ProcessID"] = "51", ["ImageName"] = "second.dll" }));
        Check(analysis.State == "DecodedObservedInterval" && analysis.Facts.Count == 5 && analysis.Facts[0].Identity == firstKey &&
            analysis.Facts[1].Identity == firstKey && analysis.Facts[2].Identity == firstKey && analysis.Facts[4].Identity == secondKey,
            "process ends during trace; PID reuse and thread/image attribution use observed lifecycle");
        Check(!JsonSerializer.Serialize(analysis).Contains("DO_NOT_STORE_SECRET", StringComparison.Ordinal), "payload allowlist removes command lines");
        GuardianTraceDecoder.ValidateAnalysis(trace, analysis); Check(true, "typed result validation");
        var unknown = Parse(trace, Event(trace, 333, .1), Event(trace, 1, .2, Process(first), version: 99),
            Event(trace, 1, .3, Process(first), Guid.Parse("00000000-1111-2222-3333-444444444444")));
        Check(unknown.State == "Incomplete" && unknown.UnknownEvents == 2 && unknown.RejectedProviders == 1, "unknown schema/provider explicitly incomplete");
        var uncertain = Parse(trace, Event(trace, 1, .1, new() { ["ProcessID"] = "51" }), Event(trace, 5, .2, new() { ["ProcessID"] = "51" }),
            Event(trace, 1, .3, new() { ["ProcessID"] = "52", ["CreateTime"] = "2026" }));
        Check(uncertain.Facts.All(f => !GuardianTraceDecoder.TryIdentity(f.Identity, out _)), "PID without creation time never becomes identity");
        var customTrace = trace with { Profile = "custom", Components = ["process", "cpu", "file", "disk", "network"] };
        var cpu = Event(customTrace, 0, .6, new() { ["ThreadId"] = "61" },
            new("ce1dbfb4-137e-4da6-87b0-3f59aa102cbc"), 2);
        cpu.Descendants().Single(e => e.Name.LocalName == "Opcode").Value = "46";
        var components = Parse(customTrace, Event(customTrace, 1, .1, Process(first)),
            Event(customTrace, 3, .2, new() { ["ProcessID"] = "51", ["ThreadID"] = "61" }),
            Event(customTrace, 15, .3, new() { ["IssuingThreadId"] = "61", ["FileName"] = "sensitive-private.bin", ["IoSize"] = "65536" },
                GuardianTraceProfiles.Providers.Single(p => p.Id == "file").Guid),
            Event(customTrace, 10, .4, new() { ["IssuingThreadId"] = "61", ["DiskNumber"] = "2", ["TransferSize"] = "65536" },
                GuardianTraceProfiles.Providers.Single(p => p.Id == "disk").Guid),
            Event(customTrace, 10, .5, new() { ["PID"] = "51", ["saddr"] = "127.0.0.1", ["daddr"] = "127.0.0.1", ["size"] = "1" },
                GuardianTraceProfiles.Providers.Single(p => p.Id == "network").Guid), cpu);
        Check(components.State == "DecodedObservedInterval" && components.Facts.Skip(2).All(f => f.Identity == firstKey) &&
            components.Facts.Select(f => f.Kind).Contains("CpuSample"), "documented CPU, file, disk and network fields bind only through observed process/thread lifecycle");
        Check(components.Facts.Single(f => f.Kind == "FileIO").Data.ContainsKey("FileName") &&
            !components.Facts.Single(f => f.Kind == "DiskIO").Data.ContainsKey("FileName"), "logical file operations and device IO stay separate");
        var threadReuse = Parse(customTrace, Event(customTrace, 1, .1, Process(first)),
            Event(customTrace, 3, .2, new() { ["ProcessID"] = "51", ["ThreadID"] = "61" }),
            Event(customTrace, 3, .3, new() { ["ProcessID"] = "52", ["ThreadID"] = "61" }), cpu);
        var foreignThreadEnd = Parse(customTrace, Event(customTrace, 1, .1, Process(first)),
            Event(customTrace, 3, .2, new() { ["ProcessID"] = "51", ["ThreadID"] = "61" }),
            Event(customTrace, 4, .3, new() { ["ProcessID"] = "52", ["ThreadID"] = "61" }));
        Check(!GuardianTraceDecoder.TryIdentity(threadReuse.Facts[^1].Identity, out _) &&
            !GuardianTraceDecoder.TryIdentity(foreignThreadEnd.Facts[^1].Identity, out _), "reused thread ID or mismatched end owner never inherits old process");
        var otherPerf = new XElement(cpu);
        otherPerf.Descendants().Single(e => e.Name.LocalName == "Opcode").Value = "51";
        var futurePerf = new XElement(cpu);
        futurePerf.Descendants().Single(e => e.Name.LocalName == "Version").Value = "99";
        Check(Parse(customTrace, otherPerf).Facts.Single().Kind == "Unknown" && Parse(customTrace, futurePerf).Facts.Single().Kind == "Unknown",
            "non-sample PerfInfo opcode or unknown version is not CPU sampling");
        var lost = Parse(trace with { LostEvents = 1 }, Event(trace, 1, .1, Process(first)), Event(trace, 3, .2, new() { ["ProcessID"] = "51", ["ThreadID"] = "61" }));
        Check(lost.State == "Incomplete" && !GuardianTraceDecoder.TryIdentity(lost.Facts[1].Identity, out _), "losses suppress derived lifecycle binding");
        var jump = Parse(trace with { Ended = trace.Started.AddDays(-1) }, Event(trace, 1, .1, Process(first)));
        Check(jump.State == "Incomplete" && jump.Limits.Any(l => l.Contains("Uhrsprung", StringComparison.Ordinal)), "UTC jump is not interpolated into QPC");
        var large = Parse(trace, Enumerable.Range(1, GuardianTraceDecoder.MaximumEvents + 2).Select(i => Event(trace, 5, .5)).ToArray());
        Check(large.Facts.Count == GuardianTraceDecoder.MaximumFacts && large.TotalEvents == GuardianTraceDecoder.MaximumEvents + 1 &&
            large.State == "Incomplete", "hard event/fact bounds");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        using var xml = new MemoryStream(Encoding.UTF8.GetBytes(new XElement("Events", Event(trace, 1, .1)).ToString()));
        try { GuardianTraceDecoder.ParseXml(xml, trace, cancel.Token); Check(false, "decode cancellation"); }
        catch (OperationCanceledException) { Check(true, "decode cancellation observed"); }
        using var malformed = new MemoryStream(Encoding.UTF8.GetBytes("<Events><Event>"));
        Reject(() => GuardianTraceDecoder.ParseXml(malformed, trace, default), "truncated XML");
        using var dtd = new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE Events [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><Events>&x;</Events>"));
        Reject(() => GuardianTraceDecoder.ParseXml(dtd, trace, default), "DTD/external entities forbidden");
        var history = new ProcessHistory();
        history.Apply([new("Own fixture", 51, 15, 20, 1, 2, .5, .2, "synthetic", first.UtcTicks)
            { ObservedAt = trace.Started.AddMilliseconds(200) }], trace.Started.AddMilliseconds(200));
        var artifact = GuardianTraceDecoder.Artifact(trace, analysis, new("synthetic", "synthetic"), history: history);
        artifact.Validate();
        var raw = GuardianTraceDecoder.RawArtifact(trace, new("synthetic", "synthetic")); raw.Validate();
        Check(raw.TraceAnalysis is null && raw.Fields["state"] == "RawOnly", "raw result remains a retryable separate state if decoding fails or is cancelled");
        Check(artifact.TraceHistory?.Single().Samples.Count == 1 && analysis.Activity.Sum(b => b.Count) == analysis.Facts.Count,
            "same-instance resource history is bounded to trace interval; event buckets show observed frequency, not CPU utilization");
        var snapshot = new IntelligenceSnapshot(1, Guid.NewGuid(), "Synthetic trace context", trace.Ended, [], [], [], "synthetic");
        var projected = GuardianWorkflows.Project(snapshot, [artifact]);
        IntelligenceModel.Validate(projected);
        Check(projected.Entities.Any(e => e.Id == "process:" + firstKey) && projected.Entities.Any(e => e.Id == "process:" + secondKey) &&
            projected.Edges.All(e => e.Metadata.Classification == DataClassification.Sensitive && e.Metadata.Source.Contains("ETW", StringComparison.Ordinal)),
            "System Map and process links preserve provider/time/privacy metadata and separate reused PID");
        Check(GuardianTraceProjection.Timeline(artifact).Select(t => t.Id).SequenceEqual(GuardianTraceProjection.Timeline(artifact).Select(t => t.Id)),
            "stable timeline identities across reload");
        Check(GuardianWorkflows.Project(snapshot with { HostScope = "foreign" }, [artifact]).Entities.Count == 0, "foreign host never merged");
        Check(IntelligenceModel.Compare(snapshot, projected).Any(c => c.Domain == "DeepTrace" && c.Kind == ChangeKind.Incomplete && c.After is not null),
            "Universal Compare includes trace facts without claiming full source coverage");
        Check(new IntelligenceSessionEngine().Diagnose(projected).Contains("Deep Trace:", StringComparison.Ordinal), "Slow-PC diagnosis shows trace context, not a cause");
        Check(GuardianTraceProjection.ProcessContext(new(51, first.UtcTicks), [artifact]).Contains("ProcessEnd", StringComparison.Ordinal) &&
            !GuardianTraceProjection.ProcessContext(new(51, first.UtcTicks), [artifact]).Contains("ImageLoad", StringComparison.Ordinal),
            "Process Command Center context excludes later PID reuse");
        Check(!Encoding.UTF8.GetString(GuardianRescue.Support([artifact])).Contains("sensitive-private", StringComparison.Ordinal), "support allowlist excludes trace payloads");
        Reject(() => (artifact with { TraceAnalysis = analysis with { Session = Guid.NewGuid() } }).Validate(), "session mismatch quarantinable");
    }
    private static void MalformedAggregates(string root)
    {
        var trace = Trace(root);
        void InvalidTime(XElement item) =>
            item.Descendants().Single(element => element.Name.LocalName == "TimeCreated").SetAttributeValue("SystemTime", "invalid-time");
        GuardianTraceAnalysis Read(GuardianTraceResult source, string renderPath, params XElement[] events)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new XElement("Events", events).ToString()));
            return GuardianTraceDecoder.ParseXml(stream, source, default, renderPath: renderPath);
        }

        var missingProvider = Event(trace, 1, .1);
        missingProvider.Descendants().Single(element => element.Name.LocalName == "Provider").Attribute("Guid")!.Remove();
        var invalidProvider = Event(trace, 1, .2);
        invalidProvider.Descendants().Single(element => element.Name.LocalName == "Provider").SetAttributeValue("Guid", "not-a-guid");
        var providerDiagnostics = Read(trace, "Windows EvtRender XML", missingProvider, invalidProvider);
        Check(providerDiagnostics.Decoder is
            {
                MalformedFields: 2,
                EventDiagnostics.Count: 2,
                MalformedAggregateIssuesDropped: 0
            } &&
            providerDiagnostics.Decoder.EventDiagnostics.Select(item => item.Provider)
                .Order().SequenceEqual(new[] { "(invalid)", "(missing)" }) &&
            providerDiagnostics.Decoder.EventDiagnostics.All(item => item.State == "Malformed" &&
                item.Field == "Provider.Guid" && item.Required),
            "missing and invalid provider GUIDs are separately grouped without their source values");
        GuardianTraceDecoder.ValidateAnalysis(trace, providerDiagnostics);
        Check(true, "provider diagnostics satisfy malformed-count invariant");

        var noProvider = Event(trace, 1, .1);
        noProvider.Descendants().Single(element => element.Name.LocalName == "Provider").Remove();
        var emptyProvider = Event(trace, 1, .1);
        emptyProvider.Descendants().Single(element => element.Name.LocalName == "Provider").SetAttributeValue("Guid", "");
        var structure = Read(trace, "XML", noProvider, missingProvider, emptyProvider, invalidProvider, Event(trace, 1, .1));
        Check(structure.Decoder!.MalformedFields == 4 &&
            structure.Decoder.EventDiagnostics.Where(item => item.State == "Malformed")
                .Select(item => item.ProviderGuidStructure).Order().SequenceEqual(new[]
                {
                    "ProviderElementMissing", "ProviderGuidAttributeEmpty", "ProviderGuidAttributeMissing",
                    "ProviderGuidSyntaxInvalid"
                }.Order()) &&
            !JsonSerializer.Serialize(structure.Decoder.EventDiagnostics).Contains("not-a-guid", StringComparison.Ordinal),
            "provider structure classification distinguishes absent, empty and invalid GUIDs without values");
        GuardianTraceDecoder.ValidateAnalysis(trace, structure);

        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        XElement Descriptor(int id, int version, string eventGuid, string opcode, string task, string level, string keywords)
        {
            var item = Event(trace, id, .1, version: version);
            InvalidTime(item);
            var system = item.Elements().Single(element => element.Name.LocalName == "System");
            system.Add(new XElement(ns + "Task", task), new XElement(ns + "Level", level),
                new XElement(ns + "Keywords", keywords));
            system.Elements().Single(element => element.Name.LocalName == "Opcode").Value = opcode;
            item.Add(new XElement(ns + "ExtendedTracingInfo", new XElement(ns + "EventGuid", eventGuid)));
            return item;
        }
        var descriptorRows = new[]
        {
            Descriptor(1, 4, "{11111111-1111-1111-1111-111111111111}", "1", "2", "3", "0x1"),
            Descriptor(2, 2, "{22222222-2222-2222-2222-222222222222}", "2", "3", "4", "0x2"),
            Descriptor(3, 1, "{33333333-3333-3333-3333-333333333333}", "3", "4", "5", "0x4")
        };
        var descriptorDiagnostics = Read(trace, "tracerpt XML", descriptorRows);
        Check(descriptorDiagnostics.Decoder!.EventDiagnostics.Count == 3 &&
            descriptorDiagnostics.Decoder.EventDiagnostics.Select(item => item.EventId).Distinct().Count() == 3 &&
            descriptorDiagnostics.Decoder.EventDiagnostics.Select(item => item.EventGuid).Distinct().Count() == 3 &&
            descriptorDiagnostics.Decoder.EventDiagnostics.All(item => item.ReasonCode == "SystemTimeInvalid" &&
                item.Task is not null && item.Level is not null && item.Keywords is not null),
            "aggregation keys preserve event GUID and complete descriptor metadata");
        GuardianTraceDecoder.ValidateAnalysis(trace, descriptorDiagnostics);
        var renderedDiagnostics = Read(trace, "Windows EvtRender XML", descriptorRows[0]);
        Check(renderedDiagnostics.Decoder!.EventDiagnostics.Single().RenderPath == "Windows EvtRender XML" &&
            descriptorDiagnostics.Decoder.EventDiagnostics.All(item => item.RenderPath == "tracerpt XML"),
            "aggregation records the decoder source path without merging independent runs");

        var cpuTrace = trace with { Profile = "cpu", Components = ["process", "cpu"] };
        var invalidSample = SampledProfileEvent(cpuTrace, .1, "PRIVATE_THREAD_VALUE");
        invalidSample.Descendants().Single(element => element.Attribute("Name")?.Value == "ThreadId")
            .SetAttributeValue("AetherType", "System.UInt32");
        var cpuDiagnostics = Read(cpuTrace, "Windows EvtRender XML", invalidSample);
        var serializedAggregates = JsonSerializer.Serialize(cpuDiagnostics.Decoder!.EventDiagnostics);
        Check(cpuDiagnostics.Decoder.MalformedFields == 1 && cpuDiagnostics.Decoder.UnknownSchemasSkipped == 1 &&
            cpuDiagnostics.Decoder.EventDiagnostics.Any(item => item.State == "Malformed" &&
                item.Field == "ThreadId" && item.ActualType == "System.UInt32" &&
                item.ReasonCode == "ThreadIdInvalid") &&
            cpuDiagnostics.Decoder.EventDiagnostics.Any(item => item.State == "UnknownSchema" &&
                item.ReasonCode == "PayloadSchemaInvalid") &&
            !serializedAggregates.Contains("PRIVATE_THREAD_VALUE", StringComparison.Ordinal) &&
            !serializedAggregates.Contains("0x0000000000001000", StringComparison.Ordinal),
            "CPU payload diagnostics retain field/schema reasons but no payload values");
        GuardianTraceDecoder.ValidateAnalysis(cpuTrace, cpuDiagnostics);
        Check(true, "CPU malformed and unknown-schema counts validate independently");

        var formatCases = new (string Value, string Format, string Whitespace, bool Accepted)[]
        {
            ("42", "DecimalPositive", "None", true),
            (" 42", "DecimalPositive", "LeadingWhitespace", true),
            ("42 ", "DecimalPositive", "TrailingWhitespace", true),
            (" 42 ", "DecimalPositive", "LeadingWhitespace+TrailingWhitespace", true),
            (" 4 2 ", "Other", "LeadingWhitespace+TrailingWhitespace+EmbeddedWhitespace", false),
            ("0", "DecimalZero", "None", false),
            ("-42", "SignedNegative", "None", false),
            ("+42", "SignedPositive", "None", false),
            ("0x2a", "HexPrefixed", "None", false),
            ("2a", "HexWithoutPrefix", "None", false),
            ("xyz", "NonNumeric", "None", false),
            ("4294967296", "UIntOverflow", "None", false),
            ("", "Empty", "None", false),
            ("   ", "WhitespaceOnly", "None", false)
        };
        foreach (var (value, format, whitespace, accepted) in formatCases)
        {
            Check(GuardianTraceDecoder.ClassifyThreadIdFormat(value) == (format, whitespace),
                "thread format classification: " + format + " / " + whitespace);
            var sample = Read(cpuTrace, "XML", SampledProfileEvent(cpuTrace, .1, value));
            var malformedThread = sample.Decoder!.EventDiagnostics
                .Where(item => item.ReasonCode == "ThreadIdInvalid").ToArray();
            Check(accepted ? malformedThread.Length == 0 && sample.Facts.Any(f => f.Kind == "CpuSample") :
                malformedThread.Length == 1 && malformedThread[0].ThreadIdFormatClass == format &&
                malformedThread[0].ThreadIdWhitespace == whitespace,
                "thread format diagnostics preserve acceptance and classify rejected sample: " + format);
            GuardianTraceDecoder.ValidateAnalysis(cpuTrace, sample);
        }
        var classified = Read(cpuTrace, "tracerpt XML",
            SampledProfileEvent(cpuTrace, .1, "0"), SampledProfileEvent(cpuTrace, .2, "0"),
            SampledProfileEvent(cpuTrace, .3, "+42"), SampledProfileEvent(cpuTrace, .4, "0x2a"));
        var classifiedGroups = classified.Decoder!.EventDiagnostics.Where(item => item.ReasonCode == "ThreadIdInvalid").ToArray();
        Check(classifiedGroups.Length == 3 && classifiedGroups.Single(item => item.ThreadIdFormatClass == "DecimalZero").Count == 2 &&
            classifiedGroups.Single(item => item.ThreadIdFormatClass == "SignedPositive").Count == 1 &&
            classifiedGroups.Single(item => item.ThreadIdFormatClass == "HexPrefixed").Count == 1,
            "format classes split aggregate counts without retaining values");
        GuardianTraceDecoder.ValidateAnalysis(cpuTrace, classified);
        var tampered = classified.Decoder.EventDiagnostics.Select(item => item.ReasonCode == "ThreadIdInvalid"
            ? item with { ThreadIdFormatClass = "PRIVATE_THREAD_VALUE" } : item).ToArray();
        Reject(() => GuardianTraceDecoder.ValidateAnalysis(cpuTrace,
            classified with { Decoder = classified.Decoder with { EventDiagnostics = tampered } }),
            "untrusted diagnostic format cannot carry arbitrary values");
        var whitespaceEvent = Event(trace, 1, .1, new() { ["ProcessID"] = "   " });
        var preservedEvent = Event(trace, 1, .1, new() { ["ProcessID"] = "   " });
        preservedEvent.Descendants().Single(element => element.Attribute("Name")?.Value == "ProcessID")
            .SetAttributeValue(XNamespace.Xml + "space", "preserve");
        var whitespaceFacts = Read(trace, "XML", whitespaceEvent, preservedEvent).Facts;
        Check(whitespaceFacts[0].Data["ProcessID"] == "" && whitespaceFacts[1].Data["ProcessID"] == "   ",
            "diagnostic whitespace inspection preserves previous XML whitespace semantics for other events");

        var missingThread = SampledProfileEvent(cpuTrace, .2);
        missingThread.Descendants().Single(element => element.Attribute("Name")?.Value == "ThreadId").Remove();
        var duplicateThread = SampledProfileEvent(cpuTrace, .3);
        duplicateThread.Elements().Single(element => element.Name.LocalName == "EventData")
            .Add(new XElement(ns + "Data", new XAttribute("Name", "ThreadId"), "62"));
        var unexpectedField = SampledProfileEvent(cpuTrace, .4);
        unexpectedField.Elements().Single(element => element.Name.LocalName == "EventData")
            .Add(new XElement(ns + "Data", new XAttribute("Name", "PrivateExtra"), "PRIVATE_EXTRA_VALUE"));
        var payloadDiagnostics = Read(cpuTrace, "tracerpt XML", missingThread, duplicateThread, unexpectedField);
        Check(payloadDiagnostics.Decoder!.EventDiagnostics.Any(item => item.Field == "ThreadId" &&
                item.ReasonCode == "PayloadFieldMissing") &&
            payloadDiagnostics.Decoder.EventDiagnostics.Count(item => item.Field == "ThreadId" &&
                item.ReasonCode == "PayloadFieldDuplicate") == 2 &&
            payloadDiagnostics.Decoder.EventDiagnostics.Any(item => item.Field == "PrivateExtra" &&
                item.ReasonCode == "PayloadFieldUnexpected") &&
            !JsonSerializer.Serialize(payloadDiagnostics.Decoder.EventDiagnostics)
                .Contains("PRIVATE_EXTRA_VALUE", StringComparison.Ordinal),
            "payload aggregation separates missing, duplicate and unexpected field reasons without values");
        GuardianTraceDecoder.ValidateAnalysis(cpuTrace, payloadDiagnostics);

        var wrongEventGuid = SampledProfileEvent(cpuTrace, .5);
        wrongEventGuid.Elements().Single(element => element.Name.LocalName == "ExtendedTracingInfo")
            .Elements().Single(element => element.Name.LocalName == "EventGuid").Value =
            "{01853a65-418f-4f36-aefc-dc0f1d2fd235}";
        var wrongDescriptor = SampledProfileEvent(cpuTrace, .6);
        wrongDescriptor.Elements().Single(element => element.Name.LocalName == "System")
            .Elements().Single(element => element.Name.LocalName == "Level").Value = "1";
        var rejectedCpu = Read(cpuTrace, "Windows EvtRender XML", wrongEventGuid, wrongDescriptor);
        Check(rejectedCpu.Decoder!.EventDiagnostics.Count(item => item.State == "Skipped") == 2 &&
            rejectedCpu.Decoder.EventDiagnostics.Any(item => item.ReasonCode == "EventGuidMismatch") &&
            rejectedCpu.Decoder.EventDiagnostics.Any(item => item.ReasonCode == "DescriptorMismatch"),
            "near-match CPU descriptors remain skipped and receive distinct rejection reasons");
        GuardianTraceDecoder.ValidateAnalysis(cpuTrace, rejectedCpu);

        var foreign = Read(trace, "tracerpt XML",
            Event(trace, 1, .1, new() { ["CommandLine"] = "PRIVATE_SKIPPED_VALUE" },
                Guid.Parse("00000000-1111-2222-3333-444444444444")));
        Check(foreign.RejectedProviders == 1 && foreign.Decoder!.EventDiagnostics.Single() is
            { State: "Skipped", ReasonCode: "ProviderNotAllowed", Field: "Provider.Guid" } &&
            !JsonSerializer.Serialize(foreign.Decoder.EventDiagnostics).Contains("PRIVATE_SKIPPED_VALUE", StringComparison.Ordinal),
            "disallowed-provider diagnostics remain distinct from malformed and contain no payload");
        GuardianTraceDecoder.ValidateAnalysis(trace, foreign);
        Check(true, "skipped-provider aggregate matches the provider rejection count");

        var fileProvider = GuardianTraceProfiles.Providers.Single(provider => provider.Id == "file").Guid;
        var factLimitTrace = trace with { Profile = "custom", Components = ["process", "file"] };
        var factLimitEvents = Enumerable.Range(0, GuardianTraceDecoder.MaximumFacts + 1)
            .Select(index => Event(factLimitTrace, 15, index / 1000d,
                provider: fileProvider)).ToList();
        var malformedAfterLimit = Event(factLimitTrace, 1, .1);
        InvalidTime(malformedAfterLimit);
        factLimitEvents.Add(malformedAfterLimit);
        var malformedAfterLimitAgain = Event(factLimitTrace, 1, .2);
        InvalidTime(malformedAfterLimitAgain);
        factLimitEvents.Add(malformedAfterLimitAgain);
        var afterFactLimit = Read(factLimitTrace, "tracerpt XML", factLimitEvents.ToArray());
        Check(afterFactLimit.Decoder is { FactLimitReached: true, FactsProduced: 2000, MalformedFields: 2 } &&
            afterFactLimit.Decoder.EventDiagnostics.Single(item => item.State == "Malformed").Count == 2,
            "malformed aggregation continues across events after the fact-retention limit");
        GuardianTraceDecoder.ValidateAnalysis(factLimitTrace, afterFactLimit);
        Check(true, "fact-limited analysis retains exact malformed totals");

        var unique = Enumerable.Range(1, 70).Select(id =>
        {
            var item = Event(trace, id, .1);
            InvalidTime(item);
            return item;
        }).ToList();
        unique.AddRange(Enumerable.Range(0, 10).Select(_ =>
        {
            var item = Event(trace, 1, .2);
            InvalidTime(item);
            return item;
        }));
        var capped = Read(trace, "tracerpt XML", unique.ToArray());
        Check(capped.Decoder is
            {
                MalformedFields: 80,
                EventDiagnostics.Count: GuardianTraceDecoder.MaximumMalformedAggregateGroups,
                MalformedAggregateIssuesDropped: 6
            } &&
            capped.Decoder.EventDiagnostics.Sum(item => item.Count) + capped.Decoder.MalformedAggregateIssuesDropped == 80 &&
            capped.Decoder.EventDiagnostics.Single(item => item.EventId == 1).Count == 11,
            "64-group cap counts omitted malformed occurrences while continuing known groups");
        GuardianTraceDecoder.ValidateAnalysis(trace, capped);
        Check(true, "bounded aggregation validates exact totals at its group limit");

        var skippedBeforeMalformed = Enumerable.Range(0, GuardianTraceDecoder.MaximumMalformedAggregateGroups)
            .Select(_ => Event(trace, 1, .1, provider: Guid.NewGuid())).ToList();
        var priorityMalformed = Event(trace, 1, .2);
        InvalidTime(priorityMalformed);
        skippedBeforeMalformed.Add(priorityMalformed);
        var priority = Read(trace, "tracerpt XML", skippedBeforeMalformed.ToArray());
        Check(priority.Decoder is
            {
                EventDiagnostics.Count: GuardianTraceDecoder.MaximumMalformedAggregateGroups,
                UnknownProvidersSkipped: GuardianTraceDecoder.MaximumMalformedAggregateGroups,
                RejectedEventDiagnosticsDropped: 1
            } &&
            priority.Decoder.EventDiagnostics.Any(item => item.State == "Malformed" &&
                item.ReasonCode == "SystemTimeInvalid"),
            "malformed groups take priority over skipped-provider groups at the shared cap");
        GuardianTraceDecoder.ValidateAnalysis(trace, priority);

        var unsupported = Enumerable.Range(1000, 70).Select(id => Event(trace, id, .1)).ToArray();
        var unsupportedCapped = Read(trace, "tracerpt XML", unsupported);
        Check(unsupportedCapped.Decoder is
            {
                UnknownSchemasSkipped: 70,
                EventDiagnostics.Count: GuardianTraceDecoder.MaximumMalformedAggregateGroups,
                UnknownSchemaDiagnosticsDropped: 6
            } &&
            unsupportedCapped.Decoder.EventDiagnostics.All(item => item.State == "UnknownSchema"),
            "unknown-schema diagnostic groups have their own bounded dropped counter");
        GuardianTraceDecoder.ValidateAnalysis(trace, unsupportedCapped);
    }
    private static void MatrixDiagnostics(string root)
    {
        var trace = Trace(root) with { Profile = "custom", Components = ["process", "file", "network"] };
        var identity = new ProcessIdentity(51, trace.Started.UtcTicks);
        Dictionary<string, string> Payload() => new()
        {
            ["ProcessID"] = "   51 ",
            ["CreateTime"] = "  " + trace.Started.ToString("O") + "  "
        };
        var file = GuardianTraceProfiles.Providers.Single(p => p.Id == "file").Guid;
        var network = GuardianTraceProfiles.Providers.Single(p => p.Id == "network").Guid;
        var events = new List<XElement> { Event(trace, 1, .01, Payload()) };
        events.AddRange(Enumerable.Range(0, 2600).Select(i => Event(trace, 15, .1, new() { ["ProcessID"] = "999" }, file)));
        events.Add(Event(trace, 2, .8, Payload()));
        events.Add(Event(trace, 10, .9, new() { ["PID"] = "999" }, network));
        events.Add(Event(trace, 1, 1, new() { ["ProcessID"] = "999" }, Guid.Parse("00000000-1111-2222-3333-444444444444")));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new XElement("Events", events).ToString()));
        var bounded = GuardianTraceDecoder.ParseXml(stream, trace, default, identity);
        Check(bounded.Facts.Count == 2000 && bounded.Decoder is
        {
            FactLimitReached: true, EventLimitReached: false,
            XmlValidated: true, MalformedFields: 0, DecodeIncomplete: true, UnknownProvidersSkipped: 1
        },
            "decoder success, fact limit, skipped provider and incomplete state are separate");
        Check(bounded.Facts.Any(f => f.Kind == "ProcessEnd" && f.Sequence > 2000 && f.Identity == identity.ToString()) &&
            bounded.Facts.Any(f => f.Kind == "NetworkMetadata" && f.Sequence > 2000),
            "FileIO flood does not displace own exact-identity lifecycle or required profile source");
        Check(bounded.Facts.Any(f => !GuardianTraceDecoder.TryIdentity(f.Identity, out _)) &&
            bounded.Facts.Single(f => f.Kind == "ProcessStart").Identity == identity.ToString(),
            "whitespace numeric/creation fields normalize; uncertain foreign facts never invalidate direct workload identity");
        GuardianTraceDecoder.ValidateAnalysis(trace, bounded);
        Check(true, "bounded prioritized analysis remains valid for persistence");
        var steps = new List<GuardianTraceMatrix.Step>
        {
            new("StartTrace", 0, true, 0), new("QueryStatus", 0, true, 100),
            new("ControlTraceStop", 0, true, 2000), new("SessionAbsentAfterStop", 4201, true, 2001)
        };
        steps.AddRange(trace.Components.Select(p => new GuardianTraceMatrix.Step("Provider:" + p, 0, true, 1)));
        var gates = GuardianTraceMatrix.EvaluateCore("custom", "profile", 8, trace, bounded, identity, true, steps, true, true);
        Check(gates.All(g => !g.Required || g.Passed == true), "bounded useful trace passes only with every mandatory core fact and lifecycle gate");
        Check(gates.Single(g => g.Name == "DecoderOutputComplete") is { Required: false, Passed: false } &&
            gates.Single(g => g.Name == "WorkloadIdentityObserved").Passed == true,
            "partial decoder output never becomes a completeness claim");
        var conversionFailure = GuardianTraceMatrix.EvaluateCore("custom", "profile", 8, trace, null, identity, true, steps,
            true, false, bounded.Decoder);
        Check(conversionFailure.Where(g => g.Name is "XmlProduced" or "XmlValidated").All(g => g.Passed == true) &&
            conversionFailure.Single(g => g.Name == "DecoderInvocationPassed").Passed == false &&
            conversionFailure.Single(g => g.Name == "DecoderOutputValid").Passed == false,
            "later conversion failure preserves existing XML diagnostic, without successful-final-decoder fallback");
        using (var source = new MemoryStream(Encoding.UTF8.GetBytes(new XElement("Events",
            Event(trace, 1, .01, Payload()), Event(trace, 3, .02, new() { ["ProcessID"] = "51", ["ThreadID"] = "61" })).ToString())))
        {
            var inherited = GuardianTraceDecoder.ParseXml(source, trace, default, identity, incompleteSource: true);
            Check(inherited.State == "Incomplete" && !GuardianTraceDecoder.TryIdentity(inherited.Facts[1].Identity, out _),
                "EVTX path cannot erase an incomplete original source or enable derived lifecycle bindings");
        }
        var missing = bounded with { Facts = bounded.Facts.Where(f => f.Kind != "ProcessEnd").ToArray() };
        var failed = GuardianTraceMatrix.EvaluateCore("custom", "profile", 8, trace, missing, identity, true, steps, true, true);
        Check(failed.Single(g => g.Name == "ProcessEndObserved").Passed == false &&
            failed.Single(g => g.Name == "ExpectedWorkloadObserved").Passed == false,
            "fact limit with displaced core fact fails precise mandatory gates");
        var odd = Event(trace, 1, .01, Payload());
        odd.Descendants().Single(e => e.Name.LocalName == "TimeCreated").SetAttributeValue("SystemTime", "2026-11-10T19:02:54.9943114+00:59");
        var offset = Parse(trace, odd).Facts.Single().Time;
        Check(offset.Offset == TimeSpan.FromMinutes(59) && offset.UtcDateTime.Hour == 18 && offset.UtcDateTime.Minute == 3,
            "unusual +00:59 offset is preserved exactly, never silently shifted one hour");
        foreach (var source in new[] { "2026-11-10T19:02:54.9943114+01:00", "2026-11-10T18:02:54.9943114Z" })
        {
            var value = new XElement(odd);
            value.Descendants().Single(e => e.Name.LocalName == "TimeCreated").SetAttributeValue("SystemTime", source);
            var parsed = Parse(trace, value).Facts.Single().Time;
            Check(parsed.UtcDateTime.Hour == 18 && parsed.UtcDateTime.Minute == 2 &&
                parsed.Offset == (source.EndsWith('Z') ? TimeSpan.Zero : TimeSpan.FromHours(1)),
                "ordinary UTC/+01:00 XML never becomes +00:59 through parser or serializer");
        }
        var outside = bounded with { Facts = bounded.Facts.Select(f => f with { Time = f.Time.AddMinutes(1) }).ToArray() };
        Check(GuardianTraceMatrix.EvaluateCore("custom", "profile", 8, trace, outside, identity, true, steps, true, true)
            .Single(g => g.Name == "CoreEventTimesWithinCapture").Passed == false, "self-shifted event timestamps fail temporal gate");
        var jump = trace with { Ended = trace.Ended.AddMinutes(1) };
        Check(GuardianTraceMatrix.EvaluateCore("custom", "profile", 8, jump, bounded, identity, true, steps, true, true)
            .Single(g => g.Name == "UtcQpcAnchorsConsistent").Passed == false, "UTC/QPC mismatch never reconstructed or ignored");
        var size = trace with { State = "Incomplete", Bytes = 1048576, EndReason = "Größenlimit", AutomaticEndCode = 4201 };
        var sizeSteps = steps.Where(s => s.Name != "ControlTraceStop").Append(new("QueryStatus", 4201, true, 2000)).ToArray();
        Check(GuardianTraceMatrix.EvaluateCore("custom", "size", 1, size, bounded, identity, true, sizeSteps, true, true)
            .Where(g => g.Name is "StopSucceeded" or "SessionAbsentAfterStop").All(g => g.Passed == true) &&
            GuardianTraceMatrix.EvaluateCore("custom", "size", 1, size, bounded, identity, true, sizeSteps, true, true)
                .Single(g => g.Name == "SizeWithinGate").Passed == false,
            "automatic hard-file exhaustion is observable but no longer passes controlled lossless soft-size acceptance");
        Check(GuardianTraceMatrix.EvaluateCore("custom", "size", 1, size with { LossCountersVerified = false }, bounded, identity, true, sizeSteps, true, true)
            .Single(g => g.Name == "LostEventsAcceptable").Passed == false, "missing final ETL loss counters are not defaulted to successful zero");
        var skipped = Parse(trace, Event(trace, 1, .1, Payload(), Guid.Parse("00000000-1111-2222-3333-444444444444")));
        Check(skipped.Decoder is { UnknownProvidersSkipped: 1, UnknownSchemasSkipped: 0, XmlValidated: true } &&
            skipped.Decoder.SkippedProviders.Count == 1, "unknown provider recorded by GUID and skipped without payload import");
        foreach (var profile in GuardianTraceProfiles.All)
        {
            var fixture = trace with { Profile = profile.Id, Components = profile.Id == "custom" ? trace.Components : profile.Components };
            var rows = new List<XElement> { Event(fixture, 1, .01, Payload()), Event(fixture, 2, 1.5, Payload()) };
            if (fixture.Components.Contains("file")) rows.Add(Event(fixture, 15, .2, provider: file));
            if (fixture.Components.Contains("disk")) rows.Add(Event(fixture, 10, .3,
                provider: GuardianTraceProfiles.Providers.Single(p => p.Id == "disk").Guid));
            if (fixture.Components.Contains("network")) rows.Add(Event(fixture, 10, .4, provider: network));
            if (fixture.Components.Contains("cpu"))
                rows.Add(SampledProfileEvent(fixture, .5));
            var decoded = Parse(fixture, rows.ToArray());
            var nativeSteps = steps.Where(s => !s.Name.StartsWith("Provider:", StringComparison.Ordinal))
                .Concat(fixture.Components.Select(c => new GuardianTraceMatrix.Step("Provider:" + c, 0, true, 1))).ToArray();
            Check(GuardianTraceMatrix.EvaluateCore(profile.Id, "profile", 8, fixture, decoded, identity, true, nativeSteps, true, true)
                .All(g => !g.Required || g.Passed == true), "synthetic required-source/core-gate matrix: " + profile.Id);
            if (profile.Id == "cpu")
            {
                var noSamples = decoded with { Facts = decoded.Facts.Where(f => f.Kind != "CpuSample").ToArray() };
                Check(GuardianTraceMatrix.EvaluateCore("cpu", "profile", 8, fixture, noSamples, identity, true, nativeSteps, true, true)
                    .Single(g => g.Name == "RequiredCpuSample").Passed == false, "privilege/provider success without real CPU source facts never passes");
                var lateSamples = decoded with { Facts = decoded.Facts.Select(f => f.Kind == "CpuSample" ? f with { Time = fixture.CaptureEnded.AddSeconds(1) } : f).ToArray() };
                Check(GuardianTraceMatrix.EvaluateCore("cpu", "profile", 8, fixture, lateSamples, identity, true, nativeSteps, true, true)
                    .Single(g => g.Name == "CpuSampleTimesWithinCapture").Passed == false, "CPU samples outside the observed interval fail temporal correlation");
                var blockedProvider = nativeSteps.Select(s => s.Name == "Provider:cpu" ? s with { NativeCode = 1314, Verified = false } : s).ToArray();
                Check(GuardianTraceMatrix.EvaluateCore("cpu", "profile", 8, fixture, decoded, identity, true, blockedProvider, true, true)
                    .Single(g => g.Name == "ProvidersEnabled").Passed == false, "CPU provider 1314 still fails even after privilege activation");
            }
        }
    }
    private static void SourceFixtures(string root)
    {
        var trace = Trace(root); var key = new ProcessIdentity(51, trace.Started.UtcTicks);
        Dictionary<string, string> Payload(bool rendered, bool end) => new()
        {
            ["ProcessID"] = "   51",
            ["CreateTime"] = (rendered ? "\u200e" : "") + trace.Started.ToString("O").Replace("-", rendered ? "\u200e-" : "-", StringComparison.Ordinal),
            ["ExitTime"] = end ? trace.Started.AddSeconds(.5).ToString("O") : ""
        };
        var start = Event(trace, 1, .001, Payload(true, false), version: 4);
        var end = Event(trace, 2, .501, Payload(true, true), version: 2);
        GuardianTraceAnalysis Read(string path, params XElement[] rows)
        {
            using var xml = new MemoryStream(Encoding.UTF8.GetBytes(new XElement("Events", rows).ToString()));
            return GuardianTraceDecoder.ParseXml(xml, trace, default, key, renderPath: path);
        }
        var primary = Read("Windows EvtRender XML", start, end);
        var confirmation = Read("tracerpt XML", Event(trace, 1, .001, Payload(false, false), version: 4),
            Event(trace, 2, .501, Payload(false, true), version: 2));
        Check(primary.Decoder is { MalformedFields: 0, MissingFields: 0 } &&
            primary.Facts.All(f => f.Identity == key.ToString()), "real EvtRender U+200E FILETIME display formatting is normalized only in that render path");
        Check(primary.Decoder!.FieldIssues.Count == 2 && primary.Decoder.FieldIssues.All(i => i.State == "Available" &&
            i.RawValue!.Contains('\u200e') && i.Required && i.RenderPath == "Windows EvtRender XML"),
            "normalization retains exact raw formatting, field provenance and required status");
        Check(Read("tracerpt XML", start).Decoder!.MalformedFields == 1, "unexpected formatting in another source is not silently stripped");
        var confirmed = GuardianTraceDecoder.ConfirmSources(trace, primary, confirmation, key);
        Check(confirmed.Decoder!.Sources.All(s => s.IdentityState == "Confirmed" && s.TimeState == "Confirmed" && s.PrimaryTimeVerified),
            "same provider/event/schema/opcode/exact identity confirms complete primary records without mixing fields");
        var changed = confirmation with
        {
            Facts = confirmation.Facts.Select(f => f with
            { Time = f.Time.AddMinutes(1), OriginalSystemTime = f.Time.AddMinutes(1).ToOffset(TimeSpan.FromMinutes(59)).ToString("O") }).ToArray()
        };
        var conflict = GuardianTraceDecoder.ConfirmSources(trace, primary, changed, key);
        Check(conflict.Decoder!.Sources.All(s => s.TimeState == "Conflict" && s.PrimaryTimeVerified) &&
            conflict.Facts.Select(f => f.Time).SequenceEqual(primary.Facts.Select(f => f.Time)),
            "tracerpt +00:59 conflict stays visible while provider+capture-confirmed primary UTC instant is kept intact");
        var badPrimary = primary with { Facts = primary.Facts.Select(f => f with { Time = f.Time.AddMinutes(1) }).ToArray() };
        Check(GuardianTraceDecoder.ConfirmSources(trace, badPrimary, confirmation, key).Decoder!.Sources.All(s => !s.PrimaryTimeVerified),
            "primary is not automatically trusted if inconsistent with provider time/capture");
        var typed = primary with
        {
            Facts = primary.Facts.Select(f => f with
            {
                TypedValues = new Dictionary<string, string>
                { ["CreateTime"] = trace.Started.AddTicks(1).ToString("O") }
            }).ToArray()
        };
        Check(GuardianTraceDecoder.ConfirmSources(trace, typed, confirmation, key).Decoder!.Sources.All(s =>
            s.IdentityState == "Conflict" && !s.PrimaryTimeVerified), "typed EvtRender contradiction fails, no silent field override");
        var typedExit = primary with
        {
            Facts = primary.Facts.Select(f => f.Kind == "ProcessEnd" ? f with
            { TypedValues = new Dictionary<string, string> { ["ExitTime"] = trace.Started.AddSeconds(.6).ToString("O") } } : f).ToArray()
        };
        Check(GuardianTraceDecoder.ConfirmSources(trace, typedExit, confirmation, key).Decoder!.Sources.Single(s =>
            s.Kind == "ProcessEnd") is { TimeState: "Conflict", PrimaryTimeVerified: false },
            "contradictory typed ExitTime cannot validate process end time");
        Check(GuardianTraceDecoder.ConfirmSources(trace, primary, confirmation with { Facts = [] }, key).Decoder!.Sources.All(s =>
            s.IdentityState == "Missing" && !s.PrimaryTimeVerified), "missing confirmation is not an implicit success");
        var otherInstance = confirmation with
        {
            Facts = confirmation.Facts.Select(f => f with
            { Identity = new ProcessIdentity(51, trace.Started.AddTicks(1).UtcTicks).ToString() }).ToArray()
        };
        Check(GuardianTraceDecoder.ConfirmSources(trace, primary, otherInstance, key).Decoder!.Sources.All(s =>
            s.IdentityState == "Conflict" && !s.PrimaryTimeVerified), "same rendered record with contradictory creation identity is Conflict, not fallback");
        var optional = Event(trace, 2, .5, new() { ["ProcessID"] = "51", ["CreateTime"] = trace.Started.ToString("O"), ["ExitTime"] = "NULL" }, version: 2);
        Check(Read("Windows EvtRender XML", optional).Decoder is { MalformedFields: 0, MissingFields: 0 } &&
            Read("Windows EvtRender XML", optional).Decoder!.FieldIssues.Any(i => i.Field == "ExitTime" && i.State == "NotApplicable" && !i.Required),
            "optional Windows NULL field is NotApplicable, not malformed");
        var malformed = Event(trace, 1, .1, new() { ["ProcessID"] = "not-a-number", ["CreateTime"] = "damaged" }, version: 4);
        Check(Read("Windows EvtRender XML", malformed).Decoder!.MalformedFields == 2, "damaged mandatory payload still fails exact field contracts");
        var wrongType = new XElement(start);
        wrongType.Descendants().Single(e => e.Attribute("Name")?.Value == "ProcessID").SetAttributeValue("AetherType", "System.Boolean");
        var wrongTypedAnalysis = Read("Windows EvtRender XML", wrongType);
        Check(wrongTypedAnalysis.Decoder!.MalformedFields == 1 &&
            wrongTypedAnalysis.Facts.All(f => f.Availability == "Unknown" &&
                !GuardianTraceDecoder.TryIdentity(f.Identity, out _)),
            "wrong native payload type remains malformed and cannot qualify an identity despite numeric-looking text");
        var missing = Event(trace, 1, .1, new() { ["ProcessID"] = "51" }, version: 4);
        Check(Read("Windows EvtRender XML", missing).Decoder is { MissingFields: 1, MalformedFields: 0 },
            "missing mandatory identity field is Missing separately from malformed");
        var flood = trace with { Profile = "custom", Components = ["process", "file"] };
        var fileProvider = GuardianTraceProfiles.Providers.Single(p => p.Id == "file").Guid;
        var rows = new List<XElement> { Event(flood, 1, .001, Payload(false, false), version: 4),
                    Event(flood, 3, .01, new() { ["ProcessID"] = "51", ["ThreadID"] = "61" }),
                    Event(flood, 5, .02, new() { ["ProcessID"] = "51" }),
                    Event(flood, 15, .03, new() { ["IssuingThreadId"] = "61" }, fileProvider) };
        rows.AddRange(Enumerable.Range(0, 10000).Select(_ => Event(flood, 15, .04, provider: fileProvider)));
        rows.Add(Event(flood, 2, .5, Payload(false, true), version: 2));
        using (var buffer = new MemoryStream(Encoding.UTF8.GetBytes(new XElement("Events", rows).ToString())))
        {
            var limited = GuardianTraceDecoder.ParseXml(buffer, flood, default, key);
            Check(limited.Decoder is { FactLimitReached: true, EventLimitReached: true } &&
                limited.Facts.Any(f => f.Kind == "ThreadStart") && limited.Facts.Any(f => f.Kind == "ImageLoad") &&
                limited.Facts.Any(f => f.Kind == "FileIO" && f.Data.ContainsKey("IssuingThreadId")),
                "six retention classes keep own thread/image/profile context under global FileIO flood");
            Check(!limited.Facts.Any(f => f.Kind == "ProcessEnd") &&
                limited.Facts.Where(f => f.Kind != "ProcessStart").All(f => !GuardianTraceDecoder.TryIdentity(f.Identity, out _)),
                "core fact outside 10000 events remains missing; retention priority never creates PID-only identity");
        }
        foreach (var time in new[] { "2026-03-29T01:59:59+01:00", "2026-03-29T03:00:00+02:00",
                    "2026-10-25T02:30:00+02:00", "2026-10-25T02:30:00+01:00", "2026-11-10T18:37:46Z", "2026-11-10T19:37:46+00:59" })
        {
            var row = new XElement(start); row.Descendants().Single(e => e.Name.LocalName == "TimeCreated").SetAttributeValue("SystemTime", time);
            var fact = Read("Windows EvtRender XML", row).Facts.Single();
            Check(fact.Time == DateTimeOffset.Parse(time, System.Globalization.CultureInfo.InvariantCulture) &&
                fact.Time == XmlConvert.ToDateTimeOffset(time), "strict XML and invariant DateTimeOffset agree across explicit offsets/DST: " + time);
        }
        foreach (var time in new[] { "2026-11-10T18:37:46", "2026-11-10T18:37:46+25:00", "2026-11-10T18:37:46+00:99" })
        {
            var row = new XElement(start); row.Descendants().Single(e => e.Name.LocalName == "TimeCreated").SetAttributeValue("SystemTime", time);
            Check(Read("Windows EvtRender XML", row).Decoder!.MalformedFields == 1, "missing or invalid time offset rejected: " + time);
        }
        var cancelled = trace with { State = "Cancelled" };
        var native = new GuardianTraceMatrix.Step[] { new("StartTrace", 0, true, 0), new("Provider:process", 0, true, 0),
                    new("QueryStatus", 0, true, 1), new("ControlTraceStop", 0, true, 2), new("SessionAbsentAfterStop", 4201, true, 3) };
        foreach (var mode in new[] { "cancel", "shutdown-token" })
        {
            var gates = GuardianTraceMatrix.EvaluateCore("process", mode, 8, cancelled, null, null, true, native, false, false);
            Check(gates.All(g => !g.Required || g.Passed == true) && gates.Single(g => g.Name == "ProcessEndObserved") is { Required: false, Passed: false },
                mode + " validates lifecycle independently, missing content remains observed false/not required");
            Check(GuardianTraceMatrix.EvaluateCore("process", mode, 8, cancelled, null, null, true,
                native.Where(s => s.Name != "SessionAbsentAfterStop").ToArray(), false, false)
                .Single(g => g.Name == "SessionAbsentAfterStop").Passed == false, "cancel/shutdown never waives session absence");
        }
        GuardianTraceDecoder.ValidateAnalysis(trace, confirmed);
        Check(true, "new source contracts and field issues persist under strict bounded validation");
    }
    private sealed class TokenFixture(bool present, bool enabled, uint error = 0) : GuardianCpuPrivilege.IToken
    {
        internal bool Enabled = enabled; internal int Adjustments; internal bool Disposed;
        internal bool RefuseEnable; internal uint RestoreError; internal bool RefuseRestore;
        public GuardianTraceProfiles.ProfilePrivilege Read() => new(present, Enabled, 0);
        public uint Adjust(bool enable, out bool previous)
        {
            Adjustments++; previous = Enabled;
            var code = Adjustments > 1 ? RestoreError : error;
            if (code == 0 && !(Adjustments == 1 ? RefuseEnable : RefuseRestore)) Enabled = enable;
            return code;
        }
        public void Dispose() => Disposed = true;
    }
    private static void CpuPrivilege()
    {
        var journal = new List<GuardianCpuPrivilege.Entry>(); var token = new TokenFixture(true, false);
        using (GuardianCpuPrivilege.Enable(true, journal, token)) Check(token.Enabled, "explicit CPU fixture enables only existing own privilege");
        Check(!token.Enabled && token.Disposed && journal.Any(e => e.Step == "ReadRestored" && e.Succeeded) &&
            journal.Any(e => e.Step == "TokenHandleClosed" && e.Succeeded && e.NativeCode is null),
            "CPU scope restores previous state and closes token");
        var already = new TokenFixture(true, true);
        using (GuardianCpuPrivilege.Enable(true, [], already)) { }
        Check(already.Enabled, "previously enabled privilege remains enabled after scope");
        var absent = new TokenFixture(false, false);
        try { GuardianCpuPrivilege.Enable(true, [], absent); Check(false, "absent privilege"); }
        catch (UnauthorizedAccessException) { Check(absent.Adjustments == 0 && absent.Disposed, "missing privilege is never added"); }
        var unassigned = new TokenFixture(true, false, 1300);
        try { GuardianCpuPrivilege.Enable(true, [], unassigned); Check(false, "not all assigned"); }
        catch (Win32Exception ex) { Check(ex.NativeErrorCode == 1300 && !unassigned.Enabled && unassigned.Disposed, "ERROR_NOT_ALL_ASSIGNED is explicit failure"); }
        var noConsent = new TokenFixture(true, false);
        try { GuardianCpuPrivilege.Enable(false, [], noConsent); Check(false, "no consent"); }
        catch (InvalidOperationException) { Check(noConsent.Adjustments == 0 && !noConsent.Enabled, "no explicit flag means no privilege effect"); }
        Check(GuardianCpuPrivilege.CheckAdjustment(true, 1300) == 1300, "nonzero BOOL with ERROR_NOT_ALL_ASSIGNED is not successful adjustment");
        Check(GuardianCpuPrivilege.CheckAdjustment(false, 5) == 5, "native adjustment FALSE preserves direct error code");
        try { GuardianCpuPrivilege.CheckAdjustment(false, 0); Check(false, "FALSE without error"); }
        catch (InvalidOperationException) { Check(true, "native FALSE without error never becomes success"); }
        var failed = new TokenFixture(true, false, 5);
        try { GuardianCpuPrivilege.Enable(true, [], failed); Check(false, "adjust failed"); }
        catch (Win32Exception ex) { Check(ex.NativeErrorCode == 5 && failed.Disposed && !failed.Enabled, "failed activation closes token without claiming enablement"); }
        var unread = new TokenFixture(true, false) { RefuseEnable = true }; var unreadJournal = new List<GuardianCpuPrivilege.Entry>();
        try { GuardianCpuPrivilege.Enable(true, unreadJournal, unread); Check(false, "readback disabled"); }
        catch (InvalidOperationException)
        {
            Check(unread.Disposed && unreadJournal.Any(e => e.Step == "ReadRestored" && e.Succeeded),
            "failed enabled readback restores and verifies original disabled state before closing");
        }
        foreach (var refuse in new[] { false, true })
        {
            var restoreFailure = new TokenFixture(true, false) { RestoreError = refuse ? 0u : 5u, RefuseRestore = refuse };
            var scope = GuardianCpuPrivilege.Enable(true, [], restoreFailure);
            try { scope.Dispose(); Check(false, "restore failure"); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            { Check(restoreFailure.Disposed && restoreFailure.Enabled, "restore or restoration-readback failure is explicit; token always closed"); }
        }
    }
    private static async Task CustomContractsAsync(string root)
    {
        var combinations = new string[][]
        {
            ["process"], ["file"], ["network"], ["process", "file"], ["process", "network"],
            ["file", "network"], ["process", "file", "network"]
        };
        foreach (var selected in combinations)
        {
            var plan = GuardianTraceProfiles.CustomPlan(selected);
            Check(plan.Seconds == 1 && plan.EtlMiB == 8 && plan.DecoderBytes == 67108864 && plan.SoftStopBytes == 524288 &&
                plan.Effective.Contains("process") && selected.All(plan.Effective.Contains) &&
                GuardianTraceProfiles.Resolve(GuardianTraceProfiles.Get("custom"), plan.Effective).Select(p => p.Id).SequenceEqual(plan.Effective) &&
                GuardianTraceProfiles.Resolve(GuardianTraceProfiles.Get("custom"), selected).Select(p => p.Id).SequenceEqual(selected),
                "bounded custom selected/effective preview: " + string.Join("+", selected));
            var fixture = Trace(root) with { Profile = "custom", Components = plan.Effective };
            var identity = new ProcessIdentity(51, fixture.Started.UtcTicks);
            var payload = new Dictionary<string, string> { ["ProcessID"] = "51", ["CreateTime"] = fixture.Started.ToString("O") };
            var rows = new List<XElement> { Event(fixture, 1, .01, payload), Event(fixture, 2, .2, payload) };
            if (plan.Effective.Contains("file")) rows.Add(Event(fixture, 15, .1,
                provider: GuardianTraceProfiles.Providers.Single(p => p.Id == "file").Guid));
            if (plan.Effective.Contains("network")) rows.Add(Event(fixture, 10, .15,
                provider: GuardianTraceProfiles.Providers.Single(p => p.Id == "network").Guid));
            var analysis = Parse(fixture, rows.ToArray());
            var steps = new List<GuardianTraceMatrix.Step> { new("StartTrace", 0, true, 0), new("QueryStatus", 0, true, 100),
                new("ControlTraceStop", 0, true, 500), new("SessionAbsentAfterStop", 4201, true, 501) };
            steps.AddRange(plan.Effective.Select(c => new GuardianTraceMatrix.Step("Provider:" + c, 0, true, 1)));
            Check(GuardianTraceMatrix.EvaluateCore("custom", "bounded-custom", 8, fixture, analysis, identity, true, steps, true, true)
                .All(g => !g.Required || g.Passed == true), "custom requires exactly its selected source facts plus own lifecycle: " + string.Join("+", selected));
            var soft = fixture with
            {
                State = "Incomplete",
                Bytes = 524288,
                SoftStopBytes = 524288,
                EndReason = "Größenlimit: kontrollierter Soft-Stopp mit Reserve zum harten Dateilimit"
            };
            Check(GuardianTraceMatrix.EvaluateCore("custom", "bounded-custom", 8, soft, analysis, identity, true, steps, true, true)
                .Single(g => g.Name == "CaptureStateExpected").Passed == true, "custom controlled softstop is not automatic hard exhaustion");
        }
        Reject(() => GuardianTraceProfiles.CustomPlan(["process", "file", "network", "disk"]), "third heavy source rejected before effects");
        var apiDirectory = Path.Combine(root, "custom-softstop"); var api = new Api(apiDirectory, "soft-size");
        var captured = await new GuardianTrace(api).RunProfileCoreAsync(apiDirectory, GuardianTraceProfiles.Get("custom"),
            1, 8, ["file", "network"], default);
        Check(captured.State == "Incomplete" && captured.SoftStopBytes == 524288 && captured.Bytes == 524288 &&
            captured.ElapsedMilliseconds < 1500 && captured.Components.SequenceEqual(new[] { "process", "file", "network" }) &&
            !api.Active && api.Stops == 1, "custom enforces previewed process context and early controlled softstop with unchanged hard ETL budget");
        Reject(() => new GuardianTrace(api).RunProfileCoreAsync(Path.Combine(root, "custom-long"), GuardianTraceProfiles.Get("custom"),
            2, 8, ["file"], default).GetAwaiter().GetResult(), "custom long capture rejected before effects");
        Reject(() => new GuardianTrace(api).RunProfileCoreAsync(Path.Combine(root, "custom-large"), GuardianTraceProfiles.Get("custom"),
            1, 9, ["file"], default).GetAwaiter().GetResult(), "custom enlarged ETL budget rejected");
        foreach (var format in new[] { "XML", "EVTX" })
        {
            var directory = Path.Combine(root, "conversion-" + format); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "events." + format.ToLowerInvariant());
            var reports = new List<GuardianTraceConversion>(); var converterStopped = false;
            using var stop = new CancellationTokenSource();
            try
            {
                await GuardianTraceDecoder.ConvertBoundedAsync(format, path, stop, async ct =>
                {
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                        stream.SetLength(GuardianTraceDecoder.MaximumXmlBytes + 1L);
                    try { await Task.Delay(Timeout.Infinite, ct); return new CommandResult(0, "", ""); }
                    finally { converterStopped = true; }
                }, reports.Add);
                Check(false, "decoder oversize");
            }
            catch (IOException)
            {
                Check(converterStopped && stop.IsCancellationRequested && reports.Any(r => r.State == "BudgetExceeded") &&
                    reports.Last().ObservedBytes == 67108865 && reports.Last().ExitCode is null,
                    format + " converter budget cancels and awaits converter without fabricated exit code");
            }
            finally { GuardianTraceDecoder.DeleteConversionDirectory(directory); }
            Check(!Directory.Exists(directory), format + " aborted conversion cleanup leaves no retained directory");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, "own synthetic converter output");
            using var normalStop = new CancellationTokenSource();
            var completed = new List<GuardianTraceConversion>();
            try
            {
                await GuardianTraceDecoder.ConvertBoundedAsync(format, path, normalStop,
                    _ => Task.FromResult(new CommandResult(0, "", "")), completed.Add);
                Check(completed.Single() is { State: "Completed", ExitCode: 0, ObservedBytes: > 0 } &&
                    !normalStop.IsCancellationRequested, format + " successful bounded converter retains real zero exit and measured bytes");
            }
            finally { GuardianTraceDecoder.DeleteConversionDirectory(directory); }
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                stream.SetLength(GuardianTraceDecoder.MaximumXmlBytes + 1L);
            using var completedStop = new CancellationTokenSource();
            var oversizeCompleted = new List<GuardianTraceConversion>();
            try
            {
                await GuardianTraceDecoder.ConvertBoundedAsync(format, path, completedStop,
                    _ => Task.FromResult(new CommandResult(0, "", "")), oversizeCompleted.Add);
                Check(false, "completed oversize converter");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                Check(oversizeCompleted.Any(r => r.State == "BudgetExceeded") &&
                    oversizeCompleted.Last() is { State: "Failed", ExitCode: 0, ObservedBytes: 67108865 },
                    format + " converter completing between polls still fails budget and records detection time with real exit0");
            }
            finally { GuardianTraceDecoder.DeleteConversionDirectory(directory); }
        }
        foreach (var profile in GuardianTraceProfiles.All)
        {
            var q = profile.Qualification; var real = profile.Id is "process" or "io" or "crash" or "network" or "startup";
            Check(q.Implemented && q.SyntheticallyValidated && q.RealProfileValidated == real &&
                q.RealDecoderValidated == (real || profile.Id == "custom") && q.RealLifecycleValidated &&
                q.PrivilegeContractRealValidated == (profile.Id == "cpu") && !q.ProductionReleased &&
                (real ? q.Evidence.Contains(GuardianTraceProfiles.Matrix03Sha256) : q.Evidence.Contains("-admin-01.json")),
                "separate original-backed lifecycle/decoder/privilege qualification without automatic release: " + profile.Id);
        }
    }
    private static async Task RepeatResourcesAsync(string root)
    {
        var baseline = new GuardianTraceMatrix.Resources(300, 20, 10000000, 20000000);
        Check(GuardianTraceMatrix.RepeatModes.SequenceEqual(new[] { "cold", "warm-1", "warm-2" }),
            "CPU/custom repeats are exactly cold/warm1/warm2 in the same runner");
        foreach (var handles in new[] { -1, 0, 64, 65 })
        foreach (var threads in new[] { -1, 0, 8, 9 })
            Check(GuardianTraceMatrix.WithinWarmBudget(baseline, baseline with
                { Handles = baseline.Handles + handles, Threads = baseline.Threads + threads }) == (handles <= 64 && threads <= 8),
                $"warm resource thresholds are unchanged at boundary handles={handles}, threads={threads}");
        Check(GuardianTraceMatrix.WarmPlateau(baseline, baseline), "stable warmed endstand passes");
        Check(GuardianTraceMatrix.WarmPlateau(baseline, baseline with { Handles = 299, Threads = 19 }), "falling warmed endstand passes");
        Check(!GuardianTraceMatrix.WarmPlateau(baseline, baseline with { Handles = 301 }), "even one growing warm handle blocks repeat gate");
        Check(!GuardianTraceMatrix.WarmPlateau(baseline, baseline with { Threads = 21 }), "one growing warm thread blocks repeat gate");
        var clock = Stopwatch.StartNew();
        await GuardianTraceMatrix.SettleAsync();
        Check(clock.Elapsed >= TimeSpan.FromSeconds(1), "fixed post-GC settling actually waits at least one second; no minimum-picking");
        foreach (var mode in GuardianTraceMatrix.RepeatModes)
        {
            var token = new TokenFixture(true, false); var journal = new List<GuardianCpuPrivilege.Entry>();
            using (GuardianCpuPrivilege.Enable(true, journal, token))
            {
                using var shutdown = new CancellationTokenSource();
                shutdown.Cancel();
                Check(token.Enabled && shutdown.IsCancellationRequested, mode + ": shutdown sees active scoped privilege");
            }
            Check(!token.Enabled && token.Disposed && GuardianTraceMatrix.PrivilegePassed(journal),
                mode + ": every repeat/shutdown restores and closes its own token before resource measurement");
            Check(!GuardianTraceMatrix.PrivilegePassed(journal.Where(e => e.Step != "ReadRestored").ToArray()),
                mode + ": missing restoration readback is not a successful privilege cycle");
            Check(!GuardianTraceMatrix.PrivilegePassed(journal.Concat(new[] { new GuardianCpuPrivilege.Entry("extra", false, 5, "fixture") }).ToArray()),
                mode + ": any journal failure blocks privilege acceptance");
        }
        var trace = Trace(root) with { Profile = "cpu", Components = ["process", "cpu"] };
        var provider = GuardianTraceProfiles.Providers.Single(p => p.Id == "cpu");
        Check(provider.Guid == new Guid("bfeb0324-1cee-496f-a409-2ac2b48a6322") && provider.Keywords == 1 &&
            provider.SystemLogger, "SDK-backed SystemProfileProvider/SYSTEM_PROFILE_KW_GENERAL is not replaced by CPU config events");
        var legacy = new Guid("9e814aad-3204-11d2-9a82-006008a86939");
        Check(!provider.EventProviders.Contains(legacy),
            "observed NT kernel sample provider remains outside the configured profile allowlist");
        var rows = Enumerable.Range(0, 3).Select(_ => Event(trace, 0, .1, new() { ["ThreadId"] = "51" }, legacy, 2)).ToList();
        foreach (var row in rows) row.Descendants().Single(e => e.Name.LocalName == "Opcode").Value = "46";
        for (var index = 0; index < 5; index++)
        {
            var row = Event(trace, 100 + index, .2, new() { ["ThreadId"] = "51", ["RawFixture"] = "diagnostic-only" }, version: 2);
            row.Descendants().Single(e => e.Name.LocalName == "Provider").Attribute("Guid")!.Remove();
            row.Descendants().Single(e => e.Name.LocalName == "Provider").SetAttributeValue("Name", "unverified-fixture");
            row.SetAttributeValue("AetherNativeProvider", legacy);
            rows.Add(row);
        }
        GuardianTraceAnalysis Read(bool diagnostic, params XElement[] input)
        {
            using var xml = new MemoryStream(Encoding.UTF8.GetBytes(new XElement("Events", input).ToString()));
            return GuardianTraceDecoder.ParseXml(xml, trace, default, diagnoseEventTypes: diagnostic);
        }
        var observedSample = Read(true, SampledProfileEvent(trace, .1));
        var sampleFact = observedSample.Facts.Single();
        Check(sampleFact is { Kind: "CpuSample", Availability: "Available", EventId: 0, Version: 2, Opcode: "46",
                Task: 0, Level: 0, Keywords: 0 } &&
            sampleFact.Provider == legacy &&
            sampleFact.EventGuid == new Guid("ce1dbfb4-137e-4da6-87b0-3f59aa102cbc") &&
            sampleFact.Data.GetValueOrDefault("ThreadId") == "61" &&
            !sampleFact.Data.ContainsKey("InstructionPointer") && !sampleFact.Data.ContainsKey("Count") &&
            sampleFact.PayloadFields.SequenceEqual(new[] { "InstructionPointer", "ThreadId", "Count", "Reserved" }) &&
            observedSample.Decoder!.EventTypes.Single().State == "Decoded schema: CpuSample",
            "observed NT kernel sample is decoded only for its exact descriptor and payload without retaining instruction pointers");
        GuardianTraceDecoder.ValidateAnalysis(trace, observedSample);
        Check(true, "observed CPU sample passes ETL fact validation without widening the profile provider allowlist");
        var persistedSample = JsonSerializer.Deserialize<GuardianTraceAnalysis>(JsonSerializer.Serialize(observedSample))!;
        GuardianTraceDecoder.ValidateAnalysis(trace, persistedSample);
        Check(persistedSample.Facts.Single().PayloadFields.SequenceEqual(sampleFact.PayloadFields),
            "CPU sample schema field names survive safe analysis serialization and validation");
        Reject(() => GuardianTraceDecoder.ValidateAnalysis(trace, persistedSample with
        { Facts = [sampleFact with { PayloadFields = Enumerable.Repeat("ThreadId", 65).ToArray() }] }),
            "serialized fact payload field metadata remains bounded");
        foreach (var threadId in new[] { "61", " 61", "61 ", " 61 " })
        {
            var padded = Read(false, SampledProfileEvent(trace, .1, threadId));
            Check(padded.Facts.Single().Data.GetValueOrDefault("ThreadId") == "61",
                "observed CPU sample ThreadId is canonicalized after trimming outer whitespace only");
            GuardianTraceDecoder.ValidateAnalysis(trace, padded);
        }
        foreach (var invalidThreadId in new[] { "", " ", "0", "-1", "+1", "0x3D", "6 1", "4294967296" })
        {
            var invalid = Read(false, SampledProfileEvent(trace, .1, invalidThreadId));
            Check(invalid.Facts.Count == 0 && invalid.Decoder is { MalformedFields: > 0 },
                "invalid observed CPU ThreadId representation is rejected");
        }
        var missingThread = SampledProfileEvent(trace, .1);
        missingThread.Elements().Single(e => e.Name.LocalName == "EventData").Elements()
            .Single(e => e.Attribute("Name")?.Value == "ThreadId").Remove();
        var missingThreadAnalysis = Read(false, missingThread);
        Check(missingThreadAnalysis.Facts.Count == 0 && missingThreadAnalysis.Decoder is { MalformedFields: > 0 },
            "observed CPU sample without ThreadId is rejected");
        var duplicateThread = SampledProfileEvent(trace, .1);
        duplicateThread.Elements().Single(e => e.Name.LocalName == "EventData")
            .Add(new XElement(duplicateThread.Name.Namespace + "Data", new XAttribute("Name", "ThreadId"), "62"));
        var duplicateThreadAnalysis = Read(false, duplicateThread);
        Check(duplicateThreadAnalysis.Facts.Count == 0 && duplicateThreadAnalysis.Decoder is { MalformedFields: > 0 },
            "observed CPU sample with duplicate ThreadId is rejected");
        var extraPayload = SampledProfileEvent(trace, .1);
        extraPayload.Elements().Single(e => e.Name.LocalName == "EventData")
            .Add(new XElement(extraPayload.Name.Namespace + "Data", new XAttribute("Name", "UnexpectedField"), "not-retained"));
        var extraPayloadAnalysis = Read(false, extraPayload);
        Check(extraPayloadAnalysis.Facts.Count == 0 && extraPayloadAnalysis.Decoder is { MalformedFields: > 0 },
            "observed CPU sample with an additional payload field is rejected without retaining its value");
        var invalidSample = SampledProfileEvent(trace, .1);
        invalidSample.Elements().Single(e => e.Name.LocalName == "EventData").Elements()
            .Single(e => e.Attribute("Name")?.Value == "InstructionPointer").Remove();
        var invalidSampleAnalysis = Read(false, invalidSample);
        Check(invalidSampleAnalysis.Facts.Count == 0 &&
            invalidSampleAnalysis.Decoder is { MalformedFields: 1, UnknownSchemasSkipped: 1 },
            "sample descriptor with missing required payload is reported malformed and never promoted");
        var wrongDescriptor = SampledProfileEvent(trace, .1);
        wrongDescriptor.Elements().Single(e => e.Name.LocalName == "System")
            .Elements().Single(e => e.Name.LocalName == "Level").Value = "1";
        var wrongDescriptorAnalysis = Read(false, wrongDescriptor);
        Check(wrongDescriptorAnalysis.Facts.Count == 0 && wrongDescriptorAnalysis.RejectedProviders == 1,
            "other events from the observed provider remain outside the narrow sample exception");
        var wrongEventGuid = SampledProfileEvent(trace, .1);
        wrongEventGuid.Elements().Single(e => e.Name.LocalName == "ExtendedTracingInfo")
            .Elements().Single(e => e.Name.LocalName == "EventGuid").Value = "{01853a65-418f-4f36-aefc-dc0f1d2fd235}";
        var wrongEventGuidAnalysis = Read(false, wrongEventGuid);
        Check(wrongEventGuidAnalysis.Facts.Count == 0 && wrongEventGuidAnalysis.RejectedProviders == 1,
            "matching provider descriptor and payload with an unobserved EventGuid are rejected");
        var invalidDescriptors = new (string Name, Action<XElement> Mutate)[]
        {
            ("outer provider", e => e.Elements().Single(x => x.Name.LocalName == "System")
                .Elements().Single(x => x.Name.LocalName == "Provider").SetAttributeValue("Guid", Guid.NewGuid().ToString("B"))),
            ("event ID", e => e.Elements().Single(x => x.Name.LocalName == "System")
                .Elements().Single(x => x.Name.LocalName == "EventID").Value = "1"),
            ("version", e => e.Elements().Single(x => x.Name.LocalName == "System")
                .Elements().Single(x => x.Name.LocalName == "Version").Value = "3"),
            ("task", e => e.Elements().Single(x => x.Name.LocalName == "System")
                .Elements().Single(x => x.Name.LocalName == "Task").Value = "1"),
            ("opcode", e => e.Elements().Single(x => x.Name.LocalName == "System")
                .Elements().Single(x => x.Name.LocalName == "Opcode").Value = "45"),
            ("level", e => e.Elements().Single(x => x.Name.LocalName == "System")
                .Elements().Single(x => x.Name.LocalName == "Level").Value = "1"),
            ("keywords", e => e.Elements().Single(x => x.Name.LocalName == "System")
                .Elements().Single(x => x.Name.LocalName == "Keywords").Value = "0x1")
        };
        foreach (var (name, mutate) in invalidDescriptors)
        {
            var invalidDescriptor = SampledProfileEvent(trace, .1);
            mutate(invalidDescriptor);
            var rejectedDescriptor = Read(false, invalidDescriptor);
            Check(rejectedDescriptor.Facts.Count == 0 && rejectedDescriptor.RejectedProviders == 1,
                "observed CPU sample with incorrect " + name + " remains outside the narrow exception");
        }
        Reject(() => GuardianTraceDecoder.ValidateAnalysis(trace, observedSample with
        { Facts = [sampleFact with { EventGuid = null }] }),
            "serialized CPU facts cannot pass validation without the observed EventGuid");
        GuardianTraceFactContractException RejectFact(GuardianTraceFact invalidFact)
        {
            var invalidAnalysis = observedSample with
            {
                TotalEvents = 1,
                Facts = [invalidFact],
                Decoder = observedSample.Decoder! with { EventsParsed = 1, FactsProduced = 1 }
            };
            try
            {
                GuardianTraceDecoder.ValidateAnalysis(trace, invalidAnalysis);
                throw new InvalidOperationException("Expected CPU fact rejection was not thrown.");
            }
            catch (GuardianTraceFactContractException ex) { return ex; }
        }
        Check(RejectFact(sampleFact with { Provider = Guid.NewGuid() }).RejectionEvidence.Single().Reason == "ProviderMismatch",
            "final CPU fact diagnostic identifies a provider mismatch");
        Check(RejectFact(sampleFact with { EventGuid = Guid.NewGuid() }).RejectionEvidence.Single().Reason == "EventGuidMismatch",
            "final CPU fact diagnostic identifies an EventGuid mismatch");
        Check(RejectFact(sampleFact with { Version = 3 }).RejectionEvidence.Single().Reason == "DescriptorMismatch",
            "final CPU fact diagnostic identifies a descriptor mismatch");
        Check(RejectFact(sampleFact with { PayloadFields = ["ThreadId"] }).RejectionEvidence.Single().Reason == "PayloadSchemaMismatch",
            "final CPU fact diagnostic identifies a payload schema mismatch");
        Check(RejectFact(sampleFact with { Kind = "Unknown" }).RejectionEvidence.Single().Reason == "KindMismatch",
            "final CPU fact diagnostic identifies a fact kind mismatch");
        Check(RejectFact(sampleFact with { Data = new Dictionary<string, string>() }).RejectionEvidence.Single().Reason == "ThreadIdMissing",
            "final CPU fact diagnostic identifies a missing ThreadId");
        var invalidFactEvidence = RejectFact(sampleFact with
        { Data = new Dictionary<string, string> { ["ThreadId"] = "SENSITIVE_RAW_SENTINEL" } });
        Check(invalidFactEvidence.RejectionEvidence.Single().Reason == "ThreadIdInvalid",
            "final CPU fact diagnostic identifies an invalid ThreadId");
        var serializedEvidence = JsonSerializer.Serialize(invalidFactEvidence.RejectionEvidence);
        Check(!serializedEvidence.Contains("SENSITIVE_RAW_SENTINEL", StringComparison.Ordinal) &&
            !serializedEvidence.Contains("0x0000000000001000", StringComparison.Ordinal) &&
            invalidFactEvidence.RejectionEvidence.Single().PayloadFields.SequenceEqual(sampleFact.PayloadFields) &&
            invalidFactEvidence.RejectionEvidence.Single().PayloadTypes.Count == sampleFact.PayloadTypes.Count,
            "bounded CPU rejection evidence contains only contract metadata and no raw payload values");
        var cappedFailures = Enumerable.Range(0, GuardianTraceDecoder.MaximumRejectionEvidence + 2)
            .Select(index => sampleFact with { Sequence = index + 1, Kind = "Unknown" }).ToArray();
        var cappedError = RejectFactSet(cappedFailures);
        Check(cappedError.RejectionEvidence.Count == GuardianTraceDecoder.MaximumRejectionEvidence &&
            cappedError.DroppedEvidence == 2,
            "CPU fact rejection evidence is bounded and counts omitted entries");
        var analysis = Read(true, rows.ToArray());
        Check(analysis.Decoder is { MalformedFields: 5, UnknownProvidersSkipped: 3 } &&
            !analysis.Facts.Any(f => f.Kind == "CpuSample"), "legacy opcode46 and missing GUID are not promoted to samples by diagnostics");
        Check(analysis.Decoder!.FieldIssues.Select(i => i.EventId).SequenceEqual(Enumerable.Range(100, 5)) &&
            analysis.Decoder.FieldIssues.All(i => i.Version == 2 && i.Field == "Provider.Guid" && i.RawValue is null && i.Required),
            "each missing GUID retains actual available event metadata without inventing a provider");
        Check(analysis.Decoder.EventTypes.Count == 6 && analysis.Decoder.EventTypes.Single(e => e.Provider == legacy.ToString("B")).Count == 3 &&
            analysis.Decoder.EventTypes.Count(e => e.State.StartsWith("Malformed", StringComparison.Ordinal) &&
                e.NativeProvider == legacy.ToString() && e.Xml!.Contains("RawFixture")) == 5,
            "bounded diagnostic keeps each missing-provider event and grouped legacy descriptors/native side evidence/raw payload");
        Check(Read(false, rows.ToArray()).Decoder!.EventTypes.Count == 0 &&
            !JsonSerializer.Serialize(Read(false, rows.ToArray())).Contains("diagnostic-only"),
            "ordinary decode never retains rejected raw diagnostic payload");
        GuardianTraceDecoder.ValidateAnalysis(trace, analysis); Check(true, "new diagnostic schema validates without qualifying unknown events");
        var many = Enumerable.Range(0, 70).Select(i => Event(trace, i, .1, provider: legacy)).ToArray();
        var bounded = Read(true, many);
        Check(bounded.Decoder is { EventTypes.Count: 56, EventTypeDiagnosticsDropped: 14 } &&
            bounded.Decoder.EventTypes.Count(e => e.Xml is not null) == 8,
            "diagnostic descriptor/raw budgets remain bounded and dropped descriptors are explicit");
        var afterFlood = Read(true, many.Concat(rows.Skip(3)).ToArray());
        Check(afterFlood.Decoder!.EventTypes.Count(e => e.State.StartsWith("Malformed", StringComparison.Ordinal)) == 5,
            "invalid provider records have reserved slots after a flood of regular descriptors");
        var oversized = Event(trace, 3, .1, new() { ["RawFixture"] = new string('x', 8193) }, legacy);
        Check(Read(true, oversized).Decoder!.EventTypes.Single().Xml is null, "oversized diagnostic XML omitted rather than truncated as valid XML");
        Reject(() => GuardianTraceDecoder.ValidateAnalysis(trace, analysis with
        { Decoder = analysis.Decoder with { EventTypes = [analysis.Decoder.EventTypes[0] with { Xml = new string('x', 8193) }] } }),
            "untrusted diagnostic raw XML over the recorded budget is rejected");

        GuardianTraceFactContractException RejectFactSet(IReadOnlyList<GuardianTraceFact> invalidFacts)
        {
            var invalidAnalysis = observedSample with
            {
                TotalEvents = invalidFacts.Count,
                Facts = invalidFacts,
                Decoder = observedSample.Decoder! with { EventsParsed = invalidFacts.Count, FactsProduced = invalidFacts.Count }
            };
            try
            {
                GuardianTraceDecoder.ValidateAnalysis(trace, invalidAnalysis);
                throw new InvalidOperationException("Expected CPU fact rejection was not thrown.");
            }
            catch (GuardianTraceFactContractException ex) { return ex; }
        }
    }
    private static void Ui(string root)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { UiCore(root); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static void UiCore(string root)
    {
        var view = new GuardianView(Path.Combine(root, "ui")) { Synthetic = true };
        var profiles = (ComboBox)view.FindName("TraceProfile");
        Check(profiles.Items.Count == 7, "profile selector exposes all contracts without starting native work");
        var gate = (TextBlock)view.FindName("TraceGate");
        var area = (ComboBox)view.FindName("Area");
        Check(area.Items.Cast<ComboBoxItem>().Select(item => item.Tag?.ToString()).SequenceEqual(new[]
            {
                "Recovery Vault", "Change Journal", "Rollback Center", "Recovery Readiness", "Softwaremanifest",
                "Installation Observer", "Update Intelligence", "Driver Guard", "Crash Lab", "Deep Trace",
                "Maintenance Window", "Rescue Package"
            }), "localized Guardian labels retain stable internal route keys");
        var targetKind = (ComboBox)view.FindName("TargetKind");
            var actionBox = (ComboBox)view.FindName("Action");
            Check(targetKind.Items.Cast<ComboBoxItem>().Select(item =>
                    (item.Content?.ToString() ?? "", item.Tag?.ToString() ?? ""))
                .SequenceEqual(new[] { ("Dienst", "service"), ("Geplante Aufgabe", "task"), ("Autostart", "startup") }) &&
                actionBox.Items.Cast<ComboBoxItem>().Select(item =>
                    (item.Content?.ToString() ?? "", item.Tag?.ToString() ?? ""))
                .SequenceEqual(new[] { ("Starten", "start"), ("Beenden", "stop"), ("Neu starten", "restart"),
                    ("Aktivieren", "enable"), ("Deaktivieren", "disable") }),
            "localized change targets and actions preserve exact internal values");
        Check(string.IsNullOrEmpty(((TextBox)view.FindName("EntryName")).Text) &&
            ((TextBlock)view.FindName("EntryNameLabel")).Text == "Name des Vorgangs (optional)",
            "optional Guardian entry name has a visible label and no implied default value");
        for (var index = 0; index < profiles.Items.Count; index++)
        {
            profiles.SelectedIndex = index;
            Check(!((Button)view.FindName("TraceStart")).IsEnabled &&
                gate.Text.Contains("Produktfreigabe: Nicht erteilt", StringComparison.Ordinal) &&
                !gate.Text.Contains("ProductionReleased=", StringComparison.Ordinal) &&
                !gate.Text.Contains("RealProfileValidated=", StringComparison.Ordinal) &&
                !gate.Text.Contains("True", StringComparison.Ordinal) &&
                !gate.Text.Contains("False", StringComparison.Ordinal),
                "profile " + index + " keeps production lock and shows a human-readable qualification");
        }
        profiles.SelectedItem = GuardianTraceProfiles.Get("cpu");
        Check(gate.Text.Contains("Reales Profil: Nicht bestanden", StringComparison.Ordinal) &&
            gate.Text.Contains("163/190/168", StringComparison.Ordinal),
            "CPU profile displays its failed malformed-field gate and real-run counts");
        profiles.SelectedItem = GuardianTraceProfiles.Get("custom");
        Check(gate.Text.Contains("Reales Profil: Ausstehend", StringComparison.Ordinal) &&
            gate.Text.Contains("Ressourcenwiederholung ausstehend", StringComparison.Ordinal),
            "Custom profile distinguishes pending resource qualification from a passing gate");
        var details = (Expander)view.FindName("DetailsExpander");
        Check(!details.IsExpanded && details.Header.ToString()!.Contains("JSON", StringComparison.Ordinal),
            "technical JSON details are explicitly labelled and collapsed by default");
        view.ShutdownAsync().GetAwaiter().GetResult();
        Check(view.Pending.IsCompleted, "UI shutdown awaits own cancellable work");
        foreach (var mode in new[] { "cancel", "shutdown" })
        {
            var directory = Path.Combine(root, "ui-" + mode); var api = new Api(directory, "time");
            var activeView = new GuardianView(Path.Combine(root, "view-" + mode)) { Synthetic = true };
            using var began = new ManualResetEventSlim();
            GuardianTraceResult? result = null;
            Func<CancellationToken, Task> action = async token =>
                result = await new GuardianTrace(api).RunProfileCoreAsync(directory, GuardianTraceProfiles.Get("process"), 2, 8, null, token,
                    (name, code) => { if (name == "StartTrace" && code == 0) began.Set(); }).ConfigureAwait(false);
            typeof(GuardianView).GetMethod("Run", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(activeView, [action]);
            Check(began.Wait(TimeSpan.FromSeconds(5)), "UI " + mode + " test reaches injected native start");
            Check(((Button)activeView.FindName("CancelButton")).Visibility == Visibility.Visible,
                "UI " + mode + " exposes cancellation only while its own work is active");
            if (mode == "cancel")
            {
                typeof(GuardianView).GetMethod("CancelClicked", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(activeView, [activeView, new RoutedEventArgs()]);
                Wait(activeView.Pending);
            }
            Wait(activeView.ShutdownAsync());
            Check(result?.State == "Cancelled" && !api.Active && api.Stops == 1, "actual UI " + mode + " propagates to own trace cleanup (injected native API)");
            Check(((Button)activeView.FindName("CancelButton")).Visibility == Visibility.Collapsed,
                "UI " + mode + " hides cancellation when no work remains");
        }
    }
    private static void Wait(Task task)
    {
        var clock = Stopwatch.StartNew();
        while (!task.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(10))
        { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("Eigene UI-Testarbeit beendet sich nicht.");
        task.GetAwaiter().GetResult();
    }
}
