using System.Text.Json;
using AetherOperations;

namespace SystemCockpit.Services;

internal enum ChangeState
{
    Planned, Preflight, Ready, Protected, Executing, Verifying, Completed, Partial, Failed, Cancelled,
    RollbackAvailable, RollingBack, RolledBack, RollbackPartial, RollbackFailed, NotReversible
}
internal enum RollbackCapability { Available, Partial, ExternalSourceRequired, ManualOnly, Unknown, NotAvailable, AlreadyRolledBack }
internal enum RecoveryKind
{
    RecoveryPoint, Transaction, SoftwareManifest, InstallationObservation, DriverState, CrashCase,
    Maintenance, DiagnosticEvidence, ConfigurationBackup, RollbackManifest, UpdateHistory, RescuePackage
}
internal enum ReadinessKind { Recoverable, PartiallyRecoverable, DocumentedOnly, ExternalSourceRequired, NotProtected, NotAssessable }
internal sealed record GuardianContext(string Host, string User)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Host.Length > 128 || string.IsNullOrWhiteSpace(User) || User.Length > 256)
            throw new JsonException("Guardian benötigt einen gültigen Rechner- und Benutzerkontext.");
    }
}
internal sealed record GuardianTarget(string Kind, string Id, string Fingerprint, string State,
    IReadOnlyDictionary<string, string> Fields)
{
    internal void Validate()
    {
        if (Kind is not ("service" or "task" or "startup" or "process" or "configuration" or "invocation" or "fixture") || string.IsNullOrWhiteSpace(Id) ||
            Id.Length > 4096 || Fingerprint is null || Fingerprint.Length != 64 || !Fingerprint.All(char.IsAsciiHexDigit) ||
            string.IsNullOrWhiteSpace(State) || State.Length > 256 ||
            Fields is null || Fields.Count > 64 || Fields.Any(f => f.Key.Length > 128 || f.Value is null || f.Value.Length > 32768))
            throw new JsonException("Ungültiges Guardian-Ziel.");
    }
}
internal sealed record GuardianPlan(string Action, GuardianTarget Before, string ExpectedState, string Rights,
    string Dependencies, string Effects, string Risks, string Restart, string Protection, string Verification,
    RollbackCapability Rollback, string RollbackExplanation)
{
    internal void Validate()
    {
        if (Before is null) throw new JsonException("Change-Plan-Ziel fehlt.");
        Before.Validate();
        if (new[] { Action, ExpectedState, Rights, Dependencies, Effects, Risks, Restart, Protection, Verification, RollbackExplanation }
            .Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 65536) || !Enum.IsDefined(Rollback))
            throw new JsonException("Unvollständiger Change Plan.");
    }
}
internal sealed record ChangeJournalEntry(int Sequence, ChangeState State, DateTimeOffset Time, double ElapsedMilliseconds,
    string Detail, string PreviousHash, string Hash);
