namespace AetherOperations;

public enum DiagnosticSeverity { Trace, Debug, Info, Notice, Warning, Error, Critical, Audit }
public enum OperationPhase { Started, Finished }
public enum StopBehavior { Unknown, TerminateProcessTree, WaitForCurrentStep }

public sealed record AuditField(string Value, DataClassification Classification);

public sealed record TechnicalEvent(
    string Module, string Action, OperationPhase Phase, DiagnosticSeverity Severity,
    string Source, AuditField Target, bool? RequiresElevation, bool? UserConfirmed,
    StopBehavior StopBehavior, bool? RollbackAvailable, double? TimeoutSeconds = null,
    OperationOutcome? Outcome = null)
{
    public Guid OperationId { get; init; } = Guid.NewGuid();

    public object JournalDetails() => new
    {
        schema = 1,
        operationId = OperationId,
        module = Module,
        action = Action,
        phase = Phase.ToString(),
        severity = Severity.ToString(),
        source = Source,
        target = DiagnosticDataPolicy.CanShare(Target.Classification, false) ? Target.Value : "[REDACTED]",
        targetClassification = Target.Classification.ToString(),
        requiresElevation = RequiresElevation,
        userConfirmed = UserConfirmed,
        stopBehavior = StopBehavior.ToString(),
        rollbackAvailable = RollbackAvailable,
        timeoutSeconds = TimeoutSeconds,
        outcome = Outcome
    };
}
