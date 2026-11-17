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
        public DateTimeOffset First, Last;
        public long Count;
        public double Min = double.PositiveInfinity, Max = double.NegativeInfinity, Mean;
        public void Add(double value, DateTimeOffset time)
        {
            if (Count == 0) First = time;
            Last = time;
            Count++;
            Min = Math.Min(Min, value);
            Max = Math.Max(Max, value);
            var delta = value - Mean;
            Mean = double.IsFinite(delta) ? Mean + delta / Count : Mean * (1 - 1d / Count) + value / Count;
        }
    }
    private readonly Dictionary<SensorChannelKey, Aggregate> _values = [];
    private DateTimeOffset? _lastSensorTime;
    public string Warning { get; private set; } = "";
    public DateTimeOffset? Started { get; private set; }
    public void Reset() { _values.Clear(); Started = null; Warning = ""; _lastSensorTime = null; }

    public void Add(LiveSample sample)
    {
        Started ??= sample.Timestamp;
        Add("native/cpu", "Windows", "CPU Gesamt", "%", sample.CpuPercent, sample.Timestamp);
        Add("native/ram", "Windows", "RAM Belegung", "%", sample.MemoryPercent, sample.Timestamp);
        Add("native/down", "Netzwerk", "Download", "Mbit/s", sample.DownloadMbps, sample.Timestamp);
        Add("native/up", "Netzwerk", "Upload", "Mbit/s", sample.UploadMbps, sample.Timestamp);
        AddSensorValues(sample.Timestamp, sample.Sensors, retainAbsent: true);
    }

    public void AddSensors(DateTimeOffset time, IReadOnlyList<SensorReading> readings, bool retainAbsent = true)
    {
        if (_lastSensorTime is { } last && time <= last)
            throw new InvalidOperationException("Sensorstatistik benötigt aufsteigende Zeitstempel.");
        AddSensorValues(time, readings, retainAbsent);
        _lastSensorTime = time;
    }

    private void AddSensorValues(DateTimeOffset time, IReadOnlyList<SensorReading> readings, bool retainAbsent)
    {
        var (channels, warning) = SensorSemantics.MeasurementChannels(readings);
        if (!retainAbsent)
            foreach (var key in _values.Keys.Where(k => !channels.ContainsKey(k)).ToArray()) _values.Remove(key);
        if (_values.Keys.Union(channels.Keys).Count() > 2048)
            throw new InvalidOperationException("Statistiklimit von 2048 Messreihen erreicht.");
        Started ??= time;
        Warning = warning;
        foreach (var sensor in channels.Values)
            Add(sensor.Id, sensor.Hardware, sensor.Name, sensor.Unit, sensor.Value, time, sensor.Type,
                sensor.ProviderDeviceId, sensor.PnpDeviceId);
    }

    private void Add(string id, string hardware, string name, string unit, double? value, DateTimeOffset time, string type = "",
        string? providerDeviceId = null, string? pnpDeviceId = null)
    {
        if (value is not { } number || !double.IsFinite(number)) return;
        var key = new SensorChannelKey(id, hardware, name, type, unit, providerDeviceId, pnpDeviceId);
        if (!_values.TryGetValue(key, out var aggregate))
        {
            if (_values.Count >= 2048) throw new InvalidOperationException("Statistiklimit von 2048 Messreihen erreicht.");
            _values[key] = aggregate = new(hardware, name, unit);
        }
        aggregate.Add(number, time);
    }

    public (Statistic Value, DateTimeOffset First, DateTimeOffset Last)? ForSensor(SensorReading sensor) =>
        _values.TryGetValue(SensorSemantics.ChannelKey(sensor), out var a)
            ? (new(a.Hardware, a.Name, a.Unit, a.Count, a.Min, a.Max, a.Mean), a.First, a.Last) : null;

    public IReadOnlyList<Statistic> Snapshot() => _values.Values
        .OrderBy(a => a.Hardware).ThenBy(a => a.Name)
        .Select(a => new Statistic(a.Hardware, a.Name, a.Unit, a.Count, a.Min, a.Max, a.Mean)).ToList();
}
