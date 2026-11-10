using System.Runtime.InteropServices;
using AetherOperations;

namespace SystemCockpit.Services;

internal static class HardwareDirect3D
{
    private const string Source = "Direct3D 12 / adapterbezogene CheckFeatureSupport-Abfrage";
    internal static IReadOnlyList<HardwareFact> Read(object adapter, DateTimeOffset time)
    {
        var iid = typeof(IDevice).GUID;
        var hr = D3D12CreateDevice(adapter, 0xb000, ref iid, out var device);
        try
        {
            if (hr < 0 || device is null) return [Failure("d3d12", "Direct3D 12", hr, time)];
            var facts = new List<HardwareFact>();
            void Add(string key, string name, string value) =>
                facts.Add(new(key, name, SourceObservation<string>.Present(value, "",
                    new(Source, SourceType.Direct, time, DataConfidence.ProviderReported, DataAvailability.Available,
                        documentationLink: new("https://learn.microsoft.com/windows/win32/api/d3d12/nf-d3d12-id3d12device-checkfeaturesupport")))));
            Add("d3d12", "Direct3D 12 Gerät", "Erfolgreich für diesen DXGI-Adapter erstellt; kein Render-/Lasttest.");
            var levels = new[] { 0xc200, 0xc100, 0xc000, 0xb100, 0xb000 };
            var pinned = GCHandle.Alloc(levels, GCHandleType.Pinned);
            try
            {
                var input = new FeatureLevels { Count = checked((uint)levels.Length), Levels = pinned.AddrOfPinnedObject() };
                var query = Query(device, 2, input);
                if (query.HResult == unchecked((int)0x80070057))
                    query = Query(device, 2, new FeatureLevels { Count = checked((uint)levels.Length - 1), Levels = IntPtr.Add(input.Levels, 4) });
                if (query.HResult >= 0)
                    Add("featurelevel", "Höchstes geprüftes Direct3D Feature Level (bis 12_2)", FeatureLevel(query.Value.Maximum));
                else facts.Add(Failure("featurelevel", "Direct3D Feature Level", query.HResult, time));
            }
            finally { pinned.Free(); }
            var options = Query(device, 27, new Options5());
            if (options.HResult >= 0) Add("raytracing", "Direct3D Raytracing Tier", Raytracing(options.Value.RaytracingTier));
            else facts.Add(Failure("raytracing", "Direct3D Raytracing Tier", options.HResult, time));
            var architecture = Query(device, 16, new Architecture());
            if (architecture.HResult >= 0)
            {
                Add("uma", "Unified Memory Architecture / Node 0", (architecture.Value.Uma != 0).ToString());
                Add("cache-uma", "Cache-kohärente UMA / Node 0", (architecture.Value.CoherentUma != 0).ToString());
            }
            else facts.Add(Failure("uma", "Direct3D Speicherarchitektur", architecture.HResult, time));
            return facts;
        }
        finally { if (device is not null) Marshal.ReleaseComObject(device); }
    }

    private static HardwareFact Failure(string key, string name, int hr, DateTimeOffset time) =>
        HardwareFact.Missing(key, name, Source, time, $"Direct3D HRESULT 0x{hr:X8}.",
            hr is unchecked((int)0x80004002) or unchecked((int)0x887A0004) ? DataAvailability.Unsupported :
                hr == unchecked((int)0x80070005) ? DataAvailability.AccessDenied : DataAvailability.ProviderError);

    internal static string FeatureLevel(uint value) => value switch
    {
        0xb000 => "11_0", 0xb100 => "11_1", 0xc000 => "12_0", 0xc100 => "12_1", 0xc200 => "12_2",
        _ => $"Windows Feature-Level-Code 0x{value:X4}"
    };

    internal static string Raytracing(uint value) => value switch
    {
        0 => "Nicht unterstützt (Tier 0)", 10 => "Tier 1.0", 11 => "Tier 1.1",
        _ => $"Windows Raytracing-Tier-Code {value}"
    };

    private static (int HResult, T Value) Query<T>(IDevice device, int feature, T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, memory, false);
            var hr = device.CheckFeatureSupport(feature, memory, checked((uint)size));
            return (hr, Marshal.PtrToStructure<T>(memory));
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct FeatureLevels
    {
        public uint Count;
        public IntPtr Levels;
        public uint Maximum;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Options5 { public int Tiled; public uint RenderPasses, RaytracingTier; }
    [StructLayout(LayoutKind.Sequential)] private struct Architecture { public uint Node; public int Tiled, Uma, CoherentUma, IsolatedMmu; }

    [DllImport("d3d12.dll", ExactSpelling = true)]
    private static extern int D3D12CreateDevice([MarshalAs(UnmanagedType.IUnknown)] object adapter, uint minimum,
        ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IDevice? device);

    [ComImport, Guid("189819F1-1DB6-4B57-BE54-1821339B85F7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        void GetPrivateData(); void SetPrivateData(); void SetPrivateDataInterface(); void SetName();
        void GetNodeCount(); void CreateCommandQueue(); void CreateCommandAllocator(); void CreateGraphicsPipelineState();
        void CreateComputePipelineState(); void CreateCommandList();
        [PreserveSig] int CheckFeatureSupport(int feature, IntPtr data, uint length);
    }
}
