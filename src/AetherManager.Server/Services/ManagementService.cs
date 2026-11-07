using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AetherOperations;

namespace AetherManager.Server.Services;

internal sealed class ManagementService(string root, IManagerStore devices) : IManagementService
{
    private readonly string _path = Path.Combine(root, "management.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public Task<ManagedDeviceStatus[]> GetStatusesAsync(CancellationToken token) =>
        ReadAsync(state => state.Devices.ToArray(), token);

    public async Task<bool> SetDeviceApprovalAsync(Guid deviceId, bool approved, CancellationToken token)
    {
        await ChangeAsync(state =>
        {
            Audit(state, deviceId, null, approved ? "Administrator requested approval" : "Administrator requested revocation");
            if (!approved)
            {
                PutStatus(state, Status(state, deviceId) with { Policy = new ManagementPolicy() });
                foreach (var entry in state.Jobs.Where(item => item.Job.DeviceId == deviceId && item.Job.State == "queued").ToArray())
                    Replace(state, entry, entry with { Job = entry.Job with { State = "cancelled", FinishedAt = DateTimeOffset.UtcNow } });
            }
            return true;
        }, token);
        var result = approved ? await devices.ApproveDeviceAsync(deviceId, token) :
            await devices.RevokeDeviceAsync(deviceId, token);
        await ChangeAsync(state =>
        {
            Audit(state, deviceId, null, $"Device {(approved ? "approval" : "revocation")} result={result}");
            return true;
        }, token);
        return result;
    }

    public Task<ManagedDeviceStatus> HeartbeatAsync(Guid deviceId, HeartbeatRequest request, CancellationToken token) =>
        ChangeAsync(state =>
        {
            if (string.IsNullOrWhiteSpace(request.ClientVersion) || request.ClientVersion.Length > 64 ||
                request.AllowAutoRepair && !request.AllowChanges)
                throw new ArgumentException("Invalid client permissions or version.");
            var current = Status(state, deviceId);
            var updated = current with { LastSeenAt = DateTimeOffset.UtcNow, ClientVersion = request.ClientVersion,
                LocalChangesAllowed = request.AllowChanges, LocalAutoRepairAllowed = request.AllowAutoRepair };
            PutStatus(state, updated);
            return updated;
        }, token);

    public async Task<ManagedDeviceStatus> SetPolicyAsync(Guid deviceId, ManagementPolicy policy, CancellationToken token)
    {
        await RequireApprovedAsync(deviceId, token);
        return await ChangeAsync(state =>
        {
            if (policy.AllowAutoRepair && !policy.AllowChanges)
                throw new ArgumentException("Automatic repair requires permission to change the device.");
            var status = Status(state, deviceId) with { Policy = policy };
            PutStatus(state, status);
            Audit(state, deviceId, null, $"Policy: changes={policy.AllowChanges}, autoRepair={policy.AllowAutoRepair}");
            return status;
        }, token);
    }

    public async Task<ManagedJob> CreateJobAsync(Guid deviceId, CreateManagedJobRequest request, CancellationToken token)
    {
        await RequireApprovedAsync(deviceId, token);
        var action = ManagedActionCatalog.Get(request.ActionId);
        return await ChangeAsync(state =>
        {
            var status = Status(state, deviceId);
            if (action.ChangesSystem && (!request.ConfirmChanges || !status.Policy.AllowChanges ||
                    !status.LocalChangesAllowed))
                throw new InvalidOperationException("Changes require administrator confirmation, server policy and local device consent.");
            if (action.Id == "windows-update-install")
            {
                var selection = request.Update ?? throw new ArgumentException("An exact update selection is required.");
                if (!selection.AcceptEula || selection.UpdateId == Guid.Empty || selection.Revision < 0 ||
                    selection.EulaHash is null || selection.EulaHash.Length != 64)
                    throw new ArgumentException("Invalid update identity or EULA acceptance.");
                var source = state.Jobs.SingleOrDefault(item => item.Job.Id == request.SourceJobId &&
                    item.Job.DeviceId == deviceId)?.Job;
                if (source is null || source.ActionId != "windows-update-search" || source.State != "completed" ||
                    source.Truncated || source.FinishedAt < DateTimeOffset.UtcNow.AddHours(-1) || source.Output is null)
                    throw new InvalidOperationException("Updates require a complete successful search from the same device in the last hour.");
                using var offers = JsonDocument.Parse(source.Output);
                var matches = offers.RootElement.GetProperty("updates").EnumerateArray().Where(offer =>
                    offer.GetProperty("updateId").GetGuid() == selection.UpdateId &&
                    offer.GetProperty("revision").GetInt32() == selection.Revision &&
                    offer.GetProperty("eulaHash").GetString() == selection.EulaHash).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException("The selected identity and EULA do not match the reviewed update search.");
            }
            else if (request.Update is not null || request.SourceJobId is not null)
                throw new ArgumentException("Update parameters are not allowed for this action.");
            return Queue(state, deviceId, action.Id, false, request.Update);
        }, token);
    }

    public async Task<ManagedJob?> CreateAutoRepairAsync(Guid deviceId, AutoRepairRequest request, CancellationToken token)
    {
        await RequireApprovedAsync(deviceId, token);
        return await ChangeAsync<ManagedJob?>(state =>
        {
            var status = Status(state, deviceId);
            if (!status.Policy.AllowAutoRepair || !status.Policy.AllowChanges ||
                !status.LocalAutoRepairAllowed || !status.LocalChangesAllowed ||
                !request.DefaultResolverFailed || !request.ConfiguredResolverSucceeded)
                throw new InvalidOperationException("Automatic DNS repair requires both opt-ins and a confirmed resolver discrepancy.");
            if (state.Jobs.Any(item => item.Job.DeviceId == deviceId && item.Job.Automatic &&
                    item.Job.CreatedAt > DateTimeOffset.UtcNow.AddDays(-1)))
                return null;
            return Queue(state, deviceId, "dns-cache-clear", true);
        }, token);
    }

    public Task<ManagedJob[]> GetJobsAsync(Guid? deviceId, CancellationToken token) =>
        ReadAsync(state => state.Jobs.Where(item => deviceId is null || item.Job.DeviceId == deviceId)
            .Select(item => item.Job).OrderByDescending(job => job.CreatedAt).Take(500).ToArray(), token);

    public Task<ManagedJob?> GetJobAsync(Guid id, CancellationToken token) =>
        ReadAsync(state => state.Jobs.SingleOrDefault(item => item.Job.Id == id)?.Job, token);

    public async Task<ManagedJobClaim?> ClaimAsync(Guid deviceId, CancellationToken token)
    {
        await RequireApprovedAsync(deviceId, token);
        return await ChangeAsync<ManagedJobClaim?>(state =>
        {
            if (state.Jobs.Any(item => item.Job.DeviceId == deviceId && item.Job.State == "running"))
                return null;
            var status = Status(state, deviceId);
            foreach (var entry in state.Jobs.Where(item => item.Job.DeviceId == deviceId && item.Job.State == "queued").ToArray())
            {
                var action = ManagedActionCatalog.Get(entry.Job.ActionId);
                if (entry.Job.CreatedAt < DateTimeOffset.UtcNow.AddHours(-24))
                {
                    Replace(state, entry, entry with { Job = entry.Job with { State = "expired", FinishedAt = DateTimeOffset.UtcNow } });
                    Audit(state, deviceId, entry.Job.Id, "Job expired before execution");
                    continue;
                }
                if (action.ChangesSystem && (!status.Policy.AllowChanges || !status.LocalChangesAllowed) ||
                    entry.Job.Automatic && (!status.Policy.AllowAutoRepair || !status.LocalAutoRepairAllowed))
                    continue;
                var claim = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                var job = entry.Job with { State = "running", StartedAt = DateTimeOffset.UtcNow };
                Replace(state, entry, new JobEntry(job, Hash(claim)));
                Audit(state, deviceId, job.Id, "Job claimed once; no automatic redispatch");
                return new ManagedJobClaim(job, claim);
            }
            return null;
        }, token);
    }

    public Task<ManagedJob> CompleteAsync(Guid deviceId, Guid jobId, CompleteManagedJobRequest request, CancellationToken token) =>
        ChangeAsync(state =>
        {
            if (request.Output is null || request.Output.Length > 65536 ||
                string.IsNullOrWhiteSpace(request.ClaimToken) || request.ClaimToken.Length != 64)
                throw new ArgumentException("Invalid or oversized job result.");
            var entry = state.Jobs.SingleOrDefault(item => item.Job.Id == jobId && item.Job.DeviceId == deviceId)
                ?? throw new KeyNotFoundException("Job not found.");
            if (entry.ClaimHash is null || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(entry.ClaimHash), Encoding.UTF8.GetBytes(Hash(request.ClaimToken))))
                throw new InvalidOperationException("The claim does not match this job.");
            if (entry.Job.State is "completed" or "failed")
                return entry.Job;
            if (entry.Job.State != "running")
                throw new InvalidOperationException("The job is not running.");
            var result = entry.Job with { State = request.ExitCode == 0 ? "completed" : "failed",
                FinishedAt = DateTimeOffset.UtcNow, ExitCode = request.ExitCode, Output = request.Output,
                Truncated = request.Truncated };
            Replace(state, entry, entry with { Job = result });
            Audit(state, deviceId, jobId, $"Job {result.State}; exit code {request.ExitCode}");
            return result;
        }, token);

