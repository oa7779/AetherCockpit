using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;
using Path = System.IO.Path;

internal static class ModernDesktopTests
{
    internal static int Run()
    {
        var assertions = 0;
        void Check(bool value, string message)
        {
            assertions++;
            if (!value) throw new InvalidOperationException(message);
        }

        Check(new CockpitSettings().Skin == "retro-neon" && new CockpitSettings().Language == "de",
            "Existing installations retain the Retro Neon and German defaults");
        var emptySettings = System.Text.Json.JsonSerializer.Deserialize<CockpitSettings>("{}")!;
        Check(emptySettings.Skin == "retro-neon" && emptySettings.Language == "de",
            "Settings files from before appearance and language preferences migrate to existing defaults");
        var deferredEnglish = CockpitSettings.Upgrade(new CockpitSettings
        {
            Language = "en", Skin = "windows-modern", CpuWarningC = 83
        });
        Check(deferredEnglish.Language == "de" && deferredEnglish.Skin == "windows-modern" &&
            deferredEnglish.CpuWarningC == 83,
            "Previously saved English preferences migrate to supported German without changing other settings");
        var oldSettings = CockpitSettings.Upgrade(
            System.Text.Json.JsonSerializer.Deserialize<CockpitSettings>("{\"Skin\":\"fluent-26h2\"}")!);
        Check(oldSettings.Skin == "windows-modern" && oldSettings.Language == "de",
            "Legacy Fluent skin setting migrates without changing the existing language default");
        Check(SkinCatalog.Valid("windows-modern") && SkinCatalog.Valid("fluent-26h2") &&
            SkinCatalog.CanonicalId("fluent-26h2") == "windows-modern",
            "Windows Modern is selectable and the previous Fluent identifier remains loadable");
        Check(DesktopScreensaverPolicy.IsEnabled(new()) &&
            !DesktopScreensaverPolicy.IsEnabled(new() { Skin = "windows-modern" }),
            "Only Windows Modern disables the saver host and its live feed");
        Check(!DesktopScreensaverPolicy.ReadSafely(() => throw new UnauthorizedAccessException()) &&
            !DesktopScreensaverPolicy.ReadSafely(() => throw new System.Text.Json.JsonException()) &&
            !DesktopScreensaverPolicy.ReadSafely(() => throw new System.Security.SecurityException()) &&
            DesktopScreensaverPolicy.ReadSafely(() => new CockpitSettings()),
            "Saver settings failures stop the host; genuine absent settings retain the Neon default");
        try
        {
            new CockpitSettings { Language = "fr" }.Validate();
            throw new InvalidOperationException("Unsupported language was accepted.");
        }
        catch (ArgumentException) { assertions++; }

        Check(DesktopText.CatalogKeys.Count == DesktopText.CatalogKeys.Distinct(StringComparer.Ordinal).Count(),
            "Desktop resource catalog keys are unique");
        foreach (var language in new[] { "de", "en" })
            foreach (var key in DesktopText.CatalogKeys)
                Check(DesktopText.HasExplicitValue(key, language), $"Resource '{key}' has an explicit {language} value");
        Check(DesktopUiText.Keys.SequenceEqual(DesktopUiText.KeysForLanguage("en")),
            "Extended authored desktop catalogs have exactly matching named keys, with no silent English fallback");
        foreach (var key in DesktopUiText.Keys)
            foreach (var language in new[] { "de", "en" })
                Check(DesktopUiText.HasExplicitValue(key, language), $"Extended resource '{key}' is explicitly authored in {language}");

        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        var previousUiCulture = System.Globalization.CultureInfo.CurrentUICulture;
        var previousDefaultCulture = System.Globalization.CultureInfo.DefaultThreadCurrentCulture;
        var previousDefaultUiCulture = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            DesktopText.ConfigureSupportedUi("en");
            Check(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de",
                "Both desktop and saver startup gate unfinished English before any authored UI");
            DesktopText.Configure("en");
            Check(DesktopUiText.Get("Package.ConsentDetails").Contains("--exact", StringComparison.Ordinal) &&
                DesktopUiText.Get("Package.ConsentDetails").Contains("licenses are accepted", StringComparison.Ordinal) &&
                DesktopUiText.Get("Package.UninstallPermanent").Contains("permanently", StringComparison.Ordinal) &&
                DesktopUiText.Get("Package.QueueIncomplete").Contains("partial actions may remain", StringComparison.Ordinal),
                "English package confirmations retain exact targeting, license acceptance, permanent removal and partial-change warnings");
            Check(DesktopUiText.Get("Package.QueueSummary").Contains("Not proof of functionality", StringComparison.Ordinal) &&
                DesktopUiText.Get("Package.Imported").Contains("Versions are not version locks", StringComparison.Ordinal) &&
                DesktopUiText.Format("Package.Target", "Original.Id", "Original Hardware / unveraendert", "Original.Source")
                    .Contains("Original Hardware / unveraendert", StringComparison.Ordinal),
                "Package wrappers preserve native identifiers/names and do not overstate success or imported versions");
            Check(DesktopUiText.Get("Center.ManagerConsent").Contains("only while AETHER is running", StringComparison.Ordinal) &&
                DesktopUiText.Get("Center.EnrollConsent").Contains("does not enable reports", StringComparison.Ordinal) &&
                DesktopUiText.Get("Main.SensorConsent").Contains("No automatic elevation", StringComparison.Ordinal) &&
                DesktopUiText.Get("Main.SensorConsent").Contains("driver blocks", StringComparison.Ordinal),
                "English management and sensor confirmations preserve separate opt-in, app lifetime and Windows protection boundaries");
            Check(AetherScreensaver.Program.ConfigurationMessage.Contains("Windows screensaver settings", StringComparison.Ordinal) &&
                DesktopUiText.Get("Saver.NoLive").Contains("CPU --", StringComparison.Ordinal) &&
                DesktopUiText.Get("Saver.NoLive").Contains("NO LIVE READINGS", StringComparison.Ordinal),
                "English saver configuration and disconnected presentation retain unavailable readings");
            Check(DesktopUiText.Get("FirstRun.Accept") == "I have read and accept the terms." &&
                EulaConsent.Text.Contains("Mandatory statutory rights", StringComparison.Ordinal) &&
                EulaConsent.Text.Contains(@"%LOCALAPPDATA%\AetherCockpit", StringComparison.Ordinal),
                "English first-run consent and EULA preserve statutory rights and the original data path");
            var commands = SystemActions.Catalog;
            Check(commands.Single(a => a.Id == "sfc-repair").Arguments.SequenceEqual(["/scannow"]) &&
                commands.Single(a => a.Id == "sfc-repair").Warning.Contains("Back up first", StringComparison.Ordinal) &&
                commands.Single(a => a.Id == "firmware").Arguments.SequenceEqual(["/r", "/fw", "/t", "30"]),
                "Action names and warnings are localized without altering executable arguments or restart delay");
            foreach (var tool in ManagedActionTools.All)
            {
                var canonical = AetherOperations.ManagedActionCatalog.Get(tool.Action.Id);
                Check(tool.Action.Script == canonical.Script && tool.Action.ChangesSystem == canonical.ChangesSystem &&
                    tool.Action.RequiredCommands.SequenceEqual(canonical.RequiredCommands),
                    $"Desktop localization preserves the executable definition of '{canonical.Id}'");
            }
            Check(ManagedActionTools.All.Single(t => t.Action.Id == "dism-repair").Risk.Contains(
                "Cancellation is not a rollback", StringComparison.Ordinal) &&
                NetworkMonitorWindow.ConsentText.Contains("128 bytes/packet", StringComparison.Ordinal) &&
                NetworkMonitorWindow.ConsentText.Contains("authorized interface", StringComparison.Ordinal),
                "English repair and capture warnings retain safety boundaries");
            var history = new OperationHistory();
            var job = history.Add("Synthetic");
            job.State = "LAEUFT";
            Check(job.State == "LAEUFT" && job.StateDisplay == "RUNNING",
                "Translated job display does not change the invariant job state used by eviction and execution");
            Check(new SensorReading("/test", "Original Hardware", "Original Sensor", "Temperature", null, "C").Display ==
                "Unavailable" && PacketFilter.Protocols[0] == "Alle" &&
                PacketFilter.Create("", "Alle", "", false, false).Matches(
                    new PacketSummary(DateTimeOffset.Now, "Original source", "Original destination", "TCP", 1, 2, 64, false, false)),
                "Unavailable readings remain unavailable and localized protocol options preserve filter identifiers");
            Check(DesktopUiText.Format("Job.Result", "EXTERNAL OUTPUT / unveraendert", "Synthetic",
                TimeSpan.FromSeconds(3)).Contains("EXTERNAL OUTPUT / unveraendert", StringComparison.Ordinal),
                "Localized console wrappers never replace or translate external command output");
            var diagnosticSnapshot = new WindowsDiagnosticSnapshot(
                "Fixture host", "Windows 11", "Fixture version", "Fixture build",
                [new("Application", "2026-01-01T00:00:00Z", 1000, 2,
                    "Original Provider", "EXTERNAL EVENT / unveraendert")],
                [new("Original Device", "Display", "", null, @"PCI\ORIGINAL")], [], [], []);
            var findings = DiagnosticAssistants.Analyze(diagnosticSnapshot).Findings;
            var applicationFinding = findings.Single(f => f.Agent == "Anwendungsereignisse");
            var deviceFinding = findings.Single(f => f.Agent == "Treiber und Geraete");
            Check(applicationFinding.AgentDisplay == "Application events" &&
                applicationFinding.Severity == "MITTEL" && applicationFinding.SeverityDisplay == "MEDIUM" &&
                applicationFinding.Evidence == "EXTERNAL EVENT / unveraendert" &&
                applicationFinding.Summary.Contains("Original Provider", StringComparison.Ordinal),
                "Diagnostic display resources preserve agent/severity codes, provider names and native event messages");
            Check(deviceFinding.Severity == "UNBEKANNT" && deviceFinding.SeverityDisplay == "UNKNOWN" &&
                deviceFinding.Recommendation.Contains("does not establish a fault-free condition", StringComparison.Ordinal) &&
                deviceFinding.Evidence.Contains(@"PCI\ORIGINAL", StringComparison.Ordinal),
                "English diagnostic findings never turn missing device status into healthy status");
            Check(BackgroundDiagnosticMonitor.IsActionable(applicationFinding) &&
                !BackgroundDiagnosticMonitor.IsActionable(deviceFinding) &&
                BackgroundDiagnosticMonitor.NotificationText([applicationFinding]).Contains(
                    "Repair requires approval", StringComparison.Ordinal),
                "Localized notifications retain the severity policy and explicit repair approval");
            var persistedFinding = System.Text.Json.JsonSerializer.Serialize(applicationFinding);
            Check(!persistedFinding.Contains("AgentDisplay", StringComparison.Ordinal) &&
                !persistedFinding.Contains("SeverityDisplay", StringComparison.Ordinal),
                "Localized display projections do not change the stored diagnostic finding contract");
            Check(DesktopUiText.Get("Help.SkinsBody").Contains("confirmed app restart", StringComparison.Ordinal) &&
                DesktopUiText.Get("Help.SchemaBody").Contains("reduced-motion", StringComparison.Ordinal) &&
                DiagnosticAssistants.CollectionScript.Contains("throw ($aetherEventLogError -f", StringComparison.Ordinal),
                "Authored help and collection failures reflect restart, motion and localization semantics");
            Check(DesktopUiText.Format("Cleaner.DeleteConsent", 3, 2.5, 1).Contains(
                    "No Recycle Bin and no undo", StringComparison.Ordinal) &&
                DesktopUiText.Get("Cleaner.DeleteConsent").Contains("hidden by the view filter", StringComparison.Ordinal) &&
                DesktopUiText.Get("Plan.RemoteConsent").Contains("No changes or WinRM setup", StringComparison.Ordinal) &&
                DesktopUiText.Get("Plan.NoRestoreConsent").Contains("not a file backup", StringComparison.Ordinal),
                "English cleanup, hidden selections, remote access and restore-point confirmations retain safety limits");
            Check(MaintenancePlans.Catalog.Count == 17 &&
                MaintenancePlans.Catalog.Single(a => a.Id == "diskScan").Description.Contains("online corrections", StringComparison.Ordinal) &&
                MaintenancePlans.Catalog.Single(a => a.Id == "diskScan").Script.Contains("/scan", StringComparison.Ordinal) &&
                MaintenancePlans.Template("Nur Diagnose").SequenceEqual(["inventory", "events", "startup", "componentAnalysis"]),
                "Localized maintenance descriptions preserve action IDs, native flags and invariant template selection");
            var systemAssistant = WindowsTroubleshootingAssistants.Get("system");
            Check(systemAssistant.Script.Contains("NO REPAIR", StringComparison.Ordinal) &&
                systemAssistant.Script.Contains("/verifyonly", StringComparison.Ordinal) &&
                !systemAssistant.Script.Contains("/scannow", StringComparison.Ordinal) &&
                WindowsTroubleshootingAssistants.Get("network").Script.Contains(
                    "Resolve-DnsName -Name 'www.microsoft.com'", StringComparison.Ordinal) &&
                DesktopScriptText.Preamble("Settings.Language").Contains("''de''", StringComparison.Ordinal),
                "Authored script headings use quoted named resources without modifying native output, diagnostic flags or DNS targets");
            var sensorChecks = SensorDiagnostics.Evaluate(
                [new("/fixture", "Original hardware", "Thermal Throttling", "Factor", 0, "")], new CockpitSettings());
            Check(sensorChecks.Count(c => c.Name.StartsWith("Throttling indicator", StringComparison.Ordinal)) == 1 &&
                sensorChecks.Single().State == "ok",
                "Localized sensor display does not create a duplicate unknown throttling finding by comparing translated names");
            try { DesktopUiText.Get("Missing.Deliberate.Test"); throw new InvalidOperationException("Missing resource silently fell back."); }
            catch (System.Resources.MissingManifestResourceException) { assertions++; }
            try { DesktopText.Get("Missing.Deliberate.Test"); throw new InvalidOperationException("Missing legacy resource silently fell back."); }
            catch (InvalidOperationException error) when (error.Message.StartsWith("Desktop resource", StringComparison.Ordinal))
            { assertions++; }
            Check(DesktopText.Get("Assistant.System.Name") == "Windows files and components" &&
                DesktopText.Get("Safety.RestartNow.Body").Contains("Restart AETHER now", StringComparison.Ordinal),
                "English assistant description and restart confirmation use authored resources");
            DesktopText.Configure("de");
            Check(EulaConsent.Text.StartsWith("ENDNUTZER-LIZENZVEREINBARUNG", StringComparison.Ordinal) &&
                SystemActions.Catalog.Single(a => a.Id == "sfc-repair").Warning == "Aendert/repariert geschuetzte Systemdateien. Vorher sichern.",
                "German defaults retain the existing EULA and repair warning");
            Check(DesktopText.Get("Assistant.System.Name") == "Windows-Dateien und Komponenten" &&
                DesktopText.Get("Safety.RestartNow.Body").Contains("AETHER jetzt neu starten", StringComparison.Ordinal),
                "German assistant description and restart confirmation use authored resources");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
            System.Globalization.CultureInfo.CurrentUICulture = previousUiCulture;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = previousDefaultCulture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUiCulture;
        }

