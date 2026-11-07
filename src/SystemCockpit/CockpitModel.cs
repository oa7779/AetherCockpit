using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using SystemCockpit.Services;

namespace SystemCockpit;

public sealed partial class CockpitModel : Observable
{
    internal DateTimeOffset LastSampleAt { get; private set; }
    public ObservableCollection<MetricCard> Cards { get; } =
    [
        new("CPU / AUSLASTUNG", "%", "#24FFE3"),
        new("RAM / BELEGUNG", "%", "#4294FF"),
        new("GPU / CORE LOAD", "%", "#D45CFF"),
        new("NETZ / DOWNLOAD", "Mbit/s", "#32D5FF")
    ];
    public ObservableCollection<Check> Checks { get; } = [];
    public ObservableCollection<Check> Disks { get; } = [];
    public ObservableCollection<Check> Volumes { get; } = [];
    public ObservableCollection<Check> Alerts { get; } = [];
    public ObservableCollection<SensorReading> Sensors { get; } = [];
    public SensorChartHistory SensorCharts { get; } = new();
    public ObservableCollection<Statistic> Statistics { get; } = [];
    public ObservableCollection<Check> Diagnostics { get; } = [];
    public ObservableCollection<CleanupCandidate> CleanupFiles { get; } = [];
    public ObservableCollection<Check> Instruments { get; } = [];
    public ObservableCollection<DashboardGraph> SidebarTemperatures { get; } =
    [
        new("CPU / TEMPERATUR", "C", "#32D5FF"),
        new("GPU / TEMPERATUR", "C", "#D45CFF"),
        new("SSD / TEMPERATUR", "C", "#24FFE3")
    ];
    public DashboardGraph SidebarUpload { get; } = new("NETZ / UPLOAD", "Mbit/s", "#24FFE3");
    private CockpitSettings _settings = new();
    public CockpitSettings Settings { get => _settings; set => Set(ref _settings, value); }
    private readonly SessionStatistics _statistics = new();
    public bool RecordStatistics { get; set; } = true;
    private string _sessionStatus = "Messauswertung ab dem ersten gueltigen Messpunkt.";
    private string _cleanupStatus = "Noch keine Vorschau. Nur lokale Benutzer-Temp-Dateien aelter als 7 Tage.";
    private bool _canClean = true;
    private bool _motionEnabled = System.Windows.SystemParameters.ClientAreaAnimation;
    private string _accessStatus = "";
    public string SessionStatus { get => _sessionStatus; set => Set(ref _sessionStatus, value); }
    public string CleanupStatus { get => _cleanupStatus; set => Set(ref _cleanupStatus, value); }
    public string AccessStatus { get => _accessStatus; set => Set(ref _accessStatus, value); }
    public bool CanClean { get => _canClean; set => Set(ref _canClean, value); }
    public bool MotionEnabled { get => _motionEnabled; set => Set(ref _motionEnabled, value); }
    public ICollectionView SensorView { get; }
    public ICollectionView StatisticsView { get; }
    public ICollectionView CleanupView { get; }
    private string _statisticsFilter = "", _cleanupFilter = "";
    public string StatisticsFilter
    {
        get => _statisticsFilter;
        set { Set(ref _statisticsFilter, value); StatisticsView.Refresh(); }
    }
    public string CleanupFilter
    {
        get => _cleanupFilter;
        set { Set(ref _cleanupFilter, value); CleanupView.Refresh(); }
    }
    private readonly List<double>[] _traces = [[], [], [], []];
    private Check? _measurementError;
    private Check? _sensorError;
    private string _systemIdentity = "Lokale Hardware-Erkennung laeuft ...";
    private string _liveStatus = "INITIALISIERUNG";
    private string _cpuTemperature = "Nicht aktiviert";
    private string _gpuTemperature = "Nicht aktiviert";
    private string _cpuPower = "Nicht aktiviert";
    private string _gpuIdentity = "GPU / Erkennung ausstehend";
    public string GpuIdentity { get => _gpuIdentity; set => Set(ref _gpuIdentity, value); }
    private string _sensorStatus = "Optionaler Zugriff ueber LibreHardwareMonitor. Verfuegbarkeit haengt von Hardware, Treibern und Rechten ab.";
    private string _historyStatus = "Aus. Live-Verlaeufe werden nur im Arbeitsspeicher gehalten.";
    private string _probeStatus = "Lesende Windows-Abfrage laeuft ...";
    private string _securitySummary = "Sicherheitsstatus wird abgefragt.";
    private string _footer = "2s Live/Prozesse | 15s Ereignisse/Disk-I/O | 60s Windows-Status | rein lokal";
    private string _sensorFilter = "";
    private bool _canRefresh;
    public string SystemIdentity { get => _systemIdentity; set => Set(ref _systemIdentity, value); }
    public string LiveStatus { get => _liveStatus; set => Set(ref _liveStatus, value); }
    public string CpuTemperature { get => _cpuTemperature; set => Set(ref _cpuTemperature, value); }
    public string GpuTemperature { get => _gpuTemperature; set => Set(ref _gpuTemperature, value); }
    public string CpuPower { get => _cpuPower; set => Set(ref _cpuPower, value); }
    private string _storageTemperature = "Nicht aktiviert";
    private bool _cpuTempWarning, _gpuTempWarning, _storageTempWarning;
    private string _overlayAlert = "";
    public string StorageTemperature { get => _storageTemperature; set => Set(ref _storageTemperature, value); }
    public bool CpuTempWarning { get => _cpuTempWarning; set => Set(ref _cpuTempWarning, value); }
    public bool GpuTempWarning { get => _gpuTempWarning; set => Set(ref _gpuTempWarning, value); }
    public bool StorageTempWarning { get => _storageTempWarning; set => Set(ref _storageTempWarning, value); }
    public string OverlayAlert { get => _overlayAlert; set => Set(ref _overlayAlert, value); }
    public string SensorStatus { get => _sensorStatus; set => Set(ref _sensorStatus, value); }
    public string HistoryStatus { get => _historyStatus; set => Set(ref _historyStatus, value); }
    public string ProbeStatus { get => _probeStatus; set => Set(ref _probeStatus, value); }
    public string SecuritySummary { get => _securitySummary; set => Set(ref _securitySummary, value); }
    public string Footer { get => _footer; set => Set(ref _footer, value); }
    public bool CanRefresh { get => _canRefresh; set => Set(ref _canRefresh, value); }
    public string SensorFilter
    {
        get => _sensorFilter;
        set { Set(ref _sensorFilter, value); SensorView.Refresh(); SensorCharts.Filter(value); }
    }

