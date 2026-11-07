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
    private readonly SemaphoreSlim _sensorGate = new(1);
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

    public MainWindow()
    {
        InitializeComponent();
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
            _model.Settings = CockpitSettings.Load();
            var optionalWarnings = _model.Settings.OptionalFileWarnings();
            if (optionalWarnings.Count > 0)
            {
                _configurationIssue = string.Join("\n", optionalWarnings);
                _model.Footer = _configurationIssue;
            }
            LoadCacheTargets(0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            _configurationIssue = ex.Message;
            ReportError("Einstellungen/Cache-Katalog", ex);
            MessageBox.Show(this, ex.Message, "Konfiguration nicht lesbar; Standardwerte bleiben sichtbar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Loaded += (_, _) =>
        {
            StartBackgroundServices();
        };
        Closing += OnClosing;
    }

    internal void StartBackgroundServices()
    {
        if (_monitoringStarted) return;
        _monitoringStarted = true;
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
    internal void OpenNetworkMonitor() => _desktop?.OpenNetwork();

    private void SaveDesktopSettings(CockpitSettings settings)
    {
        try { settings.Save(); _model.Settings = settings; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ReportError("Desktop-Einstellungen nicht gespeichert", ex);
            MessageBox.Show(ex.Message, "AETHER / Einstellungen nicht gespeichert", MessageBoxButton.OK, MessageBoxImage.Error);
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
        report("[LOAD] WPF / Cockpit / gemeinsame Neon- und Glasressourcen geladen", 8);
        report(_configurationIssue is null ? "[LOAD] Einstellungen / Cache-Katalog initialisiert; keine Bereinigung gestartet" :
            $"[WARN] Konfiguration unvollstaendig: {_configurationIssue}", 12);
        report("[INIT] Windows CPU/RAM/Netzwerk / erste Zaehler-Baseline", 17);
        var sample = await Task.Run(_metrics.Read, token);
        report($"[OK] RAM {sample.MemoryTotalGb:0.0} GB / {sample.Volumes.Count} Volumes", 22);
        report("[INIT] LibreHardwareMonitor / CPU, GPU, RAM, Mainboard, Controller, Storage", 25);
        report("[HINWEIS] Low-Level-Sensorzugriff kann einen lokalen Treiber laden; Windows-Schutz bleibt unveraendert", 25);
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
                report(gpu.Count > 0 ? $"[GPU] {gpu[0].Hardware} / {gpu.Count} Sensoren" : "[WARN] Keine GPU-Sensoren erkannt", 45);
                var lineDelay = TimeSpan.FromMilliseconds(Math.Min(24, 3600d / Math.Max(1, readings.Count)));
                foreach (var reading in readings)
                {
                    report($"[{(SensorSemantics.IsTemperatureLimit(reading) ? "LIMIT / KEIN MESSWERT" : "SENSOR")} / {(reading.Value is null ? "UNAVAILABLE" : "OK")}] {reading.Hardware} / {reading.Name} = {reading.Display}", 50);
                    // Pace already collected results for a readable CRT sequence, not simulated sensor work.
                    await Task.Delay(lineDelay, token);
                }
                warnings |= gpu.Count == 0 || CockpitModel.FindGpu(gpu, "Load", "GPU Core")?.Value is null;
                report($"[RESULT] {readings.Count(s => s.Value is not null)} / {readings.Count} Sensorwerte lesbar. Fehlende Kanaele sind nicht 0.", 60);
                sample = sample with { Sensors = readings };
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
            {
                warnings = true;
                _sensorsEnabled = false;
                await Task.Run(_sensors.Dispose);
                report($"[WARN] Hardware-Sensorzugriff fehlgeschlagen: {ex.Message}", 60);
                sample = sample with { SensorError = ex.Message };
                ReportError("Boot / Hardware-Sensoren", ex);
            }
        }
        finally { _sensorGate.Release(); }
        _model.Apply(sample, _sensorsEnabled);
        report("[INIT] Prozessmonitor / logische CPU-Zeiten / Commit / Adapter-Baselines", 65);
        try
        {
            _model.ApplyDashboard(await Task.Run(_dashboardMetrics.Read, token), sample);
            report("[OK] Performance-Baselines erfasst; Intervallwerte ab zweiter Messung", 72);
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
        foreach (var module in new[] { "SMART", "Netzwerk", "Pakete", $"Wartung / {AdminCommandCatalog.All.Count} Administrationsbefehle", "Autostart", "Spuren", "Logs / Gaming", "Einstellungen", "Schluessel" })
            report($"[MODULE / ON DEMAND] {module} registriert; Aktionen erst bei Aufruf", 88);
        report($"[TOOLS] smartctl {(File.Exists(SmartService.DetectExecutable()) ? "vorhanden" : "nicht vorhanden")} / PresentMon {(File.Exists(FrameTelemetry.DetectExecutable()) ? "vorhanden" : "nicht vorhanden")}; keine Tests/Captures gestartet", 92);
        report("[SCHEDULED] Ereignismonitor / Disk-I/O 15s; Windows-Status 60s. Erste Abfragen nach Cockpitstart.", 95);
        report("[PRIVACY] Messaufzeichnung / Auto-LAN / Geheimnisabfragen bleiben aus", 97);
        return warnings;
    }

    public void SetBootTranscript(string transcript) => _bootTranscript = transcript;

    public async Task ReleaseBeforeStartupAsync()
    {
        _stop.Cancel();
        HardwareBoard.Shutdown();
        await Task.WhenAll(_samplingTask, _probeTask, _eventTask, _boardFeedTask, _managerReportingTask,
            _backgroundDiagnosticsTask,
            _currentProbe, _cleanupTask, HardwareBoard.Pending);
        if (_desktop is not null)
        {
            await _desktop.StopAsync();
            _desktop.Dispose();
            _desktop = null;
        }
        await Task.Run(_sensors.Dispose);
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
                        var detail = await Task.Run(_dashboardMetrics.Read, _stop.Token);
                        _model.ApplyDashboard(detail, sample);
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NetworkInformationException)
                    {
                        _model.DashboardFailed(ex.Message);
                        ReportError("Dashboard-Messung", ex);
                    }
                    if (_recording && (sample.Timestamp - _lastRecorded).TotalSeconds >= _model.Settings.RecordIntervalSeconds)
                    {
                        try
                        {
                            await Task.Run(() => _history.Append(sample), _stop.Token);
                            _lastRecorded = sample.Timestamp;
                            _model.HistoryStatus = $"Aufzeichnung aktiv | zuletzt {sample.Timestamp:HH:mm:ss}\n{_history.DirectoryPath}";
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
                await Task.Delay(TimeSpan.FromSeconds(2), _stop.Token);
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
                try { _model.ApplyEvents(await EventMonitor.ReadAsync(_stop.Token)); }
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
        _model.ProbeStatus = "Windows-Status wird gelesen ...";
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
            _model.Footer = "Zuerst die laufende Aktion/den Auto-Scan in der erweiterten Systemkontrolle beenden.";
            return;
        }
        if (_model.AccessStatus == "ACCESS / ADMINISTRATOR")
        {
            _model.Footer = "Bereits mit Administratorrechten gestartet.";
            return;
        }
        if (MessageBox.Show(this,
            "Die gesamte App mit Administratorrechten neu starten? Windows zeigt die UAC-Abfrage. " +
            "Die aktuelle Messsitzung im RAM endet; exportiere sie bei Bedarf vorher. Sensoren und Aufzeichnung " +
            "bleiben nach dem Neustart aus. Windows-Schutzfunktionen werden nicht abgeschaltet.",
            "Administrator-Modus", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "SystemCockpit.exe");
            var elevated = Process.Start(new ProcessStartInfo
            {
                FileName = executable, UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory
            }) ?? throw new InvalidOperationException("Administrator-Neustart konnte nicht gestartet werden.");
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
            MessageBox.Show(this, ex.Message, "Neustart fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetStatisticsClicked(object sender, RoutedEventArgs e) => _model.ResetStatistics();
    private void PauseStatisticsClicked(object sender, RoutedEventArgs e)
    {
        _model.RecordStatistics = !_model.RecordStatistics;
        _model.SessionStatus = _model.RecordStatistics ? "Messsitzung wird fortgesetzt; Pausenwerte werden nicht eingerechnet."
            : "Messsitzung angehalten. MIN/MAX/AVG eingefroren; Live-Cockpit laeuft weiter.";
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
        if (snapshot.Length == 0) { _model.Footer = "Keine sichtbaren Statistikreihen fuer den Export."; return; }
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
            _model.Footer = $"{snapshot.Length} sichtbare Statistikreihen gespeichert: {dialog.FileName}";
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
        if (snapshot.Length == 0) { _model.Footer = "Keine sichtbaren Sensorzeilen fuer den Export."; return; }
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
            _model.Footer = $"{snapshot.Length} sichtbare Sensorzeilen als Momentaufnahme gespeichert; keine fortlaufende Aufzeichnung.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportError("Sensor-Snapshot", ex);
            MessageBox.Show(this, ex.Message, "Sensor-Snapshot nicht gespeichert", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(this, ex.Message, "Werkzeug nicht gestartet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SelectPage(string header) => Pages.SelectedItem = Pages.Items.Cast<TabItem>().Single(t => Equals(t.Header, header));

    private void TaskCenterClicked(object sender, RoutedEventArgs e) => OpenTaskCenter();

    internal void OpenTaskCenter()
    {
        var window = new TaskCenterWindow { Owner = this };
        if (window.ShowDialog() != true || window.SelectedTask is not { } task) return;
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
            HelpTitle.Text = "Kein Thema gefunden.";
            HelpBody.Text = "Anderen Suchbegriff versuchen, z.B. 'Update', 'Temperatur', 'Loeschen' oder 'Gaming'.";
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
            _model.Footer = $"Diagnosebericht gespeichert: {path}";
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
            MessageBox.Show(this, ex.Message, "Ansicht nicht geoeffnet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ToggleSidebar()
    {
        if (_closing) return;
        if (_sidebar is not null) { _sidebar.Close(); return; }
        _sidebar = new SidebarWindow(_model, _model.Settings, SaveSidebarSettings);
        _sidebar.Closed += (_, _) => { _sidebar = null; if (!_closing) _model.Footer = "AETHER xBar geschlossen."; };
        _sidebar.Show();
        _model.Footer = "AETHER xBar aktiv: konfigurierbare Live-Diagramme und Laufwerksbelegung als Torten.";
    }

    private void SaveSidebarSettings(CockpitSettings settings)
    {
        try
        {
            settings.Save();
            _model.Settings = settings;
            _model.Footer = "AETHER xBar-Einstellungen gespeichert.";
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

    private async void ProcessDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        if (TaskmanagerProcesses.SelectedItem is not ProcessMetric process || process.StartTicks <= 0)
        {
            _model.Footer = "Lesbaren Prozess mit Startzeit auswaehlen. Geschuetzte Prozesse haben keine sicheren Detaildaten.";
            return;
        }
        OpenControlCenterClicked(sender, e);
        if (_controlCenter is null) return;
        await _controlCenter.DisplayReadOnlyAsync("Prozessdetails", token => CommandRunner.PowerShellAsync(
            $"$p=Get-Process -Id {process.Id}; if($p.StartTime.ToUniversalTime().Ticks -ne {process.StartTicks}){{throw 'PID wurde wiederverwendet'}}; " +
            "$p | Format-List Name,Id,StartTime,Path,PriorityClass,CPU,WorkingSet64,PrivateMemorySize64,VirtualMemorySize64,HandleCount; " +
            "$p.Threads | Format-Table Id,ThreadState,StartTime,TotalProcessorTime", token));
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
        _model.CleanupStatus = $"Vorschau laeuft: {_cleaner.Root}";
        try
        {
            if (CacheTargets.SelectedItem is not CacheTarget target)
                throw new IOException("Zuerst eine vorhandene Cache-Kategorie auswaehlen.");
            _cleaner = new TempCleaner(target.Path);
            if (target.Level >= 2 && MessageBox.Show(this, $"{target.Name}\n{target.Risk}\nNur Vorschau lesen?",
                "Diagnose-/Systemcache", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                _model.CleanupStatus = "Vorschau nicht freigegeben.";
                return;
            }
            var scan = await Task.Run(() => _cleaner.Scan(_cleanupStop.Token));
            foreach (var file in scan.Files) _model.CleanupFiles.Add(file);
            _model.CleanupStatus = CacheCatalog.Assess(target, scan.Files.Count, scan.Files.Sum(f => f.Bytes)) + "\n" +
                "Noch nichts geloescht. Dateien in der Spalte LOESCHEN? ankreuzen; Zeilenmarkierung allein reicht nicht.\n" + IssueSummary(scan.Issues);
            UpdateCleanupSelection();
        }
        catch (OperationCanceledException)
        {
            _model.CleanupStatus = "Vorschau abgebrochen. Keine Dateien geloescht.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _model.CleanupStatus = $"Vorschau fehlgeschlagen: {ex.Message}";
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
            _model.CleanupStatus = "Keine Dateien angekreuzt. Erst Vorschau erstellen und LOESCHEN?-Checkboxen aktivieren.";
            return;
        }
        if (MessageBox.Show(this,
            $"{selected.Count} ausgewaehlte Temp-Dateien ({selected.Sum(f => f.Bytes) / 1048576d:0.00} MiB) " +
            "endgueltig loeschen? Kein Papierkorb und kein Rueckgaengig. Jede Datei wird vor dem Loeschen erneut " +
            "geprueft. Andere Dateien und alle Ordner bleiben bestehen.\n" +
            $"Achtung: {selected.Count(f => !_model.CleanupView.Contains(f))} angekreuzte Dateien sind durch den Ansichtsfilter verborgen. " +
            "Auch diese sind Teil der Loeschung. Zum Pruefen abbrechen und Filter leeren.",
            "Ausgewaehlte Dateien endgueltig loeschen", MessageBoxButton.YesNo, MessageBoxImage.Warning,
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
            _model.CleanupStatus = $"{(result.Cancelled ? "Abgebrochen" : "Abgeschlossen")}: {result.Deleted} Dateien geloescht / " +
                $"{result.Bytes / 1048576d:0.00} MiB. Fuer weitere Aktionen neue Vorschau erstellen.\n" + IssueSummary(result.Issues);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _model.CleanupFiles.Clear();
            _model.CleanupStatus = $"Bereinigung fehlgeschlagen: {ex.Message}";
            ReportError("Cleaner", ex);
        }
        finally
        {
            _cleanupStop.Dispose();
            _cleanupStop = null;
            _model.CanClean = !_closing;
        }
    }

    private static string IssueSummary(List<string> issues) => issues.Count == 0 ? "Keine Zugriffsfehler."
        : $"{issues.Count} Fehler/Hinweise:\n" + string.Join("\n", issues.Take(10)) +
          (issues.Count > 10 ? "\nWeitere Hinweise durch Ausgabelimit nicht angezeigt." : "");

    private void CancelCleanupClicked(object sender, RoutedEventArgs e) => _cleanupStop?.Cancel();

    private void CleanupChecked(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(UpdateCleanupSelection);
    private void UpdateCleanupSelection()
    {
        if (CleanupSelectionStatus is null) return;
        var selected = _model.CleanupFiles.Where(f => f.Selected).ToList();
        var hidden = selected.Count(f => !_model.CleanupView.Contains(f));
        CleanupSelectionStatus.Text = $"{selected.Count} / {_model.CleanupFiles.Count} Dateien angekreuzt / {selected.Sum(f => f.Bytes) / 1048576d:0.00} MiB" +
            $" / davon {hidden} durch Filter verborgen. Zeilenmarkierung loescht nichts.";
        if (selected.Count > 0 && _model.CleanupStatus.StartsWith("Keine Dateien angekreuzt.", StringComparison.Ordinal))
            _model.CleanupStatus = "Auswahl bereit. Angekreuzte Dateien werden erst nach ausdruecklicher Bestaetigung geloescht.";
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
            _model.CleanupStatus = $"Cache-Katalog nicht lesbar: {ex.Message}";
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
        if (!ReferenceEquals(e.Source, Pages) || Pages.SelectedItem is not TabItem { Header: string header }) return;
        if (!_trailNavigating) _trail.Visit(header);
        if (header == "Mainboard") HardwareBoard.EnsureLoaded();
        if (NavTrail is null) return;
        NavTrail.Text = $"HAUPTEBENE  \u25B8  {header.ToUpperInvariant()}";
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
                "LibreHardwareMonitor liest Hardware-Sensoren. Bei erhoehten Rechten kann die Bibliothek einen " +
                "lokalen Low-Level-Treiber laden. Das kann durch Windows-Schutzfunktionen blockiert werden.\n\n" +
                (admin ? "Die App laeuft bereits mit Administratorrechten." : "Die App laeuft ohne Administratorrechte; einige Werte werden fehlen.") +
                "\n\nKeine automatische Rechteerhoehung, keine Aenderung von Defender oder Treibersperren. Sensorzugriff aktivieren?",
                "Optionaler Hardware-Zugriff", MessageBoxButton.YesNo, MessageBoxImage.Information);
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
                _model.SensorStatus = enabled ? "Sensoren werden initialisiert ..." : "Sensorzugriff aus. Keine tiefen Hardware-Messungen.";
            }
            finally { _sensorGate.Release(); }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _sensorsEnabled = false;
            SensorToggle.IsChecked = false;
            ReportError("Sensorzugriff", ex);
            _model.SensorStatus = $"Sensorzugriff fehlgeschlagen: {ex.Message}";
        }
        finally { SensorToggle.IsEnabled = !_closing; }
    }

    private void HistoryChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _closing) return;
        _recording = HistoryToggle.IsChecked == true;
        _lastRecorded = DateTimeOffset.MinValue;
        _model.HistoryStatus = _recording ? $"Aufzeichnung aktiviert: {_history.DirectoryPath}"
            : "Aus. Vorhandene Aufzeichnungen bleiben lokal gespeichert.";
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
            MessageBox.Show(this, ex.Message, "Datenordner konnte nicht geoeffnet werden", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        await Task.WhenAll(_samplingTask, _probeTask, _eventTask, _boardFeedTask, _managerReportingTask,
            _backgroundDiagnosticsTask,
            _currentProbe, _cleanupTask);
        await Task.WhenAll(Workbench.Pending, EmbeddedEvents.Pending, Operations.Pending, HardwareBoard.Pending);
        if (controlCenter is not null) await controlCenter.PendingOperations;
        await _sensorGate.WaitAsync();
        try { await Task.Run(_sensors.Dispose); }
        finally { _sensorGate.Release(); }
        _stop.Dispose();
        _allowClose = true;
        await Dispatcher.InvokeAsync(Close);
    }
}
