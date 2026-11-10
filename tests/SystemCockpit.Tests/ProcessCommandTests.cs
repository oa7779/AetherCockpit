using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Xml.Linq;
using AetherOperations;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class ProcessCommandTests
{
    private static int _assertions;
    private static void Check(bool value, string message)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException("Process center: " + message);
    }

    internal static int Run()
    {
        _assertions = 0;
        var root = Row(1001, 100);
        var child = Row(1002, 200) with { ParentPid = root.Id };
        var reused = Row(1003, 50) with { ParentPid = root.Id };
        Check(ProcessActions.Tree(root.Identity, [root, child, reused]).Count == 2, "parent PID reused before child start is not a relationship");
        var fake = new FakeSource { Rows = [root, child] };
        var journal = new List<string>();
        var actions = new ProcessActions(fake, (name, _) => journal.Add(name));
        var plan = actions.PrepareAsync(root.Identity, false, default).GetAwaiter().GetResult();
        Check(plan.Root.Identity == root.Identity && plan.Targets.Count == 1, "confirmation has exact identity");
        var result = actions.ExecuteAsync(plan, default).GetAwaiter().GetResult();
        Check(result.Single().State == ProcessActionState.Exited, "same target exit verified");
        Check(fake.Opened == fake.Disposed, "all target handles disposed");
        Check(journal.SequenceEqual(new[] { "process-terminate-intent", "process-terminate-result" }), "intent precedes mutation");
        fake.Rows = [root with { StartTicks = 300 }];
        result = actions.ExecuteAsync(plan, default).GetAwaiter().GetResult();
        Check(result.Single().State == ProcessActionState.Failed && fake.Terminations == 1, "PID reuse never targets replacement");
        fake.Rows = [];
        Check(actions.ExecuteAsync(plan, default).GetAwaiter().GetResult().Single().State == ProcessActionState.Failed, "exit while confirming is explicit");
        fake.Rows = [root];
        fake.Error = 5;
        Check(actions.ExecuteAsync(plan, default).GetAwaiter().GetResult().Single().State == ProcessActionState.AccessDenied, "access denied translated");
        fake.Error = null;
        fake.Protection = "protected";
        Check(actions.ExecuteAsync(plan, default).GetAwaiter().GetResult().Single().State == ProcessActionState.Failed, "protected target blocks");
        fake.Protection = "";
        fake.Protected.Add(root.Identity);
        Check(actions.ExecuteAsync(plan, default).GetAwaiter().GetResult().Single().State == ProcessActionState.Failed, "own service blocked");
        fake.Protected.Clear();
        var tree = actions.PrepareAsync(root.Identity, true, default).GetAwaiter().GetResult();
        fake.Rows = [root, child];
        Check(actions.ExecuteAsync(tree, default).GetAwaiter().GetResult().Single().State == ProcessActionState.Reconfirm, "changed tree needs new confirmation");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Check(actions.ExecuteAsync(plan, cancel.Token).GetAwaiter().GetResult().Single().State == ProcessActionState.Cancelled, "cancel before mutation");
        using var current = Process.GetCurrentProcess();
        var self = new ProcessIdentity(current.Id, current.StartTime.ToUniversalTime().Ticks);
        using (var handle = ProcessNative.Open(self))
        {
            Check(ProcessNative.Identity(handle, current.Id) == self, "real native identity roundtrip");
            Check(!ProcessNative.Exited(handle), "self is running");
            Check(ProcessNative.Architecture(handle).Length > 0, "native architecture");
            Check(ProcessNative.Token(handle).User.StartsWith("S-1-", StringComparison.Ordinal), "token SID without account-name assumption");
        }
        try { using var wrong = ProcessNative.Open(self with { StartTicks = self.StartTicks + 1 }); Check(false, "wrong native identity"); }
        catch (InvalidOperationException) { Check(true, "native PID reuse guard"); }
        Check(new NativeProcessActionSource().Open(self, false).DisposeAndProtection().Length > 0, "self protection based on actual identity");
        var dialog = new ProcessActionDialog(plan);
        Check(dialog.CancelButton.IsCancel && dialog.CancelButton.IsDefault, "cancel is default and escape action");
        Check(!dialog.ConfirmButton.IsDefault && dialog.ConfirmButton.Content.Equals("Prozess beenden"), "explicit hard action label");
        Check(dialog.Plan.Root.Identity == root.Identity, "dialog identity is frozen plan");
        var view = new ProcessCommandView { Synthetic = true };
        var menu = view.MenuFor(new(root));
        Check(menu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "Prozess beenden") && item.IsEnabled), "valid process context menu");
        Check(menu.Items.OfType<MenuItem>().Count() >= 15, "full context command set");
        Check(menu.Items.OfType<MenuItem>().All(item => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(item))), "context actions have screenreader names");
        Check(!view.MenuFor(new(root with { StartTicks = 0 })).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Prozess beenden")).IsEnabled,
            "unknown identity disables destructive menu");
        Check(view.ProcessGrid.EnableRowVirtualization && view.ProcessGrid.EnableColumnVirtualization, "both axes virtualized");
        view.ShutdownAsync().GetAwaiter().GetResult();
        var history = new ProcessHistory();
        var time = DateTimeOffset.UtcNow;
        history.Apply([root, child], time);
        var stable = history.Rows[root.Identity];
        history.Apply([root with { CpuPercent = 20 }, child], time.AddSeconds(2));
        Check(ReferenceEquals(stable, history.Rows[root.Identity]), "row identity stable across samples");
        Check(history.Rows[child.Identity].Parent == root.Identity, "tree uses creation-time ordering");
        history.Apply([root with { StartTicks = 500 }], time.AddSeconds(4));
        Check(stable.Ended && history.Rows.ContainsKey(new(root.Id, 500)), "reuse separates history and exit marker");
        for (var index = 3; index <= 150; index++) history.Apply([root with { StartTicks = 500 }], time.AddSeconds(index * 2));
        Check(history.Rows[new(root.Id, 500)].Samples.Count == 120 && !history.Rows.ContainsKey(root.Identity), "history and ended retention bounded");
        history.Apply([root with { StartTicks = 500 }], time.AddSeconds(400));
        Check(history.Rows[new(root.Id, 500)].Samples.Last().Gap, "sampling gap never interpolated");
        Check(history.Rows[new(root.Id, 500)].History.Contains("AVG"), "real sample MIN MAX AVG exposed");
        var frozen = history.Rows[new(root.Id, 500)].Freeze();
        var oldFrozen = frozen.Current;
        history.Apply([root with { StartTicks = 500, CpuPercent = 99 }], time.AddSeconds(402));
        Check(ReferenceEquals(frozen.Current, oldFrozen) && frozen.Current.CpuPercent == 10 &&
            frozen.Samples.Last().Timestamp < history.Rows[new(root.Id, 500)].Samples.Last().Timestamp, "paused row separates immutable sample projection from continuing history");
        var capped = new ProcessHistory();
        capped.Apply(Enumerable.Range(1, ProcessHistory.Capacity + 1).Select(i => Row(20000 + i, 1000 + i)).ToArray(), time);
        Check(capped.Rows.Count == ProcessHistory.Capacity, "actual identity count never exceeds declared history capacity");
        var xml = XDocument.Parse("<Event><System><Execution ProcessID='1001'/></System><EventData><Data Name='Other'>1001</Data></EventData></Event>");
        Check(!ProcessCorrelation.EventTargets(xml, root.Id), "provider execution PID is not target correlation");
        Check(ProcessCorrelation.EventTargets(XDocument.Parse("<Event><System><Provider Name='Application Error'/><EventID>1000</EventID></System><EventData><Data Name='ProcessId'>0x3e9</Data></EventData></Event>"), root.Id),
            "structured target PID including hex");
        Check(!ProcessCorrelation.EventTargets(XDocument.Parse("<Event><System><Provider Name='Other'/><EventID>1000</EventID></System><EventData><Data Name='ProcessId'>1001</Data></EventData></Event>"), root.Id),
            "unknown event schema cannot establish a target relationship");
        var relation = new ProcessRelation("Autostart", "PRIVATE-KEY", null, @"C:\fixture.exe", "PRIVATE-VALUE", "fixture", time, DataAvailability.Available);
        Check(ProcessCorrelation.Matches(relation, root.Identity, @"c:\FIXTURE.exe"), "exact executable path match");
        Check(!ProcessCorrelation.Matches(relation, root.Identity, @"C:\other\fixture.exe"), "same executable name is not a match");
        var snapshot = new ProcessSystemSnapshot("aether.systemsnapshot/1", time,
            [new(root.Identity, "PRIVATE-NAME", 10, 50, time)], [relation], []);
        foreach (var format in new[] { ".json", ".csv", ".txt" })
        {
            Check(!snapshot.Export(format, false).Contains("PRIVATE"), "snapshot sensitive redaction " + format);
            Check(snapshot.Export(format, true).Contains("PRIVATE"), "explicit sensitive opt-in " + format);
        }
        Check(! (snapshot with { Relations = [relation with { Classification = DataClassification.Secret }] }).Export(".json", true).Contains("PRIVATE-VALUE"), "secret never exported");
        Check(ProcessSystemSnapshot.Csv("=1+1").StartsWith("\"'") && ProcessSystemSnapshot.Csv("\ttext").StartsWith("\"'"), "shared CSV injection protection");
        Check(ProcessSystemSnapshot.Compare(snapshot, snapshot with { Processes = [new(new(root.Id, 900), "new", null, null, time)] }).Contains("Neu beobachtet"), "snapshot compares identities not PID");
        var imported = ProcessSystemSnapshot.Parse(snapshot.LocalJson());
        Check(imported.Processes.Single().Identity == root.Identity, "versioned local snapshot roundtrip");
        try { ProcessSystemSnapshot.Parse(snapshot.LocalJson().Replace("aether.systemsnapshot/1", "future/2", StringComparison.Ordinal)); Check(false, "unknown snapshot schema"); }
        catch (System.Text.Json.JsonException) { Check(true, "future schema explicitly rejected"); }
        try { ProcessSystemSnapshot.Parse("{\"Schema\":\"aether.systemsnapshot/1\"}"); Check(false, "missing snapshot sections"); }
        catch (System.Text.Json.JsonException) { Check(true, "missing sections rejected"); }
        var sideChannel = relation with { Kind = "PRIVATE-KIND", Source = "PRIVATE-SOURCE", Classification = DataClassification.Normal };
        Check(!(snapshot with { Relations = [sideChannel] }).Export(".json", false).Contains("PRIVATE"), "metadata cannot bypass relationship privacy");
        Check(ProcessGpu.Parse("pid_1001_luid_0x00000000_0x0000abcd_phys_0_eng_1_engtype_3D") is { Pid: 1001, Luid: "00000000:0000ABCD" }, "GPU PID/LUID engine identity parsed exactly");
        Check(ProcessGpu.Parse("fixture_1001_adapterSameName") is null, "GPU name heuristics refused");
        var preferences = TaskCenterPreferences.Empty.Use("Prozesse anzeigen").Toggle("Prozesse anzeigen");
        Check(preferences.Recent.Count == 1 && preferences.Favorites.Single() == "Prozesse anzeigen", "favorite and recent tools separate from process data");
        fake.Rows = [root]; fake.TerminationWorks = false;
        result = actions.ExecuteAsync(plan, default).GetAwaiter().GetResult();
        Check(result.Single().State == ProcessActionState.Timeout, "accepted terminate is not success before observed exit");
        using var afterRequest = new CancellationTokenSource();
        fake.OnTerminate = afterRequest.Cancel;
        result = actions.ExecuteAsync(plan, afterRequest.Token).GetAwaiter().GetResult();
        Check(result.Single().State == ProcessActionState.Partial, "cancellation after request cannot undo termination");
        fake.OnTerminate = null;
        var beforeFailedJournal = fake.Terminations;
        var noJournal = new ProcessActions(fake, (name, _) => { if (name.EndsWith("intent", StringComparison.Ordinal)) throw new System.IO.IOException("journal fixture"); });
        Check(noJournal.ExecuteAsync(plan, default).GetAwaiter().GetResult().Single().State == ProcessActionState.Failed &&
            fake.Terminations == beforeFailedJournal, "journal failure blocks mutation");
        Check(fake.Opened == fake.Disposed, "all failure, timeout and cancellation handles freed");
        fake.Rows = [root, child]; fake.TerminationWorks = true;
        var fullTree = actions.PrepareAsync(root.Identity, true, default).GetAwaiter().GetResult();
        fake.Protected.Add(child.Identity);
        var beforeProtectedTree = fake.Terminations;
        result = actions.ExecuteAsync(fullTree, default).GetAwaiter().GetResult();
        Check(fake.Terminations == beforeProtectedTree && result.All(r => r.State != ProcessActionState.Exited), "protected child blocks entire tree before any mutation");
        fake.Protected.Clear();
        var reads = 0;
        fake.OnRead = () => { if (++reads == 2) fake.Rows = [root, child, Row(1004, 400) with { ParentPid = child.Id }]; };
        result = actions.ExecuteAsync(fullTree, default).GetAwaiter().GetResult();
        Check(result.Single().State == ProcessActionState.Reconfirm && fake.Terminations == beforeProtectedTree, "last-moment tree change blocks before first mutation");
        fake.OnRead = null; fake.Rows = [root, child];
        fake.TerminateErrorPid = root.Id;
        result = actions.ExecuteAsync(fullTree, default).GetAwaiter().GetResult();
        Check(result.Single(r => r.Identity == child.Identity).State == ProcessActionState.Exited &&
            result.Single(r => r.Identity == root.Identity) is { State: ProcessActionState.Partial, NativeCode: 5 }, "partial failure attributed to actual target, verified child kept");
        Check(fake.Opened == fake.Disposed, "all protected and changed tree handles freed");
        var service = relation with { Kind = "Dienst", Key = "service-fixture", Value = "Starttyp: Auto" };
        var task = relation with { Kind = "Geplante Aufgabe", Key = "task-fixture" };
        var left = snapshot with { Relations = [service, task, task with { Value = "second action" }] };
        var changes = ProcessSystemSnapshot.Compare(left, snapshot with { Relations = [service with { Value = "Starttyp: Manual" }] });
        Check(changes.Contains("Dienst geändert") && changes.Contains("Geplante Aufgabe nicht mehr beobachtet"), "service changes and duplicate task actions compare without losing removals");
        Check(ProcessSystemSnapshot.Compare(left, snapshot with { Relations = [service with { Availability = DataAvailability.AccessDenied }] })
            .Contains("Quelle unvollständig"), "incomplete source never asserts safe changes");
        foreach (var invalid in new[]
        {
            snapshot with { Processes = [snapshot.Processes[0], snapshot.Processes[0]] },
            snapshot with { Processes = [snapshot.Processes[0] with { ObservedAt = default }] },
            snapshot with { Relations = [relation with { Availability = (DataAvailability)999 }] },
            snapshot with { Relations = [relation with { Timestamp = default }] }
        })
        {
            try { ProcessSystemSnapshot.Parse(invalid.LocalJson()); Check(false, "invalid snapshot accepted"); }
            catch (System.Text.Json.JsonException) { Check(true, "invalid identity/time/availability rejected"); }
        }
        var fixtureScript = """
            function Get-CimInstance { throw [UnauthorizedAccessException]::new('isolated fixture') }
            function Get-NetTCPConnection {}
            function Get-NetUDPEndpoint {}
            function Test-Path { return $false }
            function Get-AppxPackage {}
            function Get-ScheduledTask {
                foreach($value in @('"C:\Fixture Folder\program.exe"','C:\invalid|path.exe','relative.exe')) {
                    [pscustomobject]@{TaskPath='\Fixture\';TaskName=$value;Actions=@([pscustomobject]@{Execute=$value})}
                }
            }
            """ + "\n" + ProcessCorrelation.SystemScript;
        var scriptResult = AdminShell.RunAsync(fixtureScript, TimeSpan.FromSeconds(15), default).GetAwaiter().GetResult();
        Check(scriptResult.ExitCode == 0 && !scriptResult.OutputTruncated, "isolated real PowerShell correlation script executes");
        using (var document = System.Text.Json.JsonDocument.Parse(scriptResult.Output))
        {
            var rows = document.RootElement.EnumerateArray().ToArray();
            Check(rows.Single(r => r.GetProperty("Kind").GetString() == "Dienst").GetProperty("Availability").GetString() == "AccessDenied",
                "PowerShell access denied is a source state, not generic failure");
            var tasks = rows.Where(r => r.GetProperty("Kind").GetString() == "Geplante Aufgabe").ToArray();
            Check(tasks.Length == 3 && tasks.Count(r => r.GetProperty("Executable").ValueKind == System.Text.Json.JsonValueKind.String) == 1 &&
                tasks.Single(r => r.GetProperty("Executable").ValueKind == System.Text.Json.JsonValueKind.String).GetProperty("Executable").GetString() == @"C:\Fixture Folder\program.exe",
                "quoted task executable normalized, invalid and relative actions observed without aborting provider");
        }
        return _assertions;
    }

    private static string DisposeAndProtection(this IProcessTargetHandle handle)
    {
        using (handle) return handle.Target.Protection;
    }
    internal static ProcessMetric Row(int pid, long ticks) => new("fixture", pid, 10, 50, 2, 10, 1, 2, "Lesbar", ticks)
        { ObservedAt = DateTimeOffset.UtcNow };
    private sealed class FakeSource : IProcessActionSource
    {
        internal IReadOnlyList<ProcessMetric> Rows = [];
        internal readonly HashSet<ProcessIdentity> Protected = [];
        internal string Protection = "";
        internal int? Error;
        internal int Opened, Disposed, Terminations;
        internal bool TerminationWorks = true;
        internal Action? OnTerminate;
        internal Action? OnRead;
        internal int? TerminateErrorPid;
        public Task<IReadOnlyList<ProcessMetric>> ReadAsync(CancellationToken token) { OnRead?.Invoke(); return Task.FromResult(Rows); }
        public Task<IReadOnlySet<ProcessIdentity>> ProtectedAsync(CancellationToken token) => Task.FromResult<IReadOnlySet<ProcessIdentity>>(Protected);
        public IProcessTargetHandle Open(ProcessIdentity identity, bool terminate)
        {
            if (Error is { } error) throw new Win32Exception(error);
            Opened++;
            return new FakeHandle(this, new(identity, "fixture", @"C:\fixture.exe", "fixture", 1, Protection));
        }
        private sealed class FakeHandle(FakeSource owner, ProcessTarget target) : IProcessTargetHandle
        {
            public ProcessTarget Target => target;
            public bool Exited { get; private set; }
            public void Terminate()
            {
                if (owner.TerminateErrorPid == target.Identity.Pid) throw new Win32Exception(5);
                owner.Terminations++; Exited = owner.TerminationWorks; owner.OnTerminate?.Invoke();
            }
            public void Dispose() => owner.Disposed++;
        }
    }
}
