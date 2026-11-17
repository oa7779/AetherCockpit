using System.Text.Json;

namespace SystemCockpit.Services;

internal sealed record GuardianMaintenanceStep(Guid Id, GuardianPlan Plan, string State = "Planned",
    Guid? TransactionId = null, string Result = "Ausstehend", string Verification = "Nicht geprüft");

internal static class GuardianMaintenance
{
    private const string Key = "transactionSteps";
    private static readonly string ProcessSession = Guid.NewGuid().ToString("N");
    internal static IReadOnlyList<GuardianMaintenanceStep> Steps(GuardianArtifact maintenance)
    {
        maintenance.Validate();
        if (maintenance.Kind != RecoveryKind.Maintenance || !maintenance.Fields.TryGetValue(Key, out var json))
            throw new InvalidOperationException("Kein schrittgebundener Wartungsplan. Alte manuelle Notizen nicht automatisch ausführbar.");
        var steps = JsonSerializer.Deserialize<GuardianMaintenanceStep[]>(json, IntelligenceModel.Json)
            ?? throw new JsonException("Wartungsschritte fehlen.");
        if (steps.Length is < 1 or > 16 || steps.Any(s => s is null) || steps.Select(s => s.Id).Distinct().Count() != steps.Length ||
            steps.Any(s => s.Id == Guid.Empty || s.Plan is null || s.Result is null || s.Verification is null ||
                s.State is not ("Planned" or "Ready" or "Executing" or "Completed" or "Unknown" or "Failed")))
            throw new JsonException("Ungültige Wartungsschritte.");
        foreach (var step in steps) step.Plan.Validate();
        return steps;
    }
    private static GuardianArtifact Save(GuardianArtifact item, IReadOnlyList<GuardianMaintenanceStep> steps, string state, bool pinned = true)
    {
        var json = JsonSerializer.Serialize(steps, IntelligenceModel.Json);
        if (json.Length > 60000) throw new InvalidOperationException("Wartungsplan zu groß; höchstens 16 begrenzte Schritte.");
        return item with { Pinned = pinned, Fields = new Dictionary<string, string>(item.Fields)
            { [Key] = json, ["state"] = state, ["verification"] = "Schritte einzeln prüfen; keine automatische Wiederholung nach Neustart." } };
    }
    internal static GuardianArtifact Create(string name, GuardianPlan plan, IntelligenceSnapshot snapshot, GuardianContext context)
    {
        plan.Validate();
        if (snapshot.HostScope != context.Host) throw new InvalidOperationException("Wartungsinventar gehört nicht zum aktuellen Hostkontext.");
        var item = GuardianVault.Artifact(RecoveryKind.Maintenance, name, context,
            "Schrittgebundener Transaktionsplan: Identität, Preconditions, Schutz, Wirkung, Prüfung, Neustartgrenze und Rückweg je Change Plan. Kein automatischer Neustart.",
            snapshot: snapshot, pinned: true);
        return Save(item, [new(Guid.NewGuid(), plan)], "Planned");
    }
    internal static GuardianArtifact Add(GuardianArtifact item, GuardianPlan plan, GuardianContext context)
    {
        plan.Validate();
        Check(item, context);
        var steps = Steps(item);
        if (steps.Any(s => s.State == "Executing") || steps.Count >= 16) throw new InvalidOperationException("Plan belegt oder Schrittlimit erreicht.");
        return Save(item, steps.Append(new(Guid.NewGuid(), plan)).ToArray(), "Planned");
    }
    private static void Check(GuardianArtifact item, GuardianContext context)
    {
        if (item.Context != context || item.Kind != RecoveryKind.Maintenance || !item.Pinned)
            throw new InvalidOperationException("Wartungskontext passt nicht oder Vorgang ist abgeschlossen.");
    }
    internal static async Task<GuardianArtifact> ReanalyseAsync(GuardianArtifact item, GuardianContext context,
        IntelligenceSnapshot actual, IReadOnlyList<GuardianArtifact> artifacts, IGuardianAction action, CancellationToken token)
    {
        Check(item, context);
        IntelligenceModel.Validate(actual);
        if (actual.HostScope != context.Host) throw new InvalidOperationException("Neu gelesenes Inventar passt nicht zum Host.");
        var updated = new List<GuardianMaintenanceStep>();
        foreach (var step in Steps(item))
        {
            token.ThrowIfCancellationRequested();
            var tx = artifacts.SingleOrDefault(a => a.Id == step.TransactionId)?.Transaction;
            if (tx is not null && tx.Context != context) throw new InvalidOperationException("Schritttransaktion in anderem Kontext.");
            GuardianTarget target;
            try { target = await action.ReadAsync(step.Plan.Before, token).ConfigureAwait(false); target.Validate(); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or
                System.ComponentModel.Win32Exception or JsonException)
            {
                System.Diagnostics.Trace.TraceError("Guardian-Wartung: Schritt {0}: {1}", step.Id, ex);
                updated.Add(step with { State = "Unknown", Verification = "Quelle nicht lesbar: " + ex.Message });
                continue;
            }
            var completed = tx?.State == ChangeState.Completed && tx.After is { } after && GuardianCore.Matches(after, target);
            updated.Add(step with
            {
                State = completed ? "Completed" : step.TransactionId is not null ? "Unknown" :
                    GuardianCore.Matches(step.Plan.Before, target) ? "Ready" : "Unknown",
                Verification = completed ? "Abgeschlossener Schritt am aktuellen Ziel erneut bestätigt." :
                    step.TransactionId is not null ? "Unterbrochener oder nicht mehr bestätigter Schritt. Nicht wiederholen; neu planen." :
                    GuardianCore.Matches(step.Plan.Before, target) ? "Preconditions erneut gelesen. Nächste Aktion erneut bestätigen." : "Identität oder Ausgangszustand geändert.",
                Result = tx?.State.ToString() ?? step.Result
            });
        }
        return Save(item with { Fields = new Dictionary<string, string>(item.Fields) { ["analysisSession"] = ProcessSession } },
            updated, updated.All(s => s.State == "Completed") ? "Verified" : "Reanalysed",
            !updated.All(s => s.State == "Completed"));
    }
    internal static (GuardianArtifact Pending, GuardianTransaction Transaction) Reserve(GuardianArtifact item,
        GuardianContext context, GuardianCore core, bool confirmed)
    {
        Check(item, context);
        if (item.Fields.GetValueOrDefault("analysisSession") != ProcessSession)
            throw new InvalidOperationException("Neuer AETHER-Prozess: Host, Benutzer, aktuelle Ziele und abgeschlossene Schritte zuerst erneut analysieren.");
        if (!confirmed) throw new InvalidOperationException("Nächsten Wartungsschritt ausdrücklich bestätigen.");
        var steps = Steps(item).ToArray();
        var index = Array.FindIndex(steps, s => s.State != "Completed");
        if (index < 0 || steps[index].State != "Ready") throw new InvalidOperationException("Zuerst neu analysieren; unbekannte Schritte niemals wiederholen.");
        var tx = core.Plan(item.Name[..Math.Min(item.Name.Length, 140)] + " / Schritt " + (index + 1), steps[index].Plan);
        steps[index] = steps[index] with { State = "Executing", TransactionId = tx.Id, Result = "Ausführung reserviert; bei Unterbrechung neu analysieren." };
        return (Save(item, steps, "Executing"), tx);
    }
    internal static GuardianArtifact Complete(GuardianArtifact item, GuardianTransaction tx)
    {
        Check(item, tx.Context);
        tx.Validate();
        var steps = Steps(item).ToArray();
        var index = Array.FindIndex(steps, s => s.TransactionId == tx.Id && s.State == "Executing");
        if (index < 0) throw new InvalidOperationException("Transaktion gehört nicht zum reservierten Schritt.");
        if (JsonSerializer.Serialize(steps[index].Plan, IntelligenceModel.Json) != JsonSerializer.Serialize(tx.Plan, IntelligenceModel.Json))
            throw new InvalidOperationException("Transaktionsvertrag stimmt nicht mit dem bestätigten Wartungsschritt überein.");
        steps[index] = steps[index] with { State = tx.State == ChangeState.Completed ? "Completed" : "Failed",
            Result = tx.State.ToString(), Verification = tx.VerificationResult };
        return Save(item with { Dependencies = item.Dependencies.Append(tx.Id).Distinct().ToArray() }, steps,
            tx.State == ChangeState.Completed ? "Paused" : "NeedsAnalysis");
    }
}
