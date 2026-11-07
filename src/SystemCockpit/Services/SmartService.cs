using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SystemCockpit.Services;

public sealed record SmartDevice(string Name, string Type, string Info)
{
    public override string ToString() => $"{Name} [{Type}] {Info}";
}
public sealed record SmartReport(string Summary, string Json, int ExitCode);

public sealed class SmartService(string executable)
{
    public static string DetectExecutable()
    {
        foreach (var path in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "smartctl.exe"),
            @"C:\Program Files\smartmontools\bin\smartctl.exe"
        }) if (File.Exists(path)) return path;
        return "";
    }

    public async Task<List<SmartDevice>> ScanAsync(CancellationToken token)
    {
        var result = await RunAsync(["--scan-open", "-j"], token);
        using var json = JsonDocument.Parse(result.Output);
        CheckExit(result.ExitCode);
        if (!json.RootElement.TryGetProperty("devices", out var devices)) return [];
        return devices.EnumerateArray().Select(d => new SmartDevice(d.GetProperty("name").GetString()!,
            d.GetProperty("type").GetString()!, d.TryGetProperty("info_name", out var info) ? info.GetString() ?? "" : "")).ToList();
    }

    public async Task<SmartReport> ReadAsync(SmartDevice device, CancellationToken token)
    {
        ValidateDevice(device);
        var result = await RunAsync(["-x", "-j", "-d", device.Type, device.Name], token);
        CheckExit(result.ExitCode);
        return new(Summarize(result.Output, result.ExitCode), result.Output, result.ExitCode);
    }

    public async Task<SmartReport> ExecuteAsync(SmartDevice device, string operation, string argument, CancellationToken token)
    {
        if (device.Type.StartsWith("nvme", StringComparison.OrdinalIgnoreCase) &&
            operation is "conveyance" or "selective" or "aam" or "apm")
            throw new NotSupportedException("Diese ATA-spezifische Aktion ist fuer NVMe nicht definiert. Short/Long nur bei gemeldeter Device-Self-Test-Unterstuetzung.");
        var args = BuildOperation(device, operation, argument);
        var result = await RunAsync(args, token);
        CheckExit(result.ExitCode);
        return new($"SMART-Befehl abgeschlossen, Exit {result.ExitCode}. " +
            "Selbsttests laufen im Laufwerk weiter; fuer Fortschritt und Ergebnis erneut Diagnose lesen.",
            result.Output, result.ExitCode);
    }

    public static string[] BuildOperation(SmartDevice device, string operation, string argument)
    {
        ValidateDevice(device);
        string[] action = operation switch
        {
            "short" or "long" or "conveyance" => ["-t", operation],
            "abort" => ["-X"],
            "selective" => Selective(argument),
            "aam" => ["-s", $"aam,{BoundedInteger(argument, 128, 254)}"],
            "apm" => ["-s", $"apm,{BoundedInteger(argument, 1, 254)}"],
            _ => throw new ArgumentException("Unbekannte SMART-Aktion.")
        };
        return [.. action, "-j", "-d", device.Type, device.Name];
    }

    private static string[] Selective(string argument)
    {
        var parts = argument.Split('-');
        if (parts.Length != 2 || !ulong.TryParse(parts[0], out var start) || !ulong.TryParse(parts[1], out var end) || start > end)
            throw new ArgumentException("Selektiver Test: LBA-Start-Ende, z.B. 0-100000.");
        return ["-t", $"select,{start}-{end}"];
    }
    private static int BoundedInteger(string text, int min, int max) =>
        int.TryParse(text, out var number) && number >= min && number <= max ? number
            : throw new ArgumentException($"Erwartet: Ganzzahl {min} bis {max}.");
    private static void ValidateDevice(SmartDevice device)
    {
        if (!Regex.IsMatch(device.Name, @"^(/dev/[A-Za-z0-9/_-]+|[A-Za-z]:|\\\\\.\\PhysicalDrive\d+)$") ||
            !Regex.IsMatch(device.Type, @"^[A-Za-z0-9,/_+-]+$"))
            throw new ArgumentException("Unerwarteter SMART-Geraetepfad/-typ.");
    }
    private async Task<CommandResult> RunAsync(string[] args, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new FileNotFoundException("smartctl fehlt. Offizielle smartmontools-Version installieren und EXE in Einstellungen auswaehlen.", executable);
        var result = await CommandRunner.RunAsync(executable, args, TimeSpan.FromSeconds(90), token, utf8: true);
        if (!string.IsNullOrWhiteSpace(result.Error)) throw new IOException($"smartctl: {result.Error}");
        return result;
    }
    private static void CheckExit(int exit)
    {
        // smartctl uses a bitmask: bits 0..2 are command/open/read failures, bits 3..7 are health/log findings.
        if ((exit & 7) != 0) throw new IOException($"smartctl Befehls-/Zugriffsfehler (Bitmaske {exit}). Administratorrechte, Transport und Geraeteunterstuetzung pruefen.");
    }
    public static string Summarize(string text, int exitCode)
    {
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        var lines = new List<string>();
        if (root.TryGetProperty("model_name", out var model)) lines.Add($"Modell: {model.GetString()}");
        if (root.TryGetProperty("smart_status", out var status) && status.TryGetProperty("passed", out var passed))
            lines.Add(passed.GetBoolean() ? "SMART Gesamtstatus: PASSED (keine Garantie fuer Fehlerfreiheit)" : "SMART Gesamtstatus: FAILED");
        else lines.Add("SMART Gesamtstatus: nicht gemeldet");
        if (root.TryGetProperty("temperature", out var temp) && temp.TryGetProperty("current", out var current))
            lines.Add($"Temperatur: {current} C");
        if (root.TryGetProperty("power_on_time", out var hours)) lines.Add($"Betriebszeit: {hours}");
        if (root.TryGetProperty("nvme_smart_health_information_log", out var nvme))
        {
            foreach (var field in new[] { "critical_warning", "percentage_used", "media_errors", "num_err_log_entries", "data_units_written", "available_spare" })
                if (nvme.TryGetProperty(field, out var value)) lines.Add($"NVMe {field}: {value}");
            lines.Add("NVMe num_err_log_entries ist kein ATA-Sektorzaehler. percentage_used ist verbrauchter Ausdauerwert, keine Restlebenszeit.");
        }
        if (root.TryGetProperty("ata_smart_attributes", out var ata) && ata.TryGetProperty("table", out var table))
            foreach (var attribute in table.EnumerateArray())
                lines.Add($"ATA {attribute.GetProperty("id")}: {attribute.GetProperty("name")} | {attribute.GetProperty("raw")}");
        lines.Add($"smartctl Exit-Bitmaske: {exitCode}. Rohdaten enthalten Faehigkeiten, Fehler- und Selbsttestlogs.");
        return string.Join(Environment.NewLine, lines);
    }
}
