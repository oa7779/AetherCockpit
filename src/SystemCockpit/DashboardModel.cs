using System.Collections.ObjectModel;
using System.Windows.Data;
using System.ComponentModel;
using System.Windows.Media;
using SystemCockpit.Services;

namespace SystemCockpit;

public sealed class DashboardGraph(string title, string unit, string accent) : Observable
{
    private readonly List<double> _points = [];
    public string Title { get; } = title;
    public string Unit { get; } = unit;
    public Brush Accent { get; } = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent));
    private string _value = "--", _detail = "";
    private string _numericValue = "--";
    public string NumericValue { get => _numericValue; private set => Set(ref _numericValue, value); }
    private double _level;
    private PointCollection _trace = [];
    public string Value { get => _value; private set => Set(ref _value, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public double Level { get => _level; private set => Set(ref _level, value); }
    public PointCollection Trace { get => _trace; private set => Set(ref _trace, value); }

    public void Update(double? value, string detail, double? fixedScale = null)
    {
        Detail = detail;
        if (value is not { } number || !double.IsFinite(number))
        {
            Value = "--";
            NumericValue = "--";
            Level = 0;
            _points.Clear();
            Trace = [];
            return;
        }
        _points.Add(number);
        if (_points.Count > 90) _points.RemoveAt(0);
        var scale = fixedScale ?? Math.Max(1, _points.Max());
        Value = $"{number:0.0} {Unit}";
        NumericValue = $"{number:0.0}";
        Level = Math.Clamp(100 * number / scale, 0, 100);
        Trace = Sparkline.Create(_points, scale);
        Detail += $" | Skala 0..{scale:0.0} {Unit}";
    }
}

public sealed record EventBucket(string Label, int Count, double Height, string Detail);

public sealed partial class CockpitModel
{
    public ObservableCollection<DashboardGraph> CoreGraphs { get; } = [];
    public int CoreColumns => Math.Max(1, (int)Math.Ceiling(Math.Sqrt(CoreGraphs.Count * 1.5)));
    private string _cpuIdentity = "CPU / Hardwareinventar ausstehend";
    public string CpuIdentity { get => _cpuIdentity; set => Set(ref _cpuIdentity, value); }
    private string _memoryIdentity = "RAM / Inventar ausstehend";
    public string MemoryIdentity { get => _memoryIdentity; set => Set(ref _memoryIdentity, value); }
    public ObservableCollection<DashboardGraph> DetailGraphs { get; } = [];
    public ObservableCollection<DashboardGraph> AdapterGraphs { get; } = [];
    public ObservableCollection<DashboardGraph> SensorGraphs { get; } = [];
    public ObservableCollection<DashboardGraph> VolumeGraphs { get; } = [];
    public ObservableCollection<DashboardGraph> DiskGraphs { get; } = [];
    public ObservableCollection<ProcessMetric> Processes { get; } = [];
    public ObservableCollection<MonitorEvent> Events { get; } = [];
    public ObservableCollection<Check> EventCounts { get; } = [];
    public ObservableCollection<EventBucket> EventBuckets { get; } = [];
    private readonly Dictionary<string, DashboardGraph> _sensorGraphs = [];
    private readonly Dictionary<string, DashboardGraph> _adapterGraphs = [];
    private readonly Dictionary<string, DashboardGraph> _volumeGraphs = [];
    private readonly Dictionary<string, DashboardGraph> _diskGraphs = [];
    private string _dashboardStatus = "Taskmanager-Messung wird vorbereitet.";
    private string _cpuSensorDetail = "Sensorprovider wird initialisiert.";
    public string CpuSensorDetail { get => _cpuSensorDetail; set => Set(ref _cpuSensorDetail, value); }
    private string _systemClock = "--:--:--", _systemUptime = "Windows-Uptime / Baseline ausstehend";
    public string SystemClock { get => _systemClock; private set => Set(ref _systemClock, value); }
    public string SystemUptime { get => _systemUptime; private set => Set(ref _systemUptime, value); }
    private string _eventStatus = "Ereignisse werden gelesen.";
    private string _processFilter = "", _eventFilter = "";
    private int _eventSeverity;
    private double _physicalMemoryMb = 1;
    private MonitorEvent? _selectedEvent;
    public string DashboardStatus { get => _dashboardStatus; set => Set(ref _dashboardStatus, value); }
    public string EventStatus { get => _eventStatus; set => Set(ref _eventStatus, value); }
    public string ProcessFilter { get => _processFilter; set { Set(ref _processFilter, value); ProcessView.Refresh(); } }
    public string EventFilter { get => _eventFilter; set { Set(ref _eventFilter, value); EventView.Refresh(); } }
    public int EventSeverity { get => _eventSeverity; set { Set(ref _eventSeverity, value); EventView.Refresh(); } }
    public MonitorEvent? SelectedEvent { get => _selectedEvent; set => Set(ref _selectedEvent, value); }
    public double PhysicalMemoryMb { get => _physicalMemoryMb; private set => Set(ref _physicalMemoryMb, value); }
    public ICollectionView ProcessView { get; private set; } = null!;
    public ICollectionView EventView { get; private set; } = null!;

    private void InitializeDashboard()
    {
        ProcessView = CollectionViewSource.GetDefaultView(Processes);
        ProcessView.Filter = o => o is ProcessMetric p && $"{p.Name} {p.Id} {p.Status}".Contains(ProcessFilter, StringComparison.OrdinalIgnoreCase);
        ProcessView.SortDescriptions.Add(new(nameof(ProcessMetric.CpuPercent), ListSortDirection.Descending));
        EventView = CollectionViewSource.GetDefaultView(Events);
        EventView.Filter = o => o is MonitorEvent e && e.SearchText.Contains(EventFilter, StringComparison.OrdinalIgnoreCase) &&
            (EventSeverity == 0 || EventSeverity == 1 && e.Level is 1 or 2 || EventSeverity == 2 && e.Level == 3 || EventSeverity == 3 && e.Level == 4);
        DetailGraphs.Add(new("RAM / VERWENDET", "GB", "#4294FF"));
        DetailGraphs.Add(new("COMMIT / ZUGESICHERT", "GB", "#D45CFF"));
        DetailGraphs.Add(new("SYSTEM CACHE", "GB", "#32D5FF"));
        DetailGraphs.Add(new("NETZ / UPLOAD", "Mbit/s", "#24FFE3"));
    }

    public void ApplyDashboard(DashboardSnapshot snapshot, LiveSample sample)
    {
        SystemClock = snapshot.Timestamp.LocalDateTime.ToString("HH:mm:ss");
        SystemUptime = $"UPTIME {snapshot.Uptime.Days}d {snapshot.Uptime:hh\\:mm\\:ss}";
        PhysicalMemoryMb = Math.Max(1, sample.MemoryTotalGb * 1024);
        if (CoreGraphs.Count != snapshot.Cores.Count)
        {
            CoreGraphs.Clear();
            for (var i = 0; i < snapshot.Cores.Count; i++) CoreGraphs.Add(new($"CPU / LOGISCH {i}", "%", "#24FFE3"));
            OnPropertyChanged(nameof(CoreColumns));
        }

        for (var i = 0; i < snapshot.Cores.Count; i++) CoreGraphs[i].Update(snapshot.Cores[i], "Windows-Kernzeit / 2s", 100);
        DetailGraphs[0].Update(sample.MemoryUsedGb, $"{sample.MemoryTotalGb - sample.MemoryUsedGb:0.0} GB verfuegbar", sample.MemoryTotalGb);
        DetailGraphs[1].Update(snapshot.CommitGb, $"Limit {snapshot.CommitLimitGb:0.0} GB / RAM + Auslagerung", snapshot.CommitLimitGb);
        DetailGraphs[2].Update(snapshot.CacheGb, "System-Cache / nicht ausschliesslich Dateicache", sample.MemoryTotalGb);
        DetailGraphs[3].Update(sample.UploadMbps, "Alle aktiven Adapter / dynamisch");
        Replace(Processes, snapshot.Processes);
        DashboardStatus = $"{snapshot.Timestamp:HH:mm:ss} / {snapshot.Processes.Count} Prozesse / {snapshot.Threads:N0} Threads / " +
            $"{snapshot.Handles:N0} Handles / Laufzeit {snapshot.Uptime.Days}d {snapshot.Uptime:hh\\:mm\\:ss} / " +
            $"{snapshot.Processes.Count(p => p.CpuPercent is null)} Prozesse ohne aktuellen CPU-Wert" +
            (snapshot.Issues.Count > 0 ? "\n" + string.Join("\n", snapshot.Issues) : "");
        var activeAdapters = new HashSet<string>();
        foreach (var adapter in snapshot.Adapters)
        {
            foreach (var (suffix, value) in new[] { ("RX", adapter.DownloadMbps), ("TX", adapter.UploadMbps) })
            {
                var id = adapter.Id + suffix;
                activeAdapters.Add(id);
                if (!_adapterGraphs.TryGetValue(id, out var graph)) _adapterGraphs[id] = graph = new($"{adapter.Name} / {suffix}", "Mbit/s", "#32D5FF");
                graph.Update(value, $"Link {adapter.Speed / 1_000_000d:0} Mbit/s / dynamisch");
            }
        }
        foreach (var id in _adapterGraphs.Keys.Except(activeAdapters).ToArray()) _adapterGraphs.Remove(id);
        Replace(AdapterGraphs, _adapterGraphs.Values);
        var activeVolumes = new HashSet<string>();
        foreach (var volume in sample.Volumes)
        {
            activeVolumes.Add(volume.Name);
            if (!_volumeGraphs.TryGetValue(volume.Name, out var graph)) _volumeGraphs[volume.Name] = graph = new(volume.Name + " / BELEGT", "%", "#4294FF");
            graph.Update(volume.FreePercent is { } free ? 100 - free : null, volume.Detail, 100);
        }
        foreach (var id in _volumeGraphs.Keys.Except(activeVolumes).ToArray()) _volumeGraphs.Remove(id);
        Replace(VolumeGraphs, _volumeGraphs.Values);
        var activeSensors = new HashSet<string>();
        foreach (var sensor in sample.Sensors.Where(s => !SensorSemantics.IsTemperatureLimit(s)))
        {
            activeSensors.Add(sensor.Id);
            if (!_sensorGraphs.TryGetValue(sensor.Id, out var graph)) _sensorGraphs[sensor.Id] = graph = new(sensor.Name, sensor.Unit, "#D45CFF");
            graph.Update(sensor.Value, sensor.Hardware + " / " + sensor.Type, sensor.Type == "Load" ? 100 : null);
        }
        foreach (var id in _sensorGraphs.Keys.Except(activeSensors).ToArray()) _sensorGraphs.Remove(id);
        Replace(SensorGraphs, _sensorGraphs.Values);
    }

    public void DashboardFailed(string message)
    {
        DashboardStatus = "Taskmanager-Messung fehlgeschlagen: " + message;
        Processes.Clear();
        foreach (var graph in CoreGraphs.Concat(DetailGraphs).Concat(AdapterGraphs).Concat(VolumeGraphs).Concat(SensorGraphs))
            graph.Update(null, "Abfrage fehlgeschlagen");
    }

    public void ApplyEvents(EventSnapshot snapshot)
    {
        var selected = SelectedEvent;
        Replace(Events, snapshot.Events.OrderByDescending(e => e.Timestamp));
        SelectedEvent = selected is null ? null : Events.FirstOrDefault(e => e.Log == selected.Log && e.RecordId == selected.RecordId);
        var total = Events.Count;
        Replace(EventCounts, new[] { (1, "KRITISCH"), (2, "FEHLER"), (3, "WARNUNG"), (4, "INFORMATION") }.Select(item =>
        {
            var count = Events.Count(e => e.Level == item.Item1);
            return new Check(item.Item2, count.ToString(), item.Item1 <= 2 ? "error" : item.Item1 == 3 ? "warning" : "info",
                "Anteil im begrenzten Snapshot, nicht alle Ereignisse seit Start", total == 0 ? 0 : 100d * count / total);
        }));
        var start = snapshot.Timestamp.AddHours(-24);
        var buckets = Enumerable.Range(0, 24).Select(i =>
        {
            var from = start.AddHours(i);
            var until = from.AddHours(1);
            var count = Events.Count(e => e.Timestamp >= from && (i == 23 ? e.Timestamp <= until : e.Timestamp < until));
            return (From: from, Until: until, Count: count);
        }).ToArray();
        var peak = Math.Max(1, buckets.Max(b => b.Count));
        Replace(EventBuckets, buckets.Select(b => new EventBucket(b.From.ToLocalTime().ToString("HH"), b.Count,
            70d * b.Count / peak, $"{b.From.ToLocalTime():MM-dd HH:mm} bis {b.Until.ToLocalTime():MM-dd HH:mm}: {b.Count} Ereignisse im begrenzten Snapshot")));
        EventStatus = $"{snapshot.Timestamp.ToLocalTime():HH:mm:ss} / {total} Ereignisse / letzte 24h, max. 200 je System/Application / " +
            "Polling alle 15s; Zaehler nur fuer diesen Snapshot" +
            (snapshot.Issues.Count > 0 ? "\nNicht vollstaendig: " + string.Join(" | ", snapshot.Issues) : "");
        var active = new HashSet<string>();
        foreach (var disk in snapshot.DiskActivity ?? [])
        {
            foreach (var (suffix, value, unit, scale) in new (string, double?, string, double?)[]
            {
                ("READ", disk.ReadMbps, "MB/s", null), ("WRITE", disk.WriteMbps, "MB/s", null),
                ("AKTIV", disk.BusyPercent, "%", 100), ("QUEUE", disk.QueueLength, "I/O", null)
            })
            {
                var id = disk.Name + suffix;
                active.Add(id);
                if (!_diskGraphs.TryGetValue(id, out var graph)) _diskGraphs[id] = graph = new($"DISK {disk.Name} / {suffix}", unit, "#4294FF");
                graph.Update(value, "Windows-Performancezaehler / 15s Polling", scale);
            }
        }
        foreach (var id in _diskGraphs.Keys.Except(active).ToArray()) _diskGraphs.Remove(id);
        Replace(DiskGraphs, _diskGraphs.Values);
    }

    public void EventsFailed(string message)
    {
        Events.Clear();
        EventCounts.Clear();
        EventBuckets.Clear();
        foreach (var graph in DiskGraphs) graph.Update(null, "Windows-Performanceabfrage fehlgeschlagen");
        SelectedEvent = null;
        EventStatus = "Ereignismonitor nicht verfuegbar: " + message;
    }
}
