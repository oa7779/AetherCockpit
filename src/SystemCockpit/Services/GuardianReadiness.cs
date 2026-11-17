using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

internal static class GuardianReadiness
{
    internal static async Task<IReadOnlyList<RecoveryReadiness>> StoredSourcesAsync(GuardianContext context, CancellationToken token)
    {
        var result = new List<RecoveryReadiness>();
        var root = IntelligenceStore.DefaultDirectory;
        foreach (var (type, name) in new[] { ("baseline", "Baselines"), ("snapshot", "Snapshots"), ("recording", "Diagnoseaufnahmen"),
            ("session", "Watch-Regeln / Timeline"), ("preferences", "System-Intelligence-Einstellungen") })
        {
            var files = Directory.Exists(root) ? Directory.EnumerateFiles(root, type + "-*.json").Take(513).ToArray() : [];
            var valid = 0; var issues = new List<string>();
            foreach (var file in files.Take(512))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (new FileInfo(file).Length > IntelligenceStore.MaximumFileBytes) throw new JsonException("Dateigröße überschreitet das Quellenlimit.");
                    var json = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);
                    if (type is "baseline" or "snapshot")
                    {
                        var value = EvidenceFile.Decode<IntelligenceSnapshot>(type, json); IntelligenceModel.Validate(value);
                        if (value.HostScope != context.Host) throw new JsonException("Anderer oder fehlender Hostkontext.");
                    }
                    else if (type == "recording")
                    {
                        var value = EvidenceFile.Decode<DiagnosisRecording>(type, json); value.Validate();
                        if (value.Before.HostScope != context.Host || value.After.HostScope != context.Host) throw new JsonException("Aufnahmekontext passt nicht.");
                    }
                    else if (type == "session") EvidenceFile.Decode<IntelligenceSession>(type, json).Validate();
                    else EvidenceFile.Decode<IntelligencePreferences>(type, json).Validate();
                    valid++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                {
                    System.Diagnostics.Trace.TraceError("Guardian-Bereitschaft: {0}: {1}", file, ex);
                    issues.Add(Path.GetFileName(file) + ": " + ex.Message);
                }
            }
            if (files.Length > 512) issues.Add("Quelleninventar bei 512 Dateien begrenzt; weitere Dateien nicht geprüft.");
            result.Add(new(name, issues.Count > 0 ? ReadinessKind.NotAssessable : valid > 0 ? ReadinessKind.DocumentedOnly : ReadinessKind.NotProtected,
                $"{valid} integritäts-/schemageprüfte lokale Dateien. " + string.Join(" | ", issues.Take(8)),
                "Keine Kopie an unabhängiger Stelle. Aktueller Windows-Profilkontext; Session/Präferenzen enthalten keinen eigenen Hostnachweis. Keine Quelle verändert."));
        }
        return result;
    }
}
