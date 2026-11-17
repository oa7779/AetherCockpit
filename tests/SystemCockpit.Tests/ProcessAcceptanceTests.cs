using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class ProcessAcceptanceTests
{
    internal static int Fixture()
    {
        Thread.Sleep(TimeSpan.FromSeconds(45));
        return 0;
    }
    internal static void Live(string output)
    {
        if (!Path.IsPathFullyQualified(output) || File.Exists(output)) throw new ArgumentException("Neue absolute Ergebnisdatei erforderlich.");
        var executable = Path.Combine(AppContext.BaseDirectory, "SystemCockpit.Tests.exe");
        using var child = Process.Start(new ProcessStartInfo { FileName = executable, Arguments = "--process-fixture", UseShellExecute = false, CreateNoWindow = true })
            ?? throw new InvalidOperationException("Testprozess nicht gestartet.");
        var results = new List<object>();
        try
        {
            var identity = new ProcessIdentity(child.Id, child.StartTime.ToUniversalTime().Ticks);
            var details = ProcessInspector.Read(identity, default);
            if (details.Identity != identity || details.Facts.Count < 10) throw new InvalidOperationException("Native Detailprobe fehlt.");
            results.Add(new { Kind = "native-fixture-details", Facts = details.Facts.Count });
            var native = new NativeProcessActionSource();
            var source = new FixtureSource(identity, native);
            var journal = new List<string>();
            var actions = new ProcessActions(source, (name, _) => journal.Add(name));
            var timer = Stopwatch.StartNew();
            var plan = actions.PrepareAsync(identity, false, default).GetAwaiter().GetResult();
            var result = actions.ExecuteAsync(plan, default).GetAwaiter().GetResult();
            if (result.Single().State != ProcessActionState.Exited || !child.WaitForExit(5000))
                throw new InvalidOperationException("Kontrolliertes Testprozessende nicht verifiziert.");
            results.Add(new { Kind = "native-fixture-termination", State = result.Single().State.ToString(), ElapsedMs = timer.Elapsed.TotalMilliseconds,
                IntentAndResult = journal.SequenceEqual(new[] { "process-terminate-intent", "process-terminate-result" }) });
            using var self = Process.GetCurrentProcess();
            var handleBefore = self.HandleCount;
            for (var index = 0; index < 100; index++)
            {
                using var handle = ProcessNative.Open(new(self.Id, self.StartTime.ToUniversalTime().Ticks));
                if (ProcessNative.Exited(handle)) throw new InvalidOperationException("Eigene Identität unerwartet beendet.");
            }
            self.Refresh();
            results.Add(new { Kind = "100-native-handle-lifecycles", Before = handleBefore, After = self.HandleCount });
            var nativeRows = native.ReadAsync(default).GetAwaiter().GetResult();
            if (!nativeRows.Any(r => r.Id == self.Id && r.Identity.IsKnown)) throw new InvalidOperationException("Reale Prozessbaumquelle fehlt.");
            results.Add(new { Kind = "native-process-topology", Rows = nativeRows.Count });
            var selfBlocked = false;
            try { ProcessActions.Create().PrepareAsync(new(self.Id, self.StartTime.ToUniversalTime().Ticks), false, default).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { selfBlocked = true; }
            if (!selfBlocked) throw new InvalidOperationException("Eigenschutz fehlt.");
            results.Add(new { Kind = "production-self-preflight", Blocked = selfBlocked });
            var relations = ProcessCorrelation.ReadAsync(new(self.Id, self.StartTime.ToUniversalTime().Ticks), default).GetAwaiter().GetResult();
            results.Add(new { Kind = "real-self-readonly-correlation", Rows = relations.Count,
                States = relations.GroupBy(r => r.Availability.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                Kinds = relations.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Count()) });
            var system = ProcessCorrelation.ReadSystemAsync(default).GetAwaiter().GetResult();
            results.Add(new { Kind = "real-system-readonly-sources", Rows = system.Count,
                Sources = system.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => new
                { Rows = g.Count(), States = g.GroupBy(r => r.Availability.ToString()).ToDictionary(s => s.Key, s => s.Count()) }) });
            File.WriteAllText(output, JsonSerializer.Serialize(new { Scope = "Owned short-lived test process only; own host read-only correlation. No foreign termination.", Results = results },
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: controlled native fixture termination, details, 100 handle lifecycles and read-only self correlation.");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); }
        }
    }

    private sealed class FixtureSource(ProcessIdentity fixture, NativeProcessActionSource native) : IProcessActionSource
    {
        public Task<IReadOnlyList<ProcessMetric>> ReadAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ProcessMetric>>([ProcessCommandTests.Row(fixture.Pid, fixture.StartTicks)]);
        public Task<IReadOnlySet<ProcessIdentity>> ProtectedAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlySet<ProcessIdentity>>(new HashSet<ProcessIdentity>());
        public IProcessTargetHandle Open(ProcessIdentity identity, bool terminate)
        {
            if (identity != fixture) throw new InvalidOperationException("Testquelle darf ausschließlich eigenes Fixture öffnen.");
            return native.Open(identity, terminate);
        }
    }

    internal static void Proof(string directory)
    {
        var old = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try { ProofCore(directory); }
        finally { SynchronizationContext.SetSynchronizationContext(old); }
    }
    private static void ProofCore(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || Directory.Exists(directory)) throw new ArgumentException("Neuer absoluter Proofordner erforderlich.");
        Directory.CreateDirectory(directory);
        var time = DateTimeOffset.UtcNow;
        var fixtures = Enumerable.Range(1, 5000).Select(index => ProcessCommandTests.Row(10000 + index, time.UtcTicks - 1000000000 + index)
            with { Name = $"AETHER Fixture {index:0000}", ParentPid = index > 1 ? 10001 : null }).ToArray();
        var measures = new List<object>();
        ModernWindowAppearance.Enable();
        foreach (var theme in new[] { "retro-neon", "windows-modern-light", "windows-modern-dark" })
        foreach (var scale in new[] { 1.0, 1.5, 2.0 })
        {
            SkinCatalog.Apply(Application.Current, theme == "retro-neon" ? theme : "windows-modern", theme.EndsWith("light", StringComparison.Ordinal));
            var view = new ProcessCommandView { Synthetic = true };
            AetherNeonFrame.SetReducedMotion(view, true);
            var window = new Window { Content = view, Width = 1460, Height = 880, ShowInTaskbar = false };
            window.Show();
            var watch = Stopwatch.StartNew();
            view.Apply(Snapshot(time, fixtures));
            window.UpdateLayout(); Pump();
            var ready = watch.Elapsed.TotalMilliseconds;
            if (ready > 2500) throw new InvalidOperationException($"Vorab gesetztes Gate 5000 Zeilen/2500 ms überschritten: {ready:F1} ms");
            view.Select(fixtures[2].Identity);
            watch.Restart();
            view.Apply(Snapshot(time.AddSeconds(2), fixtures));
            window.UpdateLayout(); Pump();
            var update = watch.Elapsed.TotalMilliseconds;
            if (update > 2500) throw new InvalidOperationException("Vorab gesetztes Updategate 2500 ms überschritten.");
            var root = new AdornerDecorator();
            window.Content = null; root.Child = view; window.Content = root;
            VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
            root.Measure(new Size(1440, 840)); root.Arrange(new Rect(0, 0, 1440, 840)); root.UpdateLayout();
            var containers = view.ProcessGrid.Items.Cast<object>().Count(item => view.ProcessGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow);
            if (containers >= 200) throw new InvalidOperationException("Recyclingvirtualisierung erzeugte zu viele Container.");
            var bitmap = new RenderTargetBitmap((int)(1440 * scale), (int)(840 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(directory, $"process-{theme}-{scale * 100:0}.png"))) encoder.Save(stream);
            measures.Add(new { Theme = theme, Scale = scale, Rows = fixtures.Length, ReadyMs = ready, UpdateMs = update, Containers = containers });
            if (scale == 1)
            {
                var menu = view.MenuFor(view.History.Rows[fixtures[2].Identity]);
                menu.PlacementTarget = view.ProcessGrid; menu.IsOpen = true; Pump();
                if (!menu.IsOpen || menu.Items.OfType<MenuItem>().Count() < 15) throw new InvalidOperationException("Kontextmenü nicht vollständig geöffnet.");
                SaveVisual(menu, Path.Combine(directory, $"context-{theme}.png"));
                menu.IsOpen = false;
                var target = new ProcessTarget(fixtures[2].Identity, "AETHER Fixture", @"C:\AETHER-Fixture\fixture.exe", "fixture", 1, "");
                var dialog = new ProcessActionDialog(new(target, [target], false, time)) { Owner = window, ShowInTaskbar = false };
                dialog.Show(); Pump();
                if (!dialog.CancelButton.IsDefault || !dialog.CancelButton.IsCancel || !dialog.CancelButton.IsKeyboardFocused)
                    throw new InvalidOperationException("Abbrechen nicht Standard/Fokus.");
                SaveVisual(dialog, Path.Combine(directory, $"confirmation-{theme}.png"));
                dialog.Close();
                var previous = fixtures[0].Identity;
                view.Select(previous);
                var clicked = (DataGridRow)view.ProcessGrid.ItemContainerGenerator.ContainerFromItem(view.History.Rows[fixtures[2].Identity]);
                view.ProcessGrid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
                    { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent, Source = clicked });
                if ((view.ProcessGrid.SelectedItem as ProcessRow)?.Identity != fixtures[2].Identity)
                    throw new InvalidOperationException("Rechtsklick verwendet vorherige Auswahl statt angeklickter Zeile.");
                var exact = view.MenuFor(view.History.Rows[fixtures[2].Identity]).Items.OfType<MenuItem>()
                    .Single(item => Equals(item.Header, "Ansicht auf diesen Prozess filtern"));
                exact.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (view.ProcessGrid.Items.Count != 1) throw new InvalidOperationException("Identitätsfilter ist kein exakter Filter.");
                view.Select(previous);
                var tree = (CheckBox)view.FindName("TreeMode"); tree.IsChecked = true;
                var cpuColumn = view.ProcessGrid.Columns.Single(c => c.SortMemberPath == "Current.CpuPercent");
                typeof(DataGrid).GetMethod("OnSorting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(view.ProcessGrid, [new DataGridSortingEventArgs(cpuColumn)]);
                if (((ProcessRow)view.ProcessGrid.Items[0]).Identity != fixtures[0].Identity)
                    throw new InvalidOperationException("Baumsortierung trennt Eltern und Kinder.");
                var pause = (CheckBox)view.FindName("Pause");
                pause.IsChecked = true;
                var frozenCpu = ((ProcessRow)view.ProcessGrid.Items[0]).Current.CpuPercent;
                view.Apply(Snapshot(time.AddSeconds(4), fixtures.Select(r => r with { CpuPercent = 77 }).ToArray()));
                if (((ProcessRow)view.ProcessGrid.Items[0]).Current.CpuPercent != frozenCpu ||
                    view.History.Rows[fixtures[0].Identity].Current.CpuPercent != 77)
                    throw new InvalidOperationException("Pause friert nicht nur die Anzeige ein.");
                pause.IsChecked = false;
                if (((ProcessRow)view.ProcessGrid.Items[0]).Current.CpuPercent != 77)
                    throw new InvalidOperationException("Fortsetzen zeigt nicht die aktuelle gemeinsame Probe.");
                view.Run(token => Task.Delay(TimeSpan.FromMinutes(1), token));
                var shutdown = view.ShutdownAsync();
                var dispatcher = Dispatcher.CurrentDispatcher;
                var frame = new DispatcherFrame();
                shutdown.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                shutdown.GetAwaiter().GetResult();
                if (!view.Pending.IsCompleted) throw new InvalidOperationException("Shutdown lässt Abfrage zurück.");
            }
            window.Close(); view.ShutdownAsync().GetAwaiter().GetResult(); Pump();
            if (scale == 1) IntegratedProof(directory, theme, time, fixtures);
        }
        var resources = new List<object>();
        var references = new List<WeakReference>();
        using (var process = Process.GetCurrentProcess())
        {
            object Resource(string label)
            {
                process.Refresh();
                return new { Label = label, process.HandleCount, process.WorkingSet64, process.PrivateMemorySize64, Threads = process.Threads.Count };
            }
            resources.Add(Resource("before-30-view-lifecycles"));
            for (var cycle = 1; cycle <= 30; cycle++)
            {
                references.Add(Cycle(time, fixtures.Take(500).ToArray()));
                if (cycle % 10 == 0) resources.Add(Resource("after-" + cycle));
            }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Pump();
            resources.Add(Resource("after-separate-reference-gc"));
            resources.Add(new { Alive = references.Count(r => r.IsAlive), Total = references.Count });
            for (var cycle = 0; cycle < 30; cycle++)
            {
                var empty = new Window { Width = 1000, Height = 700, ShowInTaskbar = false };
                empty.Show(); Pump(); empty.Close(); Pump();
            }
            resources.Add(Resource("after-30-empty-window-counterexample"));
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Pump();
            resources.Add(Resource("after-counterexample-reference-gc"));
            resources.Add(new { AliveAfterCounterexample = references.Count(r => r.IsAlive), Total = references.Count });
        }
        SkinCatalog.Apply(Application.Current, "retro-neon");
        File.WriteAllText(Path.Combine(directory, "measurements.json"), JsonSerializer.Serialize(new
        {
            Scope = "Synthetic 5000 processes; root-DPI not physical monitor changes; reduced motion",
            Gates = new { ReadyMs = 2500, UpdateMs = 2500, ContainersLessThan = 200 }, Measurements = measures, Resources = resources
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: 5000-process virtualized proof, nine theme/root-DPI images; all predeclared gates met.");
    }
    internal static DashboardSnapshot Snapshot(DateTimeOffset time, IReadOnlyList<ProcessMetric> rows) =>
        new(time, [20], rows, [], 2, 16, 1, 100, 200, TimeSpan.FromMinutes(1), []);
    private static void IntegratedProof(string directory, string theme, DateTimeOffset time, IReadOnlyList<ProcessMetric> rows)
    {
        var main = new MainWindow(new CockpitSettings()) { WindowState = WindowState.Normal, Width = 1460, Height = 1000, ShowInTaskbar = false };
        var center = (ProcessCommandView)main.FindName("ProcessCenter");
        try
        {
            var sample = new LiveSample(time, 10, 20, 2, 10, 1, 2, [], []);
            main.Model.ApplyDashboard(Snapshot(time, rows), sample); center.Apply(Snapshot(time, rows));
            var pages = (TabControl)main.FindName("Pages");
            pages.SelectedItem = pages.Items.Cast<TabItem>().Single(p => Equals(p.Tag, "Taskmanager"));
            main.Show(); Pump(); main.UpdateLayout();
            center.Select(rows[2].Identity);
            if (center.ProcessGrid.ActualHeight < 100) throw new InvalidOperationException("Integrierte Prozessliste zu klein.");
            SaveVisual(main, Path.Combine(directory, $"main-process-{theme}.png"));
            var palette = new TaskCenterWindow(rows) { Owner = main, ShowInTaskbar = false };
            palette.Show(); palette.SearchBox.Text = rows[2].Name; Pump();
            if (!palette.Results.Items.Cast<CockpitTask>().Any(t => t.QuickAction == "process-id:" + rows[2].Identity))
                throw new InvalidOperationException("Globale Suche verliert genaue Prozessidentität.");
            SaveVisual(palette, Path.Combine(directory, $"palette-process-{theme}.png"));
            palette.Close();
        }
        finally { center.ShutdownAsync().GetAwaiter().GetResult(); main.Close(); Pump(); }
    }
    private static WeakReference Cycle(DateTimeOffset time, IReadOnlyList<ProcessMetric> rows)
    {
        var view = new ProcessCommandView { Synthetic = true };
        var window = new Window { Content = view, Width = 1000, Height = 700, ShowInTaskbar = false };
        window.Show(); view.Apply(Snapshot(time, rows)); window.UpdateLayout(); Pump();
        window.Close(); view.ShutdownAsync().GetAwaiter().GetResult(); window.Content = null; Pump();
        if (!view.Pending.IsCompleted) throw new InvalidOperationException("Shutdown nicht abgeschlossen.");
        return new(view);
    }
    private static void SaveVisual(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(element.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
