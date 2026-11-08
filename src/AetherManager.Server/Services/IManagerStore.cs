using System.Text.Json;
using AetherManager.Server.Models;

namespace AetherManager.Server.Services;

internal sealed record EnrolledDevice(Guid Id, string MachineName, string CockpitVersion, string TokenHash,
    string State, DateTimeOffset EnrolledAt, DateTimeOffset? LastReportAt);

internal sealed record StoredReport(ReportMetadataResponse Metadata, string Path);
internal sealed record ReportPage(ReportMetadataResponse[] Reports, int Offset, int TotalCount, int? NextOffset);

internal interface IManagerStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<string> GetAdminTokenAsync(CancellationToken cancellationToken);
    Task<EnrollmentCodeResponse> CreateEnrollmentCodeAsync(CancellationToken cancellationToken);
    Task<(DeviceResponse Device, string Token)?> EnrollAsync(CreateEnrollmentRequest request, CancellationToken cancellationToken);
    Task<EnrolledDevice?> FindDeviceAsync(string token, CancellationToken cancellationToken);
    Task<DeviceResponse[]> GetDevicesAsync(CancellationToken cancellationToken);
    Task<ReportMetadataResponse[]> GetLatestReportsAsync(CancellationToken cancellationToken);
    Task<bool> ApproveDeviceAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> RevokeDeviceAsync(Guid id, CancellationToken cancellationToken);
    Task<ReportMetadataResponse> AddReportAsync(Guid deviceId, DateTimeOffset capturedAt, JsonElement report,
        CancellationToken cancellationToken);
    Task<ReportPage> GetReportsAsync(Guid? deviceId, int limit, int offset, CancellationToken cancellationToken);
    Task<StoredReportResponse?> GetReportAsync(Guid reportId, CancellationToken cancellationToken);
    Task<int> PurgeExpiredReportsAsync(CancellationToken cancellationToken);
}