    public CockpitModel()
    {
        InitializeDashboard();
        SensorView = CollectionViewSource.GetDefaultView(Sensors);
        SensorView.Filter = item => item is SensorReading s &&
            $"{s.Hardware} {s.Name} {s.Type} {s.Unit} {s.Id}".Contains(SensorFilter, StringComparison.OrdinalIgnoreCase);
        StatisticsView = CollectionViewSource.GetDefaultView(Statistics);
        StatisticsView.Filter = item => item is Statistic s &&
            $"{s.Hardware} {s.Name} {s.Unit}".Contains(StatisticsFilter, StringComparison.OrdinalIgnoreCase);
        CleanupView = CollectionViewSource.GetDefaultView(CleanupFiles);
        CleanupView.Filter = item => item is CleanupCandidate file && file.Path.Contains(CleanupFilter, StringComparison.OrdinalIgnoreCase);
    }

    public void Apply(LiveSample sample, bool sensorsEnabled)
    {
        LastSampleAt = sample.Timestamp;
        _measurementError = null;
        _sensorError = sample.SensorError is { } error
            ? new Check("Hardware-Sensoren", "Abfrage fehlgeschlagen", "unknown", error) : null;
        LiveStatus = $"LIVE / {sample.Timestamp:HH:mm:ss}";
        UpdateCard(0, sample.CpuPercent, "Windows CPU-Gesamtzeit", 100);
        UpdateCard(1, sample.MemoryPercent, $"{sample.MemoryUsedGb:0.0} / {sample.MemoryTotalGb:0.0} GB", 100);
        var selectedGpu = SelectGpu(sample.Sensors);
        GpuIdentity = selectedGpu.FirstOrDefault()?.Hardware ?? (sensorsEnabled ? "GPU / nicht erkannt" : "GPU / Sensorzugriff ausgeschaltet");
        var gpu = FindGpu(selectedGpu, "Load", "GPU Core");
        UpdateCard(2, gpu?.Value, sensorsEnabled ? $"{GpuIdentity} | GPU Core" : "Sensorzugriff nicht aktiviert", 100);
        UpdateCard(3, sample.DownloadMbps, sample.UploadMbps is { } upload
            ? $"Upload {upload:0.00} Mbit/s | alle Adapter | Skala dynamisch" : "Netzwerk-Baseline wird aufgebaut",
            Math.Max(1, _traces[3].Append(sample.DownloadMbps ?? 0).DefaultIfEmpty(1).Max()));
        var cpuTemp = SensorSemantics.CpuTemperature(sample.Sensors);
        var gpuTemp = FindGpu(selectedGpu, "Temperature", "GPU Core");
        var cpuPower = SensorSemantics.CpuPower(sample.Sensors);
        CpuTemperature = cpuTemp?.Display ?? (sensorsEnabled ? "Nicht verfuegbar" : "Nicht aktiviert");
        GpuTemperature = gpuTemp?.Display ?? (sensorsEnabled ? "Nicht verfuegbar" : "Nicht aktiviert");
        CpuPower = cpuPower?.Display ?? (sensorsEnabled ? "Nicht verfuegbar" : "Nicht aktiviert");
        // Overlay-Werte: heissester echter Storage-Messkanal; Grenzwertkanaele bleiben ausgeschlossen.
        var storageTemp = sample.Sensors
            .Where(s => s.Type == "Temperature" && SensorSemantics.IsStorage(s.Id) &&
                !SensorSemantics.IsTemperatureLimit(s) && s.Value is { } v && double.IsFinite(v))
            .OrderByDescending(s => s.Value).FirstOrDefault();
        StorageTemperature = storageTemp?.Display ?? (sensorsEnabled ? "Nicht verfuegbar" : "Nicht aktiviert");
        var temperatureStatus = sensorsEnabled ? "Nicht verfuegbar" : "Nicht aktiviert";
        SidebarTemperatures[0].Update(cpuTemp?.Value, cpuTemp?.Display ?? temperatureStatus);
        SidebarTemperatures[1].Update(gpuTemp?.Value, gpuTemp?.Display ?? temperatureStatus);
        SidebarTemperatures[2].Update(storageTemp?.Value, storageTemp?.Display ?? temperatureStatus);
        SidebarUpload.Update(sample.UploadMbps, sample.UploadMbps is null
            ? "Netzwerk-Baseline wird aufgebaut / nicht verfuegbar" : "Alle aktiven Adapter / dynamische Skala");
        CpuTempWarning = cpuTemp?.Value >= Settings.CpuWarningC;
        GpuTempWarning = gpuTemp?.Value >= Settings.GpuWarningC;
        StorageTempWarning = storageTemp?.Value >= Settings.StorageWarningC;
        OverlayAlert = CpuTempWarning || GpuTempWarning || StorageTempWarning
            ? "WARNSCHWELLE ERREICHT: " + string.Join(" / ", new[]
            {
                CpuTempWarning ? "CPU" : null, GpuTempWarning ? "GPU" : null, StorageTempWarning ? "STORAGE" : null
            }.Where(s => s is not null))
            : "";
        CpuSensorDetail = !sensorsEnabled ? "Hardware-Zugriff ausgeschaltet." :
            $"Temperaturkanal: {cpuTemp?.Name ?? "kein lesbarer CPU-Kanal"} / " +
            $"Leistung: {cpuPower?.Name ?? "kein lesbarer Package-Kanal"}. " +
            "Keine Ableitung aus Mainboard-/ACPI-Zonen. Treiberzugriff und CPU-Unterstuetzung im Providerbericht pruefen." +
            (cpuTemp is null && cpuPower is null ? "\n" + HardwareSensors.KernelAccessHint : "");
        UpdateInstruments(sample.Sensors, sensorsEnabled);
        Replace(Volumes, sample.Volumes.Select(volume => volume.FreePercent is { } percent
            ? volume with { State = percent < Settings.FreeSpaceWarningPercent ? "warning" : "ok" } : volume));
        Replace(Sensors, sample.Sensors.OrderBy(s => s.Hardware).ThenBy(s => s.Type).ThenBy(s => s.Name));
        SensorCharts.Add(sample.Timestamp, sensorsEnabled && sample.SensorError is null ? sample.Sensors : []);
        if (sensorsEnabled)
            SensorStatus = sample.SensorError is { } sensorError ? $"Sensorabfrage fehlgeschlagen: {sensorError}"
                : $"{Sensors.Count(s => s.Value is { } v && double.IsFinite(v))} / {Sensors.Count} Sensorwerte lesbar. " +
                "Nicht verfuegbar = kein Messwert, nicht 0. CPU/GPU-Package-Werte koennen ohne Rechte oder Treiber fehlen." +
                (SensorCharts.Warning.Length == 0 ? "" : "\n" + SensorCharts.Warning);
        RefreshAlerts(cpuTemp, gpuTemp);
        if (RecordStatistics)
        {
            _statistics.Add(sample);
            Replace(Statistics, _statistics.Snapshot());
            SessionStatus = $"Messsitzung seit {_statistics.Started:HH:mm:ss} | {Statistics.Count} Reihen | MIN / MAX / AVG aus gueltigen Messpunkten" +
                (_statistics.Warning.Length == 0 ? "" : "\n" + _statistics.Warning);
        }
    }

