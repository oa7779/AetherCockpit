using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AetherOperations;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class HardwareAcceptanceTests
{
    internal static void Run(string path)
    {
        if (!Path.IsPathFullyQualified(path) || File.Exists(path) || !Directory.Exists(Path.GetDirectoryName(path)))
            throw new ArgumentException("Neuer absoluter Berichtspfad erforderlich.");
        var old = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try { Drain(RunAsync(path), TimeSpan.FromMinutes(8)); }
        finally { SynchronizationContext.SetSynchronizationContext(old); }
    }

    private static void Drain(Task task, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (!task.IsCompleted && elapsed.Elapsed < timeout)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(1);
        }
        if (!task.IsCompleted) throw new TimeoutException("Hardware-Abnahmematrix überschreitet ihr Zeitlimit.");
        task.GetAwaiter().GetResult();
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Hardware-Abnahme: " + message);
    }

    private static async Task RunAsync(string path)
    {
        var measurements = new List<object>();
        var gaps = new List<double>();
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed.TotalMilliseconds;
        var heartbeat = new DispatcherTimer(TimeSpan.FromMilliseconds(25), DispatcherPriority.Normal, (_, _) =>
        {
            var now = clock.Elapsed.TotalMilliseconds;
            gaps.Add(now - last);
            last = now;
        }, Dispatcher.CurrentDispatcher);
        HardwareIntelligenceView? view = null;
        Window? window = null;
        try
        {
            var opening = Stopwatch.StartNew();
            view = new HardwareIntelligenceView();
            window = new Window { Content = view, Width = 1440, Height = 820, ShowInTaskbar = false };
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            measurements.Add(new { Kind = "real-open-first-dispatch", Milliseconds = opening.Elapsed.TotalMilliseconds });
            await view.Pending;
            Console.WriteLine("Hardware-Abnahme: reale Systemansicht geladen.");
            measurements.Add(new { Kind = "real-system-ready", Milliseconds = opening.Elapsed.TotalMilliseconds,
                Components = view.Snapshot.Components.Count });
            var selector = (ComboBox)view.FindName("Categories");
            foreach (var category in new[] { HardwareClass.Cpu, HardwareClass.Gpu, HardwareClass.Display, HardwareClass.Audio, HardwareClass.Network })
            {
                var elapsed = Stopwatch.StartNew();
                selector.SelectedValue = category;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var dispatchMs = elapsed.Elapsed.TotalMilliseconds;
                await view.Pending;
                Console.WriteLine("Hardware-Abnahme: " + category);
                measurements.Add(new { Kind = "real-class-switch", Class = category.ToString(), DispatchMs = dispatchMs,
                    ReadyMs = elapsed.Elapsed.TotalMilliseconds,
                    States = view.Snapshot.Components.Where(c => c.Class == category).SelectMany(c => c.Facts)
                        .GroupBy(f => f.State).ToDictionary(g => g.Key, g => g.Count()) });
            }
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var cpu = process.TotalProcessorTime;
            var idle = Stopwatch.StartNew();
            var before = Resources(process);
            await Task.Delay(TimeSpan.FromSeconds(15));
            process.Refresh();
            measurements.Add(new { Kind = "real-idle", ElapsedMs = idle.Elapsed.TotalMilliseconds,
                CpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds, Before = before, After = Resources(process) });
            window.Content = null;
            await view.Pending;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.Content = view;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await view.Pending;
            measurements.Add(new { Kind = "real-reopen", Components = view.Snapshot.Components.Count });
            var actualPending = view.RefreshAsync();
            await Task.Delay(100);
            var navigation = Stopwatch.StartNew();
            window.Content = null;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await actualPending;
            Require(actualPending.IsCompletedSuccessfully, "Reale CIM-Erfassung wurde bei Navigation nicht entleert.");
            measurements.Add(new { Kind = "real-cim-navigation-cancellation", Milliseconds = navigation.Elapsed.TotalMilliseconds });
            window.Content = view;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await view.Pending;
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.ReadInventory = async (_, token) =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(30), token); return []; }
                finally { canceled.TrySetResult(); }
            };
            var running = view.RefreshAsync();
            var closing = Stopwatch.StartNew();
            window.Close();
            await view.ShutdownAsync();
            Require(canceled.Task.IsCompleted && running.IsCompletedSuccessfully, "Schließen entleert abgebrochene Erfassung.");
            measurements.Add(new { Kind = "synthetic-close-during-slow-provider", Milliseconds = closing.Elapsed.TotalMilliseconds });
            view = null;
            window = null;

            var weak = new List<WeakReference>();
            for (var i = 0; i < 30; i++)
            {
                weak.Add(await CycleAsync(i, measurements));
                if ((i + 1) % 10 == 0)
                    measurements.Add(new { Kind = "synthetic-cycle-resources-no-gc", Cycles = i + 1, Resources = Resources(process) });
            }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            process.Refresh();
            measurements.Add(new { Kind = "synthetic-after-30-cycles-no-gc", Resources = Resources(process) });
            await Task.Delay(TimeSpan.FromSeconds(2));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var alive = weak.Count(w => w.IsAlive);
            Require(alive <= 1, "Geschlossene Hardwareansichten bleiben referenziert: " + alive);
            measurements.Add(new { Kind = "synthetic-reference-collection-only", Alive = alive, Total = weak.Count, Resources = Resources(process) });
            await Task.Delay(TimeSpan.FromSeconds(2));
            measurements.Add(new { Kind = "synthetic-after-compositor-settle", Resources = Resources(process) });
            var controlBefore = Resources(process);
            for (var i = 0; i < 30; i++)
            {
                var control = new Window { Content = new TextBlock { Text = "WPF host control" },
                    Width = 400, Height = 200, ShowInTaskbar = false };
                control.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                control.Close();
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
            measurements.Add(new { Kind = "framework-control-30-empty-hosts", Before = controlBefore, After = Resources(process) });
            for (var i = 0; i < 3; i++)
            {
                using var cancellation = new CancellationTokenSource(100);
                var elapsed = Stopwatch.StartNew();
                try
                {
                    await HardwareCpuFrequency.ReadAsync(cancellation.Token);
                    throw new InvalidOperationException("PDH-Sampling nicht abgebrochen.");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    measurements.Add(new { Kind = "real-pdh-cancellation", Round = i + 1, Milliseconds = elapsed.Elapsed.TotalMilliseconds });
                }
            }
            Require(gaps.Count >= 100, "Dispatcher-Beobachtungsdauer zu kurz.");
            var sorted = gaps.Order().ToArray();
            measurements.Add(new { Kind = "dispatcher-heartbeat", Samples = sorted.Length, TargetMs = 25,
                P95Ms = sorted[(int)((sorted.Length - 1) * .95)], MaxMs = sorted[^1] });
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Timestamp = DateTimeOffset.UtcNow,
                Scope = "Actual isolated hardware view and read-only providers; injected errors/large inventories/lifecycle. Not full MainWindow or physical hotplug/resume/DPI.",
                Measurements = measurements,
                NotTested = new[] { "Physical hotplug", "Actual standby/resume", "Physical monitor/DPI change", "Long-term operation" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: real read-only view, 15s idle, 30 fixture navigation/shutdown cycles, 5000-component inventory and cancellation; evidence saved.");
        }
        finally
        {
            heartbeat.Stop();
            if (window is not null) window.Close();
            if (view is not null) await view.ShutdownAsync();
        }
    }

    private static object Resources(Process process)
    {
        process.Refresh();
        return new { process.WorkingSet64, process.PrivateMemorySize64, process.HandleCount, Threads = process.Threads.Count };
    }

    private static async Task<WeakReference> CycleAsync(int index, List<object> measurements)
    {
        var view = new HardwareIntelligenceView { Synthetic = true };
        var window = new Window { Content = view, Width = 1440, Height = 820, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var fixture = HardwareIntelligenceTests.Fixture(HardwareClass.Cpu).First();
            var large = Enumerable.Range(0, index == 0 ? 5000 : 20).Select(i => fixture with { Id = "fixture:" + i }).ToArray();
            var watch = Stopwatch.StartNew();
            view.Present(HardwareClass.Cpu, large);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (index == 0)
            {
                var list = (ListBox)view.FindName("Components");
                Require(list.Items.Count == 5000, "Großes Inventar wurde abgeschnitten.");
                var containers = Enumerable.Range(0, 5000).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) is not null);
                Require(containers < 100, "Komponentenliste virtualisiert nicht: " + containers);
                measurements.Add(new { Kind = "synthetic-5000-components", ReadyMs = watch.Elapsed.TotalMilliseconds, Containers = containers });
            }
            view.ReadInventory = (_, _) => throw new UnauthorizedAccessException("fixture denied");
            await view.RefreshAsync();
            Require(view.Snapshot.Components.Any(c => c.Facts.Any(f => f.State == "AccessDenied")), "AccessDenied fehlt.");
            view.ReadInventory = (_, _) => throw new InvalidOperationException("fixture provider error");
            await view.RefreshAsync();
            Require(view.Snapshot.Components.Any(c => c.Facts.Any(f => f.State == "ProviderError")), "ProviderError fehlt.");
            view.ReadInventory = async (_, token) => { await Task.Delay(30000, token); return []; };
            _ = view.RefreshAsync();
            window.Content = null;
            await view.Pending;
            Require(view.Pending.IsCompletedSuccessfully, "Navigation hat Provider nicht abgebrochen.");
            return new WeakReference(view);
        }
        finally { window.Close(); await view.ShutdownAsync(); }
    }
}
