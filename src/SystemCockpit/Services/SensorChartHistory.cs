using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Media;

namespace SystemCockpit.Services;

public sealed record SensorChartPoint(DateTimeOffset Time, double? Value);

public sealed class SensorChartSeries : Observable
{
    private readonly Action _changed;
    private bool _selected = true, _matches = true;
    private string _current = "Nicht verfuegbar";
    internal int AbsentSamples { get; set; }
    public SensorReading Sensor { get; }
    public string Name => Sensor.Name;
    public string Identity => Sensor.Id;
    public Brush Accent { get; }
    public string Current { get => _current; private set => Set(ref _current, value); }
    public bool MatchesFilter { get => _matches; internal set => Set(ref _matches, value); }
    public bool IsSelected
    {
        get => _selected;
        set { if (_selected == value) return; Set(ref _selected, value); _changed(); }
    }
    private readonly List<SensorChartPoint> _points = [];
    public IReadOnlyList<SensorChartPoint> Points => _points;

    internal SensorChartSeries(SensorReading sensor, Brush accent, Action changed)
    { Sensor = sensor; Accent = accent; _changed = changed; }

    internal void Add(DateTimeOffset time, SensorReading? sensor)
    {
        var value = sensor?.Value is { } number && double.IsFinite(number) ? number : (double?)null;
        _points.Add(new(time, value));
        if (_points.Count > SensorChartHistory.Capacity) _points.RemoveAt(0);
        Current = value is { } v ? $"{v:0.##} {Sensor.Unit}" : "Nicht verfuegbar";
        AbsentSamples = sensor is null ? AbsentSamples + 1 : 0;
    }

    internal void Reset() => _points.Clear();
}

public sealed class SensorChartGroup : Observable
{
    public string Hardware { get; }
    public string HardwareId { get; }
    public string Unit { get; }
    internal string Dimension { get; }
    public string Title => $"{Hardware} / {(Unit.Length == 0 ? Dimension + " (ohne Einheit)" : Unit)}";
    public ObservableCollection<SensorChartSeries> Series { get; } = [];
    public ICollectionView SeriesView { get; }
    public DateTimeOffset Start { get; internal set; }
    public DateTimeOffset End { get; internal set; }
    internal SensorChartGroup(string hardware, string hardwareId, string unit, string dimension)
    {
        Hardware = hardware; HardwareId = hardwareId; Unit = unit; Dimension = dimension;
        SeriesView = CollectionViewSource.GetDefaultView(Series);
        SeriesView.Filter = item => item is SensorChartSeries series && series.MatchesFilter;
    }
    internal void Changed() => OnPropertyChanged(nameof(Series));
}

public sealed class SensorChartHistory : Observable
{
    private static SensorChannelKey Key(SensorReading sensor) => SensorSemantics.ChannelKey(sensor);
    public const int Capacity = 180;
    private static readonly Brush[] Colors =
        [Brushes.Turquoise, Brushes.Orchid, Brushes.Gold, Brushes.DodgerBlue,
         Brushes.SpringGreen, Brushes.Coral, Brushes.LightSkyBlue, Brushes.Plum];
    private readonly Queue<DateTimeOffset> _times = new();
    public ObservableCollection<SensorChartGroup> Groups { get; } = [];
    public ICollectionView View { get; }
    private string _filter = "";
    private string _warning = "";
    public string Warning { get => _warning; private set => Set(ref _warning, value); }
    public string EmptyText => View.IsEmpty
        ? "Keine passenden Messkanaele. Sensorzugriff im Cockpit aktivieren oder Filter aendern. Grenzwerte stehen nur in der Detailmatrix."
        : "";

    public SensorChartHistory()
    {
        View = CollectionViewSource.GetDefaultView(Groups);
        View.Filter = item => item is SensorChartGroup group && group.Series.Any(s => s.MatchesFilter);
    }

    public void Filter(string value)
    {
        _filter = value;
        foreach (var group in Groups) ApplyFilter(group);
        RefreshView();
    }

    private void ApplyFilter(SensorChartGroup group)
    {
        foreach (var series in group.Series)
            series.MatchesFilter = $"{series.Sensor.Hardware} {series.Sensor.Name} {series.Sensor.Type} {series.Sensor.Unit} {series.Identity}"
                .Contains(_filter, StringComparison.OrdinalIgnoreCase);
        group.SeriesView.Refresh();
        group.Changed();
    }

    public void Add(DateTimeOffset time, IEnumerable<SensorReading> readings)
    {
        if (_times.Count > 0 && time <= _times.Last())
        {
            Trace.TraceWarning("Sensor-Verlauf: Zeitstempel nicht aufsteigend; Zeitachse wird neu begonnen.");
            _times.Clear();
            foreach (var series in Groups.SelectMany(group => group.Series)) series.Reset();
        }
        _times.Enqueue(time);
        if (_times.Count > Capacity) _times.Dequeue();
        var all = readings.ToArray();
        var (current, warning) = SensorSemantics.MeasurementChannels(all);
        if (warning != Warning)
        {
            Warning = warning;
            if (warning.Length > 0) Trace.TraceWarning("Sensor-Verlauf: {0}", warning);
        }
        var changed = false;
        var previousIds = Groups.SelectMany(g => g.Series).GroupBy(s => s.Identity)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var group in Groups.ToArray())
            foreach (var series in group.Series.ToArray())
                if (!current.ContainsKey(Key(series.Sensor)) &&
                    (previousIds[series.Identity] == 1 && all.Any(s => s.Id == series.Identity) ||
                     all.Any(s => SensorSemantics.IsTemperatureLimit(s) && s.Id == series.Identity &&
                         s.Hardware == series.Sensor.Hardware && s.Name == series.Name)))
                { group.Series.Remove(series); changed = true; }
        foreach (var sensor in current.Values)
        {
            var hardwareId = HardwareId(sensor.Id);
            var dimension = sensor.Unit.Length == 0 ? sensor.Type : sensor.Unit;
            var group = Groups.FirstOrDefault(g => g.HardwareId == hardwareId && g.Hardware == sensor.Hardware &&
                g.Unit == sensor.Unit && g.Dimension == dimension);
            if (group is null)
            {
                group = new(sensor.Hardware, hardwareId, sensor.Unit, dimension);
                Groups.Add(group);
                changed = true;
            }
            if (!group.Series.Any(s => Key(s.Sensor) == Key(sensor)))
            {
                group.Series.Add(new(sensor, Colors[group.Series.Count % Colors.Length], group.Changed));
                changed = true;
            }
        }
        foreach (var group in Groups.ToArray())
        {
            foreach (var series in group.Series.ToArray())
            {
                var sensor = current.GetValueOrDefault(Key(series.Sensor));
                series.Add(time, sensor);
                if (series.AbsentSamples >= Capacity) { group.Series.Remove(series); changed = true; }
            }
            if (group.Series.Count == 0) { Groups.Remove(group); changed = true; continue; }
            group.Start = _times.Peek(); group.End = time;
            if (changed) ApplyFilter(group);
            else group.Changed();
        }
        if (changed) RefreshView();
    }

    private void RefreshView()
    {
        View.Refresh();
        OnPropertyChanged(nameof(EmptyText));
    }

    internal static string HardwareId(string sensorId)
    {
        var last = sensorId.LastIndexOf('/');
        var type = last > 0 ? sensorId.LastIndexOf('/', last - 1) : -1;
        return type > 0 ? sensorId[..type] : sensorId;
    }
}