internal sealed record GuardianTransaction(int Schema, Guid Id, string Title, GuardianContext Context, GuardianPlan Plan,
    DateTimeOffset Created, DateTimeOffset? Started, DateTimeOffset? Ended, ChangeState State,
    GuardianTarget? After, string VerificationResult, string NativeResult, IReadOnlyList<string> Errors,
    IReadOnlyList<ChangeJournalEntry> Journal, Guid? RollbackOf, DataClassification Privacy, SourceMetadata Metadata)
{
    internal bool InFlight => State is ChangeState.Planned or ChangeState.Preflight or ChangeState.Ready or ChangeState.Protected or
        ChangeState.Executing or ChangeState.Verifying or ChangeState.RollingBack;
    internal void Validate()
    {
        if (Context is null || Plan is null) throw new JsonException("Transaktionskontext oder Plan fehlt.");
        Context.Validate(); Plan.Validate(); After?.Validate();
        if (Schema != 1 || Id == Guid.Empty || string.IsNullOrWhiteSpace(Title) || Title.Length > 160 || Created == default ||
            !Enum.IsDefined(State) || Privacy != DataClassification.Sensitive || !IntelligenceModel.Valid(Metadata) ||
            Errors is null || Errors.Count > 64 || Errors.Any(e => e is null || e.Length > 65536) ||
            Journal is null || Journal.Count is < 1 or > 256 || VerificationResult is null || VerificationResult.Length > 65536 ||
            NativeResult is null || NativeResult.Length > 65536 || Metadata.Classification != DataClassification.Sensitive)
            throw new JsonException("Ungültige Change Transaction.");
        var previous = "";
        var elapsed = 0d;
        foreach (var (entry, index) in Journal.Select((e, i) => (e, i)))
        {
            if (entry is null || entry.Sequence != index || !Enum.IsDefined(entry.State) || entry.Time == default ||
                !double.IsFinite(entry.ElapsedMilliseconds) || entry.ElapsedMilliseconds < elapsed || entry.Detail is null ||
                entry.Detail.Length > 65536 || entry.PreviousHash != previous || entry.Hash != GuardianCore.JournalHash(entry))
                throw new JsonException("Change Journal ist beschädigt oder nicht append-orientiert.");
            elapsed = entry.ElapsedMilliseconds; previous = entry.Hash;
        }
        if (Journal[^1].State != State) throw new JsonException("Journal und Transaktionszustand widersprechen sich.");
    }
}
internal sealed record SoftwareRecoveryItem(string Identity, string Name, string Version, string Architecture,
    string Context, string Source, string Method, RollbackCapability Restore, IReadOnlyList<string> Relations)
{
    public string PackageId { get; init; } = "Nicht technisch zugeordnet";
    public string AvailableVersion { get; init; } = "Nicht aus einer exakten Quelle gelesen";
    public string RecoverySource { get; init; } = "Externe Installationsquelle erforderlich";
    public string Restriction { get; init; } = "Keine Installationsmedien oder Lizenz gesichert";
    public string Restart { get; init; } = "Nicht paketgenau bekannt; kein Neustart ausgelöst";
    public string Verification { get; init; } = "Exakte Registrierungs-/Paketidentität und Version nach manueller Wiederherstellung neu lesen";
}
internal sealed record GuardianDriverCheck(string Identity, string DeviceInstance,
    IReadOnlyDictionary<string, string> Before, IReadOnlyDictionary<string, string>? After,
    string DeviceStatus, string PnPProblem, string ServiceStatus, string Function, string Rollback);
