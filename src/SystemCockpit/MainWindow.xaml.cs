using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Globalization;
using System.Text;
using SystemCockpit.Services;

namespace SystemCockpit;

public partial class MainWindow : Window
{
    private readonly CockpitModel _model = new();
    private readonly NativeMetrics _metrics = new();
    private readonly DashboardMetrics _dashboardMetrics = new();
    private readonly HardwareSensors _sensors = new();
    private readonly WindowsProbe _probe = new();
    private readonly ManagerReportingService _managerReporting;
    private readonly BackgroundDiagnosticMonitor _backgroundDiagnostics;
    private readonly CancellationTokenSource _stop = new();
    private readonly DisplayPulse _displayPulse;
    private readonly SemaphoreSlim _sensorGate = new(1);
    private readonly SemaphoreSlim _processRefresh = new(0, 1);
    private readonly LocalHistory _history = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherCockpit", "History"));
    private Task _samplingTask = Task.CompletedTask;
    private Task _probeTask = Task.CompletedTask;
    private Task _eventTask = Task.CompletedTask;
    private Task _boardFeedTask = Task.CompletedTask;
    private Task _managerReportingTask = Task.CompletedTask;
    private Task _backgroundDiagnosticsTask = Task.CompletedTask;
    private Task _currentProbe = Task.CompletedTask;
    private bool _sensorsEnabled, _recording, _allowClose, _closing;
    private DateTimeOffset _lastRecorded = DateTimeOffset.MinValue;
    private TempCleaner _cleaner = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
    private CancellationTokenSource? _cleanupStop;
    private Task _cleanupTask = Task.CompletedTask;
    private string _bootTranscript = "";
    private bool _suppressSensorToggle;
    private string? _configurationIssue;
    private SidebarWindow? _sidebar;
    private DesktopController? _desktop;
    private bool _exitRequested, _monitoringStarted;
    internal CockpitModel Model => _model;

    public MainWindow() : this(null) { }

