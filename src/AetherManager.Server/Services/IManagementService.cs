using AetherOperations;

namespace AetherManager.Server.Services;

internal interface IManagementService
{
    Task<ManagedDeviceStatus[]> GetStatusesAsync(CancellationToken token);
    Task<bool> SetDeviceApprovalAsync(Guid deviceId, bool approved, CancellationToken token);
    Task<ManagedDeviceStatus> HeartbeatAsync(Guid deviceId, HeartbeatRequest request, CancellationToken token);
    Task<ManagedDeviceStatus> SetPolicyAsync(Guid deviceId, ManagementPolicy policy, CancellationToken token);
    Task<ManagedJob> CreateJobAsync(Guid deviceId, CreateManagedJobRequest request, CancellationToken token);
    Task<ManagedJob?> CreateAutoRepairAsync(Guid deviceId, AutoRepairRequest request, CancellationToken token);
    Task<ManagedJob[]> GetJobsAsync(Guid? deviceId, CancellationToken token);
    Task<ManagedJob?> GetJobAsync(Guid id, CancellationToken token);
    Task<ManagedJobClaim?> ClaimAsync(Guid deviceId, CancellationToken token);
    Task<ManagedJob> CompleteAsync(Guid deviceId, Guid jobId, CompleteManagedJobRequest request, CancellationToken token);
    Task<ManagedJob> CancelAsync(Guid jobId, CancellationToken token);
    Task<ManagementAuditEntry[]> GetAuditAsync(CancellationToken token);
}
