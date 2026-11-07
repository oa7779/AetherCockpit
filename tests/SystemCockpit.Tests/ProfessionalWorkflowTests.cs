using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SystemCockpit;
using SystemCockpit.Services;

internal static class ProfessionalWorkflowTests
{
    public static int Run()
    {
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new InvalidOperationException(message); }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or JsonException) { count++; return; }
            throw new InvalidOperationException(message);
        }

        Check(TaskCatalog.All.Count > 100 && TaskCatalog.All.Select(t => (t.Category, t.Title)).Distinct().Count() == TaskCatalog.All.Count,
            "Search includes unique workflows and complete command catalog");
        Check(TaskCatalog.Search("Software Updates").Any(t => t.Destination == "Pakete") &&
            TaskCatalog.Search("DNS", "Netzwerk").All(t => t.Category == "Netzwerk") &&
            TaskCatalog.Search("NonsensNichtVorhanden").Count == 0, "Multi-term search/category/empty results are genuine");
        Check(AdminCommandCatalog.All.All(c => TaskCatalog.All.Any(t => t.Command == c.Command && t.Effect == c.RiskBadge)),
            "Every command is discoverable with unchanged risk classification");

        var packages = new[] { new WingetRow("Git", "Git.Git", "2.0", "2.1", "winget"), new WingetRow("7-Zip", "7zip.7zip", "", "", "winget") };
        var bundle = PackageCenter.ParseBundle(PackageCenter.SerializeBundle(packages));
        Check(bundle.Schema == 1 && bundle.Packages.Count == 2 && bundle.Packages[0].Source == "winget", "Package bundle roundtrips source-bound identities");
        foreach (var verb in new[] { "install", "upgrade", "uninstall", "show" })
        {
            var script = PackageCenter.TargetCommandScript(verb, packages[0]);
            Check(script.Contains("--id 'Git.Git' --exact --source 'winget'") && script.Contains("-ErrorAction Stop") &&
                !script.Contains("--all") && !script.Contains("--allow-reboot"), "Exact source binding, unavailable tool errors and no implicit all/reboot");
            Check((verb is "install" or "upgrade") == script.Contains("--silent"), "Only install/upgrade allow silent package execution");
        }
        Reject(() => PackageCenter.TargetCommandScript("show", packages[0] with { Source = "winget;evil" }), "Source injection accepted");
        Reject(() => PackageCenter.TargetCommandScript("show", packages[0] with { Id = "Git\u2026" }), "Truncated identity accepted");
        Reject(() => PackageCenter.SerializeBundle([packages[0] with { Source = "" }]), "Source-less bundle accepted");
        Reject(() => PackageCenter.SerializeBundle([packages[0], packages[0] with { Id = "git.git", Source = "other" }]), "Ambiguous duplicate bundle accepted");
        Reject(() => PackageCenter.SerializeBundle([]), "Empty bundle accepted");
        Reject(() => PackageCenter.ParseBundle("{\"Schema\":1,\"Packages\":[null]}"), "Null bundle entry accepted");
        Reject(() => PackageCenter.ParseBundle("{\"Schema\":1,\"Packages\":null}"), "Null package list accepted");
        Reject(() => PackageCenter.ParseBundle(PackageCenter.SerializeBundle(packages).Replace("\"Schema\": 1", "\"Schema\": 2")), "Unknown schema accepted");
        Reject(() => PackageCenter.ParseBundle("{\"Schema\":1,\"Schema\":1,\"Packages\":[]}"), "Duplicate JSON property accepted");
        Reject(() => PackageCenter.ParseBundle("{\"Schema\":1,\"Packages\":[],\"Script\":\"evil\"}"), "Executable/unknown field accepted");
        Reject(() => PackageCenter.ParseBundle(new string(' ', PackageCenter.MaxBundleBytes + 1)), "Unbounded package input accepted");

        var packet = new PacketSummary(DateTimeOffset.UtcNow, "192.0.2.2", "2001:db8::1", "DNS / UDP", 49000, 53, 90, false, true);
        Check(PacketFilter.Create("192.0.2 DNS", "UDP", "53", false, true).Matches(packet), "Packet filters combine text/protocol/port/flags");
        Check(!PacketFilter.Create("", "TCP", "", false, false).Matches(packet) &&
            !PacketFilter.Create("", "DNS", "80", false, false).Matches(packet) &&
            !PacketFilter.Create("", "DNS", "", true, false).Matches(packet), "Protocol, port and SYN exclusions are real");
        Check(PacketFilter.Create("", "ICMP", "", false, false).Matches(packet with { Protocol = "ICMPv6" }), "ICMP includes IPv6");
        foreach (var port in new[] { "0", "65536", "abc", "-1", "53;evil" })
            Reject(() => PacketFilter.Create("", "Alle", port, false, false), "Invalid packet port accepted");
        Check(PacketFilter.Describe(packet).Contains("128-Byte") &&
            PacketFilter.Csv([packet with { Source = "=DANGER" }]).Contains("'=DANGER"), "Details state limits and CSV neutralizes spreadsheet formulas");

        var stats = new SessionStatistics();
        var sensor = new SensorReading("/gpu-nvidia/0/load/3", "GPU", "Video", "Load", 12, "%");
        var sample = new LiveSample(DateTimeOffset.UtcNow, 10, 20, 2, 10, 1, 2, [],
            [sensor, sensor, sensor with { Name = "Bus", Value = 32 }]);
        stats.Add(sample);
        Check(stats.Snapshot().Single(s => s.Name == "Video").Count == 1 &&
            stats.Snapshot().Single(s => s.Name == "Bus").Average == 32, "Statistics coalesce identical duplicates and keep colliding channels separate");
        stats.Add(sample with { Sensors = [sensor, sensor with { Value = 99 }] });
        Check(stats.Snapshot().Single(s => s.Name == "Video").Count == 1 && stats.Warning.Contains("widerspruechlich"),
            "Contradictory samples do not become false averages");
        stats.Reset();
        Check(stats.Snapshot().Count == 0 && stats.Warning.Length == 0, "Statistics reset clears warning and data");

        var folder = Path.Combine(Path.GetTempPath(), "Aether-workflows-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var bundleFile = Path.Combine(folder, "packages.json");
            File.WriteAllText(bundleFile, PackageCenter.SerializeBundle(packages), new UTF8Encoding(false));
            Check(PackageCenter.ReadBundle(bundleFile).Packages.Count == 2, "Bounded file reader reads actual UTF8 bundle");
            File.WriteAllText(bundleFile, new string('x', PackageCenter.MaxBundleBytes + 1));
            Reject(() => PackageCenter.ReadBundle(bundleFile), "Oversized bundle file accepted");
            var history = new LocalHistory(folder);
            history.Append(sample);
            history.Append(sample with { Timestamp = sample.Timestamp.AddSeconds(2), Sensors = [sensor, sensor with { Value = 99 }] });
            var report = HistoryReader.Read(Directory.GetFiles(folder, "metrics-*.jsonl").Single(), CancellationToken.None);
            Check(report.Series.Single(s => s.Label.EndsWith(" / Video")).Points.Count == 1 &&
                report.Series.Single(s => s.Label.EndsWith(" / Bus")).Points.Single().Value == 32 &&
                report.Issues.Any(s => s.Contains("widerspruechlich")), "Stored logs use same collision/conflict policy as charts/statistics");
            var exe = Path.Combine(folder, "Wireshark.exe");
            var pcap = Path.Combine(folder, "capture with spaces.pcapng");
            File.WriteAllText(exe, "fixture only / never executed");
            File.WriteAllText(pcap, "fixture only / never parsed");
            var launch = CaptureHandoff.Wireshark(exe, pcap);
            Check(!launch.UseShellExecute && launch.ArgumentList.SequenceEqual(new[] { "-r", pcap }) &&
                !launch.ArgumentList.Contains("-k"), "Wireshark handoff passes file as one argument without starting a capture or shell");
            Reject(() => CaptureHandoff.Wireshark(exe, bundleFile), "Non-capture file handoff accepted");
            Reject(() => CaptureHandoff.Wireshark(bundleFile, pcap), "Unrelated executable handoff accepted");
            var manager = Path.Combine(folder, "UniGetUI.exe");
            File.WriteAllText(manager, "fixture only / never executed");
            Check(!PackageCenter.ExternalManager(manager).UseShellExecute &&
                PackageCenter.ExternalManager(manager).ArgumentList.Count == 0, "External package manager starts without hidden package actions");
            Reject(() => PackageCenter.ExternalManager(exe), "Wrong package manager executable accepted");
            var scanRoot = Directory.CreateDirectory(Path.Combine(folder, "scan")).FullName;
            File.WriteAllBytes(Path.Combine(scanRoot, "small.bin"), new byte[10]);
            File.WriteAllBytes(Path.Combine(scanRoot, "large.bin"), new byte[200]);
            var storage = StorageAnalysis.Scan(scanRoot, 100, CancellationToken.None);
            Check(storage.Files.Single().Bytes == 200 && storage.Inspected == 2 && !storage.Limited &&
                File.ReadAllBytes(Path.Combine(scanRoot, "small.bin")).Length == 10, "Storage scan measures exact threshold without changing files");
            for (var i = 0; i < StorageAnalysis.MaxResults + 2; i++)
                File.WriteAllBytes(Path.Combine(scanRoot, $"candidate-{i}.bin"), new byte[100]);
            storage = StorageAnalysis.Scan(scanRoot, 100, CancellationToken.None);
            Check(storage.Files.Count == StorageAnalysis.MaxResults && storage.Limited &&
                storage.Files[0].Bytes == 200, "Storage results really cap at 1000 and retain largest file");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var aborted = false;
            try { StorageAnalysis.Scan(scanRoot, 0, cancelled.Token); }
            catch (OperationCanceledException) { aborted = true; }
            Check(aborted, "Storage cancellation is explicit rather than a success-shaped empty report");
        }
        finally { Directory.Delete(folder, recursive: true); }

        var model = new CockpitModel();
        model.Apply(sample, true);
        model.StatisticsFilter = "Video";
        Check(model.StatisticsView.Cast<Statistic>().Single().Name == "Video" && model.Statistics.Count > 1, "Statistics filter preserves underlying samples");
        var first = new CleanupCandidate(@"C:\fixture\first.tmp", 10, DateTime.UtcNow) { Selected = true };
        var second = new CleanupCandidate(@"C:\fixture\second.tmp", 20, DateTime.UtcNow);
        model.CleanupFiles.Add(first); model.CleanupFiles.Add(second);
        model.CleanupFilter = "second";
        Check(model.CleanupView.Cast<CleanupCandidate>().Single() == second && first.Selected,
            "Cleaner filter never silently deselects or authorizes hidden files");
        Check(ControlCenterWindow.FilterStartup([new("HKCU", "One", "program.exe --example", true),
            new("HKLM", "Two", "other.exe", false)], "--example").Count == 1, "Autostart filter searches full command");
        var jobs = new OperationHistory();
        var active = jobs.Add("active"); active.State = "LAEUFT";
        for (var i = 0; i < 250; i++) jobs.Add("finished").State = "ABGESCHLOSSEN";
        Check(jobs.Entries.Count == OperationHistory.Capacity && jobs.Entries.Contains(active), "Bounded job history never discards active work");

        var center = new ControlCenterWindow(model);
        try
        {
            var grid = (DataGrid)center.FindName("JobsGrid");
            var entries = (System.Collections.ObjectModel.ObservableCollection<OperationEntry>)grid.ItemsSource;
            var calls = new List<string>();
            var journal = new List<string>();
            var failed = false;
            try
            {
                center.RunPackageQueueAsync("upgrade", packages, CancellationToken.None,
                    (row, _) => { calls.Add(row.Id); return Task.FromResult(new CommandResult(row.Id == "Git.Git" ? 5 : 0, "", "")); },
                    (action, _) => journal.Add(action)).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && calls.SequenceEqual(packages.Select(p => p.Id)) && journal.Count == 4 &&
                entries.Any(j => j.State == "FEHLGESCHLAGEN") && entries.Any(j => j.State == "ABGESCHLOSSEN"),
                "Actual package queue reports per-package failure, continues serially and journals each outcome with mocked executor");
            using var cancel = new CancellationTokenSource();
            calls.Clear();
            try
            {
                center.RunPackageQueueAsync("install", packages, cancel.Token,
                    (row, _) => { calls.Add(row.Id); cancel.Cancel(); throw new OperationCanceledException(cancel.Token); },
                    (_, _) => { }).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { count++; }
            Check(calls.Count == 1 && entries.Any(j => j.State == "ABGEBROCHEN") &&
                entries.Any(j => j.State == "NICHT AUSGEFUEHRT"), "Queue cancellation never starts remaining packages or disguises partial actions");
            var executed = false;
            try
            {
                center.RunPackageQueueAsync("install", packages, CancellationToken.None,
                    (_, _) => { executed = true; return Task.FromResult(new CommandResult(0, "", "")); },
                    (_, _) => throw new IOException("fixture journal unavailable")).GetAwaiter().GetResult();
            }
            catch (IOException) { count++; }
            Check(!executed && !entries.Any(j => j.State is "LAEUFT" or "NICHT GESTARTET"), "Unavailable journal stops before package execution and resolves waiting state");
            var before = entries.Count;
            center.DisplayReadOnlyAsync("private fixture", _ => Task.FromResult("private result")).GetAwaiter().GetResult();
            Check(entries.Count == before, "Private/sensitive operation is absent from job history");
        }
        finally { center.Close(); Pump(); }
        return count;
    }

    public static int Ui(string? screenshotPrefix)
    {
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new InvalidOperationException(message); }
        var scripts = new[] { "install", "upgrade", "uninstall", "show" }.Select(verb =>
            PackageCenter.TargetCommandScript(verb, new("Fixture", "Publisher.Package", "", "", "winget"))).ToArray();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(scripts)));
        var parsed = Task.Run(() => CommandRunner.PowerShellAsync(
            "$scripts=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "')) | ConvertFrom-Json; " +
            "foreach($script in $scripts){[void][scriptblock]::Create($script)}; 'PACKAGE SCRIPTS PARSED'", CancellationToken.None)).GetAwaiter().GetResult();
        Check(parsed.Contains("PACKAGE SCRIPTS PARSED"), "Real Windows PowerShell parser accepts all source-bound scripts without executing them");
        var tasks = new TaskCenterWindow { ShowInTaskbar = false };
        try
        {
            tasks.Show(); Pump();
            Check(tasks.Results.Items.Count == TaskCatalog.All.Count && tasks.SearchBox.IsKeyboardFocused, "Task center opens with keyboard search focus");
            tasks.SearchBox.Text = "software updates";
            Pump();
            Check(tasks.Results.Items.Cast<CockpitTask>().Any(t => t.Destination == "Pakete"), "Visible task search really filters workflow list");
            tasks.SearchBox.Text = "";
            tasks.CategoryBox.SelectedItem = "Netzwerk";
            Pump();
            Check(tasks.Results.Items.Count >= 3 && tasks.Results.Items.Cast<CockpitTask>().All(t => t.Category == "Netzwerk"),
                "Visible category selection narrows results");
            foreach (var size in new[] { new Size(1040, 720), new Size(780, 560) })
            {
                tasks.Width = size.Width; tasks.Height = size.Height; Pump(); tasks.UpdateLayout();
                Check(tasks.Results.ActualWidth >= 260 && tasks.Results.ActualHeight > 160 &&
                    Descendants(tasks).OfType<Button>().Single().IsEnabled, "Task list and action remain usable at supported window sizes");
            }
            tasks.Width = 1040; tasks.Height = 720; Pump();
            if (screenshotPrefix is not null) ModuleTests.SaveScreenshot(tasks, screenshotPrefix + "-arbeitsplatz.png");
            tasks.SearchBox.Text = "zznichtvorhanden";
            Pump();
            Check(tasks.Results.Items.Count == 0 && !Descendants(tasks).OfType<Button>().Single().IsEnabled, "No-result state disables opening, not a fake default action");
        }
        finally { tasks.Close(); }
        var model = new CockpitModel();
        var center = new ControlCenterWindow(model) { ShowInTaskbar = false };
        try
        {
            center.Show(); center.SelectModule("Pakete"); Pump();
            var grid = (DataGrid)center.FindName("PackagesGrid");
            grid.ItemsSource = new[] { new WingetRow("Git", "Git.Git", "2.0", "2.1", "winget") };
            center.UpdateLayout();
            Check(grid.ActualHeight >= 130 && Descendants(center).OfType<Button>().Any(b => Equals(b.Content, "Paketdetails lesen") && b.IsVisible),
                "Package details/transfer actions and list fit actual WPF layout");
            if (screenshotPrefix is not null) ModuleTests.SaveScreenshot(center, screenshotPrefix + "-pakete.png");
            center.SelectModule("Autostart"); Pump();
            var startup = (DataGrid)center.FindName("StartupEntries");
            startup.ItemsSource = new[] { new StartupEntry("HKCU", "Fixture", "example.exe --safe", true) };
            startup.SelectedIndex = 0; Pump();
            Check(((TextBox)center.FindName("StartupDetails")).Text.Contains("example.exe --safe") && startup.IsReadOnly,
                "Autostart selection exposes full exact command without editing it");
            Check(TaskCatalog.All.Where(t => t.Command is not null).All(command => command.Destination == "Wartung"),
                "Command search targets actual catalog, not direct execution");
            model.Settings = model.Settings with { ExpertMode = false };
            center.FocusCatalogCommand("sfc /scannow");
            Check(((ListBox)center.FindName("CommandList")).Items.Count == 0 && !model.Settings.ExpertMode,
                "Task center command handoff never unlocks expert-only commands");
            center.SelectModule("Auftraege"); Pump();
            Check(((DataGrid)center.FindName("JobsGrid")).IsVisible, "Job history is a real discoverable module");
        }
        finally { center.Close(); Pump(); }
        using var watch = new NetworkWatch(() => model.Settings, _ => { });
        watch.RecentPackets.Add(new(DateTimeOffset.Now, "192.0.2.1", "192.0.2.53", "DNS / UDP", 50000, 53, 90, false, true));
        watch.RecentPackets.Add(new(DateTimeOffset.Now, "192.0.2.1", "192.0.2.80", "TCP", 50001, 443, 100, true, false));
        var network = new NetworkMonitorWindow(watch, model, _ => throw new InvalidOperationException("UI validation must not save settings")) { ShowInTaskbar = false };
        try
        {
            network.Show(); Pump();
            var grid = Descendants(network).OfType<DataGrid>().Single();
            Check(grid.Items.Count == 2 && !watch.IsCapturing, "Network UI renders synthetic packets without capture/monitoring");
            var protocol = Descendants(network).OfType<ComboBox>().Single(c => c.ItemsSource == PacketFilter.Protocols);
            protocol.SelectedItem = "DNS";
            Descendants(network).OfType<Button>().Single(b => Equals(b.Content, "Filter anwenden")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            grid.SelectedIndex = 0; Pump();
            if (screenshotPrefix is not null) ModuleTests.SaveScreenshot(network, screenshotPrefix + "-netzwerk.png");
            var tab = (TabItem)Descendants(network).OfType<TabControl>().Single().Items[0];
            var live = (StackPanel)((ScrollViewer)tab.Content).Content;
            var details = live.Children.OfType<TextBlock>().Last();
            Check(grid.Items.Count == 1 && details.Text.Contains("128-Byte-Mitschnitt") && details.IsVisible,
                $"Visible filter/details wired: {grid.Items.Count} rows; selected detail: {details.Text}; visible: {details.IsVisible}");
        }
        finally { network.Close(); Pump(); }
        var storageWindow = new StorageAnalysisWindow { ShowInTaskbar = false };
        try
        {
            storageWindow.Show(); Pump();
            Check(Descendants(storageWindow).OfType<TextBlock>().Any(t => t.Text == "Noch kein Ordner gelesen.") &&
                Descendants(storageWindow).OfType<DataGrid>().Single().Items.Count == 0,
                "Opening storage analysis never starts a scan");
        }
        finally { storageWindow.Close(); Pump(); }
        var warning = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Publisher.Package{i} / Quelle: winget"));
        var confirmation = new ActionConfirmationWindow("Fixture / never executed", warning) { ShowInTaskbar = false };
        try
        {
            confirmation.Show(); Pump();
            Check(confirmation.Preview.Text == warning && !confirmation.Confirm.IsEnabled &&
                confirmation.Preview.VerticalScrollBarVisibility == ScrollBarVisibility.Auto,
                "Confirmation contains all 100 IDs in scrollable preview and defaults to no consent");
            confirmation.Consent.IsChecked = true;
            Check(confirmation.Confirm.IsEnabled, "Explicit consent enables confirmation without executing anything");
        }
        finally { confirmation.Close(); Pump(); }
        var main = new MainWindow();
        try
        {
            var pages = (TabControl)main.FindName("Pages");
            var modules = new ControlCenterWindow(model);
            try
            {
                var advanced = (TabControl)modules.FindName("Modules");
                Check(TaskCatalog.All.Where(t => t.Surface == "Hauptfenster").All(t =>
                    pages.Items.Cast<TabItem>().Any(p => Equals(p.Header, t.Destination))) &&
                    TaskCatalog.All.Where(t => t.Surface == "Systemkontrolle").All(t =>
                    advanced.Items.Cast<TabItem>().Any(p => Equals(p.Header, t.Destination))),
                    "Every task route names a real compiled destination");
            }
            finally { modules.Close(); Pump(); }
            var root = (FrameworkElement)main.Content;
            main.Content = null;
            var host = new Window { Content = root, DataContext = main.Model, Width = 1100, Height = 760, ShowInTaskbar = false };
            host.Resources.MergedDictionaries.Add(main.Resources);
            try
            {
                pages.SelectedItem = pages.Items.Cast<TabItem>().Single(t => Equals(t.Header, "Cleaner"));
                main.Model.CleanupFiles.Clear();
                main.Model.CleanupFiles.Add(new(@"C:\fixture\visible.tmp", 10, DateTime.UtcNow));
                main.Model.CleanupFiles.Add(new(@"C:\fixture\hidden.tmp", 20, DateTime.UtcNow) { Selected = true });
                main.Model.CleanupFilter = "visible";
                host.Show(); Pump();
                var navigation = (StackPanel)((Button)main.FindName("CockpitNav")).Parent;
                var navigationButtons = navigation.Children.OfType<Button>().Concat(navigation.Children.OfType<StackPanel>()
                    .SelectMany(panel => panel.Children.OfType<Button>())).ToArray();
                Check(navigationButtons.All(button => button.Content is string text &&
                    new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch),
                        button.FontSize, Brushes.White, VisualTreeHelper.GetDpi(button).PixelsPerDip).WidthIncludingTrailingWhitespace <=
                    button.ActualWidth - button.Padding.Left - button.Padding.Right - button.BorderThickness.Left - button.BorderThickness.Right + 1),
                    "Every primary/quick navigation caption fits its actual sidebar content width without clipping");
                var visible = Descendants(host).OfType<Button>().Single(b => Equals(b.Content, "Sichtbare ankreuzen"));
                visible.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                var status = (TextBlock)main.FindName("CleanupSelectionStatus");
                Check(main.Model.CleanupFiles.All(f => f.Selected) && status.Text.Contains("1 durch Filter verborgen") &&
                    ((DataGrid)main.FindName("CleanupGrid")).ActualHeight > 70 &&
                    !((DataGrid)main.FindName("CleanupGrid")).AutoGenerateColumns,
                    "Actual cleaner selection keeps hidden selection explicit and remains usable at 1100x760 without starting main services");
                Descendants(host).OfType<Button>().Single(b => Equals(b.Content, "Keine ankreuzen")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(main.Model.CleanupFiles.All(f => !f.Selected), "Clear selection includes filtered-out candidates");
                if (screenshotPrefix is not null) ModuleTests.SaveScreenshot(host, screenshotPrefix + "-cleaner.png");
            }
            finally { host.Close(); Pump(); }
        }
        finally { main.RequestExit(); Pump(); }
        return count;
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