    internal MainWindow(CockpitSettings? syntheticSettings)
    {
        InitializeComponent();
        HardwareIntelligence.Synthetic = syntheticSettings is not null;
        ProcessCenter.Synthetic = syntheticSettings is not null;
        SystemIntelligence.Synthetic = syntheticSettings is not null;
        SystemIntelligence.HardwareSnapshot = () => HardwareIntelligence.Snapshot;
        SystemIntelligence.NotificationRequested += message => { _model.Footer = message; _desktop?.NotifyDiagnostics(message); };
        ProcessCenter.ActionObserved += result => SystemIntelligence.Engine.Mark(
            result.State == ProcessActionState.Exited ? "ProcessConfirmedEnd" : "ProcessAction",
            result.Identity + " / " + result.State, result.Message, DateTimeOffset.UtcNow, "process:" + result.Identity);
        SystemIntelligence.NavigationRequested += entity =>
        {
            if (entity.Kind == IntelligenceKind.Process && entity.Id.StartsWith("process:", StringComparison.Ordinal))
            {
                SelectPage("Taskmanager");
                ProcessCenter.QuickAction("process-id:" + entity.Id[8..]);
            }
            else if (entity.Kind == IntelligenceKind.Event && entity.Fields.TryGetValue("channel", out var channel) &&
                long.TryParse(entity.Fields.GetValueOrDefault("recordId"), out var recordId))
            {
                SelectPage("Ereignisse");
                _ = EmbeddedEvents.NavigateToAsync(channel, recordId, entity.Metadata.Timestamp);
            }
            else if (entity.Kind == IntelligenceKind.Hardware && entity.Id.StartsWith("hardware:", StringComparison.Ordinal) &&
                HardwareIntelligence.SelectExisting(entity.Id[9..])) SelectPage("HardwareIntelligence");
            else if (entity.Kind == IntelligenceKind.Service)
            { SelectPage("Superadmin"); Workbench.SelectArea("services"); }
            else _model.Footer = "Quelle und genaue Identität stehen im Intelligence-Inspektor; keine heuristische Navigation.";
        };
        ProcessCenter.HardwareSnapshot = () => HardwareIntelligence.Snapshot;
        HardwareIntelligence.ProcessLinksRequested += component =>
        {
            SelectPage("Taskmanager");
            ProcessCenter.Reverse(component);
        };
        ProcessCenter.AttachDashboard(DashboardProcesses);
        ProcessCenter.RefreshRequested += () =>
        {
            if (_processRefresh.CurrentCount == 0) _processRefresh.Release();
            _model.Footer = "Prozessaktion abgeschlossen; gemeinsame Erfassung zur Aktualisierung angefordert.";
        };
        InitializeNavigation();
        _displayPulse = new DisplayPulse(Dispatcher, _model, day => DisplayTime.Apply(this, day));
        _managerReporting = new ManagerReportingService(status =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_closing)
                        _model.Footer = status;
                });
        });
        _backgroundDiagnostics = new BackgroundDiagnosticMonitor(message =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(() =>
                {
                    if (_closing) return;
                    _model.Footer = message;
                    _desktop?.NotifyDiagnostics(message);
                });
        }, status =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_closing) _model.Footer = status;
                });
        });
        DataContext = _model;
        if (SkinCatalog.IsModern) NeonDesignTagline.Visibility = Visibility.Collapsed;
        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CockpitModel.CleanupFilter))
                Dispatcher.BeginInvoke(UpdateCleanupSelection);
            if (e.PropertyName == nameof(CockpitModel.Settings))
                _backgroundDiagnostics.Configure(_model.Settings.BackgroundDiagnosticsEnabled);
        };
        // Startpunkt des Navigationsverlaufs; spaetere Events deduplizieren sich selbst.
        _trail.Visit("Cockpit");
        RefreshHelp();
        Operations.CleanerRequested += (_, _) => SelectPage("Cleaner");
        using var identity = WindowsIdentity.GetCurrent();
        _model.AccessStatus = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
            ? "ACCESS / ADMINISTRATOR" : "ACCESS / STANDARD";
        try
        {
            _model.Settings = syntheticSettings ?? CockpitSettings.Load();
            var optionalWarnings = _model.Settings.OptionalFileWarnings();
            if (optionalWarnings.Count > 0)
            {
                _configurationIssue = string.Join("\n", optionalWarnings);
                _model.Footer = _configurationIssue;
            }
            if (syntheticSettings is null) LoadCacheTargets(0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            _configurationIssue = ex.Message;
            ReportError("Einstellungen/Cache-Katalog", ex);
            MessageBox.Show(this, ex.Message, DesktopUiText.Get("Main.ConfigurationUnreadable"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Loaded += (_, _) =>
        {
            if (syntheticSettings is not null) return;
            _displayPulse.Start();
            StartBackgroundServices();
        };
        IsVisibleChanged += (_, _) => { if (syntheticSettings is null && IsVisible && IsLoaded) _displayPulse.Start(); else _displayPulse.Stop(); };
        if (syntheticSettings is not null)
        {
            _allowClose = true;
            Operations.Shutdown();
            foreach (var board in SyntheticDescendants(this).OfType<Controls.HardwareBoardView>()) board.Shutdown();
        }
        Closed += (_, _) => _displayPulse.Dispose();
        Closing += OnClosing;
    }

    private static IEnumerable<DependencyObject> SyntheticDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in SyntheticDescendants(child)) yield return descendant;
        }
    }

    internal void StartBackgroundServices()
    {
        if (_monitoringStarted) return;
        _monitoringStarted = true;
        SystemIntelligence.EnsureInitialized();
        _samplingTask = SampleLoopAsync();
        _probeTask = ProbeLoopAsync();
        _eventTask = EventLoopAsync();
        _boardFeedTask = BoardLiveFeed.ServeAsync(_model, Dispatcher, _stop.Token);
        _managerReporting.Configure(_model.Settings);
        _managerReportingTask = _managerReporting.RunAsync(_stop.Token);
        _backgroundDiagnostics.Configure(_model.Settings.BackgroundDiagnosticsEnabled);
        _backgroundDiagnosticsTask = _backgroundDiagnostics.RunAsync(_stop.Token);
    }

    internal bool ManagerDeviceTokenAvailable => _managerReporting.HasDeviceToken;

    internal Task<EnrollmentResult> EnrollManagerAsync(string serverUrl, string code, CancellationToken cancellationToken) =>
        _managerReporting.EnrollAsync(serverUrl, code, cancellationToken);

    internal void UpdateManagerReportingSettings(CockpitSettings settings)
    {
        _model.Settings = settings;
        _managerReporting.Configure(settings);
    }

    internal void InitializeDesktop(bool logon)
    {
        _desktop = new DesktopController(this, _model, SaveDesktopSettings, RequestExit, () => Operations.OpenWindowsAssistants());
        if (logon) _desktop.StartLogonCapture();
    }

    internal void OpenDesktopSettings() => _desktop?.OpenSettings();
    private void DisplaySettingsClicked(object sender, RoutedEventArgs e) => OpenDesktopSettings();
    internal void OpenNetworkMonitor() => _desktop?.OpenNetwork();

    private void SaveDesktopSettings(CockpitSettings settings)
    {
        try { settings.Save(); _model.Settings = settings; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ReportError("Desktop-Einstellungen nicht gespeichert", ex);
            MessageBox.Show(ex.Message, DesktopUiText.Get("Main.SettingsNotSaved"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    internal void RequestExit()
    {
        _exitRequested = true;
        Close();
    }

    public async Task<bool> InitializeHardwareAsync(Action<string, int> report, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        report(DesktopUiText.Get(SkinCatalog.IsModern ? "Boot.ModernResourcesLoaded" : "Boot.ResourcesLoaded"), 8);
        report(_configurationIssue is null ? DesktopUiText.Get("Boot.SettingsLoaded") :
            DesktopUiText.Format("Boot.ConfigurationWarning", _configurationIssue), 12);
        report(DesktopUiText.Get("Boot.NativeBaseline"), 17);
        var sample = await Task.Run(_metrics.Read, token);
        report(DesktopUiText.Format("Boot.Memory", sample.MemoryTotalGb, sample.Volumes.Count), 22);
        report("[INIT] LibreHardwareMonitor / CPU, GPU, RAM, Mainboard, Controller, Storage", 25);
        report(DesktopUiText.Get("Boot.DriverNotice"), 25);
        var warnings = _configurationIssue is not null;
        await _sensorGate.WaitAsync(token);
        try
        {
            try
            {
                var readings = await Task.Run(() => { _sensors.Open(); return _sensors.Read(); }, token);
                token.ThrowIfCancellationRequested();
                _sensorsEnabled = true;
                _suppressSensorToggle = true;
                try { SensorToggle.IsChecked = true; }
                finally { _suppressSensorToggle = false; }
                var gpu = CockpitModel.SelectGpu(readings);
                report(gpu.Count > 0 ? DesktopUiText.Format("Boot.Gpu", gpu[0].Hardware, gpu.Count) : DesktopUiText.Get("Boot.NoGpu"), 45);
                var lineDelay = TimeSpan.FromMilliseconds(Math.Min(24, 3600d / Math.Max(1, readings.Count)));
                foreach (var reading in readings)
                {
                    report($"[{(SensorSemantics.IsTemperatureLimit(reading) ? DesktopUiText.Get("Boot.Limit") : "SENSOR")} / {(reading.Value is null ? "UNAVAILABLE" : "OK")}] {reading.Hardware} / {reading.Name} = {reading.Display}", 50);
                    // Pace already collected results for a readable CRT sequence, not simulated sensor work.
                    await Task.Delay(lineDelay, token);
                }
                warnings |= gpu.Count == 0 || CockpitModel.FindGpu(gpu, "Load", "GPU Core")?.Value is null;
                report(DesktopUiText.Format("Boot.SensorResult", readings.Count(s => s.Value is not null), readings.Count), 60);
                sample = sample with { Sensors = readings };
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
            {
                warnings = true;
                _sensorsEnabled = false;
                await Task.Run(_sensors.Dispose);
                report(DesktopUiText.Format("Boot.SensorFailed", ex.Message), 60);
                sample = sample with { SensorError = ex.Message };
                ReportError("Boot / Hardware-Sensoren", ex);
            }
        }
        finally { _sensorGate.Release(); }
        _model.Apply(sample, _sensorsEnabled);
        HardwareIntelligence.UpdateSensors(sample.Sensors, sample.Timestamp, _sensorsEnabled, sample.SensorError);
        HardwareIntelligence.UpdateSensors(sample.Sensors, sample.Timestamp, _sensorsEnabled, sample.SensorError);
        report(DesktopUiText.Get("Boot.PerformanceInit"), 65);
        try
        {
            var detail = await Task.Run(() => _dashboardMetrics.Read(token), token);
            _model.ApplyDashboard(detail, sample);
            ProcessCenter.Apply(detail);
            SystemIntelligence.Apply(detail, sample);
            report(DesktopUiText.Get("Boot.PerformanceBaseline"), 72);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NetworkInformationException)
        {
            warnings = true;
            _model.DashboardFailed(ex.Message);
            report($"[WARN] Performance: {ex.Message}", 72);
            ReportError("Boot / Performance", ex);
        }
        var headers = Pages.Items.Cast<TabItem>().Select(t => t.Header?.ToString() ?? "").ToArray();
        foreach (var header in headers)
        {
            report($"[MODULE / LOADED] {header}", 80);
            await Task.Delay(70, token);
        }
        foreach (var module in new[] { "SMART", DesktopUiText.Get("Module.Netzwerk"), DesktopUiText.Get("Module.Pakete"),
            DesktopUiText.Format("Boot.Commands", AdminCommandCatalog.All.Count), DesktopUiText.Get("Module.Autostart"),
            DesktopUiText.Get("Module.Spuren"), "Logs / Gaming", DesktopUiText.Get("Module.Einstellungen"), DesktopUiText.Get("Module.Schluessel") })
            report(DesktopUiText.Format("Boot.OnDemand", module), 88);
        report(DesktopUiText.Format("Boot.Tools", DesktopUiText.Get(File.Exists(SmartService.DetectExecutable()) ? "Common.Present" : "Common.Missing"),
            DesktopUiText.Get(File.Exists(FrameTelemetry.DetectExecutable()) ? "Common.Present" : "Common.Missing")), 92);
        report(DesktopUiText.Get("Boot.Scheduled"), 95);
        report(DesktopUiText.Get("Boot.Privacy"), 97);
        return warnings;
    }

    public void SetBootTranscript(string transcript) => _bootTranscript = transcript;

    public async Task ReleaseBeforeStartupAsync()
    {
        _stop.Cancel();
        HardwareBoard.Shutdown();
        await HardwareIntelligence.ShutdownAsync();
        await ProcessCenter.ShutdownAsync();
        await SystemIntelligence.ShutdownAsync();
        await Task.WhenAll(_samplingTask, _probeTask, _eventTask, _boardFeedTask, _managerReportingTask,
            _backgroundDiagnosticsTask,
            _currentProbe, _cleanupTask, HardwareBoard.Pending);
        _managerReporting.Dispose();
        _backgroundDiagnostics.Dispose();
        if (_desktop is not null)
        {
            await _desktop.StopAsync();
            _desktop.Dispose();
            _desktop = null;
        }
        await _sensorGate.WaitAsync();
        try { await Task.Run(_sensors.Dispose); }
        finally { _sensorGate.Release(); }
        _sensorGate.Dispose();
        _processRefresh.Dispose();
        _stop.Dispose();
        _allowClose = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var result = WindowAppearance.DarkCaption(this);
        if (result != 0)
            ReportError("Dunkle Windows-Titelleiste", new Win32Exception(result));
    }

    private async Task SampleLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await _sensorGate.WaitAsync(_stop.Token);
                    LiveSample sample;
                    try
                    {
                        sample = await Task.Run(() =>
                        {
                            var live = _metrics.Read();
                            try { return live with { Sensors = _sensors.Read() }; }
                            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
                            {
                                Trace.TraceError("Hardware-Sensoren: {0}", ex);
                                return live with { SensorError = ex.Message };
                            }
                        }, _stop.Token);
                    }
                    finally { _sensorGate.Release(); }
                    _model.Apply(sample, _sensorsEnabled);
                    try
                    {
                        var detail = await Task.Run(() => _dashboardMetrics.Read(_stop.Token), _stop.Token);
                        _model.ApplyDashboard(detail, sample);
                        ProcessCenter.Apply(detail);
                        SystemIntelligence.Apply(detail, sample);
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NetworkInformationException)
                    {
                        _model.DashboardFailed(ex.Message);
                        ProcessCenter.Failed(ex.Message);
                        ReportError("Dashboard-Messung", ex);
                    }
                    if (_recording && (sample.Timestamp - _lastRecorded).TotalSeconds >= _model.Settings.RecordIntervalSeconds)
                    {
                        try
                        {
                            await Task.Run(() => _history.Append(sample), _stop.Token);
                            _lastRecorded = sample.Timestamp;
                            _model.HistoryStatus = DesktopUiText.Format("Main.HistoryActive", sample.Timestamp, _history.DirectoryPath);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            _recording = false;
                            HistoryToggle.IsChecked = false;
                            ReportError("Aufzeichnung gestoppt", ex);
                            _model.HistoryStatus = $"Aufzeichnung gestoppt: {ex.Message}";
                        }
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    _model.MeasurementFailed(ex.Message);
                    _model.DashboardFailed(ex.Message);
                    ReportError("Live-Messung", ex);
                }
                await _processRefresh.WaitAsync(TimeSpan.FromSeconds(2), _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ProbeLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await RefreshProbeAsync();
                await SystemIntelligence.ObserveAsync();
                await Task.Delay(TimeSpan.FromSeconds(60), _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task EventLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    var events = await EventMonitor.ReadAsync(_stop.Token);
                    _model.ApplyEvents(events);
                    SystemIntelligence.ApplyEvents(events);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException or System.Text.Json.JsonException or System.Xml.XmlException or IOException)
                {
                    _model.EventsFailed(ex.Message);
                    ReportError("Ereignismonitor", ex);
                }
                await Task.Delay(TimeSpan.FromSeconds(15), _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private Task RefreshProbeAsync()
    {
        if (!_currentProbe.IsCompleted) return _currentProbe;
        _currentProbe = RunProbeAsync();
        return _currentProbe;
    }

    private async Task RunProbeAsync()
    {
        _model.CanRefresh = false;
        _model.ProbeStatus = DesktopUiText.Get("Main.ProbeReading");
        try { _model.ApplyProbe(await _probe.ReadAsync(_stop.Token)); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Text.Json.JsonException or System.Xml.XmlException or Win32Exception or IOException)
        {
            _model.ProbeFailed(ex.Message);
            ReportError("Windows-Status", ex);
        }
        finally { _model.CanRefresh = !_closing; }
    }

    private async void RefreshClicked(object sender, RoutedEventArgs e) => await RefreshProbeAsync();

    private void RestartAdminClicked(object sender, RoutedEventArgs e)
    {
        if (_closing || !_model.CanClean) return;
        if (_controlCenter is not null && !_controlCenter.PendingOperations.IsCompleted)
        {
            _model.Footer = DesktopUiText.Get("Main.AdminBusy");
            return;
        }
        if (_model.AccessStatus == "ACCESS / ADMINISTRATOR")
        {
            _model.Footer = DesktopUiText.Get("Main.AdminAlready");
            return;
        }
        if (MessageBox.Show(this,
            DesktopUiText.Get("Main.AdminConsent"),
            DesktopUiText.Get("Main.AdminTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "SystemCockpit.exe");
            var elevated = Process.Start(new ProcessStartInfo
            {
                FileName = executable, UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory
            }) ?? throw new InvalidOperationException(DesktopUiText.Get("Main.AdminStartFailed"));
            elevated.Dispose();
            Close();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            ReportError("UAC-Freigabe abgebrochen", ex);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ReportError("Administrator-Neustart", ex);
            MessageBox.Show(this, ex.Message, DesktopUiText.Get("Main.RestartFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetStatisticsClicked(object sender, RoutedEventArgs e) => _model.ResetStatistics();
    private void PauseStatisticsClicked(object sender, RoutedEventArgs e)
    {
        _model.RecordStatistics = !_model.RecordStatistics;
        _model.SessionStatus = DesktopUiText.Get(_model.RecordStatistics ? "Main.SessionResumed" : "Main.SessionPaused");
    }

    public static string CsvCell(string value)
    {
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') ||
            value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private void ExportStatisticsClicked(object sender, RoutedEventArgs e)
    {
        var snapshot = _model.StatisticsView.Cast<Statistic>().ToArray();
        if (snapshot.Length == 0) { _model.Footer = DesktopUiText.Get("Main.StatisticsEmpty"); return; }
        var dialog = new SaveFileDialog
        {
            Filter = "CSV-Auswertung (*.csv)|*.csv", FileName = $"Aether-session-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var lines = new List<string> { "Hardware,Sensor,Unit,Count,Min,Max,Average" };
            lines.AddRange(snapshot.Select(s => string.Join(",",
                CsvCell(s.Hardware), CsvCell(s.Name), CsvCell(s.Unit),
                s.Count.ToString(CultureInfo.InvariantCulture), s.Minimum.ToString("R", CultureInfo.InvariantCulture),
                s.Maximum.ToString("R", CultureInfo.InvariantCulture), s.Average.ToString("R", CultureInfo.InvariantCulture))));
            File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
            _model.Footer = DesktopUiText.Format("Main.StatisticsSaved", snapshot.Length, dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportError("CSV-Export", ex);
            MessageBox.Show(this, ex.Message, "Export fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportSensorsClicked(object sender, RoutedEventArgs e)
    {
        var snapshot = _model.SensorView.Cast<SensorReading>().ToArray();
        if (snapshot.Length == 0) { _model.Footer = DesktopUiText.Get("Main.SensorsEmpty"); return; }
        var dialog = new SaveFileDialog { Filter = "Sensor-Snapshot (*.csv)|*.csv", FileName = "Aether-Sensor-Snapshot.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var time = _model.LastSampleAt;
            var lines = new List<string> { "Timestamp,Id,Hardware,Name,Type,Unit,Value,Meaning" };
            lines.AddRange(snapshot.Select(s => string.Join(",", CsvCell(time.ToString("O", CultureInfo.InvariantCulture)),
                CsvCell(s.Id), CsvCell(s.Hardware), CsvCell(s.Name), CsvCell(s.Type), CsvCell(s.Unit),
                s.Value is { } v && double.IsFinite(v) ? v.ToString("R", CultureInfo.InvariantCulture) : "",
                CsvCell(SensorSemantics.IsTemperatureLimit(s) ? "Grenzwert / kein Messwert" : "Messkanal / Luecke ist unbekannt"))));
            File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
            _model.Footer = DesktopUiText.Format("Main.SensorsSaved", snapshot.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportError("Sensor-Snapshot", ex);
            MessageBox.Show(this, ex.Message, DesktopUiText.Get("Main.SnapshotFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenToolClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tool }) return;
        if (tool == "taskmgr") { SelectPage("Taskmanager"); return; }
        if (tool == "events") { SelectPage("Ereignisse"); return; }
        if (tool is "updates" or "network")
        {
            SelectPage("Superadmin");
            Workbench.SelectArea(tool == "updates" ? "updates" : "adapters");
            return;
        }
        if (tool == "startup") { OpenControlCenterModule("Autostart"); return; }
        if (tool == "xbar") { ToggleSidebar(); return; }
        if (tool == "godmode") { OpenAllTasks(); return; }
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var (file, arguments) = tool switch
        {
            "devices" => (Path.Combine(system, "devmgmt.msc"), ""),
            "disks" => (Path.Combine(system, "diskmgmt.msc"), ""),
            "tasks" => (Path.Combine(system, "taskschd.msc"), ""),
            "services" => (Path.Combine(system, "services.msc"), ""),
            "security" => ("windowsdefender:", ""),
            "startup" => ("ms-settings:startupapps", ""),
            "updates" => ("ms-settings:windowsupdate", ""),
            "network" => ("ms-settings:network", ""),
            _ => throw new InvalidOperationException("Unbekanntes Systemwerkzeug.")
        };
        try
        {
            using var process = Process.Start(new ProcessStartInfo { FileName = file, Arguments = arguments, UseShellExecute = true });
            _model.Footer = $"Windows-Werkzeug geoeffnet: {tool}";
        }

        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ReportError("Systemwerkzeug", ex);
            MessageBox.Show(this, ex.Message, DesktopUiText.Get("Main.ToolFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SelectPage(string route) => Pages.SelectedItem = Pages.Items.Cast<TabItem>().Single(t => Equals(t.Tag, route));

    private void TaskCenterClicked(object sender, RoutedEventArgs e) => OpenTaskCenter();

    internal void OpenTaskCenter()
    {
        var window = new TaskCenterWindow(_model.Processes.ToArray(), SystemIntelligence.SearchTasks()) { Owner = this };
        if (window.ShowDialog() != true || window.SelectedTask is not { } task) return;
        if (task.QuickAction is { } quick)
        {
            SelectPage(task.Destination);
            if (quick == "services") Workbench.SelectArea("services");
            else if (task.Destination == "SystemIntelligence") SystemIntelligence.QuickAction(quick);
            else if (quick == "hardware-report") HardwareIntelligence.RequestExport();
            else ProcessCenter.QuickAction(quick);
            return;
        }
        if (task.Surface == "Systemkontrolle")
        {
            OpenControlCenterModule(task.Destination);
            if (task.Command is not null) _controlCenter?.FocusCatalogCommand(task.Command);
        }
        else if (task.Destination == "Netzwerkbeobachtung") OpenNetworkMonitor();
        else if (task.Destination == "Desktop-Einstellungen") OpenDesktopSettings();
        else if (task.Destination == "Speicheranalyse") OpenStorageAnalysis();
        else SelectPage(task.Destination);
    }

    private StorageAnalysisWindow? _storageAnalysis;
    private void StorageAnalysisClicked(object sender, RoutedEventArgs e) => OpenStorageAnalysis();
    private void OpenStorageAnalysis()
    {
        if (_storageAnalysis is null)
        {
            _storageAnalysis = new StorageAnalysisWindow { Owner = this };
            _storageAnalysis.Closed += (_, _) => _storageAnalysis = null;
        }
        WindowActivation.Show(_storageAnalysis);
    }

    private void HelpSearchChanged(object sender, TextChangedEventArgs e) => RefreshHelp();

    private void RefreshHelp()
    {
        if (HelpList is null) return;
        var topics = HelpCatalog.Search(HelpSearch.Text);
        HelpList.ItemsSource = topics;
        if (topics.Count > 0) HelpList.SelectedIndex = 0;
        else
        {
            HelpTitle.Text = DesktopUiText.Get("Main.HelpEmptyTitle");
            HelpBody.Text = DesktopUiText.Get("Main.HelpEmptyBody");
        }
    }

    private void HelpSelected(object sender, SelectionChangedEventArgs e)
    {
        if (HelpList.SelectedItem is not HelpTopic topic) return;
        HelpTitle.Text = $"{topic.Module} / {topic.Title}";
        HelpBody.Text = topic.Body;
    }

    private async void MemoryDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        OpenControlCenterClicked(sender, e);
        if (_controlCenter is null) return;
        // Liest nur vorhandene Ergebnisse; ein neuer RAM-Test wird nie automatisch gestartet.
        await _controlCenter.DisplayReadOnlyAsync("Windows-Speicherdiagnose / vorhandene Ergebnisse", token =>
            CommandRunner.PowerShellAsync(
                "$events = Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Microsoft-Windows-MemoryDiagnostics-Results'} -MaxEvents 20 -ErrorAction SilentlyContinue; " +
                "if(-not $events){'Keine Ergebnisse der Windows-Speicherdiagnose im System-Ereignislog gefunden.'; " +
                "'Neuen RAM-Test selbst planen: mdsched.exe starten (verlangt einen Neustart). AETHER startet ihn nicht automatisch.'} " +
                "else{$events | Format-List TimeCreated,Id,Message}", token));
    }

    private void ExportDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        var preview = new ExportPreviewWindow(DiagnosticExport.Build(_model)) { Owner = this };
        if (preview.ShowDialog() == true && preview.SavedPath is { } path)
            _model.Footer = DesktopUiText.Format("Main.DiagnosticSaved", path);
    }

    private InventoryWindow? _inventory;
    private void InventoryClicked(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        if (_inventory is not null) { _inventory.Activate(); return; }
        _inventory = new InventoryWindow { Owner = this };
        _inventory.Closed += (_, _) => _inventory = null;
        _inventory.Show();
    }

    public const string GodModeClsid = "{ED7BA470-8E54-465E-825C-99712043E26C}";

    public static bool GodModeAvailable()
    {
        using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"CLSID\" + GodModeClsid);
        return key is not null;
    }

    // GodMode ("Alle Aufgaben") existiert nicht auf jedem Windows-Build; neuere Builds
    // haben die CLSID entfernt. Dann wird ehrlich die vollstaendige klassische
    // Systemsteuerung geoeffnet statt eines leeren Ordners oder einer Fehlermeldung.
    private void OpenAllTasks()
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        try
        {
            if (GodModeAvailable())
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AetherCockpit", "GodMode." + GodModeClsid);
                Directory.CreateDirectory(folder);
                using var process = Process.Start(new ProcessStartInfo
                    { FileName = explorer, Arguments = "\"" + folder + "\"", UseShellExecute = true });
                _model.Footer = "GodMode-Aufgabenliste geoeffnet (Windows-Ansicht 'Alle Aufgaben').";
                return;
            }
            using var fallback = Process.Start(new ProcessStartInfo
                { FileName = explorer, Arguments = "shell:::{21EC2020-3AEA-1069-A2DD-08002B30309D}", UseShellExecute = true });
            _model.Footer = "GodMode ('Alle Aufgaben') ist auf diesem Windows-Build entfernt. " +
                "Stattdessen geoeffnet: Alle Systemsteuerungselemente. Verwaltungskonsolen stehen daneben als eigene Knoepfe bereit.";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ReportError("GodMode/Systemsteuerung", ex);
            MessageBox.Show(this, ex.Message, DesktopUiText.Get("Main.ViewFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ToggleSidebar()
    {
        if (_closing) return;
        if (_sidebar is not null) { _sidebar.Close(); return; }
        _sidebar = new SidebarWindow(_model, _model.Settings, SaveSidebarSettings);
        _sidebar.Closed += (_, _) => { _sidebar = null; if (!_closing) _model.Footer = "AETHER xBar geschlossen."; };
        _sidebar.Show();
        _model.Footer = DesktopUiText.Get("Main.XbarActive");
    }

    private void SaveSidebarSettings(CockpitSettings settings)
    {
        try
        {
            settings.Save();
            _model.Settings = settings;
            _model.Footer = DesktopUiText.Get("Main.XbarSaved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ReportError("AETHER xBar-Einstellungen konnten nicht gespeichert werden", ex);
            throw;
        }
    }

    private void ManagementClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string area }) return;
        SelectPage("Superadmin");
        Workbench.SelectArea(area);
    }

    private async void SensorReportClicked(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        OpenControlCenterClicked(sender, e);
        if (_controlCenter is null) return;
        await _controlCenter.DisplayReadOnlyAsync("Hardwareprovider / CPU-Diagnose", async token =>
        {
            await _sensorGate.WaitAsync(token);
            try { return await Task.Run(_sensors.Report, token); }
            finally { _sensorGate.Release(); }
        });
    }

    private void ProcessDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (!_closing) ProcessCenter.ShowSelectedDetails();
    }

    private async void ScanCleanupClicked(object sender, RoutedEventArgs e)
    {
        if (!_model.CanClean || _closing) return;
        _cleanupTask = ScanCleanupAsync();
        await _cleanupTask;
    }

    private async Task ScanCleanupAsync()
    {
        _model.CanClean = false;
        _model.CleanupFiles.Clear();
        _cleanupStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _model.CleanupStatus = DesktopUiText.Format("Cleaner.PreviewRunning", _cleaner.Root);
        try
        {
            if (CacheTargets.SelectedItem is not CacheTarget target)
                throw new IOException(DesktopUiText.Get("Cleaner.SelectCategory"));
            _cleaner = new TempCleaner(target.Path);
            if (target.Level >= 2 && MessageBox.Show(this, DesktopUiText.Format("Cleaner.PreviewConsent", target.Name, target.Risk),
                DesktopUiText.Get("Cleaner.PreviewTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                _model.CleanupStatus = DesktopUiText.Get("Cleaner.PreviewDeclined");
                return;
            }
            var scan = await Task.Run(() => _cleaner.Scan(_cleanupStop.Token));
            foreach (var file in scan.Files) _model.CleanupFiles.Add(file);
            _model.CleanupStatus = CacheCatalog.Assess(target, scan.Files.Count, scan.Files.Sum(f => f.Bytes)) + "\n" +
                DesktopUiText.Get("Cleaner.PreviewComplete") + IssueSummary(scan.Issues);
            UpdateCleanupSelection();
        }
        catch (OperationCanceledException)
        {
            _model.CleanupStatus = DesktopUiText.Get("Cleaner.PreviewCanceled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _model.CleanupStatus = DesktopUiText.Format("Cleaner.PreviewFailed", ex.Message);
            ReportError("Cleaner-Vorschau", ex);
        }
        finally
        {
            _cleanupStop.Dispose();
            _cleanupStop = null;
            _model.CanClean = !_closing;
            UpdateCleanupSelection();
        }
    }

    private async void DeleteCleanupClicked(object sender, RoutedEventArgs e)
    {
        if (!_model.CanClean || _closing) return;
        var selected = _model.CleanupFiles.Where(f => f.Selected).ToList();
        if (selected.Count == 0)
        {
            _model.CleanupStatus = DesktopUiText.Get("Cleaner.NoneSelected");
            return;
        }
        if (MessageBox.Show(this,
            DesktopUiText.Format("Cleaner.DeleteConsent", selected.Count, selected.Sum(f => f.Bytes) / 1048576d,
                selected.Count(f => !_model.CleanupView.Contains(f))),
            DesktopUiText.Get("Cleaner.DeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _cleanupTask = DeleteCleanupAsync(selected);
        await _cleanupTask;
    }

    private async Task DeleteCleanupAsync(List<CleanupCandidate> selected)
    {
        _model.CanClean = false;
        _cleanupStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        try
        {
            var result = await Task.Run(() => _cleaner.DeleteSelected(selected, _cleanupStop.Token));
            _model.CleanupFiles.Clear();
            _model.CleanupStatus = DesktopUiText.Format("Cleaner.Deleted",
                DesktopUiText.Get(result.Cancelled ? "Cleaner.Canceled" : "Cleaner.Completed"),
                result.Deleted, result.Bytes / 1048576d) + IssueSummary(result.Issues);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _model.CleanupFiles.Clear();
            _model.CleanupStatus = DesktopUiText.Format("Cleaner.DeleteFailed", ex.Message);
            ReportError("Cleaner", ex);
        }
        finally
        {
            _cleanupStop.Dispose();
            _cleanupStop = null;
            _model.CanClean = !_closing;
        }
    }

    private static string IssueSummary(List<string> issues) => issues.Count == 0 ? DesktopUiText.Get("Cleaner.NoAccessErrors")
        : DesktopUiText.Format("Cleaner.IssueCount", issues.Count) + string.Join("\n", issues.Take(10)) +
          (issues.Count > 10 ? DesktopUiText.Get("Cleaner.IssueLimit") : "");

    private void CancelCleanupClicked(object sender, RoutedEventArgs e) => _cleanupStop?.Cancel();

    private void CleanupChecked(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(UpdateCleanupSelection);
    private void UpdateCleanupSelection()
    {
        if (CleanupSelectionStatus is null) return;
        var selected = _model.CleanupFiles.Where(f => f.Selected).ToList();
        var hidden = selected.Count(f => !_model.CleanupView.Contains(f));
        CleanupSelectionStatus.Text = DesktopUiText.Format("Cleaner.Selection", selected.Count, _model.CleanupFiles.Count,
            selected.Sum(f => f.Bytes) / 1048576d, hidden);
        if (selected.Count > 0 && _model.CleanupStatus == DesktopUiText.Get("Cleaner.NoneSelected"))
            _model.CleanupStatus = DesktopUiText.Get("Cleaner.SelectionReady");
    }
    private void CleanupSelectionClicked(object sender, RoutedEventArgs e)
    {
        if (!_model.CanClean || _closing || sender is not Button { Tag: string mode }) return;
        if (mode == "rows")
            foreach (var file in CleanupGrid.SelectedItems.Cast<CleanupCandidate>()) file.Selected = true;
        else if (mode == "visible")
            foreach (var file in _model.CleanupView.Cast<CleanupCandidate>()) file.Selected = true;
        else
            foreach (var file in _model.CleanupFiles) file.Selected = mode == "all";
        UpdateCleanupSelection();
    }

    private void LoadCacheTargets(int level)
    {
        CacheTargets.ItemsSource = CacheCatalog.Discover().Where(t => t.Level <= level).ToList();
        CacheTargets.SelectedIndex = 0;
    }

    private void CacheLevelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !_model.CanClean) return;
        try { LoadCacheTargets(CacheLevel.SelectedIndex); _model.CleanupFiles.Clear(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportError("Cache-Katalog", ex);
            _model.CleanupStatus = DesktopUiText.Format("Main.CacheUnreadable", ex.Message);
        }
    }

    private void CacheTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        _model.CleanupFiles.Clear();
        CleanupGrid?.UnselectAll();
    }

    private ControlCenterWindow? _controlCenter;
    private void OpenControlCenterClicked(object sender, RoutedEventArgs e)
    {
        if (_controlCenter is null)
        {
            _controlCenter = new ControlCenterWindow(_model) { Owner = this };
            _controlCenter.SetBootTranscript(_bootTranscript);
            _controlCenter.Closed += (_, _) => _controlCenter = null;
            _controlCenter.Show();
        }
        else _controlCenter.Activate();
    }

    private void OpenWindowsAssistantsClicked(object sender, RoutedEventArgs e)
    {
        SelectPage("Operations");
        Operations.OpenWindowsAssistants();
    }

    // Direktsprung in ein Systemkontroll-Modul: ein Klick statt Fenster oeffnen + Tab suchen.
    private void OpenControlCenterModule(string module)
    {
        OpenControlCenterClicked(this, new RoutedEventArgs());
        _controlCenter?.SelectModule(module);
    }

    private void ControlCenterNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string module }) OpenControlCenterModule(module);
    }

    private void Navigate(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string header }) return;
        SelectPage(header);
    }

    private readonly NavigationTrail _trail = new();
    private bool _trailNavigating;

    private void PageChanged(object sender, SelectionChangedEventArgs e)
    {
        // Innere Listen/Tabs blubbern SelectionChanged hoch; nur echte Seitenwechsel zaehlen.
        if (!ReferenceEquals(e.Source, Pages) || Pages.SelectedItem is not TabItem { Tag: string route, Header: string header }) return;
        if (!_trailNavigating) _trail.Visit(route);
        if (route == "Mainboard") HardwareBoard.EnsureLoaded();
        if (NavTrail is null) return;
        NavTrail.Text = $"{UpdateNavigation(route).ToUpperInvariant()}  \u25B8  {header.ToUpperInvariant()}";
        NavBack.IsEnabled = _trail.CanBack;
        NavForward.IsEnabled = _trail.CanForward;
    }

    private void TrailGo(string? header)
    {
        if (header is null || _closing) return;
        _trailNavigating = true;
        try { SelectPage(header); }
        finally { _trailNavigating = false; }
        NavBack.IsEnabled = _trail.CanBack;
        NavForward.IsEnabled = _trail.CanForward;
    }

    private void NavBackClicked(object sender, RoutedEventArgs e) => TrailGo(_trail.Back());
    private void NavForwardClicked(object sender, RoutedEventArgs e) => TrailGo(_trail.Forward());
    private void NavHomeClicked(object sender, RoutedEventArgs e) => SelectPage("Cockpit");
    private void NavHelpClicked(object sender, RoutedEventArgs e) => SelectPage("Hilfe");

    protected override void OnPreviewMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.XButton1) { TrailGo(_trail.Back()); e.Handled = true; }
        else if (e.ChangedButton == System.Windows.Input.MouseButton.XButton2) { TrailGo(_trail.Forward()); e.Handled = true; }
        base.OnPreviewMouseDown(e);
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.K &&
            e.KeyboardDevice.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            OpenTaskCenter();
            e.Handled = true;
        }
        // Alt+Pfeil links/rechts navigiert wie im Browser; Alt liefert Key.System mit SystemKey.
        if (e.Key == System.Windows.Input.Key.System &&
            e.KeyboardDevice.Modifiers == System.Windows.Input.ModifierKeys.Alt)
        {
            if (e.SystemKey == System.Windows.Input.Key.Left) { TrailGo(_trail.Back()); e.Handled = true; }
            else if (e.SystemKey == System.Windows.Input.Key.Right) { TrailGo(_trail.Forward()); e.Handled = true; }
        }
        base.OnPreviewKeyDown(e);
    }

    private async void SensorsChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _closing || _suppressSensorToggle) return;
        var enabled = SensorToggle.IsChecked == true;
        if (enabled)
        {
            using var identity = WindowsIdentity.GetCurrent();
            var admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            var answer = MessageBox.Show(this,
                DesktopUiText.Format("Main.SensorConsent", DesktopUiText.Get(admin ? "Main.SensorAdmin" : "Main.SensorNoAdmin")),
                DesktopUiText.Get("Main.SensorConsentTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                SensorToggle.IsChecked = false;
                return;
            }
        }
        SensorToggle.IsEnabled = false;
        try
        {
            await _sensorGate.WaitAsync(_stop.Token);
            try
            {
                await Task.Run(() =>
                {
                    if (enabled) _sensors.Open();
                    else _sensors.Dispose();
                }, _stop.Token);
                _sensorsEnabled = enabled;
                _model.SensorStatus = DesktopUiText.Get(enabled ? "Main.SensorInitializing" : "Main.SensorOff");
            }
            finally { _sensorGate.Release(); }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _sensorsEnabled = false;
            SensorToggle.IsChecked = false;
            ReportError("Sensorzugriff", ex);
            _model.SensorStatus = DesktopUiText.Format("Main.SensorFailed", ex.Message);
        }
        finally { SensorToggle.IsEnabled = !_closing; }
    }

    private void HistoryChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _closing) return;
        _recording = HistoryToggle.IsChecked == true;
        _lastRecorded = DateTimeOffset.MinValue;
        _model.HistoryStatus = _recording ? DesktopUiText.Format("Main.HistoryEnabled", _history.DirectoryPath)
            : DesktopUiText.Get("Main.HistoryOff");
    }

    private void OpenHistoryClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_history.DirectoryPath);
            Process.Start(new ProcessStartInfo { FileName = _history.DirectoryPath, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            ReportError("Datenordner", ex);
            MessageBox.Show(this, ex.Message, DesktopUiText.Get("Main.DataFolderFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ReportError(string source, Exception ex)
    {
        _model.Footer = $"{DateTimeOffset.Now:HH:mm:ss} | {source}: {ex.Message}";
        Trace.TraceError("{0}: {1}", source, ex);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_desktop is not null && !_exitRequested)
        {
            Hide();
            return;
        }
        if (_closing) return;
        _closing = true;
        IsEnabled = false;
        _sidebar?.Close();
        _inventory?.Close();
        var controlCenter = _controlCenter;
        controlCenter?.Close();
        var storageAnalysis = _storageAnalysis;
        if (storageAnalysis is not null) await storageAnalysis.ShutdownAsync();
        storageAnalysis?.Close();
        _model.CanRefresh = false;
        SensorToggle.IsEnabled = false;
        HistoryToggle.IsEnabled = false;
        _stop.Cancel();
        if (_desktop is not null)
        {
            await _desktop.StopAsync();
            _desktop.Dispose();
            _desktop = null;
        }
        Workbench.Cancel();
        EmbeddedEvents.Shutdown();
        Operations.Shutdown();
        HardwareBoard.Shutdown();
        await ProcessCenter.ShutdownAsync();
        await SystemIntelligence.ShutdownAsync();
        await HardwareIntelligence.ShutdownAsync();
        await Task.WhenAll(_samplingTask, _probeTask, _eventTask, _boardFeedTask, _managerReportingTask,
            _backgroundDiagnosticsTask,
            _currentProbe, _cleanupTask);
        _managerReporting.Dispose();
        _backgroundDiagnostics.Dispose();
        await Task.WhenAll(Workbench.Pending, EmbeddedEvents.Pending, Operations.Pending, HardwareBoard.Pending);
        if (controlCenter is not null) await controlCenter.PendingOperations;
        await _sensorGate.WaitAsync();
        try { await Task.Run(_sensors.Dispose); }
        finally { _sensorGate.Release(); }
        _sensorGate.Dispose();
        _processRefresh.Dispose();
        _stop.Dispose();
        _allowClose = true;
        await Dispatcher.InvokeAsync(Close);
    }
}
