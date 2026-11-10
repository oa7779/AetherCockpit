using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class InterfaceTests
{
    internal static int Run(string? screenshotPrefix)
    {
        var count = 0;
        void Check(bool valid, string message) { count++; if (!valid) throw new InvalidOperationException(message); }
        var morning = new DateTimeOffset(2026, 11, 7, 7, 0, 0, TimeSpan.FromHours(1));
        Check(!DisplayTime.IsDay("auto", morning.AddTicks(-1)) && DisplayTime.IsDay("auto", morning),
            "Automatic daytime starts at precisely 07:00 local time");
        Check(DisplayTime.IsDay("auto", morning.AddHours(12).AddTicks(-1)) &&
            !DisplayTime.IsDay("auto", morning.AddHours(12)), "Night starts at precisely 19:00 local time");
        Check(DisplayTime.IsDay("day", morning.AddHours(18)) && !DisplayTime.IsDay("night", morning),
            "Manual day/night overrides local time without location or internet access");
        try { (new CockpitSettings { DisplayMode = "invalid" }).Validate(); throw new InvalidOperationException("Invalid mode accepted"); }
        catch (ArgumentException) { count++; }
        var model = new CockpitModel();
        var changes = 0;
        using var pulse = new DisplayPulse(Dispatcher.CurrentDispatcher, model, _ => changes++);
        pulse.Refresh(morning);
        pulse.Refresh(morning.AddMilliseconds(500));
        Check(model.SystemClock == "07:00:00" && changes == 1, "Subsecond pulse does not rebuild appearance");
        pulse.Refresh(morning.AddSeconds(1));
        Check(model.SystemClock == "07:00:01" && changes == 1, "Clock advances without any hardware snapshot");
        pulse.Refresh(morning.AddHours(12));
        Check(!model.IsDaytime && changes == 2, "Live automatic boundary switches appearance once");
        model.Settings = model.Settings with { DisplayMode = "day" };
        pulse.Refresh(morning.AddHours(12));
        Check(model.IsDaytime && changes == 3, "Manual override switches immediately on next pulse");
        model.UpdateDisplayTime(morning);
        var sample = new LiveSample(morning.AddHours(-1), 10, 20, 1, 8, 0, 0, [], []);
        var snapshot = new DashboardSnapshot(sample.Timestamp, [], [], [], 0, 8, 0, 0, 0, TimeSpan.FromHours(2), []);
        model.ApplyDashboard(snapshot, sample);
        Check(model.SystemClock == "07:00:00", "Delayed hardware snapshot never rewinds the clock");
        var graphChanges = 0;
        model.DetailGraphs.CollectionChanged += (_, _) => graphChanges++;
        model.AdapterGraphs.CollectionChanged += (_, _) => graphChanges++;
        model.ApplyDashboard(snapshot, sample);
        Check(graphChanges == 0, "Unchanged graph membership raises no collection reset/recreation");
        var savedModes = new List<string>();
        var settingsWindow = new DesktopSettingsWindow(model, settings =>
        {
            settings.Validate();
            model.Settings = settings;
            savedModes.Add(settings.DisplayMode);
        }, () => throw new InvalidOperationException("Display selection must not open the network monitor"));
        try
        {
            var settingsPanel = (StackPanel)((ScrollViewer)settingsWindow.Content).Content;
            var modeControl = settingsPanel.Children.OfType<ComboBox>().Single(control => control.Name == "DisplayModeSelector");
            Check(savedModes.Count == 0 && Equals(modeControl.SelectedValue, "day"),
                "Opening display settings reflects the saved mode without writing any settings");
            foreach (var value in new[] { "night", "auto", "day" }) modeControl.SelectedValue = value;
            Check(savedModes.SequenceEqual(new[] { "night", "auto", "day" }),
                "All display choices invoke the existing save callback with validated settings");
        }
        finally { settingsWindow.Close(); }

        var main = new MainWindow();
        var content = (FrameworkElement)main.Content;
        main.Content = null;
        var host = new Window { Content = content, DataContext = main.Model, Width = 1440, Height = 940,
            Background = main.Background, ShowInTaskbar = false };
        host.Resources.MergedDictionaries.Add(main.Resources);
        var board = (HardwareBoardView)main.FindName("HardwareBoard");
        board.Apply(HardwareBoardTests.IllustrationExample);
        try
        {
            host.Show(); Pump();
            var shellFrame = (Border)main.FindName("ShellNeonFrame");
            Check(AetherNeonFrame.GetEnabled(shellFrame) && !FrameGlow.GetVisible(shellFrame) &&
                System.Windows.Documents.AdornerLayer.GetAdornerLayer(shellFrame)?.GetAdorners(shellFrame)?
                    .OfType<AetherNeonFrame.NeonAdorner>().Count() == 1,
                "Cockpit shell uses exactly one new frame and no legacy moving rim");
            main.Model.MotionEnabled = false; Pump();
            Check(!AetherNeonFrame.GetMotionActive(shellFrame) && AetherNeonFrame.GetEnabled(shellFrame),
                "Cockpit motion switch retains static neon contour");
            main.Model.MotionEnabled = true; Pump();
            Check(AetherNeonFrame.GetMotionActive(shellFrame) == SystemParameters.ClientAreaAnimation,
                "Cockpit breathing follows existing application and Windows gates");
            for (var index = 0; index < 30; index++)
                main.Model.ApplyEvents(new(DateTimeOffset.Now, [], [],
                    [new("0 / SYNTHETISCH C:", index % 8 * 12, index % 5 * 9, index % 10 * 10, index % 4),
                     new("1 / SYNTHETISCH D:", index % 6 * 7, index % 9 * 5, null, index % 3)]));
            var disks = (ItemsControl)main.FindName("CockpitDiskOverview");
            disks.BringIntoView(); Pump();
            Check(disks.Items.Count == 2 &&
                ReferenceEquals(disks.ItemsSource, main.Model.DiskGraphs) &&
                main.Model.DiskGraphs.All(disk => disk.Metrics.Length == 4),
                "Cockpit binds one four-series card per physical disk");
            Check(main.Model.DiskGraphs[1].Metrics[2].Value == "--",
                "Grouped disk UI never substitutes unavailable activity");
            var diskCard = (FrameworkElement)disks.ItemContainerGenerator.ContainerFromIndex(0);
            Check(diskCard.ActualWidth >= 280 && diskCard.ActualHeight >= 180,
                "Grouped disk card reserves readable space for all four labeled histories");
            if (screenshotPrefix is not null) ModuleTests.SaveScreenshot(host, screenshotPrefix + "-disks.png");
            ((ScrollViewer)main.FindName("CockpitScroll")).ScrollToTop(); Pump();
            Check(main.FindName("CockpitOverview") is null && main.FindName("CockpitDetails") is null,
                "Cockpit has no replacement summary or enclosing detail expander");
            Check(((DataGrid)main.FindName("DashboardProcesses")).IsVisible &&
                ((ItemsControl)main.FindName("CockpitCoreOverview")).IsVisible &&
                ((DataGrid)main.FindName("DashboardEvents")).IsVisible &&
                main.FindName("SensorToggle") is CheckBox && main.FindName("HistoryToggle") is CheckBox,
                "Original process, CPU-core, event and data-control views are directly reachable");
            var cockpitScroll = (ScrollViewer)main.FindName("CockpitScroll");
            host.Width = 1060; host.Height = 680; Pump();
            Check(cockpitScroll.ViewportWidth > 0 && cockpitScroll.ExtentWidth <= cockpitScroll.ViewportWidth + 1,
                "Original cockpit fits the minimum supported window width without horizontal overflow");
            host.Width = 1440; host.Height = 940; Pump();
            var groups = (StackPanel)main.FindName("NavigationGroups");
            Check(groups.Children.OfType<Expander>().Select(g => (string)g.Header)
                .SequenceEqual(new[] { "Übersicht", "Hardware", "Diagnose", "Wartung", "Verwaltung", "Hilfe" }),
                "Navigation presents all six task groups");
            var pages = (TabControl)main.FindName("Pages");
            var routes = groups.Children.OfType<Expander>().SelectMany(g =>
                ((StackPanel)g.Content).Children.OfType<Button>()).ToArray();
            Check(routes.Length == 24 && routes.Select(b => (string)b.Tag).Distinct().Count() == 24,
                "Every main/secondary module is reachable exactly once in task navigation");
            var external = new HashSet<string> { "SMART", "Logs / Gaming", "Netzwerk", "Wartung", "Pakete",
                "Autostart", "Auftraege", "Spuren", "Schluessel", "Einstellungen" };
            var center = new ControlCenterWindow(main.Model);
            try
            {
                var modules = (TabControl)center.FindName("Modules");
                Check(routes.Where(b => external.Contains((string)b.Tag)).All(b =>
                    modules.Items.Cast<TabItem>().Any(t => Equals(t.Tag, b.Tag))),
                    "Secondary routes reference real modules without executing commands");
            }
            finally { center.Close(); }
            foreach (var button in routes.Where(b => !external.Contains((string)b.Tag)))
            {
                if (Equals(button.Tag, "Ereignisse") || Equals(button.Tag, "Superadmin") || Equals(button.Tag, "Operations"))
                {
                    Check(pages.Items.Cast<TabItem>().Any(t => Equals(t.Header, button.Tag)), "Live module route remains available");
                    continue;
                }
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Check(pages.SelectedItem is TabItem t && Equals(t.Tag, button.Tag) &&
                    ((Expander)((StackPanel)button.Parent).Parent).IsExpanded && button.BorderThickness.Left == 2,
                    "Actual navigation opens destination and marks active group: " + button.Tag);
            }
            pages.SelectedItem = pages.Items.Cast<TabItem>().Single(t => Equals(t.Header, "Mainboard")); Pump();
            Check(((FrameworkElement)board.FindName("DeviceDetails")).ActualWidth >= 260 &&
                ((FrameworkElement)board.FindName("DeviceList")).ActualHeight > 80 &&
                ((FrameworkElement)board.FindName("BoardScroller")).ActualWidth > 550,
                "Mainframe separates a usable board viewport from bounded device details");
            var zoom = (ComboBox)board.FindName("BoardZoom");
            zoom.SelectedIndex = 2; Pump();
            Check(((Viewbox)board.FindName("BoardScale")).ActualWidth >= 1500 &&
                ((ScrollViewer)board.FindName("BoardScroller")).HorizontalScrollBarVisibility == ScrollBarVisibility.Auto,
                "100 percent board zoom makes labels readable without expanding device details");
            zoom.SelectedIndex = 0; Pump();
            Check(((ScrollViewer)board.FindName("BoardScroller")).HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled,
                "Fit mode restores bounded overview without horizontal nesting");
            var canvas = (Canvas)board.FindName("BoardCanvas");
            var halo = canvas.Children.OfType<System.Windows.Shapes.Polyline>().Count(p =>
                p.Tag is string tag && tag.StartsWith("neon-halo:", StringComparison.Ordinal));
            var flows = canvas.Children.OfType<System.Windows.Shapes.Polyline>().Count(p => p.StrokeDashArray.Count > 0);
            Check(halo > 20 && flows < halo, "Brighter static vector halos use fewer animated paths than halo strokes");
            var starts = board.AnimationStarts;
            board.UpdateComponentReadings(null); Pump();
            Check(board.AnimationStarts == starts, "Readout refresh does not recreate animation clocks");
            main.Model.Settings = main.Model.Settings with { DisplayMode = "day" };
            using var clock = new DisplayPulse(host.Dispatcher, main.Model, day =>
            {
                DisplayTime.Apply(main, day);
                DisplayTime.Apply(host, day);
            });
            clock.Refresh(morning); Pump();
            if (screenshotPrefix is not null) ModuleTests.SaveScreenshot(host, screenshotPrefix + "-mainframe-day.png");
            Check(main.Model.IsDaytime && host.Resources.Contains("SceneBackdrop"), "Day palette applied to shell and board");
            main.Model.Settings = main.Model.Settings with { DisplayMode = "night" };
            clock.Refresh(morning); Pump();
            if (screenshotPrefix is not null) ModuleTests.SaveScreenshot(host, screenshotPrefix + "-mainframe-night.png");
            Check(!main.Model.IsDaytime && new[] { "WindowBackdrop", "GlossPanel", "ChromeSidebar", "SceneBackdrop" }
                .All(key => host.Resources[key] is SolidColorBrush brush && brush.Color == Colors.Black && brush.IsFrozen),
                "Night uses frozen pure-black shell backgrounds instead of the blue/purple skin");
            var fans = ((Canvas)board.FindName("BoardCanvas")).Children.OfType<Canvas>()
                .Where(p => Equals(p.Tag, "gpu-fan-plane")).ToArray();
            Check(fans.Length == 3 && fans.All(p => p.RenderTransform is MatrixTransform m &&
                m.Matrix.M21 == 0 && m.Matrix.M22 < 0 && m.Matrix.M11 > 0 &&
                p.Children.OfType<System.Windows.Shapes.Ellipse>().Count(h => Equals(h.Tag, "fan-hub")) == 1),
                "Three GPU rotors and hubs share the vertical cooler face instead of the top/PCB plane");
            Check(fans.All(p => ((Canvas)p.Children[0]).Children.OfType<System.Windows.Shapes.Path>()
                .Count(b => b.Data.IsFrozen) == 7), "GPU blades are seven frozen curved geometries per rotor");
            var source = (Canvas)board.FindName("BoardCanvas");
            source.Children.OfType<Button>().Single(b => Equals(b.Tag, "device:audio-routed"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            main.Model.IsDaytime = true; Pump();
            Check(((ListBox)board.FindName("DeviceList")).Items.Count == 1 &&
                ((BoardDevice)((ListBox)board.FindName("DeviceList")).Items[0]).Id == "audio-routed",
                "Appearance changes preserve the exact selected peripheral, not only its group");
            pages.SelectedItem = pages.Items.Cast<TabItem>().Single(t => Equals(t.Header, "Cockpit")); Pump();
            main.Model.IsDaytime = false;
            pages.SelectedItem = pages.Items.Cast<TabItem>().Single(t => Equals(t.Header, "Mainboard")); Pump();
            var nightSky = (LinearGradientBrush)((Canvas)board.FindName("BoardCanvas"))
                .Children.OfType<System.Windows.Shapes.Rectangle>().Single(r => Equals(r.Tag, "synthwave-sky")).Fill;
            Check(nightSky.GradientStops.All(stop =>
                    (Color)stop.GetAnimationBaseValue(GradientStop.ColorProperty) == Colors.Black && stop.Color == Colors.Black),
                "Returning to a hidden board catches up with the current day/night mode");
            Check(((Canvas)((Canvas)board.FindName("BoardCanvas")).Children.OfType<Canvas>()
                .Single(c => Equals(c.Tag, "synthwave-ground"))).Background is SolidColorBrush groundBrush &&
                groundBrush.Color == Colors.Black, "Night ground remains black behind the neon grid");
            var seen = new List<(string Text, double Time)>();
            var elapsed = Stopwatch.StartNew();
            void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(CockpitModel.SystemClock)) seen.Add((main.Model.SystemClock, elapsed.Elapsed.TotalSeconds));
            }
            main.Model.PropertyChanged += Changed;
            try
            {
                clock.Start();
                var wait = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                timer.Tick += (_, _) => { timer.Stop(); wait.Continue = false; };
                timer.Start(); Dispatcher.PushFrame(wait);
                clock.Stop();
            }
            finally { main.Model.PropertyChanged -= Changed; }
            var intervals = seen.Zip(seen.Skip(1), (a, b) => b.Time - a.Time).ToArray();
            Check(seen.Count >= 6 && intervals.Skip(1).All(i => i is >= .5 and <= 1.5),
                "Clock updates each second under synthetic board animation, maximum interval 1.5s");
            Console.WriteLine($"CLOCK: {seen.Count} changes/6s; max interval {intervals.DefaultIfEmpty(0).Max():F3}s; no sensor reads");
            host.Width = 1100; host.Height = 760; Pump();
            var smallViewport = (ScrollViewer)board.FindName("BoardScroller");
            Check(smallViewport.ActualWidth > 350 && smallViewport.ViewportHeight > 200 &&
                ((FrameworkElement)board.FindName("DeviceDetails")).ActualWidth >= 260,
                "Minimum desktop size preserves bounded board scrolling and separate readable details");
        }
        finally { board.Shutdown(); host.Close(); main.Close(); Pump(); }
        var saver = new AetherScreensaver.SaverWindow(new System.Drawing.Rectangle(0, 0, 800, 600));
        try
        {
            var frame = new BoardFeedFrame(DateTimeOffset.UtcNow, HardwareBoardTests.Example, "--", "--", "--", "--", null, "--", false,
                DisplayMode: "day");
            saver.Present(frame.Hardware, frame, "SYNTHETIC");
            var background = (LinearGradientBrush)saver.Background;
            Check(background.GradientStops[0].Color == Color.FromRgb(27, 48, 81),
                "Saver accepts manual day mode from a fresh local frame without starting animation");
            saver.Present(frame.Hardware, frame with { DisplayMode = "night" }, "SYNTHETIC");
            Check(background.GradientStops.All(stop => stop.Color == Colors.Black),
                "Saver accepts manual night mode from the same hardware snapshot");
            var decorator = (System.Windows.Documents.AdornerDecorator)saver.Content;
            var panel = (Grid)decorator.Child;
            Check(Grid.GetRow(panel.Children.OfType<HardwareBoardView>().Single()) == 1 &&
                Grid.GetRow(panel.Children.OfType<StackPanel>().Single()) == 0,
                "Saver reserves a separate header row so status, clock and board cannot overlap");
        }
        finally { saver.Close(); }
        return count;
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

}
