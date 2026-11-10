using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Text.Json;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public partial class AdminWorkbench : UserControl
{
    private readonly RegistryTweaks _tweaks = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AetherCockpit", "Backups", "registry-settings.json"));
    private readonly AdminArea _registryArea = new("registry", "Explorer / Datenschutz / Tweaks",
        "Gezielte Einstellungen mit exakter Registry-Sicherung vor Aenderung. Ruecknahme verweigert extern geaenderte Werte. HKCU = aktuelles Admin-Konto.",
        "", ["AN", "AUS", "Gesicherten Zustand wiederherstellen"]);
    private List<AdminEntity> _rows = [];
    private List<TweakState> _states = [];
    private CancellationTokenSource? _stop;
    private Task _pending = Task.CompletedTask;
    private AdminPlan? _preview;
    private AdminEntity? _previewEntity;
    private string _previewParameter = "", _previewAction = "";
    private AdminArea? _previewArea;
    private (AdminArea Area, AdminEntity After, AdminPlan Plan)? _undo;
    public Task Pending => _pending;
    public void Cancel() => _stop?.Cancel();

    public AdminWorkbench()
    {
        InitializeComponent();
        Area.ItemsSource = AdminManagement.Areas.Concat([_registryArea]).ToList();
        Area.SelectedIndex = 0;
    }

    public void SelectArea(string id)
    {
        if (_stop is not null) { Status.Text = "Zuerst die laufende Verwaltungsaktion beenden."; return; }
        Area.SelectedItem = Area.Items.Cast<AdminArea>().Single(a => a.Id == id);
    }

    private void AreaChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Operation is null || Area.SelectedItem is not AdminArea area) return;
        Operation.ItemsSource = area.Actions;
        Operation.SelectedIndex = 0;
        AreaDescription.Text = area.Description;
        _rows.Clear();
        _states.Clear();
        Entities.ItemsSource = null;
        _preview = null;
        Console.Clear();
        Parameter.Clear();
    }

    private void FilterChanged(object sender, TextChangedEventArgs e)
    {
        if (Entities?.ItemsSource is null) return;
        CollectionViewSource.GetDefaultView(Entities.ItemsSource).Filter = o => o is AdminEntity row &&
            $"{row.Name} {row.Id} {row.Status} {row.Detail}".Contains(Filter.Text, StringComparison.OrdinalIgnoreCase);
    }
    private void EntityChanged(object sender, SelectionChangedEventArgs e) => _preview = null;
    private async void ReadClicked(object sender, RoutedEventArgs e) => await RunAsync("Verwaltungsstatus lesen", async token =>
    {
        var area = SelectedArea();
        _rows = await ReadRows(area, token);
        Entities.ItemsSource = _rows;
        FilterChanged(sender, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
        Write($"[STATUS] {_rows.Count} Zeilen. Quelle: {area.Name}. Leere Liste ist kein Fehlerfreiheitsbeweis.\n");
        _preview = null;
    });
    private AdminArea SelectedArea() => Area.SelectedItem as AdminArea ?? throw new ArgumentException("Bereich auswaehlen.");
    private async Task<List<AdminEntity>> ReadRows(AdminArea area, CancellationToken token, string? parameterOverride = null)
    {
        if (area.Id != "registry") return await AdminManagement.ReadAsync(area, token, area.Id == "acl" ? parameterOverride ?? Parameter.Text.Trim() : "");
        _states = await Task.Run(_tweaks.Read, token);
        return _states.Select(s => new AdminEntity(s.Setting.Id, s.Name, s.Status, s.Detail, s.Value?.ToString() ?? "absent")).ToList();
    }

    private void PreviewClicked(object sender, RoutedEventArgs e) => RunAsync("Aenderungsvorschau", _ =>
    {
        var area = SelectedArea();
        var row = Entities.SelectedItem as AdminEntity ?? throw new ArgumentException("Zeile auswaehlen.");
        var action = Operation.SelectedItem as string ?? throw new ArgumentException("Aktion auswaehlen.");
        _previewArea = area;
        _previewEntity = row;
        _previewParameter = Parameter.Text.Trim();
        _previewAction = action;
        _preview = area.Id == "registry"
            ? new(area.Name + " / " + action, $"Registry: {row.Name}\nAktion: {action}\nVorher: {row.Status}\n{row.Detail}", null,
                "Gezielte Registry-Aenderung. Originalwert/Abwesenheit wird vor dem Schreiben gesichert. Wirkung kann Explorer-Neustart/Abmeldung erfordern.")
            : AdminManagement.Plan(area, action, row, _previewParameter);
        Write("[VORSCHAU / KEINE AUSFUEHRUNG]\n" + _preview.Warning + "\n\n" + _preview.Script + "\n");
        return Task.CompletedTask;
    });

    private async void ApplyClicked(object sender, RoutedEventArgs e)
    {
        if (_stop is not null) { Status.Text = "Aktion laeuft bereits."; return; }
        var plan = _preview;
        var area = _previewArea;
        var row = _previewEntity;
        if (plan is null || area is null || row is null || Entities.SelectedItem as AdminEntity != row ||
            SelectedArea() != area || Parameter.Text.Trim() != _previewParameter || Operation.SelectedItem as string != _previewAction)
        {
            Status.Text = "Vorschau fehlt oder Auswahl wurde geaendert. Neue Vorschau erzeugen.";
            return;
        }
        if (area.Id == "processes" && _previewAction == "Prozess beenden")
        {
            await RunAsync("Prozess beenden", async token =>
            {
                var identity = new ProcessIdentity(int.Parse(row.Id, System.Globalization.CultureInfo.InvariantCulture),
                    long.Parse(row.Token, System.Globalization.CultureInfo.InvariantCulture));
                var actions = ProcessActions.Create();
                var target = await Task.Run(() => actions.PrepareAsync(identity, false, token), token);
                var dialog = new ProcessActionDialog(target) { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() != true) { Write("[ABBRUCH] Kein Eingriff.\n"); return; }
                var results = await Task.Run(() => actions.ExecuteAsync(target, token), token);
                Write(string.Join("\n", results.Select(r => $"{r.State}: {r.Message}")) + "\n");
                _rows = await ReadRows(area, token);
                Entities.ItemsSource = _rows;
                _preview = null;
            });
            return;
        }
        if (MessageBox.Show(Window.GetWindow(this), plan.Warning + "\n\nJetzt ausfuehren?", plan.Label,
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await RunAsync(plan.Label, async token =>
        {
            SystemActions.RequireAdmin();
            var currentRows = await ReadRows(area, token);
            var current = currentRows.SingleOrDefault(r => r.Id == row.Id);
            if (current is null || current.Status != row.Status || current.Token != row.Token || current.Auxiliary != row.Auxiliary)
                throw new InvalidOperationException("Zielzustand seit Vorschau geaendert. Tabelle neu lesen.");
            if (area.Id == "processes" && int.TryParse(row.Id, out var pid) && pid == Environment.ProcessId)
                throw new InvalidOperationException("AETHER kann sich nicht selbst beenden oder umpriorisieren.");
            _undo = null;
            if (area.Id == "registry")
            {
                var state = _states.Single(s => s.Setting.Id == row.Id);
                await Task.Run(() =>
                {
                    if (_previewAction == "Gesicherten Zustand wiederherstellen") _tweaks.Restore(state.Setting);
                    else _tweaks.Set(state.Setting, state.Value, _previewAction == "AN");
                }, token);
                Write("[REGISTRY] Geschrieben und rueckgelesen / Originalwert gesichert.\n");
            }
            else
            {
                ActionJournal.Append("admin-gui-start", new { area.Id, action = _previewAction, target = row.Id });
                var restore = plan.RestoreScript;
                if (area.Id == "power")
                {
                    var previous = currentRows.SingleOrDefault(r => r.Status.Equals("True", StringComparison.OrdinalIgnoreCase));
                    if (previous is not null) restore = $"& powercfg.exe /setactive {AdminManagement.Quote(previous.Id)}; if($LASTEXITCODE -ne 0){{throw 'Ruecknahme fehlgeschlagen'}}";
                }
                await Execute(plan.Script, token);
                var after = (await ReadRows(area, token)).SingleOrDefault(r => r.Id == row.Id);
                if (restore is not null && after is not null) _undo = (area, after, plan with { RestoreScript = restore });
                ActionJournal.Append("admin-gui-process-complete", new { area.Id, action = _previewAction, target = row.Id });
                Write("[ENDE] Befehl beendet. Rueckgelesenen Status unten/Tabelle pruefen; kein pauschales Erfolgsurteil.\n");
            }
            _rows = await ReadRows(area, token);
            Entities.ItemsSource = _rows;
            _preview = null;
        });
    }

    private async void UndoClicked(object sender, RoutedEventArgs e)
    {
        if (_stop is not null) { Status.Text = "Laufende Aktion zuerst beenden."; return; }
        var undo = _undo;
        if (undo is null) { Status.Text = "Keine reversible Sitzungsaktion. Registry: gesicherten Zustand ueber Aktion wiederherstellen."; return; }
        if (MessageBox.Show(Window.GetWindow(this), undo.Value.Plan.Warning + "\n\nVorherigen Zustand zurueckschreiben?\n" + undo.Value.Plan.RestoreScript,
            "Ruecknahme / Vorschau", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await RunAsync("Ruecknahme", async token =>
        {
            SystemActions.RequireAdmin();
            var current = (await ReadRows(undo.Value.Area, token, undo.Value.After.Id)).SingleOrDefault(r => r.Id == undo.Value.After.Id);
            if (current is null || current.Status != undo.Value.After.Status || current.Token != undo.Value.After.Token ||
                current.Auxiliary != undo.Value.After.Auxiliary)
                throw new InvalidOperationException("Ziel extern geaendert. Keine blinde Ruecknahme.");
            ActionJournal.Append("admin-gui-restore-start", new { undo.Value.Area.Id, target = current.Id });
            await Execute(undo.Value.Plan.RestoreScript!, token);
            _rows = await ReadRows(undo.Value.Area, token, undo.Value.After.Id);
            Entities.ItemsSource = _rows;
            _undo = null;
            Write("[RUECKNAHME] Befehl beendet, Status neu gelesen. Zustandswirkung pruefen.\n");
        });
    }
    private async Task Execute(string script, CancellationToken token)
    {
        Write("[BEFEHL]\n" + script + "\n");
        var result = await AdminShell.RunAsync(script, TimeSpan.FromMinutes(90), token,
            new InlineProgress(chunk => Dispatcher.Invoke(() => Write(chunk))));
        if (result.ExitCode != 0) throw new InvalidOperationException($"Exitcode {result.ExitCode}. Teilaenderungen moeglich; Ausgaben beachten.");
    }
    private sealed class InlineProgress(Action<string> report) : IProgress<string> { public void Report(string value) => report(value); }
    private void CancelClicked(object sender, RoutedEventArgs e) => Cancel();
    private void Write(string text)
    {
        if (Console.Text.Length + text.Length > 1_000_000)
        {
            Console.Text = Console.Text[^Math.Min(Console.Text.Length, 500_000)..] + "\n[ANZEIGELIMIT / aeltere Ausgabe entfernt]\n";
        }
        Console.AppendText(text);
        Console.ScrollToEnd();
    }
    private Task RunAsync(string label, Func<CancellationToken, Task> action)
    {
        if (_stop is not null) { Status.Text = "Laufende Aktion zuerst beenden."; return Task.CompletedTask; }
        _pending = ExecuteAsync();
        return _pending;
        async Task ExecuteAsync()
        {
            _stop = new();
            SelectionControls.IsEnabled = false;
            Entities.IsEnabled = false;
            Filter.IsEnabled = false;
            Parameter.IsEnabled = false;
            Status.Text = label + " laeuft ...";
            try { await action(_stop.Token); Status.Text = label + " abgeschlossen. Ausgaben und Status beachten."; }
            catch (OperationCanceledException) { Status.Text = "Abgebrochen; Teilaenderungen bleiben bestehen."; Write("[ABBRUCH]\n"); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or
                System.ComponentModel.Win32Exception or JsonException or TimeoutException or NotSupportedException or System.Security.SecurityException or System.Xml.XmlException)
            {
                Status.Text = label + ": " + ex.Message;
                Write("[FEHLER] " + ex.Message + "\n");
                Trace.TraceError("{0}: {1}", label, ex);
                _preview = null;
            }
            finally
            {
                _stop.Dispose(); _stop = null;
                SelectionControls.IsEnabled = Entities.IsEnabled = Filter.IsEnabled = Parameter.IsEnabled = true;
            }
        }
    }
}