    public void MeasurementFailed(string message)
    {
        LastSampleAt = DateTimeOffset.MinValue;
        LiveStatus = "MESSUNG FEHLGESCHLAGEN";
        _measurementError = new Check("Live-Messung", "Nicht verfuegbar", "unknown", message);
        for (var i = 0; i < Cards.Count; i++) UpdateCard(i, null, "Messung fehlgeschlagen", 100);
        Sensors.Clear();
        SensorCharts.Add(DateTimeOffset.Now, []);
        Replace(Volumes, [new Check("Volumes", "Nicht verfuegbar", "unknown", message)]);
        CpuTemperature = GpuTemperature = CpuPower = StorageTemperature = "Nicht verfuegbar";
        foreach (var graph in SidebarTemperatures) graph.Update(null, "Messung fehlgeschlagen / nicht verfuegbar");
        SidebarUpload.Update(null, "Messung fehlgeschlagen / nicht verfuegbar");
        CpuTempWarning = GpuTempWarning = StorageTempWarning = false;
        OverlayAlert = "";
        GpuIdentity = "GPU / Messung fehlgeschlagen";
        UpdateInstruments([], true);
        RefreshAlerts(null, null);
    }

    public void ApplyProbe(ProbeReport report)
    {
        SystemIdentity = $"{report.Cpu} / {report.Gpu}\n{report.Board} | {report.Memory}\n{report.Windows}";
        CpuIdentity = report.Cpu;
        MemoryIdentity = report.Memory;
        Replace(Checks, report.Checks);
        Replace(Disks, report.Disks);
        Replace(Diagnostics, report.Diagnostics ?? []);
        ProbeStatus = $"Zuletzt gelesen: {DateTimeOffset.Now:HH:mm:ss} | naechste automatische Abfrage in etwa 60s";
        SecuritySummary = $"{Checks.Count(c => c.State == "warning")} Hinweise | " +
            $"{Checks.Count(c => c.State == "unknown")} nicht lesbare Pruefungen | " +
            $"{Checks.Count(c => c.State == "ok")} bestandene Pruefungen";
        RefreshAlerts(SensorSemantics.CpuTemperature(Sensors),
            FindGpu(SelectGpu(Sensors), "Temperature", "GPU Core"));
    }

