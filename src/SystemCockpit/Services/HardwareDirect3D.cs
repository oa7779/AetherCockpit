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
            var nodes = device.GetNodeCount();
            if (nodes is 0 or > 64) throw new InvalidOperationException("Ungültige Direct3D-Nodezahl.");
            Add("nodes", "Direct3D Adapter-Nodes (keine Enginezählung)", nodes.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
            for (uint node = 0; node < nodes; node++)
            {
                var architecture = Query(device, 16, new Architecture { Node = node });
                if (architecture.HResult >= 0)
                {
                    Add("uma:" + node, "Unified Memory Architecture / Node " + node, (architecture.Value.Uma != 0).ToString());
                    Add("cache-uma:" + node, "Cache-kohärente UMA / Node " + node, (architecture.Value.CoherentUma != 0).ToString());
                    Add("isolated-mmu:" + node, "Isolierte MMU / Node " + node, (architecture.Value.IsolatedMmu != 0).ToString());
                }
                else facts.Add(Failure("uma:" + node, "Direct3D Speicherarchitektur / Node " + node, architecture.HResult, time));
                if (device is IVideoDevice video)
                {
                    var count = QueryVideo(video, 10, new VideoCount { Node = node });
                    if (count.HResult >= 0 && count.Value.Count <= 256)
                    {
                        Add("video-profiles:" + node, "Video-Decode-Profilanzahl / Node " + node, count.Value.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        if (count.Value.Count > 0)
                        {
                            var memory = Marshal.AllocHGlobal(checked((int)count.Value.Count * 16));
                            try
                            {
                                var profiles = QueryVideo(video, 1, new VideoProfiles { Node = node, Count = count.Value.Count, Profiles = memory });
                                if (profiles.HResult >= 0 && profiles.Value.Count <= count.Value.Count)
                                {
                                    for (var i = 0; i < profiles.Value.Count; i++)
                                        Add($"video-profile:{node}:{i}", $"Gemeldetes Decode-Profil-GUID / Node {node} / {i}",
                                            Marshal.PtrToStructure<Guid>(IntPtr.Add(memory, i * 16)).ToString());
                                }
                                else facts.Add(Failure("video-profiles-list:" + node, "Decode-Profile", profiles.HResult < 0 ? profiles.HResult : unchecked((int)0x80004005), time));
                            }
                            finally { Marshal.FreeHGlobal(memory); }
                        }
                    }
                    else facts.Add(Failure("video-profiles:" + node, "Video-Decode-Profile", count.HResult < 0 ? count.HResult : unchecked((int)0x80004005), time));
                    var streams = QueryVideo(video, 6, new VideoCount { Node = node });
                    if (streams.HResult >= 0)
                        Add("video-process-streams:" + node, "Video-Process maximale Inputstreams / Node " + node,
                            streams.Value.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    else facts.Add(Failure("video-process-streams:" + node, "Video-Process Inputstreams", streams.HResult, time));
                }
                else facts.Add(Failure("video:" + node, "ID3D12VideoDevice", unchecked((int)0x80004002), time));
            }
            var waves = Query(device, 8, new Options1());
            if (waves.HResult >= 0)
            {
                Add("wave-ops", "Shader Wave Operations", (waves.Value.WaveOps != 0).ToString());
                Add("wave-min", "Wave Lane Count Min", waves.Value.Min.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Add("wave-max", "Wave Lane Count Max", waves.Value.Max.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Add("shader-int64", "Int64 Shader Operations", (waves.Value.Int64 != 0).ToString());
            }
            else facts.Add(Failure("wave-ops", "Direct3D Options1", waves.HResult, time));
            var mesh = Query(device, 32, new Options7());
            if (mesh.HResult >= 0)
            {
                Add("mesh-tier", "Mesh Shader Tier (Windows-Code)", mesh.Value.Mesh.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Add("sampler-tier", "Sampler Feedback Tier (Windows-Code)", mesh.Value.Sampler.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else facts.Add(Failure("mesh-tier", "Direct3D Options7", mesh.HResult, time));
            foreach (var model in new uint[] { 0x69, 0x68, 0x67, 0x66, 0x65, 0x64, 0x63, 0x62, 0x61, 0x60, 0x51 })
            {
                var shader = Query(device, 7, new ShaderModel { Maximum = model });
                if (shader.HResult == unchecked((int)0x80070057)) continue;
                if (shader.HResult >= 0) Add("shader-model", "Höchstes geprüftes Shader Model (bis 6.9)", ShaderName(shader.Value.Maximum));
                else facts.Add(Failure("shader-model", "Shader Model", shader.HResult, time));
                break;
            }
            if (!facts.Any(f => f.Key == "shader-model"))
                facts.Add(Failure("shader-model", "Shader Model", unchecked((int)0x80070057), time));
            return facts;
        }
        finally { if (device is not null) Marshal.ReleaseComObject(device); }
    }

    private static HardwareFact Failure(string key, string name, int hr, DateTimeOffset time) =>
        HardwareFact.Missing(key, name, Source, time, $"Direct3D HRESULT 0x{hr:X8}.",
            hr is unchecked((int)0x80004002) or unchecked((int)0x887A0004) or unchecked((int)0x80070057) ? DataAvailability.Unsupported :
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

    internal static string ShaderName(uint value) => value is >= 0x60 and <= 0x69 || value == 0x51
        ? $"{value >> 4}.{value & 15}" : $"Windows Shader-Model-Code 0x{value:X}";

    private static (int HResult, T Value) Query<T>(IDevice device, int feature, T value) where T : struct
        => QueryBuffer(device.CheckFeatureSupport, feature, value);

    private static (int HResult, T Value) QueryVideo<T>(IVideoDevice device, int feature, T value) where T : struct
        => QueryBuffer(device.CheckFeatureSupport, feature, value);

    private static (int HResult, T Value) QueryBuffer<T>(Func<int, IntPtr, uint, int> query, int feature, T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, memory, false);
            var hr = query(feature, memory, checked((uint)size));
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
    [StructLayout(LayoutKind.Sequential)] private struct Options1 { public int WaveOps; public uint Min, Max, Total; public int Expanded, Int64; }
    [StructLayout(LayoutKind.Sequential)] private struct Options7 { public uint Mesh, Sampler; }
    [StructLayout(LayoutKind.Sequential)] private struct ShaderModel { public uint Maximum; }
    [StructLayout(LayoutKind.Sequential)] private struct VideoCount { public uint Node, Count; }
    [StructLayout(LayoutKind.Sequential)] private struct VideoProfiles { public uint Node, Count; public IntPtr Profiles; }

    [DllImport("d3d12.dll", ExactSpelling = true)]
    private static extern int D3D12CreateDevice([MarshalAs(UnmanagedType.IUnknown)] object adapter, uint minimum,
        ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IDevice? device);

    [ComImport, Guid("189819F1-1DB6-4B57-BE54-1821339B85F7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        void GetPrivateData(); void SetPrivateData(); void SetPrivateDataInterface(); void SetName();
        [PreserveSig] uint GetNodeCount();
        void CreateCommandQueue(); void CreateCommandAllocator(); void CreateGraphicsPipelineState();
        void CreateComputePipelineState(); void CreateCommandList();
        [PreserveSig] int CheckFeatureSupport(int feature, IntPtr data, uint length);
    }

    [ComImport, Guid("1F052807-0B46-4ACC-8A89-364F793718A4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVideoDevice
    {
        [PreserveSig] int CheckFeatureSupport(int feature, IntPtr data, uint length);
    }
}
