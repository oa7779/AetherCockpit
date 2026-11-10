using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AetherOperations;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class SystemIntelligenceTests
{
    private static int _assertions;
    private static void Check(bool value, string message)
    { _assertions++; if (!value) throw new InvalidOperationException("System Intelligence: " + message); }
    private static void Reject(Action action, string message)
    {
        var rejected = false;
        try { action(); } catch (Exception ex) when (ex is JsonException or ArgumentException or IOException or System.Xml.XmlException) { rejected = true; }
        Check(rejected, message);
    }
    internal static IntelligenceSnapshot Fixture(int count = 10)
    {
        var now = DateTimeOffset.UtcNow;
        return new(1, Guid.NewGuid(), "Synthetische Testbaseline", now,
            Enumerable.Range(0, count).Select(i => IntelligenceSources.Entity("fixture:" + i, (IntelligenceKind)(i % 12), $"Fixture {i:00000}",
                "Fixture", "Synthetische Fixture, keine echten Systemdaten", now, i % 12 == 5 ?
                    new Dictionary<string, string> { ["value"] = i.ToString(), ["protocol"] = "TCP", ["localAddress"] = "::1",
                        ["localPort"] = i.ToString(), ["remoteAddress"] = "2001:db8::1", ["remotePort"] = "65535", ["state"] = "Listen" } :
                    new Dictionary<string, string> { ["value"] = i.ToString() })).ToArray(),
            Enumerable.Range(1, count - 1).Select(i => new IntelligenceEdge("fixture:" + (i - 1), "fixture:" + i, "Synthetische Kante",
                IntelligenceModel.Metadata("Fixture", now))).ToArray(),
            [new("Fixture", true, IntelligenceModel.Metadata("Fixture", now))], "synthetic-fixture-host");
    }
    internal static int Run()
    {
        _assertions = 0;
        var fixture = Fixture(); IntelligenceModel.Validate(fixture);
        var roundtrip = JsonSerializer.Deserialize<IntelligenceSnapshot>(JsonSerializer.Serialize(fixture))!;
        IntelligenceModel.Validate(roundtrip);
        Check(IntelligenceModel.Compare(fixture, roundtrip).All(c => c.Kind == ChangeKind.Unchanged), "snapshot JSON roundtrip preserves values and identities");
        Reject(() => IntelligenceModel.Validate(fixture with { Version = 99 }), "unknown schema rejected");
        Reject(() => IntelligenceModel.Validate(fixture with { Entities = fixture.Entities.Concat([fixture.Entities[0]]).ToArray() }), "duplicate identities rejected");
        Reject(() => IntelligenceModel.Validate(fixture with { Edges = [fixture.Edges[0] with { To = "missing" }] }), "dangling edge rejected");
        Reject(() => IntelligenceModel.Validate(fixture with { Coverage = [fixture.Coverage[0] with { Metadata = fixture.Coverage[0].Metadata.AsStale() }] }), "complete stale source rejected");
        Reject(() => IntelligenceModel.Validate(fixture with { Entities = [fixture.Entities[0] with { Metadata = IntelligenceModel.Metadata("Secret", fixture.Timestamp, privacy: DataClassification.Secret) }] }), "secrets never enter persistent entity state");
        var subset = fixture with { Entities = fixture.Entities.Take(5).ToArray(), Edges = fixture.Edges.Take(4).ToArray() };
        Check(IntelligenceModel.Compare(fixture, subset).Count(c => c.Kind == ChangeKind.Removed) == 5, "complete empty or partial observation permits known removal");
        var incomplete = subset with { Coverage = [fixture.Coverage[0] with { Complete = false }] };
        Check(IntelligenceModel.Compare(fixture, incomplete).All(c => c.Kind != ChangeKind.Removed), "incomplete source never fabricates mass removal");
        Check(IntelligenceModel.Compare(fixture, incomplete).Count(c => c.Kind == ChangeKind.Unchanged) == 5, "known matching identities still compare despite partial domain");
        var empty = fixture with { Entities = [], Edges = [] };
        Check(IntelligenceModel.Compare(fixture, empty).Count(c => c.Kind == ChangeKind.Removed) == 10, "explicit successful empty source differs from missing source");
        Check(IntelligenceModel.Compare(fixture, empty with { Coverage = [] }).All(c => c.Kind != ChangeKind.Removed), "absent coverage does not mean empty");
        Check(IntelligenceModel.Path("fixture:0", "fixture:9", fixture.Edges).Count == 10, "real graph path found");
        Check(IntelligenceModel.Path("fixture:9", "fixture:0", fixture.Edges).Count == 10, "relationship exploration may traverse reverse edges without reversing causal claims");
        Check(IntelligenceModel.Path("fixture:0", "unknown", fixture.Edges).Count == 0, "no invented disconnected path");
        Check(IntelligenceModel.Path("fixture:0", "fixture:9", fixture.Edges.Concat([fixture.Edges[0] with { From = "fixture:9", To = "fixture:0" }]).ToArray()).Count == 2, "cycles and duplicate graph edges terminate");
        foreach (var format in new[] { ".json", ".csv", ".txt" })
        {
            var report = IntelligenceModel.Export(fixture, [], format, false);
            Check(!report.Contains("Fixture 00001", StringComparison.Ordinal) && report.Contains("REDACTED", StringComparison.Ordinal), "privacy report redacts identity fields: " + format);
            Check(IntelligenceModel.Export(fixture, [], format, true).Contains("Fixture 00001", StringComparison.Ordinal), "consent exposes only permitted fields: " + format);
        }
        var fields = IntelligenceWindowsEvents.ReadFields("""
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event"><EventData>
            <Data Name="BootTime">12345</Data><Data Name="Password">do-not-store</Data><Data Name="CommandLine">private</Data>
            </EventData></Event>
            """);
        Check(fields.Count == 1 && fields["BootTime"] == "12345", "event allowlist excludes arbitrary secret or command fields");
        Reject(() => IntelligenceWindowsEvents.ReadFields("<!DOCTYPE x [<!ENTITY z SYSTEM 'file:///private'>]><x>&z;</x>"), "DTD event payload refused");
        Check(IntelligenceWindowsEvents.Category("Application Error", 1000, 2) == "ApplicationCrash" &&
            IntelligenceWindowsEvents.Category("Other", 1000, 4) == "WindowsEvent", "known provider and ID required for crash");
        Check(IntelligenceWindowsEvents.ComparableBoots(fixture.Entities).Count == 0, "generic boot labels do not imply comparable Windows boot timings");
        var time = DateTimeOffset.UtcNow;
        var rule = new IntelligenceRule(Guid.NewGuid(), "CPU high", WatchTrigger.Cpu, "", 80, 4, 30, WatchAction.Timeline);
        var watch = new WatchEvaluator();
        Check(!watch.Evaluate(rule, 90, time, time), "duration starts with first valid high sample");
        Check(!watch.Evaluate(rule, 90, time.AddSeconds(2), time.AddSeconds(2)), "duration not reached");
        Check(watch.Evaluate(rule, 90, time.AddSeconds(4), time.AddSeconds(4)), "duration measured in source time");
        Check(!watch.Evaluate(rule, 90, time.AddSeconds(6), time.AddSeconds(6)), "latched threshold does not spam");
        Check(!watch.Evaluate(rule, 10, time.AddSeconds(8), time.AddSeconds(8)), "fall resets incident");
        Check(!watch.Evaluate(rule, 90, time.AddSeconds(10), time.AddSeconds(10)), "cooldown suppresses rearm");
        Check(!watch.Evaluate(rule, 90, time.AddSeconds(12), time.AddSeconds(40)), "stale numeric value does not fire");
        watch.Reset(rule.Id);
        Check(!watch.Evaluate(rule, 90, time, time) && !watch.Evaluate(rule, 90, time.AddSeconds(20), time.AddSeconds(20)), "gap breaks continuous duration");
        Check(!watch.Evaluate(rule, double.NaN, time.AddSeconds(22), time.AddSeconds(22)), "NaN unknown not zero or valid");
        Check(!watch.Evaluate(rule with { Enabled = false }, 90, time.AddSeconds(24), time.AddSeconds(24)), "disabled rule does no action");
        Reject(() => (rule with { Trigger = WatchTrigger.Crash, DurationSeconds = 4 }).Validate(), "event rule cannot pretend continuous measurement");
        var clock = new ManualClock(time);
        var engine = new IntelligenceSessionEngine(clock);
        engine.UpdateRule(rule);
        engine.Mark("ApplicationCrash", "Fixture crash", "Synthetic only", time, "fixture-app");
        var duplicate = engine.Timeline.Single();
        engine.Add(duplicate); Check(engine.Timeline.Count == 1, "equal source key deduplicates");
        engine.Add(duplicate with { Id = "late", Occurred = time.AddSeconds(-20) });
        Check(IntelligenceModel.Reliability(engine.Timeline).Single().First == time.AddSeconds(-20), "late arrival retains source ordering");
        Check(IntelligenceModel.Reliability(engine.Timeline).Single().Count == 2, "repeated source signature not inferred cause");
        var process = ProcessCommandTests.Row(2345, time.UtcTicks - 10000000) with { ObservedAt = time, CpuPercent = 90 };
        DashboardSnapshot Dashboard(DateTimeOffset t, params ProcessMetric[] rows) => new(t, [0], rows, [], 1, 2, 1, 1, 1, TimeSpan.FromMinutes(10), []);
        LiveSample Live(DateTimeOffset t, double? cpu = 90) => new(t, cpu, 50, 1, 2, 0, 0, [], []);
        engine.Apply(Dashboard(time, process), Live(time), () => fixture);
        engine.Apply(Dashboard(time.AddSeconds(2), process with { StartTicks = process.StartTicks + 1 }), Live(time.AddSeconds(2)), () => fixture);
        Check(engine.Timeline.Count(e => e.Category == "ProcessAppeared") == 1 && engine.Timeline.Count(e => e.Category == "ProcessMissing") == 1, "PID reuse is new identity plus unobserved earlier identity");
        engine.Apply(Dashboard(time.AddSeconds(4), process with { StartTicks = 0 }), Live(time.AddSeconds(4)), () => fixture);
        Check(engine.Timeline.Count(e => e.Category == "ProcessMissing") == 1, "access-denied row does not fake termination");
        for (var i = 3; i < 150; i++) engine.Apply(Dashboard(time.AddSeconds(i * 2)), Live(time.AddSeconds(i * 2), 5), () => fixture);
        Check(engine.Resources.Count == 120, "bounded shared resource history");
        Check(engine.Boots.Single().Complete && engine.Boots.Single().Criterion.Contains("keine Windows", StringComparison.Ordinal), "explicit AETHER idle criterion never implies Windows fully booted");
        engine.Freeze().Validate();
        var restored = new IntelligenceSessionEngine(); restored.Restore(engine.Freeze());
        Check(restored.Timeline.Count == engine.Timeline.Count && restored.Rules.Count == 1, "restart restores retained state without pending duration");
        restored.Restore(new(1, [], [], [], new Dictionary<Guid, DateTimeOffset>()));
        Check(restored.Timeline.Count == 0 && restored.Rules.Count == 0, "reset really clears retained entries");
        engine.Start(fixture, 60, "Ressourcen");
        var recordTime = DateTimeOffset.UtcNow.AddSeconds(2);
        engine.Apply(Dashboard(recordTime, process), Live(recordTime), () => fixture);
        var recording = engine.Stop(fixture, recordTime);
        Check(recording.Samples.Count == 1 && recording.Markers.Count == 0 && !engine.IsRecording, "resource-only profile stops without timeline payload");
        recording.Validate();
        engine.Start(fixture, 2, "Ressourcen und Timeline");
        clock.Advance(TimeSpan.FromSeconds(3));
        recordTime = DateTimeOffset.UtcNow.AddSeconds(3);
        engine.Apply(Dashboard(recordTime), Live(recordTime), () => fixture);
        Check(!engine.IsRecording && engine.Completed is { Markers.Count: > 0 }, "duration auto-stops owned recorder");
        Extended(fixture, time);
        Storage(fixture);
        var parsed = CommandRunner.PowerShellAsync("$tokens=$null; $errors=$null; [void][Management.Automation.Language.Parser]::ParseInput(" +
            AdminManagement.Quote(IntelligenceSources.InventoryScript) + ",[ref]$tokens,[ref]$errors); [string]$errors.Count", default).GetAwaiter().GetResult();
        Check(parsed.Trim() == "0", "Windows inventory script parses without executing any system action");
        return _assertions;
    }
    private static void Extended(IntelligenceSnapshot fixture, DateTimeOffset time)
    {
        Check(IntelligenceModel.Compare(fixture, fixture with { HostScope = "other" }).All(c => c.Kind == ChangeKind.NotComparable), "foreign host never implies identical local identities");
        Check(IntelligenceModel.Compare(fixture, fixture with { HostScope = null }).All(c => c.Kind == ChangeKind.NotComparable), "legacy unknown host never implies identical local identities");
        Reject(() => IntelligenceModel.Import("[]"), "non-object import rejected explicitly");
        Reject(() => IntelligenceModel.Validate(fixture with { Entities = [null!] }), "nested null entity rejected");
        Reject(() => IntelligenceModel.Validate(fixture with { Coverage = [null!] }), "nested null coverage rejected");
        Reject(() => IntelligenceModel.Validate(fixture with { Edges = [null!] }), "nested null edge rejected");
        var secret = IntelligenceModel.Metadata("Secret", time, privacy: DataClassification.Secret);
        Reject(() => IntelligenceModel.Validate(fixture with { Edges = [fixture.Edges[0] with { Metadata = secret }] }), "secret relation rejected before persistence");
        Reject(() => IntelligenceModel.Validate(fixture with { Coverage = [fixture.Coverage[0] with { Metadata = secret }] }), "secret coverage rejected before persistence");
        var legacy = new ProcessSystemSnapshot("aether.systemsnapshot/1", time,
            [new(new(2345, time.UtcTicks - 10000000), "Fixture", 1, 2, time)], [], [new("fixture-hardware", "Unavailable", time)]);
        var imported = IntelligenceStore.ImportSnapshot(legacy.LocalJson());
        Check(imported.Entities.Count == 2 && imported.HostScope is null && imported.Coverage.All(c => !c.Complete), "legacy imports preserve hardware availability without inventing scope or completeness");
        var domains = new[] { "PnP", "Driver", "KernelService", "ReliabilityIndex", "Boot", "NetworkInventory" };
        var sources = domains.Select(d => new { Domain = d, Complete = true, Source = "Fixture" }).ToArray();
        var parsed = IntelligenceSources.ParseInventory(JsonSerializer.Serialize(new { Rows = Array.Empty<object>(), Sources = sources }));
        Check(parsed.Entities.Count == 0 && parsed.Coverage.All(c => c.Complete), "successful empty native inventories explicitly represented");
        Reject(() => IntelligenceSources.ParseInventory("""{"Rows":[],"Sources":[]}"""), "absent native coverage rejected");
        Reject(() => IntelligenceSources.ParseInventory(JsonSerializer.Serialize(new { Rows = new object?[] { null }, Sources = sources })), "null native row rejected");
        var row = new { Id = "device:fixture", Kind = "Hardware", Domain = "PnP", Name = "Fixture", Source = "Fixture", Timestamp = time, Fields = new Dictionary<string, string>() };
        Reject(() => IntelligenceSources.ParseInventory(JsonSerializer.Serialize(new { Rows = new[] { row, row }, Sources = sources })), "duplicate native identifiers rejected");
        var field = IntelligenceWindowsEvents.ReadFields("""
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event"><System><Provider Name="Application Error"/><EventID>1000</EventID></System>
            <EventData><Data Name="AppName">fixture.exe</Data><Data Name="AppVersion">1</Data><Data Name="CommandLine">secret</Data></EventData></Event>
            """);
        Check(field["eventSubject"] == "fixture.exe / 1" && !field.ContainsKey("CommandLine"), "known crash schema exposes reported subject but never arbitrary commands");
        var crashEntity = fixture.Entities[0] with { Fields = new Dictionary<string, string> {
            ["provider"] = "Application Error", ["eventId"] = "1000", ["ProcessId"] = "0x1234" } };
        Check(IntelligenceWindowsEvents.ProcessTarget(crashEntity, out var targetPid) && targetPid == 0x1234, "only schema-qualified EventData PID can be a process target");
        Check(!IntelligenceWindowsEvents.ProcessTarget(crashEntity with { Fields = new Dictionary<string, string> {
            ["provider"] = "Other", ["eventId"] = "1000", ["ProcessId"] = "0x1234" } }, out _), "logger or unrelated provider PID does not imply process relationship");
        var engine = new IntelligenceSessionEngine();
        var entry = new TimelineEntry("event:fixture", time, time, "ApplicationCrash", "Fixture", null, fixture.Entities[0].Metadata, "",
            new("Application", 1, "Application Error", 1000));
        engine.Add(entry);
        engine.Add(entry with { Event = entry.Event! with { Subject = "fixture.exe" } });
        engine.Add(entry with { Id = "event:fixture2", Event = entry.Event! with { Subject = "other.exe" } });
        Check(engine.Timeline.Count == 2 && IntelligenceModel.Reliability(engine.Timeline).Count == 2, "structured enrichment preserves deduplication and separates proven subjects");
        engine.Add(entry with { Id = "old", Occurred = time.AddDays(-3) });
        engine.Trim(time, 1); Check(engine.Timeline.All(e => e.Id != "old"), "per-event retention trims recently rewritten sessions");
        foreach (var (trigger, kind, domain) in new[]
        {
            (WatchTrigger.NewListener, IntelligenceKind.Endpoint, "TCP"), (WatchTrigger.NewStartup, IntelligenceKind.Startup, "Autostart"),
            (WatchTrigger.NewService, IntelligenceKind.Service, "Dienst"), (WatchTrigger.HardwareChanged, IntelligenceKind.Hardware, "PnP")
        })
        {
            var subject = IntelligenceSources.Entity("new:" + domain, kind, "Fixture", domain, "Fixture", time,
                new Dictionary<string, string> { ["state"] = "Listen" });
            var after = fixture with { Timestamp = time, Entities = [subject], Edges = [], Coverage = [new(domain, true, subject.Metadata)] };
            var before = after with { Entities = [] };
            var rule = new IntelligenceRule(Guid.NewGuid(), trigger.ToString(), trigger, "", 1, 0, 60, WatchAction.DiagnosisMarker);
            var evaluator = new IntelligenceSessionEngine(); evaluator.UpdateRule(rule);
            evaluator.ObserveChanges(before, after);
            Check(evaluator.Timeline.Count(e => e.Category == "DiagnosisMarker") == 1, trigger + " actual complete inventory fires selected safe action");
            evaluator.ObserveChanges(before, after);
            Check(evaluator.Timeline.Count(e => e.Category == "DiagnosisMarker") == 1, trigger + " cooldown prevents repeat");
            evaluator = new(); evaluator.UpdateRule(rule);
            evaluator.ObserveChanges(before, after with { Coverage = [after.Coverage[0] with { Complete = false }] });
            Check(evaluator.Timeline.All(e => e.Category != "DiagnosisMarker"), trigger + " partial source cannot fabricate new inventory");
        }
        var crash = new IntelligenceRule(Guid.NewGuid(), "Fixture", WatchTrigger.Crash, "", 1, 0, 60, WatchAction.Notification);
        Reject(() => (crash with { Trigger = WatchTrigger.ProcessAppeared, Threshold = 0 }).Validate(), "zero occurrence never means a new process");
        Reject(() => (crash with { Trigger = WatchTrigger.Ram, Threshold = 101 }).Validate(), "percentage threshold bounded to actual percentage domain");
        Reject(() => (crash with { Threshold = 2 }).Validate(), "single crash-event trigger does not pretend a rolling count");
        engine = new(); engine.UpdateRule(crash); var notifications = 0; engine.NotificationRequested += _ => notifications++;
        engine.ApplyEvents(new(time, [new(time.AddMinutes(-1), "Application", 1, 1000, 2, "Application Error", "Fixture")], []));
        engine.ApplyEvents(new(time, [new(time, "Application", 2, 1000, 2, "Application Error", "Fixture")], []));
        engine.ApplyEvents(new(time, [new(time, "Application", 2, 1000, 2, "Application Error", "Fixture")], []));
        Check(notifications == 1, "only new fresh crash triggers notification; no historical or duplicate alarm");
        var restored = new IntelligenceSessionEngine(); restored.Restore(engine.Freeze()); restored.NotificationRequested += _ => notifications++;
        restored.ApplyEvents(new(time.AddSeconds(1), [new(time.AddSeconds(1), "Application", 3, 1000, 2, "Application Error", "Fixture")], []));
        Check(notifications == 1, "restart retains notification cooldown without reconstructing a pending duration");
        var targetedCrash = new IntelligenceSessionEngine(); var targetedNotices = 0;
        targetedCrash.UpdateRule(crash with { Id = Guid.NewGuid(), Target = @"C:\Fixture\App.exe" });
        targetedCrash.NotificationRequested += _ => targetedNotices++;
        targetedCrash.ApplyEvents(new(time, [
            new(time, "Application", 10, 1000, 2, "Application Error", "Fixture"),
            new(time, "Application", 11, 1000, 2, "Application Error", "Fixture", @"C:\Other\App.exe")], []));
        Check(targetedNotices == 0, "targeted crash rule never matches unknown subject, filename similarity or unrelated program");
        targetedCrash.ApplyEvents(new(time, [new(time, "Application", 12, 1000, 2, "Application Error", "Fixture", @"c:\fixture\app.exe")], []));
        Check(targetedNotices == 1, "targeted crash rule uses exact case-insensitive structured Windows subject, not message text");
        var clock = new ManualClock(time);
        var numeric = new IntelligenceSessionEngine(clock);
        numeric.UpdateRule(crash with { Id = Guid.NewGuid(), Trigger = WatchTrigger.Cpu, Threshold = 0 });
        var numericNotices = 0; numeric.NotificationRequested += _ => numericNotices++;
        clock.Advance(TimeSpan.FromMilliseconds(10));
        numeric.Apply(ProcessAcceptanceTests.Snapshot(time, []), new(time.AddMilliseconds(1), 5, 50, 1, 2, 0, 0, [], []), () => fixture);
        Check(numericNotices == 1, "sequentially captured live source after dashboard time is not falsely classified as future");
        var disk = new IntelligenceSessionEngine(clock); var diskNotices = 0;
        disk.UpdateRule(crash with { Id = Guid.NewGuid(), Trigger = WatchTrigger.DiskFree, Target = @"C:\", Threshold = 10 });
        disk.NotificationRequested += _ => diskNotices++;
        disk.Apply(ProcessAcceptanceTests.Snapshot(clock.GetUtcNow(), []), new(clock.GetUtcNow(), 5, 50, 1, 2, 0, 0, [], []), () => fixture);
        Check(diskNotices == 0, "absent disk remains null rather than zero percent free");
        clock.Advance(TimeSpan.FromSeconds(1));
        disk.Apply(ProcessAcceptanceTests.Snapshot(clock.GetUtcNow(), []), new(clock.GetUtcNow(), 5, 50, 1, 2, 0, 0,
            [new(@"C:\", "Unavailable", "unknown", "Fixture")], []), () => fixture);
        Check(diskNotices == 0, "unavailable disk free space never fires threshold");
        clock.Advance(TimeSpan.FromSeconds(1));
        disk.Apply(ProcessAcceptanceTests.Snapshot(clock.GetUtcNow(), []), new(clock.GetUtcNow(), 5, 50, 1, 2, 0, 0,
            [new(@"C:\", "Low", "warning", "Fixture", 5)], []), () => fixture);
        Check(diskNotices == 1, "actual low free space fires using a valid source sample");
        engine = new(clock); engine.Start(fixture, 2, "Benutzerdefiniert", RecordingScope.Sensors);
        clock.Advance(TimeSpan.FromSeconds(3));
        var rollback = engine.Stop(fixture, time.AddHours(-1));
        rollback.Validate();
        Check(rollback.ClockDiscontinuity && rollback.ElapsedMilliseconds == 3000 && rollback.Before.Entities.Count == 0,
            "clock rollback retains actual UTC discontinuity, monotonic duration and precise custom sensor scope");
        Reject(() => (rollback with { ClockDiscontinuity = false }).Validate(), "reverse interval without explicit discontinuity rejected");
        var boot = fixture.Entities[0] with { Kind = IntelligenceKind.Boot, Fields = new Dictionary<string, string> {
            ["provider"] = "Microsoft-Windows-Diagnostics-Performance", ["eventId"] = "100", ["BootTime"] = "1000",
            ["eventVersion"] = "1", ["osVersion"] = "fixture-os", ["BootType"] = "fixture-cold" } };
        var boot2 = boot with { Id = "boot:second" };
        Check(IntelligenceWindowsEvents.ComparableBoots([boot, boot2]).Count == 2, "Windows boot comparison needs explicit same schema/OS/start type group");
        Check(IntelligenceWindowsEvents.ComparableBoots([boot, boot2 with { Fields = boot2.Fields.Where(p => p.Key != "BootType").ToDictionary() }]).Count == 0,
            "unknown boot start type never implies comparable fast/slow startup");
        var rollbackClock = new ManualClock(time);
        var bootEngine = new IntelligenceSessionEngine(rollbackClock);
        bootEngine.Apply(ProcessAcceptanceTests.Snapshot(time, []), new(time, 5, 50, 1, 2, 0, 0, [], []), () => fixture);
        bootEngine.Apply(ProcessAcceptanceTests.Snapshot(time.AddHours(-1), []), new(time.AddHours(-1), 5, 50, 1, 2, 0, 0, [], []), () => fixture);
        bootEngine.Freeze().Validate();
        Check(bootEngine.Boots.Count == 2 && bootEngine.Boots.All(b => !b.Complete), "backward source time opens incomplete new observation instead of invalid negative idle interval");
        foreach (var format in new[] { ".json", ".csv", ".txt" })
            Check(IntelligenceModel.ExportRecording(rollback, format, false).Contains("REDACTED", StringComparison.Ordinal), "recording export privacy: " + format);
        var software = fixture.Entities[7] with { Id = "software:fixture", Kind = IntelligenceKind.Software };
        var image = fixture.Entities[1] with { Id = "file:fixture", Kind = IntelligenceKind.File };
        var process = fixture.Entities[0] with { Id = "process:fixture", Kind = IntelligenceKind.Process };
        var graph = fixture with { Entities = [software, image, process], Edges = [
            new(software.Id, image.Id, "Manifest executable", software.Metadata), new(process.Id, image.Id, "Current image", process.Metadata)] };
        Check(IntelligenceModel.SoftwareProcesses(graph, software.Id).Single().Id == process.Id, "software activity requires two explicit image/file links");
        Check(IntelligenceModel.SoftwareProcesses(graph with { Edges = [] }, software.Id).Count == 0, "software names or installation paths alone never prove ownership");
        Check(IntelligenceModel.DriverGroup(fixture.Entities[8] with { Fields = new Dictionary<string, string> { ["class"] = "Net" } }) == "Netzwerk", "driver grouping follows reported PnP class");
        Check(IntelligenceModel.DriverGroup(fixture.Entities[8] with { Fields = new Dictionary<string, string> { ["class"] = "Net", ["functionalGroup"] = "WLAN" } }) == "WLAN",
            "driver WLAN group uses explicit physically reported network classification, never a device name");
        var driver = fixture.Entities[8] with { Kind = IntelligenceKind.Driver, Fields = new Dictionary<string, string> { ["class"] = "Net", ["deviceId"] = "PCI\\FIXTURE" } };
        var adapter = driver with { Id = "adapter:fixture", Kind = IntelligenceKind.Adapter, Fields = new Dictionary<string, string>
            { ["pnpDeviceId"] = "pci\\fixture", ["physicalMediumCode"] = "9" } };
        var drivers = new List<IntelligenceEntity> { driver };
        IntelligenceSources.ClassifyDrivers(drivers, [adapter]);
        Check(IntelligenceModel.DriverGroup(drivers[0]) == "WLAN" && drivers[0].Fields["functionalGroupSource"].Contains("=9", StringComparison.Ordinal),
            "WLAN derives from uniquely joined exact case-insensitive PnP identity with explicit medium provenance");
        drivers = [driver];
        IntelligenceSources.ClassifyDrivers(drivers, [adapter, adapter with { Id = "adapter:other", Fields = new Dictionary<string, string>
            { ["pnpDeviceId"] = "PCI\\FIXTURE", ["physicalMediumCode"] = "14" } }]);
        Check(IntelligenceModel.DriverGroup(drivers[0]) == "Netzwerk", "ambiguous adapter identity cannot become unique by filtering for wireless first");
        drivers = [driver];
        IntelligenceSources.ClassifyDrivers(drivers, [adapter with { Fields = new Dictionary<string, string>
            { ["pnpDeviceId"] = "PCI\\OTHER", ["physicalMediumCode"] = "9" } }]);
        Check(IntelligenceModel.DriverGroup(drivers[0]) == "Netzwerk", "wireless medium without exact device identity never assigns a WLAN driver");
        foreach (var bad in new[] { "$null", "'PCI\\FIXTURE'" })
        {
            var stub = """
                function Get-CimInstance($ClassName,$Filter,$OperationTimeoutSec) {
                    if($ClassName -eq 'Win32_PnPEntity') {
                        [pscustomobject]@{DeviceID='PCI\FIXTURE';Name='Fixture';PNPClass='Net';Service='FixtureSvc';Status='OK';ConfigManagerErrorCode=0;HardwareID=@('fixture')}
                        [pscustomobject]@{DeviceID=BAD_ID;Name='Invalid';PNPClass='Net';Service='FixtureSvc';Status='OK';ConfigManagerErrorCode=0;HardwareID=@('fixture')}
                    }
                    if($ClassName -eq 'Win32_OperatingSystem') {[pscustomobject]@{LastBootUpTime=[DateTime]::UtcNow.AddMinutes(-1);LocalDateTime=[DateTime]::UtcNow}}
                    if($ClassName -eq 'Win32_LogonSession') {[pscustomobject]@{LogonId='123';StartTime=[DateTime]::UtcNow;LogonType=2}}
                }
                function Get-NetAdapter {
                    param([switch]$IncludeHidden,$ErrorAction)
                    [pscustomobject]@{InterfaceGuid='fixture-interface';Name='Fixture adapter';PnPDeviceID='PCI\FIXTURE';NdisPhysicalMedium=9}
                }
                """.Replace("BAD_ID", bad, StringComparison.Ordinal);
            var output = CommandRunner.PowerShellAsync(stub + "\n" + IntelligenceSources.InventoryScript, default).GetAwaiter().GetResult();
            var result = IntelligenceSources.ParseInventory(output);
            Check(result.Entities.Count(e => IntelligenceModel.Domain(e) == "PnP") == 1 && !result.Coverage.Single(c => c.Domain == "PnP").Complete,
                "executed isolated native script rejects missing/duplicate PnP IDs without treating partial inventory as complete: " + bad);
            Check(result.Entities.Any(e => e.Id.StartsWith("logon:123:", StringComparison.Ordinal)), "executed logon identity includes actual session start, not reusable LogonId alone");
            Check(result.Entities.Single(e => e.Kind == IntelligenceKind.Adapter).Fields["physicalMediumCode"] == "9",
                "executed native adapter script preserves numeric NDIS medium rather than localized enum text");
        }
        var eventStub = """
            function Get-WinEvent {
                param($FilterHashtable,$MaxEvents,$ErrorAction)
                if($FilterHashtable.LogName -ne 'Application'){return}
                $row=[pscustomobject]@{TimeCreated=[DateTime]::UtcNow;LogName='Application';RecordId=1;Id=1000;Level=2;
                    ProviderName='Application Error';Message='Fixture'}
                $row | Add-Member -MemberType ScriptMethod -Name ToXml -Value {
                    '<Event><EventData><Data Name="AppName">App.exe</Data><Data Name="AppPath">C:\Fixture\App.exe</Data><Data Name="Password">do-not-store</Data></EventData></Event>'
                }
                $row
            }
            function Get-CimInstance {param($ClassName)}
            """;
        var eventOutput = CommandRunner.PowerShellAsync(eventStub + "\n" + EventMonitor.Script, default).GetAwaiter().GetResult();
        var eventResult = EventMonitor.Parse(eventOutput);
        Check(eventResult.Events.Single().Subject == @"C:\Fixture\App.exe", "executed shared event script reads allowlisted crash subject with AppPath precedence");
        Check(!eventOutput.Contains("do-not-store", StringComparison.Ordinal), "shared event script never serializes arbitrary EventData or whole XML");
    }
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private long _ticks;
        public override DateTimeOffset GetUtcNow() => now;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        internal void Advance(TimeSpan span) { _ticks += span.Ticks; now += span; }
    }
    private static void Storage(IntelligenceSnapshot snapshot)
    {
        var root = Path.Combine(Path.GetTempPath(), "AetherIntelligence-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new IntelligenceStore(root);
            store.SaveAsync("snapshot", snapshot.Id, snapshot, default).GetAwaiter().GetResult();
            var loaded = store.LoadAsync<IntelligenceSnapshot>("snapshot", default).GetAwaiter().GetResult();
            Check(loaded.Count == 1 && loaded[0].Id == snapshot.Id, "atomic checked envelope roundtrip");
            var corrupt = Path.Combine(root, "snapshot-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(corrupt, "{\"Version\":1,\"Payload\":\"bad\"}");
            Check(store.LoadAsync<IntelligenceSnapshot>("snapshot", default).GetAwaiter().GetResult().Count == 1 &&
                store.Issues.Count == 1 && File.Exists(corrupt + ".corrupt"), "single corrupt record isolated without losing good record");
            File.WriteAllText(Path.Combine(root, "session-unrelated.txt"), "not owned");
            store.ResetAsync(default).GetAwaiter().GetResult();
            Check(File.Exists(Path.Combine(root, "session-unrelated.txt")) && !File.Exists(corrupt + ".corrupt"), "reset deletes only exact owned file names, including quarantine");
            Reject(() => store.SaveAsync("..", Guid.NewGuid(), snapshot, default).GetAwaiter().GetResult(), "storage path escape rejected");
            Reject(() => new IntelligenceRetention(1, 0).Validate(), "retention settings bounded");
            store.Retention = new(1, 1, 8, 1048576);
            for (var i = 0; i < 10; i++) store.SaveAsync("snapshot", Guid.NewGuid(), snapshot, default).GetAwaiter().GetResult();
            Check(Directory.GetFiles(root, "snapshot-*.json").Length <= 8, "file count retention enforced across records");
            var files = Directory.GetFiles(root, "snapshot-*.json");
            File.SetLastWriteTimeUtc(files[0], DateTime.UtcNow.AddDays(-3));
            store.LoadAsync<IntelligenceSnapshot>("snapshot", default).GetAwaiter().GetResult();
            Check(!File.Exists(files[0]), "age retention enforced");
            var unrelated = Path.Combine(root, "unrelated.json"); File.WriteAllText(unrelated, "not owned");
            File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-3));
            for (var i = 0; i < 10; i++) store.SaveAsync("preferences", Guid.NewGuid(), new IntelligencePreferences(1, false, store.Retention), default).GetAwaiter().GetResult();
            Check(File.Exists(unrelated) && Directory.GetFiles(root, "preferences-*.json").Length == 1 &&
                Directory.GetFiles(root, "*.json").Length <= store.Retention.Files + 1, "prune owns exact filenames and preferences consume a bounded single slot");
            var envelopeFile = Directory.GetFiles(root, "snapshot-*.json").First();
            Check(IntelligenceStore.ImportSnapshot(File.ReadAllText(envelopeFile)).Id == snapshot.Id, "checked persisted envelope imports without live mutation");
            var cancelled = new CancellationToken(true);
            var didCancel = false;
            try { store.SaveAsync("snapshot", Guid.NewGuid(), snapshot, cancelled).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { didCancel = true; }
            Check(didCancel && Directory.GetFiles(root, "*.tmp").Length == 0, "cancel leaves no temporary record");
        }
        finally { foreach (var file in Directory.GetFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
    internal static void Live(string output)
    {
        if (!Path.IsPathFullyQualified(output) || File.Exists(output)) throw new ArgumentException("Neue absolute Ergebnisdatei erforderlich.");
        using var self = Process.GetCurrentProcess();
        var sampler = new DashboardMetrics(); var native = new NativeMetrics();
        var beforeHandles = self.HandleCount; var beforePrivate = self.PrivateMemorySize64; var watch = Stopwatch.StartNew();
        var snapshot = IntelligenceSources.CaptureAsync(IntelligenceSources.Live(sampler.Read(), native.Read(), null, "Reale lesende Erfassung"), default).GetAwaiter().GetResult();
        IntelligenceModel.Validate(snapshot); self.Refresh();
        var captureMs = watch.Elapsed.TotalMilliseconds; var captureHandles = self.HandleCount; var capturePrivate = self.PrivateMemorySize64;
        var old = SynchronizationContext.Current;
        object ui;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { ui = NativeLifecycle(); } finally { SynchronizationContext.SetSynchronizationContext(old); }
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            Scope = "Read-only Windows sources; controlled local recorder and notification rule; no foreign mutation, ETW or packet capture.", DurationMs = captureMs,
            Entities = snapshot.Entities.GroupBy(e => e.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()), Edges = snapshot.Edges.Count,
            WirelessDrivers = snapshot.Entities.Count(e => e.Kind == IntelligenceKind.Driver && IntelligenceModel.DriverGroup(e) == "WLAN"),
            Coverage = snapshot.Coverage.Select(c => new { c.Domain, c.Complete, State = c.Metadata.Availability.ToString() }),
            BeforeHandles = beforeHandles, AfterHandles = captureHandles, BeforePrivate = beforePrivate, AfterPrivate = capturePrivate, UI = ui
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: real read-only capture, {snapshot.Entities.Count} entities, {snapshot.Edges.Count} edges; {captureMs:F1} ms; real Watch/recorder/cancellation verified.");
    }
    private static object NativeLifecycle()
    {
        var root = Path.Combine(Path.GetTempPath(), "AetherIntelligence-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var view = new SystemIntelligenceView(root);
        AetherNeonFrame.SetReducedMotion(view, true);
        var window = new Window { Content = view, Width = 1200, Height = 850, ShowInTaskbar = false };
        try
        {
            window.Show(); Pump(); Await(view.Pending);
            var dashboard = new DashboardMetrics(); var native = new NativeMetrics();
            view.Apply(dashboard.Read(), native.Read());
            view.QuickAction("Watch");
            ((TextBox)view.FindName("Threshold")).Text = "0"; ((TextBox)view.FindName("Duration")).Text = "0";
            ((ComboBox)view.FindName("RuleAction")).SelectedItem = WatchAction.Notification;
            var notifications = 0; view.NotificationRequested += _ => notifications++;
            Click(view, "Regel speichern / ändern");
            view.QuickAction("Recorder"); ((ComboBox)view.FindName("Profile")).SelectedIndex = 0;
            Click(view, "Aufzeichnung starten");
            view.Apply(dashboard.Read(), native.Read());
            Buttons(view).Single(b => Equals(b.Content, "Inventar lesen")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var overlapping = !view.Pending.IsCompleted;
            var timer = Stopwatch.StartNew();
            Buttons(view).Single(b => Equals(b.Content, "Stoppen")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var stopMs = timer.Elapsed.TotalMilliseconds;
            if (view.Engine.IsRecording || stopMs > 2500) throw new InvalidOperationException("Recorder stop must take effect before pending inventory completes.");
            Await(view.Pending);
            var recording = view.Engine.Completed ?? throw new InvalidOperationException("Native recording missing.");
            recording.Validate();
            if (recording.Samples.Count != 1 || notifications != 1 || Directory.GetFiles(root, "recording-*.json").Length != 1)
                throw new InvalidOperationException($"Real lifecycle failed: samples={recording.Samples.Count}; notifications={notifications}; files={Directory.GetFiles(root, "recording-*.json").Length}.");
            return new { RecordingSamples = recording.Samples.Count, Notifications = notifications, PendingInventoryAtStop = overlapping,
                ImmediateStopMs = stopMs, recording.ElapsedMilliseconds, RecordingPersisted = true, TemporaryOwnedStoreRemoved = true };
        }
        finally
        {
            Await(view.ShutdownAsync()); window.Close(); Pump();
            foreach (var file in Directory.GetFiles(root)) File.Delete(file);
            Directory.Delete(root);
        }
    }
    internal static void Proof(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || Directory.Exists(directory)) throw new ArgumentException("Neuer absoluter Proofordner erforderlich.");
        Directory.CreateDirectory(directory);
        var old = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { ProofCore(directory); } finally { SynchronizationContext.SetSynchronizationContext(old); }
    }
    private static void ProofCore(string directory)
    {
        var measurements = new List<object>(); var fixture = Fixture(5000);
        foreach (var theme in new[] { "retro-neon", "windows-modern-light", "windows-modern-dark" })
        foreach (var scale in new[] { 1d, 1.5, 2 })
        {
            SkinCatalog.Apply(Application.Current, theme == "retro-neon" ? theme : "windows-modern", theme.EndsWith("light", StringComparison.Ordinal));
            var view = new SystemIntelligenceView { Synthetic = true };
            AetherNeonFrame.SetReducedMotion(view, true);
            var root = new AdornerDecorator { Child = view };
            var window = new Window { Content = root, Width = 1460, Height = 940, ShowInTaskbar = false };
            window.Show(); Pump();
            var clock = Stopwatch.StartNew(); view.SetFixture(fixture); window.UpdateLayout(); Pump();
            var ready = clock.Elapsed.TotalMilliseconds;
            var count = view.ResultGrid.Items.Count;
            var containers = view.ResultGrid.Items.Cast<object>().Count(e => view.ResultGrid.ItemContainerGenerator.ContainerFromItem(e) is DataGridRow);
            if (ready > 2500 || containers >= 200 || count != 5000) throw new InvalidOperationException($"Intelligence UI gate failed: {ready:F1} ms, {containers} containers, {count} rows.");
            clock.Restart(); ((TextBox)view.FindName("Search")).Text = "Fixture 04999"; Pump();
            var filterMs = clock.Elapsed.TotalMilliseconds;
            if (view.ResultGrid.Items.Count != 1 || filterMs > 2500) throw new InvalidOperationException("Exact search/filter gate failed.");
            ((TextBox)view.FindName("Search")).Text = ""; Pump();
            window.Content = null;
            VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
            root.Measure(new Size(1440, 900)); root.Arrange(new Rect(0, 0, 1440, 900)); root.UpdateLayout();
            Save(root, Path.Combine(directory, $"intelligence-{theme}-{scale * 100:0}.png"), scale);
            window.Content = root; window.UpdateLayout(); Pump();
            clock.Restart(); view.QuickAction("System Map"); window.UpdateLayout(); Pump();
            var mapMs = clock.Elapsed.TotalMilliseconds;
            if (mapMs > 2500) throw new InvalidOperationException("Map projection exceeds 2500ms budget.");
            var buttons = ((Canvas)view.FindName("Map")).Children.OfType<Button>().ToArray();
            if (buttons.Length != 200 || !buttons[0].IsVisible || buttons[0].ActualWidth < 200)
                throw new InvalidOperationException("Map budget or actual visible layout not applied.");
            if (scale == 1)
            {
                window.Content = null; VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
                root.Measure(new Size(1440, 900)); root.Arrange(new Rect(0, 0, 1440, 900)); root.UpdateLayout();
                Save(root, Path.Combine(directory, $"map-{theme}.png"), scale);
                window.Content = root; window.UpdateLayout(); Pump();
            }
            if (scale == 1) ExerciseAreas(view, window, directory, theme);
            measurements.Add(new { Theme = theme, Scale = scale, ReadyMs = ready, FilterMs = filterMs, MapMs = mapMs, Rows = count, Containers = containers, MapNodes = buttons.Length });
            Await(view.ShutdownAsync()); window.Close(); Pump();
        }
        var engine = new IntelligenceSessionEngine(); var sampleTime = DateTimeOffset.UtcNow;
        var processes = Enumerable.Range(1, 5000).Select(i => ProcessCommandTests.Row(20000 + i, sampleTime.UtcTicks - 10000000 + i)).ToArray();
        var dashboard = new DashboardSnapshot(sampleTime, [0], processes, [], 1, 2, 1, 1, 1, TimeSpan.FromMinutes(10), []);
        var sample = new LiveSample(sampleTime, 5, 50, 1, 2, 0, 0, [], []);
        var timer = Stopwatch.StartNew();
        engine.Apply(dashboard, sample, () => fixture); timer.Stop();
        if (timer.Elapsed.TotalMilliseconds > 100) throw new InvalidOperationException("Live kernel processing exceeds 100 ms / 5000 process budget.");
        measurements.Add(new { Label = "5000 shared process observations", ApplyMs = timer.Elapsed.TotalMilliseconds });
        timer.Restart(); var compared = IntelligenceModel.Compare(fixture, fixture); var compareMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart(); var path = IntelligenceModel.Path("fixture:0", "fixture:4999", fixture.Edges); var pathMs = timer.Elapsed.TotalMilliseconds;
        var sourceView = new SystemIntelligenceView { Synthetic = true }; sourceView.SetFixture(fixture);
        timer.Restart(); var search = sourceView.SearchTasks(); var searchMs = timer.Elapsed.TotalMilliseconds;
        Await(sourceView.ShutdownAsync());
        if (compareMs > 2500 || pathMs > 2500 || searchMs > 2500 || compared.Count != 5000 || path.Count != 5000 || search.Count != 5000)
            throw new InvalidOperationException("Compare/graph/search 5000-entity budget or output shape failed.");
        for (var i = 0; i < 32; i++) engine.UpdateRule(new(Guid.NewGuid(), "Fixture " + i, WatchTrigger.Cpu, "", 100, 10, 60, WatchAction.Timeline));
        engine.Start(fixture, 60, "Ressourcen und Timeline");
        var recordingTime = DateTimeOffset.UtcNow;
        timer.Restart(); engine.Apply(dashboard with { Timestamp = recordingTime }, sample with { Timestamp = recordingTime }, () => fixture);
        var watchedMs = timer.Elapsed.TotalMilliseconds; engine.Stop(fixture, DateTimeOffset.UtcNow);
        if (watchedMs > 100 || engine.RuleStatus.Count != 32 || engine.Completed?.Samples.Count != 1)
            throw new InvalidOperationException("32 Watch rules plus recorder exceed 100ms budget or lose samples.");
        measurements.Add(new { Label = "5000-entity compare/path/search and 32-rule recording", CompareMs = compareMs, PathMs = pathMs,
            SearchProjectionMs = searchMs, WatchedRecordingApplyMs = watchedMs });
        Integrated(directory, fixture);
        var cycles = new List<WeakReference>();
        using (var self = Process.GetCurrentProcess())
        {
            object Resources()
            {
                self.Refresh();
                return new { Handles = self.HandleCount, PrivateBytes = self.PrivateMemorySize64, WorkingSet = self.WorkingSet64,
                    Threads = self.Threads.Count, CpuMs = self.TotalProcessorTime.TotalMilliseconds, Gc0 = GC.CollectionCount(0),
                    Gc1 = GC.CollectionCount(1), Gc2 = GC.CollectionCount(2), AllocatedBytes = GC.GetTotalAllocatedBytes() };
            }
            var before = Resources();
            for (var i = 0; i < 20; i++) cycles.Add(Cycle());
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Pump();
            var aliveBeforeCounterexample = cycles.Count(w => w.IsAlive);
            var afterViews = Resources();
            for (var i = 0; i < 30; i++)
            {
                var empty = new Window { Width = 1000, Height = 750, ShowInTaskbar = false };
                empty.Show(); Pump(); empty.Close(); Pump();
            }
            Await(Task.Delay(1000)); Pump();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Pump();
            if (cycles.Any(w => w.IsAlive)) throw new InvalidOperationException($"Intelligence views retained after shutdown/GC: {aliveBeforeCounterexample} before, {cycles.Count(w => w.IsAlive)} after empty-window counterexample.");
            measurements.Add(new { Label = "20 synthetic view lifecycles plus 30 empty WPF windows and 1s dispatcher drain; GC only for reachability, not a long-term leak claim",
                Before = before, AfterViews = afterViews, AliveBeforeCounterexample = aliveBeforeCounterexample,
                AfterCounterexample = Resources(), AliveViews = cycles.Count(w => w.IsAlive) });
            self.Refresh(); var cpu = self.TotalProcessorTime; var idle = Stopwatch.StartNew();
            Await(Task.Delay(2000)); idle.Stop(); self.Refresh();
            var cpuMs = (self.TotalProcessorTime - cpu).TotalMilliseconds;
            measurements.Add(new { Label = "2s settled test-host dispatcher idle; not a full-product idle claim",
                ElapsedMs = idle.Elapsed.TotalMilliseconds, CpuMs = cpuMs, OneCorePercent = cpuMs / idle.Elapsed.TotalMilliseconds * 100,
                LogicalProcessorCount = Environment.ProcessorCount });
        }
        File.WriteAllText(Path.Combine(directory, "measurements.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: nine theme/root-DPI cases, all areas, Watch/recorder/comparison clicks, exact global search/navigation, 20 lifecycles and shared live processing.");
    }
    private static void ExerciseAreas(SystemIntelligenceView view, Window window, string directory, string theme)
    {
        Click(view, "Baseline merken");
        Click(view, "A / B vergleichen");
        if (view.ResultGrid.Items.Count == 0) throw new InvalidOperationException("Baseline comparison produced no evidence rows.");
        view.QuickAction("Watch");
        ((TextBox)view.FindName("Duration")).Text = "0";
        ((TextBox)view.FindName("Threshold")).Text = "0";
        ((ComboBox)view.FindName("RuleAction")).SelectedItem = WatchAction.Notification;
        var notifications = 0; view.NotificationRequested += _ => notifications++;
        Click(view, "Regel speichern / ändern");
        if (view.Engine.Rules.Count != 1) throw new InvalidOperationException("Watch creation failed.");
        view.ResultGrid.SelectedIndex = 0; Pump();
        ((TextBox)view.FindName("RuleName")).Text = "Editor bleibt erhalten";
        var time = DateTimeOffset.UtcNow;
        var rows = new[] { ProcessCommandTests.Row(120003, time.UtcTicks - 10000000) with { ObservedAt = time } };
        var dashboard = ProcessAcceptanceTests.Snapshot(time, rows);
        var sample = new LiveSample(time, 5, 50, 1, 2, 0, 0, [], []);
        view.Apply(dashboard, sample); Pump();
        if (notifications != 1 || ((TextBox)view.FindName("RuleName")).Text != "Editor bleibt erhalten")
            throw new InvalidOperationException("Watch notification or stable editor selection failed.");
        Click(view, "Regel speichern / ändern");
        if (view.Engine.Rules.Single().Name != "Editor bleibt erhalten") throw new InvalidOperationException("Watch edit failed.");
        view.QuickAction("Recorder");
        Click(view, "Aufzeichnung starten");
        time = DateTimeOffset.UtcNow;
        view.Apply(dashboard with { Timestamp = time }, sample with { Timestamp = time });
        Click(view, "Stoppen");
        if (view.Engine.Completed is not { Samples.Count: 1, Cancelled: false } || view.ResultGrid.Items.Count != 1)
            throw new InvalidOperationException("Recorder UI failed to stop and retain actual samples.");
        Click(view, "Aufzeichnung starten"); Click(view, "Aufzeichnung verwerfen");
        if (view.Engine.IsRecording || view.Engine.Completed is not { Cancelled: true }) throw new InvalidOperationException("Recorder discard failed.");
        foreach (var area in new[] { "Übersicht", "Timeline", "Change / Compare", "Reliability", "Software", "Treiber", "Startup", "Netzwerk 2.0", "Diagnose", "Recorder", "Watch", "System Map" })
        {
            view.QuickAction(area); window.UpdateLayout(); Pump();
            if (!view.IsVisible || view.ActualHeight < 500) throw new InvalidOperationException("Work area is not laid out: " + area);
            Save(view, Path.Combine(directory, "area-" + theme + "-" + area.Replace(" / ", "-").Replace(" ", "-") + ".png"), 1);
        }
        var map = (Canvas)view.FindName("Map");
        map.Children.OfType<Button>().Single(b => b.Tag is IntelligenceEntity e && e.Id == "fixture:0").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Click(view, "Pfadanfang merken");
        map.Children.OfType<Button>().Single(b => b.Tag is IntelligenceEntity e && e.Id == "fixture:1").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Click(view, "Pfad zur Auswahl");
        if (!((TextBox)view.FindName("Inspector")).Text.Contains("fixture:0", StringComparison.Ordinal)) throw new InvalidOperationException("Map path lost actual source node.");
        Click(view, "Fokus + direkte Nachbarn");
        if (map.Children.OfType<Button>().Count() > 3) throw new InvalidOperationException("Map focus did not reduce to exact neighbors.");
        Click(view, "Gesamte Karte");
        if (map.Children.OfType<Button>().Count() != 200) throw new InvalidOperationException("Map focus cannot be reset.");
        var identity = "process:" + rows[0].Identity;
        view.QuickAction("intelligence:" + identity);
        if (view.ResultGrid.SelectedItem is not SystemIntelligenceView.Row { Value: IntelligenceEntity selected } || selected.Id != identity)
            throw new InvalidOperationException("Exact entity navigation selected a similar identity.");
        var tasks = view.SearchTasks();
        if (!tasks.Any(t => t.QuickAction == "intelligence:" + identity) ||
            !tasks.Any(t => t.QuickAction?.StartsWith("intelligence-object:watch:", StringComparison.Ordinal) == true) ||
            !tasks.Any(t => t.QuickAction?.StartsWith("intelligence-object:recording:", StringComparison.Ordinal) == true))
            throw new InvalidOperationException("Typed global tasks lost entity, Watch or recording identities.");
    }
    private static void Integrated(string directory, IntelligenceSnapshot fixture)
    {
        SkinCatalog.Apply(Application.Current, "retro-neon");
        var main = new MainWindow(new CockpitSettings()) { WindowState = WindowState.Normal, Width = 1460, Height = 1000, ShowInTaskbar = false };
        var view = (SystemIntelligenceView)main.FindName("SystemIntelligence");
        var center = (ProcessCommandView)main.FindName("ProcessCenter");
        try
        {
            var time = DateTimeOffset.UtcNow;
            var rows = new[] { ProcessCommandTests.Row(120003, time.UtcTicks - 10000000) with { ObservedAt = time } };
            var dashboard = ProcessAcceptanceTests.Snapshot(time, rows);
            var sample = new LiveSample(time, 5, 50, 1, 2, 0, 0, [], []);
            main.Model.ApplyDashboard(dashboard, sample); center.Apply(dashboard); view.Apply(dashboard, sample); view.SetFixture(fixture);
            var pages = (TabControl)main.FindName("Pages");
            pages.SelectedItem = pages.Items.Cast<TabItem>().Single(p => Equals(p.Tag, "SystemIntelligence"));
            main.Show(); Pump(); main.UpdateLayout();
            Save(main, Path.Combine(directory, "main-intelligence.png"), 1);
            var palette = new TaskCenterWindow(rows, view.SearchTasks()) { Owner = main, ShowInTaskbar = false };
            palette.Show(); palette.SearchBox.Text = rows[0].Name; Pump();
            if (!palette.Results.Items.Cast<CockpitTask>().Any(t => t.QuickAction == "intelligence:process:" + rows[0].Identity))
                throw new InvalidOperationException("Integrated palette loses Intelligence entity identity.");
            Save(palette, Path.Combine(directory, "palette-intelligence.png"), 1); palette.Close();
            view.QuickAction("intelligence:process:" + rows[0].Identity);
            Click(view, "Zur technischen Quelle / Prozesszentrale");
            if (!Equals((pages.SelectedItem as TabItem)?.Tag, "Taskmanager")) throw new InvalidOperationException("Exact source navigation did not reach process center.");
        }
        finally { Await(view.ShutdownAsync()); Await(center.ShutdownAsync()); main.Close(); Pump(); }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference Cycle()
    {
        var view = new SystemIntelligenceView { Synthetic = true };
        AetherNeonFrame.SetReducedMotion(view, true);
        var window = new Window { Content = view, Width = 1000, Height = 750, ShowInTaskbar = false };
        window.Show(); view.SetFixture(Fixture(500)); window.UpdateLayout(); Pump();
        Await(view.ShutdownAsync()); window.Close(); window.Content = null; Pump();
        return new(view);
    }
    private static IEnumerable<Button> Buttons(DependencyObject root)
    {
        if (root is Button button) yield return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Buttons(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Click(SystemIntelligenceView view, string content)
    {
        Buttons(view).Single(b => Equals(b.Content, content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Await(view.Pending); Pump();
    }
    private static void Save(FrameworkElement root, string file, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file); encoder.Save(stream);
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Await(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
}
