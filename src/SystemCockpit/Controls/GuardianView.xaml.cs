using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public partial class GuardianView : UserControl
{
    private static readonly JsonSerializerOptions Display = new(IntelligenceModel.Json)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    internal sealed record Row(GuardianArtifact Artifact)
    {
        public string Kind => Artifact.Kind switch
        {
            RecoveryKind.RecoveryPoint => "Zustandsaufnahme",
            RecoveryKind.SoftwareManifest => "Softwarebestand",
            RecoveryKind.InstallationObservation => "Installationsvergleich",
            RecoveryKind.UpdateHistory => "Updateverlauf",
            RecoveryKind.DriverState => "Treiberstatus",
            RecoveryKind.CrashCase => "Absturz-/Hangfall",
            RecoveryKind.DiagnosticEvidence => "Diagnosebeleg",
            RecoveryKind.Maintenance => "Wartungsablauf",
            RecoveryKind.RescuePackage => "Wiederherstellungspaket",
            RecoveryKind.Transaction => "Änderungsvorgang",
            RecoveryKind.ConfigurationBackup => "Konfigurationssicherung",
            RecoveryKind.RollbackManifest => "Rückwegbeleg",
            _ => "Guardian-Eintrag"
        };
        public string Name => DisplayArtifactName(Artifact);
        public DateTimeOffset Created => Artifact.Created;
        public string State => DisplayState(Artifact.Transaction?.State.ToString() ??
            Artifact.Fields.GetValueOrDefault("state") ?? (Artifact.Pinned ? "Pinned" : "Documented"));
    }
    private readonly GuardianVault _vault;
    private readonly GuardianActions _actions = new();
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _work;
    private Task _pending = Task.CompletedTask;
    private GuardianCore? _core;
    private GuardianTransaction? _planned;
    private bool _shutdown;
    private bool _isRunning;
    private IReadOnlyList<GuardianArtifact> _artifacts = [];
    private int? _openOperationCount;
    private DateTimeOffset? _lastCheck;
    internal bool Synthetic { get; set; }
    internal Func<CancellationToken, Task<IntelligenceSnapshot>>? Capture { get; set; }
    internal Func<IReadOnlyList<TimelineEntry>>? Timeline { get; set; }
    internal Func<ProcessHistory>? ProcessHistory { get; set; }
    internal Func<IReadOnlyList<DiagnosisRecording>>? Recordings { get; set; }
    internal Func<GuardianConfigurationData>? Configuration { get; set; }
    internal GuardianConfigurationAction? ConfigurationAction { get => _actions.Configuration; set => _actions.Configuration = value; }
    internal Action<TimelineEntry>? Mark { get; set; }
    internal IReadOnlyList<GuardianArtifact> Artifacts => _artifacts;
    internal Task Pending => _pending;
    private GuardianContext Context => Synthetic ? new("synthetic", "synthetic") : IntelligenceSources.GuardianContext();
    public GuardianView() : this(GuardianVault.DefaultDirectory) { }
    internal GuardianView(string root)
    {
        _vault = new(root); InitializeComponent();
        TraceProfile.ItemsSource = GuardianTraceProfiles.All; TraceProfile.SelectedIndex = 0;
        IsVisibleChanged += (_, _) => { if (IsVisible && !_shutdown) Render(); };
    }
    private GuardianCore Core
    {
        get
        {
            if (_core is not null) return _core;
            _core = new(_vault, () => Context);
            return _core;
        }
    }
    private void Emit(string category, string title, string detail, Guid id)
    {
        var now = DateTimeOffset.UtcNow;
        Mark?.Invoke(new($"guardian:{id:D}:{category}:{Guid.NewGuid():N}", now, now, category, title,
            "guardian:" + id.ToString("D"), IntelligenceModel.Metadata("AETHER Guardian / lokaler Vorgang", now), detail));
    }
    private void Run(Func<CancellationToken, Task> action)
    {
        if (_shutdown) return;
        if (_isRunning)
        {
            Status.Text = "Guardian arbeitet. Abbrechen beendet die eigene Arbeit kontrolliert.";
            UpdateContextActions();
            return;
        }
        _work?.Dispose(); _work = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _isRunning = true;
        _pending = RunAsync(action, _work.Token);
        UpdateContextActions();
    }
    private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        try { await action(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { Status.Text = "Abgebrochen. Bereits erfolgte Systemwirkungen bleiben im Journal; kein automatischer Rückweg."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or
            ArgumentException or Win32Exception or TimeoutException or System.Security.SecurityException or CryptographicException)
        {
            Trace.TraceError("Guardian: {0}", ex);
            Status.Text = "Der Guardian-Vorgang ist fehlgeschlagen. Technische Einzelheiten sind im Detailbereich gekennzeichnet.";
            var technicalDetails = ex is GuardianJournalException failure
                ? JsonSerializer.Serialize(failure.Transaction, new JsonSerializerOptions(IntelligenceModel.Json) { WriteIndented = true })
                : ex.GetType().Name + Environment.NewLine + ex.Message;
            SetDetails(technicalDetails, "Technische Fehlerdetails des fehlgeschlagenen Guardian-Vorgangs.");
        }
        finally
        {
            _isRunning = false;
            UpdateContextActions();
        }
    }
    private static string DisplayState(string state) => state switch
    {
        "Planned" => "Geplant",
        "Preflight" => "Vorprüfung",
        "Ready" => "Bereit",
        "Protected" => "Gesperrt",
        "Reserved" => "Vorgemerkt",
        "Executing" => "Wird ausgeführt",
        "Verifying" => "Wird geprüft",
        "Completed" or "Closed" => "Abgeschlossen",
        "Partial" => "Teilweise abgeschlossen",
        "Failed" => "Fehlgeschlagen",
        "Cancelled" => "Abgebrochen",
        "RollbackAvailable" => "Rückweg verfügbar",
        "RollingBack" => "Rückweg wird ausgeführt",
        "RolledBack" => "Rückweg abgeschlossen",
        "RollbackPartial" => "Rückweg teilweise abgeschlossen",
        "RollbackFailed" => "Rückweg fehlgeschlagen",
        "NotReversible" => "Nicht rückgängig zu machen",
        "Paused" => "Pausiert",
        "Observing" => "Vorherzustand gespeichert",
        "Pinned" => "Offen / geschützt",
        "Documented" => "Dokumentiert",
        _ => "Nicht eingeordnet"
    };
    private static string DisplayArtifactName(GuardianArtifact artifact)
    {
        var name = artifact.Name;
        if (name == "Driver Guard — belegter Treiberzustand") return "Treiberstatus – belegter Ausgangszustand";
        if (name == "Update Intelligence") return "Updateverlauf";
        if (name == "Windows Recovery — Status") return "Windows-Wiederherstellungsstatus";
        if (name == "Rescue Package — geprüfter Export") return "Wiederherstellungspaket – Exportnachweis";
        if (name == "Installation beobachten") return "Installationsvergleich – Vorherzustand";
        if (name == "Beobachtungszeitraum abgeschlossen") return "Installationsvergleich – Nachherzustand";
        if (name.StartsWith("Crash Lab — ", StringComparison.Ordinal))
            return "Absturz-/Hangfall – " + name["Crash Lab — ".Length..]
                .Replace("ApplicationCrash", "Anwendungsabsturz", StringComparison.Ordinal)
                .Replace("ApplicationHang", "Anwendung hängt", StringComparison.Ordinal);
        const string rawTrace = "Deep Trace — Rohaufnahme / ";
        if (name.StartsWith(rawTrace, StringComparison.Ordinal))
        {
            var profile = GuardianTraceProfiles.All.SingleOrDefault(p => p.Id == name[rawTrace.Length..]);
            return "ETW-Rohaufnahme – " + (profile?.Name ?? "Profil");
        }
        const string trace = "Deep Trace — ";
        if (name.StartsWith(trace, StringComparison.Ordinal)) return "ETW-Tiefendiagnose – " + name[trace.Length..];
        return name;
    }
    private void SetDetails(string text, string? summary = null, bool expand = true)
    {
        Details.Text = text;
        if (summary is not null) DetailsSummary.Text = summary;
        DetailsExpander.IsExpanded = expand;
    }
    private GuardianArtifact Selection => (Rows.SelectedItem as Row)?.Artifact ?? throw new InvalidOperationException("Zuerst einen passenden Vault-Eintrag auswählen.");
    private async Task<IntelligenceSnapshot> ReadAsync(CancellationToken token) =>
        Capture is null ? throw new InvalidOperationException("Gemeinsame Intelligence-Erfassung ist noch nicht verbunden.") : await Capture(token);
    private async Task LoadAsync(CancellationToken token)
    {
        await RefreshIndexAsync(token);
        var readiness = GuardianVault.Readiness(_artifacts, Context);
        var storedSources = Synthetic ? [] : await GuardianReadiness.StoredSourcesAsync(Context, token);
        var unfinished = _artifacts.Count(a => a.Transaction?.InFlight == true || a.Kind == RecoveryKind.Maintenance && a.Pinned);
        _openOperationCount = unfinished;
        _lastCheck = DateTimeOffset.UtcNow;
        Status.Text = $"{_artifacts.Count} geprüfte Vault-Objekte. {unfinished} offene Vorgänge: analysieren, bewusst fortsetzen oder abbrechen; keine automatische Wiederholung.";
        if (_vault.Issues.Count != 0) Status.Text += " Quarantäne: " + string.Join(" | ", _vault.Issues);
        var details = string.Join("\n\n", readiness.Select(r => $"{r.Area}: {r.State}\n{r.Evidence}\n{r.Limitation}"));
        if (storedSources.Count > 0) details += "\n\nVORHANDENE QUELLEN (KEIN UNABHÄNGIGES BACKUP)\n" +
            string.Join("\n", storedSources.Select(r => $"{r.Area}: {r.State} / {r.Evidence} / {r.Limitation}"));
        SetDetails(details, "Wiederherstellungsstatus und Umfang der lokal erfassten Quellen. Ein Systemzustand ist kein Dateibackup.", false);
        Render();
    }
    internal async Task RefreshIndexAsync(CancellationToken token)
    {
        if (_shutdown) throw new InvalidOperationException("Guardian ist beendet.");
        if (!Synthetic) _artifacts = await _vault.LoadAsync(token);
        foreach (var tx in _artifacts.Where(a => a.Context == Context).Select(a => a.Transaction).OfType<GuardianTransaction>())
            ProjectJournal(tx);
        foreach (var artifact in _artifacts.Where(a => a.Context == Context))
            foreach (var entry in GuardianTraceProjection.Timeline(artifact)) Mark?.Invoke(entry);
    }
    private async Task SaveAsync(GuardianArtifact artifact, CancellationToken token)
    {
        await _vault.SaveAsync(artifact, token);
        Emit("Guardian" + artifact.Kind, artifact.Name, artifact.Summary, artifact.Id);
        await LoadAsync(token);
        Rows.SelectedItem = Rows.Items.Cast<Row>().FirstOrDefault(r => r.Artifact.Id == artifact.Id);
    }
    private void Render()
    {
        if (Rows is null || Search is null || Area is null || MaintenanceTools is null) return;
        var selected = (Area.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var recoveryVisible = selected is "Recovery Vault" or "Recovery Readiness" or "Softwaremanifest" or
            "Installation Observer" or "Update Intelligence" or "Driver Guard" ? Visibility.Visible : Visibility.Collapsed;
        RecoveryTools.Visibility = recoveryVisible;
        RecoveryToolsHeader.Visibility = recoveryVisible;
        var actionVisible = selected is "Change Journal" or "Rollback Center" ? Visibility.Visible : Visibility.Collapsed;
        ActionTools.Visibility = actionVisible;
        ActionToolsHeader.Visibility = actionVisible;
        var diagnosticVisible = selected is "Crash Lab" or "Deep Trace" or "Rescue Package" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticTools.Visibility = diagnosticVisible;
        DiagnosticToolsHeader.Visibility = diagnosticVisible;
        CrashTools.Visibility = selected == "Crash Lab" ? Visibility.Visible : Visibility.Collapsed;
        TraceTools.Visibility = selected == "Deep Trace" ? Visibility.Visible : Visibility.Collapsed;
        RescueTools.Visibility = selected == "Rescue Package" ? Visibility.Visible : Visibility.Collapsed;
        var maintenanceVisible = selected == "Maintenance Window" ? Visibility.Visible : Visibility.Collapsed;
        MaintenanceTools.Visibility = maintenanceVisible;
        MaintenanceToolsHeader.Visibility = maintenanceVisible;
        var kind = selected switch
        {
            "Change Journal" or "Rollback Center" => RecoveryKind.Transaction,
            "Softwaremanifest" => RecoveryKind.SoftwareManifest,
            "Installation Observer" => RecoveryKind.InstallationObservation,
            "Update Intelligence" => RecoveryKind.UpdateHistory,
            "Driver Guard" => RecoveryKind.DriverState,
            "Crash Lab" => RecoveryKind.CrashCase,
            "Deep Trace" => RecoveryKind.DiagnosticEvidence,
            "Maintenance Window" => RecoveryKind.Maintenance,
            "Rescue Package" => RecoveryKind.RescuePackage,
            _ => (RecoveryKind?)null
        };
        var rows = _artifacts.Where(a => (kind is null || a.Kind == kind) &&
            (a.Name + " " + a.Id + " " + a.Summary).Contains(Search.Text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Created).Select(a => new Row(a)).ToArray();
        Rows.ItemsSource = rows;
        Rows.Visibility = rows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Text = Search.Text.Length > 0 ? "Keine passenden Guardian-Vorgänge." :
            _lastCheck is null ? "Vorgänge noch nicht geladen. Mit „Vorgang vorbereiten“ lokal prüfen." :
            kind is null ? "Keine offenen Guardian-Vorgänge." : "Keine Vorgänge in diesem Guardian-Bereich.";
        Crash.ItemsSource = Timeline?.Invoke().Where(t => t.Category is "ApplicationCrash" or "ApplicationHang")
            .OrderByDescending(t => t.Occurred).Take(128).ToArray();
        UpdateContextActions();
    }
    private void FilterChanged(object sender, RoutedEventArgs e) => Render();
    private void Selected(object sender, SelectionChangedEventArgs e)
    {
        if (Rows.SelectedItem is Row row)
            SetDetails(JsonSerializer.Serialize(row.Artifact, Display),
                $"{row.Name}: {row.Artifact.Summary}", false);
        UpdateContextActions();
    }
    private void CancelClicked(object sender, RoutedEventArgs e) => _work?.Cancel();
    private void TargetChanged(object sender, TextChangedEventArgs e) => UpdateContextActions();
    private void PrepareClicked(object sender, RoutedEventArgs e)
    {
        var command = PrepareCommand();
        if (command is null)
        {
            Status.Text = "Für diese Aktion zuerst die passende Auswahl oder Zielidentität bereitstellen.";
            return;
        }
        Run(token => CommandAsync(command, token));
    }
    private void OpenSelected(object sender, RoutedEventArgs e)
    {
        if (Rows.SelectedItem is not Row row) return;
        SetDetails(JsonSerializer.Serialize(row.Artifact, Display), $"{row.Name}: {row.Artifact.Summary}");
        UpdateContextActions();
    }
    private void OpenRowClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Row row }) return;
        Rows.SelectedItem = row;
        OpenSelected(sender, e);
    }
    private string? PrepareCommand()
    {
        var selected = (Area.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var artifact = (Rows.SelectedItem as Row)?.Artifact;
        return selected switch
        {
            "Recovery Vault" or "Recovery Readiness" => "load",
            "Change Journal" when !string.IsNullOrWhiteSpace(TargetId.Text) => "plan",
            "Rollback Center" when CanPrepareRollback(artifact?.Transaction) => "rollback",
            "Softwaremanifest" => "manifest",
            "Installation Observer" => "observe-start",
            "Update Intelligence" => "updates",
            "Driver Guard" => "drivers",
            "Crash Lab" when Crash.SelectedItem is TimelineEntry => "crash",
            "Deep Trace" when TraceProfile.SelectedItem is GuardianTraceProfile => "trace-profile",
            "Maintenance Window" when _planned?.Plan is not null => "maintenance",
            "Rescue Package" when artifact?.Kind == RecoveryKind.RescuePackage => "inspect-private",
            _ => null
        };
    }
    private bool CanPrepareRollback(GuardianTransaction? transaction) =>
        transaction is not null && transaction.Context == Context &&
        (transaction.State is ChangeState.Completed or ChangeState.Partial) &&
        transaction.After is not null &&
        (transaction.Plan.Rollback is RollbackCapability.Available or RollbackCapability.Partial) &&
        transaction.Plan.Action != "restart";
    private void UpdateContextActions()
    {
        if (PrepareButton is null || Rows is null) return;
        var area = (Area.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var selected = (Rows.SelectedItem as Row)?.Artifact;
        var prepare = PrepareCommand();
        PrepareButton.IsEnabled = prepare is not null;
        OperationHint.Text = area switch
        {
            "Recovery Vault" or "Recovery Readiness" => "Lädt lokale Guardian-Daten und den belegten Wiederherstellungsstatus. Keine Systemänderung.",
            "Change Journal" => "Erstellt eine Vorschau für das angegebene Ziel. Ohne Zielidentität bleibt die Aktion gesperrt.",
            "Rollback Center" => "Bereitet nur für eine ausgewählte Transaktion einen geprüften Rückweg vor.",
            "Softwaremanifest" => "Erfasst lokale, belegte Softwaredaten; eine Installation wird nicht gestartet.",
            "Installation Observer" => "Speichert den Vorherzustand für einen späteren, getrennt gestarteten Vergleich.",
            "Update Intelligence" => "Liest die vorhandene Updatehistorie ohne Suche oder Installation.",
            "Driver Guard" => "Erfasst den belegten Treiberzustand. Installiert oder sichert keinen Treiber.",
            "Crash Lab" => "Wertet nur einen ausgewählten qualifizierten Absturz- oder Hangfall aus.",
            "Deep Trace" => "Zeigt Profilgrenzen; eine reale ETW-Aufzeichnung bleibt gesperrt.",
            "Maintenance Window" => "Ein Wartungsablauf benötigt zuerst einen angezeigten Änderungsplan.",
            "Rescue Package" => "Paketprüfung ist erst nach Auswahl eines vorhandenen Guardian-Paketnachweises verfügbar.",
            _ => "Vorhandene lokale Guardian-Daten werden nur auf ausdrückliche Aktion gelesen."
        };
        StatusState.Text = _isRunning ? "Vorgang läuft" :
            _lastCheck is null ? "Noch nicht geprüft" : "Bereit";
        OpenCount.Text = _openOperationCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        RecoveryPointCount.Text = _lastCheck is null ? "—" :
            _artifacts.Count(a => a.Kind == RecoveryKind.RecoveryPoint)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        LastCheck.Text = _lastCheck?.ToLocalTime().ToString("dd.MM.yyyy HH:mm",
            System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        OpenButton.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = _isRunning ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
        DeleteButton.IsEnabled = selected is not null && selected.Transaction?.InFlight != true;
        PlanButton.IsEnabled = !string.IsNullOrWhiteSpace(TargetId.Text);
        PackageManifestButton.Visibility = PackageCenter.LastObserved is null
            ? Visibility.Collapsed : Visibility.Visible;
        ObservationCompleteButton.Visibility = selected?.Kind == RecoveryKind.InstallationObservation && selected.Pinned
            ? Visibility.Visible : Visibility.Collapsed;
        DriverVerifyButton.Visibility = selected?.Kind == RecoveryKind.DriverState
            ? Visibility.Visible : Visibility.Collapsed;
        ConfigurationPlanButton.Visibility = selected?.Kind == RecoveryKind.ConfigurationBackup
            ? Visibility.Visible : Visibility.Collapsed;
        var transactionSelected = selected?.Transaction is { } transaction && transaction.Context == Context;
        RollbackButton.Visibility = transactionSelected && CanPrepareRollback(selected?.Transaction) &&
            (area is "Change Journal" or "Rollback Center")
            ? Visibility.Visible : Visibility.Collapsed;
        RevalidateButton.Visibility = transactionSelected && (area is "Change Journal" or "Rollback Center")
            ? Visibility.Visible : Visibility.Collapsed;
        ExecuteButton.Visibility = _planned is not null && (area is "Change Journal" or "Rollback Center")
            ? Visibility.Visible : Visibility.Collapsed;
        CrashEvaluateButton.Visibility = area == "Crash Lab" && Crash.SelectedItem is TimelineEntry
            ? Visibility.Visible : Visibility.Collapsed;
        TraceProfileDetailsButton.Visibility = area == "Deep Trace" && TraceProfile.SelectedItem is GuardianTraceProfile
            ? Visibility.Visible : Visibility.Collapsed;
        var traceSelected = area == "Deep Trace" && selected?.Kind == RecoveryKind.DiagnosticEvidence &&
            (selected.TraceResult is not null || selected.Fields.ContainsKey("trace"));
        TraceDecodeButton.Visibility = traceSelected ? Visibility.Visible : Visibility.Collapsed;
        TraceDeleteButton.Visibility = traceSelected ? Visibility.Visible : Visibility.Collapsed;
        InspectPrivateButton.Visibility = area == "Rescue Package" && selected?.Kind == RecoveryKind.RescuePackage
            ? Visibility.Visible : Visibility.Collapsed;
        var maintenanceSelected = area == "Maintenance Window" && selected?.Kind == RecoveryKind.Maintenance;
        MaintenanceAddButton.Visibility = maintenanceSelected && selected?.Pinned == true && _planned?.Plan is not null
            ? Visibility.Visible : Visibility.Collapsed;
        var maintenanceState = selected?.Fields.GetValueOrDefault("state");
        MaintenanceCreateButton.Visibility = _planned?.Plan is not null ? Visibility.Visible : Visibility.Collapsed;
        MaintenanceNextButton.Visibility = maintenanceSelected && maintenanceState == "Reanalysed"
            ? Visibility.Visible : Visibility.Collapsed;
        MaintenancePauseButton.Visibility = maintenanceSelected && selected?.Pinned == true && maintenanceState != "Paused"
            ? Visibility.Visible : Visibility.Collapsed;
        MaintenanceResumeButton.Visibility = maintenanceSelected && maintenanceState == "Paused"
            ? Visibility.Visible : Visibility.Collapsed;
        MaintenanceVerifyButton.Visibility = maintenanceSelected && selected?.Pinned == true
            ? Visibility.Visible : Visibility.Collapsed;
        MaintenanceCancelButton.Visibility = maintenanceSelected && selected?.Pinned == true
            ? Visibility.Visible : Visibility.Collapsed;
    }
    private void KeyPressed(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) { _work?.Cancel(); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Enter && Rows.IsKeyboardFocusWithin && Rows.SelectedItem is Row)
        { Details.Focus(); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.F && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        { Search.Focus(); e.Handled = true; }
    }
    private void CommandClicked(object sender, RoutedEventArgs e)
    {
        var command = (string)((Button)sender).Tag;
        Run(token => CommandAsync(command, token));
    }
    internal async Task CommandAsync(string command, CancellationToken token)
    {
        if (command == "load") { await LoadAsync(token); return; }
        if (command == "package-manifest")
        {
            await SaveAsync(GuardianWorkflows.PackageManifest(PackageCenter.LastObserved ??
                throw new InvalidOperationException("Zuerst im vorhandenen Paketbereich WinGet-Liste oder Updateangebot manuell lesen."), Context), token); return;
        }
        if (command == "configuration-backup")
        {
            var data = Configuration?.Invoke() ?? throw new InvalidOperationException("Konfigurationsquelle nicht verbunden.");
            await SaveAsync(GuardianConfiguration.Backup(data, Context), token); return;
        }
        if (command == "configuration-plan")
        {
            var backup = Selection;
            if (backup.Kind != RecoveryKind.ConfigurationBackup || backup.Context != Context)
                throw new InvalidOperationException("Passende Konfigurationssicherung dieses Hosts und Benutzers auswählen.");
            var adapter = ConfigurationAction ?? throw new InvalidOperationException("Konfigurationswiederherstellung nicht verbunden.");
            var target = adapter.Target(Context, backup.Fields["payload"]);
            _planned = Core.Plan("AETHER-Konfiguration wiederherstellen", GuardianConfigurationAction.Plan(target));
            UpdateContextActions();
            SetDetails(JsonSerializer.Serialize(_planned, Display), "Konfigurationsrückweg geplant. Vor der Ausführung erneut prüfen und bestätigen.");
            Status.Text = "Wiederherstellungsplan lesen und separat bestätigen."; return;
        }
        if (command == "delete")
        {
            var selected = Selection;
            if (!Confirm("Vault-Eintrag löschen", "Nur diesen lokalen Bestand löschen? Aktive Vorgänge und Abhängigkeiten bleiben geschützt.\n" + selected.Name)) return;
            await _vault.DeleteAsync(selected.Id, token); await LoadAsync(token); return;
        }
        if (command == "plan")
        {
            if (Synthetic) throw new InvalidOperationException("Synthetische Oberfläche verändert keine Windows-Ziele.");
            var kind = ((ComboBoxItem)TargetKind.SelectedItem).Tag?.ToString() ??
                throw new InvalidOperationException("Zieltyp auswählen.");
            var action = ((ComboBoxItem)Action.SelectedItem).Tag?.ToString() ??
                throw new InvalidOperationException("Aktion auswählen.");
            var actual = await _actions.InspectAsync(kind, TargetId.Text.Trim(), token);
            _planned = Core.Plan("Guardian: " + actual.Id, GuardianActions.MakePlan(actual, action));
            UpdateContextActions();
            Emit("GuardianPlan", _planned.Title, "Änderungsplan angezeigt; keine Systemänderung und noch keine Ausführungsfreigabe.", _planned.Id);
            SetDetails(JsonSerializer.Serialize(_planned, Display), "Änderungsvorschau; das Ziel wurde noch nicht geändert.");
            Status.Text = "Plan gelesen; keine Änderung. Vor Ausführung vollständig prüfen und separat bestätigen."; return;
        }
        if (command == "rollback")
        {
            _planned = Core.Rollback(Selection.Transaction ?? throw new InvalidOperationException("Transaktion auswählen."));
            UpdateContextActions();
            SetDetails(JsonSerializer.Serialize(_planned, Display), "Rückweg geplant; Identität und Zustand werden vor der Ausführung erneut geprüft.");
            Status.Text = "Rückweg geplant. Erneute Bestätigung, Identitäts- und Zustandsprüfung sind erforderlich."; return;
        }
        if (command == "revalidate")
        {
            var tx = Selection.Transaction ?? throw new InvalidOperationException("Offene Transaktion auswählen.");
            if (tx.Context != Context) throw new InvalidOperationException("Rechner-/Benutzerkontext hat sich geändert.");
            var actual = await ActionFor(tx.Plan).ReadAsync(tx.Plan.Before, token);
            SetDetails(JsonSerializer.Serialize(new
            {
                Transaction = tx.Id,
                Current = actual,
                IdentityMatches = actual.Fingerprint == tx.Plan.Before.Fingerprint,
                AutomaticReplay = false
            }, IntelligenceModel.Json), "Aktueller Zielzustand und Identitätsvergleich. Keine Aktion wurde wiederholt.");
            Status.Text = "Tatsächlicher Zustand neu gelesen. Keine alte Aktion fortgesetzt; für weitere Änderungen einen neuen Plan erstellen."; return;
        }
        if (command == "updates") { await SaveAsync(await GuardianWorkflows.UpdatesAsync(Context, token), token); return; }
        if (command == "trace-profile")
        {
            var profile = SelectedTraceProfile;
            var custom = profile.Id == "custom" ? GuardianTraceProfiles.CustomPlan(SelectedTraceComponents) : null;
            SetDetails(JsonSerializer.Serialize(new
            {
                Profile = profile,
                profile.Rights,
                profile.Privacy,
                profile.Filters,
                profile.Stop,
                profile.Analysis,
                profile.Released,
                Providers = GuardianTraceProfiles.Resolve(profile, custom?.Effective ?? SelectedTraceComponents),
                Qualification = profile.Qualification,
                Custom = custom,
                DecoderBudget = "64 MiB je XML/EVTX/gerendertem XML; 30 Sekunden insgesamt",
                Gate = "Realer Prüfstand profilweise; ProductionReleased bleibt false."
            }, Display), "Profilquellen, Datenschutz, Rechte, Grenzen und Qualifikationsnachweise.");
            return;
        }
        if (command is "trace-decode" or "trace-delete")
        {
            if (Synthetic) throw new InvalidOperationException("Synthetische Oberfläche öffnet oder löscht keine ETL.");
            var selectedTrace = Selection;
            var trace = selectedTrace.TraceResult ?? (selectedTrace.Fields.TryGetValue("trace", out var json) ?
                JsonSerializer.Deserialize<GuardianTraceResult>(json, IntelligenceModel.Json) : null) ??
                throw new InvalidOperationException("Zuerst ein eigenes Deep-Trace-Ergebnis auswählen.");
            GuardianTraceDecoder.Validate(trace);
            if (selectedTrace.Context != Context || Path.GetFullPath(Path.GetDirectoryName(trace.Path)!) != Path.GetFullPath(Path.Combine(_vault.Root, "ETL")))
                throw new InvalidOperationException("Nur die eigene lokale Guardian-ETL-Ablage ist hier zugelassen.");
            if (GuardianTrace.InspectOrStop(trace.Id, Path.GetDirectoryName(trace.Path)!, false) != "Nicht aktiv")
                throw new InvalidOperationException("ETL ist noch aktiv; zunächst eigene Sitzung kontrolliert beenden.");
            if (command == "trace-delete")
            {
                if (!Confirm("Sensible Roh-ETL löschen", "Eigene inaktive Roh-ETL und ihren Sessionmarker unwiderruflich löschen? Dekodierte Fakten bleiben im Vault bis zu dessen eigener Retention.")) return;
                await GuardianTrace.DeleteOwnedAsync(trace, token);
                Status.Text = "Eigene inaktive Roh-ETL und Sessionmarker gelöscht. Vault-/Rescue-Kopien behalten eigene Aufbewahrung.";
                return;
            }
            var analysis = await GuardianTraceDecoder.DecodeAsync(trace, token);
            await SaveAsync(GuardianTraceDecoder.Artifact(trace, analysis, Context, selectedTrace.Snapshot, ProcessHistory?.Invoke()), token);
            return;
        }
        if (command == "trace-recovery")
        {
            var directory = Path.Combine(_vault.Root, "ETL");
            if (!Directory.Exists(directory)) { Status.Text = "Keine eigene ETL-Ablage vorhanden."; return; }
            var lines = new List<string>();
            foreach (var file in Directory.EnumerateFiles(directory, "*.etl.session").Take(16))
            {
                var name = Path.GetFileName(file);
                if (!Guid.TryParseExact(name[..^12], "N", out var id)) continue;
                var status = GuardianTrace.InspectOrStop(id, directory, false);
                if (status == "Eigene Sitzung aktiv" && Confirm("Unterbrochene eigene Sitzung",
                    $"{id:D}: noch aktiv. Nur diese identitätsgeprüfte eigene Sitzung beenden?"))
                    status = GuardianTrace.InspectOrStop(id, directory, true);
                lines.Add(id + ": " + status);
            }
            SetDetails(string.Join("\n", lines), "Status der eigenen ETW-Sitzungsmarker.");
            Status.Text = "Eigene ETW-Sitzungen geprüft; keine fremden Sitzungen verändert."; return;
        }
        if (command == "windows-recovery")
        {
            var result = await CommandRunner.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "reagentc.exe"),
                ["/info"], TimeSpan.FromSeconds(25), token);
            if (result.ExitCode != 0 || result.OutputTruncated) throw new InvalidOperationException("Windows Recovery: " + result.Error + result.Output);
            await SaveAsync(GuardianVault.Artifact(RecoveryKind.DiagnosticEvidence, "Windows Recovery — Status", Context,
                "reagentc /info, nur lesend. Keine Erstellung/Löschung oder Ausführung einer Wiederherstellung.",
                new Dictionary<string, string> { ["reagentc"] = result.Output }), token); return;
        }
        if (command == "inspect-private")
        {
            var dialog = new OpenFileDialog { Filter = "Privates Guardian-Paket|*.aetherprivate", CheckFileExists = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            if (new FileInfo(dialog.FileName).Length > GuardianVault.MaximumObjectBytes + 65536) throw new IOException("Privates Paket zu groß.");
            var entries = GuardianRescue.OpenPrivate(await File.ReadAllBytesAsync(dialog.FileName, token), Context);
            SetDetails(string.Join("\n", entries.Select(a => a.Kind + ": " + a.Name)),
                "Inhalt des ausgewählten Wiederherstellungspakets.");
            Status.Text = $"{entries.Count} Einträge erfolgreich entschlüsselt, auf Integrität, Schema, Kontext und Abhängigkeiten geprüft. Keine Systemwiederherstellung ausgeführt."; return;
        }
        var selectedObject = command is "observe-stop" or "pause" or "resume" or "verify-maintenance" or "cancel-maintenance" or "maintenance-add" or "maintenance-next" or "drivers-verify" ? Selection : null;
        if (command == "maintenance-add")
        {
            var plan = _planned?.Plan ?? throw new InvalidOperationException("Zuerst im Änderungsjournal einen genauen Änderungsplan anzeigen.");
            await SaveAsync(GuardianMaintenance.Add(selectedObject!, plan, Context), token); return;
        }
        if (command == "maintenance-next")
        {
            if (Synthetic) throw new InvalidOperationException("Synthetische Oberfläche führt keine Windows-Aktion aus.");
            var next = GuardianMaintenance.Steps(selectedObject!).FirstOrDefault(s => s.State != "Completed")
                ?? throw new InvalidOperationException("Keine offenen Schritte.");
            if (!Confirm("Guardian — nächsten Wartungsschritt bestätigen", JsonSerializer.Serialize(next, Display))) return;
            var reservation = GuardianMaintenance.Reserve(selectedObject!, Context, Core, true);
            await SaveAsync(reservation.Pending, token);
            var tx = await Core.ExecuteAsync(reservation.Transaction, _actions, true, token);
            await SaveAsync(GuardianMaintenance.Complete(reservation.Pending, tx), CancellationToken.None); return;
        }
        var crash = command == "crash" ? Crash.SelectedItem as TimelineEntry ?? throw new InvalidOperationException("Qualifizierten Crash oder Hang auswählen.") : null;
        if (command is "pause" or "cancel-maintenance")
        {
            await SaveAsync(GuardianWorkflows.MaintenanceState(selectedObject!, command == "pause" ? "Paused" : "Cancelled", Context), token); return;
        }
        var snapshot = await ReadAsync(token);
        var artifact = command switch
        {
            "point" => GuardianWorkflows.RecoveryPoint(snapshot, Context, EntryName.Text.Trim()),
            "manifest" => GuardianWorkflows.SoftwareManifest(snapshot, Context),
            "drivers" => GuardianWorkflows.DriverState(snapshot, Context),
            "drivers-verify" => GuardianWorkflows.VerifyDrivers(selectedObject!, snapshot, Context),
            "observe-start" => GuardianWorkflows.BeginObservation(snapshot, Context),
            "observe-stop" => GuardianWorkflows.CompleteObservation(selectedObject!, snapshot, Context),
            "crash" => GuardianWorkflows.CrashCase(crash!, snapshot, Context, Timeline?.Invoke() ?? [], _artifacts, ProcessHistory?.Invoke(), Recordings?.Invoke()),
            "maintenance" => GuardianMaintenance.Create(EntryName.Text.Trim(), _planned?.Plan ??
                throw new InvalidOperationException("Zuerst im Änderungsjournal einen genauen Änderungsplan anzeigen."), snapshot, Context),
            "resume" or "verify-maintenance" => await GuardianMaintenance.ReanalyseAsync(selectedObject!, Context, snapshot, _artifacts, _actions, token),
            _ => throw new ArgumentException("Unbekannter Guardian-Befehl.")
        };
        if (command == "crash" && !Synthetic) artifact = await GuardianWorkflows.ReadCrashFilesAsync(artifact, token);
        await SaveAsync(artifact, token);
        if (command == "observe-stop")
        {
            await _vault.SaveAsync(selectedObject! with
            {
                Pinned = false,
                Fields = new Dictionary<string, string>(selectedObject!.Fields) { ["state"] = "Closed" }
            }, token);
            SetDetails(JsonSerializer.Serialize(GuardianWorkflows.ObservationChanges(selectedObject!, artifact),
                new JsonSerializerOptions(IntelligenceModel.Json) { WriteIndented = true }),
                "Vorher-/Nachhervergleich des beobachteten Installationsintervalls.");
        }
    }
    private static bool Confirm(string title, string text) => MessageBox.Show(text, title, MessageBoxButton.YesNo,
        MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void ExecuteClicked(object sender, RoutedEventArgs e)
    {
        if (_planned is not { } tx) { Status.Text = "Zuerst einen Änderungsplan anzeigen."; return; }
        if (!Confirm("Guardian — Systemänderung bestätigen", JsonSerializer.Serialize(tx.Plan, Display))) return;
        Run(async token =>
        {
            if (Synthetic) throw new InvalidOperationException("Synthetische Oberfläche führt keine Windows-Aktion aus.");
            _planned = null;
            UpdateContextActions();
            var result = await Core.ExecuteAsync(tx, ActionFor(tx.Plan), true, token);
            await LoadAsync(CancellationToken.None);
            SetDetails(JsonSerializer.Serialize(result, new JsonSerializerOptions(IntelligenceModel.Json) { WriteIndented = true }),
                "Ergebnis und Zustandsprüfung der bestätigten Transaktion.");
            Status.Text = $"Transaktion: {result.State} / {result.VerificationResult}";
        });
    }
    private void TraceClicked(object sender, RoutedEventArgs e)
    {
        var profile = SelectedTraceProfile;
        if (!profile.Released) { Status.Text = "Dieses Profil ist implementiert, aber noch nicht real freigegeben. Manueller Testhost: --guardian-etw-matrix. Keine Sitzung gestartet."; return; }
        if (!int.TryParse(TraceSeconds.Text, out var seconds) || seconds < 1 || seconds > profile.MaximumSeconds)
        { Status.Text = $"Dieses Profil benötigt 1–{profile.MaximumSeconds} Sekunden."; return; }
        var components = SelectedTraceComponents;
        var custom = profile.Id == "custom" ? GuardianTraceProfiles.CustomPlan(components) : null;
        if (!Confirm("ETW-Tiefendiagnose — sensible ETL-Datei", $"{profile.Name}: höchstens {seconds} Sekunden / {profile.MaximumMiB} MiB. " +
            $"Bausteine: {string.Join(", ", GuardianTraceProfiles.Resolve(profile, custom?.Effective ?? components).Select(p => p.Id))}. " +
            $"Decoder: 64 MiB je Ausgabe / 30 Sekunden. {custom?.Limitation} " +
            $"{profile.Privacy}. {profile.Rights}. {profile.Stop}. Erhöhte Systemlast möglich. Eigene Ablage, höchstens 256 MiB; keine automatische Löschung.")) return;
        Run(async token =>
        {
            if (Synthetic) throw new InvalidOperationException("Synthetische Oberfläche startet kein ETW.");
            Status.Text = "Eigene ETW-Sitzung läuft; Abbrechen oder AETHER-Shutdown beendet sie kontrolliert.";
            var result = await new GuardianTrace().RunReleasedProfileAsync(Path.Combine(_vault.Root, "ETL"), profile.Id, seconds, token, components);
            var raw = GuardianTraceDecoder.RawArtifact(result, Context);
            await SaveAsync(raw, CancellationToken.None);
            var analysis = await GuardianTraceDecoder.DecodeAsync(result, token);
            await SaveAsync(GuardianTraceDecoder.Artifact(result, analysis, Context, history: ProcessHistory?.Invoke()) with
            { Id = raw.Id, Created = raw.Created }, CancellationToken.None);
        });
    }
    private GuardianTraceProfile SelectedTraceProfile => TraceProfile.SelectedItem as GuardianTraceProfile ?? GuardianTraceProfiles.Get("process");
    private IReadOnlyList<string> SelectedTraceComponents => SelectedTraceProfile.Id == "custom" ?
        new[] { (TraceProcess, "process"), (TraceCpu, "cpu"), (TraceFile, "file"), (TraceDisk, "disk"), (TraceNetwork, "network") }
            .Where(item => item.Item1.IsChecked == true).Select(item => item.Item2).ToArray() : SelectedTraceProfile.Components;
    private void TraceProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TraceStart is null || TraceGate is null || TraceSeconds is null) return;
        var profile = SelectedTraceProfile;
        TraceSeconds.Text = profile.DefaultSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        TraceStart.IsEnabled = profile.Released;
        if (TraceComponents is not null) TraceComponents.Visibility = profile.Id == "custom" ? Visibility.Visible : Visibility.Collapsed;
        var qualification = profile.Qualification;
        var decoderStatus = qualification.RealDecoderValidated ? "Bestanden" :
            profile.Id == "cpu" ? "Teilweise bestanden; Malformed-Felder offen" : "Nicht nachgewiesen";
        var profileStatus = qualification.RealProfileValidated ? "Bestanden" :
            profile.Id == "cpu" ? "Nicht bestanden" :
            profile.Id == "custom" ? "Ausstehend" : "Nicht nachgewiesen";
        TraceStart.Content = qualification.ProductionReleased ? "ETW-Aufzeichnung starten" : "Reale ETW-Aufzeichnung nicht freigegeben";
        TraceGate.Text = $"{profile.Name}\n" +
            $"Umsetzung: {(qualification.Implemented ? "Vorhanden" : "Nicht vorhanden")} · " +
            $"Synthetische Prüfung: {(qualification.SyntheticallyValidated ? "Bestanden" : "Nicht bestanden")} · " +
            $"Realer Sitzungsablauf: {(qualification.RealLifecycleValidated ? "Bestanden" : "Nicht nachgewiesen")} · " +
            $"Realer Decoder: {decoderStatus} · Reales Profil: {profileStatus} · " +
            $"Produktfreigabe: {(qualification.ProductionReleased ? "Erteilt" : "Nicht erteilt")}.\n" +
            profile.Description + " " + qualification.Limitation +
            " Aktuelle Tokenrechte werden nur im manuellen Prüfmodus geprüft; AETHER erhöht sich nicht selbst.";
        if (profile.Id == "custom") TraceComponentsChanged(this, new RoutedEventArgs());
        UpdateContextActions();
    }
    private void TraceComponentsChanged(object sender, RoutedEventArgs e)
    {
        if (TraceGate is null || TraceNetwork is null || SelectedTraceProfile.Id != "custom") return;
        try
        {
            var plan = GuardianTraceProfiles.CustomPlan(SelectedTraceComponents);
            TraceStart.IsEnabled = SelectedTraceProfile.Released;
            var providers = GuardianTraceProfiles.Resolve(SelectedTraceProfile, plan.Effective);
            var components = string.Join(", ", providers.Select(provider => provider.Id switch
            {
                "process" => "Prozesskontext",
                "cpu" => "CPU-Samples",
                "file" => "Datei-I/O",
                "disk" => "Datenträger-I/O",
                "network" => "Netzwerk-Metadaten",
                _ => "ETW-Baustein"
            }));
            TraceGate.Text = $"Benutzerdefiniertes Profil: {components}. Höchstens {plan.Seconds} Sekunde / {plan.EtlMiB} MiB ETL; " +
                $"Decoderbudget {plan.DecoderBytes / 1048576} MiB je Datei / 30 Sekunden. {plan.Limitation} " +
                "Reales Profil: Ausstehend. Produktfreigabe: Nicht erteilt; realer Custom-Profilnachweis und Ressourcenwiederholung ausstehend.";
        }
        catch (ArgumentException ex)
        {
            TraceGate.Text = "Customauswahl ungültig: " + ex.Message;
            TraceStart.IsEnabled = false;
        }
    }
    private IGuardianAction ActionFor(GuardianPlan plan) => plan.Before.Kind == "configuration" ?
        ConfigurationAction ?? throw new InvalidOperationException("Konfigurationsadapter nicht verbunden.") : _actions;
    private void RescueClicked(object sender, RoutedEventArgs e) => Run(async token =>
    {
        await LoadAsync(token);
        var privatePackage = (string)((Button)sender).Tag == "private";
        var preview = GuardianRescue.Preview(_artifacts, privatePackage);
        if (!Confirm("Guardian — Inhaltsvorschau", JsonSerializer.Serialize(preview, new JsonSerializerOptions(IntelligenceModel.Json) { WriteIndented = true }))) return;
        var dialog = new SaveFileDialog
        {
            Filter = privatePackage ? "Private Recovery|*.aetherprivate" : "Redigiertes Supportpaket|*.aethersupport",
            FileName = "Aether-Guardian-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"),
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var result = await Task.Run(() => GuardianRescue.WriteAsync(dialog.FileName, _artifacts, privatePackage, true, token), token);
        await SaveAsync(GuardianVault.Artifact(RecoveryKind.RescuePackage, "Rescue Package — geprüfter Export", Context,
            "Lokales externes Paket nach Erstellung bytegenau geprüft. Spätere Existenz, Löschung und externe Aufbewahrung separat prüfen.",
            new Dictionary<string, string>
            {
                ["path"] = result.Path,
                ["sha256"] = result.Sha256,
                ["bytes"] = result.Bytes.ToString(),
                ["privacy"] = result.Privacy
            }), token);
        SetDetails(JsonSerializer.Serialize(result, new JsonSerializerOptions(IntelligenceModel.Json) { WriteIndented = true }),
            "Prüfergebnis des lokal erstellten Wiederherstellungspakets.");
        Status.Text = "Paket erstellt und bytegenau nachgeprüft. " + result.Privacy + "; keine automatische Weitergabe.";
    });
    internal void QuickAction(string command)
    {
        if (command.StartsWith("guardian:", StringComparison.Ordinal) && Guid.TryParse(command[9..], out var id))
        {
            Area.SelectedIndex = 0; Search.Text = id.ToString("D"); Render(); return;
        }
        foreach (ComboBoxItem item in Area.Items)
            if (Equals(item.Tag, command)) { Area.SelectedItem = item; break; }
        if (command == "Recovery Readiness")
        {
            if (Synthetic) SetDetails(JsonSerializer.Serialize(GuardianVault.Readiness(_artifacts, Context), Display),
                "Wiederherstellungsstatus und Umfang der lokal erfassten Quellen.", false);
            else Run(LoadAsync);
        }
    }
    internal void ApplyFixtures(IReadOnlyList<GuardianArtifact> artifacts) { _artifacts = artifacts; Render(); }
    internal void ObserveTransaction(GuardianTransaction tx)
    {
        _artifacts = _artifacts.Where(a => a.Id != tx.Id).Append(GuardianVault.Wrap(tx)).OrderByDescending(a => a.Created).Take(256).ToArray();
        ProjectJournal(tx);
        if (IsVisible) Render();
    }
    private void ProjectJournal(GuardianTransaction tx)
    {
        foreach (var entry in tx.Journal)
            Mark?.Invoke(new($"guardian:{tx.Id:D}:stage:{entry.Sequence}", entry.Time, DateTimeOffset.UtcNow, "GuardianTransaction",
                tx.Title + " / " + entry.State, "guardian:" + tx.Id.ToString("D"),
                IntelligenceModel.Metadata("AETHER Guardian / dauerhaftes Transaktionsjournal", entry.Time), entry.Detail));
    }
    internal async Task ShutdownAsync()
    {
        if (_shutdown) return;
        _shutdown = true; _stop.Cancel(); _work?.Cancel();
        await _pending;
        Rows.ItemsSource = null; Crash.ItemsSource = null;
        _artifacts = []; _planned = null; _core = null;
        Capture = null; Timeline = null; ProcessHistory = null; Recordings = null;
        Configuration = null; ConfigurationAction = null; Mark = null;
        _work?.Dispose(); _stop.Dispose();
    }
}
