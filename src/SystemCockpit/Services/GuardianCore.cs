using System.Diagnostics;
using System.IO;
using AetherOperations;

namespace SystemCockpit.Services;

internal interface IGuardianAction
{
    Task<GuardianTarget> ReadAsync(GuardianTarget target, CancellationToken token);
    Task<string> ExecuteAsync(GuardianPlan plan, CancellationToken token);
}
internal sealed class GuardianJournalException(GuardianTransaction transaction, Exception inner)
    : IOException("Guardian-Journal konnte nicht gesichert werden. Systemwirkung anhand des tatsächlichen Zielzustands prüfen.", inner)
{
    internal GuardianTransaction Transaction { get; } = transaction;
}
internal sealed class GuardianCore(GuardianVault vault, Func<GuardianContext> context, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    internal event Action<GuardianTransaction>? Changed;
    internal static event Action<GuardianTransaction>? TransactionPersisted;
    internal static string JournalHash(ChangeJournalEntry entry) => EvidenceFile.Hash(
        System.Text.Json.JsonSerializer.Serialize(entry with { Hash = "" }, IntelligenceModel.Json));
    internal GuardianTransaction Plan(string title, GuardianPlan plan, Guid? rollbackOf = null)
    {
        plan.Validate();
        var now = _clock.GetUtcNow();
        var tx = new GuardianTransaction(1, Guid.NewGuid(), title, context(), plan, now, null, null,
            ChangeState.Planned, null, "Noch nicht geprüft", "Noch nicht ausgeführt", [], [], rollbackOf,
            DataClassification.Sensitive, IntelligenceModel.Metadata("AETHER Guardian / lokaler Änderungsvertrag", now));
        return Append(tx, ChangeState.Planned, "Plan erstellt; noch keine Systemänderung.", 0);
    }
    private GuardianTransaction Append(GuardianTransaction tx, ChangeState state, string detail, double elapsed)
    {
        var item = new ChangeJournalEntry(tx.Journal.Count, state, _clock.GetUtcNow(), elapsed,
            detail, tx.Journal.LastOrDefault()?.Hash ?? "", "");
        item = item with { Hash = JournalHash(item) };
        return tx with { State = state, Journal = tx.Journal.Append(item).ToArray() };
    }
    private async Task PersistAsync(GuardianTransaction tx)
    {
        try { await vault.SaveTransactionAsync(tx, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new GuardianJournalException(tx, ex); }
        Changed?.Invoke(tx);
        TransactionPersisted?.Invoke(tx);
    }
    internal async Task<GuardianTransaction> ExecuteAsync(GuardianTransaction transaction, IGuardianAction action,
        bool confirmed, CancellationToken token)
    {
        transaction.Validate();
        if (!confirmed) throw new InvalidOperationException("Eine ausdrückliche Bestätigung des Change Plans ist erforderlich.");
        if (transaction.State != ChangeState.Planned || transaction.Context != context())
            throw new InvalidOperationException("Plan wurde bereits verwendet oder Rechner-/Benutzerkontext hat sich geändert.");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        var tx = transaction; var start = _clock.GetTimestamp(); var attempted = false; var created = false;
        double Elapsed() => _clock.GetElapsedTime(start).TotalMilliseconds;
        async Task Stage(ChangeState state, string detail)
        {
            tx = Append(tx, state, detail, Elapsed());
            await PersistAsync(tx).ConfigureAwait(false);
        }
        try
        {
            await vault.CreateTransactionAsync(tx, token).ConfigureAwait(false);
            created = true;
            tx = tx with { Started = _clock.GetUtcNow() };
            await Stage(ChangeState.Preflight, "Identität, Ausgangszustand und Kontext erneut lesen.").ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var actual = await action.ReadAsync(tx.Plan.Before, token).ConfigureAwait(false);
            actual.Validate();
            if (tx.Context != context() || !Matches(tx.Plan.Before, actual))
                throw new InvalidOperationException("Zielidentität oder Ausgangszustand hat sich seit der Planung geändert.");
            await Stage(ChangeState.Ready, "Preflight bestanden.").ConfigureAwait(false);
            await Stage(ChangeState.Protected, tx.Plan.Protection).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (tx.Context != context()) throw new InvalidOperationException("Rechner-/Benutzerkontext vor Ausführung geändert.");
            await Stage(tx.RollbackOf is null ? ChangeState.Executing : ChangeState.RollingBack, "Ausführung beginnt; Wirkung muss anschließend geprüft werden.").ConfigureAwait(false);
            attempted = true;
            tx = tx with { NativeResult = await action.ExecuteAsync(tx.Plan, token).ConfigureAwait(false) };
            await Stage(ChangeState.Verifying, "Tatsächlichen Nachherzustand lesen.").ConfigureAwait(false);
            using var verification = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            var after = await action.ReadAsync(tx.Plan.Before, verification.Token).ConfigureAwait(false);
            after.Validate();
            tx = tx with { After = after, VerificationResult = SameIdentity(tx.Plan.Before, after) &&
                after.State == tx.Plan.ExpectedState ? "Zielidentität und erwarteter Zustand bestätigt." : "Nachherzustand entspricht nicht dem Vertrag." };
            var success = SameIdentity(tx.Plan.Before, after) && after.State == tx.Plan.ExpectedState;
            tx = tx with { Ended = _clock.GetUtcNow() };
            await Stage(success ? tx.RollbackOf is null ? ChangeState.Completed : ChangeState.RolledBack :
                tx.RollbackOf is null ? ChangeState.Partial : ChangeState.RollbackPartial, tx.VerificationResult).ConfigureAwait(false);
            return tx;
        }
        catch (GuardianJournalException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or
            OperationCanceledException or TimeoutException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            if (!created) throw;
            tx = tx with { Errors = tx.Errors.Append(ex.Message).Take(64).ToArray(), Ended = _clock.GetUtcNow() };
            if (attempted)
            {
                try
                {
                    using var verify = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                    tx = tx with { After = await action.ReadAsync(tx.Plan.Before, verify.Token).ConfigureAwait(false),
                        VerificationResult = "Ausführung unterbrochen/fehlgeschlagen; aktueller Zustand erneut gelesen, kein Erfolgsnachweis." };
                }
                catch (Exception verificationError) when (verificationError is InvalidOperationException or IOException or UnauthorizedAccessException or
                    OperationCanceledException or TimeoutException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
                {
                    tx = tx with { Errors = tx.Errors.Append(verificationError.Message).Take(64).ToArray(),
                        VerificationResult = "Nachherzustand nicht lesbar; mögliche Teilwirkung." };
                }
            }
            await Stage(attempted ? tx.RollbackOf is null ? ChangeState.Partial : ChangeState.RollbackFailed :
                ex is OperationCanceledException ? ChangeState.Cancelled : ChangeState.Failed, ex.Message).ConfigureAwait(false);
            return tx;
        }
        finally { _gate.Release(); }
    }
    internal static bool Matches(GuardianTarget planned, GuardianTarget actual) =>
        SameIdentity(planned, actual) && planned.State == actual.State;
    internal static bool SameIdentity(GuardianTarget planned, GuardianTarget actual) =>
        planned.Kind == actual.Kind && planned.Id == actual.Id && planned.Fingerprint == actual.Fingerprint;
    internal GuardianTransaction Rollback(GuardianTransaction original)
    {
        original.Validate();
        if (original.Context != context() || original.State is not (ChangeState.Completed or ChangeState.Partial) ||
            original.After is null || !SameIdentity(original.Plan.Before, original.After) ||
            original.Plan.Rollback is not (RollbackCapability.Available or RollbackCapability.Partial))
            throw new InvalidOperationException("Für diesen Vorgang ist kein automatischer Rückweg belegt.");
        if (original.Plan.Action == "restart") throw new InvalidOperationException("Ein Dienstneustart kann vergangene Laufzeit nicht wiederherstellen.");
        return Plan("Rückweg: " + original.Title, original.Plan with
        {
            Before = original.Plan.Before.Kind == "configuration" ? original.After with
                { Fields = new Dictionary<string, string>(original.After.Fields) { ["desired"] = original.Plan.Before.Fields["payload"] } } : original.After,
            ExpectedState = original.Plan.Before.State,
            Action = original.Plan.Before.Kind == "configuration" ? "restore-configuration" : original.Plan.Before.State switch { "Running" => "start", "Stopped" => "stop", "Enabled" => "enable", "Disabled" => "disable",
                _ => throw new InvalidOperationException("Ausgangszustand besitzt keinen definierten Rückweg.") },
            Rollback = RollbackCapability.NotAvailable,
            RollbackExplanation = "Rückweg ist selbst eine neue, erneut geprüfte und journalisierte Änderung."
        }, original.Id);
    }
}
