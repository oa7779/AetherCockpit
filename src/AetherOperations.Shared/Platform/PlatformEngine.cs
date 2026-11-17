using System.Collections.Immutable;

namespace AetherOperations.Platform;

public sealed class PlatformEngine(PlatformStore store, PlatformPrincipal principal, IEnumerable<IPlatformAdapter> adapters)
{
    private readonly ImmutableDictionary<TransportKind, IPlatformAdapter> _adapters = adapters.ToImmutableDictionary(a => a.Transport);
    private int _pause;
    public event Action<TargetExecution>? TargetFinished;
    public PlatformJob Inspect(Guid id)
    {
        using var lease = store.Acquire();
        var job = store.Load(id); Authorize(job); return job;
    }
    public void RequestPause() => Interlocked.Exchange(ref _pause, 1);
    private IPlatformAdapter Adapter(AdminTarget target) => _adapters.GetValueOrDefault(target.Transport)
        ?? throw new InvalidOperationException("Transport ist im Plattformmotor nicht konfiguriert; kein Fallback.");

    private PlatformJob Audit(PlatformJob job, PlatformJobState state, string detail, Guid? target = null)
    {
        if (job.Audit.Length >= 4096) throw new InvalidOperationException("Auditbudget erreicht; Auftrag wird nicht weiter ausgeführt.");
        var entry = new PlatformAudit(job.Audit.Length, DateTimeOffset.UtcNow, principal.Id, detail, target,
            job.Audit.LastOrDefault()?.Hash ?? "", "");
        return job with { State = state, Audit = job.Audit.Add(entry with { Hash = PlatformStore.AuditHash(entry) }) };
    }
    private void Authorize(PlatformJob job)
    {
        PlatformStore.Validate(job);
        foreach (var target in job.Preview.Targets)
            PlatformActions.Authorize(principal, target.Target, PlatformActions.Get(job.Preview.ActionId));
    }

