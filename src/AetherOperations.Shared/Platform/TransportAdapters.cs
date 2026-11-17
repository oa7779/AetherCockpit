using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;

namespace AetherOperations.Platform;

public sealed record AdapterProbe(ConnectionState State, ImmutableDictionary<string, PlatformFact> Facts, string MarkerHash, string Detail);
public interface IPlatformAdapter
{
    TransportKind Transport { get; }
    Task<AdapterProbe> ProbeAsync(AdminTarget target, CancellationToken token);
    Task<string?> ExecuteAsync(AdminTarget target, string actionId, Guid jobId, string expectedMarkerHash, CancellationToken token);
    Task RollbackAsync(AdminTarget target, Guid jobId, string expectedHash, CancellationToken token);
}

public sealed class LocalPlatformAdapter(string markerRoot, Guid localId) : IPlatformAdapter
{
    public TransportKind Transport => TransportKind.Local;
    public static string MarkerContent(Guid job, Guid target) => $"AETHER test.marker/v1\n{job:D}\n{target:D}\n";
    private string PathFor(AdminTarget target)
    {
        if (target.Id != localId || target.Kind != TargetKind.LocalComputer || target.Simulated ||
            !string.Equals(target.Host, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Lokaler Adapter akzeptiert nur den expliziten eigenen Rechner.");
        if (!Path.IsPathFullyQualified(markerRoot)) throw new ArgumentException("Eigener absoluter Markenordner erforderlich.");
        var path = Path.Combine(markerRoot, target.Id.ToString("N") + ".marker");
        PlatformStore.CheckPath(path);
        return path;
    }
    internal static string ReadHash(string path)
    {
        PlatformStore.CheckPath(path);
        if (!File.Exists(path)) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 256) throw new InvalidDataException("Fremde oder zu große Testmarke.");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }
    public Task<AdapterProbe> ProbeAsync(AdminTarget target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = PathFor(target);
        var now = DateTimeOffset.UtcNow;
        PlatformFact Fact(string value, string source) => new(value, new(source, SourceType.Direct, now,
            DataConfidence.ProviderReported, DataAvailability.Available, classification: DataClassification.Sensitive));
        var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory.Length == 0 ? markerRoot : Environment.SystemDirectory)!);
        var facts = new Dictionary<string, PlatformFact>
        {
            ["operatingSystem"] = Fact(RuntimeInformation.OSDescription, ".NET RuntimeInformation"),
            ["build"] = Fact(Environment.OSVersion.Version.ToString(), ".NET Environment.OSVersion"),
            ["architecture"] = Fact(RuntimeInformation.OSArchitecture.ToString(), ".NET RuntimeInformation"),
            ["freeBytes"] = Fact(drive.AvailableFreeSpace.ToString(CultureInfo.InvariantCulture), ".NET DriveInfo des Systemlaufwerks"),
            ["lastContact"] = Fact(now.ToString("O"), "Lokaler Auftragsadapter"),
            ["permission"] = Fact("Aktuelles Benutzerkonto; keine zusätzliche Erhöhung", "Lokaler Auftragsadapter")
        }.ToImmutableDictionary();
        return Task.FromResult(new AdapterProbe(ConnectionState.Available, facts, ReadHash(path), "Lokale begrenzte Inventur; keine Netzwerkabfrage."));
    }
    public Task<string?> ExecuteAsync(AdminTarget target, string actionId, Guid jobId, string expectedMarkerHash, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = PathFor(target);
        if (actionId == "inventory.basic") return Task.FromResult<string?>(null);
        if (actionId != "test.marker" || expectedMarkerHash.Length != 0 || ReadHash(path).Length != 0)
            throw new InvalidOperationException("Neue Vorschau erforderlich; eigene Marke muss fehlen.");
        Directory.CreateDirectory(markerRoot);
        PlatformStore.CheckPath(path);
        var bytes = System.Text.Encoding.UTF8.GetBytes(MarkerContent(jobId, target.Id));
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(bytes); stream.Flush(true); }
        return Task.FromResult<string?>(ReadHash(path));
    }
    public Task RollbackAsync(AdminTarget target, Guid jobId, string expectedHash, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = PathFor(target);
        var ownHash = PlatformJson.TextHash(MarkerContent(jobId, target.Id));
        if (ownHash != expectedHash || ReadHash(path) != ownHash)
            throw new InvalidOperationException("Rückwegkonflikt: eigene unveränderte Marke nicht nachgewiesen.");
        var quarantine = path + "." + jobId.ToString("N") + ".rollback";
        File.Move(path, quarantine, false);
        if (ReadHash(quarantine) != ownHash)
        {
            File.Move(quarantine, path, false);
            throw new InvalidOperationException("Rückwegkonflikt nach Identitätsprüfung; Datei erhalten.");
        }
        File.Delete(quarantine);
        return Task.CompletedTask;
    }
}

