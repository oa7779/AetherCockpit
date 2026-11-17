using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherOperations.Platform;

public enum TargetKind { LocalComputer, WindowsClient, WindowsServer, WorkgroupComputer, DomainComputer, DirectoryUnit, IntuneGroup, ArcResource }
public enum TransportKind { Local, WindowsPowerShell, PowerShell7, WinRM, Cim, Wmi, Ssh, Agent, Relay, Graph, Intune, Arc, Smb, AdminCenter, Simulated }
public enum ConnectionState { Available, Unavailable, NotConfigured, NotAuthorized, Unreachable, AuthenticationFailed, CertificateError, Unsupported, Incompatible }
public enum PlatformRole { Reader, Inspector, Support, ClientAdministrator, ServerAdministrator, NetworkAdministrator, SecurityAdministrator, PackageAdministrator, RunbookAuthor, Approver, Auditor, PlatformAdministrator }
public enum PlatformJobState { Draft, Checking, PreviewReady, AwaitingApproval, Approved, Scheduled, Running, Paused, WaitingForRestart, WaitingForTarget, Partial, Succeeded, Failed, Cancelled, RolledBack, RollbackFailed, NeedsVerification }
public enum TargetResultState { Running, Succeeded, Failed, Cancelled, TimedOut, NeedsVerification, RolledBack, RollbackFailed, RollingBack }
public enum RunbookStepKind { Inventory, Condition, Query, Check, Preview, Approval, Action, Wait, Restart, Reconnect, Verify, Compare, Rollback, Report, Notification, ManualDecision }
public enum PackageTrust { Blocked, BuiltIn }

public sealed record PlatformFact(string? Value, SourceMetadata Metadata);
public sealed record AdminTarget(Guid Id, string Name, TargetKind Kind, string Host, TransportKind Transport,
    bool Simulated, ImmutableDictionary<string, PlatformFact> Facts);
public sealed record TargetGroup(Guid Id, string Name, ImmutableArray<Guid> Members, bool Dynamic = false, TargetKind? Kind = null,
    TransportKind? Transport = null);
public sealed record TransportCapability(TransportKind Transport, ConnectionState State, string Detail);
public sealed record ActionRequirements(ImmutableArray<TargetKind> Targets, string OperatingSystems, string MinimumVersion,
    ImmutableArray<string> Modules, ImmutableArray<string> Tools, string Rights, ImmutableArray<TransportKind> Transports);
public sealed record ActionPlanContract(string Parameters, string Preconditions, string Dependencies, string Detect,
    string Preview, string Execute, string Verify, string Rollback);
public sealed record ActionImpact(string Duration, string Restart, string Interruption, string Network,
    string User, string Service, string Security);
public sealed record ActionSafety(int Risk, string Idempotence, bool Parallel, bool Cancellable,
    string RollbackClass, DataClassification Classification);
public sealed record ActionResultContract(string Evidence, string Result, string Errors, string Logging, string Support, string Documentation);
public sealed record AdminAction(string ActionId, int Version, string Name, string Category, string Purpose,
    string TechnicalDescription, ActionRequirements Requirements, ActionPlanContract Plan, ActionImpact Impact,
    ActionSafety Safety, ActionResultContract Results)
{
    [JsonIgnore] public string Hash => PlatformJson.Hash(this);
}
public sealed record ScriptPackage(int ApiVersion, string Id, string Version, string Publisher,
    string Origin, string Sha256, string SignatureStatus, PackageTrust Trust, ImmutableArray<string> Permissions);
public sealed record RunbookStep(string Id, RunbookStepKind Kind, ImmutableArray<string> After, string Description);
public sealed record AdminRunbook(string Id, int Version, string Name, string ActionId, ImmutableArray<RunbookStep> Steps)
{
    [JsonIgnore] public string Hash => PlatformJson.Hash(this);
}
public sealed record PlatformPrincipal(string Id, PlatformRole Role, ImmutableArray<Guid> Scope);
public sealed record ExecutionPolicy(int Workers = 4, int WaveSize = 25, int CanaryCount = 1,
    int MaximumFailures = 0, int TargetTimeoutSeconds = 30, DateTimeOffset? NotBefore = null,
    DateTimeOffset? WindowEnd = null, int MaximumRuntimeSeconds = 900);
public sealed record PlanTarget(AdminTarget Target, ConnectionState Availability, string ExpectedMarkerHash, string EvidenceHash);
public sealed record PlatformPreview(Guid Id, string ActionId, int ActionVersion, string ActionHash,
    string Requester, DateTimeOffset Created, ImmutableArray<PlanTarget> Targets, ExecutionPolicy Policy,
    string RunbookId = "", string RunbookHash = "")
{
    [JsonIgnore] public string Hash => PlatformJson.Hash(this);
}
public sealed record PlatformApproval(string Actor, PlatformRole Role, string PreviewHash, DateTimeOffset At, DateTimeOffset Expires);
public sealed record TargetExecution(Guid TargetId, TargetResultState State, DateTimeOffset Started,
    DateTimeOffset? Ended, int? ExitCode, ImmutableDictionary<string, PlatformFact> Facts,
    string Detail, string? MarkerHash = null);
public sealed record PlatformAudit(int Sequence, DateTimeOffset At, string Actor, string Event,
    Guid? Target, string PreviousHash, string Hash);
public sealed record PlatformJob(int Schema, PlatformPreview Preview, string PreviewHash, PlatformJobState State,
    ImmutableArray<PlatformApproval> Approvals, ImmutableArray<TargetExecution> Results,
    ImmutableArray<PlatformAudit> Audit);
public sealed record TopologyEdge(Guid From, Guid To, string Relation, SourceMetadata Metadata);
public sealed record DesiredState(string Field, decimal Minimum, string Unit);
public sealed record ComplianceFinding(Guid TargetId, string State, string Explanation, SourceMetadata Metadata);

public static class PlatformJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
    public static string Hash<T>(T value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, Options));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(document.RootElement, writer);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
    private static void Write(JsonElement element, Utf8JsonWriter writer)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); Write(property.Value, writer); }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray()) Write(item, writer);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }
    public static string TextHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