internal sealed record GuardianArtifact(int Schema, Guid Id, RecoveryKind Kind, string Name, DateTimeOffset Created,
    GuardianContext Context, SourceMetadata Metadata, IReadOnlyList<Guid> Dependencies, bool Pinned,
    string Summary, IReadOnlyDictionary<string, string> Fields, IntelligenceSnapshot? Snapshot = null,
    IReadOnlyList<SoftwareRecoveryItem>? Software = null, GuardianTransaction? Transaction = null,
    IReadOnlyList<GuardianDriverCheck>? DriverChecks = null, IReadOnlyList<DiagnosisRecording>? Recordings = null)
{
    public GuardianTraceResult? TraceResult { get; init; }
    public GuardianTraceAnalysis? TraceAnalysis { get; init; }
    public IReadOnlyList<GuardianTraceProcessHistory>? TraceHistory { get; init; }
    internal void Validate()
    {
        if (Context is null) throw new JsonException("Recovery-Kontext fehlt.");
        Context.Validate();
        if (Schema != 1 || Id == Guid.Empty || !Enum.IsDefined(Kind) || string.IsNullOrWhiteSpace(Name) || Name.Length > 160 ||
            Created == default || !IntelligenceModel.Valid(Metadata) || Metadata.Classification != DataClassification.Sensitive ||
            Dependencies is null || Dependencies.Count > 256 || Dependencies.Any(d => d == Guid.Empty || d == Id) ||
            Dependencies.Distinct().Count() != Dependencies.Count || Summary is null || Summary.Length > 65536 ||
            Fields is null || Fields.Count > 128 || Fields.Any(f => f.Key.Length > 256 || f.Value is null || f.Value.Length > 65536) ||
            Software?.Count > IntelligenceModel.MaximumEntities ||
            Software is not null && Software.Any(s => s is null || string.IsNullOrWhiteSpace(s.Identity) || s.Identity.Length > 32768 || s.Name is null ||
                s.Name.Length > 32768 || s.Version is null || s.Source is null || s.Method is null || s.Architecture is null || s.Context is null ||
                new[] { s.PackageId, s.AvailableVersion, s.RecoverySource, s.Restriction, s.Restart, s.Verification }.Any(v => v is null || v.Length > 32768) ||
                !Enum.IsDefined(s.Restore) || s.Relations is null || s.Relations.Count > 4096 || s.Relations.Any(r => r is null || r.Length > 32768)) ||
            Kind is RecoveryKind.RecoveryPoint or RecoveryKind.DriverState or RecoveryKind.InstallationObservation or RecoveryKind.CrashCase or RecoveryKind.Maintenance &&
                Snapshot is null || Kind == RecoveryKind.SoftwareManifest && Software is null)
            throw new JsonException("Ungültiger Recovery-Vault-Bestand.");
        if (Snapshot is not null) IntelligenceModel.Validate(Snapshot);
        if (TraceResult is not null || TraceAnalysis is not null)
        {
            if (Kind != RecoveryKind.DiagnosticEvidence || TraceResult is null || Context.Host != TraceResult.Host)
                throw new JsonException("Traceinhalt und Vault-Kontext passen nicht.");
            try
            {
                GuardianTraceDecoder.Validate(TraceResult);
                if (TraceAnalysis is not null) GuardianTraceDecoder.ValidateAnalysis(TraceResult, TraceAnalysis);
            }
            catch (System.IO.InvalidDataException ex) { throw new JsonException("Ungültiger eigener ETL-Vertrag.", ex); }
        }
        if (TraceHistory is not null && (TraceResult is null || TraceAnalysis is null || TraceHistory.Count > 8 ||
            TraceHistory.Any(h => h is null || !GuardianTraceDecoder.TryIdentity(h.Identity, out _) ||
                !TraceAnalysis.Facts.Any(f => f.Identity == h.Identity) || h.Samples is null || h.Samples.Count > 32 ||
                h.Samples.Any(s => s is null || s.Timestamp < TraceResult.CaptureBegan || s.Timestamp > TraceResult.CaptureEnded ||
                    new[] { s.Cpu, s.MemoryMb, s.ReadMiBs, s.WriteMiBs }.Any(v => v is { } n && (!double.IsFinite(n) || n < 0))))))
            throw new JsonException("Ungültiger zeit- und identitätsgebundener Trace-/Prozesshistorienvergleich.");
        if (Kind == RecoveryKind.ConfigurationBackup && Fields.GetValueOrDefault("scope") == "settings,rules,preferences,views")
            GuardianConfiguration.Decode(Fields.GetValueOrDefault("payload") ?? throw new JsonException("Konfigurationsinhalt fehlt."));
        if (DriverChecks is not null && (Kind != RecoveryKind.DriverState || Snapshot is null ||
            DriverChecks.Count > IntelligenceModel.MaximumEntities ||
            DriverChecks.Any(c => c is null || string.IsNullOrWhiteSpace(c.Identity) || c.Identity.Length > 32768 ||
                new[] { c.DeviceInstance, c.DeviceStatus, c.PnPProblem, c.ServiceStatus, c.Function, c.Rollback }.Any(v => v is null || v.Length > 65536) ||
                c.Before is null || c.Before.Count > 256 || c.Before.Concat(c.After ?? new Dictionary<string, string>())
                    .Any(f => f.Key.Length > 256 || f.Value is null || f.Value.Length > 65536))))
            throw new JsonException("Ungültiger Driver-Guard-Vergleich.");
        Transaction?.Validate();
        if (Recordings is not null)
        {
            if (Kind != RecoveryKind.CrashCase || Recordings.Count > 8 || Recordings.Any(r => r is null))
                throw new JsonException("Ungültige Crash-Recorderbelege.");
            foreach (var recording in Recordings) recording.Validate();
        }
        if (Kind == RecoveryKind.Transaction != (Transaction is not null) || Transaction is { } t && (t.Id != Id || t.Context != Context))
            throw new JsonException("Transaktionsinhalt und Vault-Identität passen nicht.");
    }
}
internal sealed record RecoveryReadiness(string Area, ReadinessKind State, string Evidence, string Limitation);
