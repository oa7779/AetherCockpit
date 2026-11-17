using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class NeonFrameProof
{
    internal sealed record Sample(int Round, string Scenario, double DurationMs, double CpuMs,
        double OneCoreCpuPercent, double NormalizedCpuPercent, long WorkingSet, long PrivateBytes,
        int Threads, int Handles, int LayoutUpdates, int Intervals, double? MedianIntervalMs, double? P95IntervalMs);

    internal static void Run(string output)
    {
        if (!Path.IsPathFullyQualified(output) || File.Exists(output))
            throw new ArgumentException("Choose a new absolute measurement JSON path.");
        var records = new List<Sample>();
        var lifecycle = new List<object>();
        var border = new Border { Background = Brushes.Black, BorderBrush = Brushes.Cyan, Margin = new Thickness(24) };
        var root = new AdornerDecorator { Child = border };
        var window = new Window { Content = root, Width = 1280, Height = 720, ShowInTaskbar = false };
        double dpi;
        try
        {
            window.Show();
            Pump(TimeSpan.FromMilliseconds(100));
            dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
            for (var round = 1; round <= 3; round++)
            {
                records.Add(Measure(round, "idle", border));
                // Alternate order to expose rather than hide shared-process warmup effects.
                foreach (var legacy in round == 2 ? new[] { false, true } : new[] { true, false })
                {
                    if (legacy)
                    {
                        FrameGlow.SetVisible(border, true);
                        FrameGlow.SetEnabled(border, true);
                        records.Add(Measure(round, "old-moving", border));
                        FrameGlow.SetEnabled(border, false);
                        FrameGlow.SetVisible(border, false);
                    }
                    else
                    {
                        AetherNeonFrame.SetEnabled(border, true);
                        AetherNeonFrame.SetBreathingEnabled(border, false);
                        records.Add(Measure(round, "new-static", border));
                        AetherNeonFrame.SetBreathingEnabled(border, true);
                        records.Add(Measure(round, "new-breathing", border));
                        window.Hide();
                        records.Add(Measure(round, "new-hidden", border));
                        window.Show();
                        AetherNeonFrame.SetEnabled(border, false);
                    }
                }
                AetherNeonFrame.SetEnabled(border, true);
                for (var batch = 0; batch <= 5; batch++)
                {
                    if (batch > 0)
                        for (var cycle = 0; cycle < 20; cycle++)
                        {
                            root.Child = null;
                            Pump(TimeSpan.FromMilliseconds(5));
                            if (root.AdornerLayer.GetAdorners(border) is { Length: > 0 } || AetherNeonFrame.GetMotionActive(border))
                                throw new InvalidOperationException("Frame remained active after unload.");
                            root.Child = border;
                            Pump(TimeSpan.FromMilliseconds(5));
                            if (root.AdornerLayer.GetAdorners(border)?.Length != 1)
                                throw new InvalidOperationException("Frame count changed during lifecycle stress.");
                        }
                    Pump(TimeSpan.FromMilliseconds(100));
                    using var process = Process.GetCurrentProcess();
                    process.Refresh();
                    lifecycle.Add(new { Round = round, CompletedCycles = (round - 1) * 100 + batch * 20, WorkingSet = process.WorkingSet64,
                        PrivateBytes = process.PrivateMemorySize64, Threads = process.Threads.Count, Handles = process.HandleCount });
                }
                AetherNeonFrame.SetEnabled(border, false);
            }
        }
        finally
        {
            AetherNeonFrame.SetEnabled(border, false);
            FrameGlow.SetEnabled(border, false);
            FrameGlow.SetVisible(border, false);
            window.Close();
        }
        var report = new
        {
            Schema = 1, CapturedAt = DateTimeOffset.UtcNow, Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription, LogicalProcessors = Environment.ProcessorCount,
            WpfRenderTier = RenderCapability.Tier >> 16, DpiScale = dpi, WidthDip = 1280, HeightDip = 720,
            WindowsAnimations = SystemParameters.ClientAreaAnimation, HighContrast = SystemParameters.HighContrast,
            PowerLine = SystemParameters.PowerLineStatus.ToString(),
            EnergyState = "Power-line state observed only; active power plan and competing workload not controlled.",
            WarmupSeconds = 1, MeasurementSeconds = 3, SourceCommit = Environment.GetEnvironmentVariable("AETHER_MEASUREMENT_COMMIT"),
            Scope = "Same synthetic STA host, no product start, no sensors or administrative commands.",
            Records = records, Lifecycle = lifecycle,
            Summary = records.GroupBy(r => r.Scenario).Select(group => new
            {
                Scenario = group.Key, Runs = group.Count(),
                MedianCpu = Percentile(group.Select(r => r.OneCoreCpuPercent), .5),
                MinCpu = group.Min(r => r.OneCoreCpuPercent), MaxCpu = group.Max(r => r.OneCoreCpuPercent),
                MinHandles = group.Min(r => r.Handles), MaxHandles = group.Max(r => r.Handles),
                MinThreads = group.Min(r => r.Threads), MaxThreads = group.Max(r => r.Threads),
                MinWorkingSet = group.Min(r => r.WorkingSet), MaxWorkingSet = group.Max(r => r.WorkingSet)
            }).ToArray()
        };
        using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("MEASURED: three paired frame rounds; " + output);
    }

    private static Sample Measure(int round, string scenario, FrameworkElement element)
    {
        var oldEnabled = FrameGlow.GetVisible(element) || FrameGlow.GetEnabled(element);
        var newEnabled = AetherNeonFrame.GetEnabled(element);
        if (scenario == "idle" && (oldEnabled || newEnabled) ||
            scenario == "old-moving" && (!oldEnabled || newEnabled) ||
            scenario.StartsWith("new-", StringComparison.Ordinal) && (oldEnabled || !newEnabled))
            throw new InvalidOperationException("Comparison phase has overlapping or missing frame owners: " + scenario);
        Pump(TimeSpan.FromSeconds(1));
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpu = process.TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        var intervals = new List<double>();
        var layouts = 0;
        TimeSpan? previous = null;
        void Layout(object? sender, EventArgs args) => layouts++;
        void Frame(object? sender, EventArgs args)
        {
            var now = ((RenderingEventArgs)args).RenderingTime;
            if (previous is { } last && now > last) intervals.Add((now - last).TotalMilliseconds);
            previous = now;
        }
        element.LayoutUpdated += Layout;
        CompositionTarget.Rendering += Frame;
        try { Pump(TimeSpan.FromSeconds(3)); }
        finally { CompositionTarget.Rendering -= Frame; element.LayoutUpdated -= Layout; }
        watch.Stop();
        process.Refresh();
        var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        return new(round, scenario, watch.Elapsed.TotalMilliseconds, cpuMs,
            100 * cpuMs / watch.Elapsed.TotalMilliseconds,
            100 * cpuMs / watch.Elapsed.TotalMilliseconds / Environment.ProcessorCount,
            process.WorkingSet64, process.PrivateMemorySize64, process.Threads.Count, process.HandleCount,
            layouts, intervals.Count, Percentile(intervals, .5), Percentile(intervals, .95));
    }

    private static double? Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? null : sorted[(int)Math.Ceiling((sorted.Length - 1) * percentile)];
    }

    internal static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        void Stop(object? sender, EventArgs args) { timer.Stop(); frame.Continue = false; }
        timer.Tick += Stop;
        try { timer.Start(); Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); timer.Tick -= Stop; }
    }
}
