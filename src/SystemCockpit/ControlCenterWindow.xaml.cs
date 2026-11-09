using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Security;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using System.Text;
using Microsoft.Win32;
using SystemCockpit.Services;

namespace SystemCockpit;

public partial class ControlCenterWindow : Window
{
    public Task PendingOperations => Task.WhenAll(_operation, _autoTask);
    private readonly CockpitModel _model;
    private readonly StartupManager _startup = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherCockpit", "Backups"));
    private CancellationTokenSource? _active;
    private Task _operation = Task.CompletedTask;
    private bool _closing, _allowClose;
    private HistoryReport? _log;
    private SmartReport? _smart;
    private IReadOnlyList<FrameSummary>? _frames;
    private string _captureSession = "";
    private CancellationTokenSource? _autoStop;
    private Task _autoTask = Task.CompletedTask;
    private readonly Stopwatch _elapsed = new();
    private readonly DispatcherTimer _terminalTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset _lastOutput;
    private string _runningLabel = "";
    private bool _sensitiveOutput, _terminalExpanded, _outputLimitReached;
    private readonly List<string> _commandHistory = [];
    private int _historyIndex;
    private string _historyDraft = "";
    private readonly NavigationTrail _trail = new();
    private bool _trailNavigating;
    private readonly OperationHistory _jobs = new();

    private void TaskCenterClicked(object sender, RoutedEventArgs e) => (Owner as MainWindow)?.OpenTaskCenter();

    private void TrailGo(string? header)
    {
        if (header is null || _closing) return;
        if (_active is not null) { Status.Text = DesktopUiText.Get("ControlCenter.BusyNavigation"); return; }
        _trailNavigating = true;
        try { Modules.SelectedItem = Modules.Items.Cast<TabItem>().Single(t => Equals(t.Tag, header)); }
        finally { _trailNavigating = false; }
        CcBack.IsEnabled = _trail.CanBack;
        CcForward.IsEnabled = _trail.CanForward;
    }

