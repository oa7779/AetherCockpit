using System.Globalization;
using System.Text.Json;
using AetherOperations;

namespace SystemCockpit.Services;

internal enum WatchTrigger { Cpu, Ram, ProcessCpu, ProcessMemory, ProcessAppeared, ProcessMissing, NewListener, NewStartup, NewService, HardwareChanged, AdapterDisconnected, Crash, Sensor, DiskFree, Gpu }
internal enum WatchAction { Notification, Timeline, Snapshot, DiagnosisMarker }
internal sealed record IntelligenceRule(Guid Id, string Name, WatchTrigger Trigger, string Target, double Threshold,
    int DurationSeconds, int CooldownSeconds, WatchAction Action, bool Enabled = true)
{
    internal void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 160 || Target is null || Target.Length > 32768 ||
            !Enum.IsDefined(Trigger) || !Enum.IsDefined(Action) || !double.IsFinite(Threshold) ||
            Trigger is WatchTrigger.Cpu or WatchTrigger.Ram or WatchTrigger.ProcessCpu or WatchTrigger.Gpu or WatchTrigger.DiskFree && Threshold is < 0 or > 100 ||
            Trigger == WatchTrigger.ProcessMemory && Threshold < 0 ||
            Trigger is WatchTrigger.ProcessAppeared or WatchTrigger.ProcessMissing or WatchTrigger.NewListener or WatchTrigger.NewStartup or
                WatchTrigger.NewService or WatchTrigger.HardwareChanged or WatchTrigger.AdapterDisconnected or WatchTrigger.Crash && Threshold < 1 ||
            Trigger == WatchTrigger.Crash && Threshold != 1 ||
            DurationSeconds is < 0 or > 3600 || CooldownSeconds is < 1 or > 86400 ||
            Trigger is WatchTrigger.ProcessAppeared or WatchTrigger.ProcessMissing or WatchTrigger.NewListener or WatchTrigger.NewStartup or
                WatchTrigger.NewService or WatchTrigger.HardwareChanged or WatchTrigger.AdapterDisconnected or WatchTrigger.Crash && DurationSeconds != 0 ||
            Trigger is WatchTrigger.ProcessCpu or WatchTrigger.ProcessMemory or WatchTrigger.Sensor or WatchTrigger.Gpu && string.IsNullOrWhiteSpace(Target))
            throw new JsonException("Ungültige Watch-Regel.");
    }
}
[Flags]
internal enum RecordingScope { Resources = 1, Processes = 2, Network = 4, Sensors = 8, SystemContext = 16, Events = 32 }
internal sealed record RecordedProcess(string Identity, string Name, double? Cpu, double? MemoryMiB, double? ReadMiBs, double? WriteMiBs);
internal sealed record RecordedValue(string Id, double Value, string Unit, string Source, DateTimeOffset Timestamp);
internal sealed record ResourcePoint(DateTimeOffset Timestamp, double? Cpu, double? Ram, double? ProcessCpu, double? ProcessMemory, string? Process,
    IReadOnlyList<RecordedProcess>? Processes = null, IReadOnlyList<RecordedValue>? Values = null);
