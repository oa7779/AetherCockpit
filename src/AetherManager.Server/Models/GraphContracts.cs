namespace AetherManager.Server.Models;

/// <summary>Reports whether the local Microsoft Graph configuration is usable.</summary>
public sealed record GraphIntegrationStatusResponse(string State, bool Configured, string Message);

/// <summary>Represents one Intune-managed device returned by Microsoft Graph.</summary>
public sealed record GraphDeviceResponse(
    Guid Id,
    string? DeviceName,
    string? OperatingSystem,
    string? OsVersion,
    DateTimeOffset? LastSyncDateTime,
    string? ComplianceState,
    string? ManagementState,
    string? AzureAdDeviceId);

/// <summary>Returns a bounded page of Intune-managed devices.</summary>
public sealed record GraphDeviceListResponse(GraphDeviceResponse[] Devices, bool HasMore);

/// <summary>Represents one Windows Autopatch deployment.</summary>
public sealed record GraphDeploymentResponse(
    Guid Id,
    string? RequestedState,
    string? EffectiveState,
    string? CatalogEntryId,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime);

/// <summary>Returns a bounded page of Windows Autopatch deployments.</summary>
public sealed record GraphDeploymentListResponse(GraphDeploymentResponse[] Deployments, bool HasMore);

/// <summary>Requests a confirmed pause or resume for one existing Autopatch deployment.</summary>
public sealed record GraphDeploymentStateRequest
{
    /// <summary>True to pause the deployment; false to resume it.</summary>
    public required bool Paused { get; init; }
}

/// <summary>Reports the result of a Microsoft Graph administrative request.</summary>
public sealed record GraphActionResponse(string State, string Message);