        var selected = new CockpitSettings() with { Skin = "windows-modern", Language = "en" };
        var restartCalls = 0;
        DesktopRestart.RestartIfConfirmed(false, () => restartCalls++);
        Check(restartCalls == 0 && selected.Skin == "windows-modern" && selected.Language == "en",
            "Deferring restart keeps saved selections while leaving the running session unchanged");
        DesktopRestart.RestartIfConfirmed(true, () => restartCalls++);
        Check(restartCalls == 1, "Confirmed restart invokes exactly one restart action");
        var appHostRestart = DesktopRestart.ReplacementStartInfo(@"C:\AETHER\AetherCockpit.exe",
            [@"C:\AETHER\AetherCockpit.dll", "--tray"]);
        var frameworkRestart = DesktopRestart.ReplacementStartInfo(@"C:\Program Files\dotnet\dotnet.exe",
            [@"C:\AETHER\AetherCockpit.dll", "--tray"]);
        Check(appHostRestart.ArgumentList.SequenceEqual(["--tray"]) &&
            frameworkRestart.ArgumentList.SequenceEqual([@"C:\AETHER\AetherCockpit.dll", "--tray"]) &&
            !frameworkRestart.UseShellExecute,
            "Restart preserves startup flags and the assembly path when launched through dotnet, without shell interpretation");

