using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class OperationsTests
{
    public static int Run()
    {
        var count = 0;
        void Check(bool valid, string message) { count++; if (!valid) throw new InvalidOperationException(message); }
        var valid = PerformanceIndex.Parse("""{"State":1,"Base":8.9,"Cpu":9.5,"Memory":9.4,"Graphics":9.2,"Disk":8.9,"StoredAt":null}""");
        Check(valid.Valid && valid.Base == 8.9 && valid.Cpu == 9.5, "Display real valid Windows scores");
        var invalid = PerformanceIndex.Parse("""{"State":2,"Base":9.9,"Cpu":9.9,"Memory":9.9,"Graphics":9.9,"Disk":9.9,"StoredAt":null}""");
        Check(!invalid.Valid && invalid.Base is null && invalid.Cpu is null, "Hardware-incoherent assessment never displays stale numbers as valid");
        var missing = PerformanceIndex.Parse("""{"State":3,"Base":0,"Cpu":0,"Memory":0,"Graphics":0,"Disk":0,"StoredAt":null}""");
        Check(missing.Base is null && missing.Disk is null, "No assessment is not zero performance");
        var ids = MaintenancePlans.Template("Nur Diagnose");
        Check(ids.Count == 4 && MaintenancePlans.Catalog.Where(a => ids.Contains(a.Id)).All(a => !a.Changes), "Default plan is read-only");
        Check(MaintenancePlans.Catalog.Count == 17, "All original maintenance categories are represented natively with guided cleaner");
        Check(MaintenancePlans.Catalog.Single(a => a.Id == "sfc").Script.StartsWith("sfc /scannow"),
            "Native SFC plan goes through existing UTF-16 streaming helper, not raw UTF-8 native invocation");
        Check(WindowsTroubleshootingAssistants.All.Count == 4 &&
            WindowsTroubleshootingAssistants.All.Select(a => a.Id).Distinct().Count() == 4,
            "Four distinct Windows troubleshooting assistants are registered");
        var windowsAssistantSystem = WindowsTroubleshootingAssistants.Get("system").Script;
        Check(windowsAssistantSystem.Contains("/CheckHealth") && windowsAssistantSystem.Contains("/verifyonly") &&
            !windowsAssistantSystem.Contains("/RestoreHealth") && !windowsAssistantSystem.Contains("/scannow"),
            "System assistant checks image/files without performing repairs");
        var windowsAssistantUpdate = WindowsTroubleshootingAssistants.Get("update").Script;
        Check(!windowsAssistantUpdate.Contains("Start-Service") && !windowsAssistantUpdate.Contains("Set-Service") &&
            !windowsAssistantUpdate.Contains("Install-WindowsUpdate"),
            "Windows Update assistant never restarts services or installs updates");
        var windowsAssistantNetwork = WindowsTroubleshootingAssistants.Get("network").Script;
        Check(windowsAssistantNetwork.Contains("Test-Connection") && windowsAssistantNetwork.Contains("Resolve-DnsName") &&
            !windowsAssistantNetwork.Contains("Set-NetIPInterface") &&
            !windowsAssistantNetwork.Contains("Set-DnsClientServerAddress"),
            "Network assistant tests the configured gateway/DNS without changing adapter settings");
        var windowsAssistantDevices = WindowsTroubleshootingAssistants.Get("devices").Script;
        Check(windowsAssistantDevices.Contains("Get-PnpDevice") &&
            !windowsAssistantDevices.Contains("pnputil /add-driver") &&
            !windowsAssistantDevices.Contains("Update-PnpDevice"),
            "Device assistant reads PnP problems but does not install/update drivers");
        var diagnosticSnapshot = new WindowsDiagnosticSnapshot("fixture-host", "Windows 11", "24H2", "26100.1",
            Enumerable.Range(1, 5).Select(i => new DiagnosticEvent("Application", $"2026-01-01T00:0{i}:00+00:00",
                1000, 2, "Application Error", $"Fixture crash {i}")).ToArray(),
            [new("Fixture device", "Display", "Error", 10, "PCI\\FIXTURE")],
            [new("Wi-Fi", "Fixture adapter", "Up", "1 Gbps")],
            [new("Fixture app", "1.0", "Fixture publisher")],
            [new("Fixture device", "1.0", "2026-01-01", true)]);
        var diagnosticReport = DiagnosticAssistants.Analyze(diagnosticSnapshot);
        Check(diagnosticReport.Findings.Any(f => f.Agent == "Anwendungsereignisse" && f.Severity == "HOCH" &&
            f.Summary.Contains("5 Treffer")), "Repeated application crash events become a bounded, reviewable finding");
        Check(diagnosticReport.Findings.Any(f => f.Agent == "Treiber und Geraete" && f.Evidence.Contains("PCI\\FIXTURE") &&
            f.Recommendation.Contains("nicht automatisch")), "PnP problem codes recommend review but never auto-install drivers");
        Check(DiagnosticAssistants.Analyze(diagnosticSnapshot with
            { Devices = [new("Fixture device", "Display", "", null, "PCI\\FIXTURE")] })
            .Findings.Single(f => f.Agent == "Treiber und Geraete").Severity == "UNBEKANNT",
            "Missing PnP status remains unknown instead of being treated as healthy");
        Check(DiagnosticAssistants.Analyze(diagnosticSnapshot with
            { Adapters = [new("Wi-Fi", "Fixture adapter", "Disconnected", "")] })
            .Findings.Single(f => f.Agent == "Verbindungen").Severity == "HINWEIS",
            "Disconnected adapters are advisory, not automatically treated as failures");
        Check(DiagnosticAssistants.Analyze(diagnosticSnapshot with { Adapters = [] })
            .Findings.Single(f => f.Agent == "Verbindungen").Severity == "UNBEKANNT",
            "Unavailable network inventory remains unknown instead of healthy");
        var parsedDiagnostic = DiagnosticAssistants.Parse("""
            {"MachineName":"fixture-host","Windows":"Windows 11","DisplayVersion":"24H2","Build":"26100.1",
             "Events":[],"Devices":[],"Adapters":[],"Applications":[],"Drivers":[]}
            """);
        Check(parsedDiagnostic.MachineName == "fixture-host" &&
            DiagnosticAssistants.Analyze(parsedDiagnostic).Findings.Any(f => f.Severity == "INFORMATION"),
            "Agent report parser preserves zero-result states without inventing a health score");
        var package = MaintenancePlans.Catalog.Single(a => a.Id == "wingetInstall");
        var script = MaintenancePlans.Build(package, [], "Microsoft.PowerToys\nMicrosoft.PowerToys\n7zip.7zip");
        Check(script.Split("--id").Length == 3 && !script.Contains("--all") && !script.Contains("--allow-reboot"), "Exact app IDs de-duplicated, no all/reboot");
        try { MaintenancePlans.Build(package, [], "Microsoft.Test; Start-Process bad"); throw new InvalidOperationException("Option injection accepted"); }
        catch (ArgumentException) { count++; }
        Check(MaintenancePlans.ValidUpdateId(Guid.NewGuid() + ":100") && !MaintenancePlans.ValidUpdateId("other:1"), "Windows Update ID includes GUID and exact revision");
        var update = new AvailableUpdate(Guid.NewGuid() + ":1", "Fixture", 10, "EULA O'Brien $(foo) \"quote\"");
        var updateScript = MaintenancePlans.Build(MaintenancePlans.Catalog.Single(a => a.Id == "windowsInstall"), [update], "");
        Check(updateScript.Contains("RevisionNumber") && updateScript.Contains("-cne") && updateScript.Contains("GetUpdateResult"),
            "Update apply rechecks identity/revision/license and per-update download/install result");
        foreach (var host in new[] { "*.local", "host;Stop-Process", "https://server", "localhost:5986", "host..test" })
        {
            try { new ManagedDevice(host); throw new InvalidOperationException("Invalid remote target accepted"); }
            catch (ArgumentException) { count++; }
        }
        var target = new ManagedDevice("server-01.example.local");
        var remote = ManagedDevices.DiagnosticScript(target);
        Check(remote.Contains("-UseSSL") && remote.Contains("-Authentication Negotiate") && !remote.Contains("SkipCACheck") &&
            !remote.Contains("Set-Item") && !remote.Contains("Enable-PSRemoting"), "Remote diagnostics preserve TLS validation and existing security configuration");
        Check(ManagedDevices.CloudReadinessScript.Contains("LOCAL ONLY") && !ManagedDevices.CloudReadinessScript.Contains("Connect-MgGraph"),
            "Local cloud readiness never silently connects tenant");
        var candidate = new CleanupCandidate("fixture", 100, DateTime.UtcNow);
        Check(!candidate.Selected, "Cleaner candidates default unchecked");
        candidate.Selected = true;
        Check(candidate.Selected, "Explicit cleaner checkbox selection supported");
        count += VerifyLedRendering();
        return count;
    }

    private static int VerifyLedRendering()
    {
        var count = 0;
        foreach (var height in new[] { 20, 24, 26, 28, 30, 32, 36, 38, 46 })
        foreach (var dpi in new[] { 96, 120, 144, 192 })
        {
            var led = new LedDisplay { Text = "0", Accent = Brushes.Lime, Width = 60, Height = height };
            var panel = new DockPanel { Width = 60, Height = height, Background = Brushes.Black };
            panel.Children.Add(led);
            panel.Measure(new Size(60, height));
            panel.Arrange(new Rect(0, 0, 60, height));
            panel.UpdateLayout();
            if (Math.Abs(led.ActualHeight - height) > 0.01)
                throw new InvalidOperationException($"LED render height exceeds its slot: {led.ActualHeight} / {height}");
            var factor = dpi / 96d;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(60 * factor), (int)Math.Ceiling(height * factor),
                dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(panel);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var scale = height / 32d;
            foreach (var (x, y) in new[] { (9d, 2.5), (15.5, 9), (15.5, 23), (9, 29.5), (2.5, 23), (2.5, 9) })
            {
                var pixelX = (int)Math.Floor(((60 - 20 * scale) / 2 + x * scale) * factor);
                var pixelY = (int)Math.Floor(y * scale * factor);
                if (pixels[(pixelY * bitmap.PixelWidth + pixelX) * 4 + 1] < 100)
                    throw new InvalidOperationException($"LED zero lost segment at {x}/{y}, height {height}, DPI {dpi}");
            }
            count++;
        }
        return count;
    }

    public static int LiveUi(string? screenshotPrefix)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Aether-operations-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var scripts = new List<string> { PerformanceIndex.ReadScript, PerformanceIndex.BenchmarkScript,
                DiagnosticAssistants.CollectionScript, MaintenancePlans.RestorePointScript,
                ManagedDevices.CloudReadinessScript, ManagedDevices.DiagnosticScript(new("fixture-host")) };
            scripts.AddRange(WindowsTroubleshootingAssistants.All.Select(a => a.Script));
            foreach (var action in MaintenancePlans.Catalog.Where(a => a.Id != "cleanup"))
                scripts.Add(MaintenancePlans.Build(action, [new(Guid.NewGuid() + ":1", "Fixture", 1, "EULA 'quoted'")], "Fixture.Package"));
            var manifest = Path.Combine(folder, "scripts.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(scripts));
            var parser = CommandRunner.PowerShellAsync($"$scripts=Get-Content -Raw -Encoding UTF8 -LiteralPath {AdminManagement.Quote(manifest)} | ConvertFrom-Json; " +
                "foreach($script in $scripts){$errors=$null;[void][Management.Automation.Language.Parser]::ParseInput($script,[ref]$null,[ref]$errors);if($errors.Count){throw ($errors | Out-String)}};'Syntax OK'",
                CancellationToken.None).GetAwaiter().GetResult();
            if (!parser.Contains("Syntax OK")) throw new InvalidOperationException("Operations scripts parser failed");
            var large = AdminShell.RunAsync("#" + new string('x', 40000) + "\n'Large unicode / Gruen: ä / no mutation'", TimeSpan.FromSeconds(20), CancellationToken.None).GetAwaiter().GetResult();
            if (large.ExitCode != 0 || !large.Output.Contains("Gruen: ä")) throw new InvalidOperationException("Large stdin-transport script failed");
            var updateFixture = new AvailableUpdate("11111111-1111-1111-1111-111111111111:1", "Fixture", 1, "Expected license");
            var installScript = MaintenancePlans.Build(MaintenancePlans.Catalog.Single(a => a.Id == "windowsInstall"), [updateFixture], "");
            var mock = """
                $global:fixtureUpdate=[pscustomobject]@{Identity=[pscustomobject]@{UpdateID='11111111-1111-1111-1111-111111111111';RevisionNumber=1};EulaText='CHANGED LICENSE';EulaAccepted=$false}
                $global:fixtureSearch=[pscustomobject]@{ResultCode=2;Updates=@($global:fixtureUpdate)}
                $global:fixtureSearcher=[pscustomobject]@{}
                $global:fixtureSearcher | Add-Member ScriptMethod Search {param($query) return $global:fixtureSearch}
                $global:fixtureSession=[pscustomobject]@{}
                $global:fixtureSession | Add-Member ScriptMethod CreateUpdateSearcher {return $global:fixtureSearcher}
                function global:New-Object {param($ComObject)
                    if($ComObject -eq 'Microsoft.Update.Session'){return $global:fixtureSession}
                    if($ComObject -eq 'Microsoft.Update.UpdateColl'){return [Collections.ArrayList]::new()}
                    throw 'Unexpected fixture COM request'
                }
                """;
            var rejected = AdminShell.RunAsync(mock + "\n" + installScript, TimeSpan.FromSeconds(20), CancellationToken.None).GetAwaiter().GetResult();
            if (rejected.ExitCode == 0 || !rejected.Error.Contains("Lizenz seit Vorschau geaendert"))
                throw new InvalidOperationException("Changed EULA must stop mocked installation before download/install");
            var path = Path.Combine(folder, "targets.json");
            ManagedDevices.Save(path, [new("fixture-host"), new("192.0.2.1", false)]);
            var saved = ManagedDevices.Load(path);
            if (saved.Count != 2 || saved.Any(d => d.Selected) || saved[1].Https) throw new InvalidOperationException("Device target file must not save selection or credentials");
            var index = PerformanceIndex.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            var probe = new WindowsProbe().ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine("WINSAT READ: " + index.StateText + " / base " + (index.Base?.ToString() ?? "--") + " / no benchmark executed");
            var cloud = CommandRunner.PowerShellAsync(ManagedDevices.CloudReadinessScript, CancellationToken.None).GetAwaiter().GetResult();
            if (!cloud.Contains("LOCAL ONLY")) throw new InvalidOperationException("Local readiness failed");
            var main = new MainWindow();
            main.Show();
            Pump();
            try
            {
                var metrics = new DashboardMetrics();
                metrics.Read();
                Thread.Sleep(1000);
                var snapshot = metrics.Read();
                var sample = new NativeMetrics().Read();
                var model = (CockpitModel)main.DataContext;
                model.ApplyProbe(probe);
                model.Apply(sample, false);
                model.ApplyDashboard(snapshot, sample);
                model.ApplyEvents(EventMonitor.ReadAsync(CancellationToken.None).GetAwaiter().GetResult());
                var pages = (TabControl)main.FindName("Pages");
                pages.SelectedItem = pages.Items.Cast<TabItem>().Single(p => Equals(p.Header, "Taskmanager"));
                main.WindowState = WindowState.Maximized;
                Pump();
                main.UpdateLayout();
                var cores = (ItemsControl)main.FindName("TaskmanagerCoreOverview");
                var baselineWait = Stopwatch.StartNew();
                while (model.CoreGraphs.Any(g => g.NumericValue == "--") && baselineWait.Elapsed < TimeSpan.FromSeconds(8))
                {
                    Pump(); Thread.Sleep(30);
                }
                main.UpdateLayout();
                VerifyAllCoreBounds(cores, snapshot.Cores.Count);
                main.WindowState = WindowState.Normal;
                foreach (var (width, height) in new[] { (1920, 1080), (1440, 940) })
                {
                    main.Width = width; main.Height = height;
                    Pump(); main.UpdateLayout();
                    model.ApplyDashboard(snapshot, sample);
                    VerifyAllCoreBounds(cores, snapshot.Cores.Count);
                    if (screenshotPrefix is not null) Capture(main, screenshotPrefix + $"-taskmanager-{width}.png");
                    pages.SelectedItem = pages.Items.Cast<TabItem>().Single(p => Equals(p.Header, "Cockpit"));
                    Pump(); main.UpdateLayout();
                    VerifyAllCoreBounds((ItemsControl)main.FindName("CockpitCoreOverview"), snapshot.Cores.Count);
                    if (screenshotPrefix is not null) Capture(main, screenshotPrefix + $"-cockpit-{width}.png");
                    pages.SelectedItem = pages.Items.Cast<TabItem>().Single(p => Equals(p.Header, "Taskmanager"));
                    Pump(); main.UpdateLayout();
                }
                main.WindowState = WindowState.Maximized;
                Pump(); main.UpdateLayout();
                if (screenshotPrefix is not null) Capture(main, screenshotPrefix + "-taskmanager.png");
                pages.SelectedItem = pages.Items.Cast<TabItem>().Single(p => Equals(p.Header, "Cockpit"));
                if (((ItemsControl)main.FindName("CockpitCoreOverview")).Items.Count != snapshot.Cores.Count)
                    throw new InvalidOperationException("Cockpit live core overview missing");
                if (screenshotPrefix is not null) Capture(main, screenshotPrefix + "-cockpit.png");
                pages.SelectedItem = pages.Items.Cast<TabItem>().Single(p => Equals(p.Header, "Cleaner"));
                var grid = (DataGrid)main.FindName("CleanupGrid");
                model.CleanupFiles.Clear();
                var first = new CleanupCandidate(Path.Combine(folder, "never-delete-a.txt"), 1048576, DateTime.UtcNow);
                var second = new CleanupCandidate(Path.Combine(folder, "never-delete-b.txt"), 2097152, DateTime.UtcNow);
                model.CleanupFiles.Add(first); model.CleanupFiles.Add(second);
                grid.SelectedIndex = 0;
                Pump();
                Find<Button>(main, b => Equals(b.Content, "Angekreuzte Dateien loeschen")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (first.Selected || second.Selected || !model.CleanupStatus.StartsWith("Keine Dateien angekreuzt"))
                    throw new InvalidOperationException("Row highlighting must not authorize deletion");
                Find<Button>(main, b => Equals(b.Content, "Sichtbare ankreuzen")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!first.Selected || !second.Selected) throw new InvalidOperationException("Select all checkboxes failed");
                Find<Button>(main, b => Equals(b.Content, "Keine ankreuzen")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (first.Selected || second.Selected) throw new InvalidOperationException("Select none failed");
                Find<Button>(main, b => Equals(b.Content, "Markierte Zeilen ankreuzen")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!first.Selected || second.Selected) throw new InvalidOperationException("Row-to-checkbox explicit operation failed");
                var secondCheckbox = Find<CheckBox>(grid, checkbox => ReferenceEquals(checkbox.DataContext, second));
                secondCheckbox.IsChecked = true;
                Pump();
                if (!second.Selected || !((TextBlock)main.FindName("CleanupSelectionStatus")).Text.Contains("2 / 2"))
                    throw new InvalidOperationException("Rendered checkbox did not update model/count");
                if (screenshotPrefix is not null) Capture(main, screenshotPrefix + "-cleaner.png");
                pages.SelectedItem = pages.Items.Cast<TabItem>().Single(p => Equals(p.Header, "Operations"));
                Pump();
                var hub = (OperationsHub)main.FindName("Operations");
                var tasks = (DataGrid)hub.FindName("Tasks");
                if (tasks.Items.Count != 17 || tasks.Items.Cast<MaintenanceChoice>().Count(t => t.Selected) != 4)
                    throw new InvalidOperationException("Native maintenance UI/default diagnostic template missing");
                var assistantTabs = (TabControl)hub.FindName("Tabs");
                var windowsAssistantTab = assistantTabs.Items.Cast<TabItem>()
                    .SingleOrDefault(item => Equals(item.Header, "Windows-Assistenten"));
                if (windowsAssistantTab is null)
                    throw new InvalidOperationException("Windows troubleshooting assistant tab is missing");
                assistantTabs.SelectedItem = windowsAssistantTab;
                Pump();
                if (
                    WindowsTroubleshootingAssistants.All.Any(assistant =>
                        !Descendants(hub).OfType<Button>().Any(button => Equals(button.Tag, assistant.Id))))
                    throw new InvalidOperationException("Dedicated Windows troubleshooting assistants are missing from the Operations UI");
                pages.SelectedItem = pages.Items.Cast<TabItem>().Single(page => Equals(page.Header, "Diagnose"));
                Pump();
                Find<Button>(main, button => Equals(button.Content, "Windows-Diagnose & Problemlösung"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
                if (!ReferenceEquals(pages.SelectedItem, pages.Items.Cast<TabItem>()
                        .Single(page => Equals(page.Header, "Operations"))) ||
                    !Equals(assistantTabs.SelectedItem is TabItem selected ? selected.Header : null, "Windows-Assistenten"))
                    throw new InvalidOperationException("Main cockpit shortcut does not open the Windows assistants");
                Find<Button>(hub, button => Equals(button.Content, "Windows-Reparaturplan vorbereiten / noch nichts ausfuehren"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
                if (!Equals(assistantTabs.SelectedItem is TabItem repairTab ? repairTab.Header : null, "Wartungsplan") ||
                    tasks.Items.Cast<MaintenanceChoice>().Count(task => task.Selected && task.Action.Changes) != 2)
                    throw new InvalidOperationException("Repair-plan preparation did not select the confirmed DISM/SFC steps");
                if (((TextBox)hub.FindName("Console")).Text.Contains("[START]"))
                    throw new InvalidOperationException("Preparing a Windows repair plan unexpectedly executed a repair");
                Pump();
                if (screenshotPrefix is not null) Capture(main, screenshotPrefix + "-operations.png");
                Find<Button>(hub, b => Equals(b.Content, "Plan anzeigen / noch nichts ausfuehren")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!((TextBox)hub.FindName("Console")).Text.Contains("[VORSCHAU / KEINE AUSFUEHRUNG]"))
                    throw new InvalidOperationException("Native maintenance preview did not produce plan without side effects");
                Console.WriteLine($"OPERATIONS UI: {snapshot.Cores.Count} / {snapshot.Cores.Count} core graphs fit maximized {main.ActualWidth:0}x{main.ActualHeight:0}; checkbox delete guards, 17-task preview, WinSAT/readiness and {scripts.Count} parsed-only scripts verified.");
            }
            finally
            {
                main.Close();
                var watch = Stopwatch.StartNew();
                while (main.IsVisible && watch.Elapsed < TimeSpan.FromSeconds(20)) { Pump(); Thread.Sleep(20); }
                if (main.IsVisible) throw new InvalidOperationException("Operations shutdown exceeded limit");
            }
            return 10;
        }
        finally { Directory.Delete(folder, true); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static T Find<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject =>
        Descendants(root).OfType<T>().First(predicate);
    private static void VerifyAllCoreBounds(ItemsControl cores, int count)
    {
        if (cores.Items.Count != count || cores.ActualHeight <= 0 || Descendants(cores).OfType<ScrollViewer>().Any())
            throw new InvalidOperationException("Core overview still scrolls or lost cores");
        for (var i = 0; i < count; i++)
        {
            var item = (FrameworkElement)cores.ItemContainerGenerator.ContainerFromIndex(i);
            var bounds = item.TransformToAncestor(cores).TransformBounds(new Rect(item.RenderSize));
            if (bounds.Top < -1 || bounds.Left < -1 || bounds.Bottom > cores.ActualHeight + 1 || bounds.Right > cores.ActualWidth + 1 ||
                item.ActualHeight < 45 || item.ActualWidth < 100)
                throw new InvalidOperationException($"Core {i} clipped/unreadable at maximized size: {bounds}");
            var led = Find<LedDisplay>(item, _ => true);
            if (led.Text == "--" || led.ActualHeight < 15) throw new InvalidOperationException("Core percentage LED missing");
            var graph = Find<Viewbox>(item, _ => true);
            if (graph.ActualHeight < 10) throw new InvalidOperationException("Core trace collapsed");
            var ledBounds = led.TransformToAncestor(item).TransformBounds(new Rect(led.RenderSize));
            var graphBounds = graph.TransformToAncestor(item).TransformBounds(new Rect(graph.RenderSize));
            if (ledBounds.Bottom > graphBounds.Top + 0.01 || led.ActualHeight > led.Height + 0.01)
                throw new InvalidOperationException("Core trace obscures lower LED segments");
        }
    }
    private static void Capture(Window window, string path)
    {
        Pump(); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
