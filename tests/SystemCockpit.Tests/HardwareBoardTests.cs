using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Shapes;
using System.Windows.Media;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class HardwareBoardTests
{
    internal static int GpuPresentation(string? screenshotPrefix)
    {
        var assertions = 0;
        void Check(bool condition, string message) { assertions++; if (!condition) throw new InvalidOperationException(message); }
        var model = new CockpitModel { MotionEnabled = false };
        var view = new HardwareBoardView { DataContext = model };
        view.Apply(IllustrationExample);
        var window = new Window { Content = view, Width = 1600, Height = 900,
            Background = Brushes.Black, ShowInTaskbar = false };
        try
        {
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            foreach (var day in new[] { false, true })
            {
                model.IsDaytime = day;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var canvas = (Canvas)view.FindName("BoardCanvas");
                var elements = canvas.Children.Cast<UIElement>().ToArray();
                var brackets = elements.OfType<Polygon>().Where(p => Equals(p.Tag, "gpu-bracket")).ToArray();
                var face = elements.OfType<Polygon>().Single(p => Equals(p.Tag, "gpu-cooler-face"));
                var fans = elements.OfType<Canvas>().Where(p => Equals(p.Tag, "gpu-fan-plane")).ToArray();
                Check(brackets.Length == 12 && fans.Length == 3 &&
                    brackets.All(p => Array.IndexOf(elements, p) < Array.IndexOf(elements, face)),
                    "GPU bracket and ports are behind the cooler face, never painted over its fans");
                var faceGeometry = new StreamGeometry();
                using (var context = faceGeometry.Open())
                {
                    context.BeginFigure(face.Points[0], true, true);
                    context.PolyLineTo(face.Points.Skip(1).ToArray(), true, false);
                }
                var matrices = fans.Select(f => ((MatrixTransform)f.RenderTransform).Matrix).ToArray();
                Check(matrices.All(m => m.M11 > 0 && m.M12 > 0 && m.M21 == 0 && m.M22 < 0 &&
                    Math.Abs(m.M11 - matrices[0].M11) < 0.0001 &&
                    Math.Abs(m.M12 - matrices[0].M12) < 0.0001 &&
                    Math.Abs(m.M22 - matrices[0].M22) < 0.0001),
                    "All three GPU fans use the same projected vertical cooler plane");
                Check(Math.Abs((matrices[1].OffsetX - matrices[0].OffsetX) -
                    (matrices[2].OffsetX - matrices[1].OffsetX)) < 0.0001 &&
                    Math.Abs((matrices[1].OffsetY - matrices[0].OffsetY) -
                    (matrices[2].OffsetY - matrices[1].OffsetY)) < 0.0001,
                    "GPU fan centers are evenly spaced in perspective");
                foreach (var fan in fans)
                {
                    var matrix = ((MatrixTransform)fan.RenderTransform).Matrix;
                    var samples = Enumerable.Range(0, 16).Select(i =>
                        matrix.Transform(new Point(30 * Math.Cos(i * Math.PI / 8),
                            30 * Math.Sin(i * Math.PI / 8)))).Append(matrix.Transform(new Point())).ToArray();
                    Check(samples.All(faceGeometry.FillContains),
                        "Each projected GPU fan fits completely within the cooler face");
                    var laterPolygons = elements.Skip(Array.IndexOf(elements, fan) + 1).OfType<Polygon>();
                    Check(laterPolygons.All(p => samples.All(point => !p.RenderedGeometry.FillContains(point))),
                        "Later opaque component polygons do not cover the GPU fan");
                    Check(fan.Children.OfType<Ellipse>().Count(e => Equals(e.Tag, "fan-hub")) == 1 &&
                        ((Canvas)fan.Children[0]).Children.OfType<System.Windows.Shapes.Path>().Count() == 7,
                        "Each GPU rotor retains seven blades and its hub");
                }
                if (screenshotPrefix is not null)
                    ModuleTests.SaveScreenshot(view, screenshotPrefix + (day ? "-day.png" : "-night.png"));
            }
        }
        finally { view.Shutdown(); window.Close(); }
        return assertions;
    }

    internal static void RenderProfile(string isolate)
    {
        if (isolate is not ("" or "no-rays" or "verify" or "4k"))
            throw new ArgumentException("Render profile mode must be no-rays, verify or 4k.");
        var view = new HardwareBoardView { DataContext = new CockpitModel { MotionEnabled = true } };
        view.UseScreensaverLayout();
        view.Apply(IllustrationExample);
        var window = new Window { Content = view, Width = isolate == "4k" ? 3840 : 1280,
            Height = isolate == "4k" ? 2160 : 720, ShowInTaskbar = false };
        try
        {
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var canvas = (Canvas)view.FindName("BoardCanvas");
            var ground = canvas.Children.OfType<Canvas>().Single(c => Equals(c.Tag, "synthwave-ground"));
            if (isolate == "no-rays") ((Canvas)ground.Children[0]).Visibility = Visibility.Collapsed;
            Console.WriteLine($"RENDER isolate={isolate}; tier={RenderCapability.Tier >> 16}; " +
                $"effects={Descendants(view).OfType<UIElement>().Count(e => e.Effect is not null)}; " +
                $"animated-paths={Descendants(view).OfType<Polyline>().Count(p => p.HasAnimatedProperties)}");
            var intervals = new List<double>();
            TimeSpan? previous = null;
            void Frame(object? sender, EventArgs args)
            {
                var now = ((RenderingEventArgs)args).RenderingTime;
                if (previous is { } last && now > last) intervals.Add((now - last).TotalMilliseconds);
                previous = now;
            }
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            var compositionWait = new DispatcherFrame();
            var compositionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            compositionTimer.Tick += (_, _) => { compositionTimer.Stop(); compositionWait.Continue = false; };
            CompositionTarget.Rendering += Frame;
            try
            {
                compositionTimer.Start();
                Dispatcher.PushFrame(compositionWait);
            }
            finally { compositionTimer.Stop(); CompositionTarget.Rendering -= Frame; }
            intervals.Sort();
            if (intervals.Count == 0) throw new InvalidOperationException("No composition frames observed.");
            Console.WriteLine($"{view.ActualWidth:F0}x{view.ActualHeight:F0} WPF composition: intervals={intervals.Count}; " +
                $"median={intervals[intervals.Count / 2]:F1}ms; p95={intervals[(int)((intervals.Count - 1) * .95)]:F1}ms; " +
                $"CPU={(process.TotalProcessorTime - cpu).TotalMilliseconds:F0}ms/4s. Composition callbacks, not GPU-present FPS.");
            if (isolate == "verify" && intervals[(int)((intervals.Count - 1) * .95)] > 33.4)
                throw new InvalidOperationException("Synthetic preview missed the 33.4ms p95 composition budget.");
            if (isolate.Length > 0) return;
            var durations = new List<double>();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(3840, 2160, 96, 96, PixelFormats.Pbgra32);
            for (var index = 0; index < 7; index++)
            {
                var wait = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (_, _) => { timer.Stop(); wait.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(wait);
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                    context.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, 3840, 2160));
                bitmap.Clear();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                bitmap.Render(visual);
                clock.Stop();
                if (index > 0) durations.Add(clock.Elapsed.TotalMilliseconds);
            }
            durations.Sort();
            Console.WriteLine($"4K software snapshot: median={durations[durations.Count / 2]:F1}ms; " +
                $"max={durations[^1]:F1}ms; samples={durations.Count}. Not displayed GPU FPS.");
        }
        finally { view.Shutdown(); window.Close(); }
    }

    internal static BoardSnapshot Example => new(
    [
        new("cpu", "Retro CPU / 16 Cores", "Processor", "OK", "controller"),
        new("ram", "BANK 0 / 32 GB / DDR5", "Memory", "OK"),
        new("gpu", "Neon Graphics 4080", "Display", "OK", "controller"),
        new("monitor", "Z Main Screen", "Monitor", "OK", "gpu", "Desktop aktiv / Hauptbildschirm", true),
        new("monitor2", "A Secondary Screen", "Monitor", "OK", "gpu", "Desktop aktiv / Erweiterung", false, true),
        new("controller", "PCI Express Controller", "System", "OK"),
        new("net", "2.5 GbE Ethernet", "Net", "OK", "controller"),
        new("usb", "USB 3.2 Host Controller", "USB", "OK", "controller"),
        new("ssd", "NVMe SSD / 2 TB", "DiskDrive", "OK", "controller", StorageBus: "NVMe"),
        new("sound", "High Definition Audio", "Media", "OK", "controller"),
        new("audio-unused", "A Unused HDMI", "AudioEndpoint", "OK", "sound"),
        new("audio-default", "Z Current Speakers", "AudioEndpoint", "OK", "sound", "Standard: Wiedergabe / Multimedia", true),
        new("audio-mic", "Z Current Microphone", "AudioEndpoint", "OK", "sound", "Standard: Aufnahme / Kommunikation", true),
        new("audio-routed", "Z Routed Headset", "AudioEndpoint", "OK", "sound", "WIRD VERWENDET / aktive Audiositzung", false, true, true),
        new("audio-unplugged", "A Unplugged Speaker", "AudioEndpoint", "Nicht angeschlossen", "sound", "Nicht angeschlossen", false, false),
        new("keyboard", "USB Keyboard", "Keyboard", "OK", "usb"),
        new("sata", "SATA AHCI Controller (deaktiviert)", "HDC", "Error", "controller")
    ], "AETHER / SYNTHETIC TEST BOARD", "Internet laut Windows verbunden (Testdaten)", []);

    internal static BoardSnapshot IllustrationExample => Example with
    {
        Devices = Example.Devices.Concat(new BoardDevice[]
        {
            new("ssd2", "NVMe SSD / 1 TB", "DiskDrive", "OK", StorageBus: "NVMe"),
            new("ssd3", "SATA SSD / 4 TB", "DiskDrive", "OK", StorageBus: "SATA"),
            new("ssd4", "Backup SSD / 2 TB", "DiskDrive", "Error", StorageBus: "SATA")
        }).ToArray()
    };

    internal static BoardDevice[] ExternalDrives =>
    [
        new(@"USBSTOR\FLASH", "USB Flash Disk", "DiskDrive", "OK", StorageBus: "USB"),
        new(@"SCSI\UASP", "External SSD", "DiskDrive", "OK", "usb", StorageBus: "SCSI"),
        new("sd-card", "SD Card", "DiskDrive", "OK", StorageBus: "SD"),
        new("unknown-disk", "Unknown Bus SSD", "DiskDrive", "OK"),
        new("optical", "DVD Drive", "CDROM", "OK", StorageBus: "ATAPI")
    ];

    internal static int Run()
    {
        var assertions = 0;
        void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
        var snapshot = Example;
        var groups = HardwareTopology.Group(snapshot);
        Check(groups.Count == 10, "Hardware board separates physical screens from GPUs and other peripherals");
        Check(groups.Sum(group => group.Devices.Count) == snapshot.Devices.Length, "Every present device appears exactly once");
        foreach (var group in groups) Check(group.Devices.Count > 0, "Synthetic board populates " + group.Key);
        Check(groups.Single(g => g.Key == "storage").Devices.Single(d => d.Id == "sata").Status == "Error",
            "Disabled/faulted SATA hardware is not represented as active");
        var empty = HardwareTopology.Group(snapshot with { Devices = [] });
        Check(empty.All(group => group.Devices.Count == 0), "No CPU, RAM or device is fabricated when detection is unavailable");
        var noSata = HardwareTopology.Group(snapshot with { Devices = snapshot.Devices.Where(d => d.Id != "sata").ToArray() });
        Check(noSata.SelectMany(g => g.Devices).All(d => !d.Name.Contains("SATA")), "NVMe-only inventory never invents SATA devices");
        Check(groups.Single(g => g.Key == "other").Devices.Single().ParentId == "usb",
            "Detected parent relationship survives logical grouping");
        Check(HardwareTopology.Group(snapshot with { Devices = [new("unknown", "Unclassified", "", "Unknown")] })
            .Single(g => g.Key == "other").Devices.Count == 1, "Unknown device classes remain visible");
        Check(groups.Single(g => g.Key == "sound").Devices.First().Id == "audio-routed" &&
            groups.Single(g => g.Key == "sound").Devices.Skip(1).Take(2).All(d => d.Preferred) &&
            groups.Single(g => g.Key == "monitor").Devices.Count == 2,
            "All screens are retained; actually used non-default audio precedes defaults and alphabetically earlier unused endpoints");
        var merged = HardwareTopology.MergeDevices([new("SWD\\MMDEVAPI\\TEST", "Old", "AudioEndpoint", "Unknown")],
            [new("swd\\mmdevapi\\test", "Current", "AudioEndpoint", "OK", "", "Standard", true)]);
        Check(merged.Length == 1 && merged[0].Preferred && merged[0].Name == "Current",
            "Core Audio metadata replaces matching PnP entries case-insensitively without duplicate endpoints");
        Check(DisplayTopology.DeviceId(@"\\?\DISPLAY#ACME123#instance#{guid}") == @"DISPLAY\ACME123\instance" &&
            DisplayTopology.DeviceId("unsupported") is null,
            "Actual desktop monitor interfaces map to exact PnP identity, not to the first model name");
        var roundtrip = System.Text.Json.JsonSerializer.Deserialize<BoardSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(snapshot))!;
        Check(roundtrip.Devices.SequenceEqual(snapshot.Devices),
            "Active/default usage metadata roundtrips for shared screensaver feed");
        Check(groups.Single(g => g.Key == "monitor").Devices.First().Id == "monitor",
            "Actual primary display is named before an alphabetically earlier secondary screen");
        Check(HardwareTopology.IsIllustratedPeripheral(new(@"BTHENUM\DEV_012345", "Bluetooth device", "Bluetooth", "OK")) &&
            !HardwareTopology.IsIllustratedPeripheral(new(@"BTHENUM\{service}", "Bluetooth service", "Bluetooth", "OK")),
            "Bluetooth physical device nodes are pictured; protocol/service subnodes remain available in inventory, not fake extra hardware");
        var allDrives = IllustrationExample with { Devices = IllustrationExample.Devices.Concat(ExternalDrives).ToArray() };
        var saver = HardwareTopology.ScreensaverSnapshot(allDrives);
        Check(saver.Devices.Count(d => d.Class == "DiskDrive") == 4 &&
            !saver.Devices.Any(d => ExternalDrives.Any(external => external.Id == d.Id)) &&
            saver.Devices.Any(d => d.Id == "usb"),
            "Saver retains internal NVMe/SATA drives, excludes USB/UASP/SD/unknown/optical media and keeps USB controller");
        Check(allDrives.Devices.Length == IllustrationExample.Devices.Length + ExternalDrives.Length &&
            HardwareTopology.Group(allDrives).Single(g => g.Key == "storage").Devices.Any(d => d.Id == @"USBSTOR\FLASH"),
            "Saver filtering never mutates the complete cockpit inventory");
        var circular = Example with { Devices = [new("cycle", "Cycle Disk", "DiskDrive", "OK", "cycle", StorageBus: "SATA")] };
        Check(HardwareTopology.ScreensaverSnapshot(circular).Devices.Length == 1,
            "Malformed parent cycles cannot hang the saver filter");
        return assertions;
    }

    internal static int Ui(string? screenshot)
    {
        var assertions = 0;
        void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
        var model = new CockpitModel { MotionEnabled = true, CpuTemperature = "51 C (Test)" };
        model.Cards[0].Value = "42.0";
        model.Cards[1].Value = "35.0";
        model.Cards[2].Value = "--";
        model.Cards[3].Value = "12.5";
        model.SidebarUpload.Update(1.2, "Synthetische Messwerte");
        var view = new HardwareBoardView { DataContext = model };
        view.Apply(Example);
        var window = new Window { Content = view, Width = 1200, Height = 1080,
            Background = System.Windows.Media.Brushes.Black, ShowInTaskbar = false };
        try
        {
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var canvas = (Canvas)view.FindName("BoardCanvas");
            assertions += VerifyLabels(view);
            var buttons = canvas.Children.OfType<Button>().Where(b => b.Tag is string key &&
                !key.StartsWith("drive:") && !key.StartsWith("device:")).ToArray();
            Check(buttons.Length == 10, "All ten component callouts must render");
            Check(canvas.Children.OfType<Polygon>().Count() > 175,
                "Board has actual projected component geometry rather than rectangular diagram cards");
            Check(canvas.Children.OfType<Polygon>().Any(p => Equals(p.Tag, "gpu")) &&
                canvas.Children.OfType<Polygon>().Any(p => Equals(p.Tag, "cpu")),
                "CPU cooler and GPU model geometry are clickable and associated with detected hardware");
            var gpuGeometry = canvas.Children.OfType<Polygon>().Where(p => Equals(p.Tag, "gpu")).ToArray();
            var gpuPoints = gpuGeometry.SelectMany(p => p.Points).ToArray();
            Check(gpuGeometry.Count(p => p.Points.Count == 25) >= 6 &&
                gpuPoints.Max(point => point.X) - gpuPoints.Min(point => point.X) > 180,
                "GPU shroud, three fan rings, and card bracket render as a wide clickable expansion card");
            foreach (var button in buttons)
            {
                Check(Canvas.GetLeft(button) >= 0 && Canvas.GetTop(button) + button.ActualHeight <= canvas.Height &&
                    Canvas.GetLeft(button) + button.ActualWidth <= canvas.Width,
                    "Component callout fits within illustration");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Check(HardwareBoardView.WindowsStatus(view.Groups.Single(g => g.Key == "storage")).Contains("Error"),
                "Problematic device status remains visible on its illustration callout");
            Check(((TextBlock)view.FindName("CpuLive")).Text == "42.0" &&
                ((TextBlock)view.FindName("DownloadLive")).Text == "12.5" &&
                ((TextBlock)view.FindName("GpuLive")).Text == "--",
                "Actual live model bindings render readings and preserve unavailable GPU values");
            model.Cards[0].Value = "47.5";
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(((TextBlock)view.FindName("CpuLive")).Text == "47.5",
                "Live readings update independently of hardware snapshot and decorative flow");
            var flows = canvas.Children.OfType<Polyline>().Where(p => p.StrokeDashArray.Count > 0).ToArray();
            Check(flows.Length > 0 && view.FlowAnimating && flows.All(f => f.HasAnimatedProperties),
                "Symbolic flow paths animate when motion is enabled and board is visible");
            var transforms = Descendants(canvas).OfType<FrameworkElement>().Select(e => e.RenderTransform).ToArray();
            Check(transforms.OfType<RotateTransform>().Count() == 4 &&
                transforms.OfType<RotateTransform>().All(t => t.HasAnimatedProperties),
                "CPU fan and three GPU fans rotate in projected planes");
            Check(transforms.OfType<TranslateTransform>().Count() == 7 &&
                transforms.OfType<TranslateTransform>().All(t => t.HasAnimatedProperties),
                "Exactly seven binary crawl rows animate");
            Check(transforms.OfType<ScaleTransform>().Distinct().Single().HasAnimatedProperties,
                "Perspective floor grid retains its receding animation");
            var terrain = canvas.Children.OfType<Canvas>().Single(c => Equals(c.Tag, "binary-mountains"));
            Check(terrain.Clip is StreamGeometry && !terrain.RenderTransform.HasAnimatedProperties &&
                Canvas.GetTop(terrain) + terrain.Height ==
                    Canvas.GetTop(canvas.Children.OfType<Canvas>().Single(c => Equals(c.Tag, "synthwave-ground"))),
                "Mountain feet stay fixed at the grid horizon while only binary rows crawl");
            Check(terrain.Clip.FillContains(new Point(canvas.Width * .16, 60)) &&
                terrain.Clip.FillContains(new Point(canvas.Width * .84, 60)) &&
                !terrain.Clip.FillContains(new Point(canvas.Width * .5, 170)),
                "Mountain ranges flank the horizon left and right with an open central perspective corridor");
            assertions += VerifyLandscape(view);
            var groundPalette = canvas.Children.OfType<Canvas>()
                .Single(c => Equals(c.Tag, "synthwave-ground")).Background;
            var glowPalette = (RadialGradientBrush)canvas.Children.OfType<Ellipse>()
                .Single(c => Equals(c.Tag, "horizon-glow")).Fill;
            var mountainPalette = terrain.Children.OfType<System.Windows.Shapes.Path>().First().Fill;
            var ridgeInk = (SolidColorBrush)terrain.Children.OfType<System.Windows.Shapes.Path>().First(p => p.Stroke is not null).Stroke;
            var gridPalette = (LinearGradientBrush)((Canvas)canvas.Children.OfType<Canvas>()
                .Single(c => Equals(c.Tag, "synthwave-ground")).Children[0]).Children.OfType<System.Windows.Shapes.Path>().First().Stroke;
            Color BaseColor(Brush brush) => brush is LinearGradientBrush gradient
                ? gradient.GradientStops[0].Color : ((SolidColorBrush)brush).Color;
            var groundColor = BaseColor(groundPalette);
            var mountainColor = BaseColor(mountainPalette);
            var glowColor = glowPalette.GradientStops[0].Color;
            var ridgeColor = ridgeInk.Color;
            var gridColor = gridPalette.GradientStops[0].Color;
            var glowCenter = glowPalette.Center;
            var clockWait = new DispatcherFrame();
            var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            clockTimer.Tick += (_, _) => { clockTimer.Stop(); clockWait.Continue = false; };
            clockTimer.Start();
            Dispatcher.PushFrame(clockWait);
            Check((model.IsDaytime
                    ? BaseColor(groundPalette) != groundColor && BaseColor(mountainPalette) != mountainColor
                    : BaseColor(groundPalette) == Colors.Black && BaseColor(mountainPalette) == Colors.Black) &&
                glowPalette.Center != glowCenter &&
                glowPalette.GradientStops[0].Color != glowColor && ridgeInk.Color != ridgeColor &&
                gridPalette.GradientStops[0].Color != gridColor &&
                glowPalette.GradientStops.All(s => s.HasAnimatedProperties),
                "Actual frames preserve black night ground while moving the colored neon glow and grid");
            Check(glowPalette.GradientStops[^1].Color.A == 0 &&
                BaseColor(groundPalette).A == 255 && BaseColor(mountainPalette).A == 255,
                "Animated horizon keeps the glow edge transparent and the landscape opaque");
            Check(transforms.OfType<RotateTransform>().All(t => t.Angle > 0) &&
                transforms.OfType<TranslateTransform>().All(t => t.X < 0) &&
                transforms.OfType<ScaleTransform>().Distinct().Single().ScaleY < 1,
                "Actual frames rotate fans, move binary right-to-left and shrink grid toward horizon");
            foreach (var line in flows.Where(f => f.Tag is string tag && tag.StartsWith("callout-route:", StringComparison.Ordinal)))
            {
                var halos = canvas.Children.OfType<Polyline>().Where(f => Equals(f.Tag, "neon-halo:" + line.Tag)).ToArray();
                Check(halos.Length == 2 && halos.All(h => ReferenceEquals(h.Stroke, line.Stroke) &&
                    ReferenceEquals(h.Points, line.Points) &&
                    h.StrokeDashArray.Count == 0 && !h.HasAnimatedProperties && h.StrokeThickness > line.StrokeThickness),
                    "Static vector halos share route geometry/colors without redundant animated dash clocks");
            }
            var decoded = string.Concat(HardwareBoardView.BinarySignature.Trim().Split(' ')
                .Select(bits => (char)Convert.ToByte(bits, 2)));
            Check(decoded == "AETHER / Olaf Weins / ", "Binary crawl contains exact program and author signature");
            foreach (var device in Example.Devices.Where(d => d.Class is "Monitor" or "AudioEndpoint")
                .Where(HardwareTopology.IsIllustratedPeripheral))
            {
                var button = canvas.Children.OfType<Button>().Single(b => Equals(b.Tag, "device:" + device.Id));
                Check(button.ToolTip.ToString()!.Contains(device.Name) &&
                    ((TextBlock)button.Content).Text.Contains(device.Usage), "Each screen/audio endpoint has its own named, usage-labelled visual");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(((ListBox)view.FindName("DeviceList")).Items[0] is BoardDevice selected && selected.Id == device.Id,
                    "Individual peripheral click selects its exact Windows device, including non-first defaults");
            }
            Check(!canvas.Children.OfType<Button>().Any(b => Equals(b.Tag, "device:audio-unplugged")) &&
                view.Groups.Single(g => g.Key == "sound").Devices.Any(d => d.Id == "audio-unplugged"),
                "Disconnected endpoint is retained in Windows inventory but not pictured as connected hardware");
            var soundLabel = canvas.Children.OfType<Button>().Single(b => Equals(b.Tag, "sound"));
            Check(Descendants((StackPanel)soundLabel.Content).OfType<TextBlock>().Single(t => Equals(t.Tag, "component-name:sound")).Text
                .StartsWith("Z Routed Headset"), "Group caption names actually used audio before unused or default-only devices");
            model.MotionEnabled = false;
            bool Animated(Brush brush) => brush.HasAnimatedProperties ||
                brush is GradientBrush gradient && gradient.GradientStops.Any(s => s.HasAnimatedProperties);
            Check(!glowPalette.HasAnimatedProperties && !Animated(groundPalette) && !Animated(mountainPalette),
                "Motion off removes horizon glow, ground and mountain animation clocks");
            Check(!view.FlowAnimating && flows.All(f => !f.HasAnimatedProperties),
                "Global animation switch removes every flow animation clock");
            Check(!view.SceneAnimating && transforms.Where(t => t is RotateTransform or TranslateTransform or ScaleTransform)
                .All(t => !t.HasAnimatedProperties), "Animation switch releases fans, grid and binary clocks too");
            model.MotionEnabled = true;
            window.Hide();
            Check(!glowPalette.HasAnimatedProperties && !glowPalette.GradientStops.Any(s => s.HasAnimatedProperties) &&
                !ridgeInk.HasAnimatedProperties && !gridPalette.GradientStops.Any(s => s.HasAnimatedProperties),
                "Hidden board releases horizon glow and palette clocks");
            Check(!view.FlowAnimating && flows.All(f => !f.HasAnimatedProperties),
                "Hiding the board stops its animation clocks");
            Check(!view.SceneAnimating && transforms.Where(t => t is RotateTransform or TranslateTransform or ScaleTransform)
                .All(t => !t.HasAnimatedProperties), "Hidden board releases every decorative clock");
            window.Show();
            Check(view.FlowAnimating, "Restoring the visible board resumes enabled schematic animation");
            model.MotionEnabled = false;
            if (screenshot is not null)
            {
                view.Apply(IllustrationExample);
                ((ScrollViewer)view.FindName("BoardScroller")).ScrollToTop();
                ModuleTests.SaveScreenshot(view, screenshot);
            }
            model.MotionEnabled = true;
            view.Apply(Example with { Devices = [], Warnings = ["Hardware nicht verfuegbar"] });
            Check(!glowPalette.HasAnimatedProperties && !glowPalette.GradientStops.Any(s => s.HasAnimatedProperties) &&
                !ridgeInk.HasAnimatedProperties && !gridPalette.GradientStops.Any(s => s.HasAnimatedProperties),
                "Scene rebuild releases old horizon brushes before replacing the landscape");
            Check(view.Groups.All(group => group.Devices.Count == 0) &&
                !canvas.Children.OfType<Polygon>().Any(p => p.Tag is string),
                "Missing hardware removes installed component models, not just labels");
            Check(!view.FlowAnimating, "No schematic flow is animated when no hardware is detected");
            assertions += VerifyLabels(view);
            Check(HardwareBoardView.WindowsStatus(view.Groups[0]) == "NICHT GEMELDET",
                "Missing hardware is never reported as OK");
            view.Apply(Example with { Devices = Example.Devices.Where(d => d.Id != "sata").ToArray() });
            var nvmeGeometry = canvas.Children.OfType<Polygon>().Count(p => Equals(p.Tag, "storage"));
            view.Apply(Example);
            Check(canvas.Children.OfType<Polygon>().Count(p => Equals(p.Tag, "storage")) == nvmeGeometry + 9,
                "SATA connector geometry is conditional; NVMe does not fabricate SATA ports");
            var multiple = Enumerable.Range(0, 13).Select(i => new BoardDevice("ssd" + i,
                i < 10 ? "Identical NVMe SSD" : "SSD " + i, "DiskDrive", i == 4 ? "Error" : "OK")).ToArray();
            view.Apply(IllustrationExample with { Devices = IllustrationExample.Devices.Concat(ExternalDrives).ToArray() });
            Check(view.DriveCount == 9 && canvas.Children.OfType<Button>().Any(b => Equals(b.Tag, @"drive:USBSTOR\FLASH")),
                "Normal cockpit still renders USB drives and optical media after saver-only filtering change");
            view.Apply(Example with { Devices = multiple });
            Check(view.DriveCount == 13 && canvas.Children.OfType<Button>()
                .Count(b => b.Tag is string key && key.StartsWith("drive:")) == 13,
                "Every SSD has its own drawable/clickable representation, including identical model names");
            Check(Enumerable.Range(0, multiple.Length).All(index => view.BoardSurface.Contains(HardwareBoardView.DriveSlot(index))),
                "Every actual drive slot fits on the PCB, including thirteen identical models");
            assertions += VerifyLabels(view);
            Check(!canvas.Children.OfType<TextBlock>().Any(t => t.Text.StartsWith("STORAGE EXPANSION")),
                "No separate off-board drive inventory is appended below the PCB");
            foreach (var button in canvas.Children.OfType<Button>().Where(b => b.Tag is string key && key.StartsWith("drive:")))
            {
                var bounds = button.RenderTransform.TransformBounds(new Rect(0, 0, button.Width, button.Height));
                bounds.Offset(Canvas.GetLeft(button), Canvas.GetTop(button));
                Check(new Rect(0, 0, canvas.Width, canvas.Height).Contains(bounds), "Projected drive label fits inside scene bounds");
            }
            view.Apply(Example with { Devices = Enumerable.Range(0, 100)
                .Select(i => new BoardDevice("large" + i, "NVMe SSD", "DiskDrive", "OK")).ToArray() });
            Check(view.DriveCount == 100 && Enumerable.Range(0, 100).All(index =>
                view.BoardSurface.Contains(HardwareBoardView.DriveSlot(index))),
                "Dynamic board grows without any drive count limit or slot clipping");
            assertions += VerifyLabels(view);
            view.Apply(Example with { Devices = multiple });
            var disk = canvas.Children.OfType<Button>().Single(b => Equals(b.Tag, "drive:ssd4"));
            disk.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((ListBox)view.FindName("DeviceList")).Items.Count == 1 &&
                ((ListBox)view.FindName("DeviceList")).Items[0] is BoardDevice { Id: "ssd4", Status: "Error" },
                "Individual drive click selects exact device and preserves its fault status");
            view.Apply(Example with { Devices = [new("adapter", "Storage adapter", "SCSIAdapter", "OK")] });
            Check(!canvas.Children.OfType<Polygon>().Any(p => Equals(p.Tag, "storage")),
                "Storage controller detection alone does not invent a physical SSD or drive");
            var manyPeripherals = Enumerable.Range(0, 31).SelectMany(i => new[]
            {
                new BoardDevice("screen" + i, "Same Monitor", "Monitor", "OK", "", "Desktop aktiv", i == 30, true),
                new BoardDevice("endpoint" + i, "Same Speakers", "AudioEndpoint", "OK", "", "Bereit", i == 30, true)
            }).ToArray();
            view.Apply(Example with { Devices = manyPeripherals });
            Check(canvas.Children.OfType<Button>().Count(b => b.Tag is string key && key.StartsWith("device:")) == 62,
                "All monitor/audio identities are pictured even with more than thirty identical names per family");
            Check(canvas.Children.OfType<Button>().Where(b => b.Tag is string key && key.StartsWith("device:"))
                .All(b => Canvas.GetLeft(b) >= 0 && Canvas.GetTop(b) + b.Height <= canvas.Height &&
                    Canvas.GetLeft(b) + b.Width <= canvas.Width),
                "Large peripheral inventories grow the scene without clipping last devices or imposing a hidden cap");
            var shutdownGlow = (RadialGradientBrush)canvas.Children.OfType<Ellipse>()
                .Single(c => Equals(c.Tag, "horizon-glow")).Fill;
            view.Shutdown();
            Check(!shutdownGlow.HasAnimatedProperties && !shutdownGlow.GradientStops.Any(s => s.HasAnimatedProperties),
                "Shutdown releases current horizon position and palette clocks");
            Check(!view.FlowAnimating, "Shutdown releases all flow animation clocks");
            Check(!view.SceneAnimating, "Shutdown stops fans, binary crawl and grid");
            return assertions;
        }
        finally { view.Shutdown(); window.Close(); }
    }

    internal static int VerifyLabels(HardwareBoardView view)
    {
        view.UpdateLayout();
        var canvas = (Canvas)view.FindName("BoardCanvas");
        var labels = canvas.Children.OfType<Button>().Where(b => b.Tag is string key &&
            !key.StartsWith("drive:") && !key.StartsWith("device:")).ToArray();
        var bounds = labels.Select(b => new Rect(Canvas.GetLeft(b), Canvas.GetTop(b), b.ActualWidth, b.ActualHeight)).ToArray();
        var rim = canvas.Children.OfType<Polyline>().Single(p => Equals(p.Tag, "board-neon-rim"));
        var outline = new StreamGeometry();
        using (var context = outline.Open())
        {
            context.BeginFigure(rim.Points[0], true, true);
            context.PolyLineTo(rim.Points.Skip(1).ToArray(), true, false);
        }
        outline.Transform = new TranslateTransform(
            double.IsFinite(Canvas.GetLeft(rim)) ? Canvas.GetLeft(rim) : 0,
            double.IsFinite(Canvas.GetTop(rim)) ? Canvas.GetTop(rim) : 0);
        for (var index = 0; index < labels.Length; index++)
        {
            var label = labels[index];
            if (label.Background is not LinearGradientBrush brush || brush.GradientStops.Any(s => s.Color.A != 255) ||
                brush.MappingMode != BrushMappingMode.Absolute || !label.ClipToBounds ||
                Panel.GetZIndex(label) < 1)
                throw new InvalidOperationException("Label must mask underlying routes and clip content.");
            if (bounds.Where((_, other) => other != index).Any(b => b.IntersectsWith(bounds[index])))
                throw new InvalidOperationException("Component labels overlap: " + label.Tag);
            var clearance = bounds[index];
            clearance.Inflate(12, 12);
            if (outline.FillContainsWithDetail(new RectangleGeometry(clearance)) != IntersectionDetail.Empty)
                throw new InvalidOperationException("Component information box masks the PCB or its corner: " + label.Tag);
            var route = canvas.Children.OfType<Polyline>().Single(p => Equals(p.Tag, "callout-route:" + label.Tag));
            var end = route.Points[^1];
            end.Offset(double.IsFinite(Canvas.GetLeft(route)) ? Canvas.GetLeft(route) : 0,
                double.IsFinite(Canvas.GetTop(route)) ? Canvas.GetTop(route) : 0);
            var rect = bounds[index];
            if (!(Math.Abs(end.X - rect.Left) < .01 || Math.Abs(end.X - rect.Right) < .01 ||
                  Math.Abs(end.Y - rect.Top) < .01 || Math.Abs(end.Y - rect.Bottom) < .01))
                throw new InvalidOperationException("Callout route ends inside text instead of on label edge.");
            if (label.Content is StackPanel content && content.ActualHeight > label.ActualHeight - 9 + .01)
                throw new InvalidOperationException("Component description exceeds label height: " + label.Tag);
        }
        return labels.Length * 5;
    }

    internal static int VerifyLandscape(HardwareBoardView view)
    {
        var canvas = (Canvas)view.FindName("BoardCanvas");
        var sky = canvas.Children.OfType<Rectangle>().Single(r => Equals(r.Tag, "synthwave-sky"));
        if (sky.Fill is not LinearGradientBrush skyGradient || skyGradient.GradientStops.Count < 4 ||
            Panel.GetZIndex(sky) >= 0)
            throw new InvalidOperationException("Synthwave sky must sit behind hardware with navy/magenta horizon gradient.");
        var ground = canvas.Children.OfType<Canvas>().Single(c => Equals(c.Tag, "synthwave-ground"));
        var grid = (Canvas)ground.Children[0];
        if (grid.RenderTransform is ScaleTransform || ground.Children.Count != 2 ||
            ground.Children[1] is not Canvas { RenderTransform: ScaleTransform { ScaleX: 1 } } bars ||
            !Equals(bars.Tag, "road-crossbars"))
            throw new InvalidOperationException("Road rays must remain fixed; only crossbars recede on the Y axis.");
        var paths = grid.Children.OfType<System.Windows.Shapes.Path>().ToArray();
        if (paths.Length < 3 || paths.Length % 3 != 0 ||
            paths[0].Opacity >= paths[1].Opacity || paths[1].Opacity >= paths[2].Opacity ||
            paths[0].StrokeThickness <= paths[1].StrokeThickness || paths[1].StrokeThickness <= paths[2].StrokeThickness ||
            !paths.All(p => p.Data is LineGeometry { IsFrozen: true } && ReferenceEquals(p.Stroke, paths[0].Stroke)) ||
            paths[2].Stroke is not LinearGradientBrush palette ||
            !palette.GradientStops.Any(s => (Color)s.GetAnimationBaseValue(GradientStop.ColorProperty) == (Color)ColorConverter.ConvertFromString("#FF21DC")) ||
            !palette.GradientStops.Any(s => (Color)s.GetAnimationBaseValue(GradientStop.ColorProperty) == (Color)ColorConverter.ConvertFromString("#00DFFF")))
            throw new InvalidOperationException("Raster requires shared cyan/magenta wire geometry and bounded glow layer.");
        var rayBounds = new Rect(0, 0, ground.Width, ground.Height);
        rayBounds.Inflate(.01, .01);
        if (paths.Any(p => !rayBounds.Contains(p.Data.Bounds)) || palette.MappingMode != BrushMappingMode.Absolute ||
            paths.Chunk(3).Any(group => !group.All(p => ReferenceEquals(p.Data, group[0].Data))))
            throw new InvalidOperationException("Road rays must be independently frozen, bounded and share one scene-aligned palette.");
        var terrain = canvas.Children.OfType<Canvas>().Single(c => Equals(c.Tag, "binary-mountains"));
        if (terrain.Clip.FillContains(new Point(canvas.Width / 2, terrain.Height - 1)) ||
            Canvas.GetTop(terrain) + terrain.Height != Canvas.GetTop(ground) ||
            terrain.Children.OfType<System.Windows.Shapes.Path>().Count(p => p.Stroke is not null) != 6)
            throw new InvalidOperationException("Glowing mountain ranges must flank the grid horizon, not cover the floor center.");
        if (canvas.Children.OfType<Button>().Any(b => b.Tag is string key &&
            !key.StartsWith("drive:") && !key.StartsWith("device:") &&
            Canvas.GetTop(b) + b.Height > Canvas.GetTop(terrain)))
            throw new InvalidOperationException("Component labels must not mask or cut off the horizon peaks.");
        if (canvas.Children.OfType<Polyline>().Count(p => Equals(p.Tag, "neon-halo:board-neon-rim")) != 2)
            throw new InvalidOperationException("Mainboard perimeter requires neon cyan glow.");
        var bloom = canvas.Children.OfType<Image>().SingleOrDefault(image => Equals(image.Tag, "board-neon-bloom"));
        if (bloom is not { IsHitTestVisible: false, CacheMode: BitmapCache,
                Effect: System.Windows.Media.Effects.BlurEffect { Radius: >= 8 }, Source: DrawingImage { Drawing.IsFrozen: true } } ||
            Descendants(view).OfType<UIElement>().Any(e => e.Effect is not null && !ReferenceEquals(e, bloom)))
            throw new InvalidOperationException("Board bloom must use one cached, frozen stroke layer without blurring labels or animated components.");
        if (!skyGradient.GradientStops.Any(s => s.HasAnimatedProperties) && view.SceneAnimating)
            throw new InvalidOperationException("Enabled scene requires moving sky gradients, not a static sky.");
        return 8;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
