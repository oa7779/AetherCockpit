using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class DashboardTests
{
    public static int Run()
    {
        var count = 0;
        void Check(bool valid, string message) { count++; if (!valid) throw new InvalidOperationException(message); }
        Check(DashboardMetrics.CalculateProcessCpu(2, 2, 4) == 25, "Process CPU is normalized to entire machine capacity");
        Check(DashboardMetrics.CalculateProcessCpu(100, 1, 4) == 100, "Process CPU bounded at 100");
        try { DashboardMetrics.CalculateProcessCpu(1, 0, 4); throw new InvalidOperationException("Zero CPU interval accepted"); }
        catch (ArgumentOutOfRangeException) { count++; }
        var model = new CockpitModel();
        var time = DateTimeOffset.Now;
        var sample = new LiveSample(time, 25, 50, 16, 32, 1, 2,
            [new("C:\\", "8 GB frei", "info", "32 GB gesamt", 25)], [new("/fixture/fan", "GPU", "GPU FAN", "Fan", 1200, "RPM")]);
        var detail = new DashboardSnapshot(time, [25, null],
            [new("fixture", 123, 25, 100, 4, 50, 1, 2, "Lesbar")],
            [new("adapter", "Ethernet", 1, 2, 100_000_000)], 20, 64, 3, 4, 50, TimeSpan.FromHours(2), []);
        model.ApplyDashboard(detail, sample);
        Check(model.CoreGraphs.Count == 2 && model.CoreGraphs[0].Level == 25 && model.CoreGraphs[1].Value == "--",
            "Every CPU logical processor represented; unknown is not zero");
        Check(model.DetailGraphs[0].Level == 50 && model.DetailGraphs[1].Level == 31.25, "RAM/commit bars use physical and commit limits");
        Check(model.VolumeGraphs[0].Level == 75 && model.AdapterGraphs.Count == 2 && model.SensorGraphs.Count == 1,
            "Volume utilization, adapter RX/TX and hardware sensor graphs wired");
        model.ProcessFilter = "123";
        Check(model.ProcessView.Cast<ProcessMetric>().Count() == 1, "Process PID filter");
        model.ProcessFilter = "absent";
        Check(model.ProcessView.IsEmpty, "Process filter excludes absent names");
        var graph = new DashboardGraph("Fixture", "%", "#24FFE3");
        for (var i = 0; i < 100; i++) graph.Update(i, "fixture", 100);
        Check(graph.Trace.Count == 90 && graph.Level == 99, "Graph retains exactly 90 points and measured bar level");
        graph.Update(null, "Nicht lesbar");
        Check(graph.Trace.Count == 0 && graph.Value == "--", "Failed graph clears stale history, not success shaped");
        var eventRecord = new MonitorEvent(time, "System", 12, 19, 2, "Fixture", "fixture error");
        var snapshot = new EventSnapshot(time, [eventRecord, new(time, "Application", 13, 42, 4, "Fixture", "fixture info")],
            [], [new("0 C:", 5, 2, 80, 1)]);
        var parsed = EventMonitor.Parse(JsonSerializer.Serialize(snapshot));
        Check(parsed.Events.Count == 2 && parsed.Events[0].Severity == "Fehler", "Event JSON and severity roundtrip");
        model.ApplyEvents(parsed);
        Check(model.EventCounts.Single(c => c.Name == "FEHLER").FreePercent == 50 && model.DiskGraphs.Count == 1,
            "Snapshot proportions and one grouped card per physical disk");
        var disk = model.DiskGraphs.Single();
        Check(disk.Name == "0 C:" && disk.Metrics.Length == 4 &&
            disk.Metrics.Select(metric => metric.Accent.ToString()).Distinct().Count() == 4,
            "Grouped disk retains four distinctly colored, named series");
        Check(disk.Metrics.Select(metric => metric.NumericValue)
                .SequenceEqual(new[] { "5.0", "2.0", "80.0", "1.0" }.Select(value =>
                    double.Parse(value, System.Globalization.CultureInfo.InvariantCulture).ToString("0.0"))) &&
            disk.Metrics.Select(metric => metric.Unit).SequenceEqual(new[] { "MB/s", "MB/s", "%", "I/O" }) &&
            disk.Metrics[2].Level == 80,
            "Disk grouping preserves measured values, units and fixed activity scale");
        model.ApplyEvents(parsed with { DiskActivity = [new("0 C:", 4, 3, null, 2), new("1 D:", 8, 1, 25, 0)] });
        Check(model.DiskGraphs.Count == 2 && ReferenceEquals(model.DiskGraphs[0], disk) &&
            disk.Metrics[0].Trace.Count == 2 && disk.Metrics[2].Value == "--" && disk.Metrics[2].Trace.Count == 0,
            "Polling preserves per-disk history while unavailable activity stays unavailable");
        model.EventsFailed("synthetic disk failure");
        Check(model.DiskGraphs.All(item => item.Metrics.All(metric => metric.Value == "--" && metric.Trace.Count == 0)),
            "Failure clears all grouped disk series and stale histories");
        model.ApplyEvents(parsed);
        Check(model.DiskGraphs.Count == 1 && ReferenceEquals(model.DiskGraphs[0], disk),
            "Removed disks disappear without recreating remaining disk cards");
        Check(model.EventBuckets.Count == 24 && model.EventBuckets.Sum(b => b.Count) == 2 && model.EventBuckets[^1].Height == 70,
            "24 hourly histogram buckets retain exact current-window event counts");
        model.SelectedEvent = eventRecord;
        model.ApplyEvents(parsed);
        Check(model.SelectedEvent?.RecordId == 12, "Selected event retained across polling refresh");
        model.EventSeverity = 1;
        Check(model.EventView.Cast<MonitorEvent>().Count() == 1, "Critical/error event filter");
        model.EventFilter = "absent";
        Check(model.EventView.IsEmpty, "Event text filter");
        model.ApplyEvents(new(time, [], ["Application: Zugriff verweigert"], []));
        Check(model.Events.Count == 0 && model.EventStatus.Contains("Zugriff verweigert") && model.SelectedEvent is null,
            "Partial/empty snapshot reports channel failure and clears old selection");
        try { EventMonitor.Parse("{\"Events\":null,\"Issues\":[]}"); throw new InvalidOperationException("Null events accepted"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Datenformat")) { count++; }
        model.DashboardFailed("fixture failed");
        model.EventsFailed("fixture failed");
        Check(model.Processes.Count == 0 && model.CoreGraphs.All(g => g.Value == "--") && model.EventCounts.Count == 0,
            "Metric/event failures clear stale values");
        count += NeonEffects();
        return count;
    }

    internal static int NeonEffects()
    {
        var count = 0;
        void Check(bool valid, string message) { count++; if (!valid) throw new InvalidOperationException(message); }
        var border = new Border { Width = 160, Height = 100, CornerRadius = new CornerRadius(22, 5, 22, 5) };
        var decorator = new AdornerDecorator { Child = border };
        FrameGlow.SetVisible(border, true);
        decorator.Measure(new Size(180, 120));
        decorator.Arrange(new Rect(0, 0, 180, 120));
        border.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var adorner = decorator.AdornerLayer.GetAdorners(border)?.Single();
        Check(adorner is not null && !adorner.IsHitTestVisible,
            "Static neon halo is present with motion disabled and never captures input");
        var rims = (Grid)VisualTreeHelper.GetChild(adorner!, 0);
        Check(rims.Children.OfType<Border>().Count() == 3 && rims.Children.OfType<Border>().All(b =>
                b.CornerRadius == border.CornerRadius && b.BorderBrush.IsFrozen) &&
            rims.Children.OfType<Border>().Count(b => b.Effect is System.Windows.Media.Effects.BlurEffect { Radius: >= 10 }) == 1 &&
            border.Effect is null,
            "True blurred glow is confined to an empty outline and preserves sharp panel content");
        var trails = rims.Children.OfType<Rectangle>().ToArray();
        Check(trails.Length == 2 && trails.All(r => !r.HasAnimatedProperties && r.Visibility == Visibility.Hidden),
            "Motion-off frame keeps static glow with no energy clocks");
        FrameGlow.SetEnabled(border, true);
        Check(ReferenceEquals(adorner, decorator.AdornerLayer.GetAdorners(border)?.Single()),
            "Changing motion reuses the same static halo");
        Check(trails.All(r => r.HasAnimatedProperties == (border.IsVisible && SystemParameters.ClientAreaAnimation)) &&
            FrameGlow.GetMotionActive(border) == (border.IsVisible && SystemParameters.ClientAreaAnimation),
            "Energy trails require both visible content and Windows animation permission");
        FrameGlow.SetEnabled(border, false);
        Check(!FrameGlow.GetMotionActive(border) && trails.All(r => !r.HasAnimatedProperties && r.Visibility == Visibility.Hidden) &&
            decorator.AdornerLayer.GetAdorners(border)?.Length == 1,
            "Disabling motion removes every trail clock but retains the neon frame");
        border.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Check(decorator.AdornerLayer.GetAdorners(border) is not { Length: > 0 },
            "Unloading removes glow adorners and event subscriptions");
        border.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Check(decorator.AdornerLayer.GetAdorners(border)?.Length == 1,
            "Reloading restores exactly one static frame");
        FrameGlow.SetVisible(border, false);
        Check(decorator.AdornerLayer.GetAdorners(border) is not { Length: > 0 },
            "Turning off frame visibility releases the remaining static adorner");

        int LitTicks(double level)
        {
            var gauge = new NeonGauge { Width = 120, Height = 120, Level = level, Accent = Brushes.Lime };
            gauge.Measure(new Size(120, 120));
            gauge.Arrange(new Rect(0, 0, 120, 120));
            gauge.UpdateLayout();
            return VisualTreeHelper.GetDrawing(gauge)!.Children.Count - 2;
        }
        Check(LitTicks(double.NaN) == 40 && LitTicks(double.PositiveInfinity) == 40 && LitTicks(0) == 40,
            "Unknown, non-finite and zero readings never acquire decorative lit ticks");
        Check(LitTicks(50) == 80 && LitTicks(100) == 120,
            "Only measured lit ticks gain two lightweight glow strokes");
        return count;
    }

    public static int LiveUi(string? screenshot)
    {
        var metrics = new DashboardMetrics();
        var first = metrics.Read();
        if (first.Cores.Count == 0 || first.Cores.Any(c => c is not null)) throw new InvalidOperationException("Core baseline must be unavailable");
        Thread.Sleep(1100);
        var second = metrics.Read();
        if (second.Cores.Any(c => c is < 0 or > 100) || !second.Cores.Any(c => c is not null) ||
            second.CommitLimitGb <= 0 || second.CommitGb < 0 || second.Processes.Count < 1 ||
            !second.Processes.Any(p => p.CpuPercent is >= 0 and <= 100))
            throw new InvalidOperationException("Live detailed CPU/memory/process counters invalid");
        var events = EventMonitor.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (events.Events.Count > 400) throw new InvalidOperationException("Event snapshot exceeds limit");
        var main = new MainWindow();
        main.Show();
        Pump();
        try
        {
            var model = (CockpitModel)main.DataContext;
            var native = new NativeMetrics();
            model.ApplyDashboard(second, native.Read());
            model.ApplyEvents(events);
            main.UpdateLayout();
            if (((ItemsControl)main.FindName("CockpitCoreOverview")).Items.Count != second.Cores.Count ||
                ((DataGrid)main.FindName("DashboardProcesses")).Items.Count != second.Processes.Count ||
                ((DataGrid)main.FindName("DashboardEvents")).Items.Count != events.Events.Count)
                throw new InvalidOperationException("Dashboard actual UI binding missing live data");
            var processGrid = (DataGrid)main.FindName("DashboardProcesses");
            processGrid.BringIntoView();
            Pump();
            main.UpdateLayout();
            var eventGrid = (DataGrid)main.FindName("DashboardEvents");
            eventGrid.BringIntoView();
            if (events.Events.Count > 0)
            {
                eventGrid.SelectedIndex = 0;
                Pump();
                main.UpdateLayout();
                if (model.SelectedEvent is null || ((TextBox)main.FindName("DashboardEventDetail")).Text != model.SelectedEvent.Message)
                    throw new InvalidOperationException("Visible event selection does not show its message details");
            }
            var pages = (TabControl)main.FindName("Pages");
            foreach (TabItem page in pages.Items)
            {
                pages.SelectedItem = page;
                Pump();
                main.UpdateLayout();
            }
            pages.SelectedIndex = 0;
            Pump();
            var center = new ControlCenterWindow(model) { Owner = main };
            center.Show();
            try
            {
                var tabs = (TabControl)center.FindName("Modules");
                tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => (string)t.Header == "SMART");
                var dropdown = (ComboBox)center.FindName("SmartDevices");
                dropdown.ItemsSource = new[] { "Fixture Autostart / A", "Fixture Autostart / B" };
                dropdown.SelectedIndex = 0;
                dropdown.IsDropDownOpen = true;
                Pump();
                center.UpdateLayout();
                var popup = (Popup)dropdown.Template.FindName("PART_Popup", dropdown);
                var border = popup.Child as Border ?? throw new InvalidOperationException("Dropdown popup dark frame missing");
                var item = (ComboBoxItem)dropdown.ItemContainerGenerator.ContainerFromIndex(0);
                if (border.Background is not SolidColorBrush background || background.Color != Color.FromRgb(21, 16, 45) ||
                    dropdown.Foreground is not SolidColorBrush foreground || foreground.Color.G < 200 ||
                    item.Foreground is not SolidColorBrush itemForeground || itemForeground.Color.G < 200)
                    throw new InvalidOperationException("Autostart dropdown still has unreadable theme");
                dropdown.IsDropDownOpen = false;
                dropdown.SelectedIndex = 1;
                if ((string)dropdown.SelectedItem != "Fixture Autostart / B") throw new InvalidOperationException("Dropdown selection broken");
            }
            finally { center.Close(); Pump(); }
            if (screenshot is not null)
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(20); }
                main.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(main);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(screenshot);
                png.Save(stream);
            }
            Console.WriteLine($"DASHBOARD: {second.Cores.Count} logical CPU graphs, {second.Processes.Count} processes, " +
                $"{events.Events.Count} bounded events, {events.DiskActivity?.Count ?? 0} disk counters; live WPF bindings and dark Autostart popup verified");
            return 8;
        }
        finally
        {
            main.Close();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (main.IsVisible && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(20); }
            if (main.IsVisible) throw new InvalidOperationException("Dashboard polling did not shut down within 10 seconds");
        }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