internal sealed record BootObservation(string Id, DateTimeOffset Started, DateTimeOffset? Idle, int ValidSamples, string Criterion, bool Complete);
internal sealed record IntelligenceSession(int Version, IReadOnlyList<TimelineEntry> Timeline, IReadOnlyList<IntelligenceRule> Rules,
    IReadOnlyList<BootObservation> Boots, IReadOnlyDictionary<Guid, DateTimeOffset> LastTriggered)
{
    internal void Validate()
    {
        if (Version != 1 || Timeline is null || Rules is null || Boots is null || LastTriggered is null ||
            Timeline.Count > IntelligenceModel.MaximumTimeline || Rules.Count > 32 || Boots.Count > 128 || LastTriggered.Count > 32 ||
            Timeline.Any(e => e is null || string.IsNullOrWhiteSpace(e.Id) || e.Id.Length > 32768 || e.Title is null ||
                e.Title.Length > 65536 || e.Detail is null || e.Detail.Length > 65536 || e.Category is null || e.Category.Length > 256 ||
                e.Occurred == default || e.Observed == default ||
                !IntelligenceModel.Valid(e.Metadata) || e.Metadata.Classification == DataClassification.Secret ||
                e.Event is { } reference && (reference.Channel is null || reference.Channel.Length > 256 ||
                    reference.Provider is null || reference.Provider.Length > 256 || reference.RecordId < 0 || reference.Subject?.Length > 8192)) ||
            Timeline.Select(e => e.Id).Distinct().Count() != Timeline.Count ||
            Boots.Any(b => b is null || string.IsNullOrWhiteSpace(b.Id) || b.Id.Length > 160 || b.Criterion is null || b.Criterion.Length > 4096 ||
                b.Started == default || b.Idle < b.Started || b.Complete != (b.Idle is not null) || b.ValidSamples is < 0 or > 1800) ||
            Boots.Select(b => b.Id).Distinct().Count() != Boots.Count ||
            Rules.Any(r => r is null) || Rules.Select(r => r.Id).Distinct().Count() != Rules.Count)
            throw new JsonException("Ungültige Intelligence-Sitzung.");
        foreach (var rule in Rules) rule.Validate();
        if (LastTriggered.Any(p => !Rules.Any(r => r.Id == p.Key) || p.Value == default))
            throw new JsonException("Ungültiger Watch-Cooldown.");
    }
}
internal sealed record DiagnosisRecording(int Version, Guid Id, string Name, DateTimeOffset Start, DateTimeOffset End, string Profile,
    IReadOnlyList<ResourcePoint> Samples, IReadOnlyList<TimelineEntry> Markers, IReadOnlyList<string> Issues,
    IntelligenceSnapshot Before, IntelligenceSnapshot After, bool Cancelled, RecordingScope Scope = RecordingScope.Resources,
    double ElapsedMilliseconds = 0, bool ClockDiscontinuity = false)
{
    internal void Validate()
    {
        if (Version != 1 || Id == Guid.Empty || Name is null || Profile is null || Start == default || End == default || End < Start && !ClockDiscontinuity ||
            !double.IsFinite(ElapsedMilliseconds) || ElapsedMilliseconds < 0 ||
            Samples is null || Samples.Count > 1800 || Markers is null || Markers.Count > IntelligenceModel.MaximumTimeline || Issues is null ||
            Issues.Count > 256 || Samples.Any(s => s is null || s.Timestamp == default || !ClockDiscontinuity && (s.Timestamp < Start || s.Timestamp > End) ||
                new[] { s.Cpu, s.Ram, s.ProcessCpu, s.ProcessMemory }.Any(v => v is { } n && (!double.IsFinite(n) || n < 0)) ||
                s.Processes is { } processes && (processes.Count > 16 || processes.Any(p => p is null || p.Identity is null || p.Name is null ||
                    new[] { p.Cpu, p.MemoryMiB, p.ReadMiBs, p.WriteMiBs }.Any(v => v is { } n && (!double.IsFinite(n) || n < 0)))) ||
                s.Values is { } values && (values.Count > 32 || values.Any(v => v is null || v.Id is null || v.Source is null || v.Unit is null ||
                    !double.IsFinite(v.Value) || v.Timestamp == default))) || (Scope & ~(RecordingScope)63) != 0)
            throw new JsonException("Ungültige Diagnoseaufzeichnung.");
        IntelligenceModel.Validate(Before); IntelligenceModel.Validate(After);
        new IntelligenceSession(1, Markers, [], [], new Dictionary<Guid, DateTimeOffset>()).Validate();
    }
}
internal sealed class WatchEvaluator
{
    private sealed class State { internal DateTimeOffset? Since, LastSample, Fired; internal bool Latched; }
    private readonly Dictionary<Guid, State> _states = [];
    internal void Reset(Guid id) => _states.Remove(id);
    internal void Clear() => _states.Clear();
    internal bool Event(IntelligenceRule rule, DateTimeOffset now, DateTimeOffset? previous)
    {
        rule.Validate();
        if (!rule.Enabled || previous is { } fired && now - fired < TimeSpan.FromSeconds(rule.CooldownSeconds)) return false;
        return true;
    }
    internal bool Evaluate(IntelligenceRule rule, double? value, DateTimeOffset sourceTime, DateTimeOffset now, DateTimeOffset? persistedFire = null)
    {
        rule.Validate();
        if (!_states.TryGetValue(rule.Id, out var state)) _states.Add(rule.Id, state = new() { Fired = persistedFire });
        if (!rule.Enabled) { state.Since = null; state.Latched = false; state.LastSample = null; return false; }
        if (sourceTime > now || now - sourceTime > TimeSpan.FromSeconds(10) || value is null || !double.IsFinite(value.Value) ||
            state.LastSample is { } previous && (sourceTime <= previous || sourceTime - previous > TimeSpan.FromSeconds(6)))
        {
            state.Since = null; state.Latched = false; state.LastSample = sourceTime; return false;
        }
        state.LastSample = sourceTime;
        var matches = rule.Trigger == WatchTrigger.DiskFree ? value <= rule.Threshold : value >= rule.Threshold;
        if (!matches) { state.Since = null; state.Latched = false; return false; }
        state.Since ??= sourceTime;
        if (state.Latched || sourceTime - state.Since < TimeSpan.FromSeconds(rule.DurationSeconds) ||
            state.Fired is { } fired && now - fired < TimeSpan.FromSeconds(rule.CooldownSeconds)) return false;
        state.Latched = true; state.Fired = now;
        return true;
    }
}

