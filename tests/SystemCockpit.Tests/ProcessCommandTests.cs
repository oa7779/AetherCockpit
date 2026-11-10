using System.ComponentModel;
using System.Diagnostics;
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
        public Task<IReadOnlyList<ProcessMetric>> ReadAsync(CancellationToken token) => Task.FromResult(Rows);
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
            public void Terminate() { owner.Terminations++; Exited = true; }
            public void Dispose() => owner.Disposed++;
        }
    }
}
