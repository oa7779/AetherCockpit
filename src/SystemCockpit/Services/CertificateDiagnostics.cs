using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SystemCockpit.Services;

public static class CertificateDiagnostics
{
    public static IReadOnlyList<Check> Read(CancellationToken token)
    {
        var results = new List<Check>();
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
            foreach (var storeName in new[] { StoreName.Root, StoreName.My })
            {
                token.ThrowIfCancellationRequested();
                using var store = new X509Store(storeName, location);
                try
                {
                    store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                    var certificates = store.Certificates;
                    try
                    {
                        if (certificates.Count > 1000) throw new InvalidOperationException("Zertifikatsspeicher ueber 1000 Eintraege; Abfrage begrenzt.");
                        foreach (var certificate in certificates)
                        {
                            token.ThrowIfCancellationRequested();
                            using var chain = new X509Chain();
                            chain.ChainPolicy.DisableCertificateDownloads = true;
                            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                            var valid = chain.Build(certificate);
                            var expiring = certificate.NotAfter <= DateTime.Now.AddDays(30);
                            var statuses = string.Join(", ", chain.ChainStatus.Select(s => $"{s.Status}: {s.StatusInformation.Trim()}"));
                            results.Add(new($"{location}/{storeName} / {certificate.Subject}",
                                $"Ablauf {certificate.NotAfter:yyyy-MM-dd} | offline Kette {(valid ? "OK" : "nicht bestaetigt")}",
                                !valid || expiring ? "warning" : "info",
                                $"Thumbprint {certificate.Thumbprint}. {statuses} " +
                                "Keine Online-Sperrpruefung. Ein lokal vertrauter Root ist kein Beweis fuer Unbedenklichkeit; keine automatische Entfernung."));
                        }
                    }
                    finally { foreach (var certificate in certificates) certificate.Dispose(); }
                }
                catch (Exception ex) when (ex is CryptographicException or UnauthorizedAccessException or InvalidOperationException)
                {
                    results.Add(new($"{location}/{storeName}", "Nicht vollstaendig lesbar", "unknown", ex.Message));
                }
            }
        return results;
    }
}
