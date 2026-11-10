using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AetherOperations;
using Microsoft.Win32;
using SystemCockpit.Services;
using Path = System.IO.Path;

namespace SystemCockpit.Controls;

public partial class SystemIntelligenceView : UserControl
{
    internal sealed record Row(string Group, string Name, DateTimeOffset Time, string Detail, object Value)
    {
        private string? Field(string key) => (Value as IntelligenceEntity)?.Fields.GetValueOrDefault(key);
        public string? Protocol => Field("protocol");
        public string? LocalAddress => Field("localAddress");
        public int? LocalPort => int.TryParse(Field("localPort"), out var value) ? value : null;
        public string? RemoteAddress => Field("remoteAddress");
        public int? RemotePort => int.TryParse(Field("remotePort"), out var value) ? value : null;
        public string? State => Field("state") ?? Field("status");
    }
    private static readonly Guid SessionId = new("dd08b2c2-9481-43c5-a911-c12088f4b740");
    private static readonly Guid PreferencesId = new("dfdd88b3-0584-41f0-b9e0-c3aba18b916a");
    private readonly IntelligenceSessionEngine _engine = new();
    private readonly IntelligenceStore _store;
    private readonly List<IntelligenceSnapshot> _snapshots = [];
    private readonly Dictionary<Guid, string> _snapshotTypes = [];
    private readonly List<DiagnosisRecording> _recordings = [];
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _work;
    private Task _pending = Task.CompletedTask;
    private DashboardSnapshot? _dashboard;
    private LiveSample? _live;
    private IntelligenceSnapshot? _inventory;
    private bool _shutdown, _loaded, _loading, _persist, _observe;
    private DateTimeOffset _lastSaved;
    private DateTimeOffset _lastFlushAttempt;
    private DateTimeOffset _lastTrim;
    private IntelligenceSnapshot? _watchSnapshot;
    private int _savedRevision = -1;
    private Guid? _savedRecording;
    private string? _mapFocus, _pathStart;
    private string? _filterArea;
    private bool _restoringSelection, _settingFilters;
    private IReadOnlyList<IntelligenceChange> _comparison = [];
    private Point? _pan;
    private double _panX, _panY;
    internal bool Synthetic { get; set; }
    internal Func<HardwareInventory?>? HardwareSnapshot { get; set; }
    internal event Action<IntelligenceEntity>? NavigationRequested;
    internal event Action<string>? NotificationRequested;
    internal IntelligenceSessionEngine Engine => _engine;
    internal DataGrid ResultGrid => Rows;
    internal Task Pending => _pending;
    internal IntelligenceSnapshot? Inventory => _inventory;
    public SystemIntelligenceView() : this(IntelligenceStore.DefaultDirectory) { }
    internal SystemIntelligenceView(string storageDirectory)
    {
        _store = new(storageDirectory);
        InitializeComponent();
        Trigger.ItemsSource = Enum.GetValues<WatchTrigger>(); Trigger.SelectedIndex = 0;
        RuleAction.ItemsSource = Enum.GetValues<WatchAction>(); RuleAction.SelectedIndex = 0;
        _engine.NotificationRequested += message => NotificationRequested?.Invoke(message);
        KindFilter.ItemsSource = new[] { "Alle Klassen" }.Concat(Enum.GetNames<IntelligenceKind>()); KindFilter.SelectedIndex = 0;
        Loaded += (_, _) => { EnsureInitialized(); Render(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) Render(); };
        Unloaded += (_, _) => { if (!_shutdown) _work?.Cancel(); };
    }
    private string CurrentArea => (Area.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Übersicht";
    internal void EnsureInitialized() { if (!_loaded && !_loading) Run(LoadAsync); }
    internal Task ObserveAsync()
    {
        if (!_loaded || !_observe || _shutdown || Synthetic) return Task.CompletedTask;
        if (!_pending.IsCompleted)
        {
            _engine.Mark("Gap", "Inventur ausgelassen", "Gemeinsamer Arbeitsslot belegt; keine überlappende Hintergrundabfrage.", DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }
        Run(CaptureAsync);
        return _pending;
    }
    internal IntelligenceSnapshot Current()
    {
        var current = IntelligenceSources.Live(_dashboard, _live, HardwareSnapshot?.Invoke(), SnapshotName.Text.Trim(), Synthetic);
        if (_inventory is null) return current;
        var domains = current.Coverage.Select(c => c.Domain).ToHashSet();
        var extra = _inventory.Entities.Where(e => !domains.Contains(IntelligenceModel.Domain(e))).Select(e =>
        {
            var age = current.Timestamp - e.Metadata.Timestamp;
            var stale = (e.Kind == IntelligenceKind.Endpoint || IntelligenceModel.Domain(e) == "GPU") && age > TimeSpan.FromSeconds(10) ||
                e.Kind is IntelligenceKind.Service or IntelligenceKind.Adapter && age > TimeSpan.FromSeconds(60);
            return stale ? e with { Metadata = e.Metadata.AsStale("Dynamischer Inventarwert nicht erneut abgefragt.") } : e;
        }).ToArray();
        var entities = current.Entities.Concat(extra).DistinctBy(e => e.Id).ToArray();
        var ids = entities.Select(e => e.Id).ToHashSet();
        var staleIds = entities.Where(e => e.Metadata.Availability == DataAvailability.Stale).Select(e => e.Id).ToHashSet();
        var staleDomains = extra.Where(e => e.Metadata.Availability == DataAvailability.Stale).Select(IntelligenceModel.Domain).ToHashSet();
        return current with
        {
            HostScope = Synthetic ? _inventory.HostScope : current.HostScope,
            Entities = entities,
            Edges = current.Edges.Concat(_inventory.Edges).Where(e => ids.Contains(e.From) && ids.Contains(e.To))
                .DistinctBy(e => (e.From, e.To, e.Relation)).Select(e => staleIds.Contains(e.From) || staleIds.Contains(e.To)
                    ? e with { Metadata = e.Metadata.AsStale("Mindestens ein dynamischer Endpunkt ist veraltet.") } : e).ToArray(),
            Coverage = current.Coverage.Concat(_inventory.Coverage.Where(c => !domains.Contains(c.Domain)).Select(c =>
                staleDomains.Contains(c.Domain) ? c with { Complete = false, Metadata = c.Metadata.AsStale() } : c)).ToArray()
        };
    }
    internal void Apply(DashboardSnapshot dashboard, LiveSample live)
    {
        if (_shutdown) return;
        _dashboard = dashboard; _live = live;
        _engine.Apply(dashboard, live, Current);
        if (dashboard.Timestamp - _lastTrim >= TimeSpan.FromSeconds(30))
        { _engine.Trim(dashboard.Timestamp, _store.Retention.Days); _lastTrim = dashboard.Timestamp; }
        FreezeWatchSnapshot();
        if (IsVisible && CurrentArea is "Übersicht" or "Diagnose" or "Timeline" or "Recorder" or "Watch") Render();
        if (_pending.IsCompleted && dashboard.Timestamp - _lastFlushAttempt >= TimeSpan.FromSeconds(30) &&
            (_watchSnapshot is not null || _engine.Completed is { } recording && _savedRecording != recording.Id ||
            _persist && _engine.Revision != _savedRevision && dashboard.Timestamp - _lastSaved >= TimeSpan.FromSeconds(30)))
            Run(FlushAsync);
    }
    internal void ApplyEvents(EventSnapshot snapshot)
    {
        if (_shutdown) return;
        _engine.ApplyEvents(snapshot);
        if (IsVisible && CurrentArea is "Timeline" or "Reliability") Render();
    }
    private async Task LoadAsync(CancellationToken token)
    {
        _loading = true;
        try
        {
            if (!Synthetic)
            {
                var preferences = await _store.LoadAsync<IntelligencePreferences>("preferences", token);
                if (preferences.LastOrDefault() is { } settings)
                {
                    if (settings.Version != 1 || settings.Retention is null) throw new JsonException("Unbekannte Intelligence-Einstellungen.");
                    settings.Retention.Validate(); _store.Retention = settings.Retention; _persist = settings.Persist;
                    _observe = settings.ObserveInventories; ObserveInventories.IsChecked = _observe;
                    Persist.IsChecked = _persist;
                    RetentionDays.Text = settings.Retention.Days.ToString(CultureInfo.InvariantCulture);
                    RetentionFiles.Text = settings.Retention.Files.ToString(CultureInfo.InvariantCulture);
                    RetentionMb.Text = (settings.Retention.Bytes / 1048576).ToString(CultureInfo.InvariantCulture);
                }
                if ((await _store.LoadAsync<IntelligenceSession>("session", token)).LastOrDefault() is { } session) _engine.Restore(session, true);
                foreach (var type in new[] { "snapshot", "baseline" })
                    foreach (var snapshot in await _store.LoadAsync<IntelligenceSnapshot>(type, token))
                    { _snapshots.Add(snapshot); _snapshotTypes[snapshot.Id] = type; }
                foreach (var recording in await _store.LoadAsync<DiagnosisRecording>("recording", token))
                { recording.Validate(); _recordings.Add(recording); }
            }
            _engine.Trim(DateTimeOffset.UtcNow, _store.Retention.Days);
            _loaded = true; RefreshSnapshots(); Render();
            if (_store.Issues.Count > 0) Status.Text = "Beschädigte Bestände isoliert: " + string.Join("; ", _store.Issues);
        }
        finally { _loading = false; }
    }
    private void Run(Func<CancellationToken, Task> action)
    {
        if (_shutdown) return;
        if (!_pending.IsCompleted) { Status.Text = "Eine lesende oder lokale Speicheroperation läuft. Abbrechen ist möglich."; return; }
        _work?.Dispose(); _work = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _pending = RunCoreAsync(action, _work.Token);
    }
    private async Task RunCoreAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        try { await action(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Status.Text = "Abgebrochen; keine unvollständige Inventur übernommen."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or
            ArgumentException or Win32Exception or TimeoutException or System.Security.SecurityException)
        {
            Trace.TraceError("System Intelligence: {0}", ex); Status.Text = "System Intelligence: " + ex.Message;
        }
    }
    private async Task CaptureAsync(CancellationToken token)
    {
        if (Synthetic) throw new InvalidOperationException("Synthetische Ansicht liest keine Windows-Inventare.");
        Status.Text = "Nur lesende Inventur läuft; bestehende gemeinsame Liveprobe bleibt aktiv.";
        var before = Current();
        var snapshot = await IntelligenceSources.CaptureAsync(IntelligenceSources.Live(_dashboard, _live, HardwareSnapshot?.Invoke(), SnapshotName.Text.Trim()), token);
        token.ThrowIfCancellationRequested();
        _inventory = snapshot;
        foreach (var entry in snapshot.Entities.Where(e => e.Kind is IntelligenceKind.Event or IntelligenceKind.Boot && e.Fields.ContainsKey("channel")))
        {
            var provider = entry.Fields.GetValueOrDefault("provider") ?? "";
            int.TryParse(entry.Fields.GetValueOrDefault("eventId"), out var eventId);
            int.TryParse(entry.Fields.GetValueOrDefault("level"), out var level);
            _engine.Add(new(entry.Id, entry.Metadata.Timestamp, snapshot.Timestamp,
                IntelligenceWindowsEvents.Category(provider, eventId, level), entry.Name,
                entry.Id, entry.Metadata, JsonSerializer.Serialize(entry.Fields),
                long.TryParse(entry.Fields.GetValueOrDefault("recordId"), out var recordId)
                    ? new(entry.Fields.GetValueOrDefault("channel") ?? "", recordId, provider, eventId, entry.Fields.GetValueOrDefault("eventSubject")) : null));
        }
        if (before.Coverage.Count > 0) _engine.ObserveChanges(before, snapshot);
        FreezeWatchSnapshot();
        _engine.Mark("Snapshot", "Systeminventar gelesen", $"{snapshot.Entities.Count} Entitäten; Quellenabdeckung im Inspektor.", snapshot.Timestamp);
        Render();
        Status.Text = $"Inventur: {snapshot.Entities.Count} Entitäten / {snapshot.Edges.Count} belegte Beziehungen; " +
            $"{snapshot.Coverage.Count(c => !c.Complete)} unvollständige Domänen. Nicht atomar.";
    }
    private async Task SaveSnapshotAsync(string type, CancellationToken token, IntelligenceSnapshot? captured = null)
    {
        var snapshot = (captured ?? Current()) with { Id = Guid.NewGuid(), Name = captured?.Name ?? SnapshotName.Text.Trim() };
        if (snapshot.Name.Length is < 1 or > 160) throw new ArgumentException("Snapshotname: 1–160 Zeichen.");
        IntelligenceModel.Validate(snapshot);
        if (!Synthetic) await _store.SaveAsync(type, snapshot.Id, snapshot, token);
        _snapshots.Add(snapshot); _snapshotTypes[snapshot.Id] = type;
        if (_snapshots.Count > 128) _snapshots.RemoveAt(0);
        RefreshSnapshots();
        _engine.Mark("Snapshot", type == "baseline" ? "Baseline gespeichert" : "Snapshot gespeichert", snapshot.Name, snapshot.Timestamp, "snapshot:" + snapshot.Id);
        Status.Text = "Lokal gespeichert (sensibel, unverschlüsselt): " + snapshot.Name;
        Render();
    }
    private void RefreshSnapshots()
    {
        var before = Before.SelectedItem as IntelligenceSnapshot; var after = After.SelectedItem as IntelligenceSnapshot;
        var choices = _snapshots.Concat(_recordings.SelectMany(r => new[] { r.Before with { Name = r.Name + " / Beginn" },
            r.After with { Name = r.Name + " / Ende" } })).DistinctBy(s => (s.Id, s.Timestamp)).OrderByDescending(s => s.Timestamp).ToArray();
        Before.ItemsSource = choices;
        After.ItemsSource = new IntelligenceSnapshot?[] { null }.Concat(choices).ToArray();
        Before.SelectedItem = before ?? _snapshots.LastOrDefault(); After.SelectedItem = after;
    }
    private async Task FlushAsync(CancellationToken token)
    {
        _lastFlushAttempt = DateTimeOffset.UtcNow;
        if (_watchSnapshot is { } watchSnapshot)
        { await SaveSnapshotAsync("snapshot", token, watchSnapshot); _watchSnapshot = null; }
        if (_engine.Completed is { } recording && _savedRecording != recording.Id)
        {
            if (!recording.Cancelled && !Synthetic) await _store.SaveAsync("recording", recording.Id, recording, token);
            if (!recording.Cancelled) { _recordings.Add(recording); if (_recordings.Count > 32) _recordings.RemoveAt(0); RefreshSnapshots(); }
            _savedRecording = recording.Id;
        }
        if (_loaded && _persist && !Synthetic)
        {
            var revision = _engine.Revision;
            await _store.SaveAsync("session", SessionId, _engine.Freeze(), token);
            _lastSaved = DateTimeOffset.UtcNow; _savedRevision = revision;
        }
    }
    private void Render()
    {
        if (Rows is null || Search is null || KindFilter is null || Inspector is null || _settingFilters) return;
        var areaChanged = _filterArea != CurrentArea;
        if (areaChanged)
        {
            _filterArea = CurrentArea; _settingFilters = true;
            try
            {
                KindFilter.ItemsSource = new[] { "Alle Klassen" }.Concat(CurrentArea == "Timeline" ?
                    _engine.Timeline.Select(e => e.Category).Distinct().Order(StringComparer.Ordinal) : CurrentArea == "Treiber" ?
                    Current().Entities.Where(e => e.Kind == IntelligenceKind.Driver).Select(IntelligenceModel.DriverGroup).Distinct().Order(StringComparer.Ordinal) :
                    Enum.GetNames<IntelligenceKind>());
                KindFilter.SelectedIndex = 0;
            }
            finally { _settingFilters = false; }
        }
        RecordingTools.Visibility = CurrentArea == "Recorder" ? Visibility.Visible : Visibility.Collapsed;
        WatchTools.Visibility = CurrentArea == "Watch" ? Visibility.Visible : Visibility.Collapsed;
        MapTools.Visibility = CurrentArea == "System Map" ? Visibility.Visible : Visibility.Collapsed;
        NetworkTools.Visibility = CurrentArea == "Netzwerk 2.0" ? Visibility.Visible : Visibility.Collapsed;
        var current = Current();
        IEnumerable<Row> rows = CurrentArea switch
        {
            "Timeline" => _engine.Timeline.OrderByDescending(e => e.Occurred).ThenBy(e => e.Id, StringComparer.Ordinal).Select(e => new Row(e.Category, e.Title, e.Occurred, e.Detail, e)),
            "Reliability" => IntelligenceModel.Reliability(_engine.Timeline).Select(e => new Row("Quellensignatur", e.Key, e.Last, e.Summary, e))
                .Concat(current.Entities.Where(e => IntelligenceModel.Domain(e) == "ReliabilityIndex").Select(EntityRow)),
            "Change / Compare" => _comparison.Select(e => new Row(e.Kind.ToString(), (e.After ?? e.Before)?.Name ?? e.Domain,
                (e.After ?? e.Before)?.Metadata.Timestamp ?? current.Timestamp, e.Explanation, e)),
            "Recorder" => _recordings.Select(e => new Row("Recording", e.Name, e.Start, $"{e.Samples.Count} Proben / {e.ElapsedMilliseconds:F0} ms monoton / {e.Issues.Count} Hinweise/Lücken", e)),
            "Watch" => _engine.Rules.Select(e => new Row("Watch", e.Name, current.Timestamp,
                $"{e.Enabled} / {e.Trigger} / {e.Threshold} / {e.DurationSeconds}s / {e.Action} / {_engine.RuleStatus.GetValueOrDefault(e.Id) ?? "Noch nicht ausgewertet"}", e)),
            "Startup" => current.Entities.Where(e => e.Kind is IntelligenceKind.Boot or IntelligenceKind.Startup or IntelligenceKind.Task or IntelligenceKind.Service).Select(EntityRow)
                .Concat(_engine.Boots.Select(b => new Row("AETHER-Beobachtung", b.Id, b.Started, b.Complete ? $"Leerlauf nach {b.Idle - b.Started}" : "Unvollständig; Boot/Login/Desktoperfassung nicht durchgängig.", b))),
            "Software" => current.Entities.Where(e => e.Kind == IntelligenceKind.Software).Select(EntityRow),
            "Treiber" => current.Entities.Where(e => e.Kind == IntelligenceKind.Driver).Select(e => EntityRow(e) with { Group = IntelligenceModel.DriverGroup(e) }),
            "Netzwerk 2.0" => current.Entities.Where(e => e.Kind is IntelligenceKind.Endpoint or IntelligenceKind.Adapter).Select(EntityRow),
            _ => current.Entities.Select(EntityRow)
        };
        if (CurrentArea == "Übersicht")
            rows = rows.Concat(_snapshots.Select(s => new Row("Snapshot", s.Name, s.Timestamp, $"{s.Entities.Count} Entitäten", s)))
                .Concat(_recordings.Select(r => new Row("Recording", r.Name, r.Start, $"{r.Samples.Count} Proben", r)))
                .Concat(_engine.Rules.Select(r => new Row("Watch", r.Name, current.Timestamp, r.Trigger.ToString(), r)))
                .Concat(_engine.Timeline.Select(e => new Row(e.Category, e.Title, e.Occurred, e.Detail, e)));
        var term = Search.Text.Trim();
        var kind = KindFilter.SelectedItem?.ToString();
        var minutes = Period.SelectedIndex switch { 1 => 5, 2 => 60, 3 => 1440, 4 => 10080, _ => double.MaxValue };
        var displayed = rows.Where(r => (kind is null or "Alle Klassen" || r.Group == kind) &&
            (CurrentArea != "Netzwerk 2.0" || OnlyListeners.IsChecked != true || r.Value is IntelligenceEntity endpoint &&
                endpoint.Kind == IntelligenceKind.Endpoint && (endpoint.Fields.GetValueOrDefault("state") == "Listen" || endpoint.Fields.GetValueOrDefault("protocol") == "UDP")) &&
            (DateTimeOffset.UtcNow - r.Time).TotalMinutes <= minutes &&
            (term.Length == 0 || $"{r.Group} {r.Name} {r.Detail} {(r.Value as IntelligenceEntity)?.Id}"
                .Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        foreach (var column in Rows.Columns.Skip(4)) column.Visibility = CurrentArea == "Netzwerk 2.0" ? Visibility.Visible : Visibility.Collapsed;
        var selected = (Rows.SelectedItem as Row)?.Value;
        _restoringSelection = true;
        try { Rows.ItemsSource = displayed; Rows.SelectedItem = displayed.FirstOrDefault(r => selected is not null && ItemId(r.Value) == ItemId(selected)); }
        finally { _restoringSelection = false; }
        if (areaChanged)
        {
            _selectedEntity = null;
            Inspector.Text = "Eine Zeile oder einen Knoten auswählen, um Identität, Quellen und belegte Beziehungen zu prüfen.";
            if (Rows.SelectedItem is Row chosen) Inspect(chosen.Value);
        }
        var map = CurrentArea == "System Map";
        MapScroll.Visibility = map ? Visibility.Visible : Visibility.Collapsed;
        Rows.Visibility = map ? Visibility.Collapsed : Visibility.Visible;
        if (map) RenderMap(current, displayed.Select(r => r.Value).OfType<IntelligenceEntity>().ToArray());
        if (CurrentArea == "Diagnose") Inspector.Text = _engine.Diagnose(current);
        if (CurrentArea == "Startup")
        {
            var boots = IntelligenceWindowsEvents.ComparableBoots(current.Entities);
            var observedBoots = _engine.Boots.Where(b => b.Complete && b.Criterion == IntelligenceSessionEngine.IdleCriterion)
                .OrderBy(b => b.Idle - b.Started).ToArray();
            Inspector.Text = IntelligenceSessionEngine.IdleCriterion + "\nWindows-Bootdaten sind davon getrennt. Login-/Desktopabschnitte ohne durchgängige Quelle bleiben unvollständig.\n" +
                (boots.Count == 0 ? "Keine vergleichbare Windows-Diagnostics-Performance-100-Gruppe mit mindestens zwei verfügbaren Einträgen, OS-/Schemaversion und Startart; Kanalstatus unter Quellenabdeckung." :
                    $"Windows gemeldete BootTime derselben Schema-/OS-/Startartgruppe: schnellste {IntelligenceModel.Number(boots[0], "BootTime")} ms; langsamste {IntelligenceModel.Number(boots[^1], "BootTime")} ms. Andere Bedingungen können abweichen.\n" +
                    string.Join("\n", boots.OrderByDescending(b => b.Metadata.Timestamp).Take(2).Select(b => $"{b.Metadata.Timestamp:O} / {b.Fields.GetValueOrDefault("BootTime")} ms / {b.Id}"))) +
                (observedBoots.Length < 2 ? "\nWeniger als zwei abgeschlossene AETHER-Beobachtungsintervalle desselben Kriteriums: kein Laufvergleich." :
                    $"\nAETHER-Beobachtungsintervalle: kürzeste Spanne {observedBoots[0].Idle - observedBoots[0].Started}; längste {observedBoots[^1].Idle - observedBoots[^1].Started}. " +
                    "Das sind keine Windows-Bootzeiten. Unterschiedliche Ausgangslast und Messlücken können die Spanne beeinflussen.");
        }
        if (!map) Status.Text = $"{displayed.Length} Treffer · {_engine.Timeline.Count} Timelineeinträge · " +
            (_engine.IsRecording ? "AUFZEICHNUNG LÄUFT · " : "") + _engine.Notice;
    }
    private static Row EntityRow(IntelligenceEntity e) => new(e.Kind.ToString(), e.Name, e.Metadata.Timestamp,
        e.Metadata.Availability + " / " + string.Join(" / ", e.Fields.Select(p => p.Key + "=" + p.Value)), e);
    private static string ItemId(object item) => item switch
    {
        IntelligenceEntity e => e.Id, TimelineEntry e => e.Id, IntelligenceSnapshot s => s.Id.ToString(),
        DiagnosisRecording r => r.Id.ToString(), IntelligenceRule r => r.Id.ToString(),
        ReliabilityPattern p => p.Key, IntelligenceChange c => c.Id, BootObservation b => b.Id, _ => item.ToString() ?? ""
    };
    private void Selected(object sender, SelectionChangedEventArgs e)
    {
        if (!_restoringSelection && Rows.SelectedItem is Row row) Inspect(row.Value);
    }
    private void Inspect(object value)
    {
        if (value is IntelligenceEntity entity)
        {
            _selectedEntity = entity;
            Inspector.Text = entity.Name + "\n" + entity.Id + "\n" + JsonSerializer.Serialize(entity.Fields, new JsonSerializerOptions { WriteIndented = true }) +
                "\nQUELLE\n" + JsonSerializer.Serialize(entity.Metadata, new JsonSerializerOptions { WriteIndented = true }) +
                "\nBELEGTE BEZIEHUNGEN\n" + string.Join("\n", Current().Edges.Where(e => e.From == entity.Id || e.To == entity.Id)
                    .Select(e => $"{e.From} ↔ {e.To}: {e.Relation} / {JsonSerializer.Serialize(e.Metadata)}")) +
                "\nZeitliche Nachbarschaft ist kein Ursachenbeweis. Fehlende Kanten bedeuten keine nachgewiesene Unabhängigkeit.";
            if (entity.Kind == IntelligenceKind.Software)
            {
                var current = Current();
                var processes = IntelligenceModel.SoftwareProcesses(current, entity.Id);
                var known = _snapshots.TakeLast(8).Append(current).SelectMany(s => IntelligenceModel.SoftwareProcesses(s, entity.Id)).DistinctBy(p => p.Id).ToArray();
                var identities = known.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
                Inspector.Text += "\nSOFTWAREAKTIVITÄT ÜBER EXPLIZITE EXECUTABLE-/IMAGEKANTEN\n" +
                    (processes.Count == 0 ? "Aktuell keine verifizierte Prozesszuordnung. Das ist kein Nachweis fehlender Ausführung." :
                        string.Join("\n", processes.Select(p => $"{p.Id} / {p.Name} / {JsonSerializer.Serialize(p.Fields)} / {p.Metadata.Timestamp:O}"))) +
                    $"\nBelegte Instanzen in den letzten höchstens 8 Snapshots und jetzt: {known.Length}.\n" +
                    string.Join("\n", _engine.Timeline.Where(e => e.EntityId is not null && identities.Contains(e.EntityId)).OrderByDescending(e => e.Occurred).Take(20)
                        .Select(e => $"{e.Occurred:O} / {e.Category} / {e.Title}")) +
                    "\nCPU-/RAM-Kurzverlauf: nur Proben, in denen eine belegte Instanz CPU-Spitzenprozess war:\n" +
                    string.Join("\n", _engine.Resources.Where(p => p.Process is not null && identities.Contains("process:" + p.Process))
                        .Select(p => $"{p.Timestamp:O} / {p.Process}: {p.ProcessCpu}% / {p.ProcessMemory} MiB")) +
                    "\nKlassische Installationspfade, Namen und Publisher begründen keine Programmzuordnung. GPU, Dienste und Netzwerk nur über vorhandene belegte Kanten der Instanz öffnen.";
            }
        }
        else if (value is ReliabilityPattern pattern)
        {
            _selectedEntity = null;
            var baseline = _snapshots.Where(s => s.Timestamp <= pattern.First).MaxBy(s => s.Timestamp);
            if (baseline is not null) { Before.SelectedItem = baseline; After.SelectedItem = null; }
            Inspector.Text = $"{pattern.Summary}\nErstes beobachtetes Auftreten {pattern.First:O}; letztes {pattern.Last:O}.\n" +
                "Wiederholung derselben Quellensignatur; ein strukturiertes App-/Dienstsubjekt wird nur bei bekanntem Ereignisschema einbezogen. Das ist keine installierte Softwareidentität.\n" +
                "Änderungen seit erstem Auftreten:\n" + string.Join("\n", _engine.Timeline.Where(e => e.Category.StartsWith("Change/", StringComparison.Ordinal) && e.Occurred >= pattern.First)
                    .Select(e => $"{e.Occurred:O} / {e.Title} / {e.Detail}")) +
                (baseline is null ? "\nKein gespeicherter Zustand vor dem ersten Auftreten: Der damalige Bestand lässt sich nicht rekonstruieren." :
                    $"\nVorherzustand {baseline.Name} / {baseline.Timestamp:O} im Vergleich ausgewählt. A/B vergleichen zeigt vollständige Befunde; der Snapshot ist nicht exakt der Fehlerzeitpunkt.") +
                "\nZeitnachbarn sind keine Ursachen; ältere Daten außerhalb der Aufbewahrung fehlen. Kein erfundener Zuverlässigkeitsindex.";
        }
        else if (value is TimelineEntry { Event: { } reference } entry)
        {
            _selectedEntity = IntelligenceSources.Entity(entry.Id, IntelligenceKind.Event, entry.Title, "Event/" + reference.Channel,
                entry.Metadata.Source, entry.Occurred, new Dictionary<string, string>
                {
                    ["channel"] = reference.Channel, ["recordId"] = reference.RecordId.ToString(CultureInfo.InvariantCulture),
                    ["provider"] = reference.Provider, ["eventId"] = reference.EventId.ToString(CultureInfo.InvariantCulture)
                });
            Inspector.Text = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
        }
        else if (value is DiagnosisRecording recording)
        {
            _selectedEntity = null;
            Inspector.Text = $"{recording.Start:O} → {recording.End:O}\n{recording.Profile}; monotone Dauer {recording.ElapsedMilliseconds:F0} ms; Uhrsprung {recording.ClockDiscontinuity}\n" +
                string.Join("\n", recording.Samples.OrderByDescending(p => p.Cpu).Take(10).Select(p => $"CPU-Spitze {p.Timestamp:O}: {p.Cpu}% / Prozess {p.Process} ({p.ProcessCpu}%)")) +
                "\nPROZESSBEITRÄGE (BEOBACHTET, NICHT KAUSAL)\n" +
                string.Join("\n", recording.Samples.SelectMany(p => p.Processes ?? []).GroupBy(p => p.Identity).Select(g =>
                    $"{g.First().Name} / {g.Key}: max. CPU {g.Max(p => p.Cpu)} %, RAM {g.Max(p => p.MemoryMiB)} MiB").Take(32)) +
                "\nTIMELINEMARKER (HÖCHSTENS 20 ANGEZEIGT)\n" +
                string.Join("\n", recording.Markers.OrderBy(e => e.Occurred).Take(20).Select(e => $"{e.Occurred:O} / {e.Title} / {e.Metadata.Source}")) +
                "\nHINWEISE / LÜCKEN / FEHLER\n" + string.Join("\n", recording.Issues) + "\nÄNDERUNGEN\n" +
                string.Join("\n", IntelligenceModel.Compare(recording.Before, recording.After).Where(c => c.Kind != ChangeKind.Unchanged).Select(c => $"{c.Domain} / {c.Kind} / {c.Explanation}"));
        }
        else
        {
            _selectedEntity = null; Inspector.Text = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
            if (value is BootObservation) Inspector.Text += "\n" + IntelligenceSessionEngine.IdleCriterion +
                "\nNur AETHER-Beobachtungsintervalle desselben Kriteriums vergleichen; Boot→Login→Desktop ist bei nachträglichem Appstart unvollständig.";
            if (value is IntelligenceRule rule)
            {
                RuleName.Text = rule.Name; Trigger.SelectedItem = rule.Trigger; Target.Text = rule.Target;
                Threshold.Text = rule.Threshold.ToString(CultureInfo.InvariantCulture); Duration.Text = rule.DurationSeconds.ToString(CultureInfo.InvariantCulture);
                Cooldown.Text = rule.CooldownSeconds.ToString(CultureInfo.InvariantCulture); RuleAction.SelectedItem = rule.Action;
            }
        }
    }
    private IntelligenceEntity? _selectedEntity;
    private void RenderMap(IntelligenceSnapshot snapshot, IReadOnlyList<IntelligenceEntity> entities)
    {
        Map.Children.Clear();
        if (_mapFocus is { } focus)
        {
            var neighbors = snapshot.Edges.Where(e => e.From == focus || e.To == focus).SelectMany(e => new[] { e.From, e.To }).Append(focus).ToHashSet();
            entities = entities.Where(e => neighbors.Contains(e.Id)).ToArray();
        }
        var shown = entities.Take(200).ToArray();
        var positions = shown.Select((e, i) => (e.Id, Point: new Point(20 + i % 6 * 290, 20 + i / 6 * 105))).ToDictionary(p => p.Id, p => p.Point);
        Map.Height = Math.Max(600, Math.Ceiling(shown.Length / 6d) * 105 + 40);
        foreach (var edge in snapshot.Edges.Where(e => positions.ContainsKey(e.From) && positions.ContainsKey(e.To) && e.Metadata.Availability == DataAvailability.Available))
        {
            var from = positions[edge.From]; var to = positions[edge.To];
            var line = new System.Windows.Shapes.Line { X1 = from.X + 120, Y1 = from.Y + 35, X2 = to.X + 120, Y2 = to.Y + 35,
                StrokeThickness = 1, ToolTip = edge.Relation + " / " + edge.Metadata.Source + " / " + edge.Metadata.Timestamp.ToString("O"), IsHitTestVisible = false };
            line.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ModernTextPrimary"); Map.Children.Add(line);
        }
        foreach (var entity in shown)
        {
            var button = new Button { Content = new TextBlock { Text = entity.Kind + "\n" + entity.Name, TextWrapping = TextWrapping.Wrap },
                Width = 250, Height = 75, ToolTip = entity.Id, Tag = entity, Padding = new Thickness(6) };
            button.SetResourceReference(StyleProperty, "NeonButton");
            System.Windows.Automation.AutomationProperties.SetName(button, entity.Kind + ": " + entity.Name);
            button.Click += (_, _) => Inspect(entity);
            Canvas.SetLeft(button, positions[entity.Id].X); Canvas.SetTop(button, positions[entity.Id].Y);
            Map.Children.Add(button);
        }
        Status.Text = $"System Map: {shown.Length} / {entities.Count} Knoten; maximal 200. Nur belegte Kanten. Filter und Fokus begrenzen.";
    }
    internal IReadOnlyList<CockpitTask> SearchTasks()
    {
        var result = Current().Entities.Select(e => new CockpitTask("System Intelligence", $"{e.Kind}: {e.Name}", "SystemIntelligence", "Hauptfenster",
            "LESEND / bestehender Evidenzbestand", e.Metadata.Source + " / " + e.Metadata.Timestamp.ToString("O"), e.Id + " " + JsonSerializer.Serialize(e.Fields),
            QuickAction: "intelligence:" + e.Id)).ToList();
        result.AddRange(_engine.Timeline.OrderByDescending(e => e.Occurred).Take(5000).Select(e => new CockpitTask("System Intelligence", e.Category + ": " + e.Title, "SystemIntelligence", "Hauptfenster",
            "LESEND / vorhandener Timelineeintrag", e.Detail, e.Id, QuickAction: "intelligence-event:" + e.Id)));
        result.AddRange(_snapshots.Select(s => new CockpitTask("System Intelligence", "Snapshot: " + s.Name, "SystemIntelligence", "Hauptfenster",
            "LESEND / lokaler Bestand", s.Timestamp.ToString("O"), s.Id.ToString(), QuickAction: "intelligence-object:snapshot:" + s.Id)));
        result.AddRange(_recordings.Select(r => new CockpitTask("System Intelligence", "Aufzeichnung: " + r.Name, "SystemIntelligence", "Hauptfenster",
            "LESEND / lokaler Bestand", r.Start.ToString("O"), r.Id.ToString(), QuickAction: "intelligence-object:recording:" + r.Id)));
        result.AddRange(_engine.Rules.Select(r => new CockpitTask("System Intelligence", "Watch: " + r.Name, "SystemIntelligence", "Hauptfenster",
            "LESEND / Regel anzeigen", r.Trigger.ToString(), r.Id.ToString(), QuickAction: "intelligence-object:watch:" + r.Id)));
        return result;
    }
    internal void QuickAction(string action)
    {
        if (action.StartsWith("intelligence-object:", StringComparison.Ordinal))
        {
            var value = action[20..].Split(':');
            if (value.Length != 2 || !Guid.TryParse(value[1], out var id)) throw new ArgumentException("Ungültige Objektidentität.");
            SetArea(value[0] switch { "watch" => "Watch", "recording" => "Recorder", _ => "Übersicht" }); Search.Clear();
            Rows.SelectedItem = Rows.Items.Cast<Row>().FirstOrDefault(r => r.Value switch
            { IntelligenceSnapshot s => s.Id == id, DiagnosisRecording recording => recording.Id == id, IntelligenceRule rule => rule.Id == id, _ => false });
        }
        else if (action.StartsWith("intelligence-event:", StringComparison.Ordinal))
        { SetArea("Timeline"); Search.Text = ""; if (_engine.Timeline.FirstOrDefault(e => e.Id == action[19..]) is { } entry) Inspect(entry); }
        else if (action.StartsWith("intelligence:", StringComparison.Ordinal))
        {
            SetArea("Übersicht"); Search.Clear(); Render();
            if (Rows.Items.Cast<Row>().FirstOrDefault(r => r.Value is IntelligenceEntity e && e.Id == action[13..]) is { } row)
            { Rows.SelectedItem = row; Rows.ScrollIntoView(row); Inspect(row.Value); }
            else Status.Text = "Entität nicht mehr im aktuellen Bestand; Identität wird nicht durch einen ähnlichen Treffer ersetzt.";
        }
        else SetArea(action);
    }
    private void SetArea(string name) => Area.SelectedItem = Area.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Content?.ToString() == name) ?? Area.Items[0];
    private void FilterChanged(object sender, RoutedEventArgs e) => Render();
    private void CaptureClicked(object sender, RoutedEventArgs e) => Run(CaptureAsync);
    private bool ConfirmSnapshot() => Synthetic || MessageBox.Show(Window.GetWindow(this),
        "Vollständigen lokalen Zustand mit sensiblen Prozess-, Software-, Pfad- und Netzwerkdaten unverschlüsselt speichern?",
        "Lokaler Snapshot", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void SnapshotClicked(object sender, RoutedEventArgs e) { if (ConfirmSnapshot()) Run(t => SaveSnapshotAsync("snapshot", t)); }
    private void BaselineClicked(object sender, RoutedEventArgs e) { if (ConfirmSnapshot()) Run(t => SaveSnapshotAsync("baseline", t)); }
    private void CompareClicked(object sender, RoutedEventArgs e)
    {
        if (Before.SelectedItem is not IntelligenceSnapshot before) { Status.Text = "Zuerst einen Vorher-Snapshot merken oder laden."; return; }
        _comparison = IntelligenceModel.Compare(before, After.SelectedItem as IntelligenceSnapshot ?? Current());
        SetArea("Change / Compare"); Render();
        Inspector.Text = $"{before.Name} ({before.Timestamp:O}) → {(After.SelectedItem as IntelligenceSnapshot)?.Name ?? "Jetzt"}\n" +
            "Quellenabdeckung: " + string.Join("; ", before.Coverage.Select(c => c.Domain + "=" + c.Complete));
    }
    private void DiagnoseClicked(object sender, RoutedEventArgs e) { SetArea("Diagnose"); Render(); }
    private void CancelClicked(object sender, RoutedEventArgs e) => _work?.Cancel();
    private void StartClicked(object sender, RoutedEventArgs e) => Run(token =>
    {
        if (!int.TryParse(Seconds.Text, out var seconds)) throw new ArgumentException("Dauer muss eine ganze Sekundenzahl sein.");
        var scope = RecordingScope.Resources;
        if (RecordProcesses.IsChecked == true) scope |= RecordingScope.Processes;
        if (RecordNetwork.IsChecked == true) scope |= RecordingScope.Network;
        if (RecordSensors.IsChecked == true) scope |= RecordingScope.Sensors;
        if (RecordContext.IsChecked == true) scope |= RecordingScope.SystemContext;
        if (RecordEvents.IsChecked == true) scope |= RecordingScope.Events;
        _engine.Start(Current(), seconds, (Profile.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "", scope);
        Render(); return Task.CompletedTask;
    });
    private void StopClicked(object sender, RoutedEventArgs e) => CompleteRecording(false);
    private void DiscardClicked(object sender, RoutedEventArgs e) => CompleteRecording(true);
    private void CompleteRecording(bool discard)
    {
        if (_shutdown) return;
        if (!_engine.IsRecording) { Status.Text = "Keine laufende Aufzeichnung."; return; }
        var pending = _pending;
        _work?.Cancel();
        _pending = RunCoreAsync(async token =>
        {
            var recording = _engine.Stop(Current(), DateTimeOffset.UtcNow, discard);
            if (discard) _savedRecording = recording.Id;
            Render();
            await pending;
            token.ThrowIfCancellationRequested();
            await FlushAsync(token);
            Render();
        }, _stop.Token);
    }
    private void RuleClicked(object sender, RoutedEventArgs e) => Run(async token =>
    {
        var existing = (Rows.SelectedItem as Row)?.Value as IntelligenceRule;
        if (!double.TryParse(Threshold.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold) ||
            !int.TryParse(Duration.Text, out var duration) || !int.TryParse(Cooldown.Text, out var cooldown) ||
            Trigger.SelectedItem is not WatchTrigger trigger || RuleAction.SelectedItem is not WatchAction action)
            throw new ArgumentException("Schwelle, Dauer, Cooldown und Aktion prüfen.");
        var rule = new IntelligenceRule(existing?.Id ?? Guid.NewGuid(), RuleName.Text.Trim(), trigger, Target.Text.Trim(), threshold, duration, cooldown, action);
        rule.Validate();
        _engine.UpdateRule(rule);
        _engine.Mark("WatchEdit", rule.Name, "Regel bearbeitet; Dauer beginnt neu.", DateTimeOffset.UtcNow);
        if (_persist) await FlushAsync(token);
        Render();
    });
    private void ToggleRuleClicked(object sender, RoutedEventArgs e) => Run(async token =>
    {
        if ((Rows.SelectedItem as Row)?.Value is not IntelligenceRule rule) throw new InvalidOperationException("Watch-Regel auswählen.");
        _engine.UpdateRule(rule with { Enabled = !rule.Enabled });
        _engine.Mark("WatchEdit", rule.Name, "Aktivierung geändert; keine automatischen Eingriffe.", DateTimeOffset.UtcNow);
        await FlushAsync(token); Render();
    });
    private void PersistenceChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !_loaded) return;
        _persist = Persist.IsChecked == true;
        Run(async token =>
        {
            if (!Synthetic) await _store.SaveAsync("preferences", PreferencesId, new IntelligencePreferences(1, _persist, _store.Retention, _observe), token);
            if (!_persist && !Synthetic) await _store.DeleteAsync("session", SessionId, token);
            await FlushAsync(token);
            Status.Text = _persist ? "Lokale Timeline/Watch-Speicherung aktiviert." : "Sitzung nicht mehr persistent; gespeicherte Snapshots/Aufzeichnungen separat löschen.";
        });
    }
    private void RetentionClicked(object sender, RoutedEventArgs e) => Run(async token =>
    {
        if (!int.TryParse(RetentionDays.Text, out var days) || !int.TryParse(RetentionFiles.Text, out var files) || !int.TryParse(RetentionMb.Text, out var mb))
            throw new ArgumentException("Historiengrenzen als ganze Zahlen eingeben.");
        var retention = new IntelligenceRetention(1, days, files, (long)mb * 1048576); retention.Validate();
        _store.Retention = retention;
        if (!Synthetic) await _store.SaveAsync("preferences", PreferencesId, new IntelligencePreferences(1, _persist, retention, _observe), token);
        Status.Text = "Historiengrenzen gespeichert; gelten gemeinsam für alle Intelligence-Dateitypen.";
    });
    private void ImportClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "AETHER Snapshot|*.json;*.aethersnapshot;*.aetherintelligence" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        Run(async token =>
        {
            if (new FileInfo(dialog.FileName).Length > IntelligenceStore.MaximumFileBytes) throw new JsonException("Import zu groß.");
            var json = await File.ReadAllTextAsync(dialog.FileName, token);
            var imported = IntelligenceStore.ImportSnapshot(json);
            if (_snapshots.Count >= 128) throw new InvalidOperationException("Maximal 128 Snapshot-Einträge; zuerst alte Einträge löschen.");
            _snapshots.Add(imported); RefreshSnapshots(); Status.Text = "Nur zur Ansicht importiert; keine Übernahme in laufendes System.";
        });
    }
    private void ExportClicked(object sender, RoutedEventArgs e)
    {
        var consent = MessageBox.Show(Window.GetWindow(this), "Sensible Identitäten, Software, Pfade, Adressen und Ereignisse vollständig exportieren? Nein redigiert sie.",
            "Intelligence-Bericht", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (consent == MessageBoxResult.Cancel) return;
        var selectedRecording = (Rows.SelectedItem as Row)?.Value as DiagnosisRecording;
        var dialog = new SaveFileDialog { Filter = "JSON-Bericht|*.json|CSV-Bericht|*.csv|Textbericht|*.txt|Vollständiger Snapshot (sensibel)|*.aetherintelligence",
            FileName = selectedRecording is null ? "aether-intelligence" : "aether-diagnose", AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        Run(async token =>
        {
            var format = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            if (format == ".aetherintelligence" && consent != MessageBoxResult.Yes)
                throw new InvalidOperationException("Vollständige Snapshotdateien benötigen die ausdrückliche Freigabe sensibler Daten; für Redaktion ein Berichtsformat wählen.");
            var report = format == ".aetherintelligence" ? JsonSerializer.Serialize(Current(), IntelligenceModel.Json) :
                selectedRecording is { } recording ? IntelligenceModel.ExportRecording(recording, format, consent == MessageBoxResult.Yes) :
                    IntelligenceModel.Export(Current(), _engine.Timeline, format, consent == MessageBoxResult.Yes);
            await File.WriteAllTextAsync(dialog.FileName, report, token); Status.Text = "Bericht gespeichert: " + dialog.FileName;
        });
    }
    private void DeleteClicked(object sender, RoutedEventArgs e) => Run(async token =>
    {
        var value = (Rows.SelectedItem as Row)?.Value;
        if (value is IntelligenceRule rule) _engine.DeleteRule(rule);
        else if (value is IntelligenceSnapshot snapshot)
        {
            if (_snapshotTypes.TryGetValue(snapshot.Id, out var type) && !Synthetic) await _store.DeleteAsync(type, snapshot.Id, token);
            _snapshots.Remove(snapshot); _snapshotTypes.Remove(snapshot.Id); RefreshSnapshots();
        }
        else if (value is DiagnosisRecording recording)
        { if (!Synthetic) await _store.DeleteAsync("recording", recording.Id, token); _recordings.Remove(recording); }
        else throw new InvalidOperationException("Gespeicherten Snapshot, Aufzeichnung oder Regel auswählen.");
        _engine.Mark("Delete", "Lokaler Eintrag gelöscht", "Keine Systemänderung.", DateTimeOffset.UtcNow);
        await FlushAsync(token); Render();
    });
    private void ResetClicked(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this), "Alle lokalen Intelligence-Snapshots, Baselines, Regeln, Timeline und Aufzeichnungen löschen?",
            "Lokale Historie zurücksetzen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Run(async token =>
        {
            if (_engine.IsRecording) _engine.Stop(Current(), DateTimeOffset.UtcNow, true);
            if (!Synthetic) await _store.ResetAsync(token);
            _engine.Restore(new(1, [], [], [], new Dictionary<Guid, DateTimeOffset>()));
            _snapshots.Clear(); _snapshotTypes.Clear(); _recordings.Clear(); _inventory = null; _watchSnapshot = null;
            _persist = false; Persist.IsChecked = false; _observe = false; ObserveInventories.IsChecked = false;
            _savedRecording = _engine.Completed?.Id; RefreshSnapshots(); Render(); Status.Text = "Lokale Intelligence-Historie zurückgesetzt.";
        });
    }
    private void FocusMapClicked(object sender, RoutedEventArgs e) { _mapFocus = _selectedEntity?.Id; Render(); }
    private void ResetMapClicked(object sender, RoutedEventArgs e) { _mapFocus = null; Render(); }
    private void PathStartClicked(object sender, RoutedEventArgs e) { _pathStart = _selectedEntity?.Id; Status.Text = "Pfadanfang: " + (_pathStart ?? "Erst einen Knoten auswählen."); }
    private void PathClicked(object sender, RoutedEventArgs e)
    {
        if (_pathStart is null || _selectedEntity is null) { Status.Text = "Pfadanfang und Ziel auswählen."; return; }
        var current = Current();
        if (!current.Entities.Any(e => e.Id == _pathStart) || !current.Entities.Any(e => e.Id == _selectedEntity.Id))
        { Inspector.Text = "Mindestens ein gewählter Endpunkt ist nicht mehr im aktuellen Bestand."; return; }
        var path = IntelligenceModel.Path(_pathStart, _selectedEntity.Id, current.Edges);
        Inspector.Text = path.Count == 0 ? "Kein belegter Beziehungspfad in diesem Bestand." :
            $"Beziehungspfad, keine Kausalkette ({path.Count} Knoten; höchstens 200 angezeigt):\n" + string.Join("\n↕\n", path.Take(200));
    }
    private void ZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (MapScale is not null) MapScale.ScaleX = MapScale.ScaleY = e.NewValue; }
    private void PanStart(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not Canvas) return;
        _pan = e.GetPosition(MapScroll); _panX = MapScroll.HorizontalOffset; _panY = MapScroll.VerticalOffset; MapScroll.CaptureMouse();
    }
    private void PanMove(object sender, MouseEventArgs e)
    { if (_pan is { } start) { var point = e.GetPosition(MapScroll); MapScroll.ScrollToHorizontalOffset(_panX + start.X - point.X); MapScroll.ScrollToVerticalOffset(_panY + start.Y - point.Y); } }
    private void PanEnd(object sender, MouseButtonEventArgs e) { _pan = null; MapScroll.ReleaseMouseCapture(); }
    private void NavigateClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedEntity is { Kind: IntelligenceKind.Endpoint } endpoint)
        {
            var snapshot = Current();
            var owners = snapshot.Edges.Where(edge => edge.To == endpoint.Id && edge.Metadata.Availability == DataAvailability.Available)
                .Select(edge => edge.From).ToHashSet(StringComparer.Ordinal);
            var processes = snapshot.Entities.Where(entity => entity.Kind == IntelligenceKind.Process && owners.Contains(entity.Id) &&
                entity.Metadata.Availability == DataAvailability.Available).ToArray();
            if (processes.Length == 1) NavigationRequested?.Invoke(processes[0]);
            else Status.Text = "Kein eindeutiger frischer Prozessbezug dieses Endpunkts. Eine Owner-PID allein reicht nicht.";
        }
        else if (_selectedEntity is { } entity) NavigationRequested?.Invoke(entity);
        else Status.Text = "Eine Entität auswählen; Ereignisse enthalten Kanal und RecordId im Quelleninspektor.";
    }
    private void CoverageClicked(object sender, RoutedEventArgs e) =>
        Inspector.Text = JsonSerializer.Serialize(Current().Coverage, new JsonSerializerOptions { WriteIndented = true });
    private void ObservationChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !_loaded) return;
        _observe = ObserveInventories.IsChecked == true;
        Run(async token =>
        {
            if (!Synthetic) await _store.SaveAsync("preferences", PreferencesId, new IntelligencePreferences(1, _persist, _store.Retention, _observe), token);
            Status.Text = _observe ? "Inventaränderungen werden in der vorhandenen Windows-Abfrageschleife beobachtet; Quellenausfälle bleiben unvollständig." :
                "Zusätzliche Inventarbeobachtung aus; manuelle Aufnahme und vorhandene Live-/Ereignisquellen bleiben verfügbar.";
        });
    }
    private void FileDetailsClicked(object sender, RoutedEventArgs e) => Run(async token =>
    {
        var entity = _selectedEntity;
        if (entity?.Kind != IntelligenceKind.File || !entity.Fields.TryGetValue("path", out var path))
            throw new InvalidOperationException("Eine belegte Dateientität wählen, gegebenenfalls über die System Map.");
        if (Synthetic) throw new InvalidOperationException("Synthetische Ansicht liest keine Dateien.");
        var details = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested(); ProcessFileInfo.RequireLocal(path);
            var info = FileVersionInfo.GetVersionInfo(path);
            var trust = ProcessFileInfo.Trust(path);
            token.ThrowIfCancellationRequested();
            return $"{path}\nDateiversion: {info.FileVersion}\nProduktversion: {info.ProductVersion}\nHerausgebertext (nicht verifiziert): {info.CompanyName}\n{trust}\n" +
                "Aktuelle lokale Datei; kein Beweis über früher geladene oder ersetzte Inhalte. Bei Treibern kann Windows zusätzlich Katalogsignaturen verwenden.";
        }, token);
        Inspector.Text = details;
    });
    private void FreezeWatchSnapshot()
    {
        if (!_engine.SnapshotRequested) return;
        _engine.SnapshotRequested = false;
        _watchSnapshot ??= Current() with { Name = "Watch " + DateTimeOffset.UtcNow.ToString("s", CultureInfo.InvariantCulture) };
    }
    private void KeyPressed(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && Rows.SelectedItem is Row row) { Inspect(row.Value); Inspector.Focus(); e.Handled = true; } }
    internal void SetFixture(IntelligenceSnapshot snapshot) { Synthetic = true; _inventory = snapshot; Render(); }
    internal async Task ShutdownAsync()
    {
        if (_shutdown) return;
        _shutdown = true; _stop.Cancel(); _work?.Cancel(); await _pending;
        if (_engine.IsRecording) _engine.Stop(Current(), DateTimeOffset.UtcNow);
        try { await FlushAsync(CancellationToken.None); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Trace.TraceError("Intelligence-Abschluss: {0}", ex); Status.Text = "Intelligence-Abschluss fehlgeschlagen: " + ex.Message; }
        _work?.Dispose(); _work = null; _stop.Dispose();
    }
}
