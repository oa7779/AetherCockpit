using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using SystemCockpit.Services;

internal static class GuardianTraceAcceptance
{
    internal static int Run(string output)
    {
        if (!Path.IsPathFullyQualified(output) || File.Exists(output)) throw new ArgumentException("Neue absolute Ergebnisdatei erforderlich.");
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        var admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        var token = new { Administrator = admin, AuthenticationType = identity.AuthenticationType,
            ImpersonationLevel = identity.ImpersonationLevel.ToString(), ElevatedAutomatically = false,
            DurationSeconds = 2, MaximumMiB = 4, Provider = GuardianTrace.ProcessProvider, Keywords = "0x70" };
        Console.WriteLine(JsonSerializer.Serialize(token));
        Console.WriteLine("Manueller ETW-Prüfmodus: nur eigene Sitzung; keine Rechte-/Richtlinienänderung; sensible temporäre ETL wird nach Ende gelöscht.");
        var directory = Path.Combine(Path.GetTempPath(), "AetherGuardianAcceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var steps = new List<object>();
        GuardianTraceResult? result = null;
        string? failure = null; int? native = null;
        var before = process.HandleCount;
        var watch = Stopwatch.StartNew();
        var cleaned = false;
        var started = false;
        var bufferBalance = 0;
        try
        {
            result = new GuardianTrace().RunAcceptanceAsync(directory, 2, default,
                (name, code) =>
                {
                    if (name == "StartTrace" && code == 0) started = true;
                    if (name == "PropertiesBufferAllocated") bufferBalance++;
                    if (name == "PropertiesBufferFreed") bufferBalance--;
                    var managed = name is "PropertiesBufferAllocated" or "PropertiesBufferFreed" or "EtlFileExists" ||
                        name.StartsWith("Provider:", StringComparison.Ordinal);
                    steps.Add(new { Step = name, Kind = managed ? "ManagedCheck" : "NativeCall",
                        NativeCode = managed ? null : (uint?)code, Verified = code == 0 || name == "SessionAbsentAfterStop" && code == 4201,
                        ElapsedMs = watch.Elapsed.TotalMilliseconds });
                }).GetAwaiter().GetResult();
            if (result.Bytes is <= 0 or > 4 * 1024 * 1024 || result.LostEvents != 0 || result.LostBuffers != 0 ||
                result.State != "Completed" || result.ElapsedMilliseconds > 12000)
                throw new InvalidOperationException("ETW-Abnahmegate für Ergebnis, Verluste, Größe oder Dauer nicht bestanden.");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            failure = ex.Message; native = (ex as Win32Exception)?.NativeErrorCode;
            Console.Error.WriteLine("ETW-Prüfung nicht bestanden: " + ex.Message);
        }
        finally
        {
            // Cleanup only after proving this harness owns no still-active session.
            var safeToDelete = !started;
            try
            {
                if (started)
                {
                    var markers = Directory.GetFiles(directory, "*.etl.session");
                    if (markers.Length != 1) throw new InvalidDataException("Eigener ETW-Marker fehlt oder ist nicht eindeutig.");
                    var name = Path.GetFileName(markers[0]);
                    if (!Guid.TryParseExact(name[..^12], "N", out var id)) throw new InvalidDataException("Unbekannter eigener ETW-Marker.");
                    safeToDelete = GuardianTrace.InspectOrStop(id, directory, false) == "Nicht aktiv";
                    if (!safeToDelete) failure = "Eigene ETW-Sitzung nicht nachweislich beendet; Artefakte zur manuellen Untersuchung erhalten.";
                }
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                failure = "Cleanupprüfung: " + ex.Message;
                steps.Add(new { Step = "CleanupInspectionFailed", NativeCode = (ex as Win32Exception)?.NativeErrorCode, ElapsedMs = watch.Elapsed.TotalMilliseconds });
            }
            if (safeToDelete)
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
                    Directory.Delete(directory); cleaned = true;
                    steps.Add(new { Step = "OwnArtifactCleanup", Kind = "ManagedCheck", NativeCode = (int?)null,
                        Verified = true, ElapsedMs = watch.Elapsed.TotalMilliseconds });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failure = "Artefaktbereinigung: " + ex.Message;
                    steps.Add(new { Step = "OwnArtifactCleanupFailed", NativeCode = (int?)null, ElapsedMs = watch.Elapsed.TotalMilliseconds });
                }
            }
            if (bufferBalance != 0) failure = "Native Eigenschaftspuffer nicht ausgeglichen: " + bufferBalance;
            process.Refresh();
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Schema = "aether.etw.acceptance/1", Token = token, Steps = steps, Result = result, Error = failure,
                NativeCode = native, RealLifecyclePassed = failure is null && result is not null,
                CleanupVerified = cleaned, RetainedDirectory = cleaned ? null : directory,
                HandlesBefore = before, HandlesAfter = process.HandleCount,
                ManagedNativeBufferBalance = bufferBalance
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return failure is null ? 0 : 2;
    }
}