public sealed class SimulatedPlatformAdapter(string? stateRoot = null) : IPlatformAdapter
{
    private readonly ConcurrentDictionary<Guid, string> _markers = new();
    public TransportKind Transport => TransportKind.Simulated;
    public ImmutableHashSet<Guid> Offline { get; init; } = [];
    public ImmutableHashSet<Guid> Fail { get; init; } = [];
    public TimeSpan Delay { get; init; }
    private int _active, _peak, _executed;
    public int PeakWorkers => _peak;
    public int Executed => _executed;
    private string StatePath(AdminTarget target)
    {
        if (stateRoot is null || !Path.IsPathFullyQualified(stateRoot))
            throw new InvalidOperationException("Absoluter eigener Simulationsordner erforderlich.");
        var path = Path.Combine(stateRoot, target.Id.ToString("N") + ".marker");
        PlatformStore.CheckPath(path);
        return path;
    }
    public Task<AdapterProbe> ProbeAsync(AdminTarget target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!target.Simulated || target.Transport != TransportKind.Simulated)
            throw new UnauthorizedAccessException("Simulation darf keine realen Ziele übernehmen.");
        var facts = ImmutableDictionary<string, PlatformFact>.Empty.Add("freeBytes", new("10737418240",
            new("AETHER-Simulation, keine Messung", SourceType.Unconfirmed, DateTimeOffset.UtcNow,
                DataConfidence.Unconfirmed, DataAvailability.Available, classification: DataClassification.Sensitive)));
        var offline = Offline.Contains(target.Id);
        return Task.FromResult(new AdapterProbe(offline ? ConnectionState.Unreachable : ConnectionState.Available,
            offline ? ImmutableDictionary<string, PlatformFact>.Empty : facts,
            stateRoot is null ? _markers.GetValueOrDefault(target.Id) ?? "" : LocalPlatformAdapter.ReadHash(StatePath(target)),
            "SIMULIERT; keine Verbindung zu einem Zielrechner."));
    }
    public async Task<string?> ExecuteAsync(AdminTarget target, string actionId, Guid jobId, string expectedMarkerHash, CancellationToken token)
    {
        if (!target.Simulated) throw new UnauthorizedAccessException("Reales Ziel im Simulationsadapter.");
        var active = Interlocked.Increment(ref _active);
        int peak;
        do { peak = _peak; if (peak >= active) break; } while (Interlocked.CompareExchange(ref _peak, active, peak) != peak);
        try
        {
            await Task.Delay(Delay, token).ConfigureAwait(false);
            if (Offline.Contains(target.Id) || Fail.Contains(target.Id)) throw new IOException("Simulierter Zielausfall.");
            Interlocked.Increment(ref _executed);
            if (actionId == "inventory.basic") return null;
            if (actionId != "test.marker" || expectedMarkerHash != "")
                throw new InvalidOperationException("Unbekannte Aktion oder Simulationskonflikt.");
            var hash = PlatformJson.TextHash(LocalPlatformAdapter.MarkerContent(jobId, target.Id));
            if (stateRoot is null)
            {
                if (!_markers.TryAdd(target.Id, hash)) throw new InvalidOperationException("Simulierte Marke besteht bereits.");
            }
            else
            {
                var path = StatePath(target);
                Directory.CreateDirectory(stateRoot);
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(System.Text.Encoding.UTF8.GetBytes(LocalPlatformAdapter.MarkerContent(jobId, target.Id)));
                stream.Flush(true);
            }
            return hash;
        }
        finally { Interlocked.Decrement(ref _active); }
    }
    public Task RollbackAsync(AdminTarget target, Guid jobId, string expectedHash, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!target.Simulated || expectedHash != PlatformJson.TextHash(LocalPlatformAdapter.MarkerContent(jobId, target.Id)))
            throw new InvalidOperationException("Simulierter Rückwegkonflikt; keine fremde Marke entfernt.");
        if (stateRoot is null)
        {
            if (!_markers.TryRemove(new KeyValuePair<Guid, string>(target.Id, expectedHash)))
                throw new InvalidOperationException("Simulierter Rückwegkonflikt; keine fremde Marke entfernt.");
        }
        else
        {
            var path = StatePath(target);
            if (LocalPlatformAdapter.ReadHash(path) != expectedHash)
                throw new InvalidOperationException("Simulierter Rückwegkonflikt; Datei bleibt erhalten.");
            File.Delete(path);
        }
        return Task.CompletedTask;
    }
}
