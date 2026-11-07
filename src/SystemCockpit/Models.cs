using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SystemCockpit;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class MetricCard(string title, string unit, string accent) : Observable
{
    public string Title { get; } = title;
    public string Unit { get; } = unit;
    public Brush Accent { get; } = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent));
    private string _value = "--";
    private string _detail = "Messung wird vorbereitet";
    private PointCollection _trace = [];
    private double _level;
    public string Value { get => _value; set => Set(ref _value, value); }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    public PointCollection Trace { get => _trace; set => Set(ref _trace, value); }
    public double Level { get => _level; set => Set(ref _level, value); }
}

public sealed record SensorReading(string Id, string Hardware, string Name, string Type,
    double? Value, string Unit)
{
    public string Display => Value is { } value && double.IsFinite(value)
        ? $"{value:0.##} {Unit}" + (Services.SensorSemantics.IsTemperatureLimit(this) ? " (Grenzwert, keine Ist-Temperatur)" : "")
        : "Nicht verfuegbar";
}

public sealed record Check(string Name, string Value, string State, string Detail, double? FreePercent = null)
{
    public double? UsedPercent => FreePercent is { } free && double.IsFinite(free) && free is >= 0 and <= 100
        ? 100 - free : null;
    public string UsageSummary => UsedPercent is { } used
        ? $"{used:0.0} % belegt / {FreePercent:0.0} % frei"
        : "Nicht verfuegbar";
    public Brush Accent => State switch
    {
        "ok" => Brushes.MediumAquamarine,
        "warning" => Brushes.Orange,
        "error" => Brushes.IndianRed,
        _ => Brushes.LightSlateGray
    };
}

public sealed record ProbeReport(string Cpu, string Gpu, string Board, string Memory,
    string Windows, List<Check> Checks, List<Check> Disks, List<Check>? Diagnostics = null);

public sealed record LiveSample(DateTimeOffset Timestamp, double? CpuPercent, double MemoryPercent,
    double MemoryUsedGb, double MemoryTotalGb, double? DownloadMbps, double? UploadMbps,
    List<Check> Volumes, List<SensorReading> Sensors, string? SensorError = null);
