using System.Runtime.InteropServices;
using AetherOperations;

namespace SystemCockpit.Services;

internal static class HardwareGpuDriver
{
    private const string Source = "Windows D3DKMTQueryAdapterInfo / exakte DXGI Adapter-LUID";
    internal static IReadOnlyList<HardwareFact> Read(uint low, int high, DateTimeOffset time)
    {
        var adapter = new OpenAdapter { Low = low, High = high };
        var status = D3DKMTOpenAdapterFromLuid(ref adapter);
        if (status < 0) return [Failure("wddm", status, time)];
        try
        {
            var facts = new List<HardwareFact>();
            foreach (var type in new uint[] { 13, 15 })
            {
                var memory = Marshal.AllocHGlobal(4);
                try
                {
                    Marshal.WriteInt32(memory, 0);
                    var query = new Query { Handle = adapter.Handle, Type = type, Data = memory, Size = 4 };
                    status = D3DKMTQueryAdapterInfo(ref query);
                    if (status < 0) { facts.Add(Failure(type == 13 ? "wddm" : "adapter-type", status, time)); continue; }
                    var value = unchecked((uint)Marshal.ReadInt32(memory));
                    void Add(string key, string name, string text) => facts.Add(new(key, name,
                        SourceObservation<string>.Present(text, "", HardwareIntelligenceReader.Metadata(Source, time))));
                    if (type == 13) Add("wddm", "Windows-Treibermodell", Wddm(value));
                    else
                    {
                        Add("adapter-type", "Windows AdapterType Flags (roh)", $"0x{value:X8}");
                        foreach (var (key, caption, mask) in new[]
                        {
                            ("render", "RenderSupported", 1u), ("display", "DisplaySupported", 2u),
                            ("software", "SoftwareDevice", 4u), ("hybrid-discrete", "HybridDiscrete", 16u),
                            ("hybrid-integrated", "HybridIntegrated", 32u)
                        }) Add("adapter-" + key, "Windows AdapterType / " + caption, ((value & mask) != 0).ToString());
                    }
                }
                finally { Marshal.FreeHGlobal(memory); }
            }
            return facts;
        }
        finally
        {
            status = D3DKMTCloseAdapter(ref adapter.Handle);
            if (status < 0) System.Diagnostics.Trace.TraceError("Hardware D3DKMT close NTSTATUS 0x{0:X8}", status);
        }
    }

    internal static string Wddm(uint code) => code switch
    {
        1000 => "WDDM 1.0", 1102 => "WDDM 1.1 Prerelease", 1105 => "WDDM 1.1",
        1200 or 1300 => $"WDDM 1.{code % 1000 / 100}",
        >= 2000 and <= 3200 when code % 100 == 0 => $"WDDM {code / 1000}.{code % 1000 / 100}",
        _ => "Windows Treibermodell-Code " + code.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private static HardwareFact Failure(string key, int status, DateTimeOffset time)
    {
        System.Diagnostics.Trace.TraceWarning("Hardware D3DKMT {0}: NTSTATUS 0x{1:X8}", key, status);
        return HardwareFact.Missing(key, "Windows GPU-Treiberabfrage", Source, time, $"Windows NTSTATUS 0x{status:X8}.",
            status == unchecked((int)0xc0000022) ? DataAvailability.AccessDenied :
            status is unchecked((int)0xc0000002) or unchecked((int)0xc00000bb) or unchecked((int)0xc000000d) ? DataAvailability.Unsupported :
            status == unchecked((int)0xc000009d) ? DataAvailability.Unavailable : DataAvailability.ProviderError);
    }

    [StructLayout(LayoutKind.Sequential)] private struct OpenAdapter { public uint Low; public int High; public uint Handle; }
    [StructLayout(LayoutKind.Sequential)] private struct Query { public uint Handle, Type; public IntPtr Data; public uint Size; }
    [DllImport("gdi32.dll")] private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref Query query);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref uint handle);
}