    public async Task<PlatformJob> PreviewAsync(string actionId, ImmutableArray<AdminTarget> targets,
        ExecutionPolicy policy, CancellationToken token)
    {
        using var lease = store.Acquire();
        var action = PlatformActions.Get(actionId);
        PlatformActions.Validate(policy, targets.Length, action.Safety.Risk > 0);
        TargetInventory.Validate(new PlatformInventory(1, targets, []));
        var frozen = ImmutableArray.CreateBuilder<PlanTarget>();
        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();
            PlatformActions.Authorize(principal, target, action);
            var probe = await Adapter(target).ProbeAsync(target, token).ConfigureAwait(false);
            if (action.Safety.Risk > 0 && probe.MarkerHash.Length != 0)
                throw new InvalidOperationException("Mindestens eine eigene Marke besteht bereits; zuerst Rückweg oder Nachprüfung.");
            frozen.Add(new(target with { Facts = probe.Facts }, probe.State, probe.MarkerHash, PlatformJson.Hash(probe)));
        }
        var preview = new PlatformPreview(Guid.NewGuid(), actionId, action.Version, action.Hash,
            principal.Id, DateTimeOffset.UtcNow, frozen.ToImmutable(), policy,
            PlatformActions.Runbook(actionId).Id, PlatformActions.Runbook(actionId).Hash);
        var job = new PlatformJob(1, preview, preview.Hash, PlatformJobState.PreviewReady, [], [], []);
        job = Audit(job, PlatformJobState.AwaitingApproval, "Vorschau eingefroren; noch nichts geändert");
        store.Save(job);
        return job;
    }

    public PlatformJob Approve(Guid id, string previewHash, TimeSpan lifetime)
    {
        using var lease = store.Acquire();
        var job = store.Load(id);
        Authorize(job);
        if (job.State is not (PlatformJobState.AwaitingApproval or PlatformJobState.Paused or PlatformJobState.Approved) ||
            job.PreviewHash != previewHash || lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromHours(1))
            throw new InvalidOperationException("Freigabezustand, Vorschauhash oder Gültigkeit ist ungültig.");
        var now = DateTimeOffset.UtcNow;
        var approval = new PlatformApproval(principal.Id, principal.Role, previewHash, now, now + lifetime);
        job = Audit(job with { Approvals = [approval] }, PlatformJobState.Approved, "Genauen Vorschauhash freigegeben");
        store.Save(job);
        return job;
    }

    private void RequireApproval(PlatformJob job)
    {
        Authorize(job);
        if (!job.Approvals.Any(a => a.Actor == principal.Id && a.Role == principal.Role &&
                a.PreviewHash == job.PreviewHash && a.At <= DateTimeOffset.UtcNow && a.Expires > DateTimeOffset.UtcNow))
            throw new UnauthorizedAccessException("Aktuelle passende Freigabe fehlt oder ist abgelaufen.");
    }

    public PlatformJob Revoke(Guid id)
    {
        using var lease = store.Acquire();
        var job = store.Load(id); Authorize(job);
        if (job.State is not (PlatformJobState.Approved or PlatformJobState.AwaitingApproval or PlatformJobState.Paused))
            throw new InvalidOperationException("Laufende oder abgeschlossene Aufträge können nicht nachträglich widerrufen werden.");
        job = Audit(job with { Approvals = [] }, PlatformJobState.AwaitingApproval, "Freigabe widerrufen");
        store.Save(job); return job;
    }

    public async Task<PlatformJob> RunAsync(Guid id, CancellationToken token)
    {
        using var lease = store.Acquire();
        var job = store.Load(id);
        RequireApproval(job);
        if (job.State != PlatformJobState.Approved || job.Results.Any(r => r.State is TargetResultState.Running or TargetResultState.NeedsVerification or TargetResultState.RollingBack))
            throw new InvalidOperationException("Auftrag benötigt Freigabe oder manuelle Nachprüfung; keine automatische Wiederholung.");
        var policy = job.Preview.Policy;
        PlatformActions.Validate(policy, job.Preview.Targets.Length, PlatformActions.Get(job.Preview.ActionId).Safety.Risk > 0);
        if (policy.NotBefore > DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Geplanter Zeitpunkt ist noch nicht erreicht; kein Hintergrundstart.");
        using var runDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(policy.MaximumRuntimeSeconds));
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(token, runDeadline.Token);
        var executionToken = execution.Token;
        Interlocked.Exchange(ref _pause, 0);
        var pending = job.Preview.Targets.Where(t => !job.Results.Any(r =>
            r.TargetId == t.Target.Id && r.State == TargetResultState.Succeeded)).ToArray();
        var canary = Math.Min(policy.CanaryCount, pending.Length);
        var waves = new List<PlanTarget[]>();
        if (canary > 0) waves.Add(pending[..canary]);
        waves.AddRange(pending.Skip(canary).Chunk(policy.WaveSize));
        job = Audit(job, PlatformJobState.Running, "Ausführung nach erneuter Backendautorisierung");
        store.Save(job);
        var failures = 0;
        var firstWave = true;
        foreach (var wave in waves)
        {
            if (executionToken.IsCancellationRequested)
            {
                job = Audit(job, PlatformJobState.Cancelled, "Abgebrochen; abgeschlossene Änderungen bleiben erhalten");
                store.Save(job); return job;
            }
            if (_pause != 0 || policy.WindowEnd <= DateTimeOffset.UtcNow)
            {
                job = Audit(job, PlatformJobState.Paused, "Pausiert; keine weitere Welle gestartet");
                store.Save(job); return job;
            }
            try { RequireApproval(job); }
            catch (UnauthorizedAccessException)
            {
                job = Audit(job, PlatformJobState.Paused, "Freigabe abgelaufen; neue manuelle Freigabe erforderlich");
                store.Save(job); return job;
            }
            var now = DateTimeOffset.UtcNow;
            foreach (var item in wave)
                job = job with { Results = job.Results.Where(r => r.TargetId != item.Target.Id).Append(
                    new TargetExecution(item.Target.Id, TargetResultState.Running, now, null, null,
                        ImmutableDictionary<string, PlatformFact>.Empty, "Vor Adapteraufruf dauerhaft markiert")).ToImmutableArray() };
            job = Audit(job, PlatformJobState.Running, "Welle vor Ausführung dauerhaft markiert");
            store.Save(job);
            var results = new System.Collections.Concurrent.ConcurrentBag<TargetExecution>();
            await Parallel.ForEachAsync(wave, new ParallelOptions { MaxDegreeOfParallelism = policy.Workers },
                async (item, _) =>
                {
                    var result = await ExecuteTarget(job.Preview, item, executionToken).ConfigureAwait(false);
                    results.Add(result);
                    TargetFinished?.Invoke(result);
                }).ConfigureAwait(false);
            foreach (var result in results.OrderBy(r => r.TargetId))
            {
                job = job with { Results = job.Results.Where(r => r.TargetId != result.TargetId).Append(result).ToImmutableArray() };
                job = Audit(job, PlatformJobState.Running, "Zielergebnis: " + result.State, result.TargetId);
            }
            store.Save(job);
            if (results.Any(r => r.State == TargetResultState.NeedsVerification))
            {
                job = Audit(job, PlatformJobState.NeedsVerification, "Unklarer Änderungszustand; Nachprüfung erforderlich");
                store.Save(job); return job;
            }
            if (executionToken.IsCancellationRequested)
            {
                job = Audit(job, PlatformJobState.Cancelled, runDeadline.IsCancellationRequested ?
                    "Gesamtzeitlimit; kein automatischer Rückweg" : "Abbruch nach Welle; kein automatischer Rückweg");
                store.Save(job); return job;
            }
            failures += results.Count(r => r.State != TargetResultState.Succeeded);
            if (failures > policy.MaximumFailures || firstWave && canary > 0 && results.Any(r => r.State != TargetResultState.Succeeded))
            {
                job = Audit(job, PlatformJobState.Paused, "Canary oder Fehlergrenze gescheitert; manuelle Fortsetzung erforderlich");
                store.Save(job); return job;
            }
            firstWave = false;
        }
        var state = job.Results.All(r => r.State == TargetResultState.Succeeded) ? PlatformJobState.Succeeded : PlatformJobState.Partial;
        job = Audit(job, state, "Ausführung abgeschlossen; Zielergebnisse separat prüfen");
        store.Save(job); return job;
    }

    private async Task<TargetExecution> ExecuteTarget(PlatformPreview preview, PlanTarget item, CancellationToken token)
    {
        var started = DateTimeOffset.UtcNow;
        var facts = ImmutableDictionary<string, PlatformFact>.Empty;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(preview.Policy.TargetTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        if (preview.Policy.WindowEnd is { } end)
        {
            var remaining = end - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) linked.Cancel(); else linked.CancelAfter(remaining);
        }
        try
        {
            var target = item.Target;
            var action = PlatformActions.Get(preview.ActionId);
            PlatformActions.Authorize(principal, target, action);
            var adapter = Adapter(target);
            var probe = await adapter.ProbeAsync(target, linked.Token).ConfigureAwait(false);
            facts = probe.Facts;
            if (probe.State != ConnectionState.Available)
                return new(target.Id, TargetResultState.Failed, started, DateTimeOffset.UtcNow, 1, facts, "Verbindungsstatus: " + probe.State);
            if (action.Safety.Risk > 0 && probe.MarkerHash != item.ExpectedMarkerHash)
                return new(target.Id, TargetResultState.Failed, started, DateTimeOffset.UtcNow, 1, facts, "Vorbedingung geändert; neue Vorschau erforderlich");
            var marker = await adapter.ExecuteAsync(target, action.ActionId, preview.Id, item.ExpectedMarkerHash, linked.Token).ConfigureAwait(false);
            var after = await adapter.ProbeAsync(target, linked.Token).ConfigureAwait(false);
            if (after.State != ConnectionState.Available || action.Safety.Risk > 0 &&
                (marker != PlatformJson.TextHash(LocalPlatformAdapter.MarkerContent(preview.Id, target.Id)) || after.MarkerHash != marker))
                return new(target.Id, TargetResultState.NeedsVerification, started, DateTimeOffset.UtcNow, 2, facts, "Nachzustand nicht bestätigt", marker);
            return new(target.Id, TargetResultState.Succeeded, started, DateTimeOffset.UtcNow, 0, after.Facts,
                target.Simulated ? "SIMULIERT; Zielzustand verifiziert" : "Lokaler Zielzustand verifiziert", marker);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return new(item.Target.Id, preview.ActionId == "test.marker" ? TargetResultState.NeedsVerification :
                token.IsCancellationRequested ? TargetResultState.Cancelled : TargetResultState.TimedOut,
                started, DateTimeOffset.UtcNow, null, facts, "Abbruch oder Zielzeitlimit; mögliche Änderung erneut prüfen");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            System.Diagnostics.Trace.TraceError("AETHER Plattformadapter: {0}", ex);
            return new(item.Target.Id, preview.ActionId == "test.marker" ? TargetResultState.NeedsVerification : TargetResultState.Failed,
                started, DateTimeOffset.UtcNow, 1, facts, "Adapterfehler: " + ex.GetType().Name + "; keine Erfolgsaussage");
        }
    }

    public async Task<PlatformJob> ReconcileAsync(Guid id, CancellationToken token)
    {
        using var lease = store.Acquire();
        var job = store.Load(id); Authorize(job);
        if (job.State is not (PlatformJobState.Running or PlatformJobState.NeedsVerification))
            throw new InvalidOperationException("Nur unterbrochene oder unklare Aufträge nachprüfen.");
        foreach (var result in job.Results.Where(r => r.State is TargetResultState.Running or TargetResultState.NeedsVerification or TargetResultState.RollingBack).ToArray())
        {
            var target = job.Preview.Targets.Single(t => t.Target.Id == result.TargetId).Target;
            var probe = await Adapter(target).ProbeAsync(target, token).ConfigureAwait(false);
            var expected = PlatformJson.TextHash(LocalPlatformAdapter.MarkerContent(id, target.Id));
            var state = probe.State != ConnectionState.Available ? TargetResultState.NeedsVerification :
                result.State == TargetResultState.RollingBack && probe.MarkerHash.Length == 0 ? TargetResultState.RolledBack :
                job.Preview.ActionId == "inventory.basic" || probe.MarkerHash.Length == 0 ? TargetResultState.Failed :
                probe.MarkerHash == expected ? TargetResultState.Succeeded : TargetResultState.NeedsVerification;
            job = job with { Results = job.Results.Replace(result, result with { State = state, Ended = DateTimeOffset.UtcNow,
                Facts = probe.Facts,
                ExitCode = state == TargetResultState.Succeeded ? 0 : 1,
                MarkerHash = state == TargetResultState.Succeeded && job.Preview.ActionId == "test.marker" ? expected : result.MarkerHash,
                Detail = "Manuelle Nachprüfung: " + state }) };
        }
        job = Audit(job with { Approvals = [] }, job.Results.Any(r => r.State == TargetResultState.NeedsVerification)
            ? PlatformJobState.NeedsVerification : job.Results.All(r => r.State == TargetResultState.RolledBack)
                ? PlatformJobState.RolledBack : PlatformJobState.Paused, "Nachprüfung; Freigaben verworfen, keine Aktion wiederholt");
        store.Save(job); return job;
    }

    public async Task<PlatformJob> RollbackAsync(Guid id, string previewHash, CancellationToken token)
    {
        using var lease = store.Acquire();
        var job = store.Load(id); Authorize(job);
        if (previewHash != job.PreviewHash || job.Preview.ActionId != "test.marker" ||
            job.State is PlatformJobState.Running or PlatformJobState.RolledBack ||
            job.Results.Any(r => r.State is TargetResultState.NeedsVerification or TargetResultState.Running or TargetResultState.RollingBack) ||
            !job.Results.Any(r => r.MarkerHash is not null && r.State is TargetResultState.Succeeded or TargetResultState.RollbackFailed))
            throw new InvalidOperationException("Explizit bestätigter Rückweg für eigene Marken erforderlich.");
        foreach (var result in job.Results.Where(r => r.State is TargetResultState.Succeeded or TargetResultState.RollbackFailed && r.MarkerHash is not null).ToArray())
        {
            token.ThrowIfCancellationRequested();
            var target = job.Preview.Targets.Single(t => t.Target.Id == result.TargetId).Target;
            var pending = result with { State = TargetResultState.RollingBack, Detail = "Rückweg vor Adapteraufruf dauerhaft markiert" };
            job = Audit(job with { Results = job.Results.Replace(result, pending) },
                PlatformJobState.NeedsVerification, "Rückweg vor Ausführung markiert", target.Id);
            store.Save(job);
            TargetResultState state;
            try
            {
                await Adapter(target).RollbackAsync(target, id, result.MarkerHash!, token).ConfigureAwait(false);
                var probe = await Adapter(target).ProbeAsync(target, token).ConfigureAwait(false);
                state = probe.State == ConnectionState.Available && probe.MarkerHash.Length == 0 ? TargetResultState.RolledBack : TargetResultState.RollbackFailed;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { System.Diagnostics.Trace.TraceError("AETHER Plattformrückweg: {0}", ex); state = TargetResultState.RollbackFailed; }
            job = job with { Results = job.Results.Replace(pending, result with { State = state, Detail = "Rückweg: " + state }) };
            job = Audit(job, PlatformJobState.NeedsVerification, "Rückweg: " + state, target.Id);
            store.Save(job);
        }
        job = Audit(job, job.Results.Any(r => r.MarkerHash is not null && r.State != TargetResultState.RolledBack)
            ? PlatformJobState.RollbackFailed : PlatformJobState.RolledBack, "Rückwegprüfung abgeschlossen");
        store.Save(job); return job;
    }
}
