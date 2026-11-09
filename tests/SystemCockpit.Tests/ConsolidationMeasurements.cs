using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using SystemCockpit;
using SystemCockpit.Controls;

internal static class ConsolidationMeasurements
{
    public static void Run(string output)
    {
        if (!Path.IsPathFullyQualified(output)) throw new ArgumentException("Absolute measurement output path required.");
        if (File.Exists(output)) throw new IOException("Measurement output already exists; choose a separate file.");
        var records = new List<object>();
        var enumeration = new List<double>();
        var adapterCount = 0;
        for (var index = 0; index < 25; index++)
        {
            var watch = Stopwatch.StartNew();
            var adapters = NetworkInterface.GetAllNetworkInterfaces();
            adapterCount = adapters.Length;
            foreach (var adapter in adapters.Where(a => a.OperationalStatus == OperationalStatus.Up &&
                         a.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                _ = adapter.GetIPStatistics();
            watch.Stop();
            enumeration.Add(watch.Elapsed.TotalMilliseconds);
        }
        records.Add(new { Scenario = "adapter-enumeration-with-counters", Samples = enumeration.Count,
            AdapterCount = adapterCount, FirstMs = enumeration[0], WarmMedianMs = Percentile(enumeration.Skip(1), .5),
            WarmP95Ms = Percentile(enumeration.Skip(1), .95) });

        var border = new Border { Background = Brushes.Black, BorderBrush = Brushes.Cyan, Margin = new Thickness(24) };
        var root = new AdornerDecorator { Child = border };
        var window = new Window { Content = root, Width = 1280, Height = 720, ShowInTaskbar = false };
        HardwareBoardView? board = null;
        var ready = Stopwatch.StartNew();
        try
        {
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            ready.Stop();
            records.Add(new { Scenario = "synthetic-window-ready", ElapsedMs = ready.Elapsed.TotalMilliseconds,
                DpiX = VisualTreeHelper.GetDpi(window).DpiScaleX, DpiY = VisualTreeHelper.GetDpi(window).DpiScaleY,
                WindowWidth = window.ActualWidth, WindowHeight = window.ActualHeight });
            records.Add(Measure("synthetic-idle-window"));
            FrameGlow.SetVisible(border, true);
            FrameGlow.SetEnabled(border, true);
            records.Add(Measure("frameglow-original-visible"));
            FrameGlow.SetEnabled(border, false);
            FrameGlow.SetVisible(border, false);
            board = new HardwareBoardView { DataContext = new CockpitModel { MotionEnabled = true } };
            board.UseScreensaverLayout();
            board.Apply(HardwareBoardTests.IllustrationExample);
            border.Child = board;
            records.Add(Measure("synthetic-board-visible"));
            board.Visibility = Visibility.Collapsed;
            records.Add(Measure("synthetic-board-collapsed"));
        }
        finally
        {
            FrameGlow.SetEnabled(border, false);
            FrameGlow.SetVisible(border, false);
            board?.Shutdown();
            window.Close();
        }
        var result = new
        {
            Schema = 1, CapturedAt = DateTimeOffset.UtcNow, Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalProcessors = Environment.ProcessorCount, WindowsAnimations = SystemParameters.ClientAreaAnimation,
            WpfRenderTier = RenderCapability.Tier >> 16,
            Scope = "Synthetic STA test host; no hardware provider, main cockpit, network requests or administrative actions.",
            Records = records
        };
        using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("MEASURED: synthetic UI and local adapter enumeration; " + output);
    }

    private static object Measure(string scenario)
    {
        Pump(TimeSpan.FromSeconds(1));
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpu = process.TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        var intervals = new List<double>();
        TimeSpan? previous = null;
        void Frame(object? sender, EventArgs args)
        {
            var now = ((RenderingEventArgs)args).RenderingTime;
            if (previous is { } last && now > last) intervals.Add((now - last).TotalMilliseconds);
            previous = now;
        }
        CompositionTarget.Rendering += Frame;
        try { Pump(TimeSpan.FromSeconds(3)); }
        finally { CompositionTarget.Rendering -= Frame; }
        watch.Stop();
        process.Refresh();
        var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        return new
        {
            Scenario = scenario, DurationMs = watch.Elapsed.TotalMilliseconds,
            ProcessCpuMs = cpuMs, OneCoreCpuPercent = 100 * cpuMs / watch.Elapsed.TotalMilliseconds,
            NormalizedCpuPercent = 100 * cpuMs / watch.Elapsed.TotalMilliseconds / Environment.ProcessorCount,
            WorkingSetBytes = process.WorkingSet64, PrivateBytes = process.PrivateMemorySize64,
            Threads = process.Threads.Count, Handles = process.HandleCount,
            CompositionIntervals = intervals.Count, CompositionMedianMs = Percentile(intervals, .5),
            CompositionP95Ms = Percentile(intervals, .95)
        };
    }

    private static double? Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? null : sorted[(int)Math.Ceiling((sorted.Length - 1) * percentile)];
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        void Stop(object? sender, EventArgs args) { timer.Stop(); frame.Continue = false; }
        timer.Tick += Stop;
        try { timer.Start(); Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); timer.Tick -= Stop; }
    }
}