    private void CcBackClicked(object sender, RoutedEventArgs e) => TrailGo(_trail.Back());
    private void CcForwardClicked(object sender, RoutedEventArgs e) => TrailGo(_trail.Forward());
    private void CcHomeClicked(object sender, RoutedEventArgs e) => Owner?.Activate();

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1) { TrailGo(_trail.Back()); e.Handled = true; }
        else if (e.ChangedButton == MouseButton.XButton2) { TrailGo(_trail.Forward()); e.Handled = true; }
        base.OnPreviewMouseDown(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.K && e.KeyboardDevice.Modifiers == ModifierKeys.Control)
        { (Owner as MainWindow)?.OpenTaskCenter(); e.Handled = true; }
        if (e.Key == Key.System && e.KeyboardDevice.Modifiers == ModifierKeys.Alt)
        {
            if (e.SystemKey == Key.Left) { TrailGo(_trail.Back()); e.Handled = true; }
            else if (e.SystemKey == Key.Right) { TrailGo(_trail.Forward()); e.Handled = true; }
        }
        base.OnPreviewKeyDown(e);
    }

    public ControlCenterWindow(CockpitModel model)
    {
        InitializeComponent();
        WindowAppearance.Apply(this);
        _model = model;
        JobsGrid.ItemsSource = _jobs.Entries;
        ExpertToggle.IsChecked = model.Settings.ExpertMode;
        ApplyExpertMode();
        var initialModule = (Modules.SelectedItem as TabItem)?.Tag as string ?? "SMART";
        _trail.Visit(initialModule);
        CcTrail.Text = DesktopUiText.Format("ControlCenter.Trail", ((Modules.SelectedItem as TabItem)?.Header as string ?? "SMART").ToUpperInvariant());
        OsCompat.Text = $"Erkanntes System: {DetectOs()} | Katalog fuer Windows 10/11; versionsabhaengige Befehle melden fehlende Unterstuetzung selbst.";
        MaintenanceActions.ItemsSource = SystemActions.Catalog.Where(a => a.Id is not ("wlan" or "wlan-profiles" or "neighbors" or "bluetooth" or "arp")).ToList();
        MaintenanceActions.SelectedIndex = 0;
        PopulateSettings();
        SetTerminalVisible(false);
        ApplyModernMaintenanceChrome();
        Loaded += (_, _) => ApplyModernMaintenanceChrome();
        _terminalTimer.Tick += (_, _) => UpdateTerminalInfo();
        Closing += OnClosing;
    }

    private void ApplyModernMaintenanceChrome()
    {
        if (!SkinCatalog.IsModern) return;
        CommandCatalogHeading.SetResourceReference(TextBlock.ForegroundProperty, "ModernTextPrimary");
        CommandCatalogHeading.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        foreach (var text in new[] { CommandCatalogNotice, CommandDescription, CommandWhenUseful })
            text.SetResourceReference(TextBlock.ForegroundProperty, "ModernTextPrimary");
        foreach (var text in new[] { ModeHint, OsCompat })
            text.SetResourceReference(TextBlock.ForegroundProperty, "ModernTextSecondary");
        foreach (var text in new[] { CommandRisk, CommandImpact, CommandCaution, Status })
            text.SetResourceReference(TextBlock.ForegroundProperty, "ModernWarning");
        CommandList.Style = (Style)Application.Current.FindResource(typeof(ListBox));
        CommandList.SetResourceReference(Control.BackgroundProperty, "SceneBackdrop");
        CommandList.SetResourceReference(Control.ForegroundProperty, "ModernTextPrimary");
        CommandList.SetResourceReference(Control.BorderBrushProperty, "ModernBorder");
        CommandList.ItemTemplate = (DataTemplate)Resources["ModernCommandCatalogItem"];
        CommandList.ItemContainerStyle = (Style)Application.Current.FindResource(typeof(ListBoxItem));
        TerminalSplitter.SetResourceReference(Control.BackgroundProperty, "ModernBorder");
        TerminalHeading.Text = DesktopUiText.Get("Terminal.ModernHeading");
        TerminalHeading.SetResourceReference(TextBlock.ForegroundProperty, "ModernTextPrimary");
        TerminalHeading.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        CrtEffects.IsChecked = false;
        CrtEffects.IsEnabled = false;
        CrtEffects.Visibility = Visibility.Collapsed;
        TerminalDecorations.Visibility = Visibility.Collapsed;
    }

    private static string DetectOs()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = key?.GetValue("ProductName") as string ?? "Windows";
            var display = key?.GetValue("DisplayVersion") as string ?? "";
            var build = key?.GetValue("CurrentBuildNumber") as string ?? "";
            // Windows 11 meldet in ProductName weiterhin "Windows 10"; ab Build 22000 korrigieren.
            if (int.TryParse(build, out var number) && number >= 22000)
                product = product.Replace("Windows 10", "Windows 11");
            return $"{product} {display} (Build {build})".Replace("  ", " ").Trim();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return DesktopUiText.Get("Center.VersionUnreadable");
        }
    }

    private void ExpertModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        try
        {
            var settings = _model.Settings with { ExpertMode = ExpertToggle.IsChecked == true };
            settings.Save();
            _model.Settings = settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status.Text = DesktopUiText.Format("Center.ModeSaveFailed", ex.Message);
            _model.Settings = _model.Settings with { ExpertMode = ExpertToggle.IsChecked == true };
        }
        ApplyExpertMode();
    }

    private void SkinChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || SkinSelect.SelectedItem is not SkinInfo skin) return;
        var skinId = SkinCatalog.CanonicalId(skin.Id);
        if (skinId == _model.Settings.Skin) return;
        try
        {
            var settings = _model.Settings with { Skin = skinId };
            settings.Save();
            _model.Settings = settings;
            Status.Text = DesktopText.Get("Desktop.Settings.PreferenceSaved");
            if (DesktopRestart.Confirm(this))
                DesktopRestart.RestartIfConfirmed(true, DesktopRestart.RestartApplication);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SkinSelect.SelectedItem = SkinSelect.Items.Cast<SkinInfo>().FirstOrDefault(item =>
                item.Id == SkinCatalog.CanonicalId(_model.Settings.Skin));
            Status.Text = DesktopText.Get("Desktop.Settings.SaveFailed") + " " + ex.Message;
        }
    }

    private void ApplyExpertMode()
    {
        ModeHint.Text = _model.Settings.ExpertMode
            ? DesktopUiText.Get("Center.ExpertNotice")
            : DesktopUiText.Get("Center.BeginnerNotice");
        RefreshCommandList();
    }

    // Einsteiger-Modus zeigt die sichere Kernliste: ausschliesslich lesende Befehle.
    public static List<AdminCommand> FilterCatalog(string search, bool expert) => AdminCommandCatalog.All
        .Where(c => (expert || c.RiskLevel == 0) && c.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase))
        .ToList();

    private void RefreshCommandList()
    {
        if (CommandList is null) return;
        CommandList.ItemsSource = FilterCatalog(CommandSearch.Text.Trim(), _model.Settings.ExpertMode);
    }

    public void SetBootTranscript(string transcript)
    {
        if (transcript.Length > 0) AppendTerminal(DesktopUiText.Get("Center.BootJournal") + transcript);
    }

    public void SelectModule(string header)
    {
        if (_active is not null) { Status.Text = DesktopUiText.Get("ControlCenter.BusyNavigation"); return; }
        Modules.SelectedItem = Modules.Items.Cast<TabItem>().Single(t => Equals(t.Tag, header));
    }

    internal void FocusCatalogCommand(string command)
    {
        if (_active is not null) { Status.Text = DesktopUiText.Get("ControlCenter.BusyCatalog"); return; }
        CommandSearch.Text = command;
        if (CommandList.Items.Count > 0) CommandList.SelectedIndex = 0;
        else Status.Text = DesktopUiText.Get("ControlCenter.CommandBlocked");
    }

    public Task DisplayReadOnlyAsync(string label, Func<CancellationToken, Task<string>> read) =>
        RunAsync(label, read, sensitive: true);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var result = WindowAppearance.DarkCaption(this);
        if (result != 0) Status.Text = DesktopUiText.Format("Center.NativeTitleFailed", result);
    }

    private void PopulateSettings()
    {
        var s = _model.Settings;
        if (SkinHint is not null) SkinHint.Text = DesktopText.Get("Skin.ControlCenterHelp");
        CpuThreshold.Text = s.CpuWarningC.ToString(CultureInfo.CurrentCulture);
        GpuThreshold.Text = s.GpuWarningC.ToString(CultureInfo.CurrentCulture);
        StorageThreshold.Text = s.StorageWarningC.ToString(CultureInfo.CurrentCulture);
        FreeThreshold.Text = s.FreeSpaceWarningPercent.ToString(CultureInfo.CurrentCulture);
        VoltageThreshold.Text = s.VoltageTolerancePercent.ToString(CultureInfo.CurrentCulture);
        CmosThreshold.Text = s.CmosMinimumV.ToString(CultureInfo.CurrentCulture);
        RecordSeconds.Text = s.RecordIntervalSeconds.ToString(CultureInfo.CurrentCulture);
        SmartPath.Text = s.SmartctlPath.Length > 0 ? s.SmartctlPath : SmartService.DetectExecutable();
        PresentPath.Text = s.PresentMonPath.Length > 0 ? s.PresentMonPath : FrameTelemetry.DetectExecutable();
        OuiPath.Text = s.OuiCsvPath;
        ManagerServerUrl.Text = s.ManagerServerUrl;
        StandaloneMode.IsChecked = !s.ManagerClientMode;
        ManagedClientMode.IsChecked = s.ManagerClientMode;
        ManagerReportingToggle.IsChecked = s.ManagerReportingEnabled;
        UpdateManagerModeControls();
        ManagerStatus.Text = s.ManagerClientMode
            ? (Application.Current.MainWindow as MainWindow)?.ManagerDeviceTokenAvailable == true
                ? DesktopUiText.Get("Center.ManagedProtected")
                : DesktopUiText.Get("Center.ManagedLocal")
            : DesktopUiText.Get("Center.Standalone");
        SmartPath.ToolTip = s.SmartctlPath.Length > 0 && !File.Exists(s.SmartctlPath)
            ? DesktopUiText.Get("Center.ToolMissing") : SmartPath.Text;
        PresentPath.ToolTip = s.PresentMonPath.Length > 0 && !File.Exists(s.PresentMonPath)
            ? DesktopUiText.Get("Center.ToolMissing") : PresentPath.Text;
        var warnings = s.OptionalFileWarnings();
        if (warnings.Count > 0) Status.Text = string.Join("\n", warnings);
        var skins = SkinCatalog.All;
        SkinSelect.ItemsSource = skins;
        SkinSelect.SelectedItem = skins.FirstOrDefault(skin => skin.Id == SkinCatalog.CanonicalId(s.Skin)) ?? skins[0];
    }

    private void ManagerModeChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            UpdateManagerModeControls();
    }

    private void UpdateManagerModeControls()
    {
        var clientMode = ManagedClientMode.IsChecked == true;
        ManagerServerUrl.IsEnabled = clientMode;
        ManagerEnrollmentCode.IsEnabled = clientMode;
        EnrollManagerButton.IsEnabled = clientMode && (Application.Current.MainWindow as MainWindow)?.ManagerDeviceTokenAvailable != true;
        ManagerReportingToggle.IsEnabled = clientMode;
        if (!clientMode)
            ManagerReportingToggle.IsChecked = false;
        if (ManagerStatus is not null)
            ManagerStatus.Text = clientMode
                ? (Application.Current.MainWindow as MainWindow)?.ManagerDeviceTokenAvailable == true
                    ? DesktopUiText.Get("Center.ManagedRegistered")
                    : DesktopUiText.Get("Center.ManagedPending")
                : DesktopUiText.Get("Center.Standalone");
    }

    private Task RunAsync(string label, Func<CancellationToken, Task<string>> action,
        string? warning = null, bool admin = false, bool sensitive = false, bool detailedConfirmation = false)
    {
        if (_active is not null || _closing)
        {
            Status.Text = DesktopUiText.Get("Job.Busy");
            return Task.CompletedTask;
        }
        if (warning is not null && !(detailedConfirmation
            ? new ActionConfirmationWindow(label, warning) { Owner = this }.ShowDialog() == true
            : MessageBox.Show(this, warning + DesktopUiText.Get("Job.ConfirmNow"), label,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes))
        {
            Status.Text = DesktopUiText.Get("Job.NotAuthorized");
            return Task.CompletedTask;
        }
        _operation = ExecuteAsync();
        return _operation;

        async Task ExecuteAsync()
        {
            var job = sensitive ? null : _jobs.Add(label);
            if (job is not null) job.State = "LAEUFT";
            _active = new CancellationTokenSource();
            SetTerminalVisible(true);
            Modules.IsEnabled = false;
            ShellControls.IsEnabled = false;
            Output.Clear();
            _outputLimitReached = false;
            _sensitiveOutput = sensitive;
            _runningLabel = label;
            _lastOutput = DateTimeOffset.Now;
            _elapsed.Restart();
            _terminalTimer.Start();
            AppendTerminal($"AETHER VT / {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"[START] {label}\n[ACCESS] {(SystemActions.IsAdmin() ? "ADMINISTRATOR" : "STANDARD")}\n" +
                DesktopUiText.Get(sensitive ? "Job.PrivateNotice" : "Job.LocalNotice"));
            Status.Text = DesktopUiText.Format("Job.Running", label);
            try
            {
                if (admin) SystemActions.RequireAdmin();
                if (warning is not null && !sensitive) ActionJournal.Append("action-start", new { label });
                var result = await action(_active.Token);
                AppendTerminal(DesktopUiText.Format("Job.Result", result, label, _elapsed.Elapsed));
                Status.Text = DesktopUiText.Format("Job.Completed", label);
                if (warning is not null && !sensitive) ActionJournal.Append("action-complete", new { label });
                if (job is not null)
                {
                    job.State = "ABGESCHLOSSEN";
                    job.Detail = DesktopUiText.Get("Job.CompletedDetail");
                }
            }
            catch (OperationCanceledException)
            {
                Status.Text = DesktopUiText.Get("Job.Canceled");
                AppendTerminal(DesktopUiText.Get("Job.CancelOutput"));
                if (job is not null) { job.State = "ABGEBROCHEN"; job.Detail = DesktopUiText.Get("Job.CancelDetail"); }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception or InvalidOperationException
                or ArgumentException or JsonException or TimeoutException or System.Xml.XmlException
                or NotSupportedException or System.Net.Http.HttpRequestException or PingException or SecurityException
                or System.Security.Cryptography.CryptographicException or Microsoft.VisualBasic.FileIO.MalformedLineException)
            {
                Status.Text = DesktopUiText.Format("Job.Failed", label, ex.Message);
                AppendTerminal(DesktopUiText.Format("Job.ErrorOutput", ex.Message));
                if (!sensitive) Trace.TraceError("{0}: {1}", label, ex);
                if (job is not null) { job.State = "FEHLGESCHLAGEN"; job.Detail = ex.Message; }
            }
            finally
            {
                _active.Dispose();
                _active = null;
                _terminalTimer.Stop();
                _elapsed.Stop();
                TerminalInfo.Text = DesktopUiText.Format("Job.TerminalEnd", _runningLabel, _elapsed.Elapsed);
                Modules.IsEnabled = !_closing;
                ShellControls.IsEnabled = !_closing;
            }
        }
    }

    private SmartService Smart() => new(_model.Settings.SmartctlPath.Length > 0
        ? _model.Settings.SmartctlPath : SmartService.DetectExecutable());
    private SmartDevice SelectedDevice() => SmartDevices.SelectedItem as SmartDevice
        ?? throw new ArgumentException(DesktopUiText.Get("Control.SelectSmart"));

    private async void SmartScanClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(DesktopUiText.Get("Control.SmartScan"), async token =>
        {
            _smart = null;
            SmartDevices.ItemsSource = await Smart().ScanAsync(token);
            SmartDevices.SelectedIndex = 0;
            return SmartDevices.Items.Count == 0 ? DesktopUiText.Get("Control.SmartNone") :
                DesktopUiText.Format("Control.SmartCount", SmartDevices.Items.Count);
        });

    private async void SmartReadClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(DesktopUiText.Get("Control.SmartRead"), async token =>
        {
            _smart = null;
            _smart = await Smart().ReadAsync(SelectedDevice(), token);
            return _smart.Summary + DesktopUiText.Get("Control.SmartRaw") + _smart.Json;
        });

    private async void SmartExecuteClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(DesktopUiText.Get("Safety.Smart.Title"), async token =>
        {
            var operation = ((ComboBoxItem)SmartOperation.SelectedItem).Content.ToString()!;
            var result = await Smart().ExecuteAsync(SelectedDevice(), operation, SmartArgument.Text, token);
            return result.Summary + "\n" + result.Json;
        }, DesktopUiText.Get("Safety.Smart.Body"), admin: true);

    private async void SaveSmartClicked(object sender, RoutedEventArgs e)
    {
        if (_smart is null) { Status.Text = DesktopUiText.Get("Control.SmartRequired"); return; }
        var dialog = new SaveFileDialog { Filter = "SMART JSON (*.json)|*.json", FileName = "aether-smart.json" };
        if (dialog.ShowDialog(this) != true) return;
        var json = _smart.Json;
        await RunAsync(DesktopUiText.Get("Control.SmartExport"), async token =>
        {
            await File.WriteAllTextAsync(dialog.FileName, json, token);
            return DesktopUiText.Get("Control.SmartSaved");
        });
    }

    private async void PingClicked(object sender, RoutedEventArgs e)
    {
        var host = PingHost.Text.Trim();
        await RunAsync("Ping", token => NetworkTools.PingAsync(host, token),
            DesktopUiText.Format("Safety.Ping", host));
    }
    private async void RouterPingClicked(object sender, RoutedEventArgs e) =>
        await RunAsync("Router-Ping", token => NetworkTools.PingAsync(NetworkTools.DefaultGateway(), token),
            DesktopUiText.Get("Safety.RouterPing"));

    private async void SpeedtestClicked(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SpeedtestMiB.Text.Trim(), out var mib) || mib is < SpeedTest.MinMiB or > SpeedTest.MaxMiB)
        {
            Status.Text = DesktopUiText.Format("Control.SpeedSize", SpeedTest.MinMiB, SpeedTest.MaxMiB);
            return;
        }
        await RunAsync("Speedtest Download", token => SpeedTest.DownloadAsync(mib, token, ProgressOutput(raw: true)),
            DesktopUiText.Format("Safety.SpeedTest", mib, SpeedTest.TargetHost));
    }

    private List<WingetRow> _packageRows = [];
    private string _packageKind = "";

    private async void PackagesLoadClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;
        string script, label;
        try
        {
            (script, label) = kind switch
            {
                "list" => (PackageCenter.ListScript, DesktopUiText.Get("Package.ListInstalled")),
                "upgrade" => (PackageCenter.UpgradeListScript, DesktopUiText.Get("Package.ListUpdates")),
                _ => (PackageCenter.SearchScript(PackageSearch.Text), DesktopUiText.Get("Package.Search"))
            };
        }
        catch (ArgumentException ex) { PackagesInfo.Text = ex.Message; return; }
        await RunAsync(label, async token =>
        {
            var result = await AdminShell.RunAsync(script, TimeSpan.FromMinutes(10), token, ProgressOutput(raw: true));
            if (result.ExitCode != 0)
                throw new InvalidOperationException(DesktopUiText.Format("Package.NativeExit", result.ExitCode));
            var rows = PackageCenter.ParseTable(result.Output);
            _packageRows = rows;
            _packageKind = kind;
            PackagesGrid.ItemsSource = rows;
            var blocked = rows.Count(r => !r.Actionable);
            PackagesInfo.Text = DesktopUiText.Format("Package.Loaded", rows.Count, blocked) +
                (kind == "upgrade" ? DesktopUiText.Get("Package.UpdateListOnly") : "");
            return DesktopUiText.Format("Package.TableParsed", rows.Count);
        });
    }

    private async void PackageActionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        var all = action == "upgrade-all";
        var verb = all ? "upgrade" : action;
        if (_packageKind == "stale")
        { PackagesInfo.Text = DesktopUiText.Get("Package.Reload"); return; }
        if (_packageKind == "import" && verb != "install")
        { PackagesInfo.Text = DesktopUiText.Get("Package.ImportOnlyInstall"); return; }
        if (all && _packageKind != "upgrade")
        {
            PackagesInfo.Text = DesktopUiText.Get("Package.LoadUpdatesFirst");
            return;
        }
        var chosen = all ? _packageRows : PackagesGrid.SelectedItems.Cast<WingetRow>().ToList();
        if (chosen.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Select(r => r.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        { PackagesInfo.Text = DesktopUiText.Get("Package.MultipleSources"); return; }
        var skipped = chosen.Count(r => !r.Actionable);
        var actionable = chosen.Where(r => r.Actionable)
            .DistinctBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
        if (actionable.Count == 0)
        {
            PackagesInfo.Text = DesktopUiText.Get(all ? "Package.NoUpdates" : "Package.NoSelection");
            return;
        }
        if (actionable.Count > PackageCenter.MaxIds)
        {
            PackagesInfo.Text = DesktopUiText.Format("Package.Limit", PackageCenter.MaxIds);
            return;
        }
        var ids = actionable.Select(r => r.Id).ToList();
        try { foreach (var row in actionable) _ = PackageCenter.TargetCommandScript(verb, row); }
        catch (ArgumentException ex) { PackagesInfo.Text = ex.Message; return; }
        var verbText = DesktopUiText.Get(verb switch { "install" => "Package.InstallVerb", "upgrade" => "Package.UpgradeVerb", _ => "Package.UninstallVerb" });
        var warning = DesktopUiText.Format("Package.ConsentHeading", verbText, ids.Count) +
            string.Join("\n", actionable.Select(r => DesktopUiText.Format("Package.Target", r.Id, r.Name,
                r.Source.Length == 0 ? DesktopUiText.Get("Package.SourceUnknown") : r.Source))) +
            (skipped > 0 ? DesktopUiText.Format("Package.SkippedTargets", skipped) : "") +
            DesktopUiText.Get("Package.ConsentDetails") +
            (verb == "uninstall" ? DesktopUiText.Get("Package.UninstallPermanent") : "");
        await RunAsync(DesktopUiText.Format("Package.RunTitle", verbText, ids.Count),
            token => RunPackageQueueAsync(verb, actionable, token,
                (row, active) => AdminShell.RunAsync(PackageCenter.TargetCommandScript(verb, row),
                    TimeSpan.FromMinutes(180), active, ProgressOutput(raw: true)), ActionJournal.Append),
            warning, admin: true, detailedConfirmation: true);
    }

    private async void ShellbagsClicked(object sender, RoutedEventArgs e)
    {
        await RunAsync(DesktopUiText.Get("Center.ShellbagTitle"), async token =>
        {
            var entries = await Task.Run(ShellbagAnalyzer.Read, token);
            token.ThrowIfCancellationRequested();
            ShellbagGrid.ItemsSource = entries;
            ShellbagInfo.Text = DesktopUiText.Format("Center.ShellbagStatus", entries.Count, ShellbagAnalyzer.MaxEntries, entries.Count(entry => !entry.Decoded));
            return DesktopUiText.Format("Center.ShellbagResult", entries.Count);
        });
    }

    private async void PortalClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(DesktopUiText.Get("Center.PortalTitle"), NetworkTools.CaptivePortalAsync,
            DesktopUiText.Get("Center.PortalConsent"));
    private async void LanScanClicked(object sender, RoutedEventArgs e)
    {
        var cidr = LanRange.Text.Trim();
        await RunAsync("LAN-Scan", async token =>
        {
            LanGrid.ItemsSource = null;
            var devices = await NetworkTools.ScanAsync(cidr, token, ProgressOutput(), _model.Settings.OuiCsvPath);
            LanGrid.ItemsSource = devices;
            return DesktopUiText.Format("Center.LanResult", devices.Count, cidr) +
                string.Join("\n", devices.Select(d => $"{d.Address}: {d.Status} {d.Milliseconds} ms {d.Detail}"));
        }, DesktopUiText.Format("Center.LanConsent", cidr));
    }

    private async void WakeOnLanClicked(object sender, RoutedEventArgs e)
    {
        var macAddress = WakeMacAddress.Text.Trim();
        try { _ = WakeOnLan.BuildMagicPacket(macAddress); }
        catch (ArgumentException ex)
        {
            Status.Text = DesktopUiText.Format("Center.WakeInvalid", ex.Message);
            return;
        }

        await RunAsync(DesktopUiText.Get("Center.WakeTitle"), async token =>
        {
            var sent = await WakeOnLan.SendAsync(macAddress, WakeOnLan.DefaultPort, token);
            return DesktopUiText.Format("Center.WakeSent", sent, macAddress);
        }, DesktopUiText.Format("Safety.WakeOnLan", macAddress));
    }

    private void LanDeviceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (LanGrid.SelectedItem is LanDevice { Mac.Length: > 0 } device)
            WakeMacAddress.Text = device.Mac;
    }

    private async void AutoLanChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _closing) return;
        if (AutoLan.IsChecked != true)
        {
            _autoStop?.Cancel();
            return;
        }
        if (!_autoTask.IsCompleted) { Status.Text = DesktopUiText.Get("Center.AutoLanPrevious"); AutoLan.IsChecked = false; return; }
        var cidr = LanRange.Text.Trim();
        try { NetworkTools.ParseLocalRange(cidr); }
        catch (ArgumentException ex) { Status.Text = ex.Message; AutoLan.IsChecked = false; return; }
        if (MessageBox.Show(this, DesktopUiText.Format("Center.AutoLanConsent", cidr),
            DesktopUiText.Get("Center.AutoLanTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) != MessageBoxResult.Yes) { AutoLan.IsChecked = false; return; }
        _autoStop = new CancellationTokenSource();
        _autoTask = AutoScanAsync(cidr, _autoStop.Token);
        await _autoTask;
    }

    private async Task AutoScanAsync(string cidr, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && !_closing)
            {
                if (_active is null)
                    await RunAsync(DesktopUiText.Get("Center.AutoLanRun"), async active =>
                    {
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, active);
                        var devices = await NetworkTools.ScanAsync(cidr, linked.Token, ouiCsv: _model.Settings.OuiCsvPath);
                        LanGrid.ItemsSource = devices;
                        return $"{DateTime.Now:HH:mm:ss}: {devices.Count} Antworten/Fehler im freigegebenen Netz {cidr}. Naechster Scan nach 60s.";
                    });
                await Task.Delay(TimeSpan.FromSeconds(60), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Status.Text = DesktopUiText.Get("Center.AutoLanStopped"); }
        finally
        {
            _autoStop?.Dispose();
            _autoStop = null;
            AutoLan.IsChecked = false;
        }
    }

    private void AppendTerminal(string text)
    {
        if (_outputLimitReached) return;
        // Wagenruecklauf aktualisiert die laufende Fortschrittszeile ohne neue Zeile.
        var segments = text.Replace("\r\n", "\n").Split('\r');
        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0)
            {
                var current = Output.Text;
                var lineStart = current.LastIndexOf('\n') + 1;
                if (current.Length > lineStart)
                {
                    Output.Select(lineStart, current.Length - lineStart);
                    Output.SelectedText = "";
                    Output.Select(lineStart, 0);
                }
            }
            if (segments[i].Length == 0) continue;
            var remaining = 2_000_000 - Output.Text.Length;
            if (segments[i].Length > remaining)
            {
                Output.AppendText(segments[i][..Math.Max(0, remaining)] +
                    "\n[ANZEIGELIMIT] 2 Millionen Zeichen erreicht. Weitere Ausgabe wird nicht angezeigt.\n");
                _outputLimitReached = true;
                break;
            }
            Output.AppendText(segments[i]);
        }
        _lastOutput = DateTimeOffset.Now;
        if (FollowOutput.IsChecked == true) Output.ScrollToEnd();
    }

    private void UpdateTerminalInfo()
    {
        TerminalInfo.Text = $"RUN / {_runningLabel} / Laufzeit {_elapsed.Elapsed:hh\\:mm\\:ss} / " +
            $"letzte Ausgabe vor {(DateTimeOffset.Now - _lastOutput).TotalSeconds:0}s / " +
            "keine neue Ausgabe bedeutet nicht automatisch Stillstand";
    }

    private sealed class TerminalProgress(Action<string> write) : IProgress<string>
    {
        public void Report(string value) => write(value);
    }

    // InvokeAsync statt Invoke: Der Ausgabepumpen-Thread darf weder auf einen beschaeftigten
    // UI-Thread warten noch das Schliessen verzoegern; nach Beginn des Schliessens wird nur
    // die Anzeige verworfen, die Pipe selbst wird im CommandRunner weiter drainiert.
    private IProgress<string> ProgressOutput(bool raw = false) => new TerminalProgress(text =>
        Dispatcher.InvokeAsync(() =>
        {
            if (!_closing) AppendTerminal(raw ? text : text + Environment.NewLine);
        }));

    private Task ExecuteSystemAction(SystemAction action) => RunAsync(action.Label, async token =>
    {
        AppendTerminal(DesktopUiText.Format("Safety.ToolCommand", action.Executable,
            string.Join(" ", action.Arguments), action.Timeout.TotalMinutes, action.Warning) +
            (action.Id.StartsWith("sfc", StringComparison.Ordinal)
                ? $"[DETAILLOG] {Environment.GetFolderPath(Environment.SpecialFolder.Windows)}\\Logs\\CBS\\CBS.log\n" :
                action.Id.StartsWith("dism", StringComparison.Ordinal)
                    ? $"[DETAILLOG] {Environment.GetFolderPath(Environment.SpecialFolder.Windows)}\\Logs\\DISM\\dism.log\n" : "") +
            DesktopUiText.Get("Safety.ToolStream"));
        var result = await CommandRunner.RunAsync(action.Executable, action.Arguments, action.Timeout, token,
            ProgressOutput(raw: true), outputEncoding: action.Id.StartsWith("sfc", StringComparison.Ordinal) ? Encoding.Unicode : null);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(DesktopUiText.Format("Control.ToolExitFailed", result.ExitCode));
        return DesktopUiText.Format("Control.ToolExit", result.ExitCode);
    }, action.Warning, action.Admin);
    private async void ActionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) await ExecuteSystemAction(SystemActions.Catalog.Single(a => a.Id == id));
    }
    private async void MaintenanceClicked(object sender, RoutedEventArgs e)
    {
        if (MaintenanceActions.SelectedItem is SystemAction action) await ExecuteSystemAction(action);
    }

    private void CommandSearchChanged(object sender, TextChangedEventArgs e) => RefreshCommandList();

    private void CommandSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CommandList.SelectedItem is not AdminCommand command) return;
        CopyCatalogCommand(command);
    }

    private void CatalogCommandClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AdminCommand command }) return;
        CommandList.SelectedItem = command;
        CopyCatalogCommand(command);
    }

    private void CopyCatalogCommand(AdminCommand command)
    {
        CommandDescription.Text = command.Description;
        CommandRisk.Text = command.RiskBadge;
        if (SkinCatalog.IsModern) CommandRisk.SetResourceReference(TextBlock.ForegroundProperty, "ModernWarning");
        else CommandRisk.Foreground = command.RiskAccent;
        CommandImpact.Text = command.Impact;
        var (whenUseful, caution) = AdminCommandCatalog.Guidance(command.Category);
        CommandWhenUseful.Text = "SINNVOLL: " + whenUseful;
        CommandCaution.Text = "VORSICHT: " + caution;
        CommandParameters.ItemsSource = command.Parameters;
        CommandInput.Text = command.Command;
        CommandInput.CaretIndex = CommandInput.Text.Length;
        CommandInput.Focus();
    }

    private void ParameterClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AdminParameter parameter }) return;
        var position = CommandInput.SelectionStart;
        var before = position > 0 && !char.IsWhiteSpace(CommandInput.Text[position - 1]) ? " " : "";
        var end = position + CommandInput.SelectionLength;
        var after = end < CommandInput.Text.Length && !char.IsWhiteSpace(CommandInput.Text[end]) ? " " : "";
        CommandInput.SelectedText = before + parameter.Text + after;
        CommandInput.CaretIndex = position + before.Length + parameter.Text.Length + after.Length;
        CommandInput.Focus();
    }

    private async void ShellRunClicked(object sender, RoutedEventArgs e) => await RunShellAsync();

    private async void CommandInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunShellAsync();
        }
        else if (e.Key is Key.Up or Key.Down && _commandHistory.Count > 0)
        {
            e.Handled = true;
            if (_historyIndex == _commandHistory.Count) _historyDraft = CommandInput.Text;
            _historyIndex = Math.Clamp(_historyIndex + (e.Key == Key.Up ? -1 : 1), 0, _commandHistory.Count);
            CommandInput.Text = _historyIndex == _commandHistory.Count ? _historyDraft : _commandHistory[_historyIndex];
            CommandInput.CaretIndex = CommandInput.Text.Length;
        }
    }

    private async Task RunShellAsync()
    {
        if (_active is not null || _closing)
        {
            Status.Text = DesktopUiText.Get("Center.ActionRunning");
            return;
        }
        var command = CommandInput.Text.Trim();
        if (command.Length == 0)
        {
            Status.Text = DesktopUiText.Get("Center.CommandEmpty");
            return;
        }
        if (!_model.Settings.ExpertMode)
        {
            Status.Text = DesktopUiText.Get("Center.CommandBlocked");
            return;
        }
        if (!int.TryParse(CommandTimeout.Text, out var minutes) || minutes is < 1 or > 1440)
        {
            Status.Text = DesktopUiText.Get("Center.CommandTimeout");
            return;
        }
        // Free commands may print secrets. Keep both their history and output out of persistent logs.
        if (_commandHistory.Count == 0 || _commandHistory[^1] != command) _commandHistory.Add(command);
        if (_commandHistory.Count > 100) _commandHistory.RemoveAt(0);
        _historyIndex = _commandHistory.Count;
        await RunAsync(DesktopUiText.Get("Center.CommandTitle"), async token =>
        {
            AppendTerminal(DesktopUiText.Format("Center.CommandHeading", command));
            var result = await AdminShell.RunAsync(command, TimeSpan.FromMinutes(minutes), token, ProgressOutput(raw: true));
            if (result.ExitCode != 0)
                throw new InvalidOperationException(DesktopUiText.Format("Center.CommandExit", result.ExitCode));
            return DesktopUiText.Get("Center.CommandSuccess");
        }, admin: true, sensitive: true);
        if (!_closing) CommandInput.Focus();
    }

    private async void ExplorerClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(DesktopUiText.Get("Center.ExplorerTitle"), async token =>
        {
            using var current = Process.GetCurrentProcess();
            var session = current.SessionId;
            var explorers = Process.GetProcessesByName("explorer");
            try
            {
                foreach (var explorer in explorers)
                {
                    if (explorer.SessionId != session) continue;
                    explorer.Kill(entireProcessTree: false);
                    await explorer.WaitForExitAsync(token);
                }
            }
            finally
            {
                foreach (var explorer in explorers) explorer.Dispose();
                using var restart = Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                    UseShellExecute = true
                }) ?? throw new InvalidOperationException(DesktopUiText.Get("Center.ExplorerFailed"));
            }
            return DesktopUiText.Get("Center.ExplorerResult");
        }, DesktopUiText.Get("Safety.ExplorerRestart"));

    private async void DeliveryClicked(object sender, RoutedEventArgs e) =>
        await RunAsync("Delivery Optimization", token => CommandRunner.PowerShellAsync(
            DesktopScriptText.Preamble("Center.DeliveryResult") + "Delete-DeliveryOptimizationCache -Force; $aetherDesktopText['Center.DeliveryResult']", token),
            DesktopUiText.Get("Safety.DeliveryCleanup"), admin: true);
    private async void CertificatesClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(DesktopUiText.Get("Center.CertificatesTitle"), token => Task.Run(() => string.Join("\n", CertificateDiagnostics.Read(token)
            .Select(c => $"{c.Name}\n{c.State} | {c.Value}\n{c.Detail}\n")), token));
    private async void StabilityClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(DesktopUiText.Get("Center.StabilityTitle"), token => CommandRunner.PowerShellAsync(SystemActions.StabilityScript, token));
    private async void OpenJournalClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(DesktopUiText.Get("Center.JournalTitle"), _ =>
        {
            Directory.CreateDirectory(ActionJournal.DirectoryPath);
            using var process = Process.Start(new ProcessStartInfo { FileName = ActionJournal.DirectoryPath, UseShellExecute = true });
            return Task.FromResult(DesktopUiText.Get("Center.JournalOpened"));
        });

    private async void StartupReadClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(DesktopUiText.Get("Center.StartupTitle"), _ =>
        {
            _startupRows = _startup.Read();
            RefreshStartup();
            return Task.FromResult(DesktopUiText.Get("Center.StartupRead"));
        });
    private List<StartupEntry> _startupRows = [];
    internal static IReadOnlyList<StartupEntry> FilterStartup(IEnumerable<StartupEntry> rows, string query) =>
        rows.Where(row => $"{row.Hive} {row.Name} {row.Command}".Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    private void StartupSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (StartupEntries is not null) RefreshStartup();
    }
    private void RefreshStartup()
    {
        StartupEntries.ItemsSource = FilterStartup(_startupRows, StartupSearch.Text);
        StartupEntries.SelectedIndex = StartupEntries.Items.Count > 0 ? 0 : -1;
    }
    private void StartupSelected(object sender, SelectionChangedEventArgs e)
    {
        if (StartupDetails is null) return;
        StartupDetails.Text = StartupEntries.SelectedItem is StartupEntry row
            ? $"{row.Hive} / {row.Name} / {(row.Enabled ? "Run-Wert vorhanden" : "lokale Sicherung")}\n{row.Command}"
            : "Kein Eintrag ausgewaehlt.";
    }
    private async void StartupChangeClicked(object sender, RoutedEventArgs e)
    {
        if (StartupEntries.SelectedItem is not StartupEntry entry) { Status.Text = DesktopUiText.Get("Control.SelectStartup"); return; }
        var enabled = sender is Button { Tag: "on" };
        if (entry.Enabled == enabled) { Status.Text = DesktopUiText.Get("Control.StartupUnchanged"); return; }
        await RunAsync(DesktopUiText.Get("Control.StartupChange"), _ =>
        {
            _startup.SetEnabled(entry, enabled);
            _startupRows = _startup.Read();
            RefreshStartup();
            return Task.FromResult(DesktopUiText.Format("Control.StartupResult", entry.Name,
                DesktopUiText.Get(enabled ? "Control.StartupEnabled" : "Control.StartupDisabled")));
        }, DesktopUiText.Format("Safety.StartupChange", entry.Hive, entry.Name,
            DesktopUiText.Get(enabled ? "Control.StartupEnable" : "Control.StartupDisable"), entry.Command));
    }

    private async void OpenLogClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "AETHER JSONL (*.jsonl)|*.jsonl" };
        if (dialog.ShowDialog(this) != true) return;
        await RunAsync("Messlog", async token =>
        {
            _log = null;
            LogSeries.ItemsSource = null;
            LogCurve.Points = [];
            var report = await Task.Run(() => HistoryReader.Read(dialog.FileName, token), token);
            _log = report;
            LogSeries.ItemsSource = report.Series;
            LogSeries.SelectedIndex = 0;
            return $"{report.Series.Count} Reihen geladen.\n" + string.Join("\n", report.Issues);
        });
    }
    private async void CompareLogClicked(object sender, RoutedEventArgs e)
    {
        if (_log is null) { Status.Text = "Ersten Messlog laden."; return; }
        var first = _log;
        var dialog = new OpenFileDialog { Filter = "AETHER JSONL (*.jsonl)|*.jsonl" };
        if (dialog.ShowDialog(this) != true) return;
        await RunAsync("Messlog-Vergleich", async token =>
        {
            var second = await Task.Run(() => HistoryReader.Read(dialog.FileName, token), token);
            return HistoryReader.Compare(first, second) + "\n" + string.Join("\n", first.Issues.Concat(second.Issues));
        });
    }
    private void LogSeriesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogSeries.SelectedItem is not HistorySeries series || series.Points.Count == 0) return;
        var points = new PointCollection();
        var min = series.Points.Min(p => p.Value);
        var max = series.Points.Max(p => p.Value);
        var span = (series.Points[^1].Timestamp - series.Points[0].Timestamp).TotalSeconds;
        var stride = Math.Max(1, series.Points.Count / 1500);
        for (var i = 0; i < series.Points.Count; i += stride)
        {
            var point = series.Points[i];
            points.Add(new(span <= 0 ? 800 : (point.Timestamp - series.Points[0].Timestamp).TotalSeconds / span * 800,
                max == min ? 55 : 110 * (1 - (point.Value - min) / (max - min))));
        }
        LogCurve.Points = points;
        LogPoints.ItemsSource = series.Points;
        LogSummary.Text = series.Summary + DesktopUiText.Format("Center.LogRange", series.Points[0].Timestamp, series.Points[^1].Timestamp);
    }

    private async void CaptureClicked(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CaptureSeconds.Text, out var seconds)) { Status.Text = DesktopUiText.Get("Center.CaptureDuration"); return; }
        var processName = GameProcess.Text.Trim();
        var dialog = new SaveFileDialog { Filter = "PresentMon CSV (*.csv)|*.csv", FileName = $"frames-{DateTime.Now:yyyyMMdd-HHmmss}.csv" };
        if (dialog.ShowDialog(this) != true) return;
        _captureSession = "Aether-" + Guid.NewGuid().ToString("N");
        await RunAsync(DesktopUiText.Get("Center.CaptureTitle"), async token =>
        {
            var exe = _model.Settings.PresentMonPath.Length > 0 ? _model.Settings.PresentMonPath : FrameTelemetry.DetectExecutable();
            if (!File.Exists(exe)) throw new FileNotFoundException(DesktopUiText.Get("Center.PresentMonMissing"));
            var arguments = FrameTelemetry.CaptureArguments(processName, seconds, dialog.FileName, _captureSession);
            string cleanupHint = "";
            try
            {
                var result = await CommandRunner.RunAsync(exe, arguments, TimeSpan.FromSeconds(seconds + 30), token, ProgressOutput(raw: true));
                if (result.ExitCode != 0) throw new IOException($"PresentMon Exit {result.ExitCode}: {result.Output}\n{result.Error}");
            }
            finally
            {
                // Only our unique ETW session is terminated; never interfere with other PresentMon captures.
                var cleanup = await CommandRunner.RunAsync(exe, ["--session_name", _captureSession, "--terminate_existing_session"],
                    TimeSpan.FromSeconds(10), CancellationToken.None);
                if (cleanup.ExitCode != 0) cleanupHint = DesktopUiText.Format("Center.EtwCleanup", cleanup.ExitCode, cleanup.Output, cleanup.Error);
            }
            var summary = FrameTelemetry.Read(dialog.FileName);
            if (summary.Count == 0) throw new InvalidDataException(DesktopUiText.Get("Center.CaptureEmpty"));
            _frames = summary;
            return string.Join("\n", summary) + cleanupHint;
        }, DesktopUiText.Format("Center.CaptureConsent", processName, seconds));
    }
    private async void FrameCsvClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PresentMon CSV (*.csv)|*.csv" };
        if (dialog.ShowDialog(this) != true) return;
        await RunAsync(DesktopUiText.Get("Center.FrameAnalysisTitle"), async token =>
        {
            var summary = await Task.Run(() => FrameTelemetry.Read(dialog.FileName), token);
            if (summary.Count == 0) throw new InvalidDataException(DesktopUiText.Get("Center.FramesEmpty"));
            _frames = summary;
            return string.Join("\n", summary);
        });
    }

    private async void CompareFramesClicked(object sender, RoutedEventArgs e)
    {
        if (_frames is null) { Status.Text = DesktopUiText.Get("Center.FramesLoadFirst"); return; }
        var first = _frames;
        var dialog = new OpenFileDialog { Filter = "PresentMon CSV (*.csv)|*.csv" };
        if (dialog.ShowDialog(this) != true) return;
        await RunAsync(DesktopUiText.Get("Center.FrameCompareTitle"), token => Task.Run(() =>
        {
            var second = FrameTelemetry.Read(dialog.FileName);
            if (second.Count == 0) throw new InvalidDataException(DesktopUiText.Get("Center.FramesSecondEmpty"));
            return DesktopUiText.Format("Center.FrameComparison", string.Join("\n", first), string.Join("\n", second));
        }, token));
    }

    private void ToolPathClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = DesktopUiText.Get("Center.ToolFilter") };
        if (dialog.ShowDialog(this) != true) return;
        if (sender is Button { Tag: "smart" }) SmartPath.Text = dialog.FileName;
        else PresentPath.Text = dialog.FileName;
    }
    private void OuiPathClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "IEEE OUI CSV (*.csv)|*.csv" };
        if (dialog.ShowDialog(this) == true) OuiPath.Text = dialog.FileName;
    }
    private async void SaveSettingsClicked(object sender, RoutedEventArgs e)
    {
        var clientMode = ManagedClientMode.IsChecked == true;
        var reportingEnabled = clientMode && ManagerReportingToggle.IsChecked == true;
        var mainWindow = Application.Current.MainWindow as MainWindow;
        if (reportingEnabled && mainWindow?.ManagerDeviceTokenAvailable != true)
        {
            MessageBox.Show(this, DesktopUiText.Get("Center.ManagerUnregistered"),
                DesktopUiText.Get("Center.ManagerTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            ManagerReportingToggle.IsChecked = false;
            return;
        }
        if (mainWindow?.ManagerDeviceTokenAvailable == true &&
            !string.Equals(ManagerServerUrl.Text.Trim().TrimEnd('/'),
                _model.Settings.ManagerServerUrl.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, DesktopUiText.Get("Center.ManagerBound"),
                DesktopUiText.Get("Center.ManagerTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (reportingEnabled && !_model.Settings.ManagerReportingEnabled &&
            MessageBox.Show(this,
                DesktopUiText.Format("Center.ManagerConsent", ManagerServerUrl.Text.Trim()),
                DesktopUiText.Get("Center.ManagerConsentTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            ManagerReportingToggle.IsChecked = false;
            Status.Text = DesktopUiText.Get("Center.ManagerDisabled");
            return;
        }

        await RunAsync(DesktopUiText.Get("Center.SettingsTitle"), _ =>
        {
            static double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
                ? value : throw new ArgumentException(DesktopUiText.Get("Center.NumberInvalid"));
            if (!int.TryParse(RecordSeconds.Text, out var seconds)) throw new ArgumentException(DesktopUiText.Get("Center.IntervalInvalid"));
            var settings = _model.Settings with
            {
                CpuWarningC = Number(CpuThreshold.Text),
                GpuWarningC = Number(GpuThreshold.Text),
                StorageWarningC = Number(StorageThreshold.Text),
                FreeSpaceWarningPercent = Number(FreeThreshold.Text),
                VoltageTolerancePercent = Number(VoltageThreshold.Text),
                CmosMinimumV = Number(CmosThreshold.Text),
                RecordIntervalSeconds = seconds,
                SmartctlPath = SmartPath.Text.Trim(),
                PresentMonPath = PresentPath.Text.Trim(),
                OuiCsvPath = OuiPath.Text.Trim(),
                ManagerClientMode = clientMode,
                ManagerServerUrl = ManagerServerUrl.Text.Trim(),
                ManagerReportingEnabled = reportingEnabled,
                ExpertMode = _model.Settings.ExpertMode,
                Skin = (SkinSelect.SelectedItem as SkinInfo)?.Id ?? "retro-neon",
                OverlayMonitorDeviceName = _model.Settings.OverlayMonitorDeviceName,
                OverlayDockSide = _model.Settings.OverlayDockSide,
                OverlayVerticalOffset = _model.Settings.OverlayVerticalOffset,
                SidebarMonitorDeviceName = _model.Settings.SidebarMonitorDeviceName,
                SidebarDockSide = _model.Settings.SidebarDockSide,
                SidebarVerticalOffset = _model.Settings.SidebarVerticalOffset,
                SidebarHorizontalOffset = _model.Settings.SidebarHorizontalOffset,
                SidebarPinned = _model.Settings.SidebarPinned,
                SidebarAutoHide = _model.Settings.SidebarAutoHide,
                SidebarWidgetCpu = _model.Settings.SidebarWidgetCpu,
                SidebarWidgetMemory = _model.Settings.SidebarWidgetMemory,
                SidebarWidgetGpu = _model.Settings.SidebarWidgetGpu,
                SidebarWidgetTemperatures = _model.Settings.SidebarWidgetTemperatures,
                SidebarWidgetNetwork = _model.Settings.SidebarWidgetNetwork,
                SidebarWidgetDrives = _model.Settings.SidebarWidgetDrives,
                SidebarShortcuts = _model.Settings.SidebarShortcuts
            };
            settings.Save();
            _model.Settings = settings;
            mainWindow?.UpdateManagerReportingSettings(settings);
            UpdateManagerModeControls();
            ManagerStatus.Text = !clientMode
                ? DesktopUiText.Get("Center.StandaloneSaved")
                : reportingEnabled
                    ? DesktopUiText.Get("Center.ManagedSaved")
                    : mainWindow?.ManagerDeviceTokenAvailable == true
                        ? DesktopUiText.Get("Center.ManagedReportsOff")
                        : DesktopUiText.Get("Center.ManagedEnrollFirst");
            var warnings = settings.OptionalFileWarnings();
            return Task.FromResult(DesktopUiText.Get("Center.SettingsSaved") +
                (warnings.Count == 0 ? "" : DesktopUiText.Format("Center.SettingsWarnings", string.Join("\n", warnings))));
        });
    }

    private async void EnrollManagerClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(DesktopUiText.Get("Center.EnrollTitle"), async token =>
        {
            var mainWindow = Application.Current.MainWindow as MainWindow
                ?? throw new InvalidOperationException(DesktopUiText.Get("Center.MainMissing"));
            if (mainWindow.ManagerDeviceTokenAvailable)
                throw new InvalidOperationException(DesktopUiText.Get("Center.TokenExists"));

            var settings = _model.Settings with
            {
                ManagerServerUrl = ManagerServerUrl.Text.Trim(),
                ManagerClientMode = true,
                ManagerReportingEnabled = false
            };
            settings.Save();
            _model.Settings = settings;
            mainWindow.UpdateManagerReportingSettings(settings);
            var result = await mainWindow.EnrollManagerAsync(settings.ManagerServerUrl, ManagerEnrollmentCode.Password, token);
            ManagerEnrollmentCode.Clear();
            UpdateManagerModeControls();
            ManagerStatus.Text = DesktopUiText.Format("Center.EnrollPending", result.DeviceId);
            return DesktopUiText.Get("Center.Enrolled");
        }, warning: DesktopUiText.Get("Center.EnrollConsent"),
            sensitive: true);
    private async void SensorChecksClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(DesktopUiText.Get("Center.SensorProfile"), _ => Task.FromResult(string.Join("\n", SensorDiagnostics.Evaluate(_model.Sensors, _model.Settings)
            .Select(c => $"{c.Name} | {c.State} | {c.Value} | {c.Detail}"))));
    private void DesktopSettingsClicked(object sender, RoutedEventArgs e) =>
        (Application.Current.MainWindow as MainWindow)?.OpenDesktopSettings();
    private void NetworkMonitorClicked(object sender, RoutedEventArgs e) =>
        (Application.Current.MainWindow as MainWindow)?.OpenNetworkMonitor();
    private async void SecretClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;
        var profile = WifiProfile.Text;
        await RunAsync(DesktopUiText.Get("Safety.Secrets.Title"), token => CommandRunner.PowerShellAsync(SystemActions.SecretScript(kind, profile), token),
            DesktopUiText.Get("Safety.Secrets.Body"),
            admin: true, sensitive: true);
    }
    private void ClearOutputClicked(object sender, RoutedEventArgs e)
    {
        Output.Clear();
        CommandInput.Clear();
        _commandHistory.Clear();
        _historyIndex = 0;
        _historyDraft = "";
    }
    private void ModuleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Modules || Output is null) return;
        if (Modules.SelectedItem is TabItem { Header: string header, Tag: string route } && CcTrail is not null)
        {
            if (!_trailNavigating) _trail.Visit(route);
            CcTrail.Text = DesktopUiText.Format("ControlCenter.Trail", header.ToUpperInvariant());
            CcBack.IsEnabled = _trail.CanBack;
            CcForward.IsEnabled = _trail.CanForward;
            if (route == "Wartung") SetTerminalVisible(true);
        }
        if (_sensitiveOutput || Modules.SelectedItem is TabItem { Tag: "Schluessel" })
        {
            Output.Clear();
            CommandInput.Clear();
            _commandHistory.Clear();
            _historyIndex = 0;
            _historyDraft = "";
            _sensitiveOutput = false;
        }
    }
    private bool _terminalVisible = true;
    private void SetTerminalVisible(bool visible)
    {
        _terminalVisible = visible;
        TerminalFrame.Visibility = TerminalSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        TerminalRow.MinHeight = visible ? 180 : 0;
        TerminalRow.Height = !visible ? new GridLength(0) : _terminalExpanded
            ? new GridLength(1, GridUnitType.Star) : new GridLength(Height < 900 ? 220 : 300);
    }

    private void ToggleTerminalClicked(object sender, RoutedEventArgs e)
    {
        _terminalExpanded = false;
        ExpandTerminalButton.Content = DesktopUiText.Get("Terminal.Expand");
        Modules.Visibility = Visibility.Visible;
        ModuleRow.Height = new GridLength(1, GridUnitType.Star);
        SetTerminalVisible(!_terminalVisible);
    }

    private void ExpandTerminalClicked(object sender, RoutedEventArgs e)
    {
        _terminalExpanded = !_terminalExpanded;
        Modules.Visibility = _terminalExpanded ? Visibility.Collapsed : Visibility.Visible;
        ModuleRow.Height = _terminalExpanded ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        TerminalRow.Height = _terminalExpanded ? new GridLength(1, GridUnitType.Star) : new GridLength(300);
        if (sender is Button button) button.Content = DesktopUiText.Get(_terminalExpanded ? "Terminal.ShowModules" : "Terminal.Expand");
    }
    private void CancelClicked(object sender, RoutedEventArgs e) { _active?.Cancel(); _autoStop?.Cancel(); }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _terminalTimer.Stop();
        _autoStop?.Cancel();
        _active?.Cancel();
        await Task.WhenAll(_operation, _autoTask);
        Output.Clear();
        CommandInput.Clear();
        _commandHistory.Clear();
        _allowClose = true;
        await Dispatcher.InvokeAsync(Close);
    }
}
