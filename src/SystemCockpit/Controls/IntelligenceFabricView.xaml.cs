using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AetherOperations;
using AetherOperations.Intelligence;
using AetherOperations.Platform;
using Microsoft.Win32;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public partial class IntelligenceFabricView : UserControl
{
    private LiveSample? _sample;
    private FabricHardware _hardware = IntelligenceHardwareProbe.FromSample(null);
    private readonly AetherModelRegistry _models = new();
    private readonly PhiImportTrace _importTrace;
    private readonly Guid _viewDiagnosticId = Guid.NewGuid();
    private readonly Dictionary<string, string> _serverVisibility = [];
    private readonly Dictionary<string, string> _serverDigestComparison = [];
    internal string TraceServerVisibility(string id) => _serverVisibility.GetValueOrDefault(id, "unknown");
    private readonly AetherModelTelemetry _telemetry = new();
    private readonly List<CompatibleInferenceProvider> _providers = [];
    private readonly AetherModelLifecycle _lifecycle = new();
    private readonly LocalOllamaRuntime _ollamaRuntime = new();
    private string? _preferredModel;
    private string? _automaticModelBeingPrepared;
    private bool? _statusView;
    private string _automaticModelStatus =
        "Lokale KI muss eingerichtet werden. Regelbasierter Fallback ist verfügbar.";
    private AetherInferenceScheduler? _scheduler;
    private AetherResourceGovernor? _governor;
    private AetherEvidenceStore? _store;
    private AetherKnowledgeStore? _knowledge;
    private AetherIntelligenceAudit? _audit;
    private AetherIntelligenceHost? _host;
    private AetherApprovalEngine? _approvals;
    private PlatformPrincipal? _principal;
    private ImmutableArray<AdminTarget> _targets = [];
    private FabricInvestigation? _investigation;
    private FabricPlan? _plan;
    private PlatformJob? _job;
    private PlatformStore? _platform;
    private Task _pending = Task.CompletedTask;
    internal Task PendingOperation => _pending;
    private CancellationTokenSource? _stop;
    private bool _busy, _closing, _refreshingModelChoice, _catalogLoaded;
    internal Func<CancellationToken, Task<LiveSample>>? RefreshHardwareSample { get; set; }
    internal Func<FabricHardware, CancellationToken, Task<FabricHardware>>? RefreshGpuOverride { get; set; }
    internal event Action<string>? DiagnosticNavigationRequested;
    internal string? WorkspaceRootOverride { get; set; }
    internal string? CatalogPathOverride { get; set; }
    private string? _catalogFailureReason;
    private AetherModelCatalogDocument? _installedCatalog;
    private FabricRunDiagnostics? _runDiagnostics;
    private FabricRunDiagnostics? _lastRunDiagnostics;
    private readonly DispatcherTimer _snapshotStatusTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private sealed record ModeRow(string Text, FabricMode Value);
    private sealed record ModelRow(string Text, string? Id);
    private sealed record ModelOverviewRow(string Name, string Status, string Role, string Provider,
        string Execution, string Memory, string Context);
    private sealed record FindingRow(string Proof, string Text);
    private sealed record EvidenceRow(string Title, string Value, string Context);
    private sealed record TargetRow(string Name, string Source, string State);
    private sealed record Registration(int Schema, FabricModel Model, FabricProviderKind Provider, Uri Endpoint);
    private sealed record SavedRow(FabricPlanBinding Binding) { public string Name => $"{Binding.Plan.ActionId} · {Binding.Job.ToString("N")[..8]}"; }
    public IntelligenceFabricView() : this(new PhiImportTrace()) { }
    internal IntelligenceFabricView(PhiImportTrace importTrace)
    {
        _importTrace = importTrace;
        InitializeComponent();
        _snapshotStatusTimer.Tick += (_, _) =>
        {
            RefreshSnapshotStatus();
            if (!_busy && _investigation is { Assistant.RealModel: true } result)
                PresentInvestigation(result, _statusView);
        };
        _snapshotStatusTimer.Start();
        RefreshSnapshotStatus();
        Modes.ItemsSource = new[] { new ModeRow("Nur lokal", FabricMode.LocalOnly), new("Ausgeglichen", FabricMode.Balanced),
            new("Interaktiv", FabricMode.Interactive), new("Qualität", FabricMode.Quality), new("Geschwindigkeit", FabricMode.Speed),
            new("Hintergrund", FabricMode.Background), new("Nur CPU", FabricMode.CpuOnly), new("Ausgeschaltet", FabricMode.Disabled) };
        Modes.DisplayMemberPath = "Text"; Modes.SelectedIndex = 0;
        RefreshModelChoice("constructor");
        UpdateStatus();
        NoEvidenceText.Visibility = Visibility.Visible;
        StorageStatusText.Visibility = Visibility.Collapsed;
        AgentText.Text = string.Join("\n\n", new AetherAgentRegistry().Agents.Select(a => a.Id + ": " + a.Purpose)) +
            "\n\nRegelbasierte Agenten im modelllosen Pfad; keine behaupteten unabhängigen Modellurteile.";
        ModelText.Text = string.Join("\n\n", AetherModelRegistry.Capabilities.Select(p => $"{p.Kind}: {p.Detail}"));
        HelpText.Text = "Die erste Untersuchung richtet den eigenen lokalen, benutzergebunden DPAPI-verschlüsselten Intelligence-Speicher automatisch ein. " +
            "Die Testgruppe umfasst diesen Rechner und fünf eindeutig simulierte Ziele, darunter offline, Teilbelege und fehlende Berechtigung. " +
            "Nur die ausdrücklich gestartete Untersuchung liest lokalen Bestand und höchstens 128 Systemfehler der letzten 24 Stunden; keine Rohereignistexte.\n\n" +
            "Der Assistent hat keine Shell, keine Zugangsdaten und keinen freien Werkzeugzugriff. Modellvorschläge durchlaufen Schema, Zielprüfung und Policy. " +
            "Persönliche Freigabe bindet Plan-, Prompt-, Aktions-, Runbook- und Vorschauhash. Testmarken sind hier ausschließlich simuliert und keine Reparatur.\n\n" +
            "Der normale Betrieb nutzt ausschließlich den lokalen versionierten AETHER-Modellkatalog und die Setup-Konfiguration; vorhandene qualifizierte Modelle werden bei Bedarf geladen, fehlende Modelle nicht heruntergeladen. " +
            "Manueller Manifestimport und Expertenaktionen befinden sich im eingeklappten Entwicklermodus. Der erhöhte Cockpitprozess lädt keine nativen Modellbibliotheken. " +
            "Trainierte semantische ONNX-Embeddings sind nicht erworben; die lokale Suche verwendet ausdrücklich lexikalische Hashvektoren und Stichwörter.\n\n" +
            "Chattexte werden nicht dauerhaft gespeichert. Ergebnisse, Quellen und Audit bleiben lokal verschlüsselt. Wissensimporte sind sensibel, " +
            "quellengebunden und 30 Tage gültig. Geheimnisähnliche Inhalte werden zurückgewiesen. Keine externe Telemetrie oder Cloudfallbacks.\n\n" +
            "Clientzeitlimits und Speicherprüfungen sind keine harte Betriebssystemquote für einen separat betriebenen Modellserver. " +
            "Kein automatisches Entladen gemeinsam genutzter Servermodelle. CPU-admin-05 und Custom-admin-02 bleiben manuelle Aufgaben.\n\n" +
            "Betrieb und Grenzen: docs/INTELLIGENCE-OPERATIONS.md; Modellfreigabe: docs/INTELLIGENCE-MODEL-INSTALLATION.md.";
    }
    private void DeveloperModeChanged(object sender, RoutedEventArgs e)
    {
        DeveloperManagementPanel.Visibility = DeveloperMode.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
        if (DeveloperMode.IsChecked != true && _preferredModel is not null)
        {
            _preferredModel = null;
            RefreshModelChoice("developer-mode-disabled");
        }
    }

    private void RequireDeveloperMode()
    {
        if (DeveloperMode.IsChecked != true || !ModelDetails.IsExpanded || !AdvancedModelManagement.IsExpanded)
            throw new InvalidOperationException("Manuelle Modellverwaltung ist nur im geöffneten Entwicklermodus verfügbar.");
    }
    public void ApplyLive(LiveSample sample)
    {
        Volatile.Write(ref _sample, sample);
        var live = IntelligenceHardwareProbe.FromSample(sample);
        _hardware = _hardware with
        {
            At = sample.Timestamp,
            TotalRamBytes = live.TotalRamBytes,
            AvailableRamBytes = live.AvailableRamBytes,
            CpuPercent = live.CpuPercent,
            ProcessBytes = live.ProcessBytes
        };
        if (Dispatcher.CheckAccess()) RefreshSnapshotStatus();
        else Dispatcher.BeginInvoke(RefreshSnapshotStatus);
    }

    private FabricHardware CurrentHardware() => _hardware;

    internal async Task RefreshHardwareSnapshotAsync(FabricModel model, CancellationToken token)
    {
        var refresh = RefreshHardwareSample ??
            throw new FabricResourceException("Eine aktuelle lokale Hardwaremessung ist nicht verfügbar; die Inferenz wurde nicht gestartet.");
        _hardware = IntelligenceHardwareProbe.FromSample(null);
        try
        {
            var sample = await refresh(token).ConfigureAwait(false);
            if (sample.Timestamp == default || sample.Timestamp > DateTimeOffset.UtcNow)
                throw new FabricResourceException("Die lokale Hardwaremessung hat keinen gültigen Messzeitpunkt; die Inferenz wurde nicht gestartet.");
            Volatile.Write(ref _sample, sample);
            var hardware = IntelligenceHardwareProbe.FromSample(sample);
            var requiresVram = model is { RequiredVramBytes: > 0 };
            if (requiresVram)
                hardware = await (RefreshGpuOverride ?? IntelligenceHardwareProbe.ReadGpuAsync)(hardware, token).ConfigureAwait(false);
            _hardware = hardware;
            var freshness = AetherResourceGovernor.GetSnapshotFreshness(hardware, DateTimeOffset.UtcNow, requiresVram);
            if (freshness is FabricSnapshotFreshness.Unavailable or FabricSnapshotFreshness.Expired)
                throw new FabricResourceException("Die erforderliche aktuelle Hardwaremessung ist nicht verfügbar; die Inferenz wurde nicht gestartet.");
            if (Dispatcher.CheckAccess()) RefreshSnapshotStatus();
            else await Dispatcher.InvokeAsync(RefreshSnapshotStatus).Task.ConfigureAwait(false);
        }
        catch
        {
            _hardware = IntelligenceHardwareProbe.FromSample(null);
            if (Dispatcher.CheckAccess()) RefreshSnapshotStatus();
            else await Dispatcher.InvokeAsync(RefreshSnapshotStatus).Task.ConfigureAwait(false);
            throw;
        }
    }

    private void RefreshSnapshotStatus()
    {
        var hardware = CurrentHardware();
        var requiresVram = _models.AutomaticEntries
            .Any(entry => entry.Model.RequiredVramBytes > 0);
        var freshness = AetherResourceGovernor.GetSnapshotFreshness(hardware, DateTimeOffset.UtcNow, requiresVram);
        SnapshotStatus.Text = FormatSnapshotStatus(freshness, requiresVram);
    }

    internal static string FormatSnapshotStatus(FabricSnapshotFreshness freshness, bool requiresVram) => freshness switch
    {
        FabricSnapshotFreshness.Current => requiresVram
            ? "Hardware-Snapshot aktuell (RAM und VRAM)."
            : "Hardware-Snapshot aktuell (RAM).",
        FabricSnapshotFreshness.Expiring => "Hardware-Snapshot läuft bald ab; wird vor lokaler Inferenz automatisch erneuert.",
        FabricSnapshotFreshness.Expired => "Hardware-Snapshot abgelaufen. Wird bei der nächsten Untersuchung automatisch erneuert.",
        _ => "Hardwaremessung nicht verfügbar. Sie wird vor der lokalen Inferenz automatisch erneut versucht."
    };
    private T Required<T>(T? value) where T : class => value ??
        throw new InvalidOperationException("Der erforderliche Intelligence- oder Auftragszustand ist noch nicht verfügbar.");
    private async Task Operate(Func<CancellationToken, Task> work, string operation = "other", bool showProgress = false)
    {
        if (_busy || _closing)
        {
            if (operation != "other") TraceState("operation-skipped-" + operation);
            return;
        }
        SetOperationState(true, showProgress);
        _stop = new();
        var succeeded = false;
        try { Status.Text = "Lokal · Wird ausgeführt."; await work(_stop.Token); Refresh();
            succeeded = true;
            if (operation != "other") TraceState("operation-success-" + operation); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or
            JsonException or OperationCanceledException or System.Security.Cryptography.CryptographicException or System.ComponentModel.Win32Exception or FormatException or HttpRequestException or NotSupportedException or
            FabricContractException or FabricResourceException)
        { Trace.TraceError("AETHER Intelligence: {0}", ex);
            Status.Text = ex is OperationCanceledException && _stop?.IsCancellationRequested == true
                ? "Untersuchung abgebrochen." : "Nicht bestätigt: " + ex.Message;
            try { Refresh(); }
            catch (Exception refreshError) when (refreshError is IOException or InvalidOperationException or UnauthorizedAccessException or
                JsonException or System.Security.Cryptography.CryptographicException)
            { Trace.TraceError("AETHER Intelligence-Statusaktualisierung: {0}", refreshError); }
            if (operation != "other") { _importTrace.Write("operation-error", new { operation, errorType = ex.GetType().Name }); TraceState("operation-error-" + operation); } }
        finally
        {
            _stop.Dispose(); _stop = null;
            SetOperationState(false);
            if (succeeded) UpdateStatus();
        }
    }
    internal void SetOperationState(bool active, bool showProgress = false)
    {
        _busy = active;
        InvestigateButton.IsEnabled = !active;
        ModelChoice.IsEnabled = !active;
        DeveloperMode.IsEnabled = !active;
        AdvancedModelManagement.IsEnabled = !active;
        CancelButton.IsEnabled = active;
        OperationProgressPanel.Visibility = active && showProgress ? Visibility.Visible : Visibility.Collapsed;
        if (active && showProgress) SetProgressPhase(FabricProgressPhase.Preparing);
    }

    internal void SetProgressPhase(FabricProgressPhase phase)
    {
        var loadingId = _automaticModelBeingPrepared ?? _preferredModel;
        var loadingModel = loadingId is null ? null :
            _models.Entries.SingleOrDefault(entry => entry.Model.Id == loadingId).Model;
        OperationPhase.Text = phase switch
        {
            FabricProgressPhase.Preparing => "Bereitet Untersuchung vor.",
            FabricProgressPhase.ReadingSystemData => "Systemdaten werden gelesen.",
            FabricProgressPhase.CollectingEvidence => "Belege werden zusammengestellt.",
            FabricProgressPhase.RetrievingEvidence => "Belege werden gesucht.",
            FabricProgressPhase.CheckingHardware => "Hardwareeignung wird geprüft.",
            FabricProgressPhase.CheckingRuntime => "Lokale Runtime und Modellintegrität werden geprüft.",
            FabricProgressPhase.LoadingModel => $"{(loadingModel is null ? "Lokales Modell" : ModelName(loadingModel))} wird geladen.",
            FabricProgressPhase.PreparingModel => "Lokale KI wird vorbereitet.",
            FabricProgressPhase.AnalyzingLocally => "Analyse läuft.",
            FabricProgressPhase.RunningDeterministicRules => "Regelbasierte Auswertung läuft.",
            FabricProgressPhase.ValidatingProposal => "Vorschlag wird geprüft.",
            FabricProgressPhase.ValidatingAnswer => "Antwort wird geprüft.",
            FabricProgressPhase.PreparingResult => "Ergebnis wird aufbereitet.",
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
    }
    private void EnsureWorkspaceLoaded()
    {
        if (_host is not null) return;
        if (WorkspaceRootOverride is null || CatalogPathOverride is not null) LoadInstalledModelCatalog();
        var root = WorkspaceRootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AETHER", "IntelligenceFabric");
        var local = TargetInventory.Create("Dieser Rechner", Environment.MachineName, TargetKind.LocalComputer, TransportKind.Local);
        _targets = [local, .. Enumerable.Range(1, 5).Select(i => TargetInventory.Create(
            i switch { 3 => "Simulation offline", 4 => "Simulation mit Teilbelegen", 5 => "Simulation ohne Berechtigung", _ => $"Simulation {i}" },
            $"intelligence-fixture-{i}.invalid", TargetKind.WindowsClient, TransportKind.Simulated, true))];
        _principal = new($"{Environment.UserDomainName}\\{Environment.UserName}", PlatformRole.Support, _targets[..5].Select(t => t.Id).ToImmutableArray());
        _store = new(root, _principal.Id, new IntelligenceStateProtection());
        _knowledge = new(_store, _principal.Id); _knowledge.Restore();
        _audit = new(_store, _principal.Id);
        _governor = new(() => CurrentHardware(), RefreshHardwareSnapshotAsync);
        _scheduler = new(_governor, _telemetry, maximumConcurrentRequests: 1);
        _host = new(_principal, new IntelligenceFabricDesktop(local.Id, Path.Combine(root, "markers"),
            () => Volatile.Read(ref _sample), _targets), _store, new(_knowledge, new AetherEmbeddingProvider()), _models, _scheduler, _telemetry, _audit);
        TraceState("workspace-host-created");
        _platform = new PlatformStore(Path.Combine(root, "jobs"));
        var engine = new PlatformEngine(_platform, _principal, [new LocalPlatformAdapter(Path.Combine(root, "markers"), local.Id),
            new SimulatedPlatformAdapter(Path.Combine(root, "simulation")) { Offline = [_targets[3].Id] }]);
        _approvals = new(engine, new(_principal), _audit, _store);
    }
    private void LoadInstalledModelCatalog()
    {
        if (_catalogLoaded) return;
        _catalogLoaded = true;
        var path = CatalogPathOverride ?? AetherModelCatalog.DefaultPath;
        _importTrace.Write("automatic-catalog", () => new
        {
            path, exists = File.Exists(path), store = AetherLocalAiStoreContract.ModelStorePath,
            endpoint = AetherLocalAiStoreContract.Host,
            productModels = AetherProductModelCatalog.Current.Models.Select(model => new
            {
                id = _importTrace.Id(model.ModelId), qualificationStatus = model.QualificationStatus.ToString(),
                model.ApprovedForAutomaticSelection
            }).ToArray()
        });
        try
        {
            var catalog = AetherModelCatalog.Read(path);
            RegisterModelCatalog(catalog);
            _importTrace.Write("automatic-catalog-loaded", () => new
            {
                path, models = catalog.Models.Select(entry => new
                {
                    id = _importTrace.Id(entry.Model.Id), entry.HardwareQualified,
                    endpoint = entry.Endpoint.ToString(), expectedDigest = entry.Model.Digest
                }).ToArray()
            });
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            _catalogFailureReason = "CatalogMissing";
            _importTrace.Write("automatic-catalog-missing", new { path, error = ex.Message });
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or JsonException or System.Security.Cryptography.CryptographicException)
        {
            _catalogFailureReason = "CatalogRejected";
            _importTrace.Write("automatic-catalog-rejected", new { path, error = ex.Message });
            Trace.TraceError("AETHER Intelligence: lokaler Modellkatalog konnte nicht geladen werden: {0}", ex);
            _automaticModelStatus =
                "Lokale KI muss eingerichtet werden. Der AETHER-Modellkatalog ist ungültig oder nicht verfügbar; regelbasierter Fallback ist verfügbar.";
        }
    }
    internal void RegisterModelCatalog(AetherModelCatalogDocument catalog)
    {
        AetherModelCatalog.Register(catalog, _models, _providers);
        _installedCatalog = catalog;
        foreach (var entry in _models.AutomaticEntries) _lifecycle.Registered(entry.Model);
        _automaticModelStatus = catalog.Models.IsEmpty
            ? "Lokale KI muss eingerichtet werden. Der AETHER-Katalog enthält kein freigegebenes Modell; regelbasierter Fallback ist verfügbar."
            : "AETHER-Katalog geladen. Die lokale Modellbereitschaft wird vor der Untersuchung geprüft.";
        RefreshModelChoice("catalog-loaded");
    }
    private async void InvestigateClicked(object sender, RoutedEventArgs e)
    {
        var input = Question.Text;
        var progress = new Progress<FabricProgressPhase>(SetProgressPhase);
        _pending = Operate(async token =>
        {
            ClearPreviousInvestigation();
            _investigation = await RunDiagnosedInvestigationAsync(input, token, progress);
            _statusView = AetherAgentRegistry.IsSystemSummary(input);
            _plan = _investigation.Plan; _job = null;
        }, "investigation", true); await _pending;
        UpdateRunDiagnosticUi();
    }

    private async Task<FabricInvestigation> RunDiagnosedInvestigationAsync(string input, CancellationToken token,
        IProgress<FabricProgressPhase> progress)
    {
        var root = WorkspaceRootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AETHER", "IntelligenceFabric");
        var run = IntelligenceRunLog.ProcessContext();
        run.Stage = "InvestigationStarted";
        _runDiagnostics = run;
        _lastRunDiagnostics = run;
        IntelligenceRunLog? log = null;
        try
        {
            log = new IntelligenceRunLog(root, run);
            run.Changed = log.Write;
            var profile = AetherLocalAiStoreContract.AcceptanceProfilePath;
            run.Update(d => { d.ProfilePath = profile; d.ProfileFound = profile is not null; });
            try { run.Update(d => d.AcceptanceMode = AetherLocalAiStoreContract.IsAcceptanceMode); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
            {
                run.Update(d => { d.FallbackReason = "ProfileInvalid"; d.ExceptionType = ex.GetType().FullName;
                    d.ExceptionDetail = ex.Message; });
                throw;
            }
            EnsureWorkspaceLoaded();
            var path = CatalogPathOverride ?? AetherModelCatalog.DefaultPath;
            run.Update(d =>
            {
                d.CatalogPath = path; d.CatalogFound = File.Exists(path);
                d.CatalogSchema = _installedCatalog?.Schema; d.CatalogVersion = _installedCatalog?.CatalogVersion;
                d.CatalogSource = CatalogPathOverride is not null ? "TestOverride" :
                    profile is null ? "ProductCatalog" : "AcceptanceProfile";
                d.StoreRoot = Path.GetDirectoryName(path);
                d.ModelStore = _installedCatalog?.Models.FirstOrDefault()?.ModelStorePath;
            });
            var role = AetherAgentRegistry.RoleFor(input);
            run.Update(d => d.RequestedModelRole = role.ToString());
            var modelId = await PrepareAutomaticModelAsync(token, progress, role);
            var local = _targets.Single(t => !t.Simulated);
            return await Task.Run(() => AetherAgentRegistry.IsSystemSummary(input)
                ? Required(_host).SummarizeAsync(input, local, FabricMode.LocalOnly, token, modelId, progress, run)
                : Required(_host).InvestigateAsync(input, [local], FabricMode.LocalOnly, token, modelId, progress, run), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            run.Changed = null;
            run.Update(d => d.FallbackReason = "Cancelled");
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or JsonException or HttpRequestException or
            FabricContractException or FabricResourceException or System.ComponentModel.Win32Exception or
            System.Security.Cryptography.CryptographicException)
        {
            run.Changed = null;
            run.Update(d =>
            {
                d.FallbackReason ??= d.DiagnosticWriteError is not null
                    ? "DiagnosticPersistenceFailed" : "InvestigationFailed";
                d.ExceptionType = ex.GetType().FullName;
                d.ExceptionDetail ??= ex is FabricContractException contract ? contract.Diagnostic :
                    ex is FabricResourceException resource ? resource.Message : "Untersuchung fehlgeschlagen: " + ex.GetType().Name;
            });
            throw;
        }
        finally
        {
            run.Stage = "InvestigationCompleted";
            try { log?.Write(run); }
            finally
            {
                run.Changed = null; _runDiagnostics = null;
                try { log?.Dispose(); }
                finally { UpdateRunDiagnosticUi(); }
            }
        }
    }

    private void UpdateRunDiagnosticUi()
    {
        if (_lastRunDiagnostics is not { } run) return;
        RunDiagnosticText.Text = $"RunId: {run.RunId}\nModell: {run.SelectedModelId ?? "nicht ausgewählt"}\n" +
            $"Prozess: {run.ProcessId} · SID: {run.ProcessUserSid ?? "unbekannt"} · Elevated: {run.Elevated}\n" +
            $"LocalAppData: {run.LocalAppDataPath ?? "nicht verfügbar"}\n" +
            $"Primäres Diagnoseziel: {run.PrimaryDiagnosticPath ?? "nicht angelegt"}\n" +
            $"Diagnoseziel: {run.DiagnosticPath ?? "nicht angelegt"}\n" +
            $"Diagnoseschreibfehler: {run.DiagnosticWriteError ?? "keiner"}\n" +
            $"Modellrolle: {run.RequestedModelRole ?? "nicht festgelegt"}\n" +
            $"PromptVersion: {run.PromptVersion ?? "nicht gestartet"} · AnswerType: {run.AnswerType ?? "nicht gestartet"}\n" +
            $"Task: {run.InferenceTask ?? "nicht gestartet"}\nEXE: {run.ExecutablePath ?? "nicht verfügbar"}\n" +
            $"Katalogquelle: {run.CatalogSource ?? "nicht bestimmt"}\nStore: {run.StoreRoot ?? "nicht bestimmt"} · " +
            $"{run.ModelStore ?? "Modellstore nicht bestimmt"}\n" +
            $"Provider: {run.FinalProvider ?? run.IntendedProvider ?? "nicht verfügbar"}\nRealModel: {run.RealModel?.ToString() ?? "nicht bestätigt"}\n" +
            $"FallbackReason: {run.FallbackReason ?? "kein Fallback"}\nCapability: {run.Capability ?? "nicht geprüft"}\n" +
            $"Ollama: {run.OllamaEndpoint ?? "nicht verfügbar"}\n" +
            $"Runtime-Start: {run.RuntimeStartedAt?.ToString("O") ?? "nicht bestätigt"} · Store: {run.RuntimeModelStorePath ?? "nicht bestätigt"}\n" +
            $"Modell gefunden: {run.ModelFound?.ToString() ?? "nicht geprüft"}\n" +
            $"Digest geprüft: {(run.ActualDigest is null || run.ExpectedDigest is null ? "nicht geprüft" : string.Equals(run.ActualDigest, run.ExpectedDigest, StringComparison.OrdinalIgnoreCase).ToString())}\n" +
            $"Modell geladen: {run.LoadSucceeded?.ToString() ?? run.Resident?.ToString() ?? "nicht geprüft"}\n" +
            $"Resident-VRAM: {run.ResidentModelVramBytes?.ToString() ?? "nicht gemeldet"} Byte · Load: {run.ModelLoadDurationMilliseconds?.ToString("F0") ?? "nicht gemessen"} ms\n" +
            $"Inferenz gestartet: {run.InferenceStarted} · abgeschlossen: {run.InferenceSucceeded?.ToString() ?? "nicht bestätigt"}\n" +
            $"Validator: {run.ValidatorSucceeded?.ToString() ?? "nicht geprüft"} · Repair-Retry: {run.RepairRetryStarted}\n" +
            $"InitialValidationReason: {run.InitialValidationReason ?? "keiner"}\n" +
            $"ExceptionDetail: {run.ExceptionDetail ?? "keines"}\n" +
            $"FallbackDetail: {run.FallbackDetail ?? "keines"}\n" +
            $"MaximumOutputTokens: {run.MaximumOutputTokens?.ToString() ?? "nicht gestartet"}\n" +
            $"DoneReason initial/repair: {run.InitialDoneReason ?? "nicht gemeldet"}/{run.RepairDoneReason ?? "nicht gemeldet"}\n" +
            $"Antwortlänge initial/repair: {run.InitialResponseShape?.Characters.ToString() ?? "nicht gemeldet"}/" +
            $"{run.RepairResponseShape?.Characters.ToString() ?? "nicht gemeldet"} Zeichen\n" +
            $"Repair-Tokens in/out: {run.RepairInputTokens?.ToString() ?? "nicht gemeldet"}/{run.RepairOutputTokens?.ToString() ?? "nicht gemeldet"}\n" +
            $"Tokens in/out: {run.InitialInputTokens?.ToString() ?? "nicht gemeldet"}/{run.InitialOutputTokens?.ToString() ?? "nicht gemeldet"} · " +
            $"Generierung: {run.InitialGenerationSeconds?.ToString("F2") ?? "nicht gemessen"} s · First Token: {run.InitialFirstTokenSeconds?.ToString("F2") ?? "nicht verfügbar"} s";
    }
    private void ClearPreviousInvestigation()
    {
        _investigation = null;
        _statusView = null;
        _plan = null;
        _job = null;
        SummaryText.Text = "Neue Untersuchung läuft. Vorherige Ergebnisse sind ausgeblendet.";
        FindingsItems.ItemsSource = Array.Empty<FindingRow>();
        EvidenceItems.ItemsSource = Array.Empty<EvidenceRow>();
        RecommendationsItems.ItemsSource = Array.Empty<string>();
        NoEvidenceText.Visibility = Visibility.Visible;
        Explanation.Text = "";
        ModelAnswerText.Text = "Noch keine Modellantwort vorhanden.";
        ActionsText.Text = "Noch keine Antwort bestätigt. Änderungen benötigen eine eigene Vorschau und persönliche Freigabe.";
        Targets.ItemsSource = Array.Empty<TargetRow>();
        PlanOverview.Text = "Untersuchung läuft; es liegt noch kein aktueller Auftrag vor.";
        PlanText.Text = PlanOverview.Text;
    }
    internal async Task<string> PrepareAutomaticModelAsync(CancellationToken token,
        IProgress<FabricProgressPhase>? progress = null,
        FabricModelRole role = FabricModelRole.DiagnosticReasoner)
    {
        EnsureWorkspaceLoaded();
        var automaticEntries = _models.AutomaticEntries;
        if (automaticEntries.IsEmpty)
        {
            _automaticModelStatus =
                "Lokale KI muss eingerichtet werden. Keine freigegebene, hardwarequalifizierte Modellkonfiguration aus dem AETHER-Katalog vorhanden. Regelbasierter Fallback ist verfügbar.";
            return RecordAutomaticFallback(_catalogFailureReason ?? "CatalogEmpty", _automaticModelStatus);
        }
        CompatibleInferenceProvider? provider = null;
        FabricModel? selectedModel = null;
        try
        {
            var probe = automaticEntries.OrderByDescending(entry => entry.Model.RequiredVramBytes).First().Model;
            progress?.Report(FabricProgressPhase.CheckingHardware);
            await RefreshHardwareSnapshotAsync(probe, token);
            var route = new AetherModelRouter(_models, _telemetry).Route("de", FabricMode.LocalOnly,
                DataClassification.Sensitive, minimumContextTokens: 4096, hardware: CurrentHardware(),
                automaticOnly: true, role: role);
            var measured = CurrentHardware();
            var evaluatedModel = route.Model.Id == AetherModelRegistry.Fallback.Id ? probe : route.Model;
            var ramAccepted = measured.AvailableRamBytes is { } ram && evaluatedModel.RequiredRamBytes <= ram - (8L << 30);
            var vramAccepted = evaluatedModel.RequiredVramBytes == 0 || measured.AvailableVramBytes is { } vram &&
                evaluatedModel.RequiredVramBytes <= vram - (2L << 30);
            var product = AetherProductModelCatalog.Current.Models.FirstOrDefault(model => model.ModelId == evaluatedModel.Id);
            _runDiagnostics?.Update(d =>
            {
                d.HardwareSnapshotAt = measured.At; d.TotalRamBytes = measured.TotalRamBytes;
                d.AvailableRamBytes = measured.AvailableRamBytes; d.TotalVramBytes = measured.TotalVramBytes;
                d.AvailableVramBytes = measured.AvailableVramBytes;
                d.FreeModelStorageBytes = new DriveInfo(Path.GetPathRoot(AetherLocalAiStoreContract.ModelStorePath)!).AvailableFreeSpace;
                d.CatalogModelId = _installedCatalog?.Models.FirstOrDefault(model => model.Model.Id == evaluatedModel.Id)?.Model.Id;
                d.ModelStore = _installedCatalog?.Models.FirstOrDefault(model => model.Model.Id == evaluatedModel.Id)?.ModelStorePath;
                d.QualificationStatus = product?.QualificationStatus.ToString();
                d.ApprovedForAutomaticSelection = product?.ApprovedForAutomaticSelection;
                d.Capability = route.Model.Id == AetherModelRegistry.Fallback.Id ? "RoutingRejected" : "RoutingAccepted";
                d.AvailableRamAccepted = ramAccepted; d.AvailableVramAccepted = vramAccepted;
                d.SelectedModelId = route.Model.Id; d.IntendedProvider = route.Provider.Info.Kind.ToString();
                d.OllamaEndpoint = route.Provider.Info.Endpoint?.ToString(); d.ExpectedDigest = route.Model.Digest;
            });
            _importTrace.Write("automatic-capability", () =>
            {
                var hardware = CurrentHardware();
                var drive = new DriveInfo(Path.GetPathRoot(AetherLocalAiStoreContract.ModelStorePath)!);
                return new
                {
                    model = _importTrace.Id(evaluatedModel.Id), selected = _importTrace.Id(route.Model.Id),
                    result = route.Model.Id == AetherModelRegistry.Fallback.Id ? "rejected" : "accepted",
                    hardware.TotalRamBytes, hardware.AvailableRamBytes, hardware.TotalVramBytes, hardware.AvailableVramBytes,
                    freeStorageBytes = drive.AvailableFreeSpace, product?.MinimumRamBytes,
                    product?.MinimumAvailableRamBytes, product?.MinimumVramBytes, product?.MinimumStorageBytes,
                    requiredRamBytes = evaluatedModel.RequiredRamBytes, requiredVramBytes = evaluatedModel.RequiredVramBytes,
                    ramAccepted, vramAccepted,
                    route.Reason
                };
            });
            if (route.Model.Id == AetherModelRegistry.Fallback.Id)
            {
                _automaticModelStatus =
                    "Nach der aktuellen Hardwaremessung wurde kein geeignetes lokales Modell bestätigt; regelbasierter Fallback ist verfügbar.";
                var reason = !ramAccepted || !vramAccepted ? "HardwareRejected" : "ProviderUnavailable";
                return RecordAutomaticFallback(reason, route.Reason);
            }
            if (route.Provider is not CompatibleInferenceProvider { IsLoopback: true } localProvider)
                throw new NotSupportedException("Automatische lokale Modellverwaltung erfordert Ollama auf Loopback.");
            provider = localProvider;
            selectedModel = route.Model;
            _automaticModelBeingPrepared = route.Model.Id;
            if (CatalogPathOverride is null && WorkspaceRootOverride is null)
            {
                var endpoint = await _ollamaRuntime.EnsureAvailableAsync(provider, route.Model, _runDiagnostics, token);
                if (endpoint != provider.Info.Endpoint)
                {
                    var replacement = new CompatibleInferenceProvider(provider.Info.Id, FabricProviderKind.Ollama, endpoint,
                        runtimePolicy: provider.RuntimePolicy);
                    try { _models.ReplaceAutomaticProvider(route.Model.Id, provider, replacement); }
                    catch { replacement.Dispose(); throw; }
                    _providers.Add(replacement);
                    provider = replacement;
                }
            }
            await _lifecycle.EnsureReadyAsync(route.Model, provider, Required(_governor), token, progress, _runDiagnostics);
            TraceAutomaticLifecycle(route.Model, provider, "ready");
            _automaticModelStatus = $"{ModelName(route.Model)} · bereit und lokal verwaltet.";
            return route.Model.Id;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or InvalidOperationException or
            UnauthorizedAccessException or FabricResourceException or NotSupportedException or OperationCanceledException or
            LocalOllamaRuntimeException or System.ComponentModel.Win32Exception or JsonException)
        {
            Trace.TraceWarning("AETHER Intelligence: automatische lokale Modellvorbereitung fehlgeschlagen: {0}", ex);
            _automaticModelStatus = "Lokale KI muss eingerichtet werden. Provider, Modellintegrität oder Hardwareeignung nicht bestätigt; regelbasierter Fallback ist verfügbar.";
            var inventory = provider?.LastInventory;
            var reason = ex switch
            {
                LocalOllamaRuntimeException runtime => runtime.Reason,
                FabricResourceException => "HardwareRejected",
                _ when selectedModel is not null &&
                    _lifecycle.Status(selectedModel.Id) is { State: FabricModelLoadState.Error, Operation: FabricModelOperation.Loading } => "LoadFailed",
                HttpRequestException => "EndpointUnavailable",
                JsonException => "EndpointUnavailable",
                OperationCanceledException when selectedModel is null => "HardwareTimeout",
                OperationCanceledException => "RuntimeTimeout",
                InvalidDataException when inventory is { Found: false } => "ModelMissing",
                InvalidDataException when inventory is { Found: true } && selectedModel is not null &&
                    !string.Equals(inventory.ActualDigest, selectedModel.Digest, StringComparison.OrdinalIgnoreCase) => "DigestMismatch",
                NotSupportedException => "ProviderUnavailable",
                _ => "ProviderUnavailable"
            };
            if (selectedModel is not null && provider is not null)
            {
                TraceAutomaticLifecycle(selectedModel, provider, reason);
                _runDiagnostics?.Update(d =>
                {
                    d.EnsureReadySucceeded = false;
                    if (d.LoadStarted) d.LoadSucceeded = false;
                    d.EndpointReachable = inventory?.Reachable ?? (ex is HttpRequestException ? false : d.EndpointReachable);
                    d.ModelFound = inventory?.Found; d.ActualDigest = inventory?.ActualDigest; d.Resident = inventory?.Resident;
                });
            }
            _runDiagnostics?.Update(d => { d.ExceptionType = ex.GetType().FullName; d.ExceptionDetail = ex.Message; });
            return RecordAutomaticFallback(reason, ex.Message, ex.GetType().Name);
        }
    }
    private string RecordAutomaticFallback(string reason, string detail, string? errorType = null)
    {
        Trace.TraceWarning("AETHER Intelligence FallbackReason={0}: {1}", reason, detail);
        _importTrace.Write("automatic-fallback", new { fallbackReason = reason, detail, errorType });
        _runDiagnostics?.Update(d => { d.FallbackDetail = detail; d.FallbackReason = reason switch
        {
            "CatalogRejected" => "CatalogInvalid",
            "CatalogEmpty" => "ModelNotQualified",
            "RuntimeTimeout" => "EndpointUnavailable",
            "HardwareTimeout" => "HardwareRejected",
            _ => reason
        }; });
        Required(_audit).Record("Automatischer Modellfallback: " + reason + (errorType is null ? "" : " (" + errorType + ")"),
            PlatformJson.Hash(new { reason, detail, errorType }));
        return AetherModelRegistry.Fallback.Id;
    }

    private void TraceAutomaticLifecycle(FabricModel model, CompatibleInferenceProvider provider, string result)
    {
        _importTrace.Write("automatic-lifecycle", () => new
        {
            model = _importTrace.Id(model.Id), endpoint = provider.Info.Endpoint?.ToString(),
            expectedDigest = model.Digest, provider.LastInventory,
            ollamaReachable = provider.LastInventory is not null ? (bool?)true :
                result == "RuntimeUnavailable" ? false : null,
            lifecycle = _lifecycle.Status(model.Id), ensureReady = result
        });
    }
    private FabricMode SelectedMode => Modes.SelectedItem is ModeRow row ? row.Value : FabricMode.LocalOnly;
    private async void RetestClicked(object sender, RoutedEventArgs e)
    {
        RequireDeveloperMode();
        var progress = new Progress<FabricProgressPhase>(SetProgressPhase);
        _pending = Operate(async token =>
        {
            _investigation = await RunSelectedModelRetestAsync(Confirm, token, progress);
            _statusView = true;
            _plan = null; _job = null;
        }, "retest", true);
        await _pending;
    }

    internal async Task<FabricInvestigation> RunSelectedModelRetestAsync(Func<string, bool> confirm,
        CancellationToken token, IProgress<FabricProgressPhase>? progress = null)
    {
        EnsureWorkspaceLoaded();
        progress?.Report(FabricProgressPhase.PreparingModel);
        if (!await LoadSelectedModelAsync(confirm, token))
            throw new OperationCanceledException("Modellladen nicht bestätigt.", token);
        var local = _targets.Single(target => !target.Simulated);
        return await Task.Run(() => Required(_host).SummarizeAsync(
            "Fasse den aktuellen AETHER-Systemstatus zusammen.", local, FabricMode.LocalOnly, token, _preferredModel, progress), token);
    }
    private async void SystemStatusClicked(object sender, RoutedEventArgs e)
    {
        var progress = new Progress<FabricProgressPhase>(SetProgressPhase);
        _pending = Operate(async token =>
        {
            EnsureWorkspaceLoaded();
            _investigation = await RunDiagnosedInvestigationAsync(
                "Fasse den aktuellen AETHER-Systemstatus zusammen.", token, progress);
            _statusView = true;
            _plan = null; _job = null; PlanText.Text = "Systemstatus: Risiko 0, ausschließlich gelesen. Keine Verwaltungsaktion vorbereitet oder ausgeführt.";
        }, "summary", true); await _pending;
        UpdateRunDiagnosticUi();
    }
    internal void RefreshModelChoice(string source = "explicit")
    {
        if (FindName("ModelChoice") is not ComboBox choice) { TraceState("refresh-missing-" + source); return; }
        TraceState("refresh-before-" + source);
        _refreshingModelChoice = true;
        try
        {
            choice.ItemsSource = new[] { new ModelRow("Automatisch · nur freigegebene lokale Modelle", null),
                new("Regelbasiert · kein Sprachmodell", AetherModelRegistry.Fallback.Id) }
                .Concat(_models.Entries.Where(e => e.Model.Id != AetherModelRegistry.Fallback.Id)
                    .Select(e => new ModelRow(e.Model.Id, e.Model.Id))).ToArray();
            choice.DisplayMemberPath = "Text";
            choice.SelectedItem = choice.Items.Cast<ModelRow>().Single(r => r.Id == _preferredModel);
            RefreshSnapshotStatus();
        }
        finally { _refreshingModelChoice = false; TraceState("refresh-after-" + source); }
    }
    private void ModelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingModelChoice && sender is ComboBox { SelectedItem: ModelRow row }) _preferredModel = row.Id;
        RefreshSnapshotStatus();
        if (!_refreshingModelChoice) TraceState("selection-changed");
    }
    internal (FabricModel Model, CompatibleInferenceProvider Provider) SelectedHttpModel()
    {
        TraceState("selected-http-model");
        if (_preferredModel is null || _preferredModel == AetherModelRegistry.Fallback.Id)
            throw new InvalidOperationException("Zuerst ein konkretes freigegebenes Servermodell auswählen.");
        var entry = _models.Entries.Single(e => e.Model.Id == _preferredModel);
        return (entry.Model, entry.Provider as CompatibleInferenceProvider ??
            throw new InvalidOperationException("Kein qualifizierter HTTP-Provider für diese Auswahl."));
    }
    private async void CheckModelClicked(object sender, RoutedEventArgs e)
    {
        RequireDeveloperMode();
        _pending = Operate(CheckSelectedModelAsync, "check"); await _pending;
    }
    internal async Task CheckSelectedModelAsync(CancellationToken token)
    {
        var entry = SelectedHttpModel();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        await CheckModelWithTraceAsync(entry.Model, entry.Provider, deadline.Token);
    }
    private async void LoadModelClicked(object sender, RoutedEventArgs e)
    {
        RequireDeveloperMode();
        _pending = LoadSelectedModelWithConfirmationAsync(Confirm); await _pending;
    }
    internal Task LoadSelectedModelWithConfirmationAsync(Func<string, bool> confirm) =>
        Operate(async token =>
        {
            EnsureWorkspaceLoaded();
            if (!await LoadSelectedModelAsync(confirm, token))
                throw new OperationCanceledException("Modellladen nicht bestätigt.");
        }, "load");
    internal async Task<bool> LoadSelectedModelAsync(Func<string, bool> confirm, CancellationToken token)
    {
        var entry = SelectedHttpModel();
        if (entry.Provider.Info.Kind != FabricProviderKind.Ollama)
            throw new NotSupportedException("Separates Laden ist nur für Ollama verfügbar.");
        EnsureWorkspaceLoaded();
        using (var hardware = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            hardware.CancelAfter(TimeSpan.FromSeconds(15));
            await RefreshHardwareSnapshotAsync(entry.Model, hardware.Token);
        }
        using (var check = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            check.CancelAfter(TimeSpan.FromSeconds(10));
            await CheckModelWithTraceAsync(entry.Model, entry.Provider, check.Token);
        }
        _importTrace.Write("load-confirmation", new { shown = true, modelId = _importTrace.Id(entry.Model.Id) });
        if (!confirm("Das ausgewählte, bereits installierte Ollama-Modell ausdrücklich auf dem separat betriebenen lokalen Server laden? Keine Downloads und kein Serverstart.")) return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
        ModelText.Text = $"Modell wird ausdrücklich geladen: {entry.Model.Id}. Keine Gewichte werden heruntergeladen.";
        await _lifecycle.LoadAsync(entry.Model, entry.Provider, Required(_governor), deadline.Token);
        return true;
    }
    private async Task CheckModelWithTraceAsync(FabricModel model, CompatibleInferenceProvider provider, CancellationToken token)
    {
        try
        {
            await _lifecycle.CheckAsync(model, provider, token);
            if (_importTrace.Enabled)
            {
                _serverVisibility[model.Id] = "yes";
                _serverDigestComparison[model.Id] = provider.Info.Kind == FabricProviderKind.Ollama ? "matched" : "not-checked";
            }
        }
        catch (InvalidDataException ex)
        {
            if (_importTrace.Enabled)
            {
                _serverVisibility[model.Id] = ex.Message.StartsWith("Modell auf diesem Ollama-Server nicht sichtbar", StringComparison.Ordinal)
                    ? "no" : ex.Message.StartsWith("Ollama-Modelldigest widerspricht", StringComparison.Ordinal) ? "yes" : "unknown";
                _serverDigestComparison[model.Id] = _serverVisibility[model.Id] == "yes" ? "mismatch" : "not-checked";
            }
            throw;
        }
        catch
        {
            if (_importTrace.Enabled) { _serverVisibility[model.Id] = "unknown"; _serverDigestComparison[model.Id] = "unknown"; }
            throw;
        }
        finally { TraceState("server-check"); }
    }
    private async void UnloadModelClicked(object sender, RoutedEventArgs e)
    {
        RequireDeveloperMode();
        if (!Confirm("Das ausgewählte lokale Ollama-Modell auf dem separat betriebenen Server entladen? Das kann andere Serverbenutzer betreffen.")) return;
        _pending = Operate(async token =>
        {
            var entry = SelectedHttpModel();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            await _lifecycle.UnloadAsync(entry.Model, entry.Provider, deadline.Token);
        }); await _pending;
    }
    internal void Refresh()
    {
        Targets.ItemsSource = _targets.Select(t => new TargetRow(t.Name, t.Simulated ? "SIMULIERT; keine Remoteverbindung" : "Lokaler Windows-Lesepfad",
            _investigation?.Observations.SingleOrDefault(o => o.TargetId == t.Id)?.Availability switch
            { AetherOperations.DataAvailability.Available => "Teilbelege vorhanden", AetherOperations.DataAvailability.AccessDenied => "Nicht berechtigt",
                AetherOperations.DataAvailability.Unavailable => "Nicht verfügbar", AetherOperations.DataAvailability.ProviderError => "Quellenfehler", _ => "Noch nicht gelesen" })).ToArray();
        if (_investigation is { } result)
            PresentInvestigation(result, _statusView);
        if (_plan is { } plan)
        {
            PlanOverview.Text = plan.Risk == 0
                ? $"Lesender Auftrag · Risiko 0 · {plan.Targets.Length} ausdrücklich ausgewählte Ziele. Es wurde keine Systemänderung ausgeführt."
                : $"Vorschau erforderlich · Risiko {plan.Risk} · {plan.Targets.Length} ausdrücklich ausgewählte Ziele.";
            PlanText.Text = $"Absicht: {plan.Intent}\nAktion: {plan.ActionId} · Risiko {plan.Risk}\nRunbook: {plan.RunbookId}\n" +
                $"Plan: {plan.Hash}\nPrompt: {plan.PromptHash}\nCanary: {plan.Policy.CanaryCount}; Welle: {plan.Policy.WaveSize}\n" +
                "Explizite Planziele:\n" + string.Join("\n", plan.Targets.Select(id => _targets.Single(t => t.Id == id)).Select(t =>
                    $"{t.Name} · {(t.Simulated ? "SIMULIERT" : "Lokal lesend")} · {t.Id}")) + "\n" +
                $"Prüfung: {plan.Verification}\nRückweg: {plan.Rollback}\n" + string.Join("\n", plan.Assumptions) +
                (_job is null ? "\nNoch keine gebundene Vorschau." : $"\nVorschau: {_job.PreviewHash}\nStatus: {AdminPlatformView.StateText(_job.State)}\n" +
                    string.Join("\n", new AetherVerificationEngine().Verify(_job)));
        }
        else
        {
            PlanOverview.Text = "Für diese Untersuchung wurde keine Verwaltungsaktion vorbereitet oder ausgeführt.";
            PlanText.Text = PlanOverview.Text;
        }
        if (_knowledge is not null)
        {
            KnowledgeSummary.Text = _knowledge.Sources.IsEmpty
                ? "Noch keine lokalen Wissensquellen importiert."
                : $"{_knowledge.Sources.Length} lokale Wissensquelle(n). Inhalte bleiben verschlüsselt und werden nur passend und freigegeben herangezogen.";
            KnowledgeText.Text = $"Index: cpu.lexical-hash/v1; keine trainierte semantische Ähnlichkeit.\n{_knowledge.Sources.Length} Quellen.\n\n" +
                string.Join("\n\n", _knowledge.Sources.Select(s => $"{s.Title} · v{s.Version} · {s.Classification}\n{s.Digest}\nGültig bis {s.Expires:O}"));
        }
        var entries = _models.Entries;
        var visibleEntries = entries.Where(entry => entry.Model.Id == AetherModelRegistry.Fallback.Id ||
            _models.IsAutomatic(entry.Model.Id)).ToImmutableArray();
        AutomaticModelStatus.Text = _automaticModelStatus;
        var registeredFamilies = visibleEntries.Select(entry => entry.Model.Family).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ModelRows.ItemsSource = visibleEntries.Select(entry =>
        {
            var runtime = _lifecycle.Status(entry.Model.Id);
            var status = ModelStateText(runtime.State) +
                (runtime.State == FabricModelLoadState.Verified ? " · Nicht geladen" : "");
            return new ModelOverviewRow(ModelName(entry.Model), status,
                entry.Model.Id == AetherModelRegistry.Fallback.Id ? "Regelbasierte Zusammenfassung" : "Lokaler Assistent",
                ProviderName(entry.Provider.Info.Kind), entry.Model.ExecutionProvider,
                MemoryRequirement(entry.Model), ContextRequirement(entry.Model));
        }).Concat(AetherModelRegistry.Candidates.Where(candidate => !registeredFamilies.Contains(candidate.Family))
            .Select(candidate => new ModelOverviewRow(candidate.Name, "Nicht eingerichtet", candidate.IntendedRole,
                "—", "Nicht eingerichtet", "Nicht ermittelt", "Nicht ermittelt"))).ToArray();
        ModelText.Text = "Kandidaten:\n" +
            string.Join("\n\n", AetherModelRegistry.Candidates.Select(candidate =>
                $"{candidate.Id} · {candidate.Name}\n{candidate.IntendedRole}\n{candidate.AcquisitionStatus}")) +
            "\n\nFreigegebene Modelle:\n" + string.Join("\n\n", entries.Select(entry =>
            {
                var runtime = _lifecycle.Status(entry.Model.Id);
                return $"ModelId: {entry.Model.Id}\nRevision: {entry.Model.Revision}\nFamilie: {entry.Model.Family}\n" +
                    $"Provider: {entry.Provider.Info.Kind} · {entry.Provider.Info.Detail}\nEndpoint: {EndpointClass(entry.Provider.Info.Endpoint)}\n" +
                    $"Digest: {entry.Model.Digest}\nQuantisierung: {entry.Model.Quantization}\nSprachen: {string.Join(", ", entry.Model.Languages)}\n" +
                    $"Kontext laut Manifest: {entry.Model.DeclaredContextTokens}\nPfad: {entry.Model.LocalPath ?? "Serververwaltet"}\n" +
                    $"Lizenzannahme: {entry.Model.LicenseAccepted}\nRuntime: {entry.Model.ExecutionProvider}\n" +
                    $"RAM/VRAM laut Manifest: {entry.Model.RequiredRamBytes}/{entry.Model.RequiredVramBytes} Bytes\n" +
                    $"Lifecycle: {ModelStateText(runtime.State)} ({runtime.State}) · {runtime.Detail} · {runtime.At:O}";
            })) +
            $"\n\nAnfragen aktiv: {_scheduler?.Active ?? 0}; Warteschlange: {_scheduler?.Queued ?? 0}.\n" +
            "Prompt- und Antwortinhalte werden nicht als Providerdiagnose gespeichert.";
        if (_audit is not null) AuditText.Text = string.Join("\n", _audit.Entries.Select(a => $"{a.At:O} · {a.Event} · {a.SubjectHash}"));
        if (_store is not null) { SavedJobs.ItemsSource = _store.Load<FabricPlanBinding>("plan").Select(b => new SavedRow(b)).ToArray(); SavedJobs.SelectedIndex = 0; }
        UpdateStatus();
        TraceState("status-refresh");
    }

    internal void PresentInvestigation(FabricInvestigation result, bool? statusView = null)
    {
        _investigation = result;
        _statusView = statusView;
        var answer = result.Assistant;
        var measuredStatus = answer is { RealModel: true, AnswerValidated: true };
        var fastSummary = answer is { RealModel: true, AnswerValidated: true, DiagnosticReport: null };
        var unvalidatedStatus = statusView == true && answer is { RealModel: true, AnswerValidated: false };
        var storageStatus = result.Evidence.FirstOrDefault(e => e.Field == "physicalStorageStatus");
        StorageStatusText.Visibility = storageStatus is not null &&
            storageStatus.Metadata.Availability != AetherOperations.DataAvailability.Available ? Visibility.Visible : Visibility.Collapsed;
        StorageStatusText.Text = "Physisches Storageinventar ist nicht verfügbar.";
        FindingsPanel.Visibility = measuredStatus && !fastSummary || unvalidatedStatus ? Visibility.Collapsed : Visibility.Visible;
        RecommendationsPanel.Visibility = measuredStatus && !fastSummary || unvalidatedStatus ? Visibility.Collapsed : Visibility.Visible;
        if (measuredStatus)
        {
            PresentMeasuredStatus(result);
            if (fastSummary) PresentFastAssistant(answer!);
            PresentDiagnosis(answer!.DiagnosticReport, result.Evidence);
        }
        else
        {
            DiagnosisPanel.Visibility = Visibility.Collapsed;
            if (unvalidatedStatus)
            {
                PresentMeasuredStatus(result);
            }
            else if (answer is { ModelAttempted: true, AnswerValidated: false } unvalidated)
            {
                SummaryText.Text = AssistantAnswerHeading(unvalidated) +
                    "\nRegelbasierte Beleganzeige anhand der aktuell verfügbaren Messwerte.";
                FindingsItems.ItemsSource = Array.Empty<FindingRow>();
                ShowEvidence(result.Evidence.Where(e => e.Value is not null &&
                    e.Metadata.Availability == AetherOperations.DataAvailability.Available));
                RecommendationsItems.ItemsSource = Array.Empty<string>();
            }
            else
            {
                SummaryText.Text = answer is null ? FormatNarrative(result.Explanation) :
                    AssistantAnswerHeading(answer) + "\n" + FormatNarrative(answer.Answer.Summary);
                FindingsItems.ItemsSource = answer?.Answer.Findings.Where(f => answer.RealModel ||
                    !f.Evidence.Any(id => result.Evidence.Any(e => e.Id == id &&
                        (IsEventField(e.Field) || AetherToolBroker.IsPhysicalStorageField(e.Field) ||
                         AetherToolBroker.IsVolumeField(e.Field))))).Select(f =>
                {
                    var evidence = answer.RealModel || f.Evidence.Length != 1 ? null :
                        result.Evidence.FirstOrDefault(e => e.Id == f.Evidence[0]);
                    var text = evidence?.Value is { } value
                        ? $"{(evidence.Simulated ? "SIMULIERT · " : "")}{EvidenceTitle(evidence.Field)}: {EvidenceValue(evidence.Field, value, evidence.Unit)}"
                        : FormatNarrative(f.Text);
                    return new FindingRow(ProofText(f.Proof), text);
                }).ToArray() ?? [];
                ShowEvidence(result.Evidence.Where(e => e.Value is not null &&
                    e.Metadata.Availability == AetherOperations.DataAvailability.Available));
                RecommendationsItems.ItemsSource = answer?.Answer.Recommendations.Select(FormatNarrative).ToArray() ?? [];
            }
        }
        ActionsText.Text = result.Plan.Risk == 0
            ? "Es wurde keine Systemänderung ausgeführt. Änderungen benötigen eine eigene Vorschau und persönliche Freigabe."
            : "Mögliche Änderungen benötigen eine gesonderte Vorschau und persönliche Freigabe.";
        ModelAnswerText.Text = answer is { RealModel: true, AnswerValidated: true, DiagnosticReport: { } report }
            ? JsonSerializer.Serialize(report, FabricJson.Options)
            : answer is { RealModel: true, AnswerValidated: true } ? FormatAssistant(answer, result) :
            answer is { ModelAttempted: true, AnswerValidated: false }
                ? FormatAssistant(answer, result)
                : "Noch keine Modellantwort vorhanden.";
        Explanation.Text = (answer is { } assistant ? FormatAssistant(assistant, result) + "\n\n" : "") +
            result.Explanation + "\n\nHinweise:\n" + string.Join("\n", result.Issues) +
            "\n\nBelege und Identifier:\n" + string.Join("\n", result.Evidence.Select(e => $"{e.Id} · {e.TargetId} · {e.Field}: " +
                $"{(e.Metadata.Availability == AetherOperations.DataAvailability.Available ? e.Value ?? "Nicht verfügbar" : "Nicht verfügbar")} · " +
                $"{(e.Simulated ? "SIMULIERT · " : "")}{e.Metadata.Source} · {e.Metadata.Timestamp:O} · {e.Metadata.Availability}")) +
            "\n\nWissensquellen:\n" + string.Join("\n", result.Plan.Citations.Select(c => $"{c.SourceId} · v{c.Version} · {c.Digest}\n{c.Excerpt}"));
        UpdateStatus();
    }

    private void PresentDiagnosis(FabricDiagnosticReport? report, ImmutableArray<FabricEvidence> evidence)
    {
        DiagnosisPanel.Visibility = report is null ? Visibility.Collapsed : Visibility.Visible;
        if (report is null) return;
        var current = evidence.Where(e => AetherAnswerValidator.IsCurrentEvidence(e, DateTimeOffset.UtcNow))
            .Select(e => e.Id).ToHashSet();
        bool Fresh(IEnumerable<Guid> ids) => ids.All(current.Contains);
        string EvidenceName(Guid id)
        {
            var item = evidence.Single(e => e.Id == id);
            if (AetherToolBroker.IsEventGroupField(item.Field))
            {
                var group = AetherToolBroker.ValidateEventGroup(item.Value!);
                return $"{group.Log} · {group.Provider} · Ereignis-ID {group.EventId}";
            }
            if (AetherToolBroker.IsPhysicalStorageField(item.Field))
            {
                var disk = JsonSerializer.Deserialize<FabricPhysicalStorage>(item.Value!, FabricJson.Options)!;
                return disk.DiskNumber is { } number ? $"Datenträger {number}" : "Physisches Speichergerät";
            }
            if (AetherToolBroker.IsVolumeField(item.Field)) return "Volume " + AetherToolBroker.ValidateVolume(item.Value!).Drive;
            return EvidenceTitle(item.Field);
        }
        var hypotheses = report.Diagnosis.Hypotheses;
        bool FreshHypothesis(FabricDiagnosticHypothesis hypothesis) =>
            Fresh(hypothesis.Evidence) && Fresh(hypothesis.ContradictingEvidence);
        HypothesisItems.ItemsSource = hypotheses.Where(FreshHypothesis)
            .Select(h => FormatNarrative("Hypothese · unbestätigt (KI): " + h.Text + "\nBegründung: " + h.Reason +
                "\nStützende Belege: " + string.Join("; ", h.Evidence.Select(EvidenceName)) +
                (h.ContradictingEvidence.IsEmpty ? "" : "\nWidersprechende Belege: " +
                    string.Join("; ", h.ContradictingEvidence.Select(EvidenceName))) +
                "\nOffene Informationen: " + string.Join("; ", h.MissingOrContradictory))).ToArray();
        DiagnosticCheckItems.ItemsSource = report.Diagnosis.Checks.Where(c =>
                Fresh([c.SourceEvidence]) && FreshHypothesis(hypotheses[c.HypothesisIndex]))
            .Select(c => FormatNarrative($"Empfehlung: {c.Text}\nZiel: {c.Target} · Zeitraum: {c.Timeframe}\n" +
                $"Begründung: {c.Reason}\nErwartung: {c.ExpectedResult}" +
                (c.Module.Length == 0 ? "" : "\nAETHER-Modul: " + c.Module))).ToArray();
        DiagnosticSolutionItems.ItemsSource = report.Diagnosis.Solutions.Where(s => Fresh(s.Evidence) &&
                FreshHypothesis(hypotheses[s.HypothesisIndex]))
            .Select(s => FormatNarrative($"Empfehlung: {s.Target}\nBegründung: {s.Reason}\nVoraussetzung: {s.Prerequisite}\n" +
                $"Risiko: {s.Risk switch { FabricSolutionRisk.ReadOnly => "lesend", FabricSolutionRisk.Low => "niedrig",
                    FabricSolutionRisk.Elevated => "erhöht", _ => "nicht bewertet" }} · Benutzerfreigabe erforderlich\nRückweg: {s.Rollback}")).ToArray();
        DiagnosticActionItems.ItemsSource = DiagnosticNavigationRequested is null ? [] :
            report.Diagnosis.Actions.Where(a => Fresh(a.Evidence))
                .Select(a => AetherDiagnosticActions.All.Single(definition => definition.Id == a.ActionId)).ToArray();
        NoDiagnosticActionsText.Visibility = DiagnosticActionItems.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DiagnosticNavigationClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string route } &&
            AetherDiagnosticActions.All.Any(action => action.Route == route))
            DiagnosticNavigationRequested?.Invoke(route);
    }

    private void PresentMeasuredStatus(FabricInvestigation result)
    {
        var now = DateTimeOffset.UtcNow;
        var evidence = result.Evidence.Where(e => AetherAnswerValidator.IsCurrentEvidence(e, now)).ToArray();
        if (evidence.Any(e => e.Field == "operatingSystemProductName"))
            evidence = evidence.Where(e => e.Field != "operatingSystem").ToArray();
        SummaryText.Text = "Lokale KI wurde ausgeführt; ihre Antwort ist inhaltlich nicht verifiziert und steht in den technischen Details.\n" +
            (evidence.Length == 0 ? "Es liegen keine aktuellen Messbelege vor." : "Die Untersuchung zeigt die aktuellen Messbelege.");
        FindingsItems.ItemsSource = Array.Empty<FindingRow>();
        RecommendationsItems.ItemsSource = Array.Empty<string>();
        ShowEvidence(evidence);
    }

    private void PresentFastAssistant(FabricAssistantResult answer)
    {
        SummaryText.Text = "Lokale KI wurde ausgeführt; Antwortvertrag und Belegzuordnung sind formal geprüft, " +
            "der Inhalt ist inhaltlich nicht verifiziert. Anhand der angezeigten Belege prüfen.\n\n" +
            FormatNarrative(answer.Answer.Summary);
        FindingsItems.ItemsSource = answer.Answer.Findings
            .Select(finding => new FindingRow(ProofText(finding.Proof), FormatNarrative(finding.Text))).ToArray();
        RecommendationsItems.ItemsSource = answer.Answer.Recommendations.Select(FormatNarrative).ToArray();
    }

    private void ShowEvidence(IEnumerable<FabricEvidence> evidence)
    {
        var current = evidence.ToArray();
        var volumeTargets = current.Where(e => AetherToolBroker.IsVolumeField(e.Field)).Select(e => e.TargetId).ToHashSet();
        var rows = current.Where(e => !IsEventField(e.Field) &&
            !(volumeTargets.Contains(e.TargetId) && e.Field is "freeBytes" or "volumeDriveLetter" or "volumeFileSystem" or
                "volumeCapacityBytes" or "volumeUsedBytes" or "volumeFreeBytes" or "volumeFreePercent")).Select(e =>
        {
            if (e.Field == "physicalStorageSummary")
            {
                var summary = FabricJson.Parse<FabricPhysicalStorageSummary>(e.Value!);
                var capacity = summary.TotalCapacityDisplay ?? "Gesamtkapazität nicht verfügbar";
                var details = new List<string>();
                if (!string.IsNullOrWhiteSpace(summary.Status)) details.Add(summary.Status);
                details.AddRange(summary.HealthStatuses.Select(item => $"{item.Name}: {item.Count}"));
                if (summary.ManufacturerUnavailableCount > 0)
                    details.Add($"Herstellerangaben nicht verfügbar: {summary.ManufacturerUnavailableCount} von {summary.DeviceCount}");
                if (summary.ModelUnavailableCount > 0)
                    details.Add($"Modellangaben nicht verfügbar: {summary.ModelUnavailableCount} von {summary.DeviceCount}");
                if (summary.MediaTypeUnavailableCount > 0)
                    details.Add($"Medienangaben nicht verfügbar: {summary.MediaTypeUnavailableCount} von {summary.DeviceCount}");
                return new EvidenceRow("Physisches Speicherinventar",
                    $"{summary.DeviceCount} Geräte · {capacity}",
                    (e.Simulated ? "SIMULIERT · " : "") + string.Join(" · ", details));
            }
            if (e.Field == "volumeSummary")
            {
                var summary = FabricJson.Parse<FabricVolumeSummary>(e.Value!);
                return new EvidenceRow("Volumes",
                    string.Join(" · ", summary.Volumes.Select(volume => $"{volume.Drive} frei: {volume.FreeDisplay}")),
                    string.Join(" · ", summary.Volumes.Select(volume =>
                        $"{volume.Drive} {volume.FileSystem} · Gesamt: {volume.CapacityDisplay} · Belegt: {volume.UsedDisplay}")));
            }
            if (AetherToolBroker.IsVolumeField(e.Field))
            {
                var volume = AetherToolBroker.ValidateVolume(e.Value!);
                string Bytes(long value) => (value / 1073741824d).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " GB";
                return new EvidenceRow("Volume " + volume.Drive, "Frei: " + Bytes(volume.FreeBytes),
                    $"{(e.Simulated ? "SIMULIERT · " : "")}{volume.FileSystem} · Gesamt: {Bytes(volume.CapacityBytes)} · Belegt: {Bytes(volume.UsedBytes)}");
            }
            if (AetherToolBroker.IsPhysicalStorageField(e.Field))
            {
                var disk = JsonSerializer.Deserialize<FabricPhysicalStorage>(e.Value!, FabricJson.Options)
                    ?? throw new InvalidDataException("Physischer Storagebeleg fehlt.");
                var bus = disk.BusType == 17 ? "NVMe" : disk.BusType is { } busCode ? $"Bus {busCode}" : "Bus nicht verfügbar";
                var media = disk.MediaType switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Medium nicht verfügbar" };
                var label = disk.DiskNumber is { } number ? $"Datenträger {number}" : "Physisches Speichergerät";
                var capacity = disk.CapacityBytes is { } bytes
                    ? bytes >= 1_000_000_000_000
                        ? (bytes / 1_000_000_000_000d).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " TB"
                        : (bytes / 1_000_000_000d).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " GB"
                    : "Kapazität nicht verfügbar";
                var details = new List<string> { disk.Model ?? "Modell nicht verfügbar" };
                if (!string.IsNullOrWhiteSpace(disk.Manufacturer)) details.Add("Hersteller: " + disk.Manufacturer);
                if (!string.IsNullOrWhiteSpace(disk.Firmware)) details.Add("Firmware: " + disk.Firmware);
                if (disk.HealthStatus is { } health) details.Add("Windows-Healthstatus-Code: " + health);
                return new EvidenceRow($"{label} · {bus} · {media}", capacity,
                    (e.Simulated ? "SIMULIERT · " : "") + string.Join(" · ", details));
            }
            var proof = e.Metadata.SourceType switch
            {
                SourceType.Direct => "Gemessen",
                SourceType.Derived => "Abgeleitet",
                _ => "Quelle nicht bestätigt"
            };
            return new EvidenceRow(EvidenceTitle(e.Field), EvidenceValue(e.Field, e.Value!, e.Unit),
                (e.Simulated ? "SIMULIERT · " : "") + proof + " · " + EvidenceContext(e.Field, e.Metadata.Timestamp));
        }).ToList();
        foreach (var target in current.Where(e => IsEventField(e.Field)).GroupBy(e => e.TargetId))
        {
            var groups = target.Where(e => AetherToolBroker.IsEventGroupField(e.Field)).ToArray();
            if (groups.Length > 0)
            {
                foreach (var entry in groups)
                {
                    var group = AetherToolBroker.ValidateEventGroup(entry.Value!);
                    var simulationPrefix = entry.Simulated ? "SIMULIERT · " : "";
                    rows.Add(new("Ereignisgruppe · begrenzte Stichprobe", $"{group.Count} Einträge",
                        $"{simulationPrefix}{group.Log} · {group.Provider} · Ereignis-ID {group.EventId} · " +
                        $"{EvidenceValue("eventLevel", group.Level!.Value.ToString(CultureInfo.InvariantCulture), "")} · " +
                        $"{(group.Category is null ? "Kategorie nicht verfügbar" : EvidenceValue("eventCategory", group.Category, ""))}\n" +
                        $"{group.FirstSeen.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("de-DE"))} bis " +
                        $"{group.LastSeen.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("de-DE"))} · " +
                        "Meldung und betroffene Komponente nicht erfasst; keine Ursachenaussage."));
                }
                continue;
            }
            string? Value(string field) => target.FirstOrDefault(e => e.Field == field)?.Value;
            var log = Value("eventLog");
            var provider = Value("eventProvider");
            var id = Value("eventId");
            var count = Value("eventCount");
            var first = Value("eventFirstSeen");
            var last = Value("eventLastSeen");
            if (log is null || provider is null || id is null || count is null || first is null || last is null)
            {
                rows.Add(new("Ereignisgruppe", "Kontext nicht vollständig verfügbar",
                    "Keine Ereigniszahl ohne zugehörigen Provider, Ereignis-ID und Zeitraum."));
                continue;
            }
            var level = Value("eventLevel");
            var category = Value("eventCategory");
            var prefix = target.Any(e => e.Simulated) ? "SIMULIERT · " : "";
            rows.Add(new("Ereignisgruppe · begrenzte Stichprobe", $"{count} Einträge",
                $"{prefix}{log} · {provider} · Ereignis-ID {id} · " +
                $"{(level is null ? "Stufe nicht verfügbar" : EvidenceValue("eventLevel", level, ""))} · " +
                $"{(category is null ? "Kategorie nicht verfügbar" : EvidenceValue("eventCategory", category, ""))}\n" +
                $"{EvidenceValue("eventFirstSeen", first, "")} bis {EvidenceValue("eventLastSeen", last, "")} · " +
                "Meldung und betroffene Komponente nicht erfasst; keine Ursachenaussage."));
        }
        EvidenceItems.ItemsSource = rows;
        NoEvidenceText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsEventField(string field) => AetherToolBroker.IsEventGroupField(field) || field is
        "systemErrors24h" or "maximumEventRepetitions24h" or "eventLog" or "eventProvider" or "eventId" or
        "eventLevel" or "eventCategory" or "eventCount" or "eventFirstSeen" or "eventLastSeen";

    private void UpdateStatus()
    {
        if (_busy) return;
        var readiness = _host is null ? "Arbeitsbereich nicht geladen" :
            _investigation?.Assistant is { RealModel: true, AnswerValidated: true } ? "Modellantwort formal geprüft" :
            _investigation?.Assistant is { ModelAttempted: true, ModelInferenceSucceeded: true } attempted
                ? $"{ModelDisplayName(attempted.AttemptedModelId)} ausgeführt · Antwort verworfen · Fallback" :
            _investigation?.Assistant is { ModelAttempted: true } ? "Modellanfrage fehlgeschlagen · Fallback" :
            _investigation?.Assistant is not null ? "Regelbasierter Fallback" : "Bereit";
        Status.Text = $"Lokal · {readiness} · {_automaticModelStatus}";
    }

    private static string ModelName(FabricModel model) => model.Id == AetherModelRegistry.Fallback.Id
        ? "Regelbasiert" : model.Id.StartsWith("phi4-mini", StringComparison.OrdinalIgnoreCase)
            ? "Phi-4 Mini" : model.Family;

    private static string ProviderName(FabricProviderKind provider) => provider switch
    {
        FabricProviderKind.Ollama => "Ollama",
        FabricProviderKind.OpenAiCompatible => "OpenAI-kompatibel",
        FabricProviderKind.Deterministic => "Regelbasiert",
        FabricProviderKind.OnnxGenAi => "ONNX Runtime",
        FabricProviderKind.WindowsMl => "Windows ML",
        FabricProviderKind.Cuda => "CUDA",
        FabricProviderKind.DirectMl => "DirectML",
        _ => provider.ToString()
    };

    private static string MemoryRequirement(FabricModel model)
    {
        var parts = new List<string>();
        if (model.RequiredRamBytes > 0) parts.Add($"{model.RequiredRamBytes / 1073741824d:0.#} GB RAM");
        if (model.RequiredVramBytes > 0) parts.Add($"{model.RequiredVramBytes / 1073741824d:0.#} GB VRAM");
        return parts.Count == 0 ? "Nicht angegeben" : string.Join(" · ", parts) + " laut Manifest";
    }

    private static string ContextRequirement(FabricModel model) => model.DeclaredContextTokens <= 0
        ? "Nicht anwendbar" : $"{Math.Min(8192, model.DeclaredContextTokens) / 1024}K pro Anfrage";

    private static string ProofText(FabricProof proof) => proof switch
    {
        FabricProof.Observed => "Befund",
        FabricProof.Derived => "Abgeleitet",
        FabricProof.Hypothesis => "Hypothese",
        _ => "Nicht entscheidbar"
    };

    private static string EvidenceTitle(string field) => field switch
    {
        "cpuPercent" => "CPU-Auslastung",
        "freeBytes" => "Freier Speicher",
        "memoryAvailableBytes" => "Verfügbarer Arbeitsspeicher",
        "memoryTotalBytes" => "Gesamter Arbeitsspeicher",
        "systemErrors24h" => "Kritische und Fehlerereignisse",
        "maximumEventRepetitions24h" => "Häufigste Wiederholung",
        "operatingSystem" => "Betriebssystem",
        "operatingSystemProductName" => "Windows-Produkt",
        "operatingSystemEdition" => "Windows-Edition",
        "operatingSystemVersion" => "Windows-Release",
        "operatingSystemBuild" => "Windows-Build",
        "operatingSystemKernelVersion" => "Windows-Kernelversion",
        "architecture" => "Systemarchitektur",
        "volumeDriveLetter" => "Systemvolume",
        "volumeFileSystem" => "Systemvolume · Dateisystem",
        "volumeCapacityBytes" => "Systemvolume · Kapazität",
        "volumeUsedBytes" => "Systemvolume · belegt",
        "volumeFreeBytes" => "Systemvolume · frei",
        "volumeFreePercent" => "Systemvolume · frei",
        "eventProvider" => "Ereignisgruppe · Quelle",
        "eventId" => "Ereignisgruppe · Ereignis-ID",
        "eventLevel" => "Ereignisgruppe · Stufe",
        "eventCategory" => "Ereignisgruppe · Kategorie",
        "eventFirstSeen" => "Ereignisgruppe · zuerst",
        "eventLastSeen" => "Ereignisgruppe · zuletzt",
        "physicalStorageStatus" => "Physische Speichergeräte",
        "physicalStorageSummary" => "Physisches Speicherinventar",
        "volumeSummary" => "Volumes",
        _ => "Weitere Messung"
    };

    private static string EvidenceValue(string field, string value, string unit)
    {
        if (field is "cpuPercent" or "volumeFreePercent" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu))
            return cpu.ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " %";
        if (field is "freeBytes" or "memoryAvailableBytes" or "memoryTotalBytes" or "volumeCapacityBytes" or "volumeUsedBytes" or "volumeFreeBytes" &&
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
            return (bytes / 1073741824d).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " GB";
        if (field is "systemErrors24h" or "maximumEventRepetitions24h" &&
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            return count.ToString("N0", CultureInfo.GetCultureInfo("de-DE"));
        if (field is "eventFirstSeen" or "eventLastSeen" &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            return timestamp.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("de-DE"));
        if (field == "eventLevel") return value switch { "1" => "Kritisch", "2" => "Fehler", _ => value };
        if (field == "eventCategory") return value switch
        {
            "Boot" => "Start",
            "WindowsUpdate" => "Windows Update",
            "DeviceEvent" => "Geräteereignis",
            "NetworkEvent" => "Netzwerkereignis",
            "ApplicationCrash" => "Anwendungsabsturz",
            "ApplicationHang" => "Anwendung hängt",
            "ServiceFailure" => "Dienstfehler",
            "DeviceError" => "Gerätefehler",
            "WindowsError" => "Windows-Fehlerereignis",
            "WindowsEvent" => "Windows-Ereignis",
            _ => value
        };
        return value + (string.IsNullOrWhiteSpace(unit) ? "" : " " + unit);
    }

    private static string FormatNarrative(string text) => System.Text.RegularExpressions.Regex.Replace(text,
        @"(?<![\d.,-])(?<value>\d{1,3}(?:[.,\u00A0 ]\d{3})+|\d{7,})[ \u00A0]*(?:Bytes|Byte)\b",
        match =>
        {
            var digits = new string(match.Groups["value"].Value.Where(char.IsAsciiDigit).ToArray());
            if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes))
                return match.Value;
            var (divisor, unit) = bytes >= 1L << 40 ? (1099511627776d, "TB") :
                bytes >= 1L << 30 ? (1073741824d, "GB") :
                bytes >= 1L << 20 ? (1048576d, "MB") : (1d, "Bytes");
            return divisor == 1d ? match.Value :
                (bytes / divisor).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " " + unit;
        }, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static string EvidenceContext(string field, DateTimeOffset measuredAt) =>
        field is "systemErrors24h" or "maximumEventRepetitions24h" or "eventProvider" or "eventId" or "eventLevel" or "eventCategory" or "eventFirstSeen" or "eventLastSeen"
            ? "Begrenzte Stichprobe · letzte 24 Stunden"
            : field == "freeBytes" ? $"Systemvolume · für Benutzer verfügbar · {measuredAt.ToLocalTime():g}"
            : $"Messung · {measuredAt.ToLocalTime():g}";

    private static string ModelStateText(FabricModelLoadState state) => state switch
    {
        FabricModelLoadState.Registered => "Verfügbar",
        FabricModelLoadState.Checking => "Wird geprüft",
        FabricModelLoadState.NotInstalled => "Nicht eingerichtet",
        FabricModelLoadState.Verified => "Verifiziert",
        FabricModelLoadState.Loading => "Wird geladen",
        FabricModelLoadState.Ready => "Bereit",
        FabricModelLoadState.Error => "Fehler",
        FabricModelLoadState.ProviderMissing => "Nicht erreichbar",
        FabricModelLoadState.Unsupported => "Blockiert",
        _ => "Nicht eingerichtet"
    };
    private static string ModelDisplayName(string? modelId) =>
        modelId == "phi4-mini:3.8b" ? "Phi 4 Mini" :
        AetherProductModelCatalog.Current.Models.FirstOrDefault(model => model.ModelId == modelId)?.DisplayName ??
        modelId ?? "Das Sprachmodell";
    private string AssistantAnswerHeading(FabricAssistantResult result)
    {
        if (result.AnswerValidated)
            return "Formal geprüfte Modellantwort; inhaltlich anhand der Quellen prüfen";
        if (!result.ModelAttempted)
            return "Regelbasierte Zusammenfassung · kein Sprachmodell ausgeführt";
        var modelName = ModelDisplayName(result.AttemptedModelId);
        if (result.ModelInferenceSucceeded && result.FallbackReason == "OutputTruncated")
            return $"{modelName} wurde lokal ausgeführt. Die Modellantwort erreichte das Ausgabelimit und war unvollständig. " +
                "Deshalb wird eine regelbasierte Zusammenfassung angezeigt.";
        if (result.ModelInferenceSucceeded && result.FallbackReason == "ValidationFailed")
            return $"{modelName} wurde lokal ausgeführt. Die erzeugte Antwort konnte von AETHER nicht verwendet werden. " +
                "Deshalb wird eine regelbasierte Zusammenfassung angezeigt.";
        if (result.ModelInferenceSucceeded)
            return $"{modelName} wurde ausgeführt, aber keine validierte Modellantwort übernommen. " +
                "Es wird eine regelbasierte Zusammenfassung angezeigt.";
        return $"{modelName} wurde angefragt, aber die Inferenz nicht erfolgreich abgeschlossen. " +
            "Es wird eine regelbasierte Zusammenfassung angezeigt.";
    }
    private string FormatAssistant(FabricAssistantResult result, FabricInvestigation investigation) =>
        $"{AssistantAnswerHeading(result)}\n" +
        (result.DiagnosticReport is { } report ? JsonSerializer.Serialize(report, FabricJson.Options) :
            result.Answer.Summary + "\n\n" + string.Join("\n\n", result.Answer.Findings.Select(f =>
                $"{f.Proof}: {f.Text}\nEvidenceIds: {string.Join(", ", f.Evidence)}\nSourceIds: {string.Join(", ", f.Sources)}")) +
            "\n\nFehlende Angaben:\n" + string.Join("\n", result.Answer.Missing) +
            "\n\nLesende Empfehlungen (nicht ausgeführt):\n" + string.Join("\n", result.Answer.Recommendations)) +
        $"\n\nModelId: {result.ModelId}; Provider: {result.Provider}; Digest: " +
        $"{_models.Entries.SingleOrDefault(e => e.Model.Id == result.ModelId).Model?.Digest ?? "nicht verfügbar"}\n" +
        $"InvestigationId/TraceId: {investigation.Id}; lokal, keine externe Übertragung\nRouting: {result.Routing}\n" +
        $"Prompt: {result.PromptHash}; Kontext: {result.ContextCharacters} Zeichen\n" +
        $"Dauer: {result.Milliseconds:F1} ms; Eingabe-/Ausgabetoken: {result.InputTokens?.ToString() ?? "Nicht verfügbar"}/{result.OutputTokens?.ToString() ?? "Nicht verfügbar"}\n" +
        $"Laden: {result.LoadSeconds?.ToString("F3") ?? "Nicht verfügbar"} s; TTFT: {result.FirstTokenSeconds?.ToString("F3") ?? "Nicht verfügbar"} s; " +
        $"Tokenrate: {result.TokensPerSecond?.ToString("F2") ?? "Nicht verfügbar"} Token/s\n" +
        $"Cockpit-Prozessspeicher vorher/nachher: {result.ProcessBytesBefore}/{result.ProcessBytesAfter} Bytes (kein Server-RAM/VRAM)\n" +
        $"Validator: {result.Validator}\nBroker: Planaktion {investigation.Plan.ActionId}, Risiko {investigation.Plan.Risk}\n" +
        "Policy: Ergebnis muss vor jeder Änderung erneut geprüft und persönlich freigegeben werden.";
    private async void LoadJobClicked(object sender, RoutedEventArgs e)
    {
        var selected = SavedJobs.SelectedItem as SavedRow;
        _pending = Operate(_ =>
        {
            _plan = (selected ?? throw new InvalidOperationException("Gespeicherten Auftrag wählen.")).Binding.Plan;
            new AetherPolicyEngine(Required(_principal)).Validate(_plan, _targets);
            _job = Required(_platform).Load(selected.Binding.Job);
            return Task.CompletedTask;
        }); await _pending;
    }
    private async void ReconcileClicked(object sender, RoutedEventArgs e)
    { _pending = Operate(async token => _job = await Required(_approvals).ReconcileAsync(Required(_plan), Required(_job).Preview.Id, _targets, token)); await _pending; }
    private async void PreviewClicked(object sender, RoutedEventArgs e)
    { _pending = Operate(async token => _job = await Required(_approvals).PreviewAsync(Required(_plan), _targets, token)); await _pending; }
    private async void MarkerClicked(object sender, RoutedEventArgs e)
    {
        _pending = Operate(async token =>
        {
            _plan = new AetherPlanner(Required(_principal)).Marker(Required(_investigation), _targets[1]);
            _job = await Required(_approvals).PreviewAsync(_plan, _targets, token);
        }); await _pending;
    }
    private bool Confirm(string message) => MessageBox.Show(Window.GetWindow(this), message, "AETHER Intelligence · persönliche Bestätigung",
        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    private async void ApproveClicked(object sender, RoutedEventArgs e)
    {
        if (_plan is null || _job is null || !Confirm($"Genauen Plan und Vorschau für {_job.Preview.Targets.Length} Ziele freigeben?\nRisiko {_plan.Risk}\n{_job.PreviewHash}\nGültigkeit: 10 Minuten.")) return;
        _pending = Operate(_ => { _job = Required(_approvals).Approve(_plan, _job.Preview.Id, true); return Task.CompletedTask; }); await _pending;
    }
    private async void ExecuteClicked(object sender, RoutedEventArgs e)
    { _pending = Operate(async token => _job = await Required(_approvals).ExecuteAsync(Required(_plan), Required(_job).Preview.Id, token)); await _pending; }
    private async void RevokeClicked(object sender, RoutedEventArgs e)
    { _pending = Operate(_ => { _job = Required(_approvals).Revoke(Required(_job).Preview.Id); return Task.CompletedTask; }); await _pending; }
    private async void RollbackClicked(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Nur eigene unveränderte simulierte Testmarke zurücknehmen?")) return;
        _pending = Operate(async token => _job = await Required(_approvals).RollbackAsync(Required(_plan), Required(_job).Preview.Id, true, token)); await _pending;
    }
    private async void KnowledgeClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Textquellen (*.txt;*.md)|*.txt;*.md" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        _pending = Operate(_ =>
        {
            var text = ReadBounded(dialog.FileName, 16384);
            var now = DateTimeOffset.UtcNow;
            Required(_knowledge).Put(new(Guid.NewGuid(), Path.GetFileName(dialog.FileName), "1", "Bewusst lokal importierte Textquelle", text,
                PlatformJson.TextHash(text), now, now.AddDays(30), FabricTrust.OperatorApproved, AetherOperations.DataClassification.Sensitive, Required(_principal).Id, null));
            return Task.CompletedTask;
        }); await _pending;
    }
    private async void DeleteKnowledgeClicked(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Alle bewusst importierten Intelligence-Wissensquellen aus diesem Arbeitsbereich löschen?")) return;
        _pending = Operate(_ => { foreach (var source in Required(_knowledge).Sources) _knowledge!.Delete(source.Id); return Task.CompletedTask; }); await _pending;
    }
    private static string ReadBounded(string path, int maximum)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException("Nur lokale Dateien.");
        using var stream = File.OpenRead(path);
        if (stream.Length > maximum) throw new IOException("Import überschreitet das Größenlimit.");
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true)); return reader.ReadToEnd();
    }
    private async void ModelClicked(object sender, RoutedEventArgs e)
    {
        RequireDeveloperMode();
        var dialog = new OpenFileDialog { Filter = "Freigabemanifest (*.json)|*.json" };
        var accepted = dialog.ShowDialog(Window.GetWindow(this)) == true;
        _importTrace.Write("file-dialog", new { accepted });
        if (!accepted) return;
        _pending = ImportSelectedManifestAsync(dialog.FileName); await _pending;
    }
    internal Task ImportSelectedManifestAsync(string path) =>
        Operate(async token =>
        {
            _importTrace.Write("workspace-available", new { hostAvailable = _host is not null });
            await ImportModelManifestAsync(ReadBounded(path, 65536), token);
        }, "import");
    internal async Task ImportModelManifestAsync(string json, CancellationToken token)
    {
        TraceState("import-before");
        Registration registration;
        try { registration = FabricJson.Parse<Registration>(json); }
        catch (Exception ex) { _importTrace.Write("deserialize-error", new { errorType = ex.GetType().Name }); throw; }
        _importTrace.Write("deserialized", new
        {
            schema = registration.Schema, modelId = _importTrace.Id(registration.Model?.Id),
            provider = registration.Provider.ToString(), endpointClass = EndpointClass(registration.Endpoint),
            digestLength = registration.Model?.Digest?.Length, digestPresent = !string.IsNullOrEmpty(registration.Model?.Digest),
            digestFormatValid = registration.Model?.Digest is { Length: 64 } digest && digest.All(char.IsAsciiHexDigit),
            licenseAccepted = registration.Model?.LicenseAccepted, offline = registration.Model?.Offline,
            trust = registration.Model?.Trust.ToString(),
            localDigestComparison = registration.Model?.LocalPath is null ? "not-applicable" : "pending"
        });
        if (registration.Schema != 1 || registration.Model is null || registration.Endpoint is null || !registration.Model.Offline)
        {
            _importTrace.Write("schema-check", new { accepted = false });
            throw new InvalidDataException("Gültiges Manifest für ein lokales Modell und Schema 1 erforderlich.");
        }
        _importTrace.Write("schema-check", new { accepted = true });
        var provider = new CompatibleInferenceProvider("operator-" + Guid.NewGuid().ToString("N"), registration.Provider, registration.Endpoint);
        _importTrace.Write("register-call", new { registry = _models.DiagnosticId, modelId = _importTrace.Id(registration.Model.Id) });
        try { await Task.Run(() => _models.Register(registration.Model, provider), token); }
        catch (Exception ex) { provider.Dispose(); _importTrace.Write("register-error", new { errorType = ex.GetType().Name }); throw; }
        _providers.Add(provider);
        _lifecycle.Registered(registration.Model);
        _preferredModel = registration.Model.Id;
        RefreshModelChoice("import");
        _importTrace.Write("register-success", new { registry = _models.DiagnosticId,
            localDigestComparison = registration.Model.LocalPath is null ? "not-applicable" : "matched" });
        TraceState("import-registered");
    }
    private static string EndpointClass(Uri? endpoint)
    {
        if (endpoint is null || !endpoint.IsAbsoluteUri || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0 || endpoint.AbsolutePath != "/" || endpoint.Scheme is not ("http" or "https"))
            return "invalid";
        return System.Net.IPAddress.TryParse(endpoint.Host, out var address) && System.Net.IPAddress.IsLoopback(address)
            ? "loopback" : "external";
    }
    private void TraceState(string stage)
    {
        _importTrace.Write(stage, () =>
        {
            var entries = _models.Entries;
            var choice = FindName("ModelChoice") as ComboBox;
            var rows = choice?.Items.OfType<ModelRow>().Select(row => _importTrace.Id(row.Id)).ToArray();
            return new
            {
                view = _viewDiagnosticId, registry = _models.DiagnosticId,
                hostRegistry = _host?.RegistryDiagnosticId, routerRegistry = _host?.RouterRegistryDiagnosticId,
                registryCount = entries.Length,
                registryModels = entries.Select(entry => new
                {
                    id = _importTrace.Id(entry.Model.Id), provider = entry.Provider.Info.Kind.ToString(),
                    providerPresent = entry.Provider is CompatibleInferenceProvider p && _providers.Contains(p),
                    trust = entry.Model.Trust.ToString(), lifecycle = _lifecycle.Status(entry.Model.Id).State.ToString(),
                    installed = "unknown", verified = _lifecycle.Status(entry.Model.Id).State is FabricModelLoadState.Verified or FabricModelLoadState.Ready,
                    ready = _lifecycle.Status(entry.Model.Id).State == FabricModelLoadState.Ready,
                    offline = entry.Model.Offline, endpointClass = EndpointClass(entry.Provider.Info.Endpoint),
                    licenseAccepted = entry.Model.LicenseAccepted, digestPresent = !string.IsNullOrEmpty(entry.Model.Digest),
                    serverVisible = _serverVisibility.GetValueOrDefault(entry.Model.Id, "unknown"),
                    serverDigestComparison = _serverDigestComparison.GetValueOrDefault(entry.Model.Id, "not-checked"),
                    filter = entry.Model.Id == AetherModelRegistry.Fallback.Id ? "fallback-static-row" : "included"
                }).ToArray(),
                localOnly = Modes.SelectedItem is ModeRow { Value: FabricMode.LocalOnly },
                modelChoiceCount = choice?.Items.Count, modelRows = rows,
                selectedItem = choice?.SelectedItem is ModelRow selected ? _importTrace.Id(selected.Id) : "none",
                selectedModelId = _importTrace.Id(_preferredModel)
            };
        });
    }
    private async void HardwareClicked(object sender, RoutedEventArgs e)
    {
        _pending = Operate(async token =>
        {
            var model = _preferredModel is null ? AetherModelRegistry.Fallback :
                _models.Entries.Single(entry => entry.Model.Id == _preferredModel).Model;
            await RefreshHardwareSnapshotAsync(model, token);
            EvaluationText.Text = JsonSerializer.Serialize(_hardware, FabricJson.Options) +
                "\nEinzelprobe, keine Modellkapazitätszusage. TTFT nur bei tatsächlich gemessener Streamingantwort; hier unbekannt.";
        }); await _pending;
    }
    private async void EvaluateClicked(object sender, RoutedEventArgs e)
    {
        _pending = Operate(async token =>
        {
            var results = new List<string>();
            foreach (var entry in _models.Entries)
            {
                var result = await new AetherEvaluationEngine(Required(_scheduler), Required(_store)).RunAsync(entry.Model, entry.Provider, token);
                results.Add($"{result.Model}: {result.Results.Count(r => r.Passed)}/{result.Results.Length} begrenzte Vertragsfälle; keine allgemeine Qualitätsabnahme.\n" +
                    string.Join("\n", result.Results.Select(r => $"{r.Case}: {r.Validator} · {r.Milliseconds:F1} ms")));
            }
            EvaluationText.Text = string.Join("\n\n", results);
        }); await _pending;
    }
    private async void ProfileClicked(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Registrierte Pfade stufenweise mit öffentlichen künstlichen Eingaben prüfen? Kontextbudgets 1K bis 64K, Parallelität 1/2/4; keine ungemessene Kapazitätszusage und kein Serverentladen.")) return;
        _pending = Operate(async token =>
        {
            var results = ImmutableArray.CreateBuilder<FabricProfileStage>();
            foreach (var entry in _models.Entries)
                results.AddRange(await new AetherCapabilityProfiler(Required(_scheduler), Required(_governor)).MeasureAsync(entry.Model,
                    entry.Provider, [1024, 4096, 8192, 16384, 32768, 65536], [1, 2, 4], true, token));
            Required(_store).Save("profile", Guid.NewGuid(), results.ToImmutable());
            EvaluationText.Text = JsonSerializer.Serialize(results.ToImmutable(), FabricJson.Options);
        }); await _pending;
    }
    private async void ExportClicked(object sender, RoutedEventArgs e)
    {
        if (_job is null) { Status.Text = "Zuerst einen deterministischen Auftrag vorbereiten."; return; }
        var dialog = new SaveFileDialog { Filter = "Redigierter HTML-Bericht (*.html)|*.html", FileName = "AETHER-Intelligence-Bericht.html" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        _pending = Operate(token => File.WriteAllTextAsync(dialog.FileName, PlatformReports.Html(_job), token)); await _pending;
    }
    private void CancelClicked(object sender, RoutedEventArgs e) => _stop?.Cancel();
    public async Task ShutdownAsync()
    {
        _closing = true; _stop?.Cancel();
        _snapshotStatusTimer.Stop();
        try { await _pending; }
        finally
        {
            try { await LocalOllamaRuntime.StopOwnedAsync(); }
            finally
            {
                foreach (var provider in _providers) provider.Dispose();
                try { _scheduler?.Dispose(); _scheduler = null; }
                finally { _importTrace.Dispose(); }
            }
        }
    }
}
