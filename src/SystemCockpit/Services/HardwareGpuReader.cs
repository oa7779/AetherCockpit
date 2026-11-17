using System.Runtime.InteropServices;
using System.Globalization;
using AetherOperations;

namespace SystemCockpit.Services;

internal static class HardwareGpuReader
{
    internal static IReadOnlyList<HardwareComponent> Read()
    {
        var iid = typeof(IFactory).GUID;
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out var factory));
        try
        {
            var result = new List<HardwareComponent>();
            for (uint index = 0; ; index++)
            {
                var hr = factory.EnumAdapters1(index, out var adapter);
                if (hr == unchecked((int)0x887A0002)) break;
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    Marshal.ThrowExceptionForHR(adapter.GetDesc1(out var desc));
                    var time = DateTimeOffset.UtcNow;
                    var facts = new List<HardwareFact>();
                    void Add(string name, object value, string unit = "", HardwareCadence cadence = HardwareCadence.Inventory) =>
                        facts.Add(new(name, name, SourceObservation<string>.Present(Convert.ToString(value, CultureInfo.InvariantCulture)!, unit,
                            new("DXGI / IDXGIAdapter1.GetDesc1", SourceType.Direct, time, DataConfidence.ProviderReported,
                                DataAvailability.Available, documentationLink: new Uri("https://learn.microsoft.com/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc"))), cadence));
                    Add("DXGI Modell", desc.Description);
                    Add("Vendor ID", desc.Vendor.ToString("X4", CultureInfo.InvariantCulture));
                    Add("Device ID", desc.Device.ToString("X4", CultureInfo.InvariantCulture));
                    Add("Subsystem ID", desc.Subsystem.ToString("X8", CultureInfo.InvariantCulture));
                    Add("Revision", desc.Revision.ToString("X2", CultureInfo.InvariantCulture));
                    Add("Dedizierter Videospeicher (DXGI)", desc.DedicatedVideo.ToUInt64(), "B");
                    Add("Dedizierter Systemspeicher (DXGI)", desc.DedicatedSystem.ToUInt64(), "B");
                    Add("Gemeinsam nutzbarer Systemspeicher (DXGI)", desc.SharedSystem.ToUInt64(), "B");
                    Add("DXGI Flags (Software = Bit 1)", desc.Flags);
                    // A LUID names this adapter instance; it is not guessed from a model name or CIM AdapterRAM.
                    facts.Add(new("luid", "Adapter LUID", SourceObservation<string>.Present($"{desc.Luid.High:X8}:{desc.Luid.Low:X8}", "",
                        HardwareIntelligenceReader.Metadata("DXGI.GetDesc1.AdapterLuid", time, classification: DataClassification.Sensitive))));
                    try { facts.AddRange(HardwareGpuDriver.Read(desc.Luid.Low, desc.Luid.High, time)); }
                    catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
                    {
                        System.Diagnostics.Trace.TraceError("Hardware GPU-Treibermodell: {0}", ex);
                        facts.Add(HardwareFact.Missing("wddm", "Windows-Treibermodell", "Windows D3DKMT", time,
                            $"Windows-Grafikabfrage nicht verfügbar (HRESULT 0x{ex.HResult:X8}).", DataAvailability.Unsupported));
                    }
                    try { facts.AddRange(HardwareDirect3D.Read(adapter, time)); }
                    catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
                    {
                        System.Diagnostics.Trace.TraceError("Hardware Direct3D: {0}", ex);
                        facts.Add(HardwareFact.Missing("d3d12", "Direct3D 12", "D3D12CreateDevice", time,
                            $"Direct3D-Provider nicht verfügbar (HRESULT 0x{ex.HResult:X8}).",
                            ex is COMException ? DataAvailability.ProviderError : DataAvailability.Unsupported));
                    }
                    var nodeCount = uint.TryParse(facts.FirstOrDefault(f => f.Key == "nodes")?.Observation.Value,
                        NumberStyles.None, CultureInfo.InvariantCulture, out var reportedNodes) && reportedNodes is > 0 and <= 64 ? reportedNodes : 1;
                    for (uint node = 0; node < nodeCount; node++)
                    foreach (var segment in new[] { 0, 1 })
                    {
                        var name = segment == 0 ? "Lokal" : "Nichtlokal";
                        if (adapter is IAdapter3 extended)
                        {
                            hr = extended.QueryVideoMemoryInfo(node, segment, out var memory);
                            if (hr >= 0)
                            {
                                foreach (var (key, value) in new[] { ("Aktuelle Nutzung", memory.Usage), ("Budget", memory.Budget),
                                             ("Reserviert", memory.Reservation), ("Reservierbar", memory.Available) })
                                    facts.Add(new(name + node + key, name + " / Prozess / Node " + node + " / " + key, SourceObservation<string>.Present(
                                        value.ToString(CultureInfo.InvariantCulture), "B",
                                        HardwareIntelligenceReader.Metadata("IDXGIAdapter3.QueryVideoMemoryInfo (aktueller Prozess / Node " + node + ")", time)),
                                        HardwareCadence.Dynamic));
                            }
                            else facts.Add(HardwareFact.Missing(name + node, name + " Videospeicherinfo / Node " + node, "IDXGIAdapter3.QueryVideoMemoryInfo",
                                time, $"DXGI HRESULT 0x{hr:X8}.", DataAvailability.ProviderError));
                        }
                        else facts.Add(HardwareFact.Missing(name + node, name + " Videospeicherinfo / Node " + node, "IDXGIAdapter3",
                            time, "Adapter unterstützt IDXGIAdapter3 nicht.", DataAvailability.Unsupported));
                    }
                    result.Add(new($"dxgi:{desc.Luid.High:X8}:{desc.Luid.Low:X8}", HardwareClass.Gpu, "DXGI / " + desc.Description, facts));
                }
                finally { Marshal.ReleaseComObject(adapter); }
            }
            return result;
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DescriptionText;
        public uint Vendor, Device, Subsystem, Revision;
        public UIntPtr DedicatedVideo, DedicatedSystem, SharedSystem;
        public Luid Luid;
        public uint Flags;
        public readonly string Description => DescriptionText;
    }
    [StructLayout(LayoutKind.Sequential)] private struct VideoMemory
    {
        public ulong Budget, Usage, Available, Reservation;
    }
    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IFactory factory);

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFactory
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
        void EnumAdapters(); void MakeWindowAssociation(); void GetWindowAssociation(); void CreateSwapChain(); void CreateSoftwareAdapter();
        [PreserveSig] int EnumAdapters1(uint index, out IAdapter adapter);
    }
    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAdapter
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
        void EnumOutputs(); void GetDesc(); void CheckInterfaceSupport();
        [PreserveSig] int GetDesc1(out AdapterDescription description);
    }
    [ComImport, Guid("645967a4-1392-4310-a798-8053ce3e93fd"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAdapter3
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
        void EnumOutputs(); void GetDesc(); void CheckInterfaceSupport(); void GetDesc1(); void GetDesc2();
        void RegisterHardwareContentProtectionTeardownStatusEvent(); void UnregisterHardwareContentProtectionTeardownStatus();
        [PreserveSig] int QueryVideoMemoryInfo(uint node, int segment, out VideoMemory memory);
    }
}
