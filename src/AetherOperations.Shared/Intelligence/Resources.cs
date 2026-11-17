using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace AetherOperations.Intelligence;

public enum FabricSnapshotFreshness { Current, Expiring, Expired, Unavailable }

public sealed class AetherModelTelemetry
{
    private readonly ConcurrentQueue<FabricMetric> _metrics = new();
    private readonly ConcurrentQueue<FabricRoleMetric> _roleMetrics = new();
    public ImmutableArray<FabricMetric> Snapshot => _metrics.ToImmutableArray();
    public ImmutableArray<FabricRoleMetric> RoleSnapshot => _roleMetrics.ToImmutableArray();
    public void Record(FabricMetric metric)
    {
        _metrics.Enqueue(metric);
        while (_metrics.Count > 512) _metrics.TryDequeue(out _);
    }
    public void RecordRoleOutcome(FabricRoleMetric metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        if (string.IsNullOrWhiteSpace(metric.Model) || metric.Model.Length > 256 ||
            !Enum.IsDefined(metric.Role) || !double.IsFinite(metric.Milliseconds) || metric.Milliseconds < 0)
            throw new ArgumentException("Ungültige Rollenmessung.", nameof(metric));
        _roleMetrics.Enqueue(metric);
        while (_roleMetrics.Count > 256) _roleMetrics.TryDequeue(out _);
    }
}

public sealed class AetherResourceGovernor(Func<FabricHardware> hardware,
    Func<FabricModel, CancellationToken, Task>? refreshHardware = null)
{
    public const int MaximumContextCharacters = 262144;
    public static readonly TimeSpan SnapshotLifetime = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan SnapshotExpiringAfter = TimeSpan.FromSeconds(45);
    public FabricHardware Hardware => hardware();
    public bool CanRefreshHardware => refreshHardware is not null;
    public Task RefreshAsync(FabricModel model, CancellationToken token) =>
        refreshHardware is null ? Task.CompletedTask : refreshHardware(model, token);
    public static FabricSnapshotFreshness GetSnapshotFreshness(FabricHardware state, DateTimeOffset now, bool requireVram)
    {
        if (state.At == default || state.At > now || state.AvailableRamBytes is null)
            return FabricSnapshotFreshness.Unavailable;
        var age = now - state.At;
        if (requireVram)
        {
            if (state.VramMeasuredAt is not { } measured || measured > now || state.AvailableVramBytes is null)
                return FabricSnapshotFreshness.Unavailable;
            age = age > now - measured ? age : now - measured;
        }
        if (age > SnapshotLifetime) return FabricSnapshotFreshness.Expired;
        return age >= SnapshotExpiringAfter ? FabricSnapshotFreshness.Expiring : FabricSnapshotFreshness.Current;
    }

    public void Check(FabricModel model, FabricRequest request, int concurrency, FabricMode mode)
    {
        if (!Enum.IsDefined(mode) || mode == FabricMode.Disabled || concurrency is < 1 or > 4 || request.Schema != 1 ||
            request.Id == Guid.Empty || request.TimeoutSeconds is < 1 or > 120 ||
            request.MaximumOutputTokens < 1 ||
            request.MaximumOutputTokens > (request.Task == "diagnosis-report" ? 2048 :
                request.Task == "assistant-report" ? 1536 : 1024) ||
            request.Prompt.Length > MaximumContextCharacters ||
            request.Task is not ("inventory-review" or "undecidable" or "assistant-report" or "diagnosis-report") ||
            request.ContextTokens is < 0 or > 65536 || request.Language is not ("de" or "en") ||
            request.PromptHash != AetherPromptRegistry.HashFor(request.Task, request.Prompt))
            throw new InvalidOperationException("Inferenzmodus oder Ressourcenvertrag ist ungültig.");
        if (model.Id == AetherModelRegistry.Fallback.Id) return;
        if (model.RequiredRamBytes <= 0 || !model.StructuredOutput || !model.Languages.Contains(request.Language) ||
            request.ContextTokens < 1 || request.ContextTokens > model.DeclaredContextTokens ||
            mode is FabricMode.LocalOnly or FabricMode.CpuOnly && !model.Offline ||
            mode == FabricMode.CpuOnly && model.RequiredVramBytes != 0)
            throw new InvalidOperationException("Modellfähigkeiten oder Offline-/CPU-Vorgabe nicht belegt.");
        var state = Hardware;
        var now = DateTimeOffset.UtcNow;
        var freshness = GetSnapshotFreshness(state, now, model.RequiredVramBytes > 0);
        if (freshness == FabricSnapshotFreshness.Expired)
            throw new FabricResourceException("Hardwaremessung ist abgelaufen; vor lokaler Inferenz ist eine erneute Messung erforderlich.");
        if (freshness == FabricSnapshotFreshness.Unavailable)
            throw new FabricResourceException("Hardwaremessung ist nicht verfügbar; lokale Inferenz startet erst nach erfolgreicher Messung.");
        if (state.AvailableRamBytes is not { } ram || model.RequiredRamBytes > (ram - 8L * 1024 * 1024 * 1024) / concurrency ||
            model.RequiredVramBytes > 0 && (state.AvailableVramBytes is not { } vram ||
                model.RequiredVramBytes > (vram - 2L * 1024 * 1024 * 1024) / concurrency))
            throw new FabricResourceException("Aktuelle RAM-/VRAM-Reserve reicht nicht; dieser Inferenzschritt wurde nicht gestartet.");
    }
}

