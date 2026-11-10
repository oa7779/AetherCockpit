using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using AetherOperations;

namespace SystemCockpit.Services;

internal static class HardwareCpuFrequency
{
    private const string Source = "Windows PDH / Processor Information (englische Counterpfade)";
    internal static async Task<IReadOnlyList<HardwareComponent>> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new List<HardwareComponent>();
        var start = DateTimeOffset.UtcNow;
        var topology = Topology();
        var status = PdhOpenQuery(null, UIntPtr.Zero, out var query);
        if (status != 0) return [Error("query", status, start)];
        try
        {
            var counters = new List<(string Key, string Caption, string Unit, IntPtr Handle)>();
            foreach (var (key, caption, unit, counter) in new[]
            {
                ("frequency", "Windows gemeldete Prozessorfrequenz (kein effektiver Zyklustakt)", "MHz", "Processor Frequency"),
                ("performance", "Windows Prozessorleistung relativ zur Nominalleistung", "%", "% Processor Performance")
            })
            {
                status = PdhAddEnglishCounter(query, @"\Processor Information(*)\" + counter, UIntPtr.Zero, out var handle);
                if (status != 0) result.Add(Error(key, status, start));
                else counters.Add((key, caption, unit, handle));
            }
            if (counters.Count == 0) return result;
            status = PdhCollectQueryData(query);
            if (status != 0) return [.. result, Error("baseline", status, start)];
            var elapsed = Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromMilliseconds(300), token).ConfigureAwait(false);
            status = PdhCollectQueryData(query);
            var sampleSeconds = elapsed.Elapsed.TotalSeconds;
            var end = DateTimeOffset.UtcNow;
            if (status != 0) return [.. result, Error("sample", status, end)];
            token.ThrowIfCancellationRequested();
            if (topology != Topology())
                return [new("pdh:topology", HardwareClass.Cpu, "Windows Prozessorfrequenz",
                    [HardwareFact.Missing("topology", "Sampling", Source, end,
                        "Prozessortopologie änderte sich während der Stichprobe; Ergebnisse verworfen.", DataAvailability.Unavailable)])];
            var facts = new Dictionary<string, List<HardwareFact>>(StringComparer.Ordinal);
            foreach (var counter in counters)
            {
                var rows = ReadArray(counter.Handle);
                if (rows.Status != 0) { result.Add(Error(counter.Key, rows.Status, end)); continue; }
                foreach (var row in rows.Rows)
                {
                    if (!facts.TryGetValue(row.Name, out var values)) facts[row.Name] = values = [];
                    values.Add(ValidSample(row.Status, row.Value)
                        ? new(counter.Key, counter.Caption,
                            SourceObservation<string>.Present(row.Value.ToString("R", CultureInfo.InvariantCulture), counter.Unit,
                                HardwareIntelligenceReader.Metadata(Source, end)), HardwareCadence.Dynamic,
                            Calculation: $"Windows-Counterformatierung; zwei Abfragen {start:O} bis {end:O}, " +
                                $"Abstand {sampleSeconds:R} s. Instanzkennung unverändert; group,processor beziehungsweise Windows-Aggregat. " +
                                "Keine Umrechnung von Prozent in MHz, kein APERF/MPERF- oder Schlafzeit-Nachweis.")
                        : HardwareFact.Missing(counter.Key, counter.Caption, Source, end,
                            $"Keine gültige endliche Counterprobe (CStatus 0x{row.Status:X8}).", DataAvailability.Unavailable));
                }
            }
            result.AddRange(facts.Select(pair => new HardwareComponent(
                HardwareIntelligenceReader.Identity("pdh-cpu", pair.Key), HardwareClass.Cpu, "Windows Processor Information / " + pair.Key, pair.Value)));
            return result;
        }
        finally
        {
            var close = PdhCloseQuery(query);
            if (close != 0) Trace.TraceError("Hardware PDH close: 0x{0:X8}", close);
        }
    }

    internal static bool ValidSample(uint status, double value) => status is 0 or 1 && double.IsFinite(value) && value >= 0;
    private static string Topology() => string.Join(",", Enumerable.Range(0, GetActiveProcessorGroupCount())
        .Select(g => GetActiveProcessorCount(checked((ushort)g)).ToString(CultureInfo.InvariantCulture)));
    private static HardwareComponent Error(string key, uint status, DateTimeOffset time)
    {
        Trace.TraceWarning("Hardware PDH {0}: 0x{1:X8}", key, status);
        return new("pdh:error:" + key, HardwareClass.Cpu, "Windows Prozessor-Counter",
            [HardwareFact.Missing(key, "Counterstatus", Source, time, $"PDH Fehlercode 0x{status:X8}.",
                status is 0xc0000bb8 or 0xc0000bb9 ? DataAvailability.Unsupported :
                status == 0xc0000bdb ? DataAvailability.AccessDenied : DataAvailability.ProviderError)]);
    }

    private static (uint Status, List<(string Name, uint Status, double Value)> Rows) ReadArray(IntPtr counter)
    {
        uint bytes = 0;
        var status = PdhGetFormattedCounterArray(counter, 0x8200, ref bytes, out var count, IntPtr.Zero);
        if (status != 0x800007d2) return (status == 0 ? 0xc0000bc6 : status, []);
        if (bytes is 0 or > 16 * 1024 * 1024) throw new InvalidOperationException("PDH-Antwort überschreitet die Puffergrenze.");
        var memory = Marshal.AllocHGlobal(checked((int)bytes));
        try
        {
            status = PdhGetFormattedCounterArray(counter, 0x8200, ref bytes, out count, memory);
            if (status != 0) return (status, []);
            if (count > 65536 || (ulong)count * (uint)Marshal.SizeOf<CounterItem>() > bytes)
                throw new InvalidOperationException("Ungültige PDH-Arraygröße.");
            var rows = new List<(string, uint, double)>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(memory, checked(i * Marshal.SizeOf<CounterItem>())));
                rows.Add((Marshal.PtrToStringUni(item.Name) ?? throw new InvalidOperationException("PDH-Instanzname fehlt."), item.Status, item.Value));
            }
            return (0, rows);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct CounterItem
    {
        [FieldOffset(0)] public IntPtr Name;
        [FieldOffset(8)] public uint Status;
        [FieldOffset(16)] public double Value;
    }
    [DllImport("kernel32.dll")] private static extern ushort GetActiveProcessorGroupCount();
    [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort group);
    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? source, UIntPtr user, out IntPtr query);
    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string path, UIntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr memory);
}
