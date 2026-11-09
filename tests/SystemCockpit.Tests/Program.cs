using System.IO;
using System.Text.Json;
using SystemCockpit;
using SystemCockpit.Services;

internal static class Program
{
    private static int _assertions;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--command-fixture") return CommandLifecycleTests.Fixture(args[1]);
            DesktopText.Configure("de");
            if (args.Length == 2 && args[0] == "--feed-server") return ScreensaverTests.PipeServer(args[1]);
            if (args.Length == 2 && args[0] == "--feed-client") return ScreensaverTests.PipeClient(args[1]);
            // Produktfenster beziehen die Theme-Brushes aus den App-Ressourcen;
            // der Testhost stellt sie identisch bereit (Retro-Neon-Standard).
            var host = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            foreach (var source in new[] { "Controls/NeonInputs.xaml", "Controls/NeonGlass.xaml" })
                host.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/SystemCockpit;component/{source}")
                });
            if (args.Contains("--desktop-language-smoke"))
            {
                var assertions = ModernDesktopTests.Run();
                Console.WriteLine($"PASS: {assertions} authored desktop resource and compatibility assertions.");
                host.Shutdown();
                return 0;
            }
            if (args.Contains("--desktop-ui-smoke") || args.Contains("--desktop-modern-smoke"))
            {
                var index = Array.IndexOf(args, args.Contains("--desktop-modern-smoke")
                    ? "--desktop-modern-smoke" : "--desktop-ui-smoke");
                var screenshotDirectory = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[index + 1] : Environment.GetEnvironmentVariable("AETHER_SCREENSHOT_DIR");
                var assertions = ModernDesktopTests.UiSmoke(screenshotDirectory);
                Console.WriteLine($"PASS: {assertions} German Windows Modern light/dark and Neon compatibility smoke assertions.");
                host.Shutdown();
                return 0;
            }
            if (args.Contains("--board-render-profile"))
            {
                var index = Array.IndexOf(args, "--board-render-profile");
                HardwareBoardTests.RenderProfile(index + 1 < args.Length ? args[index + 1] : "");
                return 0;
            }
            if (args.Contains("--gpu-presentation"))
            {
                var index = Array.IndexOf(args, "--gpu-presentation");
                var screenshotPrefix = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[index + 1] : null;
                var assertions = HardwareBoardTests.GpuPresentation(screenshotPrefix);
                Console.WriteLine($"PASS: {assertions} GPU perspective and occlusion assertions.");
                host.Shutdown();
                return 0;
            }
            TestCpu();
            TestSparklines();
            TestSensorSelection();
            TestModel();
            TestHistory();
            TestPowerShellStreams();
            TestTheme();
            TestStatistics();
            TestCleaner();
            _assertions += ExtendedTests.Run();
            _assertions += ConsolidationTests.Run();
            _assertions += CommandLifecycleTests.Run();
            _assertions += DashboardTests.Run();
            _assertions += BootTests.Run();
            _assertions += StorageTemperatureTests.Run();
            _assertions += ModuleTests.Run();
            if (args.Contains("--inventory-ui"))
            {
                var index = Array.IndexOf(args, "--inventory-screenshot");
                _assertions += ModuleTests.InventoryUi(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
            }
            _assertions += DesktopFeatureTests.Run();
            _assertions += ModernDesktopTests.Run();
            _assertions += SettingsUpgradeTests.Run();
            _assertions += HardwareBoardTests.Run();
            _assertions += ScreensaverTests.Run();
            _assertions += SensorChartTests.Run();
            if (args.Contains("--sensor-charts-ui"))
            {
                var index = Array.IndexOf(args, "--sensor-charts-screenshot");
                _assertions += SensorChartTests.Ui(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
            }
            var scrProcessIndex = Array.IndexOf(args, "--screensaver-process");
            if (scrProcessIndex >= 0 && scrProcessIndex + 1 < args.Length)
                _assertions += ScreensaverTests.ProcessPreview(args[scrProcessIndex + 1]);
            var scrLauncherIndex = Array.IndexOf(args, "--screensaver-launcher");
            if (scrLauncherIndex >= 0 && scrLauncherIndex + 1 < args.Length)
                _assertions += ScreensaverTests.ProcessPreview(args[scrLauncherIndex + 1]);
            var scrFullscreenIndex = Array.IndexOf(args, "--screensaver-fullscreen");
            if (scrFullscreenIndex >= 0 && scrFullscreenIndex + 1 < args.Length)
                _assertions += ScreensaverTests.ProcessFullscreen(args[scrFullscreenIndex + 1]);
            if (args.Contains("--screensaver-ui"))
            {
                var index = Array.IndexOf(args, "--screensaver-screenshot");
                _assertions += ScreensaverTests.Ui(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
            }
            if (args.Contains("--board-ui"))
            {
                var index = Array.IndexOf(args, "--board-screenshot");
                _assertions += HardwareBoardTests.Ui(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
            }
            if (args.Contains("--board-live"))
            {
                var snapshot = HardwareTopology.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert(snapshot.Devices.Length > 0, "Local read-only board inventory returns devices");
                Console.WriteLine($"BOARD: {snapshot.Devices.Length} devices; {snapshot.Warnings.Length} warnings");
                var displays = DisplayTopology.Read(snapshot.Devices);
                var audio = AudioTopology.Read();
                Assert(displays.All(d => snapshot.Devices.Any(item => item.Id.Equals(d.Id, StringComparison.OrdinalIgnoreCase) &&
                    item.Connected == true && item.Usage.Contains("aktiv", StringComparison.OrdinalIgnoreCase))),
                    "Every actual active display is present with usage metadata");
                Assert(audio.All(d => snapshot.Devices.Any(item => item.Id.Equals(d.Id, StringComparison.OrdinalIgnoreCase) &&
                    item.Usage.Length > 0)), "Every actual audio endpoint is present with explicit Windows usage/state");
                Assert(audio.Where(d => d.Connected == true).All(d => d.InUse.HasValue),
                    "Actual read-only audio session enumeration is supported on each ready endpoint");
                Console.WriteLine($"BOARD ACTIVE: {displays.Count} desktop monitor paths; {audio.Count(d => d.Connected == true)} ready audio endpoints; {audio.Count(d => d.Preferred)} audio defaults; {audio.Count(d => d.InUse == true)} audio endpoints in use; {audio.Count(d => d.Connected == false)} disconnected/disabled endpoints");
                Console.WriteLine("BOARD PERIPHERALS: " + string.Join("; ", snapshot.Devices
                    .Where(HardwareTopology.IsIllustratedPeripheral)
                    .GroupBy(d => d.Class).Select(g => $"{g.Key}: {g.Count()}")));
                var disks = snapshot.Devices.Where(d => d.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase)).ToArray();
                var saverDisks = HardwareTopology.ScreensaverSnapshot(snapshot).Devices
                    .Where(d => d.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase)).ToArray();
                Assert(disks.Length == 0 || disks.Any(d => d.StorageBus.Length > 0),
                    "Actual disk bus metadata is populated by the read-only Windows query");
                Assert(saverDisks.All(d => !d.StorageBus.Equals("USB", StringComparison.OrdinalIgnoreCase) &&
                    !d.StorageBus.Equals("SD", StringComparison.OrdinalIgnoreCase)),
                    "Actual saver snapshot excludes external USB and SD disk buses");
                Console.WriteLine($"BOARD DISKS: {disks.Length} detected; {saverDisks.Length} internal in saver; buses: " +
                    string.Join("; ", disks.GroupBy(d => d.StorageBus).Select(g => $"{g.Key}: {g.Count()}")));
                foreach (var warning in snapshot.Warnings) Console.WriteLine(warning);
            }
            _assertions += SwtTests.Run();
            _assertions += AdminTests.Run();
            _assertions += OperationsTests.Run();
            _assertions += ProfessionalWorkflowTests.Run();
            if (args.Contains("--professional-ui"))
            {
                var index = Array.IndexOf(args, "--professional-screenshot-prefix");
                _assertions += ProfessionalWorkflowTests.Ui(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
            }
            if (args.Contains("--operations-ui"))
            {
                var index = Array.IndexOf(args, "--operations-screenshot-prefix");
                _assertions += OperationsTests.LiveUi(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
            }
            if (args.Contains("--admin-ui"))
            {
                var index = Array.IndexOf(args, "--admin-screenshot-prefix");
                _assertions += AdminTests.LiveUi(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
            }
            if (args.Contains("--boot-ui"))
            {
                string? Option(string name)
                {
                    var index = Array.IndexOf(args, name);
                    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
                }
                _assertions += BootTests.LiveUi(Option("--boot-screenshot"), Option("--neon-screenshot"));
            }
            if (args.Contains("--branding-ui"))
            {
                var index = Array.IndexOf(args, "--branding-screenshot-prefix");
                _assertions += BootTests.BrandingUi(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
            }
            if (args.Contains("--interface-ui"))
            {
                var index = Array.IndexOf(args, "--interface-screenshot-prefix");
                _assertions += InterfaceTests.Run(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
            }
            if (args.Contains("--dashboard-ui"))
            {
                var screenshotIndex = Array.IndexOf(args, "--dashboard-screenshot");
                _assertions += DashboardTests.LiveUi(screenshotIndex >= 0 && screenshotIndex + 1 < args.Length ? args[screenshotIndex + 1] : null);
            }
            if (args.Contains("--catalog-ui")) _assertions += ExtendedTests.CatalogUi();
            if (args.Contains("--xbar-ui"))
            {
                var index = Array.IndexOf(args, "--xbar-screenshot-prefix");
                _assertions += ModuleTests.LiveSidebarUi(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
            }
            if (args.Contains("--desktop-ui"))
            {
                var index = Array.IndexOf(args, "--desktop-screenshot-prefix");
                _assertions += DesktopFeatureTests.LiveUi(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
            }
            if (args.Contains("--live")) TestLive().GetAwaiter().GetResult();
            if (args.Contains("--hardware")) TestHardware();
            if (args.Contains("--tools")) _assertions += ExtendedTests.ToolsAsync().GetAwaiter().GetResult();
            if (args.Contains("--winget-live")) _assertions += ModuleTests.WingetLive().GetAwaiter().GetResult();
            if (args.Contains("--inventory"))
            {
                var snapshot = SystemInventory.BuildAsync(CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"INVENTORY: {snapshot.Hardware.Count} hardware entries / {snapshot.Software.Count} installed programs");
                foreach (var item in snapshot.Hardware)
                    Console.WriteLine($"  {item.Category} | {item.Name}: {item.Detail}");
                Assert(snapshot.Hardware.Count >= 4 && snapshot.Software.Count > 5, "Real inventory returns hardware and software");
            }
            Console.WriteLine($"PASS: {_assertions} assertions");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex}");
            return 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void TestCpu()
    {
        Assert(NativeMetrics.CalculateCpu(25, 100) == 75, "CPU uses kernel+user minus idle");
        Assert(NativeMetrics.CalculateCpu(100, 100) == 0, "Idle CPU is zero");
        Assert(NativeMetrics.CalculateCpu(0, 100) == 100, "Busy CPU is 100");
        try { NativeMetrics.CalculateCpu(0, 0); throw new InvalidOperationException("Zero interval accepted"); }
        catch (ArgumentOutOfRangeException) { _assertions++; }
    }

    private static void TestSparklines()
    {
        var trace = Sparkline.Create([0, 50, 100], 100);
        Assert(trace.Count == 3 && trace[0].Y == 56 && trace[1].Y == 28 && trace[2].Y == 0, "Sparkline scale");
        Assert(trace[0].X == 0 && trace[2].X == 280, "Sparkline width");
        Assert(Sparkline.Create([], 100).Count == 0, "Empty sparkline");
        Assert(Sparkline.Create([1], 100)[0].X == 280, "Single point on right");
        try { Sparkline.Create([double.NaN], 100); throw new InvalidOperationException("NaN accepted"); }
        catch (ArgumentException) { _assertions++; }
    }

    private static void TestSensorSelection()
    {
        SensorReading[] sensors =
        [
            new("/intelcpu/0/temperature/0", "CPU", "CPU Core #1", "Temperature", 45, "C"),
            new("/intelcpu/0/temperature/24", "CPU", "CPU Package", "Temperature", 60, "C"),
            new("/gpu-nvidia/0/temperature/0", "GPU", "GPU Core", "Temperature", null, "C")
        ];
        Assert(CockpitModel.Find(sensors, "/intelcpu/", "Temperature", "CPU Package")?.Value == 60,
            "Select package, not first arbitrary core");
        Assert(CockpitModel.Find(sensors, "/gpu-", "Temperature", "GPU Core")?.Display == "Nicht verfuegbar",
            "Unavailable sensor is not zero");
        Assert(CockpitModel.Find(sensors, "/intelcpu/", "Power", "CPU Package") is null, "Missing sensor remains missing");
        Assert(HardwareSensors.NormalizeValue(0, true, true, false) is null, "Zero RAPL with unreadable CPU clocks is unavailable");
        Assert(HardwareSensors.NormalizeValue(0, false, true, false) == 0, "Do not alter legitimate non-CPU zero readings");
        Assert(HardwareSensors.NormalizeValue(35, true, true, false) == 35, "Positive measured CPU power preserved");
        Assert(HardwareSensors.NormalizeValue(float.NaN, false, false, true) is null, "Invalid hardware floats cannot enter JSON history");
    }

    private static LiveSample Sample(DateTimeOffset timestamp, List<SensorReading>? sensors = null) =>
        new(timestamp, 30, 50, 16, 32, 1, 0.5, [], sensors ?? []);

    private static void TestModel()
    {
        var model = new CockpitModel();
        var time = DateTimeOffset.UtcNow;
        for (var i = 0; i < 100; i++) model.Apply(Sample(time.AddSeconds(i)), false);
        Assert(model.Cards[0].Trace.Count == 90, "History in RAM is bounded");
        Assert(model.Cards[2].Value == "--", "No fake GPU load without sensor");
        model.Apply(Sample(time) with { CpuPercent = null }, false);
        Assert(model.Cards[0].Value == "--" && model.Cards[0].Trace.Count == 0, "Missing value clears stale chart");
        model.ApplyProbe(new("CPU", "GPU", "Board", "RAM", "Windows",
            [new("Secure Boot", "Unknown", "unknown", "Access denied")], []));
        Assert(model.SecuritySummary.Contains("1 nicht lesbare"), "Unknown does not pass posture");
        Assert(model.Alerts.Count == 1, "Unknown is visible in alerts");
        model.Apply(Sample(time,
        [
            new("/intelcpu/0/temperature/0", "CPU", "CPU Package", "Temperature", 90, "C"),
            new("/gpu-nvidia/0/temperature/0", "GPU", "GPU Core", "Temperature", 85, "C")
        ]), true);
        Assert(model.Alerts.Count == 3, "Exact thermal thresholds trigger");
        model.SensorFilter = "gpu";
        Assert(model.SensorView.Cast<SensorReading>().Count() == 1, "Sensor filter");
        model.ProbeFailed("timeout");
        Assert(model.Checks.Single().State == "unknown" && model.Disks.Single().State == "unknown", "Probe error replaces stale results");
        model.Apply(Sample(time) with { SensorError = "driver blocked" }, true);
        Assert(model.Cards[0].Value != "--" && model.Cards[1].Value != "--", "Sensor errors do not disable native metrics");
        Assert(model.SensorStatus.Contains("driver blocked") && model.Alerts.Any(c => c.Name == "Hardware-Sensoren"),
            "Sensor error is visible");
        model.MeasurementFailed("native failure");
        Assert(model.Cards.All(c => c.Value == "--" && c.Trace.Count == 0), "Failed native measurement clears stale cards");
        Assert(model.CpuTemperature == "Nicht verfuegbar", "Failed native measurement clears thermal display");
    }

    private static void TestHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AetherTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var history = new LocalHistory(directory);
            var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < 9; i++) history.Append(Sample(timestamp.AddDays(i)));
            var files = Directory.GetFiles(directory);
            Assert(files.Length == LocalHistory.MaxFiles, "Keep at most 7 daily files");
            Assert(!File.Exists(Path.Combine(directory, "metrics-2026-01-01.jsonl")), "Oldest files removed");
            using var json = JsonDocument.Parse(File.ReadAllText(files[0]));
            Assert(json.RootElement.GetProperty("schema").GetInt32() == 1, "History schema");
            Assert(json.RootElement.GetProperty("CpuPercent").GetDouble() == 30, "History stores actual sample shape");
            Assert(!json.RootElement.TryGetProperty("Volumes", out _), "No filesystem inventory persisted");
            var today = timestamp.AddDays(8);
            var path = Path.Combine(directory, "metrics-2026-01-09.jsonl");
            using (var stream = File.OpenWrite(path)) stream.SetLength(LocalHistory.MaxFileBytes);
            try { history.Append(Sample(today)); throw new InvalidOperationException("History limit ignored"); }
            catch (IOException) { _assertions++; }
            Assert(new FileInfo(path).Length == LocalHistory.MaxFileBytes, "Limit does not grow file");
            // Regression: parallele Aufzeichnung ist serialisiert und vermischt keine JSONL-Zeilen.
            var parallelHistory = new LocalHistory(Path.Combine(directory, "parallel"));
            var stamp = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
            Parallel.For(0, 64, _ => parallelHistory.Append(Sample(stamp)));
            var lines = File.ReadAllLines(Path.Combine(directory, "parallel", "metrics-2026-02-01.jsonl"));
            Assert(lines.Length == 64, "Parallel appends keep every line");
            Assert(lines.All(line =>
            {
                try { using var document = JsonDocument.Parse(line); return true; }
                catch (JsonException) { return false; }
            }), "Parallel appends never interleave JSONL lines");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task TestLive()
    {
        var native = new NativeMetrics();
        var first = native.Read();
        Assert(first.CpuPercent is null && first.DownloadMbps is null, "Initial live sample requires baseline");
        await Task.Delay(1100);
        var second = native.Read();
        Assert(second.CpuPercent is >= 0 and <= 100, "Real CPU in valid range");
        Assert(second.MemoryTotalGb > 0 && second.MemoryUsedGb <= second.MemoryTotalGb, "Real physical memory");
        Assert(second.Volumes.Count > 0, "Real fixed volumes discovered");
        var report = await new WindowsProbe().ReadAsync(CancellationToken.None);
        Assert(!string.IsNullOrWhiteSpace(report.Cpu) && report.Cpu != "Nicht verfuegbar", "Real CPU identified");
        Assert(report.Checks.Count >= 10, "All Windows posture surfaces returned");
        Assert(report.Disks.Count > 0, "Real storage health returned");
        Assert(report.Checks.All(c => c.State is "ok" or "warning" or "unknown" or "info"), "Explicit status shapes");
        Assert(report.Diagnostics is { Count: > 0 }, "Extended diagnosis returned");
        var diagnostics = report.Diagnostics ?? throw new InvalidOperationException("Missing diagnosis");
        Assert(diagnostics.Any(c => c.Name.StartsWith("Crash") || c.Name == "Crash Analyzer / 30 Tage"),
            "Crash analyzer reports records, empty result or explicit access failure");
        Assert(diagnostics.Any(c => c.Name.StartsWith("Adapter") || c.Name == "Netzwerkadapter"),
            "Network adapter diagnostics wired");
        Console.WriteLine($"LIVE: {report.Cpu} | {report.Gpu}");
        Console.WriteLine($"LIVE: {second.MemoryTotalGb:0.0} GB RAM | {second.Volumes.Count} volumes | {report.Disks.Count} disks");
        foreach (var check in report.Checks)
            Console.WriteLine($"  {check.Name}: {check.State} | {check.Value}");
    }

    private static void TestPowerShellStreams()
    {
        const string progress = "#< CLIXML\n<Objs xmlns=\"http://schemas.microsoft.com/powershell/2004/04\"><Obj S=\"progress\"><MS/></Obj></Objs>";
        const string error = "#< CLIXML\n<Objs xmlns=\"http://schemas.microsoft.com/powershell/2004/04\"><S S=\"error\">Access denied</S></Objs>";
        Assert(WindowsProbe.ReadErrorStream(progress) == "", "Initialization progress is not an error");
        Assert(WindowsProbe.ReadErrorStream(error) == "Access denied", "Real PowerShell errors decoded to plain text, no raw XML");
        const string multiline = "#< CLIXML\n<Objs xmlns=\"http://schemas.microsoft.com/powershell/2004/04\">" +
            "<S S=\"Error\">Kein OEM-Firmware-Produktschluessel vorhanden._x000D__x000A_</S>" +
            "<S S=\"Error\">    + CategoryInfo          : OperationStopped_x000D__x000A_</S></Objs>";
        var decoded = WindowsProbe.ReadErrorStream(multiline);
        Assert(!decoded.Contains("<S") && !decoded.Contains("_x000D_") &&
            decoded.Contains("Kein OEM-Firmware-Produktschluessel vorhanden.") && decoded.Contains("+ CategoryInfo"),
            "CLIXML error records render as readable lines with decoded escapes");
        Assert(SystemActions.SecretScript("bitlocker", "").TrimEnd().EndsWith("Keine Schluessel werden erfunden.'}"),
            "BitLocker emptiness check runs after scanning all volumes and protectors");
        Assert(WindowsProbe.ReadErrorStream("raw failure") == "raw failure", "Raw stderr preserved");
        Assert(WindowsProbe.ReadErrorStream("") == "", "Empty stderr");
    }

    private static void TestTheme()
    {
        var window = new MainWindow();
        var style = (System.Windows.Style)window.Resources[typeof(System.Windows.Controls.TextBlock)];
        var foreground = style.Setters.OfType<System.Windows.Setter>()
            .Single(s => s.Property == System.Windows.Controls.TextBlock.ForegroundProperty).Value;
        Assert(foreground is System.Windows.Media.SolidColorBrush brush &&
            brush.Color == System.Windows.Media.Color.FromRgb(225, 234, 247),
            "Dark panels use explicit readable text foreground");
        var cards = (System.Windows.Controls.ItemsControl)window.FindName("MetricCards");
        var card = (System.Windows.Controls.Border)cards.ItemTemplate.LoadContent();
        var stack = (System.Windows.Controls.StackPanel)card.Child;
        var instrument = stack.Children.OfType<System.Windows.Controls.Grid>().Single();
        var value = instrument.Children.OfType<System.Windows.Controls.StackPanel>().Single()
            .Children.OfType<SystemCockpit.Controls.LedDisplay>().Single();
        Assert(value.Height >= 32 && value.Width >= 100, "Template metric values use large segmented LED display");
        Assert(instrument.Children.OfType<SystemCockpit.Controls.NeonGauge>().Count() == 1, "Metric uses segmented neon instrument");
        var pages = (System.Windows.Controls.TabControl)window.FindName("Pages");
        Assert(pages.Items.Count == 13, "Original modules plus hardware board, Taskmanager, events, Superadmin, Operations and help center");
        var pageHeaders = pages.Items.Cast<System.Windows.Controls.TabItem>().Select(t => (string)t.Header).ToList();
        Assert(pageHeaders[^1] == "Hilfe" && pageHeaders.IndexOf("Sensoren") < pageHeaders.IndexOf("Messauswertung") &&
            pageHeaders.IndexOf("Diagnose") < pageHeaders.IndexOf("Cleaner") && pageHeaders.IndexOf("Cleaner") < pageHeaders.IndexOf("Superadmin"),
            "Tabs follow observe-analyze-check-act-help order");
        Assert(pages.Items.Cast<System.Windows.Controls.TabItem>().All(t => t.ToolTip is string { Length: > 10 }),
            "Every main module explains itself via tooltip");
        // Navigation: Verlauf, Leiste und linke Rubriken muessen zusammenpassen.
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var navButtons = ((System.Windows.Controls.StackPanel)window.FindName("NavigationGroups"))
            .Children.OfType<System.Windows.Controls.Expander>().SelectMany(group =>
                ((System.Windows.Controls.StackPanel)group.Content).Children.OfType<System.Windows.Controls.Button>())
            .Where(b => b.Tag is string route && pageHeaders.Contains(route)).ToList();
        Assert(navButtons.Count == 13 && navButtons.All(b => pageHeaders.Contains((string)b.Tag)) &&
            navButtons.Select(b => (string)b.Tag).Distinct().Count() == 13,
            "Left rubric buttons map one-to-one onto real page headers including help");
        var navBack = (System.Windows.Controls.Button)window.FindName("NavBack");
        var navForward = (System.Windows.Controls.Button)window.FindName("NavForward");
        var navTrail = (System.Windows.Controls.TextBlock)window.FindName("NavTrail");
        var selectPage = typeof(MainWindow).GetMethod("SelectPage", flags)!;
        selectPage.Invoke(window, ["Superadmin"]);
        selectPage.Invoke(window, ["Cleaner"]);
        Assert(navBack.IsEnabled && !navForward.IsEnabled && navTrail.Text.Contains("CLEANER"),
            "Breadcrumb shows current page; back enabled after visiting submodules");
        var backClick = typeof(MainWindow).GetMethod("NavBackClicked", flags)!;
        backClick.Invoke(window, [navBack, new System.Windows.RoutedEventArgs()]);
        Assert((string)((System.Windows.Controls.TabItem)pages.SelectedItem).Header == "Superadmin" && navForward.IsEnabled,
            "Back returns to previous view without the left bar; forward becomes available");
        backClick.Invoke(window, [navBack, new System.Windows.RoutedEventArgs()]);
        Assert((string)((System.Windows.Controls.TabItem)pages.SelectedItem).Header == "Cockpit",
            "History walks back to the main level");
        typeof(MainWindow).GetMethod("NavForwardClicked", flags)!.Invoke(window, [navForward, new System.Windows.RoutedEventArgs()]);
        Assert((string)((System.Windows.Controls.TabItem)pages.SelectedItem).Header == "Superadmin",
            "Forward replays the undone navigation");
        selectPage.Invoke(window, ["Cockpit"]);
        Assert(MainWindow.CsvCell("=cmd").StartsWith("\"'="), "CSV formula injection neutralized");
        Assert(MainWindow.CsvCell("a\"b") == "\"a\"\"b\"", "CSV quoting preserved");
    }

    private static void TestStatistics()
    {
        var stats = new SessionStatistics();
        var time = DateTimeOffset.UtcNow;
        stats.Add(Sample(time) with { CpuPercent = 10 });
        stats.Add(Sample(time.AddSeconds(2)) with { CpuPercent = 20 });
        stats.Add(Sample(time.AddSeconds(4)) with { CpuPercent = null });
        var cpu = stats.Snapshot().Single(s => s.Name == "CPU Gesamt");
        Assert(cpu.Count == 2 && cpu.Minimum == 10 && cpu.Maximum == 20 && cpu.Average == 15,
            "MIN MAX AVG uses valid samples, not missing zeros");
        Assert(stats.Started == time, "Session timestamp retained");
        stats.Reset();
        Assert(stats.Snapshot().Count == 0 && stats.Started is null, "Reset clears session");
    }

    private static void TestCleaner()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "AetherCleanerTests-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(fixture, "scope");
        Directory.CreateDirectory(root);
        try
        {
            var old = Path.Combine(root, "old.tmp");
            var changed = Path.Combine(root, "changed.tmp");
            var fresh = Path.Combine(root, "fresh.tmp");
            var outside = Path.Combine(fixture, "outside.tmp");
            foreach (var path in new[] { old, changed, fresh, outside }) File.WriteAllText(path, "test content");
            foreach (var path in new[] { old, changed, outside }) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-8));
            var cleaner = new TempCleaner(root);
            var scan = cleaner.Scan(CancellationToken.None);
            Assert(scan.Files.Count == 2 && scan.Issues.Count == 0, "Only old files enter cleaner preview");
            File.AppendAllText(changed, "changed after preview");
            var fake = new CleanupCandidate(outside, new FileInfo(outside).Length, File.GetLastWriteTimeUtc(outside));
            var result = cleaner.DeleteSelected(scan.Files.Append(fake), CancellationToken.None);
            Assert(result.Deleted == 1 && result.Issues.Count == 2, "Delete only verified preview candidates");
            Assert(!File.Exists(old) && File.Exists(changed) && File.Exists(fresh) && File.Exists(outside),
                "Changed, recent and out-of-scope files preserved");
            Assert(Directory.Exists(root), "Cleaner never removes directories");
            var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert(cleaner.DeleteSelected(scan.Files, cancel.Token).Cancelled, "Cancellation reports partial status");
            File.SetLastWriteTimeUtc(changed, DateTime.UtcNow.AddDays(-8));
            var secondPreview = cleaner.Scan(CancellationToken.None);
            using (var locked = new FileStream(changed, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var lockedResult = cleaner.DeleteSelected(secondPreview.Files, CancellationToken.None);
                Assert(lockedResult.Deleted == 0 && lockedResult.Issues.Count == 1, "Locked preview file preserved with explicit error");
            }
            var link = Path.Combine(root, "hardlink.tmp");
            if (!CreateHardLink(link, outside, IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            var hardlinkScan = cleaner.Scan(CancellationToken.None);
            var hardlinkResult = cleaner.DeleteSelected(hardlinkScan.Files.Where(f => f.Path == link), CancellationToken.None);
            Assert(hardlinkResult.Deleted == 0 && hardlinkResult.Issues.Count == 1 && File.Exists(outside),
                "Hardlink never deleted even when it enters preview");
            var junction = Path.Combine(root, "junction");
            var destination = Path.Combine(fixture, "destination");
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "protected.tmp"), "outside scope");
            File.SetLastWriteTimeUtc(Path.Combine(destination, "protected.tmp"), DateTime.UtcNow.AddDays(-8));
            using var junctionCommand = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Environment.GetEnvironmentVariable("ComSpec") ?? throw new InvalidOperationException("ComSpec missing"),
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                }
            };
            junctionCommand.StartInfo.ArgumentList.Add("/c");
            junctionCommand.StartInfo.ArgumentList.Add("mklink");
            junctionCommand.StartInfo.ArgumentList.Add("/J");
            junctionCommand.StartInfo.ArgumentList.Add(junction);
            junctionCommand.StartInfo.ArgumentList.Add(destination);
            junctionCommand.Start();
            junctionCommand.WaitForExit();
            if (junctionCommand.ExitCode != 0) throw new IOException(junctionCommand.StandardError.ReadToEnd());
            try
            {
                Assert(cleaner.Scan(CancellationToken.None).Files.All(f => !f.Path.Contains("protected.tmp")),
                    "Junction targets excluded from preview");
            }
            finally { Directory.Delete(junction); }
        }
        finally
        {
            Directory.Delete(fixture, recursive: true);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string name, string existing, IntPtr security);

    private static void TestHardware()
    {
        using var sensors = new HardwareSensors();
        sensors.Open();
        sensors.Read();
        Thread.Sleep(1200);
        var readings = sensors.Read();
        Assert(readings.Count > 0, "Hardware library discovers real sensors");
        Assert(readings.Any(s => s.Value is { } value && double.IsFinite(value)), "Real hardware values readable");
        Console.WriteLine($"HARDWARE: {readings.Count(s => s.Value is not null)} / {readings.Count} readable sensors");
        foreach (var type in readings.GroupBy(s => s.Type))
            Console.WriteLine($"  {type.Key}: {type.Count(s => s.Value is not null)} / {type.Count()}");
        foreach (var reading in readings.Where(s => s.Value is not null && s.Type is "Temperature" or "Fan" or "Power").Take(25))
            Console.WriteLine($"  {reading.Hardware} | {reading.Name}: {reading.Display}");
        foreach (var reading in readings.Where(s => SensorSemantics.IsStorage(s.Id) && s.Type is "Temperature" or "TemperatureLimit"))
            Console.WriteLine($"  STORAGE: {reading.Hardware} | {reading.Id} | {reading.Name} | {reading.Type}: {reading.Display}");
        var gpu = CockpitModel.Find(readings, "/gpu-", "Load", "GPU Core");
        var cpu = CockpitModel.Find(readings, "/intelcpu/", "Temperature", "CPU Package");
        Console.WriteLine($"  Cockpit GPU core: {gpu?.Display ?? "Nicht verfuegbar"}");
        Console.WriteLine($"  Cockpit CPU package: {cpu?.Display ?? "Nicht verfuegbar"}");
    }
}