    public Task<ManagedJob> CancelAsync(Guid jobId, CancellationToken token) =>
        ChangeAsync(state =>
        {
            var entry = state.Jobs.SingleOrDefault(item => item.Job.Id == jobId)
                ?? throw new KeyNotFoundException("Job not found.");
            if (entry.Job.State != "queued")
                throw new InvalidOperationException("Only unclaimed jobs can be cancelled. Running jobs are never redispatched.");
            var job = entry.Job with { State = "cancelled", FinishedAt = DateTimeOffset.UtcNow };
            Replace(state, entry, entry with { Job = job });
            Audit(state, job.DeviceId, jobId, "Queued job cancelled by administrator");
            return job;
        }, token);

    public Task<ManagementAuditEntry[]> GetAuditAsync(CancellationToken token) =>
        ReadAsync(state => state.Audit.TakeLast(500).Reverse().ToArray(), token);

    private async Task RequireApprovedAsync(Guid id, CancellationToken token)
    {
        var device = (await devices.GetDevicesAsync(token)).SingleOrDefault(item => item.Id == id)
            ?? throw new KeyNotFoundException("Device not found.");
        if (device.State != "approved")
            throw new InvalidOperationException("The device has not been approved.");
    }

    private static ManagedDeviceStatus Status(State state, Guid id) =>
        state.Devices.SingleOrDefault(item => item.DeviceId == id) ??
        new ManagedDeviceStatus(id, null, "", false, false, new ManagementPolicy());

