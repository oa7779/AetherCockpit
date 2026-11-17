using System.Runtime.ExceptionServices;

namespace SystemCockpit.Services;

// An invocation receipt is not a verified system-state transaction or a generic rollback.
internal static class GuardianInvocation
{
    internal static event Action<GuardianTransaction>? Recorded;
    internal static Task<T> RunAsync<T>(string route, Func<CancellationToken, Task<T>> execute, CancellationToken token) =>
        RunAsync(route, execute, new GuardianVault(GuardianVault.DefaultDirectory), IntelligenceSources.GuardianContext(), token);

    internal static async Task<T> RunAsync<T>(string route, Func<CancellationToken, Task<T>> execute,
        GuardianVault vault, GuardianContext context, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("D");
        var target = new GuardianTarget("invocation", id, EvidenceFile.Hash("invocation:" + id), "NotInvoked",
            new Dictionary<string, string> { ["route"] = route, ["scope"] = "Aufrufidentität; keine erfundene Systemzielidentität" });
        var plan = new GuardianPlan("manual-invocation", target, "VerifiedSystemState", "Rechte des vorhandenen Ausführungswegs",
            "Abhängigkeiten im vorhandenen Dialog; kein generischer Nachweis", "Wirkung des ausdrücklich bestätigten bestehenden Werkzeugs",
            "Teilwirkungen und Datenverlust möglich; Prozessende/Exitcode ist kein Zustandsnachweis",
            "Kein zusätzlicher Neustart durch Guardian", "Aufruf vor Wirkung dauerhaft protokolliert. Keine allgemeine Konfigurationssicherung; kein garantierter Rückweg.",
            "Nur Aufrufabschluss dokumentierbar. Systemzustand bleibt ungeprüft.", RollbackCapability.ManualOnly,
            "Manueller externer Rückweg erforderlich; keine automatische Wiederholung.");
        var action = new InvocationAction<T>(target, execute);
        var core = new GuardianCore(vault, () => context);
        var tx = await core.ExecuteAsync(core.Plan(route, plan), action, true, token).ConfigureAwait(false);
        Recorded?.Invoke(tx);
        if (action.Failure is { } failure) ExceptionDispatchInfo.Capture(failure).Throw();
        if (!action.HasResult) throw new InvalidOperationException("Guardian hat den Aufruf vor der Wirkung gesperrt: " + string.Join(" | ", tx.Errors));
        return action.Result!;
    }

    private sealed class InvocationAction<T>(GuardianTarget target, Func<CancellationToken, Task<T>> execute) : IGuardianAction
    {
        internal T? Result;
        internal bool HasResult;
        internal Exception? Failure;
        private bool _invoked;
        public Task<GuardianTarget> ReadAsync(GuardianTarget expected, CancellationToken token) =>
            Task.FromResult(target with { State = _invoked ? "SystemStateUnverified" : "NotInvoked" });
        public async Task<string> ExecuteAsync(GuardianPlan plan, CancellationToken token)
        {
            _invoked = true;
            try { Result = await execute(token).ConfigureAwait(false); HasResult = true; }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or
                System.ComponentModel.Win32Exception or OperationCanceledException or TimeoutException or ArgumentException or
                System.Text.Json.JsonException or System.Security.SecurityException or NotSupportedException)
            {
                Failure = ex;
                throw new InvalidOperationException("Bestehender Aufruf fehlgeschlagen; sensible Befehle und Ausgaben werden nicht in Guardian übernommen.");
            }
            return "Vorhandener Ausführungsweg beendet. Kein generischer Systemerfolg oder Rückweg behauptet.";
        }
    }
}
