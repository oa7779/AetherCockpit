using System.Text.Json;

namespace AetherOperations.Platform;

public sealed class PlatformStore
{
    public string Root { get; }
    public const int MaximumJobs = 128;
    private const int MaximumBytes = 16 * 1024 * 1024;
    public PlatformStore(string root)
    {
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Absoluter eigener Datenordner erforderlich.", nameof(root));
        Root = Path.GetFullPath(root);
    }

    internal static void CheckPath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Umgeleitete Plattformpfade werden nicht verwendet.");
    }

    public IDisposable Acquire()
    {
        CheckPath(Root);
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, "workspace.lock");
        CheckPath(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private string JobPath(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Auftrags-ID fehlt.", nameof(id));
        return Path.Combine(Root, "jobs", id.ToString("N") + ".json");
    }

    public PlatformInventory? LoadInventory()
    {
        var path = Path.Combine(Root, "inventory.json");
        if (!File.Exists(path)) return null;
        var inventory = Read<PlatformInventory>(path);
        TargetInventory.Validate(inventory);
        return inventory;
    }

    public void SaveInventory(PlatformInventory inventory)
    {
        TargetInventory.Validate(inventory);
        AtomicWrite(Path.Combine(Root, "inventory.json"), inventory);
    }

    public PlatformJob Load(Guid id)
    {
        var job = Read<PlatformJob>(JobPath(id));
        Validate(job);
        if (job.Preview.Id != id) throw new InvalidDataException("Auftragsidentität widerspricht dem Dateinamen.");
        return job;
    }

    public PlatformJob[] Jobs()
    {
        var folder = Path.Combine(Root, "jobs");
        CheckPath(folder);
        if (!Directory.Exists(folder)) return [];
        var paths = Directory.GetFiles(folder, "*.json");
        if (paths.Length > MaximumJobs) throw new InvalidDataException("Auftragsbestand überschreitet das Speicherbudget.");
        return paths.Select(path => Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id)
            ? Load(id) : throw new InvalidDataException("Unbekannte Datei im Auftragsbestand.")).OrderByDescending(j => j.Preview.Created).ToArray();
    }

    public void Save(PlatformJob job)
    {
        Validate(job);
        var path = JobPath(job.Preview.Id);
        if (!File.Exists(path) && Jobs().Length >= MaximumJobs)
            throw new InvalidOperationException("Auftragsbestand ist voll. Geprüfte abgeschlossene Historie bewusst archivieren.");
        AtomicWrite(path, job);
    }

    public static void Validate(PlatformJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Schema != 1 || job.Preview is null || job.Preview.Id == Guid.Empty || !Enum.IsDefined(job.State) ||
            job.Preview.Policy is null || string.IsNullOrWhiteSpace(job.Preview.Requester) || job.Preview.Requester.Length > 256 ||
            job.Preview.Created == default ||
            job.Preview.Targets.IsDefaultOrEmpty || job.Preview.Targets.Length > TargetInventory.MaximumTargets ||
            job.Preview.Targets.Any(t => t is null || t.Target is null || !Enum.IsDefined(t.Availability) ||
                t.ExpectedMarkerHash is null || t.ExpectedMarkerHash.Length is not (0 or 64) ||
                t.EvidenceHash is null || t.EvidenceHash.Length != 64) ||
            job.Preview.Targets.Select(t => t.Target.Id).Distinct().Count() != job.Preview.Targets.Length ||
            job.PreviewHash != job.Preview.Hash || job.Preview.ActionHash != PlatformActions.Get(job.Preview.ActionId).Hash ||
            job.Preview.ActionVersion != PlatformActions.Get(job.Preview.ActionId).Version ||
            job.Preview.RunbookId != PlatformActions.Runbook(job.Preview.ActionId).Id ||
            job.Preview.RunbookHash != PlatformActions.Runbook(job.Preview.ActionId).Hash ||
            job.Results.IsDefault || job.Results.Length > job.Preview.Targets.Length || job.Results.Any(r => r is null) ||
            job.Results.Select(r => r.TargetId).Distinct().Count() != job.Results.Length ||
            job.Results.Any(r => !Enum.IsDefined(r.State) || !job.Preview.Targets.Any(t => t.Target.Id == r.TargetId) ||
                r.Detail is null || r.Detail.Length > 1024 || r.Facts is null || r.Started == default ||
                r.State == TargetResultState.Running && r.Ended is not null ||
                r.State != TargetResultState.Running && (r.Ended is null || r.Ended < r.Started) ||
                r.State == TargetResultState.Succeeded && r.ExitCode != 0 ||
                r.MarkerHash is not null && (r.MarkerHash.Length != 64 || !r.MarkerHash.All(char.IsAsciiHexDigit))) ||
            job.Approvals.IsDefault || job.Approvals.Length > 32 || job.Approvals.Any(a =>
                a is null || a.PreviewHash != job.PreviewHash || !Enum.IsDefined(a.Role) || string.IsNullOrWhiteSpace(a.Actor) ||
                a.Actor.Length > 256 || a.Expires <= a.At || a.Expires - a.At > TimeSpan.FromHours(1)) ||
            job.Audit.IsDefaultOrEmpty || job.Audit.Length > 4096)
            throw new InvalidDataException("Beschädigter oder nicht qualifizierter Plattformauftrag.");
        foreach (var target in job.Preview.Targets) TargetInventory.Validate(target.Target);
        foreach (var result in job.Results)
            TargetInventory.Validate(job.Preview.Targets.Single(t => t.Target.Id == result.TargetId).Target with { Facts = result.Facts });
        var previous = "";
        for (var i = 0; i < job.Audit.Length; i++)
        {
            var entry = job.Audit[i];
            if (entry is null || entry.Sequence != i || entry.PreviousHash != previous || entry.Hash != AuditHash(entry) ||
                entry.At == default || string.IsNullOrWhiteSpace(entry.Event) || entry.Event.Length > 256 ||
                string.IsNullOrWhiteSpace(entry.Actor) || entry.Actor.Length > 256)
                throw new InvalidDataException("Auditkette ist beschädigt.");
            previous = entry.Hash;
        }
    }

    public static string AuditHash(PlatformAudit entry) => PlatformJson.Hash(entry with { Hash = "" });

    private static T Read<T>(string path)
    {
        CheckPath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Plattformdatei überschreitet das Speicherbudget.");
        return JsonSerializer.Deserialize<T>(stream, PlatformJson.Options) ?? throw new InvalidDataException("Leere Plattformdatei.");
    }

    private static void AtomicWrite<T>(string path, T value)
    {
        CheckPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, PlatformJson.Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Plattformdatei überschreitet das Speicherbudget.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            CheckPath(path);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
