using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using AetherOperations;

namespace SystemCockpit.Services;

internal static class HardwareCpuReader
{
    internal static IReadOnlyList<HardwareComponent> Read()
    {
        var time = DateTimeOffset.UtcNow;
        var facts = new List<HardwareFact>();
        void Direct(string key, string name, object value, string unit = "") =>
            facts.Add(new(key, name, SourceObservation<string>.Present(
                Convert.ToString(value, CultureInfo.InvariantCulture)!, unit,
                new("Windows CPU API / CPUID (ausführender logischer Prozessor)", SourceType.Direct, time,
                    DataConfidence.ProviderReported, DataAvailability.Available,
                    documentationLink: new Uri("https://learn.microsoft.com/windows/win32/procthread/processor-information")))));
        var groupCount = GetActiveProcessorGroupCount();
        if (groupCount == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        Direct("groups", "Processor Groups", groupCount);
        for (ushort group = 0; group < groupCount; group++)
        {
            var count = GetActiveProcessorCount(group);
            if (count == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
            Direct("group:" + group, "Aktive logische Prozessoren in Group " + group, count);
        }
        if (X86Base.IsSupported)
        {
            var maximum = X86Base.CpuId(0, 0).Eax;
            var leaf0 = X86Base.CpuId(0, 0);
            Direct("vendor", "CPUID Vendor", Encoding.ASCII.GetString(
                BitConverter.GetBytes(leaf0.Ebx).Concat(BitConverter.GetBytes(leaf0.Edx)).Concat(BitConverter.GetBytes(leaf0.Ecx)).ToArray()));
            if (maximum >= 1)
            {
                var leaf = X86Base.CpuId(1, 0);
                Direct("leaf1", "CPUID.01 EAX (roh)", $"0x{leaf.Eax:X8}");
                var family = (leaf.Eax >> 8) & 15;
                var model = (leaf.Eax >> 4) & 15;
                var displayFamily = family == 15 ? family + ((leaf.Eax >> 20) & 255) : family;
                var displayModel = family is 6 or 15 ? model + (((leaf.Eax >> 16) & 15) << 4) : model;
                facts.Add(HardwareIntelligenceReader.Derived("family", "Display Family", displayFamily.ToString(CultureInfo.InvariantCulture),
                    "CPUID.01:EAX", time, "BaseFamily = EAX[11:8]; bei 15 plus ExtendedFamily EAX[27:20]"));
                facts.Add(HardwareIntelligenceReader.Derived("model", "Display Model", displayModel.ToString(CultureInfo.InvariantCulture),
                    "CPUID.01:EAX", time, "BaseModel EAX[7:4]; bei Family 6/15 plus ExtendedModel EAX[19:16] << 4"));
                facts.Add(HardwareIntelligenceReader.Derived("stepping", "CPUID Stepping", (leaf.Eax & 15).ToString(CultureInfo.InvariantCulture),
                    "CPUID.01:EAX", time, "EAX[3:0]"));
                foreach (var (name, bit) in new[] { ("SSE", 25), ("SSE2", 26) })
                    Feature(name, leaf.Edx, bit, "01:EDX", time, facts);
                foreach (var (name, bit) in new[] { ("SSE3", 0), ("SSSE3", 9), ("SSE4.1", 19), ("SSE4.2", 20),
                             ("AES", 25), ("AVX (Hardwarebit)", 28), ("VMX", 5), ("Hypervisor-Bit", 31) })
                    Feature(name, leaf.Ecx, bit, "01:ECX", time, facts);
            }
            if (maximum >= 7)
            {
                var leaf = X86Base.CpuId(7, 0);
                foreach (var (name, bit) in new[] { ("AVX2 (Hardwarebit)", 5), ("AVX-512F (Hardwarebit)", 16), ("SHA", 29),
                             ("SMEP", 7), ("SMAP", 20) })
                    Feature(name, leaf.Ebx, bit, "07.00:EBX", time, facts);
                Feature("Hybrid-Flag (ausführender Prozessor)", leaf.Edx, 15, "07.00:EDX", time, facts);
            }
            var extended = unchecked((uint)X86Base.CpuId(unchecked((int)0x80000000), 0).Eax);
            if (extended >= 0x80000001)
                Feature("SVM", X86Base.CpuId(unchecked((int)0x80000001), 0).Ecx, 2, "80000001:ECX", time, facts);
            Direct("avx-os", ".NET AVX nutzbar (CPU + OS)", Avx.IsSupported);
            Direct("avx2-os", ".NET AVX2 nutzbar (CPU + OS)", Avx2.IsSupported);
            Direct("avx512-os", ".NET AVX-512F nutzbar (CPU + OS)", Avx512F.IsSupported);
        }
        else facts.Add(HardwareFact.Missing("cpuid", "CPUID", ".NET X86Base", time,
            "CPUID auf dieser Architektur nicht unterstützt.", DataAvailability.Unsupported));
        facts.Add(HardwareFact.Missing("coretype", "P-/E-Core-Bezeichnung", "Windows EfficiencyClass", time,
            "Relative EfficiencyClass wird roh angezeigt; keine unbelegte P-/E-Core-Zuordnung."));
        return [new("cpu:native", HardwareClass.Cpu, "CPU / CPUID und Windows-Topologie", facts),
            .. Topology(time)];
    }

    private static void Feature(string name, int register, int bit, string leaf, DateTimeOffset time, List<HardwareFact> facts) =>
        facts.Add(HardwareIntelligenceReader.Derived("feature:" + name, name,
            ((unchecked((uint)register) >> bit) & 1).ToString(CultureInfo.InvariantCulture), "CPUID." + leaf,
            time, $"Bit {bit} aus {leaf}; Hardwareflag, keine Zusage zur OS-Nutzbarkeit oder zu anderen Sockeln."));

    private static IReadOnlyList<HardwareComponent> Topology(DateTimeOffset time)
    {
        uint length = 0;
        if (GetLogicalProcessorInformationEx(0xffff, IntPtr.Zero, ref length) ||
            Marshal.GetLastWin32Error() != 122 || length < 8)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = Marshal.AllocHGlobal(checked((int)length));
        try
        {
            if (!GetLogicalProcessorInformationEx(0xffff, buffer, ref length))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return ParseTopology(buffer, length, time);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static IReadOnlyList<HardwareComponent> ParseTopology(byte[] data, DateTimeOffset time)
    {
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { return ParseTopology(pinned.AddrOfPinnedObject(), checked((uint)data.Length), time); }
        finally { pinned.Free(); }
    }

    private static IReadOnlyList<HardwareComponent> ParseTopology(IntPtr buffer, uint length, DateTimeOffset time)
    {
            var result = new List<HardwareComponent>();
            for (var offset = 0; offset < length;)
            {
                if (length - offset < 8) throw new InvalidOperationException("Ungültige CPU-Topologielänge.");
                var ptr = IntPtr.Add(buffer, offset);
                var relation = Marshal.ReadInt32(ptr);
                var size = Marshal.ReadInt32(ptr, 4);
                if (size < 8 || size > length - offset) throw new InvalidOperationException("Ungültiger CPU-Topologieblock.");
                var facts = new List<HardwareFact>();
                void Add(string name, string value, string unit = "") => facts.Add(new(name, name,
                    SourceObservation<string>.Present(value, unit, new("GetLogicalProcessorInformationEx", SourceType.Direct,
                        time, DataConfidence.ProviderReported, DataAvailability.Available,
                        documentationLink: new Uri("https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-getlogicalprocessorinformationex")))));
                if (relation is 0 or 3 && size >= 32)
                {
                    if (relation == 0)
                    {
                        Add("SMT-Flag", Marshal.ReadByte(ptr, 8).ToString(CultureInfo.InvariantCulture));
                        Add("EfficiencyClass (relativ, kein P-/E-Label)", Marshal.ReadByte(ptr, 9).ToString(CultureInfo.InvariantCulture));
                    }
                    var count = unchecked((ushort)Marshal.ReadInt16(ptr, 30));
                    if (32 + count * 16 > size) throw new InvalidOperationException("CPU-Gruppenmasken sind abgeschnitten.");
                    for (var i = 0; i < count; i++)
                        Add("Group / Affinity " + i,
                            unchecked((ushort)Marshal.ReadInt16(ptr, 32 + i * 16 + 8)) + " / 0x" +
                            unchecked((ulong)Marshal.ReadInt64(ptr, 32 + i * 16)).ToString("X16", CultureInfo.InvariantCulture));
                }
                else if (relation == 2 && size >= 40)
                {
                    Add("Cache Level", Marshal.ReadByte(ptr, 8).ToString(CultureInfo.InvariantCulture));
                    Add("Associativity", Marshal.ReadByte(ptr, 9).ToString(CultureInfo.InvariantCulture));
                    Add("Cacheline", unchecked((ushort)Marshal.ReadInt16(ptr, 10)).ToString(CultureInfo.InvariantCulture), "B");
                    Add("Cachegröße", unchecked((uint)Marshal.ReadInt32(ptr, 12)).ToString(CultureInfo.InvariantCulture), "B");
                    Add("Cachetyp (0 Unified / 1 Instruction / 2 Data / 3 Trace)", Marshal.ReadInt32(ptr, 16).ToString(CultureInfo.InvariantCulture));
                }
                else if (relation == 1 && size >= 12)
                    Add("NUMA-Knoten", unchecked((uint)Marshal.ReadInt32(ptr, 8)).ToString(CultureInfo.InvariantCulture));
                if (facts.Count > 0) result.Add(new("cpu:topology:" + offset, HardwareClass.Cpu,
                    (relation switch { 0 => "Kern", 3 => "Sockel", 2 => "Cache", _ => "NUMA" }) + " / " + result.Count, facts));
                offset += size;
            }
            return result;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ushort GetActiveProcessorGroupCount();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetActiveProcessorCount(ushort group);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
}