    public void ProbeFailed(string message)
    {
        Replace(Checks, [new Check("Windows-Status", "Abfrage fehlgeschlagen", "unknown", message)]);
        Replace(Disks, [new Check("Storage Health", "Abfrage fehlgeschlagen", "unknown", message)]);
        Replace(Diagnostics, [new Check("Diagnose", "Abfrage fehlgeschlagen", "unknown", message)]);
        ProbeStatus = $"Abfrage fehlgeschlagen: {message}";
        SecuritySummary = "Sicherheitsstatus unbekannt. Keine gueltige aktuelle Abfrage.";
        RefreshAlerts(null, null);
    }

    public void ResetStatistics()
    {
        _statistics.Reset();
        RecordStatistics = true;
        Statistics.Clear();
        SessionStatus = "Messsitzung zurueckgesetzt. Neue Auswertung startet mit dem naechsten Messpunkt.";
    }

    private void UpdateInstruments(IEnumerable<SensorReading> sensors, bool enabled)
    {
        var gpuSensors = SelectGpu(sensors);
        Check Instrument(string label, string prefix, string type, string name, string detail)
        {
            var sensor = prefix == "/gpu-" ? FindGpu(gpuSensors, type, name) : Find(sensors, prefix, type, name);
            return new(label, sensor?.Display ?? (enabled ? "Nicht verfuegbar" : "Nicht aktiviert"),
                sensor?.Value is null ? "unknown" : "info", detail);
        }
        Replace(Instruments,
        [
            Instrument("GPU / HOTSPOT", "/gpu-", "Temperature", "GPU Hot Spot", "Separater Hotspot-Sensor"),
            Instrument("GPU / VRAM", "/gpu-", "SmallData", "GPU Memory Used", "Gemeldete Speicherbelegung"),
            Instrument("GPU / PACKAGE", "/gpu-", "Power", "GPU Package", "Gemeldete Leistungsaufnahme"),
            Instrument("GPU / FAN 1", "/gpu-", "Fan", "GPU Fan 1", "Gemeldete Drehzahl"),
            Instrument("GPU / CLOCK", "/gpu-", "Clock", "GPU Core", "Gemeldeter GPU-Takt"),
            Instrument("GPU / MEMORY TEMP", "/gpu-", "Temperature", "GPU Memory Junction", "Separater Speichertemperatursensor")
        ]);
    }