public sealed class AetherInferenceScheduler : IDisposable
{
    private readonly AetherResourceGovernor governor;
    private readonly AetherModelTelemetry telemetry;
    private readonly SemaphoreSlim _slots;
    private readonly SemaphoreSlim _large = new(1, 1);
    private readonly object _resources = new();
    private long _reservedRam, _reservedVram;
    private int _queued, _active;
    public AetherInferenceScheduler(AetherResourceGovernor governor, AetherModelTelemetry telemetry,
        int maximumConcurrentRequests = 4)
    {
        if (maximumConcurrentRequests is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentRequests));
        this.governor = governor;
        this.telemetry = telemetry;
        _slots = new(maximumConcurrentRequests, maximumConcurrentRequests);
    }
    public int Queued => Volatile.Read(ref _queued);
    public int Active => Volatile.Read(ref _active);
    internal void RecordRoleOutcome(string model, FabricModelRole role, bool validated, double milliseconds) =>
        telemetry.RecordRoleOutcome(new(model, role, validated, milliseconds));
    public Task<FabricResponse> RunAsync(FabricModel model, IAetherInferenceProvider provider,
        FabricRequest request, FabricMode mode, CancellationToken token) =>
        RunCoreAsync(model, provider, request, mode, token, refreshHardware: true);

    internal Task<FabricResponse> RetryWithoutRefreshAsync(FabricModel model, IAetherInferenceProvider provider,
        FabricRequest request, FabricMode mode, CancellationToken token) =>
        RunCoreAsync(model, provider, request, mode, token, refreshHardware: false);

    private async Task<FabricResponse> RunCoreAsync(FabricModel model, IAetherInferenceProvider provider,
        FabricRequest request, FabricMode mode, CancellationToken token, bool refreshHardware)
    {
        if (Interlocked.Increment(ref _queued) > 16)
        { Interlocked.Decrement(ref _queued); throw new InvalidOperationException("Inferenzwarteschlange voll."); }
        var waiting = true; var slot = false; var large = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds is >= 1 and <= 120 ? request.TimeoutSeconds : 1));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var watch = Stopwatch.StartNew();
        var before = Environment.WorkingSet;
        string result = "Fehler"; FabricResponse? response = null;
        try
        {
            if (model.RequiredRamBytes >= 4L * 1024 * 1024 * 1024 || model.RequiredVramBytes > 0)
            { await _large.WaitAsync(linked.Token).ConfigureAwait(false); large = true; }
            await _slots.WaitAsync(linked.Token).ConfigureAwait(false); slot = true;
            Interlocked.Decrement(ref _queued); waiting = false;
            Interlocked.Increment(ref _active);
            var reserved = false;
            try
            {
                if (refreshHardware && model.Id != AetherModelRegistry.Fallback.Id &&
                    request.Classification == DataClassification.Sensitive &&
                    provider is CompatibleInferenceProvider { IsLoopback: true })
                    await governor.RefreshAsync(model, linked.Token).ConfigureAwait(false);
                governor.Check(model, request, 1, mode);
                if (model.Id != AetherModelRegistry.Fallback.Id)
                    lock (_resources)
                    {
                        var hardware = governor.Hardware;
                        if (hardware.AvailableRamBytes is not { } ram || _reservedRam + model.RequiredRamBytes > ram - (8L << 30) ||
                            model.RequiredVramBytes > 0 && (hardware.AvailableVramBytes is not { } vram ||
                                _reservedVram + model.RequiredVramBytes > vram - (2L << 30)))
                            throw new InvalidOperationException("Gemeinsames Inferenz-Speicherbudget bereits reserviert.");
                        _reservedRam += model.RequiredRamBytes; _reservedVram += model.RequiredVramBytes; reserved = true;
                    }
                response = await provider.InferAsync(model, request, linked.Token).ConfigureAwait(false);
                if (request.ModelId != model.Id || response.ModelId != model.Id || response.Json.Length > (provider.Info.Kind == FabricProviderKind.Deterministic ? 65536 : 16384) ||
                    response.InputTokens < 0 || response.OutputTokens < 0 || response.OutputTokens > request.MaximumOutputTokens ||
                    response.GenerationSeconds is { } seconds && (!double.IsFinite(seconds) || seconds < 0) ||
                    response.LoadSeconds is { } load && (!double.IsFinite(load) || load < 0) ||
                    response.FirstTokenSeconds is { } first && (!double.IsFinite(first) || first < 0))
                    throw new InvalidDataException("Providerergebnis verletzt Identitäts-/Ausgabevertrag.");
                result = "Erfolgreich";
                return response;
            }
            finally
            {
                if (reserved) lock (_resources) { _reservedRam -= model.RequiredRamBytes; _reservedVram -= model.RequiredVramBytes; }
                Interlocked.Decrement(ref _active);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { result = token.IsCancellationRequested ? "Abgebrochen" : "Zeitlimit"; throw; }
        finally
        {
            if (waiting) Interlocked.Decrement(ref _queued);
            if (slot) _slots.Release();
            if (large) _large.Release();
            telemetry.Record(new(request.Id, provider.Info.Id, model.Id, request.Task, watch.Elapsed.TotalMilliseconds,
                response?.InputTokens, response?.OutputTokens, before, Environment.WorkingSet, result,
                provider.Info.Kind == FabricProviderKind.Mock));
        }
    }
    public void Dispose()
    {
        if (Active != 0 || Queued != 0) throw new InvalidOperationException("Inferenz zuerst abbrechen und abwarten.");
        _slots.Dispose(); _large.Dispose();
    }
}

