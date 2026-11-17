using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SystemCockpit.Services;

internal sealed record RescuePreview(string Variant, IReadOnlyList<string> Contents, long EstimatedBytes, string Privacy, string Limitation);
internal sealed record RescueResult(string Path, long Bytes, string Sha256, string Privacy, string Redaction);
internal static class GuardianRescue
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AETHER.Guardian.PrivateRecovery.v1");
    internal static RescuePreview Preview(IReadOnlyList<GuardianArtifact> artifacts, bool privatePackage) =>
        new(privatePackage ? "Private Recovery / Windows DPAPI CurrentUser" : "Support / konservativ redigiert",
            artifacts.Select(a => privatePackage ? $"{a.Kind}: {a.Name}" : a.Kind.ToString()).ToArray(),
            JsonSerializer.SerializeToUtf8Bytes(artifacts, IntelligenceModel.Json).LongLength,
            privatePackage ? "Hochsensibel; enthält lokale Zustände und Konfigurationen" : "Redigiert; nur Typen, Zustände und Anzahl",
            privatePackage ? "Nur im passenden Windows-Benutzerkontext entschlüsselbar. Kein unabhängiges Disaster-Recovery-Backup und keine Lizenz-/Dateisicherung." :
                "Keine Rohsnapshots, Pfade, Zielidentitäten, Namen, Fehlermeldungen, Rechner-/Benutzerkennungen, ETL oder Dumps.");
    internal static byte[] Support(IReadOnlyList<GuardianArtifact> artifacts)
    {
        foreach (var artifact in artifacts) artifact.Validate();
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = "aether.guardian.support/1", Redaction = "AllowlistOnly",
            Contents = artifacts.Select(a => new { Kind = a.Kind.ToString(), State = a.Transaction?.State.ToString() ?? "Documented",
                EntityCount = a.Snapshot?.Entities.Count, SoftwareCount = a.Software?.Count })
        }, IntelligenceModel.Json);
    }
    internal static byte[] Private(IReadOnlyList<GuardianArtifact> artifacts)
    {
        foreach (var artifact in artifacts) artifact.Validate();
        var clear = EvidenceFile.Encode("guardian-private-recovery", artifacts);
        if (clear.Length > GuardianVault.MaximumObjectBytes) throw new IOException("Private Recovery überschreitet das 16-MiB-Paketbudget.");
        try { return ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    internal static IReadOnlyList<GuardianArtifact> OpenPrivate(byte[] bytes, GuardianContext context)
    {
        if (bytes.Length > GuardianVault.MaximumObjectBytes + 65536) throw new IOException("Privates Paket zu groß.");
        var clear = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var artifacts = EvidenceFile.Decode<GuardianArtifact[]>("guardian-private-recovery", Encoding.UTF8.GetString(clear));
            if (artifacts.Length > 256) throw new JsonException("Zu viele Recovery-Objekte.");
            foreach (var artifact in artifacts)
            {
                artifact.Validate();
                if (artifact.Context != context) throw new InvalidOperationException("Recovery-Paket gehört zu einem anderen Rechner-/Benutzerkontext.");
            }
            var ids = artifacts.Select(a => a.Id).ToHashSet();
            if (ids.Count != artifacts.Length || artifacts.Any(a => a.Dependencies.Any(d => !ids.Contains(d))))
                throw new JsonException("Recovery-Paket enthält doppelte Identitäten oder fehlende Abhängigkeiten.");
            return artifacts;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    internal static async Task<RescueResult> WriteAsync(string path, IReadOnlyList<GuardianArtifact> artifacts, bool privatePackage,
        bool confirmed, CancellationToken token)
    {
        if (!confirmed) throw new InvalidOperationException("Inhaltsvorschau und Datenschutzfreigabe fehlen.");
        EvidenceFile.RequireLocalDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path)) throw new IOException("Bestehendes Rescue-Paket wird nicht überschrieben.");
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var packages = directory.EnumerateFiles().Where(f => f.Extension is ".aetherprivate" or ".aethersupport").ToArray();
        if (packages.Length >= 16 || packages.Sum(f => f.Length) + GuardianVault.MaximumObjectBytes + 65536 > 64L * 1024 * 1024)
            throw new IOException("Separates Rescue-Budget dieses Ausgabeordners erschöpft (16 Pakete / 64 MiB). Keine Pakete werden automatisch gelöscht; benötigte Kopien und Referenzen bewusst prüfen.");
        var bytes = privatePackage ? Private(artifacts) : Support(artifacts);
        if (bytes.Length > GuardianVault.MaximumObjectBytes + 65536) throw new IOException("Rescue-Paketbudget überschritten.");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            try
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(true);
            }
            catch
            {
                stream.Close();
                File.Delete(path);
                throw;
            }
        }
        var actual = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        if (!actual.AsSpan().SequenceEqual(bytes)) throw new IOException("Rescue-Paket-Nachprüfung fehlgeschlagen.");
        return new(path, actual.Length, Convert.ToHexString(SHA256.HashData(actual)), privatePackage ? "DPAPI CurrentUser / hochsensibel" :
            "Redigiert", privatePackage ? "Private Inhalte, verschlüsselt, kein portables Dateibackup" : "Strikte Allowlist; keine Rohdaten");
    }
}
