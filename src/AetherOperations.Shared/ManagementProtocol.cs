namespace AetherOperations;

/// <summary>Device-level server permission, additionally restricted by local client consent.</summary>
public sealed record ManagementPolicy(bool AllowChanges = false, bool AllowAutoRepair = false);

/// <summary>Client liveness and its locally granted execution permissions.</summary>
public sealed record HeartbeatRequest(string ClientVersion, bool AllowChanges, bool AllowAutoRepair);

/// <summary>Central liveness and policy state for one device.</summary>
public sealed record ManagedDeviceStatus(Guid DeviceId, DateTimeOffset? LastSeenAt,
    string ClientVersion, bool LocalChangesAllowed, bool LocalAutoRepairAllowed, ManagementPolicy Policy);

/// <summary>A request for a fixed catalog action, never a free-form shell command.</summary>
public sealed record CreateManagedJobRequest(string ActionId, bool ConfirmChanges = false,
    Guid? SourceJobId = null, WindowsUpdateSelection? Update = null);

/// <summary>A durable administrative or automatic action and its bounded execution result.</summary>
public sealed record ManagedJob(Guid Id, Guid DeviceId, string ActionId, string State,
    bool Automatic, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt = null,
    DateTimeOffset? FinishedAt = null, int? ExitCode = null, string? Output = null,
    bool Truncated = false, WindowsUpdateSelection? Update = null);

/// <summary>A once-only claim with a separate capability required to complete the action.</summary>
public sealed record ManagedJobClaim(ManagedJob Job, string ClaimToken);

/// <summary>Bounded result of a claimed device action.</summary>
public sealed record CompleteManagedJobRequest(string ClaimToken, int ExitCode, string Output, bool Truncated);

/// <summary>Evidence submitted by the DNS assistant before a narrowly permitted automatic cache clear.</summary>
public sealed record AutoRepairRequest(bool DefaultResolverFailed, bool ConfiguredResolverSucceeded);

/// <summary>Audit trail for device approval, policy changes and job transitions.</summary>
public sealed record ManagementAuditEntry(DateTimeOffset At, Guid DeviceId, Guid? JobId, string Event);

/// <summary>Catalog metadata shown without disclosing or accepting executable scripts.</summary>
public sealed record ManagedActionDescription(string Id, string Name, string Category,
    bool ChangesSystem, bool CanAutoRepair, string[] RequiredCommands);
