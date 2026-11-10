using System.Globalization;
using System.Text.RegularExpressions;
using AetherOperations;

namespace SystemCockpit.Services;

internal static partial class ProcessGpu
{
    [GeneratedRegex(@"^pid_(\d+)_luid_0x([0-9a-f]{8})_0x([0-9a-f]{8})_phys_(\d+)_eng_(\d+)_engtype_(.+?)(?:#\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Instance();
    internal static (int Pid, string Luid, string Engine)? Parse(string name)
    {
        var match = Instance().Match(name);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) return null;
        return (pid, match.Groups[2].Value.ToUpperInvariant() + ":" + match.Groups[3].Value.ToUpperInvariant(),
            "phys " + match.Groups[4].Value + " / engine " + match.Groups[5].Value + " / " + match.Groups[6].Value);
    }
    internal static async Task<IReadOnlyList<ProcessRelation>> ReadAsync(CancellationToken token)
    {
        var sample = await HardwareCpuFrequency.SampleNamedAsync(@"\GPU Engine(*)\Utilization Percentage", token).ConfigureAwait(false);
        if (sample.Status != 0)
        {
            System.Diagnostics.Trace.TraceWarning("Prozess GPU-PDH: 0x{0:X8}", sample.Status);
            return [new("GPU", "provider", null, null, $"PDH 0x{sample.Status:X8}; keine Zuordnung aus globaler GPU-Last.",
                "Windows GPU Engine / PDH", sample.End, sample.Status is 0xc0000bb8 or 0xc0000bb9 ? DataAvailability.Unsupported : DataAvailability.Unavailable)];
        }
        var rows = new List<ProcessRelation>();
        foreach (var row in sample.Rows)
        {
            var parsed = Parse(row.Name);
            if (parsed is not { } target || !HardwareCpuFrequency.ValidSample(row.Status, row.Value)) continue;
            rows.Add(new("GPU", target.Luid, target.Pid, null,
                $"{target.Engine} / {row.Value.ToString("R", CultureInfo.InvariantCulture)} % / {sample.Start:O} → {sample.End:O}; Engines nicht addieren",
                "Windows GPU Engine Instanz / PID + Adapter-LUID + Engine; PDH-NOCAP100", sample.End, DataAvailability.Available));
        }
        return rows;
    }
}
