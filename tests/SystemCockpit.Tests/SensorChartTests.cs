using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class SensorChartTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);
    internal static List<SensorReading> Readings(double? temperature = 40) =>
    [
        new("/intelcpu/0/temperature/0", "Retro CPU", "CPU Package", "Temperature", temperature, "C"),
        new("/intelcpu/0/temperature/1", "Retro CPU", "Core 1", "Temperature", 38, "C"),
        new("/intelcpu/0/load/0", "Retro CPU", "CPU Total", "Load", 25, "%"),
        new("/intelcpu/0/voltage/0", "Retro CPU", "Core Voltage", "Voltage", 1.2, "V"),
        new("/board/0/fan/0", "Retro Mainboard", "CPU Fan", "Fan", 820, "RPM"),
        new("/nvme/0/temperature/0", "Same SSD", "Composite Temperature", "Temperature", 32, "C"),
        new("/nvme/1/temperature/0", "Same SSD", "Composite Temperature", "Temperature", 33, "C"),
        new("/nvme/0/temperature/1", "Same SSD", "Warning Temperature", "Temperature", 99, "C"),
        new("/nvme/0/temperature/2", "Same SSD", "Critical Temperature", "TemperatureLimit", 109, "C")
    ];

    internal static int Run()
    {
        var assertions = 0;
        void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
        var history = new SensorChartHistory();
        var duplicate = new SensorReading("/gpu-nvidia/0/load/3", "RTX", "GPU Video Engine", "Load", 12, "%");
        var otherChannel = duplicate with { Name = "GPU Bus", Value = 27 };
        var collisions = new SensorChartHistory();
        collisions.Add(Start, [duplicate, duplicate, otherChannel]);
        Check(collisions.Groups.Single().Series.Count == 2 &&
            collisions.Groups.Single().Series.Single(s => s.Name == duplicate.Name).Points.Single().Value == 12 &&
            collisions.Warning.Length > 0,
            "Reported NVIDIA duplicate ID does not crash: identical readings coalesce, different channel names remain separate");
        collisions.Groups.Single().Series.Single(s => s.Name == duplicate.Name).IsSelected = false;
        collisions.Add(Start.AddSeconds(2), [otherChannel with { Value = 30 }, duplicate with { Value = 13 }]);
        Check(collisions.Groups.Single().Series.Count == 2 &&
            !collisions.Groups.Single().Series.Single(s => s.Name == duplicate.Name).IsSelected &&
            collisions.Groups.Single().Series.Single(s => s.Name == otherChannel.Name).Points[^1].Value == 30,
            "Reordered duplicate IDs preserve each channel history and explicit selection");
        collisions.Add(Start.AddSeconds(4), [duplicate, duplicate with { Value = 99 }, otherChannel]);
        Check(collisions.Groups.Single().Series.Single(s => s.Name == duplicate.Name).Points[^1].Value is null &&
            collisions.Groups.Single().Series.Single(s => s.Name == otherChannel.Name).Points[^1].Value == 27 &&
            collisions.Warning.Contains("widerspruechliche"),
            "Conflicting values of exactly the same channel produce an explicit unavailable gap, never an arbitrary first value");
        collisions.Add(Start.AddSeconds(6), [otherChannel]);
        Check(collisions.Groups.Single().Series.Single(s => s.Name == duplicate.Name).Points.Count == 4 &&
            collisions.Groups.Single().Series.Single(s => s.Name == duplicate.Name).Points[^1].Value is null,
            "Disappearing sibling of a duplicate ID retains its history with an unavailable gap");
        var startupModel = new CockpitModel { RecordStatistics = false };
        startupModel.Apply(new LiveSample(Start, 10, 20, 2, 10, 0, 0, [], [duplicate, duplicate, otherChannel]), true);
        Check(startupModel.SensorCharts.Groups.Single().Series.Count == 2 &&
            startupModel.SensorStatus.Contains("mehrfach") && startupModel.Sensors.Count == 3,
            "Actual startup model Apply accepts duplicate NVIDIA ID, exposes warning and retains raw detail matrix");
        history.Add(Start, Readings());
        Check(history.Groups.Count == 6 && history.Groups.Sum(g => g.Series.Count) == 7,
            "Sensor curves group actual hardware identities and units, not static temperature limits");
        var cpu = history.Groups.Single(g => g.Hardware == "Retro CPU" && g.Unit == "C");
        Check(cpu.Series.Count == 2 && history.Groups.Count(g => g.Hardware == "Same SSD") == 2,
            "CPU temperature channels share a plot; identical SSD model names never merge physical identities");
        Check(cpu.Series.All(s => s.IsSelected && s.MatchesFilter) && history.EmptyText.Length == 0,
            "All measured channels initially available with explicit checkable selection");
        var package = cpu.Series.Single(s => s.Name == "CPU Package");
        package.IsSelected = false;
        history.Add(Start.AddSeconds(2), Readings(null));
        Check(!package.IsSelected && package.Current == "Nicht verfuegbar" && package.Points[^1].Value is null,
            "Sensor selection survives updates and unavailable reading is null, never zero");
        history.Add(Start.AddSeconds(9), Readings(46));
        var plot = new Rect(0, 0, 90, 100);
        var segments = SensorPlot.Segments(package.Points, Start, Start.AddSeconds(9), 0, 100, plot);
        Check(segments.Count == 2 && segments.All(s => s.Count == 1) &&
            segments[0][0].X == 0 && segments[1][0].X == 90 && segments[1][0].Y == 54,
            "Missing points create disconnected segments on genuine elapsed-time X axis");
        var irregular = SensorPlot.Segments([new(Start, -10), new(Start.AddSeconds(2), 0), new(Start.AddSeconds(9), 10)],
            Start, Start.AddSeconds(9), -10, 10, plot);
        Check(irregular.Single()[1].X == 20 && irregular.Single()[0].Y == 100 &&
            irregular.Single()[2].Y == 0, "Negative values and irregular intervals retain correct scale and timing");
        history.Filter("Core 1");
        Check(history.View.Cast<SensorChartGroup>().Single() == cpu &&
            cpu.SeriesView.Cast<SensorChartSeries>().Single().Name == "Core 1",
            "Search filters both plotted channels and their hardware groups");
        history.Filter("not-found");
        Check(history.View.IsEmpty && history.EmptyText.Length > 0, "No-match filter displays explicit empty state");
        history.Filter("");
        Check(!package.IsSelected && history.View.Cast<SensorChartGroup>().Count() == 6,
            "Clearing filter restores groups without changing explicit sensor selection");
        history.Add(Start.AddSeconds(11), []);
        Check(history.Groups.SelectMany(g => g.Series).All(s => s.Points[^1].Value is null),
            "Disappearing sensors receive a gap, not a held-forward last reading");
        for (var index = 0; index < 200; index++)
            history.Add(Start.AddSeconds(13 + index * 2), Readings(index == 1 ? double.NaN : 40));
        Check(history.Groups.SelectMany(g => g.Series).All(s => s.Points.Count <= SensorChartHistory.Capacity) &&
            package.Points.Count == 180 && cpu.Start == Start.AddSeconds(53),
            "History has exact bounded 180-cycle retention shared by all unit panels");
        var old = history.Groups.SelectMany(g => g.Series).Count();
        for (var index = 0; index < SensorChartHistory.Capacity; index++)
            history.Add(Start.AddSeconds(500 + index * 2), []);
        Check(old == 7 && history.Groups.Count == 0 && history.EmptyText.Length > 0,
            "Channels absent for the entire history window are evicted with empty groups");
        history.Add(Start.AddSeconds(900), Readings(double.PositiveInfinity));
        Check(history.Groups.SelectMany(g => g.Series).Single(s => s.Name == "CPU Package").Points[0].Value is null,
            "Nonfinite input never becomes drawable or a zero fallback");
        history.Add(Start.AddSeconds(899), Readings());
        Check(history.Groups.SelectMany(g => g.Series).All(s => s.Points.Count == 1),
            "Clock rollback explicitly restarts timestamp axis instead of drawing backwards");
        var changed = Readings();
        changed[0] = changed[0] with { Unit = "F", Name = "Renamed" };
        history.Add(Start.AddSeconds(901), changed);
        Check(history.Groups.Single(g => g.Unit == "F").Series.Single().Name == "Renamed" &&
            history.Groups.SelectMany(g => g.Series).Count(s => s.Identity == changed[0].Id) == 1,
            "Changed sensor metadata migrates once without mixing units or duplicate identities");
        var unitless = new SensorChartHistory();
        unitless.Add(Start, [
            new("/board/0/factor/0", "Board", "Throttle", "Factor", 1, ""),
            new("/board/0/count/0", "Board", "Count", "Count", 100, "")]);
        Check(unitless.Groups.Count == 2, "Unspecified units never mix unrelated sensor dimensions on the same scale");
        var limitsOnly = Readings().Select(s => s.Id == "/intelcpu/0/temperature/0"
            ? s with { Type = "TemperatureLimit" } : s).ToArray();
        var reclassified = new SensorChartHistory();
        reclassified.Add(Start, Readings());
        reclassified.Add(Start.AddSeconds(2), limitsOnly);
        Check(!reclassified.Groups.SelectMany(g => g.Series).Any(s => s.Identity == "/intelcpu/0/temperature/0"),
            "Reclassified limit immediately disappears from curves including its old history");
        var model = new CockpitModel { RecordStatistics = false };
        var sample = new LiveSample(DateTimeOffset.Now.AddSeconds(-10), 10, 20, 2, 10, 0, 0, [], Readings());
        model.Apply(sample, true);
        Check(model.SensorCharts.Groups.Count == 6 && model.Sensors.Count == 9,
            "Real CockpitModel sampling updates chart history and retains limits in detail matrix");
        model.Apply(sample with { Timestamp = sample.Timestamp.AddSeconds(2), SensorError = "test" }, true);
        Check(model.SensorCharts.Groups.SelectMany(g => g.Series).All(s => s.Points[^1].Value is null),
            "Provider error invalidates all plotted values even if a stale sensor list was returned");
        model.Apply(sample with { Timestamp = sample.Timestamp.AddSeconds(4) }, false);
        Check(model.SensorCharts.Groups.SelectMany(g => g.Series).All(s => s.Points[^1].Value is null),
            "Disabled hardware access cannot plot stale sample readings as current");
        model.MeasurementFailed("synthetic failure");
        Check(model.SensorCharts.Groups.SelectMany(g => g.Series).All(s => s.Current == "Nicht verfuegbar"),
            "Global measurement failure preserves history but visibly invalidates current chart readings");
        return assertions;
    }

    internal static int Ui(string? screenshot)
    {
        var assertions = 0;
        void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
        var model = new CockpitModel { RecordStatistics = false };
        for (var index = 0; index < 45; index++)
            model.Apply(new(Start.AddSeconds(index * 2), 10, 20, 2, 10, 0, 0, [],
                Readings(index is >= 18 and <= 22 ? null : 44 + 8 * Math.Sin(index * .21))), true);
        var view = new SensorChartsView { DataContext = model };
        var window = new Window { Width = 1100, Height = 920, Content = view, Background = Brushes.Black, ShowInTaskbar = false };
        // The parent page hides its own navigation headers; the inner chart tabs must override that style.
        window.Resources.Add(typeof(TabItem), new Style(typeof(TabItem)) { Setters = { new Setter(UIElement.VisibilityProperty, Visibility.Collapsed) } });
        try
        {
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var modes = (TabControl)view.FindName("SensorModes");
            Check(modes.SelectedIndex == 0 && modes.Items.Cast<TabItem>().All(tab => tab.IsVisible),
                "Chart is default view and both mode tabs remain visible inside existing hidden-tab navigation");
            var items = (ItemsControl)view.FindName("ChartGroups");
            Check(items.Items.Count == 6, "All hardware/unit chart panels render");
            var cpu = model.SensorCharts.Groups.Single(g => g.Hardware == "Retro CPU" && g.Unit == "C");
            var checks = Descendants(view).OfType<CheckBox>().Where(c => c.DataContext is SensorChartSeries).ToArray();
            Check(checks.Length == 7, "Every measured sensor has its own checkable legend, no static limit checkbox");
            var check = checks.Single(c => c.DataContext is SensorChartSeries { Name: "CPU Package" });
            check.IsChecked = false;
            Check(!cpu.Series.Single(s => s.Name == "CPU Package").IsSelected, "Legend checkbox really changes plotted sensor selection");
            check.IsChecked = true;
            if (screenshot is not null) ModuleTests.SaveScreenshot(view, screenshot);
            modes.SelectedIndex = 1;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var table = (DataGrid)view.FindName("SensorTable");
            Check(table.IsVisible && table.Items.Count == 9 && table.IsReadOnly,
                "Detail matrix toggle retains all sensors, static limits, and read-only behavior");
            model.SensorFilter = "Critical Temperature";
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(table.Items.Count == 1 && model.SensorCharts.View.IsEmpty,
                "Same filter finds a threshold in table without ever creating a threshold curve");
            model.SensorFilter = "CPU Package";
            modes.SelectedIndex = 0;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(items.Items.Count == 1 && Descendants(view).OfType<CheckBox>().Count() == 1,
                "Filter immediately updates visible plot groups and sensor legend together");
            window.Hide();
            model.Apply(new(Start.AddSeconds(100), 0, 20, 2, 10, 0, 0, [], Readings(52)), true);
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(cpu.Series.Single(s => s.Name == "CPU Package").Current.StartsWith("52"),
                "Hidden view retains fresh data and reconnects rendering on return");
        }
        finally { window.Close(); }
        return assertions;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