    private static void PutStatus(State state, ManagedDeviceStatus status)
    {
        state.Devices.RemoveAll(item => item.DeviceId == status.DeviceId);
        state.Devices.Add(status);
    }

    private static ManagedJob Queue(State state, Guid id, string action, bool automatic, WindowsUpdateSelection? update = null)
    {
        if (state.Jobs.Count >= 10000 || state.Jobs.Count(item => item.Job.DeviceId == id && item.Job.State == "queued") >= 100)
            throw new InvalidOperationException("The durable job store is full; archive completed history before adding jobs.");
        var job = new ManagedJob(Guid.NewGuid(), id, action, "queued", automatic, DateTimeOffset.UtcNow, Update: update);
        state.Jobs.Add(new JobEntry(job, null));
        Audit(state, id, job.Id, $"Queued catalog action {action}; automatic={automatic}");
        return job;
    }

    private static void Replace(State state, JobEntry original, JobEntry replacement) =>
        state.Jobs[state.Jobs.IndexOf(original)] = replacement;

    private static void Audit(State state, Guid id, Guid? job, string message)
    {
        if (state.Audit.Count >= 100000)
            throw new InvalidOperationException("The audit store is full; archive it before further changes.");
        state.Audit.Add(new ManagementAuditEntry(DateTimeOffset.UtcNow, id, job, message));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private async Task<T> ReadAsync<T>(Func<State, T> read, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { return read(await LoadAsync(token)); }
        finally { _gate.Release(); }
    }

    private async Task<T> ChangeAsync<T>(Func<State, T> change, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var state = await LoadAsync(token);
            var result = change(state);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(state, Json), token);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, _path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return result;
        }
        finally { _gate.Release(); }
    }

    private async Task<State> LoadAsync(CancellationToken token)
    {
        if (!File.Exists(_path)) return new State();
        return JsonSerializer.Deserialize<State>(await File.ReadAllBytesAsync(_path, token), Json)
            ?? throw new InvalidDataException("Invalid management state.");
    }

    internal sealed record JobEntry(ManagedJob Job, string? ClaimHash);
    internal sealed class State
    {
        public List<ManagedDeviceStatus> Devices { get; init; } = [];
        public List<JobEntry> Jobs { get; init; } = [];
        public List<ManagementAuditEntry> Audit { get; init; } = [];
    }
}
