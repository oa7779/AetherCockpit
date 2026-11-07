using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace AetherManager.Server.Models;

/// <summary>Requests enrollment for a named AETHER installation using a one-time administrator-issued code.</summary>
public sealed record CreateEnrollmentRequest
{
    /// <summary>One-time enrollment code obtained from the server administrator.</summary>
    [Required, StringLength(64, MinimumLength = 12)]
    public required string Code { get; init; }

    /// <summary>Windows computer name reported by the cockpit.</summary>
    [Required, StringLength(255)]
    public required string MachineName { get; init; }

    /// <summary>AETHER version installed on the device.</summary>
    [Required, StringLength(64)]
    public required string CockpitVersion { get; init; }
}

/// <summary>Returns the enrollment result and a per-device bearer token; retain the token securely.</summary>
public sealed record EnrollmentResponse(Guid DeviceId, string State, string DeviceToken);

/// <summary>Creates a report submission for an enrolled and approved device.</summary>
public sealed record CreateReportRequest
{
    /// <summary>UTC or offset-preserving time when the local diagnostic snapshot was collected.</summary>
    [Required]
    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>Complete diagnostic snapshot produced by the AETHER read-only Windows collectors.</summary>
    [Required]
    public required JsonElement Report { get; init; }
}

/// <summary>Identifies a stored report and its ingestion time.</summary>
public sealed record ReportMetadataResponse(
    Guid ReportId,
    Guid DeviceId,
    DateTimeOffset CapturedAt,
    DateTimeOffset ReceivedAt,
    long SizeBytes);

/// <summary>Represents one enrolled cockpit and its latest central report status.</summary>
public sealed record DeviceResponse(
    Guid Id,
    string MachineName,
    string CockpitVersion,
    string State,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? LastReportAt);

/// <summary>Lists all enrolled cockpits.</summary>
public sealed record DeviceListResponse(DeviceResponse[] Devices);

/// <summary>Returns one page of reports retained for the requested fleet.</summary>
public sealed record ReportListResponse(
    ReportMetadataResponse[] Reports,
    int Offset,
    int TotalCount,
    int? NextOffset);

/// <summary>Creates a single-use code for enrolling one additional cockpit.</summary>
public sealed record EnrollmentCodeResponse(string Code, DateTimeOffset ExpiresAt);

/// <summary>Returns the complete stored diagnostic report for an authorized manager.</summary>
public sealed record StoredReportResponse(ReportMetadataResponse Metadata, JsonElement Report);
