using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class GuardianResourceProof
{
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private sealed record Metrics(int Handles, int Threads, long PrivateBytes, long WorkingSet, double CpuMs);
    private static Metrics Sample()
    {
        using var self = Process.GetCurrentProcess(); self.Refresh();
        return new(self.HandleCount, self.Threads.Count, self.PrivateMemorySize64, self.WorkingSet64, self.TotalProcessorTime.TotalMilliseconds);
    }
    internal static void Run(string output)
    {
        if (!Path.IsPathFullyQualified(output) || File.Exists(output)) throw new ArgumentException("Neue absolute Ergebnisdatei erforderlich.");
        var old = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var host = new Window { Width = 1280, Height = 800, ShowInTaskbar = false };
        try
        {
            host.Show(); Pump();
            Cycle(host);
            Collect();
            var before = Sample();
            var weak = new List<WeakReference>();
            var cycles = new List<Metrics>();
            for (var index = 0; index < 12; index++) { weak.Add(Cycle(host)); Collect(); cycles.Add(Sample()); }
            var aliveImmediately = weak.Count(w => w.IsAlive);
            var after = Sample();
            var storage = Task.Run(StorageAsync).GetAwaiter().GetResult();
            var idleView = new GuardianView(Path.Combine(Path.GetTempPath(), "must-not-create-guardian-idle")) { Synthetic = true };
            AetherNeonFrame.SetReducedMotion(idleView, true);
            host.Content = new AdornerDecorator { Child = idleView }; Pump();
            using var self = Process.GetCurrentProcess();
            var cpu = self.TotalProcessorTime;
            var stamp = Stopwatch.StartNew();
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
            self.Refresh();
            var idleCpu = (self.TotalProcessorTime - cpu).TotalMilliseconds;
            idleView.ShutdownAsync().GetAwaiter().GetResult(); host.Content = null;
            Collect();
            var alive = weak.Count(w => w.IsAlive);
            var verdict = alive == 0 && after.Handles - before.Handles <= 64 && after.Threads - before.Threads <= 8;
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Scope = "Guardian-only short proof; synthetic UI/cases and real own vault files; no system repairs or foreign mutations",
                Cycles = cycles, Before = before, After = after, ViewsAliveImmediately = aliveImmediately, ViewsAliveAfterCollection = alive,
                Storage = storage, IdleSeconds = stamp.Elapsed.TotalSeconds, IdleCpuMs = idleCpu, Passed = verdict,
                Gates = "12/12 views collectible, warmed handle delta <=64, thread delta <=8. Private Bytes and Working Set observations, not leak-free guarantees.",
                Limit = "15-second idle and short cycles are not long-term operation or physical monitor/DPI acceptance"
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (!verdict) throw new InvalidOperationException("Guardian resource/collectibility gate failed; measurements retained.");
            Console.WriteLine("PASS: twelve collectible Guardian views, keyboard/focus, native gate, vault/journal/case cycles and measured 15-second idle.");
        }
        finally { host.Content = null; host.Close(); Pump(); SynchronizationContext.SetSynchronizationContext(old); }
    }
    private static void Collect()
    {
        Pump(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Pump();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Cycle(Window window)
    {
        var context = new GuardianContext("synthetic", "synthetic");
        var fixture = GuardianVault.Artifact(RecoveryKind.ConfigurationBackup, "Long technical identity", context,
            "LongIdentity:" + new string('X', 4096));
        var view = new GuardianView(Path.Combine(Path.GetTempPath(), "must-not-create-guardian-resource")) { Synthetic = true };
        AetherNeonFrame.SetReducedMotion(view, true);
        window.Content = new AdornerDecorator { Child = view };
        try
        {
            Pump(); view.ApplyFixtures([fixture]); Pump();
            var search = (TextBox)view.FindName("Search");
            search.Text = new string('X', 256); Pump();
            var grid = (DataGrid)view.FindName("Rows");
            if (grid.Items.Count != 1) throw new InvalidOperationException("Long technical identity search failed.");
            grid.SelectedIndex = 0;
            grid.Focus(); Pump();
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view)!, 0, Key.Enter)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            grid.RaiseEvent(key); Pump();
            if (!key.Handled || !((TextBox)view.FindName("Details")).IsKeyboardFocusWithin)
                throw new InvalidOperationException("Guardian Enter-to-detail focus path failed.");
            if (((Button)view.FindName("TraceStart")).IsEnabled) throw new InvalidOperationException("Unapproved production trace is actionable.");
            foreach (var name in new[] { "Search", "Area", "Rows", "Details", "TargetId", "TargetKind" })
            {
                var element = (UIElement)view.FindName(name);
                var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(element);
                if (peer is null || string.IsNullOrWhiteSpace(peer.GetName()))
                    throw new InvalidOperationException("Guardian accessible name missing: " + name);
            }
            var details = (TextBox)view.FindName("Details");
            details.Focus();
            if (!details.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)))
                throw new InvalidOperationException("Guardian reverse tab focus path failed.");
            return new WeakReference(view);
        }
        finally
        {
            view.ShutdownAsync().GetAwaiter().GetResult();
            // Replace the active text-input scope in the still-live host, as when navigating away in AETHER.
            var replacement = new TextBox();
            window.Content = replacement; Pump(); replacement.Focus(); Pump();
        }
    }
    private sealed class OwnAction : IGuardianAction
    {
        internal GuardianTarget Target = new("fixture", "resource", EvidenceFile.Hash("resource"), "Enabled", new Dictionary<string, string>());
        public Task<GuardianTarget> ReadAsync(GuardianTarget target, CancellationToken token) => Task.FromResult(Target);
        public Task<string> ExecuteAsync(GuardianPlan plan, CancellationToken token) { Target = Target with { State = plan.ExpectedState }; return Task.FromResult("Own fixture"); }
    }
    private static async Task<object> StorageAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "GdnResource-" + Guid.NewGuid().ToString("N"));
        var context = new GuardianContext("resource-fixture", "resource-user");
        var vault = new GuardianVault(root);
        var core = new GuardianCore(vault, () => context);
        var action = new OwnAction();
        try
        {
            var before = Sample();
            for (var index = 0; index < 32; index++)
            {
                var expected = action.Target.State == "Enabled" ? "Disabled" : "Enabled";
                var plan = new GuardianPlan("fixture", action.Target, expected, "Own fixture", "None", "Synthetic state", "No external target",
                    "None", "Durable own fixture journal", "Reread state", RollbackCapability.NotAvailable, "Own fixture only");
                var tx = await core.ExecuteAsync(core.Plan("Own resource cycle", plan), action, true, default);
                if (tx.State != ChangeState.Completed) throw new InvalidOperationException("Resource journal fixture did not verify.");
                var snapshot = new IntelligenceSnapshot(1, Guid.NewGuid(), "Own case fixture", DateTimeOffset.UtcNow, [], [], [], context.Host);
                var evt = new TimelineEntry("case-" + index, snapshot.Timestamp, snapshot.Timestamp, "ApplicationCrash", "Own case fixture", null,
                    IntelligenceModel.Metadata("Synthetic case", snapshot.Timestamp), "No real crash", new("Application", index, "Application Error", 1000));
                await vault.SaveAsync(GuardianWorkflows.CrashCase(evt, snapshot, context, [evt], []), default);
                if ((await new GuardianVault(root).LoadAsync(default)).Count != 2 * (index + 1))
                    throw new InvalidOperationException("Vault reload lost current journal/case state.");
            }
            var objects = await vault.LoadAsync(default);
            if (objects.Sum(a => a.Transaction?.Journal.Count ?? 0) != 32 * 7) throw new InvalidOperationException("Unexpected journal growth.");
            return new { Iterations = 32, Objects = objects.Count, JournalEntries = 32 * 7,
                Bytes = new DirectoryInfo(root).EnumerateFiles().Sum(f => f.Length), Before = before, After = Sample(),
                CrashCases = objects.Count(a => a.Kind == RecoveryKind.CrashCase) };
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    internal static int DeniedCycles(string output)
    {
        if (SystemActions.IsAdmin()) throw new InvalidOperationException("AccessDenied-Ressourcenprüfung nur im nicht erhöhten Testhost; administrativ den Lifecycle-Abnahmemodus verwenden.");
        if (!Path.IsPathFullyQualified(output) || File.Exists(output)) throw new ArgumentException("Neue absolute Ergebnisdatei erforderlich.");
        var samples = new List<Metrics>(); var reports = new List<string>();
        for (var index = 0; index < 4; index++)
        {
            var report = output + ".attempt-" + index + ".json";
            var exit = GuardianTraceAcceptance.Run(report);
            using var json = JsonDocument.Parse(File.ReadAllText(report));
            if (exit != 2 || json.RootElement.GetProperty("NativeCode").GetInt32() != 5 ||
                !json.RootElement.GetProperty("CleanupVerified").GetBoolean() ||
                json.RootElement.GetProperty("ManagedNativeBufferBalance").GetInt32() != 0)
                throw new InvalidOperationException("Expected denied own ETW attempt/cleanup not confirmed; inspect " + report);
            reports.Add(report); GC.Collect(); samples.Add(Sample());
        }
        var passed = samples.Skip(1).Max(s => s.Handles) - samples.Skip(1).Min(s => s.Handles) <= 8;
        File.WriteAllText(output, JsonSerializer.Serialize(new { Scope = "Four own ETW start attempts; real AccessDenied only, not a successful ETW lifecycle",
            Samples = samples, Reports = reports, Passed = passed, WarmedHandleSpreadGate = 8, Limit = "Short proof, not a general leak-free assertion" },
            new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 2;
    }
}
