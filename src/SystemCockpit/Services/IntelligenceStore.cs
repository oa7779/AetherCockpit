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
internal sealed class IntelligenceStore(string root)
{
    internal static string DefaultDirectory => Path.Combine(Path.GetDirectoryName(CockpitSettings.PathName)!, "Intelligence");
    internal const int MaximumFileBytes = 8 * 1024 * 1024;
    private sealed record Envelope(int Version, string Type, string Payload, string Sha256);
    internal List<string> Issues { get; } = [];
    internal IntelligenceRetention Retention { get; set; } = new();
    internal string Root => root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    internal async Task SaveAsync<T>(string type, Guid id, T value, CancellationToken token)
    {
        ValidateType(type); Retention.Validate();
        if (id == Guid.Empty) throw new ArgumentException("Speicheridentität fehlt.");
        var payload = JsonSerializer.Serialize(value, IntelligenceModel.Json);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, type, payload, checksum), IntelligenceModel.Json);
        if (bytes.Length > MaximumFileBytes || bytes.Length > Retention.Bytes) throw new IOException("Einzelner Historienbestand überschreitet das Speicherbudget.");
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
            Prune();
            var result = new List<T>();
            foreach (var file in Directory.EnumerateFiles(root, type + "-*.json").Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (new FileInfo(file).Length > MaximumFileBytes) throw new JsonException("Bestand zu groß.");
                    var envelope = JsonSerializer.Deserialize<Envelope>(await File.ReadAllTextAsync(file, token).ConfigureAwait(false), IntelligenceModel.Json)
                        ?? throw new JsonException("Hülle fehlt.");
                    if (envelope.Version != 1 || envelope.Type != type || envelope.Payload is null || envelope.Sha256 is null ||
                        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Payload))) != envelope.Sha256)
                        throw new JsonException("Unbekannte Version oder beschädigte Prüfsumme.");
                    var value = JsonSerializer.Deserialize<T>(envelope.Payload, IntelligenceModel.Json) ?? throw new JsonException("Inhalt fehlt.");
                    if (value is IntelligenceSnapshot snapshot) IntelligenceModel.Validate(snapshot);
                    if (value is IntelligenceSession session) session.Validate();
                    if (value is DiagnosisRecording recording) recording.Validate();
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
                var name = Path.GetFileName(file);
                var prefix = name.Split('-')[0];
                var separator = name.IndexOf('-');
                if (prefix is "snapshot" or "baseline" or "session" or "preferences" or "recording" &&
                    separator > 0 && name.Length >= separator + 38 &&
                    Guid.TryParseExact(name.Substring(separator + 1, 32), "N", out _) &&
                    name[(separator + 33)..] is ".json" or ".json.tmp" or ".json.corrupt") File.Delete(file);
            }
        }
        finally { _gate.Release(); }
    }
    private void Prune()
    {
        var files = new DirectoryInfo(root).EnumerateFiles().Where(f => f.Name.EndsWith(".json", StringComparison.Ordinal) ||
            f.Name.EndsWith(".corrupt", StringComparison.Ordinal) || f.Name.EndsWith(".tmp", StringComparison.Ordinal))
            .OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        long retained = 0; var count = 0;
        foreach (var file in files)
        {
            if (DateTime.UtcNow - file.LastWriteTimeUtc > TimeSpan.FromDays(Retention.Days) ||
                count >= Retention.Files || retained + file.Length > Retention.Bytes) file.Delete();
            else { retained += file.Length; count++; }
        }
    }
    private static void ValidateType(string type)
    {
        if (type is not ("snapshot" or "baseline" or "session" or "preferences" or "recording"))
            throw new ArgumentException("Unbekannte Historienart.");
    }
}
