using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SystemCockpit;
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
        Check(model.EventCounts.Single(c => c.Name == "FEHLER").FreePercent == 50 && model.DiskGraphs.Count == 4,
            "Snapshot proportions and disk throughput/activity/queue graphs");
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
