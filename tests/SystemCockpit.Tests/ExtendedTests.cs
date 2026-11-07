using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using SystemCockpit;
using SystemCockpit.Services;

internal static class ExtendedTests
{
    public static int Run()
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException) { assertions++; return; }
            throw new InvalidOperationException(message);
        }
        var fixture = Path.Combine(Path.GetTempPath(), "AetherExtended-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var settings = new CockpitSettings();
            settings.Validate();
            Check(settings.RecordIntervalSeconds == 5, "Default log cadence 5 seconds");
            Reject(() => (settings with { CpuWarningC = double.NaN }).Validate(), "NaN CPU threshold accepted");
            Reject(() => (settings with { VoltageTolerancePercent = 0 }).Validate(), "Invalid ATX tolerance accepted");
            Reject(() => (settings with { RecordIntervalSeconds = 1 }).Validate(), "Unbounded sampling accepted");
            Reject(() => (settings with { SmartctlPath = "relative.exe" }).Validate(), "Relative executable accepted");
            Reject(() => (settings with { OverlayDockSide = "top" }).Validate(), "Unsupported overlay dock side accepted");
            Reject(() => (settings with { OverlayVerticalOffset = double.NaN }).Validate(), "Invalid overlay position accepted");
            Reject(() => (settings with { SidebarDockSide = "bottom" }).Validate(), "Unsupported sidebar dock side accepted");
            Reject(() => (settings with { SidebarVerticalOffset = double.PositiveInfinity }).Validate(), "Invalid sidebar vertical position accepted");
            Reject(() => (settings with { SidebarHorizontalOffset = -0.1 }).Validate(), "Invalid sidebar horizontal position accepted");
            Reject(() => (settings with { SidebarShortcuts = [new("Relative", "notepad.exe", "App")] }).Validate(),
                "Relative sidebar shortcut accepted");
            var localManager = settings with { ManagerServerUrl = "http://127.0.0.1:5187" };
            localManager.Validate();
            Check(JsonSerializer.Deserialize<CockpitSettings>(JsonSerializer.Serialize(localManager))!
                    .ManagerServerUrl == localManager.ManagerServerUrl,
                "Local manager URL survives settings round-trip");
            (settings with { ManagerServerUrl = "https://manager.example:5187" }).Validate();
            Reject(() => (settings with { ManagerServerUrl = "http://manager.example:5187" }).Validate(),
                "Remote manager URL accepted without HTTPS");
            Reject(() => (settings with { ManagerServerUrl = "https://manager.example/path" }).Validate(),
                "Manager URL with an API path accepted");
            Reject(() => (settings with { ManagerReportingEnabled = true }).Validate(),
                "Central reporting enabled without a server URL");
            Reject(() => (settings with
            {
                ManagerServerUrl = "http://127.0.0.1:5187",
                ManagerReportingEnabled = true
            }).Validate(), "Server reporting enabled in standalone mode");
            var oldClientSettingsPath = Path.Combine(fixture, "old-client-settings.json");
            File.WriteAllText(oldClientSettingsPath,
                """{"ManagerServerUrl":"http://127.0.0.1:5187","ManagerReportingEnabled":true}""");
            Check(CockpitSettings.Load(oldClientSettingsPath).ManagerClientMode,
                "Existing opted-in manager settings migrate to client mode");
            var missingShortcutPath = Path.Combine(fixture, "not-installed.exe");
            var staleSettings = settings with
            {
                SidebarShortcuts = [new("Missing", missingShortcutPath, "App")]
            };
            staleSettings.Validate();
            Check(JsonSerializer.Deserialize<CockpitSettings>(JsonSerializer.Serialize(staleSettings))!
                    .SidebarShortcuts.Single().Path == missingShortcutPath,
                "Missing sidebar shortcuts remain valid and serializable");
            var diagnostic = SensorDiagnostics.Evaluate(
            [
                new("/mainboard/voltage/0", "Board", "+12V", "Voltage", 11.0, "V"),
                new("/mainboard/voltage/1", "Board", "VBat", "Voltage", 2.4, "V"),
                new("/mainboard/voltage/2", "Board", "VCore", "Voltage", 1.2, "V"),
                new("/intelcpu/factor/0", "CPU", "Thermal Throttling", "Factor", 1, ""),
                new("/nvme/temperature/0", "SSD", "Temperature", "Temperature", 70, "C")
            ], settings);
            Check(diagnostic.Count(c => c.State == "warning") == 4, "Exact ATX/CMOS/thermal/storage thresholds");
            Check(diagnostic.Single(c => c.Name.Contains("VCore")).State == "info", "No guessed VCore threshold");
            Check(SensorDiagnostics.Evaluate([], settings).Single().State == "unknown", "No inferred throttling status");
            var native = new LiveSample(DateTimeOffset.UtcNow, 20, 30, 10, 32, 1, 1,
                [new("C:", "free", "ok", "not parsed", 15)], []);
            var model = new CockpitModel { Settings = settings with { FreeSpaceWarningPercent = 20 } };
            model.Apply(native, false);
            Check(model.Volumes.Single().State == "warning", "Free-space threshold uses numeric percent not display text");
            model.RecordStatistics = false;
            var countBeforePause = model.Statistics.Single(s => s.Name == "CPU Gesamt").Count;
            model.Apply(native with { CpuPercent = 90 }, false);
            Check(model.Statistics.Single(s => s.Name == "CPU Gesamt").Count == countBeforePause, "Paused statistics do not include later frames");
            model.ResetStatistics();
            Check(model.RecordStatistics && model.Statistics.Count == 0, "New measurement session resumes");

            var device = new SmartDevice("/dev/nvme0", "nvme", "Fixture");
            Check(SmartService.BuildOperation(device, "short", "").SequenceEqual(
                new[] { "-t", "short", "-j", "-d", "nvme", "/dev/nvme0" }), "SMART short arguments exact");
            Check(SmartService.BuildOperation(device, "selective", "10-100")[1] == "select,10-100", "Selective LBA shape");
            Reject(() => SmartService.BuildOperation(device, "selective", "100-10"), "Reversed LBA accepted");
            Reject(() => SmartService.BuildOperation(device, "aam", "127"), "AAM invalid value accepted");
            Reject(() => SmartService.BuildOperation(device, "apm", "255"), "APM invalid value accepted");
            Reject(() => SmartService.BuildOperation(device with { Name = "--danger" }, "short", ""), "SMART option injection");
            Reject(() => SmartService.BuildOperation(device, "format", ""), "Uncatalogued SMART action");
            var smart = SmartService.Summarize("""
                {"model_name":"Fixture","smart_status":{"passed":true},
                "nvme_smart_health_information_log":{"media_errors":0,"num_err_log_entries":2,"percentage_used":4}}
                """, 64);
            Check(smart.Contains("PASSED") && smart.Contains("kein ATA-Sektorzaehler") && smart.Contains("Bitmaske: 64"),
                "NVMe interpretation and smartctl health bitmask preserved");

            Check(NetworkTools.ParseLocalRange("192.168.2.123/24").Count == 254, "LAN /24 capped at 254 hosts");
            Check(NetworkTools.ParseLocalRange("10.0.0.1/30").Count == 2, "LAN /30 host count");
            Reject(() => NetworkTools.ParseLocalRange("8.8.8.8/24"), "Public scanning accepted");
            Reject(() => NetworkTools.ParseLocalRange("192.168.0.0/16"), "Large LAN scan accepted");
            Reject(() => NetworkTools.ParseLocalRange("127.0.0.1/24"), "Loopback LAN scan accepted");
            var oui = Path.Combine(fixture, "oui.csv");
            File.WriteAllText(oui, "Registry,Assignment,Organization Name,Organization Address\nMA-L,001122,\"Fixture, Inc.\",Somewhere\n");
            Check(NetworkTools.ReadOui(oui)["001122"] == "Fixture, Inc.", "OUI manufacturer uses parsed assignment not a guess");
            Check(NetworkTools.ReadOui("").Count == 0, "Missing OUI file remains explicitly unconfigured");
            Check(SystemActions.Catalog.Single(a => a.Id == "dism-repair").Destructive, "DISM repair gated");
            Check(SystemActions.Catalog.Single(a => a.Id == "firmware").Arguments.SequenceEqual(
                new[] { "/r", "/fw", "/t", "30" }), "UEFI restart has time for cancellation");
            Check(SystemActions.Catalog.Single(a => a.Id == "chkdsk").Arguments.Contains("/scan") &&
                !SystemActions.Catalog.Single(a => a.Id == "chkdsk").Arguments.Contains("/f"), "No implicit destructive CHKDSK");
            Check(SystemActions.SecretScript("wifi", "test'; write-host injected; '").Contains("name='test''; write-host injected; '''"),
                "WLAN profile escaped as data, not script");
            Reject(() => SystemActions.SecretScript("wifi", ""), "Empty secret profile accepted");

            var logDir = Path.Combine(fixture, "logs");
            var history = new LocalHistory(logDir);
            history.Append(native);
            history.Append(native with { Timestamp = native.Timestamp.AddSeconds(5), CpuPercent = 40 });
            var path = Directory.GetFiles(logDir).Single();
            var report = HistoryReader.Read(path, CancellationToken.None);
            Check(report.Issues.Count == 0 && report.Series.Single(s => s.Id == "native/cpu").Points.Count == 2,
                "Production history format roundtrips into viewer");
            Check(HistoryReader.Compare(report, report).Contains("AVG 30"), "Log comparison averages actual values");
            File.AppendAllText(path, "{ broken json }\n");
            Check(HistoryReader.Read(path, CancellationToken.None).Issues.Count == 1, "Corrupt log line explicitly reported");
            var csv = Path.Combine(fixture, "frames.csv");
            File.WriteAllText(csv, "Application,SwapChainAddress,CPUFrameTime\nGame.exe,0x1,10\nGame.exe,0x1,20\nGame.exe,0x2,40\n");
            var frames = FrameTelemetry.Read(csv);
            Check(frames.Count == 2 && Math.Abs(frames.Single(f => f.Swapchain == "0x1").AverageFps - 1000d / 15) < .001,
                "FPS from frame intervals, swapchains not mixed");
            Check(frames.Single(f => f.Swapchain == "0x1").OnePercentLowFps == 50, "Slowest 1 percent mean definition");
            File.WriteAllText(csv, "Application,SwapChainAddress,CPUFrameTime\nGame.exe,0x1,NaN\n");
            Reject(() => FrameTelemetry.Read(csv), "NaN frame time accepted");
            var capture = FrameTelemetry.CaptureArguments("Game.exe", 10, csv, "Aether-fixture");
            Check(capture.Contains("--no_track_input") && capture.Contains("--v2_metrics") &&
                !capture.Contains("--restart_as_admin") && !capture.Contains("--stop_existing_session"),
                "Frame capture excludes input and never elevates/interferes implicitly");
            Reject(() => FrameTelemetry.CaptureArguments("Game.exe", 10000, csv, "test"), "Unbounded capture duration");
            Reject(() => FrameTelemetry.CaptureArguments(@"C:\Game.exe", 10, csv, "test"), "Process path not name");

            var window = new ControlCenterWindow(model);
            var tabs = (TabControl)window.FindName("Modules");
            Check(tabs.Items.Count == 10 && tabs.Items.Cast<TabItem>().Any(t => Equals(t.Header, "Auftraege")),
                "Existing advanced control tabs and operation history compiled");
            Check(tabs.Items.Cast<TabItem>().Select(t => (string)t.Header)
                    .Intersect(["Pakete", "Spuren"]).Count() == 2, "Package center and shellbag modules present");
            Check(((TextBox)window.FindName("Output")).IsReadOnly, "Advanced output read-only");
            var terminal = (TextBox)window.FindName("Output");
            Check(terminal.Background is System.Windows.Media.SolidColorBrush black && black.Color == System.Windows.Media.Colors.Black,
                "CRT terminal uses black background");
            Check(terminal.Foreground is System.Windows.Media.SolidColorBrush green && green.Color.G > green.Color.R &&
                green.Color.G > green.Color.B, "CRT terminal uses green phosphor text");
            Check(window.FindName("CrtEffects") is CheckBox && window.FindName("FollowOutput") is CheckBox,
                "CRT and autoscroll controls available");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var input = (TextBox)window.FindName("CommandInput");
            var commandList = (ListBox)window.FindName("CommandList");
            Check(AdminCommandCatalog.All.Count >= 90 &&
                AdminCommandCatalog.All.Select(c => c.Command).Distinct().Count() == AdminCommandCatalog.All.Count,
                "Broad unique administration command catalog");
            Check(AdminCommandCatalog.All.All(c => c.Description.Length > 0 && c.Impact.Length > 0 &&
                c.Parameters.All(p => p.Text.Length > 0 && p.Description.Length > 0)),
                "Every catalog command and parameter has an explanation");
            commandList.SelectedItem = AdminCommandCatalog.All.Single(c => c.Command == "sfc /verifyonly");
            Check(input.Text == "sfc /verifyonly" &&
                ((TextBlock)window.FindName("CommandDescription")).Text.Contains("CBS.log"),
                "Catalog selection copies editable command and explanation without execution");
            input.Select(4, input.Text.Length - 4);
            typeof(ControlCenterWindow).GetMethod("ParameterClicked", flags)!.Invoke(window,
                [new Button { Tag = new AdminParameter("/?", "Hilfe") }, new System.Windows.RoutedEventArgs()]);
            Check(input.Text == "sfc /?", "Parameter click replaces input selection without execution");
            ((TextBox)window.FindName("CommandSearch")).Text = "Get-EventLog -List";
            Check(commandList.Items.Count == 1 && ((AdminCommand)commandList.Items[0]).Command == "Get-EventLog -List",
                "Catalog search filters to the requested administration command");
            ((TextBox)window.FindName("CommandSearch")).Clear();
            var runAction = typeof(ControlCenterWindow).GetMethod("RunAsync", flags)!;
            var append = typeof(ControlCenterWindow).GetMethod("AppendTerminal", flags)!;
            Func<CancellationToken, Task<string>> failedAction = _ =>
            {
                append.Invoke(window, ["fixture partial stdout\n"]);
                return Task.FromException<string>(new InvalidOperationException("fixture failure"));
            };
            var failed = (Task)runAction.Invoke(window, ["Fixture", failedAction, null, false, false, false])!;
            Check(failed.IsCompletedSuccessfully && terminal.Text.Contains("fixture partial stdout") &&
                terminal.Text.Contains("[FEHLER] fixture failure"), "Failure retains partial terminal transcript");
            Func<CancellationToken, Task<string>> cancelledAction = _ =>
            {
                append.Invoke(window, ["fixture before cancellation\n"]);
                return Task.FromCanceled<string>(new CancellationToken(true));
            };
            var cancelled = (Task)runAction.Invoke(window, ["Fixture", cancelledAction, null, false, false, false])!;
            Check(cancelled.IsCompletedSuccessfully && terminal.Text.Contains("fixture before cancellation") &&
                terminal.Text.Contains("[ABBRUCH]"), "Cancellation retains partial terminal transcript");
            // Einzeiliger Fortschrittsbalken: \r ueberschreibt die aktuelle Zeile statt neue anzuhaengen.
            terminal.Clear();
            append.Invoke(window, ["[BEFEHL] dism\n[===     10.0%     ===]"]);
            append.Invoke(window, ["\r[===     63.0%     ===]"]);
            append.Invoke(window, ["\r[===     63.5%"]);
            append.Invoke(window, ["     ===]\r[=== 100.0% ===]\nDer Vorgang wurde erfolgreich beendet.\n"]);
            Check(!terminal.Text.Contains("10.0%") && !terminal.Text.Contains("63.0%") && !terminal.Text.Contains("63.5%") &&
                terminal.Text.Contains("[BEFEHL] dism\n[=== 100.0% ===]\nDer Vorgang wurde erfolgreich beendet."),
                "Carriage-return progress refreshes a single terminal line in place");
            append.Invoke(window, ["naechste Zeile bleibt\n"]);
            Check(terminal.Text.Contains("erfolgreich beendet.\nnaechste Zeile bleibt"),
                "Completed lines stay untouched after progress updates");
            terminal.Clear();
            Check(((ComboBox)window.FindName("SmartOperation")).Items.Count == 7, "All SMART operations exposed");
            Check(((ComboBox)window.FindName("MaintenanceActions")).Items.Count >= 7, "Maintenance catalog exposed");
            var tabStyle = (System.Windows.Style)window.Resources[typeof(TabItem)];
            Check(tabStyle.Setters.OfType<System.Windows.Setter>().Any(s =>
                s.Property == Control.ForegroundProperty && s.Value is System.Windows.Media.SolidColorBrush brush &&
                brush.Color == System.Windows.Media.Color.FromRgb(50, 213, 255)), "Advanced tab foreground readable on neon dark background");
        }
        finally { Directory.Delete(fixture, recursive: true); }
        return assertions;
    }

    public static int CatalogUi()
    {
        var window = new ControlCenterWindow(new CockpitModel());
        window.Show();
        try
        {
            var tabs = (TabControl)window.FindName("Modules");
            tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => (string)t.Header == "Wartung");
            var commands = (ListBox)window.FindName("CommandList");
            var input = (TextBox)window.FindName("CommandInput");
            var selected = AdminCommandCatalog.All.Single(c => c.Command == "sfc /verifyonly");
            commands.SelectedItem = selected;
            commands.ScrollIntoView(selected);
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var item = commands.ItemContainerGenerator.ContainerFromItem(selected) as ListBoxItem
                ?? throw new InvalidOperationException("Selected catalog item was not rendered");
            Button? FindButton(System.Windows.DependencyObject parent)
            {
                if (parent is Button button) return button;
                for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var found = FindButton(System.Windows.Media.VisualTreeHelper.GetChild(parent, i));
                    if (found is not null) return found;
                }
                return null;
            }
            var copy = FindButton(item) ?? throw new InvalidOperationException("Visible catalog command button missing");
            input.Text = "edited fixture";
            copy.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            if (input.Text != "sfc /verifyonly") throw new InvalidOperationException("Repeated click must copy command again");
            if (input.ActualWidth < 150 || input.ActualHeight < 20 || commands.ActualHeight < 150)
                throw new InvalidOperationException("Catalog/input unusable in actual window layout");
            input.Clear();
            input.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                System.Windows.PresentationSource.FromVisual(input), 0, System.Windows.Input.Key.Enter)
                { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
            if (!((TextBlock)window.FindName("Status")).Text.Contains("Kein Befehl"))
                throw new InvalidOperationException("Enter key does not route to command validation");
            var parameters = (ItemsControl)window.FindName("CommandParameters");
            window.UpdateLayout();
            var parameterButton = FindButton(parameters) ?? throw new InvalidOperationException("Visible parameter buttons missing");
            input.Text = "sfc";
            input.CaretIndex = input.Text.Length;
            parameterButton.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            if (input.Text != "sfc /verifyonly") throw new InvalidOperationException("Rendered parameter button not wired");
            tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => (string)t.Header == "Schluessel");
            if (input.Text.Length != 0) throw new InvalidOperationException("Sensitive tab retains command input");
            Console.WriteLine($"CATALOG UI: {AdminCommandCatalog.All.Count} commands; actual WPF layout, repeat copy, rendered parameter click, Enter routing and private input clearing verified in non-elevated test host");
            return 5;
        }
        finally
        {
            window.Close();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    public static async Task<int> ToolsAsync()
    {
        var smart = SmartService.DetectExecutable();
        var present = FrameTelemetry.DetectExecutable();
        if (!File.Exists(smart) || !File.Exists(present)) throw new FileNotFoundException("Portable measurement tools missing.");
        var smartVersion = await CommandRunner.RunAsync(smart, ["--version"], TimeSpan.FromSeconds(15), CancellationToken.None);
        if (smartVersion.ExitCode != 0 || !smartVersion.Output.Contains("smartctl 7.5")) throw new InvalidOperationException("SMART version validation failed");
        var presentVersion = await CommandRunner.RunAsync(present, ["--help"], TimeSpan.FromSeconds(15), CancellationToken.None);
        // The standalone 2.6 Console emits its usage/version with exit 1 for --help; this does not start ETW.
        if (presentVersion.ExitCode is not (0 or 1) || !(presentVersion.Output + presentVersion.Error).Contains("PresentMon 2.6.0"))
            throw new InvalidOperationException("PresentMon console validation failed");
        Console.WriteLine("TOOLS: smartctl 7.5 and PresentMon 2.6.0 verified; no self-test/capture executed");
        var readOnly = await CommandRunner.PowerShellAsync("'Aether transport verified'", CancellationToken.None);
        if (!readOnly.Contains("Aether transport verified")) throw new InvalidOperationException("PowerShell transport failed");
        var certs = CertificateDiagnostics.Read(CancellationToken.None);
        if (certs.Count == 0 || certs.Any(c => c.State is not ("info" or "warning" or "unknown")))
            throw new InvalidOperationException("Offline certificate diagnosis has invalid result shape");
        Console.WriteLine($"CERTIFICATES: {certs.Count} offline checks returned; no certificate downloads");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        try
        {
            await CommandRunner.PowerShellAsync("Start-Sleep -Seconds 20; 'unexpected'", cancel.Token);
            throw new InvalidOperationException("Command cancellation ignored");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        await TestLiveStreamAsync();
        await TestAdminShellAsync();
        return 18;
    }

    private static async Task TestAdminShellAsync()
    {
        var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = AdminShell.RunAsync("'Shell Pr\u00fcfung'; Start-Sleep -Seconds 2; 'Shell Ende'",
            TimeSpan.FromSeconds(15), CancellationToken.None, new InlineProgress(chunk =>
            {
                output.Enqueue(chunk);
                if (chunk.Contains("Shell Pr\u00fcfung")) first.TrySetResult();
            }));
        try
        {
            await first.Task.WaitAsync(TimeSpan.FromSeconds(8));
            if (command.IsCompleted) throw new InvalidOperationException("Shell output is not streamed before exit");
        }
        finally { await command; }
        var result = await command;
        if (result.ExitCode != 0 || !result.Output.Contains("Shell Ende") || result.Output.Contains('\0'))
            throw new InvalidOperationException("Shell unicode/stream transport failed");
        var failed = await AdminShell.RunAsync("Write-Error 'fixture shell error'",
            TimeSpan.FromSeconds(15), CancellationToken.None);
        if (failed.ExitCode != 1 || !failed.Error.Contains("fixture shell error"))
            throw new InvalidOperationException("Shell PowerShell errors must retain stderr and fail");
        var native = await AdminShell.RunAsync("& \"$env:ComSpec\" /d /c 'exit 7'",
            TimeSpan.FromSeconds(15), CancellationToken.None);
        if (native.ExitCode != 7) throw new InvalidOperationException($"Native shell exitcode lost: {native.ExitCode}");
        var sfcFixture = AdminShell.BuildScript("sfc /verifyonly").Replace(
            "& \"$env:windir\\System32\\sfc.exe\" @args",
            "& \"$env:ComSpec\" /d /u /c 'echo SFC Pr\u00fcfung & exit 7'", StringComparison.Ordinal);
        var sfcDecoded = await CommandRunner.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe"), ["-NoProfile", "-NonInteractive", "-Command", sfcFixture],
            TimeSpan.FromSeconds(15), CancellationToken.None, utf8: true, closeInput: true);
        if (sfcDecoded.ExitCode != 7 || !sfcDecoded.Output.Contains("SFC Pr\u00fcfung") || sfcDecoded.Output.Contains('\0'))
            throw new InvalidOperationException("Shell SFC UTF-16 decoder or exitcode failed with harmless mocked native command");
        var isolated = await AdminShell.RunAsync("if (Test-Path variable:AetherFixture) { throw 'Session leaked' }; $AetherFixture=123; 'isolated'",
            TimeSpan.FromSeconds(15), CancellationToken.None);
        var second = await AdminShell.RunAsync("if (Test-Path variable:AetherFixture) { throw 'Session leaked' }; 'isolated'",
            TimeSpan.FromSeconds(15), CancellationToken.None);
        if (isolated.ExitCode != 0 || second.ExitCode != 0) throw new InvalidOperationException("Shell sessions leak state");
        var prompt = await AdminShell.RunAsync("Read-Host 'Fixture'",
            TimeSpan.FromSeconds(10), CancellationToken.None);
        if (prompt.ExitCode == 0) throw new InvalidOperationException("Interactive prompt silently accepted");
        try
        {
            await AdminShell.RunAsync("Start-Sleep -Seconds 20", TimeSpan.FromMilliseconds(400), CancellationToken.None);
            throw new InvalidOperationException("Shell timeout ignored");
        }
        catch (TimeoutException) { }
        Console.WriteLine("ADMIN SHELL: live unicode, PowerShell/stderr errors, native exitcode, isolated sessions, noninteractive prompts and timeout verified; no repairs executed");
    }

    private sealed class InlineProgress(Action<string> action) : IProgress<string>
    {
        public void Report(string value) => action(value);
    }

    private static async Task TestLiveStreamAsync()
    {
        var firstOutput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chunks = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var script = "[Console]::OutputEncoding=[Text.Encoding]::Unicode; " +
            "[Console]::Write('Pr\u00fcfung 25%'); [Console]::Out.Flush(); Start-Sleep -Seconds 2; " +
            "[Console]::Write(\"`rPr\u00fcfung 100%`r`n\"); [Console]::Error.Write('Fixture Fehler'); exit 7";
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var command = CommandRunner.RunAsync(executable,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))],
            TimeSpan.FromSeconds(15), CancellationToken.None,
            new InlineProgress(chunk =>
            {
                chunks.Enqueue(chunk);
                if (chunk.Contains("Pr\u00fcfung 25%")) firstOutput.TrySetResult();
            }), outputEncoding: System.Text.Encoding.Unicode);
        try
        {
            await firstOutput.Task.WaitAsync(TimeSpan.FromSeconds(8));
            if (command.IsCompleted) throw new InvalidOperationException("Progress appeared only after process exit");
        }
        finally { await command; }
        var result = await command;
        if (result.ExitCode != 7 || !result.Output.Contains("Pr\u00fcfung 100%") || result.Output.Contains('\0'))
            throw new InvalidOperationException("UTF-16/CR progress transport failed");
        if (!result.Error.Contains("Fixture Fehler") || !chunks.Any(c => c.StartsWith("[STDERR]")))
            throw new InvalidOperationException("stderr stream not distinguished");
        if (!chunks.Any(c => c.Contains("[PROCESS] PID")) || !chunks.Any(c => c.Contains("Exitcode 7")))
            throw new InvalidOperationException("Process metadata missing");
        Console.WriteLine("TERMINAL: UTF-16 progress without newline visible before exit; carriage returns, stderr and exitcode verified");
    }
}