        var app = Application.Current ?? throw new InvalidOperationException("WPF application host is missing.");
        var light = new ResourceDictionary { Source = SkinCatalog.Source("windows-modern", true) };
        var dark = new ResourceDictionary { Source = SkinCatalog.Source("windows-modern", false) };
        Check(((SolidColorBrush)light["WindowBackdrop"]).Color != ((SolidColorBrush)dark["WindowBackdrop"]).Color &&
            light["SceneBackdrop"] is Brush && dark["SceneBackdrop"] is Brush &&
            light["ModernAccent"] is Brush && dark["ModernAccent"] is Brush,
            "Both Windows Modern palettes provide a neutral window, scene, and accent brush set");
        var controls = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SystemCockpit;component/Controls/WindowsModernControls.xaml")
        };
        Check(controls["NeonButton"] is Style && controls["NeonTextBox"] is Style &&
            controls["NeonDataGrid"] is Style && controls["NeonCheckBox"] is Style &&
            controls["NeonTabItem"] is Style,
            "Windows Modern provides neutral equivalents for the cockpit's shared control styles");

        SkinCatalog.Apply(app, "windows-modern", true);
        Check(SkinCatalog.IsModern && app.Resources["NeonButton"] is Style &&
            app.Resources["ModernTextPrimary"] is Brush,
            "Applying Windows Modern loads its control set and light palette before windows are created");
        Check(!DesktopScreensaverPolicy.LiveFeedEnabled &&
            BoardLiveFeed.ServeAsync(new CockpitModel
            {
                Settings = new CockpitSettings { Skin = "retro-neon" }
            }, app.Dispatcher, CancellationToken.None).IsCompletedSuccessfully,
            "Active Windows Modern creates no saver feed or hardware probe, even with a pending Neon preference");
        SkinCatalog.Apply(app, "retro-neon");
        Check(!SkinCatalog.IsModern && new CockpitSettings().Skin == "retro-neon" &&
            DesktopScreensaverPolicy.LiveFeedEnabled,
            "Retro Neon remains the default; a saved preference does not change active-session feed policy");
        return assertions;
    }

    internal static int UiSmoke(string? screenshotDirectory)
    {
        var assertions = Run();
        var app = Application.Current ?? throw new InvalidOperationException("WPF application host is missing.");
        DesktopText.ConfigureSupportedUi("de");
        ModernWindowAppearance.Enable();
        var output = string.IsNullOrWhiteSpace(screenshotDirectory)
            ? Environment.GetEnvironmentVariable("AETHER_SCREENSHOT_DIR") : screenshotDirectory;
        var outputDirectory = string.IsNullOrWhiteSpace(output) ? null : Path.GetFullPath(output);
        if (outputDirectory is not null) Directory.CreateDirectory(outputDirectory);
        else Console.WriteLine("SCREENSHOT: skipped; set AETHER_SCREENSHOT_DIR to capture synthetic Windows Modern views.");
        void CheckUi(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
        var operations = new OperationsHub();
        try
        {
            var templates = (ComboBox)operations.FindName("Templates");
            var repair = (ComboBoxItem)templates.Items[1];
            CheckUi(Equals(repair.Tag, "Windows reparieren") && Equals(repair.Content, DesktopUiText.Get("Ui.RepairTemplate")) &&
                MaintenancePlans.Template((string)repair.Tag).Contains("repairImage"),
                "Localized maintenance template captions retain invariant dispatch identifiers");
            operations.OpenWindowsAssistants();
            var selectedTab = (TabItem)((TabControl)operations.FindName("Tabs")).SelectedItem;
            CheckUi(Equals(selectedTab.Tag, "Windows-Assistenten") && Equals(selectedTab.Header, DesktopUiText.Get("Layout.AssistantsTab")),
                "German Modern assistant navigation uses invariant tab tags");
        }
        finally { operations.Shutdown(); }

        foreach (var (isLight, modeName) in new[] { (true, "light"), (false, "dark") })
        {
            SkinCatalog.Apply(app, "windows-modern", isLight);
            var expectedPalette = new ResourceDictionary { Source = SkinCatalog.Source("windows-modern", isLight) };
            CheckUi(((SolidColorBrush)app.Resources["WindowBackdrop"]).Color ==
                ((SolidColorBrush)expectedPalette["WindowBackdrop"]).Color,
                "Windows Modern palette is active for smoke rendering");
            var model = new CockpitModel
            {
                Settings = new CockpitSettings { Skin = "windows-modern", Language = "de" },
                MotionEnabled = true
            };
            var board = new HardwareBoardView { DataContext = model };
            board.Apply(HardwareBoardTests.IllustrationExample);
            var canvas = (Canvas)board.FindName("BoardCanvas");
            CheckUi(canvas.Children.OfType<Border>().Count(item =>
                item.Tag is string tag && tag.StartsWith("modern-schema-card:", StringComparison.Ordinal)) == 10,
                "Modern system schema renders all synthetic device categories as neutral schematic cards");
            CheckUi(!canvas.Children.OfType<Canvas>().Any(item => Equals(item.Tag, "synthwave-ground")) &&
                ((TextBlock)board.FindName("BoardTitleText")).Text == DesktopText.Get("Board.SystemSchemaTitle"),
                "Modern system schema omits the neon landscape and uses the localized replacement name");
            var flows = canvas.Children.OfType<Polyline>()
                .Where(item => item.Tag is string tag && tag.StartsWith("modern-schema-flow:", StringComparison.Ordinal))
                .ToArray();
            var indicators = canvas.Children.OfType<Ellipse>()
                .Where(item => item.Tag is string tag && tag.StartsWith("modern-schema-active-device:", StringComparison.Ordinal))
                .ToArray();
            CheckUi(flows.Length == 10 && indicators.Length > 0,
                "Modern schema animates only illustrative links and marks Windows-reported active use");
            CheckUi(HardwareBoardView.AnimationAllowed(true, true, true, true, false) &&
                !HardwareBoardView.AnimationAllowed(true, true, false, true, false) &&
                !HardwareBoardView.AnimationAllowed(true, true, true, false, false) &&
                !HardwareBoardView.AnimationAllowed(true, false, true, true, false) &&
                !HardwareBoardView.AnimationAllowed(true, true, true, true, true),
                "Modern schema animation obeys global, Windows reduced-motion, visibility, and shutdown gates");

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(board);
            var caption = new TextBlock
            {
                Text = "SYNTHETISCHE TESTDATEN / keine Hardwareabfrage",
                Margin = new Thickness(12),
                Foreground = (Brush)app.Resources["ModernTextSecondary"]
            };
            Grid.SetRow(caption, 1);
            root.Children.Add(caption);
            var window = new Window
            {
                Title = "AETHER / Windows Modern " + modeName,
                Width = 1280,
                Height = 820,
                Content = root,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            try
            {
                window.Show();
                ModernWindowAppearance.Apply(window);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var zoom = (ComboBox)board.FindName("BoardZoom");
                var details = (Border)board.FindName("DeviceDetails");
                CheckUi(ReferenceEquals(details.BorderBrush, app.Resources["ModernBorder"]) &&
                    ReferenceEquals(zoom.Background, app.Resources["ModernInput"]) &&
                    zoom.Template.FindName("Frame", zoom) is Border zoomFrame &&
                    ReferenceEquals(zoomFrame.Background, app.Resources["ModernInput"]),
                    "Schema detail border and closed ComboBox template use actual Modern semantic brushes");
                zoom.IsDropDownOpen = true;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                CheckUi(zoom.Template.FindName("PopupFrame", zoom) is Border popupFrame &&
                    ReferenceEquals(popupFrame.Background, app.Resources["ModernInput"]),
                    "Modern ComboBox popup has a semantic background rather than the native white template");
                zoom.IsDropDownOpen = false;
                CheckUi(zoom.Items.Cast<ComboBoxItem>().All(item =>
                    ReferenceEquals(item.Foreground, app.Resources["ModernTextPrimary"]) &&
                    new[] { "ModernInput", "ModernSelection", "ModernHover" }.Any(key =>
                        ReferenceEquals(item.Background, app.Resources[key]))),
                    "Modern popup items retain neutral normal, selected and highlighted colors");
                zoom.IsEnabled = false;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                CheckUi(ReferenceEquals(zoom.Foreground, app.Resources["ModernTextDisabled"]) &&
                    zoom.Template.FindName("Frame", zoom) is Border disabledFrame &&
                    ReferenceEquals(disabledFrame.Background, app.Resources["ModernDisabledBackground"]),
                    "Disabled Modern ComboBox uses readable semantic disabled colors");
                zoom.IsEnabled = true;
                CheckUi(Contrast((SolidColorBrush)app.Resources["ModernTextPrimary"],
                    (SolidColorBrush)app.Resources["ModernInput"]) >= 4.5,
                    "Modern input/text palette meets 4.5:1 contrast in both light and dark mode");
                CaptureMajorSurfaces(modeName, model);
                CheckUi(!SystemParameters.ClientAreaAnimation ||
                    board.SceneAnimating && board.FlowAnimating && flows.All(item => item.HasAnimatedProperties) &&
                    indicators.All(item => item.HasAnimatedProperties),
                    "Enabled modern data-flow and active-device indicators animate only when Windows motion is permitted");
                var saved = false;
                var settingsWindow = new DesktopSettingsWindow(model, settings => { saved = true; model.Settings = settings; }, () => { });
                CheckUi(settingsWindow.Title == DesktopText.Get("Desktop.Settings.Title") && !saved,
                    "Desktop settings title follows the selected language without saving user settings");
                settingsWindow.Close();
                var readOnlyModel = new CockpitModel { Settings = new CockpitSettings { Skin = "windows-modern", Language = "de" } };
                using var watch = new NetworkWatch(() => readOnlyModel.Settings, _ => { });
                var network = new NetworkMonitorWindow(watch, readOnlyModel,
                    _ => throw new InvalidOperationException("Smoke windows must not save user settings."));
                var firstRun = new FirstRunWindow();
                var gauge = new NeonGauge { Width = 120, Height = 120, Level = 42 };
                var led = new LedDisplay { Width = 180, Height = 36, Text = "42.0 %" };
                var pie = new DrivePie { Width = 90, Height = 90, FreePercent = null };
                var instrumentPanel = new StackPanel();
                instrumentPanel.Children.Add(gauge); instrumentPanel.Children.Add(led); instrumentPanel.Children.Add(pie);
                var instruments = new Window { Content = instrumentPanel, Width = 320, Height = 320 };
                var saveAttempts = 0;
                var desktopSettings = new DesktopSettingsWindow(model, _ => saveAttempts++, () => { });
                try
                {
                    network.Show(); firstRun.Show(); instruments.Show(); desktopSettings.Show();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    CheckUi(network.Title == DesktopUiText.Get("Network.Title") &&
                        firstRun.Title == DesktopUiText.Get("FirstRun.Title") &&
                        ((CheckBox)firstRun.FindName("AcceptCheck")).Content as string == DesktopUiText.Get("FirstRun.Accept") &&
                        ((TextBox)firstRun.FindName("EulaText")).Text == EulaConsent.Text,
                        "German Modern network and first-run windows use authored titles, consent and license text");
                    CheckUi(ReferenceEquals(gauge.Accent, app.Resources["ModernAccent"]) &&
                        ReferenceEquals(led.Accent, app.Resources["ModernTextPrimary"]) &&
                        ReferenceEquals(pie.UsedBrush, app.Resources["ModernAccent"]) && pie.FreePercent is null,
                        "Custom-drawn gauges, numeric displays and unknown drive pies use the neutral modern palette without invented readings");
                    var preferences = ((StackPanel)((ScrollViewer)desktopSettings.Content).Content).Children
                        .OfType<ComboBox>().ToArray();
                    CheckUi(preferences[1].Items.Count == 1 && !preferences[1].IsEnabled &&
                        preferences[1].SelectedValue as string == "de" && saveAttempts == 0,
                        "Only supported German is visible; opening settings never writes preferences");
                    preferences[0].SelectedValue = "retro-neon";
                    CheckUi(saveAttempts == 1 && preferences[0].SelectedValue as string == "windows-modern" &&
                        model.Settings.Language == "de" && SkinCatalog.IsModern &&
                        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de",
                        "A failed skin save resets once without restart prompts or changing active German Modern");
                }
                finally { network.Close(); firstRun.Close(); instruments.Close(); desktopSettings.Close(); }
                if (outputDirectory is not null)
                {
                    var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
                    var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var path = Path.Combine(outputDirectory, $"windows-modern-{modeName}-system-schema.png");
                    using var stream = File.Create(path);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(stream);
                    Console.WriteLine($"SCREENSHOT: {path}");
                }
                window.Hide();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                CheckUi(!board.SceneAnimating && !board.FlowAnimating && flows.All(item => !item.HasAnimatedProperties) &&
                    indicators.All(item => !item.HasAnimatedProperties),
                    "Hiding the modern schema pauses its animation");
                window.Show();
                model.MotionEnabled = false;
                CheckUi(!board.SceneAnimating && !board.FlowAnimating && flows.All(item => !item.HasAnimatedProperties) &&
                    indicators.All(item => !item.HasAnimatedProperties),
                    "Turning off the global animation toggle stops modern schema animation");
            }
            finally
            {
                board.Shutdown();
                window.Close();
            }
        }
        SkinCatalog.Apply(app, "retro-neon");
        DesktopText.Configure("de");
        var neonModel = new CockpitModel { Settings = new CockpitSettings(), MotionEnabled = false };
        var neonBoard = new HardwareBoardView { DataContext = neonModel };
        neonBoard.Apply(HardwareBoardTests.IllustrationExample);
        var neonCanvas = (Canvas)neonBoard.FindName("BoardCanvas");
        CheckUi(neonCanvas.Children.OfType<Canvas>().Any(item => Equals(item.Tag, "synthwave-ground")) &&
            !neonCanvas.Children.OfType<Border>().Any(item =>
                item.Tag is string tag && tag.StartsWith("modern-schema-card:", StringComparison.Ordinal)) &&
            !neonBoard.SceneAnimating && DesktopScreensaverPolicy.LiveFeedEnabled &&
            DesktopScreensaverPolicy.IsEnabled(neonModel.Settings),
            "Returning to default German Neon preserves the landscape, stopped-motion toggle and saver eligibility");
        neonBoard.Shutdown();
        return assertions;

        void CaptureMajorSurfaces(string modeName, CockpitModel model)
        {
            void NoSave(CockpitSettings _) => throw new InvalidOperationException("Synthetic UI must not write settings.");
            var main = new MainWindow(model.Settings) { Width = 1440, Height = 960 };
            var control = new ControlCenterWindow(model) { Width = 1320, Height = 900 };
            var desktop = new DesktopSettingsWindow(model, NoSave, () => { });
            var sidebar = new SidebarWindow(model, model.Settings, NoSave, synthetic: true);
            var gadget = new GadgetWindow("CPU", model.DetailGraphs[0], "MetricTemplate",
                new GadgetLayout("CPU", 0, 0, 1), _ => throw new InvalidOperationException("Synthetic gadget must not write."),
                _ => throw new InvalidOperationException("Synthetic gadget failure."), synthetic: true);
            var boot = new BootWindow();
            var firstRun = new FirstRunWindow();
            var confirmation = new ActionConfirmationWindow(DesktopUiText.Get("Center.CommandTitle"),
                DesktopUiText.Get("Safety.ExplorerRestart"));
            try
            {
                Capture(main, "dashboard-navigation");
                var pages = (TabControl)main.FindName("Pages");
                pages.SelectedItem = pages.Items.Cast<TabItem>().Single(tab => Equals(tab.Tag, "Superadmin"));
                Capture(main, "superadmin");
                var tabs = (TabControl)control.FindName("Modules");
                tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Tag, "Einstellungen"));
                Capture(control, "control-center-settings");
                tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Tag, "Wartung"));
                Capture(control, "control-center-admin");
                Capture(desktop, "desktop-settings");
                Capture(sidebar, "xbar");
                Capture(gadget, "gadget");
                boot.Report("SYNTHETISCHE VORSCHAU / keine Hardwareabfrage", 65);
                Capture(boot, "boot");
                Capture(firstRun, "first-run");
                Capture(confirmation, "action-confirmation");
                CheckUi(!confirmation.Confirm.IsEnabled && confirmation.Consent.IsChecked != true,
                    "Synthetic action preview remains unapproved and executes nothing");
            }
            finally
            {
                main.Close(); control.Close(); desktop.Close(); sidebar.Close(); gadget.Close();
                boot.CloseAfterStartup(); firstRun.Close(); confirmation.Close();
            }

            void Capture(Window surface, string name)
            {
                surface.Show();
                ModernWindowAppearance.Apply(surface);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                surface.UpdateLayout();
                var elements = VisualElements(surface).ToArray();
                CheckUi(elements.All(element => element.Effect is null &&
                    !FrameGlow.GetVisible(element) && !FrameGlow.GetEnabled(element)),
                    $"Modern {modeName} {name} contains no neon glow effects");
                CheckUi(ReferenceEquals(surface.Foreground, app.Resources["ModernTextPrimary"]) &&
                    ReferenceEquals(surface.Background, app.Resources["WindowBackdrop"]),
                    $"Modern {modeName} {name} uses semantic window/text palette");
                CheckUi(elements.OfType<TextBox>().All(text =>
                    ReferenceEquals(text.Foreground, app.Resources["ModernTextPrimary"]) &&
                    ReferenceEquals(text.Background, app.Resources["ModernInput"])),
                    $"Modern {modeName} {name} text inputs retain semantic contrast");
                if (outputDirectory is not null)
                {
                    var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(surface.ActualWidth)),
                        Math.Max(1, (int)Math.Ceiling(surface.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(surface);
                    var path = Path.Combine(outputDirectory, $"windows-modern-{modeName}-{name}.png");
                    using var stream = File.Create(path);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
                    Console.WriteLine($"SCREENSHOT: {path}");
                }
                surface.Hide();
            }
        }
    }

    private static IEnumerable<FrameworkElement> VisualElements(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in VisualElements(child)) yield return descendant;
        }
    }
        private static double Contrast(SolidColorBrush foreground, SolidColorBrush background)
        {
            static double Luminance(Color color)
            {
                static double Channel(byte value)
                {
                    var v = value / 255d;
                    return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
                }
                return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
            }
            var a = Luminance(foreground.Color);
            var b = Luminance(background.Color);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