internal sealed class IntelligenceSessionEngine
{
    private readonly TimeProvider _clock;
    internal IntelligenceSessionEngine(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;
    private readonly List<TimelineEntry> _timeline = [];
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private Dictionary<ProcessIdentity, string>? _processes;
    private readonly Queue<ResourcePoint> _resources = new();
    private readonly WatchEvaluator _watch = new();
    private readonly Dictionary<Guid, DateTimeOffset> _lastTriggered = [];
    private readonly List<ResourcePoint> _recording = [];
    private readonly List<TimelineEntry> _markers = [];
    private readonly List<string> _recordingIssues = [];
    private readonly List<BootObservation> _boots = [];
    internal Dictionary<Guid, string> RuleStatus { get; } = [];
    private DateTimeOffset? _lastResource, _recordingStart;
    private IntelligenceSnapshot? _before;
    private TimeSpan _duration;
    private int _idleSamples;
    private bool _bootStarted;
    private RecordingScope _scope;
    private int _recordingBytes, _markerBytes;
    private long _recordingStamp;
    private bool _clockDiscontinuity;
    internal const string IdleCriterion = "AETHER: 10 aufeinanderfolgende gültige gemeinsame Proben mit CPU < 10 %, Abstand höchstens 6 s; keine Windows-Bootfreigabe.";
    internal IReadOnlyList<TimelineEntry> Timeline => _timeline;
    internal IReadOnlyCollection<ResourcePoint> Resources => _resources;
    internal IReadOnlyList<BootObservation> Boots => _boots;
    internal List<IntelligenceRule> Rules { get; } = [];
    internal void UpdateRule(IntelligenceRule rule)
    {
        rule.Validate();
        var index = Rules.FindIndex(r => r.Id == rule.Id);
        if (index >= 0) Rules[index] = rule;
        else { if (Rules.Count >= 32) throw new InvalidOperationException("Maximal 32 Watch-Regeln."); Rules.Add(rule); }
        _watch.Reset(rule.Id); Revision++;
    }
    internal void DeleteRule(IntelligenceRule rule)
    { Rules.Remove(rule); _watch.Reset(rule.Id); _lastTriggered.Remove(rule.Id); RuleStatus.Remove(rule.Id); Revision++; }
    internal bool IsRecording => _recordingStart is not null;
    internal string RecordingProfile { get; private set; } = "Ressourcen und Timeline";
    internal DiagnosisRecording? Completed { get; private set; }
    internal string Notice { get; private set; } = "";
    internal bool SnapshotRequested { get; set; }
    internal event Action<string>? NotificationRequested;
    internal int Revision { get; private set; }
    internal void Restore(IntelligenceSession session, bool mergeTimeline = false)
    {
        session.Validate();
        var existing = mergeTimeline ? _timeline.ToArray() : [];
        _timeline.Clear(); _ids.Clear();
        foreach (var entry in session.Timeline) Add(entry);
        foreach (var entry in existing) Add(entry);
        Rules.Clear(); Rules.AddRange(session.Rules);
        _boots.Clear(); _boots.AddRange(session.Boots);
        _lastTriggered.Clear(); foreach (var p in session.LastTriggered) _lastTriggered.Add(p.Key, p.Value);
        _watch.Clear(); _processes = null; _lastResource = null; _idleSamples = 0; _bootStarted = false;
        _resources.Clear(); RuleStatus.Clear(); Completed = null; SnapshotRequested = false;
    }
    internal IntelligenceSession Freeze() => new(1, _timeline.ToArray(), Rules.ToArray(), _boots.ToArray(),
        _lastTriggered.Where(p => Rules.Any(r => r.Id == p.Key)).ToDictionary());
    internal void Add(TimelineEntry entry)
    {
        if (!_ids.Add(entry.Id))
        {
            if (entry.Event?.Subject is { Length: > 0 })
            {
                var index = _timeline.FindIndex(e => e.Id == entry.Id);
                if (index >= 0 && _timeline[index].Event?.Subject is null)
                { _timeline[index] = entry with { Observed = _timeline[index].Observed }; Revision++; }
            }
            return;
        }
        _timeline.Add(entry); Revision++;
        if (IsRecording && _scope.HasFlag(RecordingScope.Events))
        {
            var size = _markerBytes < 512 * 1024 && _markers.Count < IntelligenceModel.MaximumTimeline ?
                JsonSerializer.SerializeToUtf8Bytes(entry).Length : 512 * 1024 + 1;
            if (_markerBytes + size <= 512 * 1024) { _markers.Add(entry); _markerBytes += size; }
            else
            {
                const string issue = "Markerbudget erreicht; weitere Timelineeinträge nicht in Aufzeichnung übernommen.";
                if (_recordingIssues.Count < 256 && !_recordingIssues.Contains(issue)) _recordingIssues.Add(issue);
            }
        }
        while (_timeline.Count > IntelligenceModel.MaximumTimeline)
        {
            var oldest = _timeline.MinBy(e => e.Occurred)!;
            _timeline.Remove(oldest); _ids.Remove(oldest.Id);
        }
    }
    internal void Trim(DateTimeOffset now, int days)
    {
        if (days is < 1 or > 90) throw new ArgumentOutOfRangeException(nameof(days));
        var removed = _timeline.RemoveAll(e => now - e.Occurred > TimeSpan.FromDays(days));
        if (removed > 0) { _ids.Clear(); foreach (var item in _timeline) _ids.Add(item.Id); Revision++; }
        _boots.RemoveAll(b => now - b.Started > TimeSpan.FromDays(days));
    }
    internal void Mark(string category, string title, string detail, DateTimeOffset now, string? entityId = null) =>
        Add(new(Guid.NewGuid().ToString("N"), now, now, category, title, entityId,
            IntelligenceModel.Metadata("AETHER / beobachteter Zustand", now, type: SourceType.Derived), detail));
    internal void ApplyEvents(EventSnapshot snapshot)
    {
        foreach (var row in snapshot.Events)
        {
            var category = IntelligenceWindowsEvents.Category(row.Provider, row.Id, row.Level);
            var key = $"event:{row.Log}:{row.RecordId}:{row.Timestamp.UtcTicks}";
            var freshCrash = !_ids.Contains(key) && category is "ApplicationCrash" or "ApplicationHang" &&
                snapshot.Timestamp >= row.Timestamp && snapshot.Timestamp - row.Timestamp <= TimeSpan.FromSeconds(15);
            Add(new(key, row.Timestamp, snapshot.Timestamp,
                category, $"{row.Provider} / {row.Id}", key,
                IntelligenceModel.Metadata("Windows Event Log / " + row.Log + " / RecordId " + row.RecordId, row.Timestamp),
                row.Message, new(row.Log, row.RecordId, row.Provider, row.Id)));
            if (freshCrash)
                foreach (var rule in Rules.Where(r => r.Enabled && r.Trigger == WatchTrigger.Crash))
                    if (_watch.Event(rule, snapshot.Timestamp, _lastTriggered.GetValueOrDefault(rule.Id))) Fire(rule, 1, snapshot.Timestamp);
        }
        foreach (var issue in snapshot.Issues) Mark("ProviderError", "Ereignisquelle unvollständig", issue, snapshot.Timestamp);
    }
    internal void Apply(DashboardSnapshot dashboard, LiveSample live, Func<IntelligenceSnapshot> capture)
    {
        var now = dashboard.Timestamp >= live.Timestamp ? dashboard.Timestamp : live.Timestamp;
        var observed = _clock.GetUtcNow();
        var current = dashboard.Processes.Where(p => p.Identity.IsKnown).ToDictionary(p => p.Identity, p => p.Name);
        var unknown = dashboard.Processes.Where(p => !p.Identity.IsKnown).Select(p => p.Id).ToHashSet();
        var appeared = _processes is null ? [] : current.Keys.Except(_processes.Keys).ToArray();
        var missing = _processes is null ? [] : _processes.Keys.Except(current.Keys)
            .Where(id => !unknown.Contains(id.Pid)).ToArray();
        foreach (var id in appeared) Mark("ProcessAppeared", current[id], "Neu beobachtete PID und Erstellungszeit; kurze Prozesse können zwischen Proben fehlen.", now, "process:" + id);
        foreach (var id in missing)
        {
            Mark("ProcessMissing", _processes![id], "Nicht mehr beobachtet; weder bestätigtes Ende noch Absturzbeweis.", now, "process:" + id);
        }
        if (_processes is not null)
            foreach (var old in _processes.Where(p => unknown.Contains(p.Key.Pid))) current.TryAdd(old.Key, old.Value);
        _processes = current;
        var top = dashboard.Processes.Where(p => p.CpuPercent is not null).MaxBy(p => p.CpuPercent);
        var point = new ResourcePoint(now, live.CpuPercent, live.MemoryPercent, top?.CpuPercent, top?.MemoryMb, top?.Identity.ToString());
        var gap = _lastResource is { } previous && (now <= previous || now - previous > TimeSpan.FromSeconds(6));
        var clockBack = _lastResource is { } earlier && now < earlier;
        if (gap) { Mark("Gap", "Messlücke", "Keine durchgängige Ressourcenbeobachtung; Dauerschwellen zurückgesetzt.", now); _idleSamples = 0; }
        if (clockBack) _bootStarted = false;
        _lastResource = now; _resources.Enqueue(point);
        while (_resources.Count > 120) _resources.Dequeue();
        if (!_bootStarted)
        {
            _bootStarted = true;
            _boots.Add(new(Guid.NewGuid().ToString("N"), now, null, 0, IdleCriterion, false));
            if (_boots.Count > 128) _boots.RemoveAt(0);
        }
        if (_boots.Count > 0 && _boots[^1].Idle is null)
        {
            _idleSamples = !gap && live.CpuPercent is >= 0 and < 10 ? _idleSamples + 1 : 0;
            _boots[^1] = _boots[^1] with { ValidSamples = _idleSamples, Idle = _idleSamples >= 10 ? now : null,
                Complete = _idleSamples >= 10 };
            if (_idleSamples >= 10) Mark("Boot", "AETHER-Leerlaufkriterium beobachtet", IdleCriterion + " Beginn ist AETHER-Beobachtungsbeginn, nicht Boot oder Login.", now);
        }
        if (IsRecording)
        {
            if (now < _recordingStart || clockBack) _clockDiscontinuity = true;
            if (gap && _recordingIssues.Count < 256) _recordingIssues.Add("Messlücke bei " + now.ToString("O"));
            var values = new List<RecordedValue>();
            if (_scope.HasFlag(RecordingScope.Network))
                foreach (var adapter in dashboard.Adapters.Take(8))
                {
                    if (adapter.DownloadMbps is { } down) values.Add(new(adapter.Id + "/download", down, "Mbit/s", "Gemeinsame Adapterprobe", now));
                    if (adapter.UploadMbps is { } up) values.Add(new(adapter.Id + "/upload", up, "Mbit/s", "Gemeinsame Adapterprobe", now));
                }
            if (_scope.HasFlag(RecordingScope.Sensors))
                values.AddRange(live.Sensors.Where(s => s.Value is not null && !SensorSemantics.IsTemperatureLimit(s)).Take(16)
                    .Select(s => new RecordedValue(s.Id, s.Value!.Value, s.Unit, "Gemeinsamer Sensorpfad", live.Timestamp)));
            point = point with
            {
                Cpu = _scope.HasFlag(RecordingScope.Resources) ? point.Cpu : null,
                Ram = _scope.HasFlag(RecordingScope.Resources) ? point.Ram : null,
                ProcessCpu = _scope.HasFlag(RecordingScope.Processes) ? point.ProcessCpu : null,
                ProcessMemory = _scope.HasFlag(RecordingScope.Processes) ? point.ProcessMemory : null,
                Process = _scope.HasFlag(RecordingScope.Processes) ? point.Process : null,
                Processes = _scope.HasFlag(RecordingScope.Processes) ? dashboard.Processes.Where(p => p.Identity.IsKnown)
                    .OrderByDescending(p => p.CpuPercent).Take(8).Concat(dashboard.Processes.Where(p => p.Identity.IsKnown).OrderByDescending(p => p.MemoryMb).Take(8))
                    .DistinctBy(p => p.Identity).Select(p => new RecordedProcess(p.Identity.ToString(), p.Name, p.CpuPercent, p.MemoryMb, p.ReadMbps, p.WriteMbps)).ToArray() : null,
                Values = values.Count == 0 ? null : values
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(point).Length;
            var fits = _recordingBytes + bytes <= 1024 * 1024;
            if (fits) { _recording.Add(point); _recordingBytes += bytes; }
            else if (_recordingIssues.Count < 256) _recordingIssues.Add("Probenbudget 1 MiB erreicht; Aufzeichnung kontrolliert beendet.");
            if (_recording.Count >= 1800 || !fits || _clock.GetElapsedTime(_recordingStamp) >= _duration) Stop(capture(), now);
        }
        foreach (var rule in Rules.Where(r => r.Enabled).Take(32))
        {
            var selected = dashboard.Processes.FirstOrDefault(p => p.Identity.ToString() == rule.Target);
            double? value = rule.Trigger switch
            {
                WatchTrigger.Cpu => live.CpuPercent, WatchTrigger.Ram => live.MemoryPercent,
                WatchTrigger.ProcessCpu => selected?.CpuPercent, WatchTrigger.ProcessMemory => selected?.MemoryMb,
                WatchTrigger.ProcessAppeared => appeared.Count(id => rule.Target.Length == 0 || id.ToString() == rule.Target),
                WatchTrigger.ProcessMissing => missing.Count(id => rule.Target.Length == 0 || id.ToString() == rule.Target),
                WatchTrigger.Sensor => live.Sensors.FirstOrDefault(s => s.Id == rule.Target && !SensorSemantics.IsTemperatureLimit(s))?.Value,
                WatchTrigger.Gpu => live.Sensors.FirstOrDefault(s => s.Id == rule.Target && s.Id.StartsWith("/gpu-", StringComparison.OrdinalIgnoreCase) && s.Type == "Load")?.Value,
                WatchTrigger.DiskFree => live.Volumes.Where(v => rule.Target.Length == 0 || v.Name == rule.Target).Select(v => v.FreePercent).DefaultIfEmpty().Min(),
                _ => null
            };
            var last = _lastTriggered.TryGetValue(rule.Id, out var fired) ? fired : (DateTimeOffset?)null;
            var sourceTime = rule.Trigger is WatchTrigger.ProcessCpu or WatchTrigger.ProcessMemory or WatchTrigger.ProcessAppeared or WatchTrigger.ProcessMissing ?
                dashboard.Timestamp : live.Timestamp;
            RuleStatus[rule.Id] = rule.Trigger is WatchTrigger.NewListener or WatchTrigger.NewStartup or WatchTrigger.NewService or
                WatchTrigger.HardwareChanged or WatchTrigger.AdapterDisconnected ? "Benötigt vollständige Folgeinventur; manuell oder optionale Beobachtung." :
                rule.Trigger == WatchTrigger.Crash ? "Auswertung frischer Windows-Absturz-/Hang-Ereignisse; historische Einträge lösen nicht aus." :
                value is null || !double.IsFinite(value.Value) || sourceTime > observed || observed - sourceTime > TimeSpan.FromSeconds(10) ?
                "Keine frische verfügbare Messung; Dauerschwelle nicht erfüllt." :
                $"Quelle {rule.Trigger}, Wert {value}, Zeitpunkt {sourceTime:O}; Lücken setzen die Dauer zurück.";
            var discrete = rule.Trigger is WatchTrigger.ProcessAppeared or WatchTrigger.ProcessMissing;
            if (discrete ? sourceTime <= observed && observed - sourceTime <= TimeSpan.FromSeconds(10) &&
                value >= rule.Threshold && _watch.Event(rule, observed, last) :
                _watch.Evaluate(rule, value, sourceTime, observed, last))
                Fire(rule, value, observed);
        }
    }
    internal void ObserveChanges(IntelligenceSnapshot before, IntelligenceSnapshot after)
    {
        var comparison = IntelligenceModel.Compare(before, after);
        foreach (var change in comparison.Where(c => c.Domain is not ("Process" or "Resource" or "Sensor" or "GPU" or "Adapter") &&
            c.Kind is ChangeKind.New or ChangeKind.Removed or ChangeKind.Changed))
            Mark("Change/" + change.Domain, (change.After ?? change.Before)!.Name + " / " + change.Kind, change.Explanation, after.Timestamp, change.Id);
        foreach (var rule in Rules.Where(r => r.Enabled && r.Trigger is WatchTrigger.NewListener or WatchTrigger.NewStartup or WatchTrigger.NewService or WatchTrigger.HardwareChanged or WatchTrigger.AdapterDisconnected))
        {
            var count = comparison.Count(c => rule.Trigger switch
            {
                WatchTrigger.NewListener => c.Kind == ChangeKind.New && c.After?.Kind == IntelligenceKind.Endpoint && c.After.Fields.GetValueOrDefault("state") == "Listen",
                WatchTrigger.NewStartup => c.Kind == ChangeKind.New && c.After?.Kind == IntelligenceKind.Startup,
                WatchTrigger.NewService => c.Kind == ChangeKind.New && c.After?.Kind == IntelligenceKind.Service,
                WatchTrigger.HardwareChanged => c.Domain is "Hardware" or "PnP" && c.Kind is ChangeKind.New or ChangeKind.Removed or ChangeKind.Changed,
                WatchTrigger.AdapterDisconnected => c.Kind == ChangeKind.Changed && c.After?.Kind == IntelligenceKind.Adapter && c.After.Fields.GetValueOrDefault("status") == "Disconnected",
                _ => false
            });
            RuleStatus[rule.Id] = $"{after.Timestamp:O}: {count} passende belegte Inventaränderungen; unvollständige Quellen lösen keine Neuanlage aus.";
            if (count >= rule.Threshold && count > 0 && _watch.Event(rule, after.Timestamp, _lastTriggered.GetValueOrDefault(rule.Id))) Fire(rule, count, after.Timestamp);
        }
    }
    private void Fire(IntelligenceRule rule, double? value, DateTimeOffset now)
    {
        _lastTriggered[rule.Id] = now;
        var source = rule.Trigger switch
        {
            WatchTrigger.Cpu or WatchTrigger.Ram => "Gemeinsame NativeMetrics",
            WatchTrigger.Sensor or WatchTrigger.Gpu => "Vorhandener Sensorpfad / exakte ID " + rule.Target,
            WatchTrigger.ProcessCpu or WatchTrigger.ProcessMemory or WatchTrigger.ProcessAppeared or WatchTrigger.ProcessMissing => "Gemeinsame DashboardMetrics / PID+Startzeit",
            WatchTrigger.DiskFree => "NativeMetrics / DriveInfo.AvailableFreeSpace",
            WatchTrigger.Crash => "Windows Application / bekannte Provider- und Event-ID-Signatur",
            _ => "Vollständiger quellenbezogener Inventarvergleich"
        };
        Notice = $"Watch: {rule.Name}; {rule.Trigger} = {value}; Schwelle {rule.Threshold}, Dauer {rule.DurationSeconds}s; {now:O}. Quelle: {source}. Keine Ursachenbehauptung.";
        Mark("Watch", rule.Name, Notice + " Aktion: " + rule.Action, now, "watch:" + rule.Id);
        if (rule.Action == WatchAction.Notification) NotificationRequested?.Invoke(Notice);
        if (rule.Action == WatchAction.Snapshot) SnapshotRequested = true;
        if (rule.Action == WatchAction.DiagnosisMarker) Mark("DiagnosisMarker", rule.Name, Notice, now);
    }
    internal void Start(IntelligenceSnapshot before, int seconds, string profile, RecordingScope? custom = null)
    {
        if (IsRecording) throw new InvalidOperationException("Eine Aufzeichnung läuft bereits.");
        if (seconds is < 2 or > 3600 || profile is not ("Ressourcen" or "Ressourcen und Timeline" or "Benutzerdefiniert")) throw new ArgumentException("Ungültiges Aufzeichnungsprofil.");
        _scope = profile == "Ressourcen" ? RecordingScope.Resources : profile == "Ressourcen und Timeline" ?
            RecordingScope.Resources | RecordingScope.Processes | RecordingScope.Network | RecordingScope.Events : custom ?? RecordingScope.Resources;
        if ((_scope & ~(RecordingScope)63) != 0 || _scope == 0) throw new ArgumentException("Ungültiger Aufzeichnungsumfang.");
        _before = RecordingSnapshot(before, _scope);
        if (JsonSerializer.SerializeToUtf8Bytes(_before).Length > 512 * 1024)
            throw new InvalidOperationException("Systemkontext über 512 KiB; Profil ohne vollständigen Systemkontext wählen.");
        _duration = TimeSpan.FromSeconds(seconds); _recordingStart = _clock.GetUtcNow(); _recordingStamp = _clock.GetTimestamp();
        RecordingProfile = profile; Completed = null; _recording.Clear(); _markers.Clear(); _recordingIssues.Clear();
        _recordingBytes = 0; _markerBytes = 0; _clockDiscontinuity = false;
        if (_scope.HasFlag(RecordingScope.Processes)) _recordingIssues.Add("Pro Probe höchstens 8 CPU- und 8 RAM-Spitzenprozesse; keine vollständige Prozesschronik.");
        if (_scope.HasFlag(RecordingScope.Network)) _recordingIssues.Add("Pro Probe höchstens 8 Adapter; Adapterraten sind keine Prozessraten.");
        if (_scope.HasFlag(RecordingScope.Sensors)) _recordingIssues.Add("Pro Probe höchstens 16 vorhandene Sensoren; keine Temperaturgrenzen als Messung.");
        Mark("Recording", "Diagnoseaufzeichnung gestartet", profile + "; vorhandener Sampler, kein ETW und kein Paketmitschnitt.", _recordingStart.Value);
    }
    internal DiagnosisRecording Stop(IntelligenceSnapshot after, DateTimeOffset now, bool cancel = false)
    {
        if (_recordingStart is not { } start || _before is null) throw new InvalidOperationException("Keine laufende Aufzeichnung.");
        _clockDiscontinuity |= now < start;
        if (_clockDiscontinuity && _recordingIssues.Count < 256) _recordingIssues.Add("ClockDiscontinuity: Systemuhr oder Quellenzeit rückläufig; Dauer ist separat monoton gemessen.");
        var ending = RecordingSnapshot(after, _scope);
        if (JsonSerializer.SerializeToUtf8Bytes(ending).Length > 512 * 1024)
        {
            const string issue = "Abschlusskontext überschreitet 512 KiB; keine vollständige Abschlussinventur gespeichert.";
            if (_recordingIssues.Count < 256) _recordingIssues.Add(issue); else _recordingIssues[^1] = "Hinweislimit erreicht. " + issue;
            ending = ending with { Entities = [], Edges = [], Coverage = ending.Coverage.Select(c => c with { Complete = false }).ToArray() };
        }
        var completed = new DiagnosisRecording(1, Guid.NewGuid(), "Diagnose " + start.ToString("g"), start, now, RecordingProfile, _recording.ToArray(),
            _markers.ToArray(), _recordingIssues.ToArray(), _before, ending, cancel, _scope,
            _clock.GetElapsedTime(_recordingStamp).TotalMilliseconds, _clockDiscontinuity);
        completed.Validate();
        _recordingStart = null; Completed = completed;
        _recording.Clear(); _markers.Clear(); _recordingIssues.Clear(); _before = null;
        Mark("Recording", cancel ? "Aufzeichnung abgebrochen" : "Aufzeichnung beendet", $"{Completed.Samples.Count} Proben; unvollständige Quellen bleiben sichtbar.", now);
        return Completed;
    }
    private static IntelligenceSnapshot RecordingSnapshot(IntelligenceSnapshot snapshot, RecordingScope scope)
    {
        if (scope.HasFlag(RecordingScope.SystemContext)) return snapshot;
        var processes = snapshot.Entities.Where(e => e.Kind == IntelligenceKind.Process)
            .OrderByDescending(e => IntelligenceModel.Number(e, "cpu")).Take(16).Select(e => e.Id).ToHashSet();
        var entities = snapshot.Entities.Where(e => scope.HasFlag(RecordingScope.Resources) && e.Kind == IntelligenceKind.Resource ||
            scope.HasFlag(RecordingScope.Network) && e.Kind == IntelligenceKind.Adapter ||
            scope.HasFlag(RecordingScope.Processes) && processes.Contains(e.Id) ||
            scope.HasFlag(RecordingScope.Sensors) && e.Kind == IntelligenceKind.Sensor).ToArray();
        return snapshot with { Entities = entities, Edges = [],
            Coverage = snapshot.Coverage.Where(c => entities.Any(e => IntelligenceModel.Domain(e) == c.Domain))
                .Select(c => c with { Complete = false }).ToArray() };
    }
    internal string Diagnose(IntelligenceSnapshot snapshot)
    {
        var lines = new List<string> { "WARUM IST MEIN PC GERADE LANGSAM?", $"Probe: {snapshot.Timestamp:O}",
            "Beobachtete Belastung, keine automatisch bewiesene Ursache. Fehlende oder alte Werte sind keine Entwarnung." };
        foreach (var field in new[] { "cpu", "ramMiB", "readMiBs", "writeMiBs" })
            foreach (var entity in snapshot.Entities.Where(e => e.Kind == IntelligenceKind.Process && e.Metadata.Availability == DataAvailability.Available)
                .Where(e => IntelligenceModel.Number(e, field) is not null).OrderByDescending(e => IntelligenceModel.Number(e, field)).Take(5))
                lines.Add($"{field}: {entity.Name} / {entity.Id} = {entity.Fields[field]} / {entity.Metadata.Timestamp:O} / {entity.Metadata.Source}");
        lines.Add("Prozess-CPU ist auf alle logischen Prozessoren normiert; Prozess-I/O ist kein physischer Datenträgerwert.");
        foreach (var row in snapshot.Entities.Where(e => e.Kind is IntelligenceKind.Resource or IntelligenceKind.Adapter or IntelligenceKind.Sensor))
            lines.Add($"{row.Name}: {JsonSerializer.Serialize(row.Fields)} / {row.Metadata.Availability} / {row.Metadata.Source} / {row.Metadata.Timestamp:O}");
        lines.Add($"Kurzverlauf: {_resources.Count} gemeinsame Proben; zeitliche Ereignisnachbarn: " +
            _timeline.Count(e => Math.Abs((e.Occurred - snapshot.Timestamp).TotalMinutes) <= 5));
        lines.Add("GPU-Enginebesitz wird im Prozessinspektor auf Anforderung gemessen; globale Sensoren sind keinem Prozess kausal zugeordnet.");
        return string.Join("\n", lines);
    }
}