    private void RefreshAlerts(SensorReading? cpu, SensorReading? gpu)
    {
        var alerts = Checks.Concat(Disks).Concat(Volumes)
            .Where(c => c.State is "warning" or "error" or "unknown").ToList();
        alerts.AddRange(SensorDiagnostics.Evaluate(Sensors, Settings).Where(c => c.State == "warning"));
        if (_measurementError is not null) alerts.Insert(0, _measurementError);
        if (_sensorError is not null) alerts.Insert(0, _sensorError);
        if (cpu?.Value >= Settings.CpuWarningC) alerts.Insert(0, new("CPU-Temperatur", cpu.Display, "warning", $"CPU Package >= {Settings.CpuWarningC} C. Beobachtungshinweis, kein Nachweis von Drosselung."));
        if (gpu?.Value >= Settings.GpuWarningC) alerts.Insert(0, new("GPU-Temperatur", gpu.Display, "warning", $"GPU Core >= {Settings.GpuWarningC} C. Hotspot separat."));
        Replace(Alerts, alerts);
    }

    public static SensorReading? Find(IEnumerable<SensorReading> sensors, string hardwarePrefix, string type, string name) =>
        sensors.FirstOrDefault(s => s.Id.StartsWith(hardwarePrefix, StringComparison.OrdinalIgnoreCase) &&
            s.Type == type && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<SensorReading> SelectGpu(IEnumerable<SensorReading> sensors) =>
        sensors.Where(s => s.Id.StartsWith("/gpu-", StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => string.Join("/", s.Id.Split('/').Take(3)), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Any(s => s.Hardware.Contains("RTX", StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(g => g.Key.StartsWith("/gpu-nvidia/", StringComparison.OrdinalIgnoreCase) ||
                g.Key.StartsWith("/gpu-amd/", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(g => g.Any(s => s.Value is { } v && double.IsFinite(v)))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.ToList() ?? [];

    public static SensorReading? FindGpu(IEnumerable<SensorReading> selectedGpu, string type, string name) =>
        selectedGpu.FirstOrDefault(s => s.Type == type && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void UpdateCard(int index, double? value, string detail, double scale)
    {
        var card = Cards[index];
        card.Value = value is { } number && double.IsFinite(number) ? $"{number:0.0}" : "--";
        card.Detail = detail;
        card.Level = value is { } level && double.IsFinite(level) ? Math.Clamp(level / scale * 100, 0, 100) : 0;
        if (value is { } point && double.IsFinite(point))
        {
            _traces[index].Add(point);
            if (_traces[index].Count > 90) _traces[index].RemoveAt(0);
        }
        else _traces[index].Clear();
        card.Trace = Sparkline.Create(_traces[index], scale);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
