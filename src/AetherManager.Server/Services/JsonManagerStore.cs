using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AetherManager.Server.Models;

namespace AetherManager.Server.Services;

internal sealed class JsonManagerStore : IManagerStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);
    private static readonly TimeSpan EnrollmentCodeLifetime = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _root;
    private readonly string _devicesPath;
    private readonly string _codesPath;
    private readonly string _reportsPath;
    private readonly string _reportDirectory;
    private readonly string _adminTokenPath;
    private readonly string _bootstrapCodePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<EnrolledDevice> _devices = [];
    private readonly List<EnrollmentCode> _codes = [];
    private readonly List<StoredReport> _reports = [];
    private string? _adminToken;

    public JsonManagerStore(string root)
    {
        _root = Path.GetFullPath(root);
        _devicesPath = Path.Combine(_root, "devices.json");
        _codesPath = Path.Combine(_root, "enrollment-codes.json");
        _reportsPath = Path.Combine(_root, "reports.json");
        _reportDirectory = Path.Combine(_root, "reports");
        _adminTokenPath = Path.Combine(_root, "admin-token.txt");
        _bootstrapCodePath = Path.Combine(_root, "enrollment-code.txt");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_reportDirectory);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _adminToken = File.Exists(_adminTokenPath)
                ? (await File.ReadAllTextAsync(_adminTokenPath, cancellationToken)).Trim()
                : CreateToken();
            if (!File.Exists(_adminTokenPath))
                await WriteTextAtomicallyAsync(_adminTokenPath, _adminToken, cancellationToken);
            if (_adminToken.Length < 40)
                throw new InvalidDataException("Der Administrator-Token in admin-token.txt ist ungueltig.");

            _devices.AddRange(await ReadJsonAsync<EnrolledDevice[]>(_devicesPath, cancellationToken) ?? []);
            _codes.AddRange(await ReadJsonAsync<EnrollmentCode[]>(_codesPath, cancellationToken) ?? []);
            var storedReports = await ReadJsonAsync<ReportEntry[]>(_reportsPath, cancellationToken) ?? [];
            _reports.AddRange(storedReports
                .Where(entry => entry.Metadata.ReportId != Guid.Empty)
                .Select(entry => new StoredReport(entry.Metadata,
                    Path.Combine(_reportDirectory, $"{entry.Metadata.ReportId:N}.json"))));
            await RemoveExpiredReportsAsync(DateTimeOffset.UtcNow, cancellationToken);
            _codes.RemoveAll(code => code.ExpiresAt <= DateTimeOffset.UtcNow);
            if (_codes.Count == 0)
            {
                var code = NewEnrollmentCode();
                _codes.Add(new EnrollmentCode(Hash(code), DateTimeOffset.UtcNow.Add(EnrollmentCodeLifetime)));
                await WriteJsonAtomicallyAsync(_codesPath, _codes.ToArray(), cancellationToken);
                await WriteTextAtomicallyAsync(_bootstrapCodePath, code, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<string> GetAdminTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_adminToken ?? throw new InvalidOperationException("The manager store is not initialized."));
    }

    public async Task<EnrollmentCodeResponse> CreateEnrollmentCodeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var code = NewEnrollmentCode();
            var expiresAt = DateTimeOffset.UtcNow.Add(EnrollmentCodeLifetime);
            _codes.RemoveAll(item => item.ExpiresAt <= DateTimeOffset.UtcNow);
            _codes.Add(new EnrollmentCode(Hash(code), expiresAt));
            await WriteJsonAtomicallyAsync(_codesPath, _codes.ToArray(), cancellationToken);
            return new EnrollmentCodeResponse(code, expiresAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(DeviceResponse Device, string Token)?> EnrollAsync(
        CreateEnrollmentRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            _codes.RemoveAll(item => item.ExpiresAt <= now);
            var codeHash = Hash(request.Code);
            var codeIndex = _codes.FindIndex(item => FixedEquals(item.Hash, codeHash));
            if (codeIndex < 0)
            {
                await WriteJsonAtomicallyAsync(_codesPath, _codes.ToArray(), cancellationToken);
                return null;
            }

            _codes.RemoveAt(codeIndex);
            await WriteJsonAtomicallyAsync(_codesPath, _codes.ToArray(), cancellationToken);
            var token = CreateToken();
            var device = new EnrolledDevice(Guid.NewGuid(), request.MachineName.Trim(),
                request.CockpitVersion.Trim(), Hash(token), "pending", now, null);
            _devices.Add(device);
            await SaveDevicesAsync(cancellationToken);
            return (ToResponse(device), token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EnrolledDevice?> FindDeviceAsync(string token, CancellationToken cancellationToken)
    {
        var tokenHash = Hash(token);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _devices.FirstOrDefault(device => FixedEquals(device.TokenHash, tokenHash));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DeviceResponse[]> GetDevicesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _devices.OrderBy(device => device.MachineName, StringComparer.OrdinalIgnoreCase)
                .Select(ToResponse).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ApproveDeviceAsync(Guid id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var index = _devices.FindIndex(device => device.Id == id);
            if (index < 0)
                return false;
            if (_devices[index].State == "approved")
                return true;
            _devices[index] = _devices[index] with { State = "approved" };
            await SaveDevicesAsync(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReportMetadataResponse> AddReportAsync(Guid deviceId, DateTimeOffset capturedAt,
        JsonElement report, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var reportId = Guid.NewGuid();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
            var metadata = new ReportMetadataResponse(reportId, deviceId, capturedAt, now, bytes.LongLength);
            var reportPath = Path.Combine(_reportDirectory, $"{reportId:N}.json");
            await WriteBytesAtomicallyAsync(reportPath, bytes, cancellationToken);
            _reports.Add(new StoredReport(metadata, reportPath));
            var deviceIndex = _devices.FindIndex(device => device.Id == deviceId);
            if (deviceIndex < 0)
                throw new InvalidOperationException("Approved device disappeared while saving its report.");
            _devices[deviceIndex] = _devices[deviceIndex] with { LastReportAt = now };
            await SaveDevicesAsync(cancellationToken);
            await RemoveExpiredReportsAsync(now, cancellationToken);
            await SaveReportsAsync(cancellationToken);
            return metadata;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReportPage> GetReportsAsync(Guid? deviceId, int limit, int offset,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var matching = _reports.Where(report => deviceId is null || report.Metadata.DeviceId == deviceId)
                .OrderByDescending(report => report.Metadata.ReceivedAt)
                .ToArray();
            var page = matching.Skip(offset).Take(limit).Select(report => report.Metadata).ToArray();
            int? nextOffset = page.Length > 0 && offset < matching.Length - page.Length
                ? offset + page.Length
                : null;
            return new ReportPage(page, offset, matching.Length, nextOffset);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredReportResponse?> GetReportAsync(Guid reportId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var report = _reports.FirstOrDefault(item => item.Metadata.ReportId == reportId);
            if (report is null || !File.Exists(report.Path))
                return null;
            await using var stream = File.OpenRead(report.Path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return new StoredReportResponse(report.Metadata, document.RootElement.Clone());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> PurgeExpiredReportsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await RemoveExpiredReportsAsync(DateTimeOffset.UtcNow, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveDevicesAsync(CancellationToken cancellationToken) =>
        await WriteJsonAtomicallyAsync(_devicesPath, _devices.ToArray(), cancellationToken);

    private async Task<int> RemoveExpiredReportsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expired = _reports.Where(report => report.Metadata.ReceivedAt < now.Subtract(Retention)).ToArray();
        if (expired.Length == 0)
        {
            if (!File.Exists(_reportsPath))
                await SaveReportsAsync(cancellationToken);
            return 0;
        }

        _reports.RemoveAll(report => report.Metadata.ReceivedAt < now.Subtract(Retention));
        await SaveReportsAsync(cancellationToken);
        foreach (var report in expired)
            if (File.Exists(report.Path))
                File.Delete(report.Path);
        return expired.Length;
    }

    private Task SaveReportsAsync(CancellationToken cancellationToken) =>
        WriteJsonAtomicallyAsync(_reportsPath, _reports.Select(report =>
            new ReportEntry(report.Metadata)).ToArray(), cancellationToken);

    private static DeviceResponse ToResponse(EnrolledDevice device) =>
        new(device.Id, device.MachineName, device.CockpitVersion, device.State,
            device.EnrolledAt, device.LastReportAt);

    private static string NewEnrollmentCode() => CreateRandomText(24);
    private static string CreateToken() => CreateRandomText(32);
    private static string CreateRandomText(int byteCount) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool FixedEquals(string left, string right)
    {
        var leftBytes = Convert.FromHexString(left);
        var rightBytes = Convert.FromHexString(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return default;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        await WriteBytesAtomicallyAsync(path, bytes, cancellationToken);
    }

    private static async Task WriteTextAtomicallyAsync(string path, string value, CancellationToken cancellationToken) =>
        await WriteBytesAtomicallyAsync(path, Encoding.UTF8.GetBytes(value), cancellationToken);

    private static async Task WriteBytesAtomicallyAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private sealed record EnrollmentCode(string Hash, DateTimeOffset ExpiresAt);
    private sealed record ReportEntry(ReportMetadataResponse Metadata);
}
