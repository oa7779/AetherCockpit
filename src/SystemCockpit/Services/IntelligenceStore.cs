using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SystemCockpit.Services;

internal sealed record IntelligenceRetention(int Version = 1, int Days = 14, int Files = 128, long Bytes = 64 * 1024 * 1024)
{
    internal void Validate()
    {
        if (Version != 1 || Days is < 1 or > 90 || Files is < 8 or > 512 || Bytes is < 1048576 or > 268435456)
            throw new ArgumentException("Historiengrenzen: 1–90 Tage, 8–512 Dateien, 1–256 MiB.");
    }
}
internal sealed record IntelligencePreferences(int Version, bool Persist, IntelligenceRetention Retention, bool ObserveInventories = false)
{
    internal void Validate()
    {
        if (Version != 1 || Retention is null) throw new JsonException("Unbekannte Intelligence-Einstellungen.");
        Retention.Validate();
    }
}
internal sealed class IntelligenceStore(string root)
{
    internal static string DefaultDirectory => Path.Combine(Path.GetDirectoryName(CockpitSettings.PathName)!, "Intelligence");
    internal const int MaximumFileBytes = 8 * 1024 * 1024;
    private sealed record Envelope(int Version, string Type, string Payload, string Sha256);
    internal List<string> Issues { get; } = [];
    internal IntelligenceRetention Retention { get; set; } = new();
    internal string Root => root;
    internal static IntelligenceSnapshot ImportSnapshot(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumFileBytes) throw new JsonException("Snapshot zu groß.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Snapshotobjekt fehlt.");
        if (!document.RootElement.TryGetProperty("Payload", out _)) return IntelligenceModel.Import(json);
        var envelope = document.RootElement.Deserialize<Envelope>() ?? throw new JsonException("Snapshot-Hülle fehlt.");
        if (envelope.Version != 1 || envelope.Type is not ("snapshot" or "baseline") || envelope.Payload is null || envelope.Sha256 is null ||
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Payload))) != envelope.Sha256)
            throw new JsonException("Snapshot-Version oder Prüfsumme ungültig.");
        return IntelligenceModel.Import(envelope.Payload);
    }
    private readonly SemaphoreSlim _gate = new(1, 1);
    internal async Task SaveAsync<T>(string type, Guid id, T value, CancellationToken token)
    {
        ValidateType(type); Retention.Validate();
        if (id == Guid.Empty) throw new ArgumentException("Speicheridentität fehlt.");
        switch (value)
        {
            case IntelligenceSnapshot snapshot when type is "snapshot" or "baseline": IntelligenceModel.Validate(snapshot); break;
            case IntelligenceSession session when type == "session": session.Validate(); break;
            case DiagnosisRecording recording when type == "recording": recording.Validate(); break;
            case IntelligencePreferences preferences when type == "preferences": preferences.Validate(); break;
            default: throw new ArgumentException("Inhalt und Speicherart stimmen nicht überein.");
        }
        var payload = JsonSerializer.Serialize(value, IntelligenceModel.Json);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, type, payload, checksum), IntelligenceModel.Json);
        if (bytes.Length > MaximumFileBytes || bytes.Length > Retention.Bytes || type == "preferences" && bytes.Length > 65536)
            throw new IOException("Einzelner Historienbestand überschreitet das Speicherbudget.");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        var file = Path.Combine(root, type + "-" + id.ToString("N") + ".json");
        var temp = file + ".tmp";
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(temp, bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temp, file, true);
            Prune();
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            finally { _gate.Release(); }
        }
    }
    internal async Task<IReadOnlyList<T>> LoadAsync<T>(string type, CancellationToken token)
    {
        ValidateType(type); Retention.Validate();
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(root)) return [];
            if (type != "preferences") Prune();
            var result = new List<T>();
            var files = Directory.EnumerateFiles(root, type + "-*.json").Where(f => Owns(Path.GetFileName(f)));
            if (type == "preferences") files = files.OrderByDescending(File.GetLastWriteTimeUtc).Take(1);
            foreach (var file in files.Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (new FileInfo(file).Length > (type == "preferences" ? 65536 : MaximumFileBytes)) throw new JsonException("Bestand zu groß.");
                    var envelope = JsonSerializer.Deserialize<Envelope>(await File.ReadAllTextAsync(file, token).ConfigureAwait(false), IntelligenceModel.Json)
                        ?? throw new JsonException("Hülle fehlt.");
                    if (envelope.Version != 1 || envelope.Type != type || envelope.Payload is null || envelope.Sha256 is null ||
                        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Payload))) != envelope.Sha256)
                        throw new JsonException("Unbekannte Version oder beschädigte Prüfsumme.");
                    var value = JsonSerializer.Deserialize<T>(envelope.Payload, IntelligenceModel.Json) ?? throw new JsonException("Inhalt fehlt.");
                    if (value is IntelligenceSnapshot snapshot) IntelligenceModel.Validate(snapshot);
                    if (value is IntelligenceSession session) session.Validate();
                    if (value is DiagnosisRecording recording) recording.Validate();
                    if (value is IntelligencePreferences preferences) preferences.Validate();
                    result.Add(value);
                }
                catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException)
                {
                    Trace.TraceError("Intelligence-Historie: {0}", ex);
                    Issues.Add(Path.GetFileName(file) + ": " + ex.Message);
                    if (Issues.Count > 64) Issues.RemoveAt(0);
                    File.Move(file, file + ".corrupt", true);
                }
            }
            return result;
        }
        finally { _gate.Release(); }
    }
    internal async Task DeleteAsync(string type, Guid id, CancellationToken token)
    {
        ValidateType(type);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { File.Delete(Path.Combine(root, type + "-" + id.ToString("N") + ".json")); }
        finally { _gate.Release(); }
    }
    internal async Task ResetAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (var file in Directory.EnumerateFiles(root))
            {
                token.ThrowIfCancellationRequested();
                if (Owns(Path.GetFileName(file))) File.Delete(file);
            }
        }
        finally { _gate.Release(); }
    }
    private void Prune()
    {
        var files = new DirectoryInfo(root).EnumerateFiles().Where(f => Owns(f.Name))
            .OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        long retained = 0; var count = 0;
        var preferences = files.FirstOrDefault(f => f.Name.StartsWith("preferences-", StringComparison.Ordinal) &&
            f.Name.EndsWith(".json", StringComparison.Ordinal) && f.Length <= 65536);
        if (preferences is not null) { retained = preferences.Length; count = 1; }
        foreach (var file in files)
        {
            if (file == preferences) continue;
            if (file.Name.StartsWith("preferences-", StringComparison.Ordinal) && file.Name.EndsWith(".json", StringComparison.Ordinal) ||
                DateTime.UtcNow - file.LastWriteTimeUtc > TimeSpan.FromDays(Retention.Days) ||
                count >= Retention.Files || retained + file.Length > Retention.Bytes) file.Delete();
            else { retained += file.Length; count++; }
        }
    }
    private static bool Owns(string name)
    {
        var separator = name.IndexOf('-');
        return separator > 0 && name[..separator] is "snapshot" or "baseline" or "session" or "preferences" or "recording" &&
            name.Length >= separator + 38 && Guid.TryParseExact(name.Substring(separator + 1, 32), "N", out _) &&
            name[(separator + 33)..] is ".json" or ".json.tmp" or ".json.corrupt";
    }
    private static void ValidateType(string type)
    {
        if (type is not ("snapshot" or "baseline" or "session" or "preferences" or "recording"))
            throw new ArgumentException("Unbekannte Historienart.");
    }
}
