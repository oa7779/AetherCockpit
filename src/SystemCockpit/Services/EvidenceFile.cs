using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SystemCockpit.Services;

internal static class EvidenceFile
{
    internal static void RequireLocalDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
            throw new IOException("Guardian-Ausgabe benötigt ein absolutes lokales Verzeichnis.");
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Guardian-Ausgabe darf keine Dateiumleitung enthalten.");
    }
    internal sealed record Envelope(int Version, string Type, string Payload, string Sha256);
    internal static byte[] Encode<T>(string type, T value)
    {
        var payload = JsonSerializer.Serialize(value, IntelligenceModel.Json);
        return JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, type, payload, Hash(payload)), IntelligenceModel.Json);
    }
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static T Decode<T>(string type, string json)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(json, IntelligenceModel.Json) ?? throw new JsonException("Dateihülle fehlt.");
        if (envelope.Version != 1 || envelope.Type != type || envelope.Payload is null || envelope.Sha256 != Hash(envelope.Payload))
            throw new JsonException("Dateiversion, Typ oder SHA256 ungültig.");
        return JsonSerializer.Deserialize<T>(envelope.Payload, IntelligenceModel.Json) ?? throw new JsonException("Dateiinhalt fehlt.");
    }
    internal static async Task WriteAsync(string path, byte[] bytes, CancellationToken token)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
