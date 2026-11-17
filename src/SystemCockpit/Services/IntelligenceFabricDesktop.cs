using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using AetherOperations;
using AetherOperations.Intelligence;
using AetherOperations.Platform;

namespace SystemCockpit.Services;

public sealed class IntelligenceStateProtection : IAetherStateProtection
{
    private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("AETHER.IntelligenceFabric.v1");
    public byte[] Protect(byte[] clear) => ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] cipher) => ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
}

public sealed class IntelligenceFabricDesktop(Guid local, string markerRoot, Func<LiveSample?> sample,
    ImmutableArray<AdminTarget> demonstration) : IAetherReadToolSource
{
    private readonly LocalPlatformAdapter _local = new(markerRoot, local);
    private readonly SemaphoreSlim _identityGate = new(1, 1);
    private SystemIdentitySnapshot? _systemIdentitySnapshot;

    private sealed record SystemIdentitySnapshot(DateTimeOffset CachedAt,
        string? CpuName, DateTimeOffset CpuTimestamp, string? GpuName, DateTimeOffset GpuTimestamp);

    internal static string? WindowsRegistryProductName(string? product, string? installationType, string? build)
    {
        if (string.IsNullOrWhiteSpace(product)) return null;
        if (product.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) &&
            installationType?.Equals("Client", StringComparison.OrdinalIgnoreCase) == true &&
            (!int.TryParse(build, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number >= 22000))
            return null;
        return product;
    }

    private static void AddSystemIdentity(ImmutableDictionary<string, PlatformFact>.Builder result)
    {
        result.Remove("operatingSystem");
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);
            if (key is null)
            {
                Trace.TraceWarning("AETHER Intelligence: Windows-Produktquelle nicht verfügbar.");
                return;
            }
            var now = DateTimeOffset.UtcNow;
            var product = key.GetValue("ProductName") as string;
            var installationType = key.GetValue("InstallationType") as string;
            var build = key.GetValue("CurrentBuildNumber") as string;
            var normalized = WindowsRegistryProductName(product, installationType, build);
            void Add(string field, string? value, string source, SourceType type = SourceType.Direct)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    result[field] = new(value, new(source, type, now, DataConfidence.ProviderReported,
                        DataAvailability.Available, classification: DataClassification.Sensitive));
            }
            Add("operatingSystemProductName", normalized, "Windows CurrentVersion ProductName und InstallationType / CurrentBuildNumber");
            Add("operatingSystem", normalized, "Windows CurrentVersion ProductName und InstallationType / CurrentBuildNumber");
            Add("operatingSystemEdition", key.GetValue("EditionID") as string, "Windows CurrentVersion EditionID");
            Add("operatingSystemVersion", key.GetValue("DisplayVersion") as string, "Windows CurrentVersion DisplayVersion");
            Add("operatingSystemBuild", build, "Windows CurrentVersion CurrentBuildNumber");
            Add("operatingSystemKernelVersion", Environment.OSVersion.Version.ToString(), ".NET Environment.OSVersion; Kernelversion");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Trace.TraceError("AETHER Intelligence-Windowsidentität: {0}", ex);
        }
    }

    private static void AddSystemVolume(ImmutableDictionary<string, PlatformFact>.Builder result)
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrEmpty(root)) throw new IOException("Systemvolume nicht ermittelt.");
            var drive = new DriveInfo(root);
            var now = DateTimeOffset.UtcNow;
            void Add(string field, string value, SourceType type = SourceType.Direct) =>
                result[field] = new(value, new(".NET DriveInfo des Systemvolumes " + drive.Name, type, now,
                    DataConfidence.ProviderReported, DataAvailability.Available, classification: DataClassification.Sensitive));
            var capacity = drive.TotalSize;
            var free = drive.TotalFreeSpace;
            if (capacity <= 0 || free < 0 || free > capacity)
                throw new InvalidDataException("Systemvolume meldet ungültige Kapazität oder freien Speicher.");
            Add("volumeDriveLetter", drive.Name);
            Add("volumeFileSystem", drive.DriveFormat);
            Add("volumeCapacityBytes", capacity.ToString(CultureInfo.InvariantCulture));
            Add("volumeFreeBytes", free.ToString(CultureInfo.InvariantCulture));
            Add("volumeUsedBytes", (capacity - free).ToString(CultureInfo.InvariantCulture), SourceType.Derived);
            Add("volumeFreePercent", (100d * free / capacity).ToString("F2", CultureInfo.InvariantCulture), SourceType.Derived);
            var index = 0;
            foreach (var volume in DriveInfo.GetDrives().Where(item => item.DriveType == DriveType.Fixed).OrderBy(item => item.Name))
            {
                if (index == 8) { Trace.TraceWarning("AETHER Intelligence: Volumeinventar auf acht Laufwerke begrenzt."); break; }
                try
                {
                    if (!volume.IsReady) { Trace.TraceWarning("AETHER Intelligence: Volume nicht bereit: {0}", volume.Name); continue; }
                    var size = volume.TotalSize;
                    var available = volume.TotalFreeSpace;
                    if (size <= 0 || available < 0 || available > size)
                        throw new InvalidDataException("Ungültige Volumekapazität.");
                    var item = new FabricVolume(volume.Name, volume.DriveFormat, size, size - available, available);
                    var json = JsonSerializer.Serialize(item, new JsonSerializerOptions(FabricJson.Options) { WriteIndented = false });
                    if (json.Length > 256) throw new InvalidDataException("Volumebeleg überschreitet das Feldbudget.");
                    result["volume." + index.ToString(CultureInfo.InvariantCulture)] = new(json,
                        new(".NET DriveInfo des lokalen Volumes " + volume.Name, SourceType.Direct, now,
                            DataConfidence.ProviderReported, DataAvailability.Available, classification: DataClassification.Sensitive));
                    index++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                { Trace.TraceError("AETHER Intelligence-Volumeinventar {0}: {1}", volume.Name, ex); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Trace.TraceError("AETHER Intelligence-Systemvolume: {0}", ex);
        }
    }

    private static async Task AddPhysicalStorageAsync(ImmutableDictionary<string, PlatformFact>.Builder result, CancellationToken token)
    {
        var source = "Windows Storage CIM MSFT_PhysicalDisk; Disknummer nur bei eindeutigem UniqueId-Match";
        try
        {
            var queries = HardwareCimCatalog.Queries.Where(q => q.Key is "OS" or "Disk" or "Physical").ToArray();
            var script = HardwareIntelligenceReader.Script(queries, HardwareClass.Storage, includeSupplementalQueries: false);
            var response = await AdminShell.RunAsync(script, TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (response.ExitCode != 0 || response.OutputTruncated)
                throw new IOException("Windows-Storageabfrage fehlgeschlagen oder überschreitet das Ausgabelimit.");
            var batches = JsonSerializer.Deserialize<HardwareCimResult[]>(response.Output,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Windows-Storageabfrage lieferte kein Ergebnis.");
            var physical = batches.Single(batch => batch.Key == "Physical");
            var disks = batches.Single(batch => batch.Key == "Disk");
            var os = batches.Single(batch => batch.Key == "OS");
            if (os.Availability == DataAvailability.Available && os.Rows is { Length: 1 } &&
                os.Rows[0].TryGetProperty("Caption", out var caption) && caption.ValueKind == JsonValueKind.String &&
                caption.GetString() is { Length: > 0 } productName)
            {
                var osMetadata = new SourceMetadata("CIM Win32_OperatingSystem.Caption", SourceType.Direct, os.Timestamp,
                    DataConfidence.ProviderReported, DataAvailability.Available, classification: DataClassification.Sensitive);
                result["operatingSystemProductName"] = new(productName, osMetadata);
                result["operatingSystem"] = new(productName, osMetadata);
            }
            else
                Trace.TraceWarning("AETHER Intelligence: CIM-Betriebssystemprodukt nicht verfügbar ({0}).", os.Availability);
            if (physical.Availability != DataAvailability.Available)
                throw new IOException("Windows-PhysicalDisk-Quelle nicht verfügbar: " + physical.Error);
            if (physical.Rows is null || disks.Rows is null)
                throw new InvalidDataException("Windows-Storagequelle lieferte keine gültigen Gerätezeilen.");
            static string? Text(JsonElement row, string property)
            {
                if (!row.TryGetProperty(property, out var value)) return null;
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null
                };
            }
            static int? Number(JsonElement row, string property) =>
                int.TryParse(Text(row, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
            static long? Size(JsonElement row) =>
                long.TryParse(Text(row, "Size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && size > 0 ? size : null;
            var metadata = new SourceMetadata(source, SourceType.Direct, physical.Timestamp, DataConfidence.ProviderReported,
                DataAvailability.Available, classification: DataClassification.Sensitive);
            var rows = physical.Rows;
            var facts = new Dictionary<string, PlatformFact>();
            for (var index = 0; index < Math.Min(rows.Length, 16); index++)
            {
                var row = rows[index];
                var uniqueId = Text(row, "UniqueId");
                var matches = disks.Availability == DataAvailability.Available && !string.IsNullOrWhiteSpace(uniqueId)
                    ? disks.Rows.Where(d => string.Equals(uniqueId, Text(d, "UniqueId"), StringComparison.OrdinalIgnoreCase)).ToArray()
                    : [];
                var diskNumber = matches.Length == 1 && rows.Count(p => string.Equals(uniqueId, Text(p, "UniqueId"),
                    StringComparison.OrdinalIgnoreCase)) == 1 ? Number(matches[0], "Number") : null;
                var fact = new FabricPhysicalStorage(diskNumber, Text(row, "Model") ?? Text(row, "FriendlyName"),
                    Text(row, "Manufacturer"), Number(row, "BusType"), Number(row, "MediaType"), Size(row),
                    Text(row, "FirmwareVersion"), Number(row, "HealthStatus"));
                var json = JsonSerializer.Serialize(fact, new JsonSerializerOptions(FabricJson.Options) { WriteIndented = false });
                if (json.Length > 256)
                    throw new InvalidDataException("Physischer Storagebeleg überschreitet das Feldbudget.");
                facts["physicalDrive." + index.ToString(CultureInfo.InvariantCulture)] = new(json, metadata);
            }
            foreach (var fact in facts) result[fact.Key] = fact.Value;
            result["physicalStorageStatus"] = new(rows.Length > 16 ? "Auf 16 physische Geräte begrenzt; weitere Geräte nicht enthalten." :
                rows.Length == 0 ? "Keine physischen Geräte gemeldet; keine Aussage über Abwesenheit." : "Windows-Storageinventar gelesen.", metadata);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or TimeoutException)
        {
            Trace.TraceError("AETHER Intelligence-Storageinventar: {0}", ex);
            result["physicalStorageStatus"] = new(null, new(source, SourceType.Direct, DateTimeOffset.UtcNow,
                DataConfidence.Unknown, ex is UnauthorizedAccessException ? DataAvailability.AccessDenied : DataAvailability.ProviderError,
                classification: DataClassification.Sensitive, error: "Storageinventar nicht verfügbar: " + ex.GetType().Name));
        }
    }

    public async Task<FabricObservation> ReadToolAsync(string tool, AdminTarget target, CancellationToken token)
    {
        if (!AetherToolBroker.ReadTools.ContainsKey(tool)) throw new InvalidDataException("Leseaktion nicht katalogisiert.");
        if (tool is "system.status" or "device.info" or "storage.status" or "events.summary")
            return await ReadAsync(target, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (target.Id != local || target.Simulated || target.Transport != TransportKind.Local)
            return new(target.Id, target.Simulated, DataAvailability.Unavailable, ImmutableDictionary<string, PlatformFact>.Empty);
        string? value = null; var availability = DataAvailability.Unavailable;
        var field = AetherToolBroker.ReadTools[tool].Single();
        var detail = "Kein qualifizierter aktueller Lesebeleg für diese Quelle vorhanden.";
        try
        {
            if (tool == "process.overview")
            {
                var processes = Process.GetProcesses();
                try { value = processes.Length.ToString(CultureInfo.InvariantCulture); }
                finally { foreach (var process in processes) process.Dispose(); }
                detail = ".NET Process.GetProcesses; nur Anzahl, keine Namen oder Befehlszeilen.";
                availability = DataAvailability.Available;
            }
            else if (tool == "network.status")
            {
                value = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Count(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    .ToString(CultureInfo.InvariantCulture);
                detail = ".NET NetworkInterface; aktive Adapter, kein Verbindungs-/Interneterreichbarkeitsnachweis.";
                availability = DataAvailability.Available;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.Net.NetworkInformation.NetworkInformationException)
        { detail = "Lesequelle nicht verfügbar: " + ex.GetType().Name; availability = DataAvailability.ProviderError; }
        var metadata = new SourceMetadata(detail, SourceType.Direct, DateTimeOffset.UtcNow, value is null ? DataConfidence.Unknown : DataConfidence.ProviderReported,
            availability, classification: DataClassification.Sensitive);
        return new(target.Id, false, availability, ImmutableDictionary<string, PlatformFact>.Empty.Add(field, new(value, metadata)));
    }

    private async Task<SystemIdentitySnapshot> ReadSystemIdentitySnapshotAsync(CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        if (_systemIdentitySnapshot is { } cached && now >= cached.CachedAt &&
            now - cached.CachedAt <= TimeSpan.FromMinutes(2))
            return cached;

        await _identityGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (_systemIdentitySnapshot is { } current && now >= current.CachedAt &&
                now - current.CachedAt <= TimeSpan.FromMinutes(2))
                return current;

            var queries = HardwareCimCatalog.Queries
                .Where(query => query.Key is "OverviewCpu" or "OverviewGpu").ToArray();
            var result = await AdminShell.RunAsync(
                HardwareIntelligenceReader.Script(queries, HardwareClass.System, includeSupplementalQueries: false),
                TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (result.ExitCode != 0 || result.OutputTruncated)
                throw new InvalidOperationException("Gezielte CIM-Identitätsabfrage fehlgeschlagen.");

            var batches = JsonSerializer.Deserialize<HardwareCimResult[]>(result.Output,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (batches is not { Length: 2 } ||
                batches.Count(batch => batch.Key == "OverviewCpu") != 1 ||
                batches.Count(batch => batch.Key == "OverviewGpu") != 1)
                throw new JsonException("CIM-Identitätsabfrage lieferte kein eindeutiges CPU-/GPU-Ergebnis.");

            var cpu = batches.Single(batch => batch.Key == "OverviewCpu");
            var gpu = batches.Single(batch => batch.Key == "OverviewGpu");
            var snapshot = new SystemIdentitySnapshot(DateTimeOffset.UtcNow,
                ReadHardwareNames(cpu), cpu.Timestamp, ReadHardwareNames(gpu), gpu.Timestamp);
            _systemIdentitySnapshot = snapshot;
            return snapshot;
        }
        finally { _identityGate.Release(); }
    }

    private static string? ReadHardwareNames(HardwareCimResult batch)
    {
        if (batch.Availability != DataAvailability.Available || batch.Rows is null || batch.Rows.Length == 0)
            return null;
        var names = new List<string>();
        foreach (var row in batch.Rows)
        {
            if (row.ValueKind != JsonValueKind.Object ||
                !row.TryGetProperty("Name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(nameElement.GetString()))
                return null;
            names.Add(nameElement.GetString()!.Trim());
        }

        var value = string.Join(" + ", names.Distinct(StringComparer.OrdinalIgnoreCase));
        return value.Length is > 0 and <= 100 ? value : null;
    }

    public async Task<FabricObservation> ReadAsync(AdminTarget target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (target.Simulated)
        {
            var index = demonstration.IndexOf(target);
            if (index == 3) return new(target.Id, true, DataAvailability.Unavailable, ImmutableDictionary<string, PlatformFact>.Empty);
            var metadata = new SourceMetadata("Ausdrücklich künstliche Demonstrationswerte", SourceType.Unconfirmed,
                DateTimeOffset.UtcNow, DataConfidence.Unconfirmed, DataAvailability.Available, classification: DataClassification.Sensitive);
            var facts = new Dictionary<string, PlatformFact>
            {
                ["freeBytes"] = new((index == 1 ? 5L * 1024 * 1024 * 1024 : 50L * 1024 * 1024 * 1024).ToString(CultureInfo.InvariantCulture), metadata),
                ["operatingSystem"] = new("SIMULIERTE Windows-Testumgebung", metadata),
                ["architecture"] = new("SIMULIERT x64", metadata),
                ["systemErrors24h"] = new("3", metadata),
                ["maximumEventRepetitions24h"] = new("3", metadata)
            };
            if (index != 4) facts["cpuPercent"] = new("90", metadata);
            return new(target.Id, true, DataAvailability.Available, facts.ToImmutableDictionary());
        }
        if (target.Id != local || target.Transport != TransportKind.Local) throw new UnauthorizedAccessException("Kein qualifizierter Remotetransport.");
        var probe = await _local.ProbeAsync(target, token).ConfigureAwait(false);
        var current = sample();
        var result = probe.Facts.ToBuilder();
        AddSystemIdentity(result);
        AddSystemVolume(result);
        await AddPhysicalStorageAsync(result, token).ConfigureAwait(false);
        try
        {
            var identities = await ReadSystemIdentitySnapshotAsync(token).ConfigureAwait(false);
            if (identities.CpuName is { } cpuName)
                result["cpuName"] = new(cpuName, new SourceMetadata("CIM Win32_Processor.Name",
                    SourceType.Direct, identities.CpuTimestamp, DataConfidence.ProviderReported,
                    DataAvailability.Available, classification: DataClassification.Sensitive));
            if (identities.GpuName is { } gpuName)
                result["gpuName"] = new(gpuName, new SourceMetadata("CIM Win32_VideoController.Name",
                    SourceType.Direct, identities.GpuTimestamp, DataConfidence.ProviderReported,
                    DataAvailability.Available, classification: DataClassification.Sensitive));
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or TimeoutException or
            IOException or UnauthorizedAccessException or System.Security.SecurityException or
            System.ComponentModel.Win32Exception)
        {
            Trace.TraceWarning("AETHER Intelligence CPU/GPU-Identität: CIM-Abfrage fehlgeschlagen ({0}, HRESULT 0x{1:X8}).",
                ex.GetType().Name, ex.HResult);
        }
        if (current?.CpuPercent is { } cpu && double.IsFinite(cpu) && cpu is >= 0 and <= 100)
            result["cpuPercent"] = new(cpu.ToString("F2", CultureInfo.InvariantCulture),
                new("Bestehender AETHER-Livesampler; Einzelprobe", SourceType.Direct, current.Timestamp, DataConfidence.ProviderReported,
                    DataAvailability.Available, classification: DataClassification.Sensitive));
        if (current is not null && double.IsFinite(current.MemoryTotalGb) && double.IsFinite(current.MemoryUsedGb) &&
            current.MemoryTotalGb > 0 && current.MemoryTotalGb < long.MaxValue / 1073741824d &&
            current.MemoryUsedGb >= 0 && current.MemoryUsedGb <= current.MemoryTotalGb)
        {
            var memoryMetadata = new SourceMetadata("Bestehender AETHER-Livesampler; physischer Arbeitsspeicher, Einzelprobe",
                SourceType.Direct, current.Timestamp, DataConfidence.ProviderReported, DataAvailability.Available,
                classification: DataClassification.Sensitive);
            result["memoryTotalBytes"] = new(((long)(current.MemoryTotalGb * 1073741824)).ToString(CultureInfo.InvariantCulture), memoryMetadata);
            result["memoryAvailableBytes"] = new(((long)((current.MemoryTotalGb - current.MemoryUsedGb) * 1073741824)).ToString(CultureInfo.InvariantCulture), memoryMetadata);
        }
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime)>=0 and timediff(@SystemTime)<=86400000]]]") { ReverseDirection = true };
            using var reader = new EventLogReader(query); var count = 0;
            var groups = new Dictionary<(string Provider, int Id, byte? Level), (int Count, DateTimeOffset? First, DateTimeOffset? Last)>();
            while (count < 128)
            {
                token.ThrowIfCancellationRequested();
                using var record = reader.ReadEvent(TimeSpan.FromMilliseconds(500));
                if (record is null) break;
                count++;
                var provider = record.ProviderName ?? record.ProviderId?.ToString("D");
                if (!string.IsNullOrEmpty(provider))
                {
                    var key = (provider, record.Id, record.Level);
                    var group = groups.GetValueOrDefault(key);
                    var time = record.TimeCreated is { } created ? new DateTimeOffset(created.ToUniversalTime()) : (DateTimeOffset?)null;
                    groups[key] = (group.Count + 1,
                        time is null ? group.First : group.First is null || time < group.First ? time : group.First,
                        time is null ? group.Last : group.Last is null || time > group.Last ? time : group.Last);
                }
            }
            result["systemErrors24h"] = new(count.ToString(CultureInfo.InvariantCulture),
                new("Windows-Systemprotokoll; letzte 24 Stunden, höchstens 128 kritische/Fehler-Einträge; keine Rohtexte",
                    SourceType.Direct, DateTimeOffset.UtcNow, DataConfidence.ProviderReported, DataAvailability.Available,
                    classification: DataClassification.Sensitive));
            var mostFrequent = groups.OrderByDescending(g => g.Value.Count).ThenBy(g => g.Key.Provider, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Id).FirstOrDefault();
            result["maximumEventRepetitions24h"] = new(mostFrequent.Value.Count.ToString(CultureInfo.InvariantCulture),
                result["systemErrors24h"].Metadata);
            var groupIndex = 0;
            foreach (var group in groups.OrderByDescending(g => g.Value.Count)
                         .ThenBy(g => g.Key.Provider, StringComparer.Ordinal).ThenBy(g => g.Key.Id))
            {
                if (groupIndex == 3) break;
                if (group.Value.First is not { } firstSeen || group.Value.Last is not { } lastSeen ||
                    group.Key.Level is not (1 or 2) || group.Key.Provider.Length > 160) continue;
                var item = new FabricEventGroup("System", group.Key.Provider, group.Key.Id, group.Key.Level,
                    IntelligenceWindowsEvents.Category(group.Key.Provider, group.Key.Id, group.Key.Level.Value),
                    firstSeen, lastSeen, group.Value.Count);
                var json = JsonSerializer.Serialize(item, new JsonSerializerOptions(FabricJson.Options) { WriteIndented = false });
                if (json.Length > 512)
                {
                    Trace.TraceWarning("AETHER Intelligence: Ereignisgruppe überschreitet das Feldbudget.");
                    continue;
                }
                result["eventGroup." + groupIndex.ToString(CultureInfo.InvariantCulture)] =
                    new(json, result["systemErrors24h"].Metadata);
                groupIndex++;
            }
            if (mostFrequent.Value.Count > 0)
            {
                var metadata = result["systemErrors24h"].Metadata;
                result["eventLog"] = new("System", metadata);
                result["eventProvider"] = new(mostFrequent.Key.Provider, metadata);
                result["eventId"] = new(mostFrequent.Key.Id.ToString(CultureInfo.InvariantCulture), metadata);
                result["eventCount"] = new(mostFrequent.Value.Count.ToString(CultureInfo.InvariantCulture), metadata);
                if (mostFrequent.Key.Level is { } level)
                {
                    result["eventLevel"] = new(level.ToString(CultureInfo.InvariantCulture), metadata);
                    result["eventCategory"] = new(IntelligenceWindowsEvents.Category(mostFrequent.Key.Provider, mostFrequent.Key.Id, level), metadata);
                }
                if (mostFrequent.Value.First is { } first) result["eventFirstSeen"] = new(first.ToString("O"), metadata);
                if (mostFrequent.Value.Last is { } last) result["eventLastSeen"] = new(last.ToString("O"), metadata);
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            Trace.TraceError("AETHER Intelligence-Ereignisaggregation: {0}", ex);
            result["systemErrors24h"] = new(null, new("Windows-Systemprotokoll", SourceType.Direct,
                DateTimeOffset.UtcNow, DataConfidence.Unknown, ex is UnauthorizedAccessException ? DataAvailability.AccessDenied : DataAvailability.ProviderError,
                classification: DataClassification.Sensitive, error: "Ereignisaggregation nicht verfügbar: " + ex.GetType().Name));
            result["maximumEventRepetitions24h"] = result["systemErrors24h"];
        }
        return new(target.Id, false, probe.State == ConnectionState.Available ? DataAvailability.Available : DataAvailability.Unavailable, result.ToImmutable());
    }
}

public static class IntelligenceHardwareProbe
{
    public static FabricHardware FromSample(LiveSample? sample)
    {
        var memoryAvailable = sample is not null && double.IsFinite(sample.MemoryTotalGb) &&
            double.IsFinite(sample.MemoryUsedGb) && sample.MemoryTotalGb > 0 &&
            sample.MemoryTotalGb < long.MaxValue / 1073741824d &&
            sample.MemoryUsedGb >= 0 && sample.MemoryUsedGb <= sample.MemoryTotalGb;
        return new(sample?.Timestamp ?? DateTimeOffset.UtcNow,
            memoryAvailable ? (long)(sample!.MemoryTotalGb * 1073741824) : null,
            memoryAvailable ? (long)((sample!.MemoryTotalGb - sample.MemoryUsedGb) * 1073741824) : null,
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "CPU-Identität nicht ermittelt", Environment.ProcessorCount,
            null, null, null, null, null,
            sample?.CpuPercent is { } cpu && double.IsFinite(cpu) && cpu is >= 0 and <= 100 ? cpu : null,
            Environment.WorkingSet, "Inferenz nicht geprüft", "Inferenz nicht geprüft", "Inferenz nicht geprüft",
            "Nicht qualifiziert ermittelt", "Nicht ermittelt");
    }
    public static async Task<FabricHardware> ReadGpuAsync(FabricHardware state, CancellationToken token)
    {
        var executable = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        if (!File.Exists(executable)) return state with { Gpu = "Kein qualifizierter NVIDIA-SMI-Messpfad vorhanden" };
        using var process = new Process { StartInfo = new(executable,
            "--query-gpu=name,memory.total,memory.free,utilization.gpu,temperature.gpu --format=csv,noheader,nounits")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        if (!process.Start()) throw new IOException("Hardwareabfrage nicht gestartet.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var text = await output.ConfigureAwait(false); var diagnostic = await error.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("NVIDIA-SMI-Abfrage fehlgeschlagen: " + diagnostic);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length != 1) throw new InvalidDataException("Mehrere GPUs benötigen eine ausdrückliche Gerätezuordnung.");
            var cells = lines[0].Split(',', StringSplitOptions.TrimEntries);
            if (cells.Length != 5) throw new InvalidDataException("Unbekanntes NVIDIA-SMI-Ergebnisformat.");
            double Number(int index) => double.Parse(cells[index], CultureInfo.InvariantCulture);
            return state with { At = DateTimeOffset.UtcNow, Gpu = cells[0], TotalVramBytes = checked((long)(Number(1) * 1048576)),
                AvailableVramBytes = checked((long)(Number(2) * 1048576)), GpuPercent = Number(3), TemperatureC = Number(4),
                VramMeasuredAt = DateTimeOffset.UtcNow };
        }
        finally { if (!process.HasExited) process.Kill(); }
    }
}
