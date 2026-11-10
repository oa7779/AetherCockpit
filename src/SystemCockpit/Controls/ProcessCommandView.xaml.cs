using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public partial class ProcessCommandView : UserControl
{
    private readonly ProcessHistory _history = new();
    private readonly ObservableCollection<ProcessRow> _rows = [];
    private CancellationTokenSource? _work;
    private Task _pending = Task.CompletedTask;
    private ProcessDetails? _details;
    private ProcessSystemSnapshot? _snapshot, _previous;
    private bool _shutdown;
    private bool _inspectorPinned;
    private ProcessIdentity? _focusedIdentity;
    private string _sortMember = "Current.Id";
    private bool _descending;
    internal bool Synthetic { get; set; }
    internal Task Pending => _pending;
    internal ProcessHistory History => _history;
    internal DataGrid ProcessGrid => Processes;
    internal event Action? RefreshRequested;
    internal Func<HardwareInventory>? HardwareSnapshot { get; set; }
    public ProcessCommandView()
    {
        InitializeComponent();
        Processes.ItemsSource = _rows;
        Processes.ContextMenu = new ContextMenu();
        Unloaded += (_, _) => _work?.Cancel();
    }

    internal void Apply(DashboardSnapshot snapshot)
    {
        if (_shutdown) return;
        _history.Apply(snapshot.Processes, snapshot.Timestamp);
        if (Pause.IsChecked == true) return;
        Render();
    }
    internal void Failed(string message)
    {
        Status.Text = "Erfassung fehlgeschlagen; Anzeige ist veraltet: " + message;
        Inspector.Text = "Keine aktuelle Probe. Alte Werte sind kein Beleg für laufende oder beendete Prozesse.";
    }
    private void Render()
    {
        var selected = (Processes.SelectedItem as ProcessRow)?.Identity;
        var desired = _history.Rows.Values.Where(Matches).ToList();
        var tree = TreeMode.IsChecked == true;
        var byIdentity = _history.Rows;
        if (tree)
        {
            desired = desired.Where(row =>
            {
                var parent = row.Parent;
                var seen = new HashSet<ProcessIdentity>();
                while (parent is { } id && seen.Add(id) && byIdentity.TryGetValue(id, out var item))
                {
                    if (item.Collapsed && Search.Text.Length == 0) return false;
                    parent = item.Parent;
                }
                return true;
            }).ToList();
        }
        foreach (var row in desired)
        {
            row.Depth = 0;
            if (tree)
            {
                var parent = row.Parent;
                var seen = new HashSet<ProcessIdentity>();
                while (parent is { } id && seen.Add(id) && byIdentity.TryGetValue(id, out var item))
                { row.Depth++; parent = item.Parent; }
            }
            row.Notify();
        }
        object? SortValue(ProcessRow row) => _sortMember switch
        {
            "Current.Name" => row.Current.Name,
            "Current.CpuPercent" => row.Current.CpuPercent,
            "Current.MemoryMb" => row.Current.MemoryMb,
            "Current.PrivateMb" => row.Current.PrivateMb,
            "Current.Threads" => row.Current.Threads,
            "Current.Handles" => row.Current.Handles,
            "Current.ReadMbps" => row.Current.ReadMbps,
            "Current.WriteMbps" => row.Current.WriteMbps,
            "Lifecycle" => row.Lifecycle,
            _ => row.Current.Id
        };
        desired = (_descending ? desired.OrderByDescending(SortValue) : desired.OrderBy(SortValue)).ToList();
        if (tree)
        {
            var identities = desired.Select(r => r.Identity).ToHashSet();
            var children = desired.Where(r => r.Parent.HasValue).GroupBy(r => r.Parent!.Value).ToDictionary(g => g.Key, g => g.ToArray());
            var stack = new Stack<ProcessRow>(desired.Where(r => r.Parent is not { } parent || !identities.Contains(parent)).Reverse());
            var ordered = new List<ProcessRow>(desired.Count);
            while (stack.TryPop(out var row))
            {
                ordered.Add(row);
                if (children.TryGetValue(row.Identity, out var items))
                    foreach (var child in items.Reverse()) stack.Push(child);
            }
            desired = ordered;
        }
        var keep = desired.ToHashSet();
        for (var index = _rows.Count - 1; index >= 0; index--) if (!keep.Contains(_rows[index])) _rows.RemoveAt(index);
        for (var index = 0; index < desired.Count; index++)
        {
            if (index < _rows.Count && ReferenceEquals(_rows[index], desired[index])) continue;
            var old = _rows.IndexOf(desired[index]);
            if (old >= 0) _rows.Move(old, index); else _rows.Insert(index, desired[index]);
        }
        if (selected is { } identity) Processes.SelectedItem = _rows.FirstOrDefault(r => r.Identity == identity);
        Status.Text = _rows.Count == 0 ? "Keine Prozesse für diesen Filter. " + _history.Status : _history.Status;
        if (!_inspectorPinned) ShowHistory();
    }
    private bool Matches(ProcessRow row) =>
        (_focusedIdentity is null || row.Identity == _focusedIdentity) &&
        (ActiveOnly?.IsChecked != true || !row.Ended) &&
        (BusyOnly?.IsChecked != true || row.Current.CpuPercent >= 10) &&
        $"{row.Current.Name} {row.Current.Id} {row.Lifecycle}".Contains(Search?.Text ?? "", StringComparison.OrdinalIgnoreCase);
    private void FilterChanged(object sender, RoutedEventArgs e) { _focusedIdentity = null; if (Processes is not null) Render(); }
    private void SortChanged(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        _descending = e.Column.SortDirection == ListSortDirection.Ascending;
        _sortMember = e.Column.SortMemberPath;
        foreach (var column in Processes.Columns) column.SortDirection = null;
        e.Column.SortDirection = _descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        Render();
    }
    private void PauseChanged(object sender, RoutedEventArgs e)
    {
        if (Processes is null) return;
        if (Pause.IsChecked != true) Render(); else Status.Text = "Ansicht pausiert; gemeinsame Erfassung läuft weiter. Anzeige kann veralten.";
    }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _details = null;
        _inspectorPinned = false;
        ShowHistory();
    }
    private void ShowHistory()
    {
        if (Processes.SelectedItem is ProcessRow row)
            Inspector.Text = $"{row.Current.Name} / {row.Identity}\n{row.History}\n\n{row.Rules}\n\n" +
                $"Quelle: gemeinsame Windows-Prozessprobe / {row.Current.ObservedAt:O}\n" +
                $"Elternidentität: {row.Parent?.ToString() ?? "Nicht eindeutig verfügbar"}\n" +
                "Kontextmenü oder Enter für Details. Prozess-I/O ist nicht automatisch physische Datenträgeraktivität.";
    }
    private void RightClicked(object sender, MouseButtonEventArgs e)
    {
        var target = e.OriginalSource as DependencyObject;
        while (target is not null && target is not DataGridRow) target = VisualTreeHelper.GetParent(target);
        if (target is DataGridRow row) { Processes.SelectedItem = row.Item; row.Focus(); }
        else Processes.SelectedItem = null;
    }
    private void GridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ShowSelectedDetails(); e.Handled = true; }
        else if (e.Key is Key.Left or Key.Right && TreeMode.IsChecked == true && Processes.SelectedItem is ProcessRow row)
        { row.Collapsed = e.Key == Key.Left; Render(); e.Handled = true; }
    }
    private void MenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = Processes.ContextMenu;
        menu.Items.Clear();
        if (Processes.SelectedItem is not ProcessRow row) { e.Handled = true; return; }
        BuildMenu(menu, row);
    }
    internal void AttachDashboard(DataGrid dashboard)
    {
        dashboard.ContextMenu = new ContextMenu();
        dashboard.PreviewMouseRightButtonDown += (_, e) =>
        {
            var target = e.OriginalSource as DependencyObject;
            while (target is not null && target is not DataGridRow) target = VisualTreeHelper.GetParent(target);
            dashboard.SelectedItem = (target as DataGridRow)?.Item;
        };
        dashboard.ContextMenuOpening += (_, e) =>
        {
            dashboard.ContextMenu.Items.Clear();
            if (dashboard.SelectedItem is not ProcessMetric metric) { e.Handled = true; return; }
            if (!_history.Rows.TryGetValue(metric.Identity, out var row))
                row = new ProcessRow(metric);
            Select(row.Identity);
            BuildMenu(dashboard.ContextMenu, row);
        };
    }
    private void BuildMenu(ContextMenu menu, ProcessRow row)
    {
        menu.SetResourceReference(BackgroundProperty, "WindowBackdrop");
        menu.SetResourceReference(ForegroundProperty, "ModernTextPrimary");
        menu.SetResourceReference(BorderBrushProperty, "GlassRim");
        menu.Resources[SystemColors.MenuBrushKey] = FindResource("WindowBackdrop");
        menu.Resources[SystemColors.MenuTextBrushKey] = FindResource("ModernTextPrimary");
        void Item(string title, Action run, bool enabled = true)
        {
            var item = new MenuItem { Header = title, IsEnabled = enabled };
            System.Windows.Automation.AutomationProperties.SetName(item, title);
            item.Click += (_, _) => run(); menu.Items.Add(item);
        }
        var known = row.Identity.IsKnown && !row.Ended && row.Current.Id != Environment.ProcessId;
        Item("Prozessdetails anzeigen", ShowSelectedDetails, row.Identity.IsKnown && !row.Ended);
        Item("Dateispeicherort öffnen", () => ShellSelected(false), row.Identity.IsKnown && !row.Ended);
        Item("Dateieigenschaften anzeigen", () => ShellSelected(true), row.Identity.IsKnown && !row.Ended);
        Item("Pfad kopieren", () => Run(async token => { var detail = await ReadSelected(row, token); Clipboard.SetText(detail.Path ?? throw new InvalidOperationException("Pfad nicht verfügbar.")); }));
        Item("PID kopieren", () => Copy(row.Current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Item("Prozessname kopieren", () => Copy(row.Current.Name));
        menu.Items.Add(new Separator());
        Item("Fenster schließen", () => Run(async token =>
        {
            var detail = await ReadSelected(row, token);
            Status.Text = (await Task.Run(() => ProcessInspector.CloseAsync(row.Identity, detail.Window, token), token)).Message;
            RefreshRequested?.Invoke();
        }), known && ProcessInspector.Window(row.Current.Id) != IntPtr.Zero);
        Item("Prozess beenden", () => Terminate(row, false), known);
        Item("Prozessbaum beenden", () => Terminate(row, true), known);
        menu.Items.Add(new Separator());
        Item("Ansicht auf diesen Prozess filtern", () =>
        {
            Search.Clear(); ActiveOnly.IsChecked = false; BusyOnly.IsChecked = false;
            _focusedIdentity = row.Identity; Render();
        });
        Item("Übergeordneten Prozess anzeigen", () => Select(row.Parent), row.Parent.HasValue);
        Item("Untergeordnete Prozesse anzeigen", () =>
        {
            TreeMode.IsChecked = true; row.Collapsed = false; Search.Clear(); Render();
            Inspector.Text = string.Join("\n", _history.Rows.Values.Where(r => r.Parent == row.Identity).Select(r => $"{r.Current.Name} / {r.Identity}"));
        });
        Item("Zugehörige Dienste anzeigen", () => Correlate(row, "Dienst"));
        Item("Zugehörige Netzwerkverbindungen anzeigen", () => Correlate(row, "Netzwerk"));
        Item("Zugehörige Ereignisse anzeigen", () => Correlate(row, "Ereignis"));
        Item("Systemkorrelation anzeigen", () => Correlate(row));
        Item(row.Collapsed ? "Baum ausklappen" : "Baum einklappen", () => { row.Collapsed = !row.Collapsed; Render(); });
    }
    internal ContextMenu MenuFor(ProcessRow row)
    {
        var menu = new ContextMenu();
        BuildMenu(menu, row);
        return menu;
    }
    internal void Select(ProcessIdentity? identity)
    {
        _focusedIdentity = null;
        Search.Clear(); ActiveOnly.IsChecked = false; BusyOnly.IsChecked = false;
        foreach (var row in _history.Rows.Values) row.Collapsed = false;
        Render();
        Processes.SelectedItem = _rows.FirstOrDefault(r => r.Identity == identity);
        if (Processes.SelectedItem is { } selected) Processes.ScrollIntoView(selected);
    }
    internal void ShowSelectedDetails()
    {
        if (Processes.SelectedItem is not ProcessRow row) { Status.Text = "Zuerst einen Prozess auswählen."; return; }
        Run(async token =>
        {
            var details = await ReadSelected(row, token);
            if (Processes.SelectedItem is not ProcessRow selected || selected.Identity != row.Identity) return;
            _details = details;
            _inspectorPinned = true;
            _history.ObserveDetails(row, details);
            row.Icon = details.Icon; row.Notify();
            Inspector.Text = $"{row.Current.Name} / {row.Identity}\n{row.History}\n{row.Rules}\n\n" +
                string.Join("\n\n", details.Facts.Select(f => $"{f.Name}: {f.Display}\n{f.Observation.Metadata.Availability} / {f.Observation.Metadata.Source} / {f.Observation.Metadata.Timestamp:O} / {f.Privacy}")) +
                "\n\nProgrammsymbol und Dateimetadaten stammen aus der lokalen Datei, nicht aus einer Namensdatenbank.";
        });
    }
    internal void QuickAction(string action)
    {
        if (action.StartsWith("process-id:", StringComparison.Ordinal))
        {
            var parts = action["process-id:".Length..].Split('@');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var pid) || !long.TryParse(parts[1], out var start))
                throw new ArgumentException("Ungültige Prozessidentität aus Befehlspalette.");
            Select(new ProcessIdentity(pid, start));
            if (Processes.SelectedItem is null) Status.Text = "Prozess nicht mehr in der aktuellen Ansicht; kein Ersatzprozess ausgewählt.";
            return;
        }
        switch (action)
        {
            case "process-search": Search.Focus(); Search.SelectAll(); break;
            case "snapshot": SnapshotClicked(this, new RoutedEventArgs()); break;
            case "compare": CompareClicked(this, new RoutedEventArgs()); break;
            case "file-location":
                if (Processes.SelectedItem is null) Status.Text = "Zuerst einen Prozess auswählen.";
                else ShellSelected(false);
                break;
            case "connections":
                Run(async token =>
                {
                    if (Synthetic) throw new InvalidOperationException("Synthetische Ansicht: keine Verbindungsabfrage.");
                    var rows = await ProcessCorrelation.ReadSystemAsync(token);
                    _inspectorPinned = true;
                    Inspector.Text = "Zeitbezogene Owner-PID-Tabelle, keine dauerhafte Identität oder Übertragungsrate.\n\n" +
                        string.Join("\n", rows.Where(r => r.Kind is "TCP" or "UDP").Select(r => $"{r.OwnerPid}: {r.Value} / {r.Availability} / {r.Timestamp:O}"));
                });
                break;
            default: throw new ArgumentException("Unbekannte Prozesspaletten-Aktion.");
        }
    }
    private static Task<ProcessDetails> ReadSelected(ProcessRow row, CancellationToken token) =>
        Task.Run(() => ProcessInspector.Read(row.Identity, token), token);
    private void Terminate(ProcessRow row, bool tree) => Run(async token =>
    {
        if (Synthetic) throw new InvalidOperationException("Synthetische Ansicht: keine Prozessaktion.");
        var actions = ProcessActions.Create();
        var plan = await Task.Run(() => actions.PrepareAsync(row.Identity, tree, token), token);
        token.ThrowIfCancellationRequested();
        var publisher = _details?.Identity == row.Identity ? _details.Facts.SingleOrDefault(f => f.Key == "publisher" && f.Observation.HasValue)?.Observation.Value : null;
        var dialog = new ProcessActionDialog(plan, publisher) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) { Status.Text = "Abgebrochen; kein Eingriff."; return; }
        var result = await Task.Run(() => actions.ExecuteAsync(plan, token), token);
        Status.Text = string.Join("\n", result.Select(r => $"{r.Identity.Pid}: {r.State} / {r.Message}"));
        _inspectorPinned = true; Inspector.Text = Status.Text;
        RefreshRequested?.Invoke();
    });
    private void ShellSelected(bool properties)
    {
        if (Processes.SelectedItem is not ProcessRow row) return;
        Run(async token =>
        {
            var detail = await ReadSelected(row, token);
            var path = detail.Path ?? throw new InvalidOperationException("Pfad nicht lesbar.");
            await Task.Run(() => { ProcessFileInfo.RequireLocal(path); File.GetAttributes(path); }, token);
            token.ThrowIfCancellationRequested();
            if (properties)
            {
                var info = new ShellExecuteInfo { Size = Marshal.SizeOf<ShellExecuteInfo>(), Mask = 0xC,
                    Verb = "properties", File = path, Show = 1 };
                if (!ShellExecuteEx(ref info)) throw ProcessNative.Error();
            }
            else
            {
                using var process = Process.Start(new ProcessStartInfo { FileName = "explorer.exe",
                    Arguments = "/select,\"" + path + "\"", UseShellExecute = true });
            }
        });
    }
    private void Correlate(ProcessRow row, string? kind = null) => Run(async token =>
    {
        if (Synthetic) throw new InvalidOperationException("Synthetische Ansicht: keine reale Systemkorrelation.");
        var result = await ProcessCorrelation.ReadAsync(row.Identity, token);
        _inspectorPinned = true;
        var shown = result.Where(r => kind is null || (kind == "Netzwerk" ? r.Kind is "TCP" or "UDP" or "Netzwerkadapter" : r.Kind == kind)).ToArray();
        Inspector.Text = $"{row.Current.Name} / {row.Identity}\n\n" + (shown.Length == 0 ? "Keine passende Beziehung in dieser zeitbezogenen Probe." :
            string.Join("\n\n", shown.Select(r => $"{r.Kind}: {r.Value}\n{r.Availability} / {r.Source} / {r.Timestamp:O} / {r.Classification}")));
    });
    internal void Reverse(HardwareComponent component) => Run(async token =>
    {
        if (Synthetic) throw new InvalidOperationException("Synthetische Ansicht: keine reale Rückwärtsabfrage.");
        var begin = DateTimeOffset.UtcNow;
        var relations = component.Class == HardwareClass.Gpu ? await ProcessGpu.ReadAsync(token) :
            component.Class == HardwareClass.Network ? await ProcessCorrelation.ReadSystemAsync(token) : [];
        var luid = component.Facts.SingleOrDefault(f => f.Key == "luid" && f.Observation.HasValue)?.Observation.Value;
        var matches = relations.Where(r => r.Availability == AetherOperations.DataAvailability.Available && r.OwnerPid.HasValue &&
            (component.Class == HardwareClass.Gpu && r.Kind == "GPU" && r.Key == luid ||
             component.Class == HardwareClass.Network && r.Kind == "Netzwerkadapter" &&
                 component.Id == HardwareIntelligenceReader.Identity("net", r.Key))).ToArray();
        var lines = new List<string> { component.Name + " / belastbare aktuelle Prozessbezüge", $"Abfragebeginn {begin:O}" };
        foreach (var relation in matches)
        {
            token.ThrowIfCancellationRequested();
            var row = _history.Rows.Values.SingleOrDefault(r => !r.Ended && r.Identity.IsKnown && r.Identity.Pid == relation.OwnerPid);
            if (row is null || row.Identity.StartTicks > begin.UtcTicks) continue;
            try
            {
                using var handle = ProcessNative.Open(row.Identity);
                if (!ProcessNative.Exited(handle)) lines.Add($"{row.Current.Name} / {row.Identity}\n{relation.Value}\n{relation.Source} / {relation.Timestamp:O}");
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            { Trace.TraceWarning("Prozess-Rückwärtsbezug: {0}", ex); lines.Add($"PID {relation.OwnerPid}: Identität nicht mehr prüfbar; keine Zuordnung."); }
        }
        if (lines.Count == 2) lines.Add("Keine exakt belegte aktuelle Beziehung. Wildcardadressen, fehlende LUIDs und Sensorursachen werden nicht geraten.");
        _inspectorPinned = true;
        Inspector.Text = string.Join("\n\n", lines);
    });
    private void Copy(string value) => Run(_ => { Clipboard.SetText(value); return Task.CompletedTask; });
    private void CancelClicked(object sender, RoutedEventArgs e) => _work?.Cancel();
    internal void Run(Func<CancellationToken, Task> action)
    {
        if (_shutdown || !_pending.IsCompleted) { Status.Text = "Aktion läuft; zuerst abbrechen oder warten."; return; }
        _work?.Dispose(); _work = new();
        _pending = RunCore(action, _work.Token);
    }
    private async Task RunCore(Func<CancellationToken, Task> action, CancellationToken token)
    {
        try
        {
            Status.Text = "Erfassung / Aktion läuft …"; await action(token);
            if (Status.Text == "Erfassung / Aktion läuft …") Status.Text = "Abfrage abgeschlossen; Status und Zeitpunkt an den einzelnen Quellen beachten.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { Status.Text = "Abgebrochen. Bereits angeforderte Aktionen sind nicht rückgängig."; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or ExternalException or NotSupportedException)
        {
            Trace.TraceError("Prozesszentrale: {0}", ex);
            Status.Text = ex is Win32Exception { NativeErrorCode: 5 } ? "Zugriff verweigert; keine automatische Rechteerhöhung." : ex.Message;
        }
    }
    internal async Task ShutdownAsync()
    {
        _shutdown = true; _work?.Cancel(); await _pending; _work?.Dispose(); _work = null;
    }
    private void SaveViewClicked(object sender, RoutedEventArgs e) => Run(_ =>
    {
        ProcessViewPreferences.Save(Search.Text, TreeMode.IsChecked == true, ActiveOnly.IsChecked == true, BusyOnly.IsChecked == true, ViewName.Text);
        Status.Text = "Ansicht lokal gespeichert; keine Prozesschronik."; return Task.CompletedTask;
    });
    private void LoadViewClicked(object sender, RoutedEventArgs e) => Run(_ =>
    {
        var saved = ProcessViewPreferences.Load(ViewName.Text);
        Search.Text = saved.Filter; TreeMode.IsChecked = saved.Tree; ActiveOnly.IsChecked = saved.Active; BusyOnly.IsChecked = saved.Busy;
        return Task.CompletedTask;
    });
    private void ViewNamesOpened(object? sender, EventArgs e) => Run(_ =>
    {
        ViewName.ItemsSource = ProcessViewPreferences.Names(); return Task.CompletedTask;
    });
    private void SaveSnapshotClicked(object sender, RoutedEventArgs e) => Run(async token =>
    {
        if (_snapshot is null) throw new InvalidOperationException("Zuerst einen Snapshot aufnehmen.");
        var dialog = new SaveFileDialog { Filter = "Vollständiger lokaler AETHER-Snapshot|*.aethersnapshot", FileName = "system.aethersnapshot" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (MessageBox.Show(Window.GetWindow(this), "Vollständige sensible Prozess-, Dienst-, Autostart- und Verbindungsdaten unverschlüsselt lokal speichern?\nNicht ungeprüft weitergeben. Der normale Export bietet Redaktion.",
            "Lokaler Snapshot", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await File.WriteAllTextAsync(dialog.FileName, _snapshot.LocalJson(), token);
        Status.Text = "Vollständiger sensibler Snapshot lokal gespeichert; nicht übertragen.";
    });
    private void LoadSnapshotClicked(object sender, RoutedEventArgs e) => Run(async token =>
    {
        var dialog = new OpenFileDialog { Filter = "Lokaler AETHER-Snapshot|*.aethersnapshot" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (new FileInfo(dialog.FileName).Length > 32 * 1024 * 1024) throw new InvalidDataException("Snapshot überschreitet 32 MiB.");
        var loaded = ProcessSystemSnapshot.Parse(await File.ReadAllTextAsync(dialog.FileName, token));
        _previous = _snapshot; _snapshot = loaded; _inspectorPinned = true;
        Inspector.Text = $"Lokaler Snapshot von {loaded.Timestamp:O} geladen. Import führt keine Aktion aus und verändert nicht die Live-Prozessliste.";
    });
    private void SnapshotClicked(object sender, RoutedEventArgs e) => Run(async token =>
    {
        _previous = _snapshot;
        _snapshot = await ProcessSystemSnapshot.CaptureAsync(_history.Rows.Values.Where(r => !r.Ended).Select(r => r.Current).ToArray(),
            HardwareSnapshot?.Invoke(), Synthetic, token);
        _inspectorPinned = true;
        Inspector.Text = $"Lokaler Sitzungssnapshot {_snapshot.Timestamp:O}: {_snapshot.Processes.Count} Prozesse, {_snapshot.Relations.Count} Quellenzeilen.\n" +
            "Nicht atomar. Speichern/Export nur auf eigenen Klick. Für Vergleich einen zweiten Snapshot aufnehmen.";
    });
    private void CompareClicked(object sender, RoutedEventArgs e)
    {
        if (_previous is null || _snapshot is null) { Status.Text = "Zuerst zwei Snapshots aufnehmen."; return; }
        _inspectorPinned = true;
        Inspector.Text = ProcessSystemSnapshot.Compare(_previous, _snapshot);
    }
    private void ExportClicked(object sender, RoutedEventArgs e) => Run(async token =>
    {
        if (_snapshot is null) throw new InvalidOperationException("Zuerst einen Snapshot aufnehmen.");
        var dialog = new SaveFileDialog { Filter = "JSON|*.json|CSV|*.csv|Text|*.txt", FileName = "aether-systemsnapshot.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var consent = MessageBox.Show(Window.GetWindow(this), "Sensible Prozessnamen, Identitäten, Pfade und Verbindungen mit exportieren?\nNur bei bewusster lokaler Verwendung oder geprüfter Weitergabe.",
            "Datenschutz", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.No);
        if (consent == MessageBoxResult.Cancel) return;
        var content = _snapshot.Export(Path.GetExtension(dialog.FileName), consent == MessageBoxResult.Yes);
        await File.WriteAllTextAsync(dialog.FileName, content, token);
        Status.Text = "Snapshot gespeichert; keine Übertragung.";
    });
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size, Mask; public IntPtr Window;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Verb, File, Parameters, Directory;
        public int Show; public IntPtr Instance, IdList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Class;
        public IntPtr ClassKey; public uint HotKey; public IntPtr Icon, Process;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
}
