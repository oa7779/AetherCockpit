using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public partial class OperationsHub : UserControl
{
    private readonly ObservableCollection<MaintenanceChoice> _tasks = new(MaintenancePlans.Catalog.Select(a => new MaintenanceChoice(a)));
    private readonly ObservableCollection<UpdateChoice> _updates = [];
    private readonly ObservableCollection<ManagedDevice> _devices = [];
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherCockpit", "Operations");
    private CancellationTokenSource? _stop;
    private Task _pending = Task.CompletedTask;
    private bool _maintenance, _stopAfterStep, _shutdown, _loaded;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _elapsed = new();
    private DateTimeOffset _lastOutput;
    private string _label = "";
    public Task Pending => _pending;
    public event EventHandler? CleanerRequested;
    public OperationsHub()
    {
        InitializeComponent();
        Tasks.ItemsSource = _tasks;
        Updates.ItemsSource = _updates;
        Devices.ItemsSource = _devices;
        ApplyTemplate("Nur Diagnose");
        _timer.Tick += (_, _) => Status.Text = $"{_label} / {_elapsed.Elapsed:hh\\:mm\\:ss} / letzte Ausgabe {_lastOutput:HH:mm:ss}" +
            (_maintenance ? _stopAfterStep ? " / ANHALTEN ANGEFORDERT: aktuellen Schritt abwarten" : " / Wartung wird nicht gewaltsam abgebrochen" : "");
        Loaded += async (_, _) =>
        {
            if (_loaded || _shutdown) return;
            _loaded = true;
            await RunAsync("Lokale Zielliste", _ =>
            {
                foreach (var device in ManagedDevices.Load(Path.Combine(_root, "targets.json"))) _devices.Add(device);
                return Task.CompletedTask;
            });
        };
    }
    public void Shutdown()
    {
        _shutdown = true;
        Stop();
    }
    public void OpenWindowsAssistants() =>
        Tabs.SelectedItem = Tabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Header, "Windows-Assistenten"));
    private void Stop()
    {
        if (_maintenance)
        {
            _stopAfterStep = true;
            Status.Text = "Anhalten nach aktuellem Schritt; Wartungsprozess wird nicht gewaltsam beendet. Bericht bleibt erhalten.";
            Write("[STOP ANGEFORDERT] Kein Folgeschritt / aktuelle Wartung geordnet abwarten.\n");
        }
        else _stop?.Cancel();
    }
    private void StopClicked(object sender, RoutedEventArgs e) => Stop();
    private void ApplyTemplate(string name)
    {
        var ids = MaintenancePlans.Template(name);
        foreach (var task in _tasks) task.Selected = ids.Contains(task.Action.Id);
    }
    private void TemplateClicked(object sender, RoutedEventArgs e)
    {
        if (Templates.SelectedItem is ComboBoxItem { Content: string name }) ApplyTemplate(name);
    }
    private void ClearPlanClicked(object sender, RoutedEventArgs e) { foreach (var task in _tasks) task.Selected = false; }
    private void TaskSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Tasks.SelectedItem is MaintenanceChoice choice)
            TaskExplanation.Text = $"{choice.Name} / {choice.Kind}\n{choice.Description}\n\n{choice.Action.Script}";
    }
    private void UpdateSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Updates.SelectedItem is UpdateChoice update)
            TaskExplanation.Text = update.Title + "\nID + Revision: " + update.Id + "\n\nEULA:\n" + update.Update.EulaText;
    }
    private async void WindowsAssistantClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var assistant = WindowsTroubleshootingAssistants.Get(id);
        AssistantTitle.Text = assistant.Name + " / " + assistant.Scope;
        AssistantNextSteps.Text = assistant.NextSteps;
        AssistantOutput.Clear();
        await RunAsync(assistant.Name, async token =>
        {
            SystemActions.RequireAdmin();
            Write($"[WINDOWS-ASSISTENT] {assistant.Name}\n[DIAGNOSE / NUR LESEN]\n");
            var result = await Execute(assistant.Script, token);
            AssistantOutput.Text = result.Output + (result.Error.Length > 0 ? "\n[STDERR]\n" + result.Error : "");
            var folder = CreateRun("assistant-" + assistant.Id);
            Save(folder, "Diagnose.json", new
            {
                Assistant = assistant.Name,
                assistant.Scope,
                CapturedAt = DateTimeOffset.Now,
                Output = result.Output,
                result.Error,
                assistant.NextSteps
            });
            Write("[NAECHSTE SCHRITTE]\n" + assistant.NextSteps + "\n");
            Write($"[LOKALER BERICHT] {Path.Combine(folder, "Diagnose.json")}\n");
        });
    }
    private void RepairPlanPrepareClicked(object sender, RoutedEventArgs e)
    {
        ApplyTemplate("Windows reparieren");
        Tabs.SelectedItem = Tabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Header, "Wartungsplan"));
        Write("[VORBEREITET / NICHT AUSGEFUEHRT] Windows-Reparaturplan. Erst Vorschau pruefen und danach den Plan sowie jeden Aenderungsschritt einzeln bestaetigen.\n");
    }
    private void BackgroundReportClicked(object sender, RoutedEventArgs e)
    {
        var path = BackgroundDiagnosticMonitor.ReportPath;
        if (!File.Exists(path))
        {
            Write("[HINTERGRUNDDIAGNOSE] Noch kein Bericht vorhanden. AETHER muss mindestens einen Diagnosezyklus abschliessen.\n");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Write("[HINTERGRUNDDIAGNOSE] Letzten lokalen Bericht geoeffnet: " + path + "\n");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Trace.TraceError("AETHER Hintergrundbericht konnte nicht geoeffnet werden: {0}", ex);
            Write("[FEHLER] Hintergrundbericht konnte nicht geoeffnet werden: " + ex.Message + "\n");
        }
    }
    private async void AgentsRunClicked(object sender, RoutedEventArgs e) => await RunAsync("Komplettes lokales Diagnoseinventar", async token =>
    {
        SystemActions.RequireAdmin();
        Write("[DIAGNOSE-AGENTEN] Nur lesende Windows-Abfragen / keine Aenderung und keine Netzwerkverbindung.\n");
        var result = await Execute(DiagnosticAssistants.CollectionScript, token);
        var report = DiagnosticAssistants.Analyze(DiagnosticAssistants.Parse(result.Output));
        var folder = CreateRun("diagnostic-agents");
        Save(folder, "Agentenbericht.json", report);
        foreach (var finding in report.Findings)
            Write($"[{finding.Severity}] {finding.Agent}: {finding.Summary}\n  Befund: {finding.Evidence}\n  Naechster Schritt: {finding.Recommendation}\n");
        Write($"[LOKALER BERICHT] {Path.Combine(folder, "Agentenbericht.json")}\n");
    });
    private List<(MaintenanceAction Action, string Script)> BuildPlan()
    {
        var selected = _tasks.Where(t => t.Selected).Select(t => t.Action).ToList();
        if (selected.Count == 0) throw new ArgumentException("Mindestens eine Aufgabe ankreuzen.");
        if (selected.Any(a => a.Id == "cleanup")) throw new ArgumentException("Temp-Bereinigung separat im Cleaner: Dateivorschau und Checkboxen erforderlich.");
        if (selected.Any(a => a.Id == "defenderFull") && selected.Any(a => a.Id == "defenderQuick"))
            throw new ArgumentException("Schnellscan ODER Vollscan auswaehlen.");
        var updates = _updates.Where(u => u.Selected).Select(u => u.Update).ToList();
        if (selected.Any(a => a.Id == "windowsInstall") && LicenseConsent.IsChecked != true)
            throw new ArgumentException("Angekreuzte Update-EULAs lesen und ausdruecklich akzeptieren.");
        return selected.Select(a => (a, MaintenancePlans.Build(a, updates, PackageIds.Text))).ToList();
    }
    private void PlanPreviewClicked(object sender, RoutedEventArgs e) => RunAsync("Wartungsvorschau", _ =>
    {
        var plan = BuildPlan();
        Console.Clear();
        Write("[VORSCHAU / KEINE AUSFUEHRUNG] Ziel: " + Environment.MachineName + "\n");
        Write("Wiederherstellungspunkt: " + (RequireRestorePoint.IsChecked == true ? "pflichtig vor Aenderungen" : "bewusst deaktiviert / Zusatzbestaetigung erforderlich") + "\n");
        foreach (var step in plan) Write($"\n{step.Action.Name} / {(step.Action.Changes ? "AENDERUNG" : "DIAGNOSE")}\n{step.Action.Description}\n{step.Script}\n");
        return Task.CompletedTask;
    });
    private async void PlanRunClicked(object sender, RoutedEventArgs e) => await RunAsync("Wartungsplan", async token =>
    {
        var plan = BuildPlan();
        SystemActions.RequireAdmin();
        if (!Confirm($"Lokales Ziel: {Environment.MachineName}\n{string.Join("\n", plan.Select(p => p.Action.Name))}\n\nIn dieser Reihenfolge starten? Veraendernde Schritte werden einzeln nochmals bestaetigt. Beim ersten Fehler stoppt der Plan."))
            throw new OperationCanceledException("Plan nicht freigegeben.");
        var requiresRestore = RequireRestorePoint.IsChecked == true;
        if (plan.Any(p => p.Action.Changes) && !requiresRestore &&
            !Confirm("Ohne Wiederherstellungspunkt fortfahren? Dieser ist kein Dateibackup. Vorher Daten extern sichern."))
            throw new OperationCanceledException("Ungesicherte Wartung nicht freigegeben.");
        _maintenance = true;
        _stopAfterStep = false;
        Directory.CreateDirectory(_root);
        using var exclusiveRun = new FileStream(Path.Combine(_root, "maintenance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var run = CreateRun("maintenance");
        var results = new List<MaintenanceResult>();
        PlanProgress.Maximum = plan.Count;
        PlanProgress.Value = 0;
        Save(run, "Plan.json", plan.Select(p => new { p.Action.Id, p.Action.Name, p.Action.Changes, p.Action.Description, p.Script }));
        var checkpointDone = false;
        var failed = false;
        foreach (var step in plan)
        {
            var started = DateTimeOffset.Now;
            token.ThrowIfCancellationRequested();
            if (_stopAfterStep || failed)
            {
                results.Add(new(step.Action.Name, "AUSGELASSEN", "Anhalten/Fehler vor diesem Schritt", started, DateTimeOffset.Now));
                SaveResults(run, results);
                continue;
            }
            try
            {
                if (step.Action.Changes && !Confirm($"{step.Action.Name}\n{step.Action.Description}\nZiel: {Environment.MachineName}\n\nJetzt diesen Aenderungsschritt ausfuehren?"))
                {
                    _stopAfterStep = true;
                    results.Add(new(step.Action.Name, "NICHT FREIGEGEBEN", "", started, DateTimeOffset.Now));
                    SaveResults(run, results);
                    continue;
                }
                if (step.Action.Changes && requiresRestore && !checkpointDone)
                {
                    Write("[SICHERUNG] Wiederherstellungspunkt erstellen und kontrollieren; kein Dateibackup.\n");
                    await Execute(MaintenancePlans.RestorePointScript, token, run);
                    checkpointDone = true;
                    if (_stopAfterStep)
                    {
                        results.Add(new(step.Action.Name, "AUSGELASSEN", "Nach Sicherung angehalten / Aenderung nicht gestartet", started, DateTimeOffset.Now));
                        SaveResults(run, results);
                        continue;
                    }
                }
                Write($"\n[START] {step.Action.Name} / lokales Ziel {Environment.MachineName}\n");
                var result = await Execute(step.Script, token, run);
                if (step.Action.Id == "windowsSearch") LoadUpdates(result.Output);
                results.Add(new(step.Action.Name, step.Action.Id == "sfc" ? "PRUEFEN" : "BEFEHL BEENDET / BEFUND PRUEFEN",
                    result.Output + "\n" + result.Error, started, DateTimeOffset.Now));
            }
            catch (Exception ex) when (OperationError(ex))
            {
                failed = true;
                Write("[FEHLER / FOLGESCHRITTE STOPPEN] " + ex.Message + "\n");
                results.Add(new(step.Action.Name, "FEHLER / TEILAENDERUNGEN MOEGLICH", ex.Message, started, DateTimeOffset.Now));
            }
            SaveResults(run, results);
            Write($"[PLAN] {results.Count} / {plan.Count} Aufgaben protokolliert; keine erfundene Gesamtrestzeit.\n");
        }
        Write("[BERICHT] " + run + "\n");
        if (failed) throw new InvalidOperationException("Wartung bei Fehler angehalten. Ergebnis.json / Live.log lesen; Teilaenderungen moeglich.");
    });
    private void SaveResults(string run, List<MaintenanceResult> results)
    {
        Save(run, "Ergebnis.json", results);
        var html = new StringBuilder("<!doctype html><html lang=\"de\"><meta charset=\"utf-8\"><title>AETHER Operations-Bericht</title><body style=\"background:#090611;color:#b0ffd4;font-family:Consolas,monospace\"><h1>AETHER / lokaler Laufbericht</h1><p>Dieser Bericht dokumentiert die ausgeführten Schritte. Ein erfolgreicher Exitcode bestätigt keinen fehlerfreien Rechner. Fehler oder Abbruch können Teilergebnisse hinterlassen. Inventar vor Weitergabe auf private Angaben prüfen.</p>");
        foreach (var result in results)
            html.Append("<details><summary>").Append(WebUtility.HtmlEncode(result.Action + " / " + result.State))
                .Append("</summary><p>").Append(WebUtility.HtmlEncode($"{result.Started:o} bis {result.Finished:o}"))
                .Append("</p><pre style=\"white-space:pre-wrap\">").Append(WebUtility.HtmlEncode(result.Output)).Append("</pre></details>");
        html.Append("</body></html>");
        var report = Path.Combine(run, "Bericht.html");
        File.WriteAllText(report + ".tmp", html.ToString());
        File.Move(report + ".tmp", report, true);
        PlanProgress.Value = Math.Min(PlanProgress.Maximum, results.Count);
    }
    private static void Save(string folder, string name, object value)
    {
        var file = Path.Combine(folder, name);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(file + ".tmp", file, true);
    }
    private string CreateRun(string kind)
    {
        var folder = Path.Combine(_root, "Reports", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + kind + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Live.log"), $"AETHER / {kind} / {DateTimeOffset.Now:o} / local admin {Environment.MachineName}\n");
        return folder;
    }
    private async Task<CommandResult> Execute(string script, CancellationToken token, string? report = null, string? target = null)
    {
        target ??= Environment.MachineName;
        Write("[BEFEHL]\n" + script + "\n");
        ActionJournal.Append("operations-command-start", new { target, report });
        var timeout = _maintenance ? Timeout.InfiniteTimeSpan : target == Environment.MachineName ? TimeSpan.FromMinutes(90) : TimeSpan.FromMinutes(3);
        Exception? logError = null;
        var result = await AdminShell.RunAsync("$ErrorActionPreference='Stop';\n" + script, timeout, token,
            new DirectProgress(text => Dispatcher.Invoke(() =>
            {
                if (report is not null && logError is null)
                {
                    try { File.AppendAllText(Path.Combine(report, "Live.log"), text); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logError = ex;
                        _stopAfterStep = true;
                        Write("[PROTOKOLLFEHLER] " + ex.Message + " / RAM-Ausgabe bleibt; Folgewartung stoppt, aktuellen Prozess geordnet abwarten.\n");
                    }
                }
                Write(text);
            })));
        if (logError is not null) throw new IOException("Live.log nicht vollstaendig geschrieben; Teilaenderungen moeglich.", logError);
        if (result.ExitCode != 0) throw new InvalidOperationException($"Exit {result.ExitCode}: {result.Error}. Teilaenderungen moeglich; Live-Protokoll beachten.");
        ActionJournal.Append("operations-command-ended", new { target, report, result.ExitCode });
        return result;
    }
    private void LoadUpdates(string json)
    {
        var updates = JsonSerializer.Deserialize<List<AvailableUpdate>>(json) ?? throw new InvalidDataException("Updateabfrage lieferte kein Listenformat.");
        if (updates.Count > 2000 || updates.Any(u => u is null || !MaintenancePlans.ValidUpdateId(u.Id) || u.Title is null || u.EulaText is null ||
            u.SizeMiB < 0 || !double.IsFinite(u.SizeMiB)) || updates.Select(u => u.Id).Distinct().Count() != updates.Count)
            throw new InvalidDataException("Ungueltiges Updateformat; keine Installation moeglich.");
        _updates.Clear();
        foreach (var update in updates) _updates.Add(new(update));
        LicenseConsent.IsChecked = false;
        Write($"[UPDATEAUSWAHL] {updates.Count} Updates / KEINES vorangekreuzt / EULA je Zeile lesen.\n");
    }
    private async void UpdateSearchClicked(object sender, RoutedEventArgs e) => await RunAsync("Windows-Updatesuche / konfigurierte Microsoft-/WSUS-Quelle", async token =>
    {
        SystemActions.RequireAdmin();
        if (!Confirm("Windows-Updatequelle laut bestehender Richtlinie kontaktieren und Softwareupdates suchen? Keine Installation."))
            throw new OperationCanceledException();
        _updates.Clear();
        LicenseConsent.IsChecked = false;
        LoadUpdates((await Execute(MaintenancePlans.SearchUpdatesScript, token)).Output);
    });
    private async void IndexReadClicked(object sender, RoutedEventArgs e) => await RunAsync("Gespeicherten WinSAT-Index lesen", ReadIndex);
    private async Task ReadIndex(CancellationToken token)
    {
        Scores.ItemsSource = null;
        IndexStatus.Text = "WinSAT wird gelesen; keine Bewertung angenommen.";
        var index = await PerformanceIndex.ReadAsync(token);
        IndexStatus.Text = index.StateText + " / DataStore-Dateizeit (nicht garantiert Messzeit): " + (index.StoredAt ?? "nicht verfuegbar") + "\nBasisscore ist der von Windows gemeldete Legacy-Wert, keine neue Gesamtnote.";
        Scores.ItemsSource = new (string Title, double? Value)[]
        {
            ("WINDOWS / BASIS", index.Base), ("CPU", index.Cpu), ("SPEICHER", index.Memory),
            ("DESKTOPGRAFIK", index.Graphics), ("SYSTEMDISK / SEQ. LESEN", index.Disk), ("GAMING / NICHT NEU BEWERTET", null)
        }.Select(item =>
        {
            var graph = new DashboardGraph(item.Title, "Index", "#39FF68");
            graph.Update(item.Value, index.Valid ? "Windows-Legacy-Index / keine FPS" : index.StateText, 9.9);
            return graph;
        }).ToList();
    }
    private async void IndexMeasureClicked(object sender, RoutedEventArgs e) => await RunAsync("WinSAT-Messung", async token =>
    {
        SystemActions.RequireAdmin();
        if (!Confirm("Vorschau: WinSAT.exe formal\nBelastet CPU, RAM, Grafik und Systemdatentraeger, kann Bildschirmdarstellung beeinflussen. Anwendungen vorher speichern/schliessen. Windows schreibt Bewertungsdateien. KEINE automatische Messung beim Boot.\n\nJetzt starten?"))
            throw new OperationCanceledException();
        var run = CreateRun("winsat");
        await Execute(PerformanceIndex.BenchmarkScript, token, run);
        await ReadIndex(token);
        Write("[BERICHT] " + run + "\n");
    });
    private void CleanerClicked(object sender, RoutedEventArgs e) => CleanerRequested?.Invoke(this, EventArgs.Empty);
    private void AddDeviceClicked(object sender, RoutedEventArgs e) => RunAsync("Geraeteziel speichern", _ =>
    {
        var target = new ManagedDevice(Host.Text, UseHttps.IsChecked == true);
        if (_devices.Any(d => d.Host.Equals(target.Host, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Ziel bereits eingetragen.");
        if (!target.Https && !Confirm("HTTP/Negotiate in Arbeitsgruppen kann TrustedHosts voraussetzen. AETHER aendert diese NICHT. HTTPS ist bevorzugt. HTTP-Ziel bewusst eintragen?"))
            throw new OperationCanceledException();
        ManagedDevices.Save(Path.Combine(_root, "targets.json"), _devices.Append(target));
        _devices.Add(target);
        Host.Clear();
        return Task.CompletedTask;
    });
    private void RemoveDeviceClicked(object sender, RoutedEventArgs e) => RunAsync("Ziel entfernen", _ =>
    {
        if (Devices.SelectedItem is not ManagedDevice device) throw new ArgumentException("Zielzeile markieren.");
        if (!Confirm($"Nur {device.Host} aus AETHER entfernen? Geraet selbst bleibt unveraendert.")) throw new OperationCanceledException();
        ManagedDevices.Save(Path.Combine(_root, "targets.json"), _devices.Where(d => d != device));
        _devices.Remove(device);
        return Task.CompletedTask;
    });
    private List<ManagedDevice> SelectedDevices()
    {
        var targets = _devices.Where(d => d.Selected).ToList();
        if (targets.Count == 0) throw new ArgumentException("Mindestens ein explizites Ziel ankreuzen.");
        return targets;
    }
    private void DevicePreviewClicked(object sender, RoutedEventArgs e) => RunAsync("Zielvorschau", _ =>
    {
        foreach (var target in SelectedDevices()) Write($"[VORSCHAU / KEIN NETZZUGRIFF] {target.Host} / {target.Transport}\n{ManagedDevices.DiagnosticScript(target)}\n");
        return Task.CompletedTask;
    });
    private async void DeviceRunClicked(object sender, RoutedEventArgs e) => await RunAsync("Geraetediagnose", async token =>
    {
        var targets = SelectedDevices();
        if (!Confirm("Nur diese Ziele kontaktieren?\n" + string.Join("\n", targets.Select(t => t.Host + " / " + t.Transport)) +
            "\nAktuelles Admin-Konto / Negotiate. Keine Aenderungen, keine WinRM-Einrichtung. Erfasste Inventar-/Ereignisdaten werden lokal protokolliert."))
            throw new OperationCanceledException();
        var run = CreateRun("devices");
        Save(run, "Targets.json", targets.Select(t => new { t.Host, t.Transport }));
        var results = new List<MaintenanceResult>();
        PlanProgress.Maximum = targets.Count;
        PlanProgress.Value = 0;
        var failures = 0;
        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();
            var start = DateTimeOffset.Now;
            target.Status = "Diagnose laeuft / " + start.ToString("HH:mm:ss");
            try
            {
                var result = await Execute(ManagedDevices.DiagnosticScript(target), token, run, target.Host);
                target.Status = "Diagnose gelesen / kein Gesundheitssiegel / " + DateTime.Now.ToString("HH:mm:ss");
                results.Add(new(target.Host, "DIAGNOSE GELESEN", result.Output, start, DateTimeOffset.Now));
            }
            catch (OperationCanceledException)
            {
                target.Status = "Diagnose abgebrochen";
                results.Add(new(target.Host, "ABGEBROCHEN", "Verbindung/Abfrage abgebrochen; keine Aenderungsbefehle", start, DateTimeOffset.Now));
                foreach (var remaining in targets.SkipWhile(t => t != target).Skip(1))
                {
                    remaining.Status = "Nicht gestartet / Lauf abgebrochen";
                    results.Add(new(remaining.Host, "AUSGELASSEN", "Lauf abgebrochen", DateTimeOffset.Now, DateTimeOffset.Now));
                }
                SaveResults(run, results);
                throw;
            }
            catch (Exception ex) when (OperationError(ex) && ex is not OperationCanceledException)
            {
                failures++;
                target.Status = "Fehler: " + ex.Message;
                results.Add(new(target.Host, "FEHLER", ex.Message, start, DateTimeOffset.Now));
                Write("[ZIELFEHLER] " + target.Host + " / " + ex.Message + "\n");
            }
            SaveResults(run, results);
        }
        Write("[GERAETEBERICHT] " + run + "\n");
        if (failures > 0) throw new InvalidOperationException($"{failures} / {targets.Count} Ziele fehlgeschlagen; keine Erfolgsvortaeuschung. Einzelstatus/Berichte lesen.");
    });
    private async void CloudReadClicked(object sender, RoutedEventArgs e) => await RunAsync("Lokale M365-/Server-Voraussetzungen", async token => { await Execute(ManagedDevices.CloudReadinessScript, token); });
    private void ReportsClicked(object sender, RoutedEventArgs e) => RunAsync("Berichtsordner", _ =>
    {
        var folder = Path.Combine(_root, "Reports");
        Directory.CreateDirectory(folder);
        using var process = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        return Task.CompletedTask;
    });
    private bool Confirm(string text) => MessageBox.Show(Window.GetWindow(this), text, "AETHER / bewusste Freigabe", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void Write(string text)
    {
        _lastOutput = DateTimeOffset.Now;
        if (Console.Text.Length + text.Length > 1_000_000) Console.Text = Console.Text[^Math.Min(500_000, Console.Text.Length)..] + "\n[ANZEIGE GEKUERZT / vollstaendiges Live.log bei Berichtslaeufen]\n";
        Console.AppendText(text);
        Console.ScrollToEnd();
    }
    private Task RunAsync(string label, Func<CancellationToken, Task> action)
    {
        if (_stop is not null || _shutdown) { Status.Text = "Laufende Aktion zuerst beenden / Fenster schliesst."; return Task.CompletedTask; }
        _pending = ExecuteOperation();
        return _pending;
        async Task ExecuteOperation()
        {
            _stop = new();
            _label = label;
            _elapsed.Restart();
            _lastOutput = DateTimeOffset.Now;
            _timer.Start();
            MaintenanceInputs.IsEnabled = DeviceInputs.IsEnabled = false;
            Tabs.IsEnabled = false;
            Status.Text = label + " laeuft ...";
            try { await action(_stop.Token); Status.Text = label + " beendet. Befunde/Ausgaben pruefen."; }
            catch (OperationCanceledException) { Status.Text = "Nicht freigegeben / abgebrochen; Teilaenderungen moeglich."; Write("[ABBRUCH]\n"); }
            catch (Exception ex) when (OperationError(ex))
            {
                Status.Text = label + ": " + ex.Message;
                Write("[FEHLER] " + ex.Message + "\n");
                Trace.TraceError("{0}: {1}", label, ex);
            }
            finally
            {
                _maintenance = false;
                _timer.Stop();
                _elapsed.Stop();
                _stop.Dispose(); _stop = null;
                MaintenanceInputs.IsEnabled = DeviceInputs.IsEnabled = Tabs.IsEnabled = true;
            }
        }
    }
    private static bool OperationError(Exception ex) => ex is IOException or InvalidOperationException or ArgumentException or
        UnauthorizedAccessException or JsonException or TimeoutException or System.ComponentModel.Win32Exception or
        System.Security.SecurityException or NotSupportedException or OperationCanceledException;
    private sealed class DirectProgress(Action<string> report) : IProgress<string> { public void Report(string text) => report(text); }
}