public sealed class AetherModelRouter(AetherModelRegistry registry, AetherModelTelemetry telemetry)
{
    private const int MinimumRoleObservationsPerCandidate = 1;
    public Guid RegistryDiagnosticId => registry.DiagnosticId;
    public (FabricModel Model, IAetherInferenceProvider Provider, string Reason) Route(string language,
        FabricMode mode, DataClassification classification, string? preference = null, int minimumContextTokens = 0,
        FabricHardware? hardware = null, bool automaticOnly = false,
        FabricModelRole role = FabricModelRole.DiagnosticReasoner)
    {
        if (mode == FabricMode.Disabled || !Enum.IsDefined(role))
            throw new InvalidOperationException("KI, deterministischer Assistent oder Modellrolle ist ungültig.");
        var scores = telemetry.Snapshot;
        if (preference == AetherModelRegistry.Fallback.Id)
        {
            var fixedPath = registry.Entries.Single(e => e.Model.Id == preference);
            return (fixedPath.Model, fixedPath.Provider, "Bewusst regelbasiert; kein Sprachmodell angefordert.");
        }
        if (preference is not null && !registry.Entries.Any(e => e.Model.Id == preference))
            throw new InvalidOperationException("Gewähltes Modell ist nicht freigegeben registriert.");
        var candidates = registry.Entries.Where(item => item.Model.Id != AetherModelRegistry.Fallback.Id &&
            (!automaticOnly || registry.IsAutomatic(item.Model.Id)) &&
            item.Provider.Info.Status == FabricStatus.Ready && item.Model.Languages.Contains(language) &&
            item.Model.StructuredOutput && item.Model.DeclaredContextTokens >= minimumContextTokens &&
            (preference is null || item.Model.Id == preference) &&
            (hardware is null || hardware.AvailableRamBytes is { } ram && item.Model.RequiredRamBytes <= ram - (8L << 30) &&
                (item.Model.RequiredVramBytes == 0 || hardware.AvailableVramBytes is { } vram && item.Model.RequiredVramBytes <= vram - (2L << 30))) &&
            (mode == FabricMode.ExternalAllowed || item.Model.Offline) &&
            (classification == DataClassification.Normal || item.Provider is CompatibleInferenceProvider { IsLoopback: true }) &&
            (mode != FabricMode.CpuOnly || item.Model.RequiredVramBytes == 0)).ToArray();
        var roleSamples = telemetry.RoleSnapshot.Where(metric => metric.Role == role)
            .GroupBy(metric => metric.Model, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var roleMeasuredForAll = candidates.Length > 0 && candidates.All(candidate =>
            roleSamples.TryGetValue(candidate.Model.Id, out var samples) &&
            samples.Length >= MinimumRoleObservationsPerCandidate);
        var orderedCandidates = candidates
            .OrderByDescending(item => item.Model.Id == preference)
            .ThenBy(item => roleMeasuredForAll && role == FabricModelRole.FastAssistant
                ? roleSamples[item.Model.Id].Where(metric => metric.Validated)
                    .Select(metric => metric.Milliseconds).DefaultIfEmpty(double.MaxValue).Average() : 0)
            .ThenByDescending(item => roleMeasuredForAll && role == FabricModelRole.DiagnosticReasoner
                ? roleSamples[item.Model.Id].Count(metric => metric.Validated) /
                  (double)roleSamples[item.Model.Id].Length : 0)
            .ThenBy(item => roleMeasuredForAll && role == FabricModelRole.DiagnosticReasoner
                ? roleSamples[item.Model.Id].Average(metric => metric.Milliseconds) : 0)
            .ThenBy(item => scores.Count(m => m.Model == item.Model.Id && m.Result != "Erfolgreich"))
            .ThenByDescending(item => item.Model.AutomaticSelectionPriority)
            .ThenBy(item => mode == FabricMode.Speed
                ? scores.Where(m => m.Model == item.Model.Id && m.Result == "Erfolgreich").Select(m => m.Milliseconds).DefaultIfEmpty(double.MaxValue).Average() : 0)
            .ThenBy(item => mode == FabricMode.Quality ? -item.Model.DeclaredContextTokens : item.Model.RequiredRamBytes).ToArray();
        var selected = orderedCandidates.FirstOrDefault();
        if (selected.Model is not null)
            return (selected.Model, selected.Provider, roleMeasuredForAll
                ? $"Rolle {role}: alle geeigneten Kandidaten besitzen mindestens eine Rollenmessung; gemessene Rollenleistung berücksichtigt."
                : $"Rolle {role}: noch keine ausreichenden Rollenmessungen für alle Kandidaten; bisherige freigegebene Priorität bleibt maßgeblich.");
        var fallback = registry.Entries.Single(e => e.Model.Id == AetherModelRegistry.Fallback.Id);
        return (fallback.Model, fallback.Provider, $"Kein freigegebenes geeignetes Sprachmodell für {role}; ausdrücklicher deterministischer Pfad.");
    }
}

public sealed class AetherCapabilityProfiler(AetherInferenceScheduler scheduler, AetherResourceGovernor governor)
{
    public async Task<ImmutableArray<FabricProfileStage>> MeasureAsync(FabricModel model, IAetherInferenceProvider provider,
        ImmutableArray<int> contexts, ImmutableArray<int> parallelism, bool confirmed, CancellationToken token)
    {
        if (!confirmed || contexts.IsDefaultOrEmpty || contexts.Length > 6 || contexts.Any(c => c is not (1024 or 4096 or 8192 or 16384 or 32768 or 65536)) ||
            parallelism.IsDefaultOrEmpty || parallelism.Any(c => c is not (1 or 2 or 4)) || parallelism.Length > 3)
            throw new ArgumentException("Explizite begrenzte Profilfreigabe erforderlich.");
        var results = ImmutableArray.CreateBuilder<FabricProfileStage>();
        foreach (var context in contexts.Distinct().Order())
        foreach (var concurrent in parallelism.Distinct().Order())
        {
            token.ThrowIfCancellationRequested();
            var watch = Stopwatch.StartNew(); var before = Environment.WorkingSet;
            var prompt = AetherContextManager.ProfilePrompt(Math.Min(context * 3, 240000));
            var request = new FabricRequest(1, Guid.NewGuid(), "inventory-review", "de", prompt, AetherPromptRegistry.Hash,
                model.Id, model.Id == AetherModelRegistry.Fallback.Id ? 0 : context, 512, 60, DataClassification.Normal);
            try
            {
                governor.Check(model, request, concurrent, FabricMode.LocalOnly);
                var responses = await Task.WhenAll(Enumerable.Range(0, concurrent).Select(_ => scheduler.RunAsync(model,
                    provider, request with { Id = Guid.NewGuid() }, FabricMode.LocalOnly, token))).ConfigureAwait(false);
                var first = responses[0];
                results.Add(new(model.Id, context, concurrent, first.InputTokens, watch.Elapsed.TotalMilliseconds,
                    first.GenerationSeconds is > 0 && first.OutputTokens is { } tokens ? tokens / first.GenerationSeconds : null,
                    Environment.WorkingSet - before,
                    model.Id == AetherModelRegistry.Fallback.Id ? "Modelllos; keine Token-/Modellkontextmessung" :
                        first.InputTokens is null ? "Tokenzahl nicht belegt; keine Kontextfreigabe" : "Gemessen; tatsächliche Eingabetoken beachten",
                    provider.Info.Kind == FabricProviderKind.Mock));
                if (model.Id == AetherModelRegistry.Fallback.Id || first.InputTokens is null) return results.ToImmutable();
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or InvalidDataException or OperationCanceledException && !token.IsCancellationRequested)
            {
                results.Add(new(model.Id, context, concurrent, null, watch.Elapsed.TotalMilliseconds, null,
                    Environment.WorkingSet - before, "Gestoppt: " + ex.GetType().Name + "; Entladen nicht als bestätigt behauptet", provider.Info.Kind == FabricProviderKind.Mock));
                return results.ToImmutable();
            }
        }
        return results.ToImmutable();
    }
}
