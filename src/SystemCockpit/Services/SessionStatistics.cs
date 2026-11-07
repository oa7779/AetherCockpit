namespace SystemCockpit.Services;

public sealed record Statistic(string Hardware, string Name, string Unit, long Count, double Minimum,
    double Maximum, double Average)
{
    public string MinDisplay => $"{Minimum:0.##} {Unit}";
    public string MaxDisplay => $"{Maximum:0.##} {Unit}";
    public string AvgDisplay => $"{Average:0.##} {Unit}";
}

public sealed class SessionStatistics
{
    private sealed class Aggregate(string hardware, string name, string unit)
    {
        public string Hardware { get; } = hardware;
        public string Name { get; } = name;
        public string Unit { get; } = unit;
        public long Count;
        public double Min = double.PositiveInfinity, Max = double.NegativeInfinity, Mean;
        public void Add(double value)
        {
            Count++;
            Min = Math.Min(Min, value);
            Max = Math.Max(Max, value);
            Mean += (value - Mean) / Count;
        }
    }
    private readonly Dictionary<SensorChannelKey, Aggregate> _values = [];
    public string Warning { get; private set; } = "";
    public DateTimeOffset? Started { get; private set; }
    public void Reset() { _values.Clear(); Started = null; Warning = ""; }

    public void Add(LiveSample sample)
    {
        Started ??= sample.Timestamp;
        Add("native/cpu", "Windows", "CPU Gesamt", "%", sample.CpuPercent);
        Add("native/ram", "Windows", "RAM Belegung", "%", sample.MemoryPercent);
        Add("native/down", "Netzwerk", "Download", "Mbit/s", sample.DownloadMbps);
        Add("native/up", "Netzwerk", "Upload", "Mbit/s", sample.UploadMbps);
        var (channels, warning) = SensorSemantics.MeasurementChannels(sample.Sensors);
        Warning = warning;
        foreach (var sensor in channels.Values)
            Add(sensor.Id, sensor.Hardware, sensor.Name, sensor.Unit, sensor.Value, sensor.Type);
    }

    private void Add(string id, string hardware, string name, string unit, double? value, string type = "")
    {
        if (value is not { } number || !double.IsFinite(number)) return;
        var key = new SensorChannelKey(id, hardware, name, type, unit);
        if (!_values.TryGetValue(key, out var aggregate))
        {
            if (_values.Count >= 2048) throw new InvalidOperationException("Statistiklimit von 2048 Messreihen erreicht.");
            _values[key] = aggregate = new(hardware, name, unit);
        }
        aggregate.Add(number);
    }

    public IReadOnlyList<Statistic> Snapshot() => _values.Values
        .OrderBy(a => a.Hardware).ThenBy(a => a.Name)
        .Select(a => new Statistic(a.Hardware, a.Name, a.Unit, a.Count, a.Min, a.Max, a.Mean)).ToList();
}
