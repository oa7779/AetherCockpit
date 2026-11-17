namespace SystemCockpit.Services;

internal enum GuardianDumpKind { Minimal, WithPrivateMemory }
internal sealed record GuardianDumpPlan(ProcessIdentity Target, GuardianDumpKind Kind, string Destination,
    long EstimatedBytes, string Privacy, string Retention, bool ExplicitConsent)
{
    internal const bool EnabledByDefault = false;
    internal void Validate()
    {
        if (!Target.IsKnown || !Enum.IsDefined(Kind) || !System.IO.Path.IsPathFullyQualified(Destination) ||
            Destination.StartsWith(@"\\", StringComparison.Ordinal) || EstimatedBytes <= 0 ||
            string.IsNullOrWhiteSpace(Privacy) || string.IsNullOrWhiteSpace(Retention) || !ExplicitConsent)
            throw new InvalidOperationException("Ein Dump benötigt ein identitätsgebundenes Ziel, lokalen Pfad, Speicherabschätzung, Datenschutz- und Retentionfreigabe.");
    }
}
