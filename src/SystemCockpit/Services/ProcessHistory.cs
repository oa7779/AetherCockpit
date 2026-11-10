using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SystemCockpit.Services;

internal sealed record ProcessSample(DateTimeOffset Timestamp, double? Cpu, double? MemoryMb, double? ReadMiBs, double? WriteMiBs, bool Gap);
internal sealed class ProcessRow(ProcessMetric current) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    internal readonly Queue<ProcessSample> Samples = new();
    public ProcessMetric Current { get; private set; } = current;
    public ProcessIdentity Identity => Current.Identity;
    public ProcessIdentity? Parent { get; internal set; }
    public int Depth { get; internal set; }
    public bool Collapsed { get; internal set; }
    public System.Windows.Media.ImageSource? Icon { get; internal set; }
    internal string? ExactPath { get; set; }
    internal bool HadFile { get; set; }
    internal string DetailHints { get; set; } = "";
    public bool Ended { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public string Name => new string(' ', Math.Min(Depth, 32) * 2) + (Collapsed ? "▸ " : "") + Current.Name;
    public string Lifecycle => Ended ? "Nicht mehr beobachtet" : Samples.Count < 3 ? "Neu beobachtet" : Current.Status;
    public string History => string.Join("\n", new[]
    {
        Summary("CPU", "%", Samples.Select(s => s.Cpu)),
        Summary("RAM", "MiB", Samples.Select(s => s.MemoryMb)),
        Summary("I/O Lesen", "MiB/s", Samples.Select(s => s.ReadMiBs)),
        Summary("I/O Schreiben", "MiB/s", Samples.Select(s => s.WriteMiBs)),
        $"Samples: {Samples.Count}/120; Lücken: {Samples.Count(s => s.Gap)}. Keine Interpolation. Netzwerk: keine Prozessbytequelle."
    });
    public string Rules
    {
        get
        {
            var rules = new List<string>();
            var recent = Samples.TakeLast(5).ToArray();
            if (recent.Length == 5 && recent.All(s => !s.Gap && s.Cpu >= 50)) rules.Add("CPU ≥ 50 % der gesamten logischen CPU-Kapazität in 5 aufeinanderfolgenden Proben.");
            if (recent.Length == 5 && recent.All(s => !s.Gap && s.MemoryMb.HasValue) &&
                recent.Zip(recent.Skip(1)).All(p => p.Second.MemoryMb > p.First.MemoryMb))
                rules.Add("RAM in 5 aufeinanderfolgenden Proben streng gestiegen; kein Leaknachweis.");
            if (Current.Handles >= 10000) rules.Add("Mindestens 10.000 Handles in der aktuellen Probe.");
            if (Current.Threads >= 500) rules.Add("Mindestens 500 Threads in der aktuellen Probe.");
            if (Current.StartTicks == 0) rules.Add("Startidentität nicht lesbar; verändernde Aktionen gesperrt.");
            if (DetailHints.Length > 0) rules.Add(DetailHints);
            return rules.Count == 0 ? "Keine dieser erklärbaren Ressourcenregeln ausgelöst; keine Sicherheitsbewertung." : string.Join("\n", rules);
        }
    }

    internal void Update(ProcessMetric metric, bool gap)
    {
        Current = metric;
        Ended = false; EndedAt = null;
        Samples.Enqueue(new(metric.ObservedAt, metric.CpuPercent, metric.MemoryMb, metric.ReadMbps, metric.WriteMbps, gap));
        while (Samples.Count > 120) Samples.Dequeue();
        Notify();
    }
    internal void MarkMissing(DateTimeOffset time)
    {
        if (Ended) return;
        Ended = true; EndedAt = time;
        Samples.Enqueue(new(time, null, null, null, null, true));
        while (Samples.Count > 120) Samples.Dequeue();
        Notify();
    }
    internal void Notify([CallerMemberName] string? unused = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    private static string Summary(string name, string unit, IEnumerable<double?> input)
    {
        var values = input.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        return values.Length == 0 ? $"{name}: unbekannt" : $"{name}: MIN {values.Min():0.00} / MAX {values.Max():0.00} / AVG {values.Average():0.00} {unit} ({values.Length} gültige Stichproben)";
    }
}

internal sealed class ProcessHistory
{
    internal const int Capacity = 8192;
    internal Dictionary<ProcessIdentity, ProcessRow> Rows { get; } = [];
    internal DateTimeOffset? LastSample { get; private set; }
    internal string Status { get; private set; } = "Noch keine Messung.";
    private readonly Dictionary<string, List<(ProcessIdentity Identity, DateTimeOffset Start)>> _starts = new(StringComparer.OrdinalIgnoreCase);
    internal void ObserveDetails(ProcessRow row, ProcessDetails details)
    {
        var hints = new List<string>();
        if (details.Path is { } path)
        {
            row.ExactPath = path;
            if (path.StartsWith(System.IO.Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                hints.Add("Ausführbarer Pfad liegt im aktuellen Windows-Tempverzeichnis; kein Sicherheitsurteil.");
            if (!_starts.TryGetValue(path, out var starts))
            {
                if (_starts.Count >= 256) _starts.Remove(_starts.MinBy(p => p.Value.Count == 0 ? DateTimeOffset.MinValue : p.Value.Max(v => v.Start)).Key);
                _starts[path] = starts = [];
            }
            if (!starts.Any(s => s.Identity == row.Identity)) starts.Add((row.Identity, new DateTimeOffset(new DateTime(row.Identity.StartTicks, DateTimeKind.Utc))));
            starts.RemoveAll(s => DateTimeOffset.UtcNow - s.Start > TimeSpan.FromMinutes(5));
            if (starts.Count > 64) starts.RemoveRange(0, starts.Count - 64);
            var recent = starts.Count(s => DateTimeOffset.UtcNow - s.Start <= TimeSpan.FromSeconds(60));
            if (recent >= 3) hints.Add($"{recent} separat beobachtete Startidentitäten derselben exakten Datei innerhalb 60 s; mehrere Instanzen sind möglich, kein Absturznachweis.");
        }
        else hints.Add("Programmpfad nicht verifizierbar; keine Datei-/Softwarezuordnung.");
        if (details.Window == IntPtr.Zero && row.Current.CpuPercent >= 10)
            hints.Add("CPU ≥ 10 % in der aktuellen Probe, kein sichtbares Top-Level-Fenster bei Detailabfrage; Hintergrundarbeit kann beabsichtigt sein.");
        if (details.Facts.Any(f => f.Observation.Metadata.Availability == AetherOperations.DataAvailability.AccessDenied))
            hints.Add("Windows verweigert mindestens ein Detail; kein Defektnachweis.");
        var file = details.Facts.SingleOrDefault(f => f.Key == "file");
        if (row.HadFile && file?.Observation.Metadata.Error is "FileNotFoundException" or "DirectoryNotFoundException")
            hints.Add("Zuvor lesbare ausführbare Datei ist bei erneuter Detailabfrage nicht mehr vorhanden.");
        row.HadFile |= file?.Observation.HasValue == true;
        row.DetailHints = string.Join("\n", hints);
        row.Notify();
    }
    internal void Apply(IReadOnlyList<ProcessMetric> values, DateTimeOffset time)
    {
        if (values.Select(v => v.Identity).Distinct().Count() != values.Count)
            throw new InvalidOperationException("Mehrdeutige Prozessidentitäten; Probe verworfen.");
        if (LastSample >= time) throw new InvalidOperationException("Rückläufige/gleiche Prozessprobe verworfen.");
        var gap = LastSample is { } last && time - last > TimeSpan.FromSeconds(6);
        var alive = values.Select(v => v.Identity).ToHashSet();
        foreach (var row in Rows.Values.Where(r => !alive.Contains(r.Identity))) row.MarkMissing(time);
        foreach (var key in Rows.Where(pair => pair.Value.EndedAt is { } ended && time - ended > TimeSpan.FromSeconds(20)).Select(pair => pair.Key).ToArray())
            Rows.Remove(key);
        foreach (var value in values)
        {
            if (!Rows.TryGetValue(value.Identity, out var row))
            {
                if (Rows.Count >= Capacity)
                {
                    var expired = Rows.FirstOrDefault(pair => pair.Value.Ended);
                    if (expired.Value is not null) Rows.Remove(expired.Key);
                    else continue;
                }
                Rows.Add(value.Identity, row = new(value));
            }
            row.Update(value with { ObservedAt = time }, gap);
        }
        var byPid = Rows.Values.Where(r => !r.Ended && r.Identity.IsKnown).GroupBy(r => r.Current.Id)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        foreach (var row in Rows.Values)
        {
            row.Parent = row.Current.ParentPid is { } parent && byPid.TryGetValue(parent, out var candidate) &&
                row.Identity.IsKnown && candidate.Current.StartTicks < row.Current.StartTicks ? candidate.Identity : null;
        }
        LastSample = time;
        Status = values.Count > Capacity ? $"Kapazität {Capacity} erreicht; {values.Count - Capacity} Prozesse nicht im Verlauf. Cockpitliste bleibt vollständig." :
            $"{values.Count} Prozesse / {time:HH:mm:ss} / Quelle Windows / max. 120 Samples je Identität / nicht beobachtet ≠ verifiziert beendet";
    }
}
