using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AetherOperations.Platform;
using Microsoft.Win32;

namespace SystemCockpit.Controls;

public partial class AdminPlatformView : UserControl
{
    private PlatformStore? _store;
    private PlatformEngine? _engine;
    private PlatformInventory _inventory = new(1, [], []);
    private PlatformJob? _job;
    private CancellationTokenSource? _stop;
    private Task _pending = Task.CompletedTask;
    private bool _busy, _closing;
    private sealed record GroupRow(string Name, TargetGroup? Group);
    private sealed record TargetRow(AdminTarget Target)
    {
        public string Name => Target.Name;
        public string Host => Target.Host;
        public string Transport => Target.Simulated ? "SIMULIERT · keine Verbindung" : Target.Transport == TransportKind.Local ? "Lokal · Benutzerkontext" : Target.Transport + " · nicht konfiguriert";
        public string Evidence => Target.Facts.Count == 0 ? "Noch nicht erhoben" : string.Join("; ", Target.Facts.Select(p =>
            $"{FieldText(p.Key)}: {p.Value.Value ?? "Nicht verfügbar"}"));
    }
    private static string FieldText(string field) => field switch
    {
        "operatingSystem" => "Betriebssystem", "build" => "Systembuild", "architecture" => "Architektur",
        "freeBytes" => "Freier Speicher (Bytes)", "lastContact" => "Letzter Kontakt", "permission" => "Berechtigung",
        "fqdn" => "Vollständiger Rechnername", "addresses" => "Adressen", "role" => "Rolle",
        "domainOrWorkgroup" => "Domäne / Arbeitsgruppe", "reachability" => "Erreichbarkeit",
        "authentication" => "Authentifizierung", "clockSkew" => "Zeitabweichung", "reboot" => "Neustartstatus",
        "updates" => "Updates", "protection" => "Schutzstatus", "storage" => "Speicher",
        "services" => "Dienste", "software" => "Software", "hardware" => "Hardware", "certificates" => "Zertifikate",
        "adapters" => "Adapter", "warnings" => "Hinweise", _ => field
    };
    private sealed record JobRow(PlatformJob Job)
    {
        public string Created => Job.Preview.Created.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        public string Name => PlatformActions.Get(Job.Preview.ActionId).Name;
        public string State => StateText(Job.State);
        public int Count => Job.Preview.Targets.Length;
    }
    public AdminPlatformView()
    {
        InitializeComponent();
        Actions.ItemsSource = PlatformActions.Runbooks;
        Actions.SelectedIndex = 0;
        Catalog.Text = string.Join("\n\n", PlatformActions.All.Select(a => $"{a.Name} · Risiko {a.Safety.Risk}\n{a.Purpose}\n" +
            $"Voraussetzung: {a.Plan.Preconditions}\nWirkung: {a.Plan.Execute}\nPrüfung: {a.Plan.Verify}\nRückweg: {a.Plan.Rollback}\nVertrag v{a.Version}: {a.Hash}")) +
            "\n\nFeste geprüfte Schritte:\n" + string.Join("\n", PlatformActions.Steps.Select(s => $"{s.Id}: {s.Description}")) +
            "\n\nWeitere vorhandene Verwaltungsaktionen bleiben im Reiter Einzelverwaltung. Sie sind nicht für diesen Mehrzielmotor qualifiziert. Freie Skripte und freie Runbookschritte werden hier nicht ausgeführt.";
        Integrations.Text = string.Join("\n\n", PlatformActions.Capabilities.Select(c => $"{c.Transport}: {c.Detail}"));
        HelpText.Text = "1. Arbeitsbereich laden registriert nur diesen Rechner; es liest keine Remoteziele. Simulation ergänzen erzeugt ausdrücklich künstliche Ziele.\n\n" +
            "2. Geräte mit Strg/Shift auswählen oder eine ausdrücklich benannte Gruppe wählen. Import erwartet CSV Name,Host oder ein Ziel-JSON; keine Verbindung wird hergestellt. Auswahl als Gruppe speichert eine manuelle Gruppe.\n\n" +
            "3. Assistent wählen und Vorschau erzeugen. Vorschau / Ergebnisse enthält alle eingefrorenen Ziele, Risiko, Wellen, Wirkung und Vertragshash. Auch Lesen braucht die Freigabe des genauen Vorschauhashes.\n\n" +
            "4. Freigeben ist die persönliche lokale Bestätigung für Risiko 0/1, keine unabhängige Vieraugenfreigabe. Die lokale Rolle Support wird vom Host gesetzt und verleiht keine zusätzlichen Rechte oder Serverberechtigung. Das Cockpit läuft gemäß bestehendem Startmanifest bereits erhöht; die Plattform ist keine Sandbox. Freigaben gelten 15 Minuten.\n\n" +
            "5. Ausführen startet zuerst Canary und dann Wellen. Zielzeitlimit 30 Sekunden, Gesamtzeitlimit 15 Minuten. Die Fehlergrenze gilt kumulativ; ein fehlgeschlagenes Canary stoppt weitere Wellen. Pausieren lässt die aktuelle Welle enden; Abbruch stoppt weitere Arbeit, nimmt Änderungen aber nicht automatisch zurück.\n\n" +
            "6. Nach Unterbrechung zuerst Aufträge auswählen und Nachprüfen. Es erfolgt keine automatische Wiederholung. Erfolgreiche Ziele werden bei ausdrücklich neu freigegebener Fortsetzung übersprungen. Der Rückweg entfernt nur eigene unveränderte Testmarken; bei Konflikt bleibt die Datei erhalten.\n\n" +
            "7. Topologie zeigt nur konfigurierte Gruppen-/Transportbeziehungen, kein entdecktes Netzwerk. Sollzustand verwendet vorhandene Freiplatzdaten, höchstens zehn Minuten alt. Fehlende Daten bleiben nicht bewertbar.\n\n" +
            "8. Daten liegen lokal im Benutzerprofil unter AETHER\\AdminPlatform. Höchstens 128 Aufträge, 16 MiB je Datei und 4096 Auditeinträge je Auftrag. Es gibt keine automatische Löschung. Bei vollem Bestand bewusst außerhalb des Arbeitsbereichs archivieren; nicht während eines Auftrags verändern.\n\n" +
            "AD, WinRM/JEA, SSH, Agent/Relay, Graph, Intune und Arc sind in diesem Motor nicht konfiguriert. Bestehende getrennte Verwaltungswege bleiben erhalten. Erweiterungsmanifeste werden nur geprüft und immer gesperrt. Details: docs/ADMIN-OPERATOR-GUIDE.md und docs/ADMIN-DEPLOYMENT-GUIDE.md.";
    }
    public static string StateText(PlatformJobState state) => state switch
    {
        PlatformJobState.AwaitingApproval => "Freigabe erforderlich", PlatformJobState.Approved => "Freigegeben",
        PlatformJobState.Running => "Läuft", PlatformJobState.Paused => "Pausiert", PlatformJobState.Succeeded => "Erfolgreich",
        PlatformJobState.Partial => "Teilerfolg", PlatformJobState.Cancelled => "Abgebrochen",
        PlatformJobState.NeedsVerification => "Nachprüfung erforderlich", PlatformJobState.RolledBack => "Zurückgenommen",
        PlatformJobState.RollbackFailed => "Rückwegkonflikt", _ => "Nicht abgeschlossen"
    };
    private PlatformEngine Engine => _engine ?? throw new InvalidOperationException("Arbeitsbereich zuerst laden.");
    private PlatformJob Job => _job ?? throw new InvalidOperationException("Zuerst eine Vorschau erzeugen oder einen Auftrag auswählen.");
    private void BindEngine()
    {
        var local = _inventory.Targets.Single(t => t.Kind == TargetKind.LocalComputer && t.Transport == TransportKind.Local &&
            t.Host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase));
        _engine = new PlatformEngine(_store!, new($"{Environment.UserDomainName}\\{Environment.UserName}",
            PlatformRole.Support, _inventory.Targets.Select(t => t.Id).ToImmutableArray()),
            [new LocalPlatformAdapter(Path.Combine(_store!.Root, "markers"), local.Id),
                new SimulatedPlatformAdapter(Path.Combine(_store.Root, "simulation"))]);
        var completed = 0;
        _engine.TargetFinished += _ => Dispatcher.BeginInvoke(() => Status.Text = $"Welle läuft · {++completed} Zielergebnisse; endgültiger Stand nach Speicherung.");
    }
    private void Refresh()
    {
        RefreshTargets();
        Groups.ItemsSource = new[] { new GroupRow("Markierte Geräte", null) }.Concat(_inventory.Groups.Select(g => new GroupRow(g.Name, g))).ToArray();
        Groups.SelectedIndex = 0;
        if (_store is not null) Jobs.ItemsSource = _store.Jobs().Select(j => new JobRow(j)).ToArray();
        ShowJob();
    }
    private void RefreshTargets()
    {
        var observed = _job?.Results.ToDictionary(r => r.TargetId, r => r.Facts);
        Targets.ItemsSource = _inventory.Targets.Select(t => new TargetRow(
            observed is not null && observed.TryGetValue(t.Id, out var facts) ? t with { Facts = facts } : t)).ToArray();
    }
    private void ShowJob()
    {
        if (_job is not { } job) return;
        var action = PlatformActions.Get(job.Preview.ActionId);
        Details.Text = $"{action.Name} · Risiko {action.Safety.Risk} · {StateText(job.State)}\n" +
            $"Runbook: {job.Preview.RunbookId}\nVorschauhash: {job.PreviewHash}\n{action.Plan.Execute}\nRückweg: {action.Plan.Rollback}\n" +
            $"Ziele: {job.Preview.Targets.Length}; Parallel: {job.Preview.Policy.Workers}; Welle: {job.Preview.Policy.WaveSize}; Canary: {job.Preview.Policy.CanaryCount}; Fehlerbudget: {job.Preview.Policy.MaximumFailures}\n\n" +
            string.Join("\n", job.Preview.Targets.Select(t => $"{t.Target.Name} · {t.Target.Host} · {(t.Target.Simulated ? "SIMULIERT" : "Lokal / nicht konfigurierter Transport")}\n" +
                (job.Results.SingleOrDefault(r => r.TargetId == t.Target.Id) is { } r ?
                    $"{r.Detail} · Exitcode {r.ExitCode?.ToString() ?? "Nicht vorhanden"}\n" +
                    string.Join("\n", r.Facts.Select(p => $"{FieldText(p.Key)}: {p.Value.Value ?? "Nicht verfügbar"} · " +
                        $"{p.Value.Metadata.Source} · {p.Value.Metadata.Timestamp:O}")) : "Noch nicht gestartet"))) +
            "\n\nAudit:\n" + string.Join("\n", job.Audit.Select(a => $"{a.At:O}: {a.Event}"));
    }
    private async Task Operate(Func<CancellationToken, Task> operation)
    {
        if (_busy || _closing) return;
        _busy = true; SetupPanel.IsEnabled = PlanPanel.IsEnabled = ActionPanel.IsEnabled = Areas.IsEnabled = false;
        PauseButton.IsEnabled = CancelButton.IsEnabled = true;
        _stop = new CancellationTokenSource();
        try { await operation(_stop.Token); Refresh(); Status.Text = _job is null ? "Arbeitsbereich bereit." : StateText(_job.State); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or JsonException or OperationCanceledException)
        { Trace.TraceError("AETHER Administrationsplattform: {0}", ex); Status.Text = "Auftrag nicht bestätigt: " + ex.Message; }
        finally
        {
            _stop.Dispose(); _stop = null; _busy = false;
            SetupPanel.IsEnabled = PlanPanel.IsEnabled = ActionPanel.IsEnabled = Areas.IsEnabled = true;
            PauseButton.IsEnabled = CancelButton.IsEnabled = false;
        }
    }
    private async void LoadClicked(object sender, RoutedEventArgs e)
    {
        _pending = Operate(_ =>
        {
            _store = new PlatformStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AETHER", "AdminPlatform"));
            using var lease = _store.Acquire();
            var local = TargetInventory.Create("Dieser Rechner", Environment.MachineName, TargetKind.LocalComputer, TransportKind.Local);
            _inventory = _store.LoadInventory() ?? new(1, [local], []);
            if (!_inventory.Targets.Any(t => t.Id == local.Id)) _inventory = _inventory with { Targets = _inventory.Targets.Add(local) };
            _store.SaveInventory(_inventory); BindEngine(); return Task.CompletedTask;
        }); await _pending;
    }
    private void SaveInventory(PlatformInventory next)
    {
        _ = Engine;
        using var lease = _store!.Acquire();
        _store.SaveInventory(next); _inventory = next; BindEngine();
    }
    private async void SimulationClicked(object sender, RoutedEventArgs e)
    {
        _pending = Operate(_ =>
        {
            if (!int.TryParse(SimulationCount.Text, out var number) || number < 1 || number > TargetInventory.MaximumTargets)
                throw new ArgumentException("Simulationsanzahl muss zwischen 1 und 1000 liegen.");
            var start = _inventory.Targets.Count(t => t.Simulated);
            var added = Enumerable.Range(start, number).Select(i => TargetInventory.Create($"Simulation {i + 1}",
                $"simulation-{i + 1}.invalid", TargetKind.WindowsClient, TransportKind.Simulated, true));
            var groups = _inventory.Groups.Any(g => g.Dynamic && g.Transport == TransportKind.Simulated) ? _inventory.Groups :
                _inventory.Groups.Add(new(Guid.NewGuid(), "Simulierte Geräte", [], true, Transport: TransportKind.Simulated));
            SaveInventory(_inventory with { Targets = _inventory.Targets.AddRange(added), Groups = groups }); return Task.CompletedTask;
        }); await _pending;
    }
    private static string ReadImport(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 512 * 1024) throw new InvalidDataException("Import ist größer als 512 KiB.");
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true));
        return reader.ReadToEnd();
    }
    private async void ImportClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Ziele (*.csv;*.json)|*.csv;*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        _pending = Operate(_ =>
        {
            var text = ReadImport(dialog.FileName);
            var targets = Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase) ?
                TargetInventory.ImportCsv(text) : TargetInventory.ImportJson(text);
            SaveInventory(_inventory with { Targets = _inventory.Targets.AddRange(targets) }); return Task.CompletedTask;
        }); await _pending;
    }
    private ImmutableArray<AdminTarget> SelectedTargets() => Groups.SelectedItem is GroupRow { Group: { } group } ?
        TargetInventory.Resolve(group, _inventory.Targets) : Targets.SelectedItems.OfType<TargetRow>().Select(r => r.Target).ToImmutableArray();
    private async void GroupClicked(object sender, RoutedEventArgs e)
    {
        var members = Targets.SelectedItems.OfType<TargetRow>().Select(t => t.Target.Id).ToImmutableArray();
        _pending = Operate(_ =>
        {
            if (members.IsEmpty) throw new ArgumentException("Zuerst Geräte markieren.");
            SaveInventory(_inventory with { Groups = _inventory.Groups.Add(new(Guid.NewGuid(), $"Auswahlgruppe {_inventory.Groups.Length + 1}", members)) });
            return Task.CompletedTask;
        }); await _pending;
    }
    private async void PreviewClicked(object sender, RoutedEventArgs e)
    {
        var targets = SelectedTargets();
        var runbook = (AdminRunbook)Actions.SelectedItem;
        _pending = Operate(async token =>
        {
            if (!int.TryParse(Workers.Text, out var workers) || !int.TryParse(Wave.Text, out var wave) ||
                !int.TryParse(Canary.Text, out var canary) || !int.TryParse(Failures.Text, out var failures))
                throw new ArgumentException("Ausführungsgrenzen müssen ganze Zahlen sein.");
            var policy = new ExecutionPolicy(workers, wave, canary, failures);
            _job = await Task.Run(() => Engine.PreviewAsync(runbook.ActionId, targets, policy, token));
            Areas.SelectedIndex = 2;
        }); await _pending;
    }
    private bool Confirm(string operation) => _job is not null && MessageBox.Show(Window.GetWindow(this),
        $"{operation}\n{PlatformActions.Get(_job.Preview.ActionId).Name}\n{_job.Preview.Targets.Length} eingefrorene Ziele\nHash: {_job.PreviewHash}\n" +
        "Die vollständige Ziel-/Wirkungsliste steht unter Vorschau / Ergebnisse. Bestätigung gilt ausschließlich für diesen Auftrag.",
        "AETHER · Auftrag bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    private async void ApproveClicked(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Vorschau persönlich für 15 Minuten freigeben? Keine Vieraugenfreigabe.")) return;
        _pending = Operate(_ => { _job = Engine.Approve(Job.Preview.Id, Job.PreviewHash, TimeSpan.FromMinutes(15)); return Task.CompletedTask; }); await _pending;
    }
    private async void RunClicked(object sender, RoutedEventArgs e)
    { _pending = Operate(async token => { var id = Job.Preview.Id; _job = await Task.Run(() => Engine.RunAsync(id, token)); }); await _pending; }
    private async void RevokeClicked(object sender, RoutedEventArgs e)
    { _pending = Operate(_ => { _job = Engine.Revoke(Job.Preview.Id); return Task.CompletedTask; }); await _pending; }
    private async void ReconcileClicked(object sender, RoutedEventArgs e)
    { _pending = Operate(async token => { var id = Job.Preview.Id; _job = await Task.Run(() => Engine.ReconcileAsync(id, token)); }); await _pending; }
    private async void RollbackClicked(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Nur eigene unveränderte Testmarken zurücknehmen?")) return;
        _pending = Operate(async token => { var job = Job; _job = await Task.Run(() => Engine.RollbackAsync(job.Preview.Id, job.PreviewHash, token)); }); await _pending;
    }
    private void PauseClicked(object sender, RoutedEventArgs e) => _engine?.RequestPause();
    private void CancelClicked(object sender, RoutedEventArgs e) => _stop?.Cancel();
    private void JobChanged(object sender, SelectionChangedEventArgs e)
    { if (!_busy && Jobs.SelectedItem is JobRow row) { _job = row.Job; RefreshTargets(); ShowJob(); } }
    private async void EvaluateClicked(object sender, RoutedEventArgs e)
    {
        _pending = Operate(_ =>
        {
            if (!decimal.TryParse(MinimumSpace.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var minimum) ||
                minimum < 0 || minimum > decimal.MaxValue / 1073741824m)
                throw new ArgumentException("Mindestfreiplatz muss eine nichtnegative Zahl sein.");
            var latest = _inventory.Targets.Select(t => t with { Facts = _job?.Results.SingleOrDefault(r => r.TargetId == t.Id)?.Facts ?? t.Facts }).ToImmutableArray();
            var names = _inventory.Targets.ToDictionary(t => t.Id, t => t.Name);
            foreach (var group in _inventory.Groups) names[group.Id] = group.Name;
            foreach (var transport in _inventory.Targets.Select(t => t.Transport).Distinct())
                names[TargetInventory.StableId("transport", transport.ToString())] =
                    transport == TransportKind.Simulated ? "SIMULIERT" : transport == TransportKind.Local ? "Lokaler Adapter" : transport.ToString();
            Topology.Text = "Nur konfigurierte Beziehungen:\n" + string.Join("\n", TargetInventory.Topology(_inventory).Select(t =>
                $"{names[t.From]} → {names[t.To]} · {t.Relation} · {t.Metadata.Source}")) +
                "\n\nSollzustand:\n" + string.Join("\n", latest.Select(t => $"{t.Name}: " +
                    TargetInventory.Evaluate(t, new("freeBytes", minimum * 1024 * 1024 * 1024, "Bytes"), DateTimeOffset.UtcNow).State));
            return Task.CompletedTask;
        }); await _pending;
    }
    private async void ExportClicked(object sender, RoutedEventArgs e)
    {
        var format = (string)((Button)sender).Tag;
        var consent = SensitiveConsent.IsChecked == true;
        var dialog = new SaveFileDialog { Filter = $"{format.ToUpperInvariant()}|*.{format}", DefaultExt = format, FileName = "AETHER-Auftragsbericht" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        _pending = Operate(_ =>
        {
            var text = format switch { "json" => PlatformReports.Json(Job, consent), "csv" => PlatformReports.Csv(Job, consent), _ => PlatformReports.Html(Job, consent) };
            File.WriteAllText(dialog.FileName, text); return Task.CompletedTask;
        }); await _pending;
    }
    private async void ManifestClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Erweiterungsmanifest|*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        _pending = Operate(_ =>
        {
            var package = JsonSerializer.Deserialize<ScriptPackage>(ReadImport(dialog.FileName), PlatformJson.Options)
                ?? throw new InvalidDataException("Manifest fehlt.");
            var inspected = PlatformActions.InspectPackage(package);
            Integrations.Text = $"{inspected.Id} · {inspected.Version}: gesperrt\n{inspected.SignatureStatus}\nKeine Erweiterung wurde geladen oder ausgeführt.";
            return Task.CompletedTask;
        }); await _pending;
    }
    public async Task ShutdownAsync()
    { _closing = true; _stop?.Cancel(); await _pending; }
}
