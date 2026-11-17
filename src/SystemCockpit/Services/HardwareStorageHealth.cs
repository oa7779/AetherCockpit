using System.Globalization;
using System.IO;
using System.Text.Json;
using AetherOperations;

namespace SystemCockpit.Services;

internal static class HardwareStorageHealth
{
    internal static async Task<IReadOnlyList<HardwareComponent>> ReadAsync(IReadOnlyList<HardwareComponent> components, CancellationToken token)
    {
        var executable = SmartService.DetectExecutable();
        var disks = components.Where(c => c.Id.StartsWith("Disk:", StringComparison.Ordinal)).ToArray();
        var result = components.ToList();
        var numbers = disks.Select(c => Field(c, "Number")).GroupBy(n => n).ToDictionary(g => g.Key ?? "", g => g.Count());
        var observed = disks.Where(d => d.Facts.Any(f => f.Key is "trim" or "removable" or "transport" && f.Observation.HasValue))
            .ToDictionary(d => d.Id, StringComparer.Ordinal);
        foreach (var disk in disks)
        {
            token.ThrowIfCancellationRequested();
            var time = DateTimeOffset.UtcNow;
            IReadOnlyList<HardwareFact> facts;
            const string source = "smartctl -x -j / Windows-Disknummer → /dev/pdN";
            var number = disk.Facts.SingleOrDefault(f => f.Key == "Number")?.Observation;
            if (executable.Length == 0)
                facts = [HardwareFact.Missing("smart", "SMART / NVMe Health", source, time,
                    "Vorhandenes smartctl nicht gefunden; keine Installation oder Ersatzabfrage.", DataAvailability.Unsupported)];
            else if (number?.Metadata.Availability != DataAvailability.Available ||
                !uint.TryParse(number.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                numbers[number.Value] != 1 || Field(disk, "UniqueId") is null || Field(disk, "Path") is null)
                facts = [HardwareFact.Missing("smart", "SMART / NVMe Health", source, time,
                    "Keine eindeutige Windows-Disknummer mit UniqueId und Gerätepfad; keine Modellnamenzuordnung.")];
            else
            {
                try
                {
                    var report = await new SmartService(executable).ReadAsync(
                        Device(index), token).ConfigureAwait(false);
                    time = DateTimeOffset.UtcNow;
                    facts = Parse(report.Json, Device(index).Name, report.ExitCode, time);
                    if (facts.Any(f => f.Observation.HasValue)) observed.TryAdd(disk.Id, disk);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or
                    TimeoutException or System.ComponentModel.Win32Exception)
                {
                    System.Diagnostics.Trace.TraceError("Hardware Storage-Health: {0}", ex);
                    facts = [HardwareFact.Missing("smart", "SMART / NVMe Health", source, time,
                        ex is SmartReadException smart ? $"Lesende smartctl-Abfrage fehlgeschlagen; Exit-Bitmaske {smart.ExitCode}." :
                            $"Lesende smartctl-Abfrage fehlgeschlagen ({ex.GetType().Name}, HRESULT 0x{ex.HResult:X8}).",
                        ex is UnauthorizedAccessException ? DataAvailability.AccessDenied : DataAvailability.ProviderError)];
                }
            }
            var position = result.FindIndex(c => c.Id == disk.Id);
            result[position] = disk with { Facts = disk.Facts.Concat(facts).ToArray() };
        }
        if (observed.Count > 0)
        {
            JsonElement[] identities = [];
            string? error = null;
            try
            {
                var check = await AdminShell.RunAsync("""
                    $ErrorActionPreference='Stop'
                    $rows=@(Get-CimInstance -Namespace root\Microsoft\Windows\Storage -ClassName MSFT_Disk -OperationTimeoutSec 8 |
                        Select-Object Number,UniqueId,Path)
                    ConvertTo-Json -InputObject $rows -Depth 3 -Compress
                    """, TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
                if (check.ExitCode != 0 || check.OutputTruncated) throw new InvalidOperationException("Windows-Diskidentitäten konnten nicht erneut geprüft werden.");
                identities = JsonSerializer.Deserialize<JsonElement[]>(check.Output) ?? throw new JsonException("Windows-Diskliste fehlt.");
                if (identities.Length > 2048) throw new JsonException("Windows-Diskliste überschreitet die Abfragegrenze.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or
                TimeoutException or System.ComponentModel.Win32Exception)
            {
                System.Diagnostics.Trace.TraceError("Hardware Storage-Identitätsprüfung: {0}", ex);
                error = $"Windows-Diskidentitäten nicht erneut prüfbar ({ex.GetType().Name}, HRESULT 0x{ex.HResult:X8}).";
            }
            foreach (var disk in observed.Values)
            {
                if (error is null && SameWindowsIdentity(disk, identities)) continue;
                var position = result.FindIndex(c => c.Id == disk.Id);
                result[position] = disk with
                {
                    Facts = WithoutDeviceProperties(disk).Append(HardwareFact.Missing("smart-identity", "Storage-Gerätezuordnung",
                        "MSFT_Disk Number / UniqueId / Path vor und nach lesenden Gerätedetails", DateTimeOffset.UtcNow,
                        error ?? "Windows-Geräteidentität änderte sich während der Abfrage; neue Health-/TRIM-/Transportwerte verworfen.",
                        DataAvailability.ProviderError)).ToArray()
                };
            }
        }
        return result;
    }

    internal static IReadOnlyList<HardwareFact> WithoutDeviceProperties(HardwareComponent disk) =>
        disk.Facts.Where(f => f.Key is not ("trim" or "removable" or "transport")).ToArray();

    internal static SmartDevice Device(uint diskNumber) =>
        new("/dev/pd" + diskNumber.ToString(CultureInfo.InvariantCulture), "auto", "");

    private static string? Field(HardwareComponent component, string key) =>
        component.Facts.SingleOrDefault(f => f.Key == key)?.Observation is { HasValue: true, Metadata.Availability: DataAvailability.Available } observation &&
            !string.IsNullOrWhiteSpace(observation.Value) ? observation.Value.Trim() : null;

    internal static bool SameWindowsIdentity(HardwareComponent component, IReadOnlyList<JsonElement> rows)
    {
        var number = Field(component, "Number");
        var matches = rows.Where(row => row.ValueKind == JsonValueKind.Object &&
            number is not null && number == HardwareIntelligenceReader.Text(row, "Number")).Take(2).ToArray();
        return matches.Length == 1 && new[] { "Number", "UniqueId", "Path" }.All(key =>
            Field(component, key) is { } original && original.Equals(HardwareIntelligenceReader.Text(matches[0], key), StringComparison.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<HardwareFact> Parse(string json, string expectedDevice, int exitCode, DateTimeOffset time)
    {
        const string source = "smartctl JSON / lesend -x -j; Windows-Disknummer → /dev/pdN; MSFT_Disk-Identität vor/nach Abfrage geprüft";
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("SMART-Bericht ist kein Objekt.");
        if (!root.TryGetProperty("device", out var device) || device.ValueKind != JsonValueKind.Object ||
            !device.TryGetProperty("name", out var deviceName) || deviceName.ValueKind != JsonValueKind.String ||
            !string.Equals(deviceName.GetString(), expectedDevice, StringComparison.Ordinal))
            return [HardwareFact.Missing("smart-identity", "SMART-Gerätezuordnung", source, time,
                "smartctl-Gerätepfad stimmt nicht mit der angefragten Windows-Disknummer überein; keine Healthwerte übernommen.",
                DataAvailability.ProviderError)];
        if ((exitCode & 7) != 0)
            return [HardwareFact.Missing("smart-read", "SMART-Lesestatus", source, time,
                $"smartctl Befehls-/Zugriffsfehler, Exit-Bitmaske {exitCode}.", DataAvailability.ProviderError)];
        var facts = new List<HardwareFact>();
        void Add(string key, string name, JsonElement value, string unit = "", DataClassification privacy = DataClassification.Normal)
        {
            if (value.ValueKind is not (JsonValueKind.Number or JsonValueKind.String or JsonValueKind.True or JsonValueKind.False))
                throw new JsonException("Nicht skalarer Storage-Healthwert.");
            facts.Add(new(key, name, SourceObservation<string>.Present(value.ValueKind == JsonValueKind.String
                ? value.GetString()! : value.GetRawText(), unit,
                HardwareIntelligenceReader.Metadata(source, time, classification: privacy)), HardwareCadence.Dynamic));
        }
        Add("smart-exit", "smartctl Exit-Bitmaske (Health-/Logbits sind keine Lesefehler)", JsonSerializer.SerializeToElement(exitCode));
        if (root.TryGetProperty("nvme_smart_health_information_log", out var nvme))
        {
            if (nvme.ValueKind != JsonValueKind.Object) throw new JsonException("NVMe-Healthlog ist kein Objekt.");
            foreach (var (key, name, unit) in new[]
            {
                ("critical_warning", "Critical Warning (NVMe-Bitmaske)", ""),
                ("temperature", "NVMe Composite Temperature (smartctl)", "°C"),
                ("available_spare", "Available Spare (NVMe gemeldet)", "%"),
                ("available_spare_threshold", "Available-Spare-Schwelle", "%"),
                ("percentage_used", "Percentage Used (verbrauchte Ausdauer, keine Restlebenszeit)", "%"),
                ("data_units_read", "Host Read Data Units (je 512.000 B, aufgerundet)", "NVMe Data Units"),
                ("data_units_written", "Host Write Data Units (je 512.000 B, aufgerundet)", "NVMe Data Units"),
                ("host_reads", "Host Read Commands", ""), ("host_writes", "Host Write Commands", ""),
                ("controller_busy_time", "Controller Busy Time", "min"), ("power_cycles", "Power Cycles", ""),
                ("power_on_hours", "Power-On Hours", "h"), ("unsafe_shutdowns", "Unsafe Shutdowns", ""),
                ("media_errors", "Media and Data Integrity Errors", ""), ("num_err_log_entries", "Error Information Log Entries", "")
            })
                if (nvme.TryGetProperty(key, out var value)) Add("nvme:" + key, name, value, unit);
                else facts.Add(HardwareFact.Missing("nvme:" + key, name, source, time, "smartctl meldet dieses NVMe-Feld nicht.", unit: unit));
        }
        if (root.TryGetProperty("ata_smart_attributes", out var ata))
        {
            if (ata.ValueKind != JsonValueKind.Object || !ata.TryGetProperty("table", out var attributes) ||
                attributes.ValueKind != JsonValueKind.Array || attributes.GetArrayLength() > 256)
                throw new JsonException("Ungültige ATA-SMART-Attributliste.");
            var seen = new HashSet<int>();
            foreach (var attribute in attributes.EnumerateArray())
            {
                if (attribute.ValueKind != JsonValueKind.Object || !attribute.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.Number ||
                    !idValue.TryGetInt32(out var id)) throw new JsonException("ATA-Attribut-ID fehlt.");
                if (id is < 1 or > 255 || !seen.Add(id)) throw new JsonException("Ungültige oder doppelte ATA-Attribut-ID.");
                foreach (var field in new[] { "value", "worst", "thresh" })
                    if (attribute.TryGetProperty(field, out var value)) Add($"ata:{id}:{field}", $"ATA {id} / {field} (Herstellerattribut)", value);
                if (attribute.TryGetProperty("raw", out var raw))
                {
                    if (raw.ValueKind != JsonValueKind.Object) throw new JsonException("ATA-Rohwert ist kein Objekt.");
                    if (raw.TryGetProperty("value", out var number)) Add($"ata:{id}:raw", $"ATA {id} / Rohwert (keine generische Deutung)", number);
                    if (raw.TryGetProperty("string", out var text)) Add($"ata:{id}:text", $"ATA {id} / Herstellertext", text, privacy: DataClassification.Sensitive);
                }
            }
        }
        if (!root.TryGetProperty("nvme_smart_health_information_log", out _) && !root.TryGetProperty("ata_smart_attributes", out _))
            facts.Add(HardwareFact.Missing("smart-data", "SMART / NVMe Health", source, time, "Gerät liefert keine ATA-Attribute oder NVMe-Healthlogwerte."));
        return facts;
    }
}
