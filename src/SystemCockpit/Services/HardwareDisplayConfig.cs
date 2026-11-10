using System.Runtime.InteropServices;
using System.Globalization;
using AetherOperations;

namespace SystemCockpit.Services;

internal static class HardwareDisplayConfig
{
    private const string Source = "Windows QueryDisplayConfig / DisplayConfigGetDeviceInfo";
    internal static IReadOnlyList<HardwareComponent> Read()
    {
        ValidateLayout();
        var time = DateTimeOffset.UtcNow;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var code = GetDisplayConfigBufferSizes(2, out var pathCount, out var modeCount);
            if (code != 0) return [new("displayconfig:status", HardwareClass.Display, Source, [Error("paths", code, time)])];
            if (pathCount > 4096 || modeCount > 16384) throw new InvalidOperationException("Windows-Anzeigetopologie überschreitet die Abfragegrenze.");
            var paths = new DisplayPath[pathCount];
            var modes = new ModeInfo[modeCount];
            code = QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (code == 122) continue;
            if (code != 0) return [new("displayconfig:status", HardwareClass.Display, Source, [Error("paths", code, time)])];
            var scales = Scales();
            var result = new List<HardwareComponent>();
            foreach (var path in paths.Take(checked((int)pathCount)))
            {
                var facts = new List<HardwareFact>
                {
                    Fact("connection", "Windows-Ausgabeverbindung", Connection(path.Target.OutputTechnology), time),
                    Fact("rotation", "Zielrotation (Windows-Code)", path.Target.Rotation, time),
                    Fact("refresh-numerator", "Ziel-Refresh Zähler", path.Target.RefreshNumerator, time),
                    Fact("refresh-denominator", "Ziel-Refresh Nenner", path.Target.RefreshDenominator, time)
                };
                AddMode(facts, modes, modeCount, path.Source.Mode, 1, path.Source.Adapter, path.Source.Id, time);
                AddMode(facts, modes, modeCount, path.Target.Mode, 2, path.Target.Adapter, path.Target.Id, time);
                if (path.Target.RefreshDenominator != 0)
                    facts.Add(HardwareIntelligenceReader.Derived("refresh", "Windows-Ziel-Refresh",
                        ((double)path.Target.RefreshNumerator / path.Target.RefreshDenominator).ToString("R", CultureInfo.InvariantCulture),
                        Source, time, "Refresh-Zähler / Refresh-Nenner; Windows-Zielpfad.", "Hz") with { Cadence = HardwareCadence.Dynamic });
                var target = Request(new TargetName { Header = Header<TargetName>(2, path.Target.Adapter, path.Target.Id) });
                var label = "Windows-Anzeigeziel";
                if (target.Code == 0)
                {
                    if (!string.IsNullOrWhiteSpace(target.Value.FriendlyName)) label = target.Value.FriendlyName;
                    facts.Add(Fact("monitor-path", "Windows-Monitorpfad", target.Value.DevicePath, time, DataClassification.Sensitive));
                }
                else facts.Add(Error("monitor-path", target.Code, time));
                var preferred = Request(new PreferredMode { Header = Header<PreferredMode>(3, path.Target.Adapter, path.Target.Id) });
                if (preferred.Code == 0)
                {
                    facts.Add(Fact("preferred-width", "Bevorzugte Windows-Zielbreite (kein Panelnachweis)", preferred.Value.Width, time, unit: "px"));
                    facts.Add(Fact("preferred-height", "Bevorzugte Windows-Zielhöhe (kein Panelnachweis)", preferred.Value.Height, time, unit: "px"));
                }
                else facts.Add(Error("preferred-mode", preferred.Code, time));
                var color = Request(new AdvancedColor { Header = Header<AdvancedColor>(9, path.Target.Adapter, path.Target.Id) });
                if (color.Code == 0)
                {
                    facts.Add(Fact("advanced-color", "Windows Advanced Color unterstützt (nicht allein HDR-Nachweis)", (color.Value.Flags & 1) != 0, time));
                    facts.Add(Fact("advanced-color-active", "Windows Advanced Color aktiv", (color.Value.Flags & 2) != 0, time));
                    facts.Add(Fact("color-encoding", "Windows-Zielfarbcodierung (Code)", color.Value.Encoding, time));
                    facts.Add(Fact("bits-per-channel", "Windows-Zielbits pro Farbkanal (keine Panelspezifikation)", color.Value.BitsPerChannel, time, unit: "bit/Kanal"));
                }
                else facts.Add(Error("advanced-color", color.Code, time));
                var hdr = Request(new AdvancedColor2 { Header = Header<AdvancedColor2>(15, path.Target.Adapter, path.Target.Id) });
                if (hdr.Code == 0)
                {
                    facts.Add(Fact("hdr-supported", "Windows HDR unterstützt (Info2)", (hdr.Value.Flags & 16) != 0, time));
                    facts.Add(Fact("hdr-user-enabled", "Windows HDR vom Benutzer aktiviert (nicht zwingend aktiv)", (hdr.Value.Flags & 32) != 0, time));
                    facts.Add(Fact("active-color-mode", "Windows aktiver Farbmodus (Info2)", ColorMode(hdr.Value.Mode), time));
                }
                else facts.Add(Error("hdr-info2", hdr.Code, time));
                var source = Request(new SourceName { Header = Header<SourceName>(1, path.Source.Adapter, path.Source.Id) });
                if (source.Code == 0 && scales.TryGetValue(source.Value.DeviceName, out var scale))
                    facts.Add(scale.Code == 0
                        ? Fact("scale", "Skalierung der zugeordneten Desktopfläche", scale.Value, time, unit: "%")
                        : Error("scale", scale.Code, time));
                else facts.Add(Error("scale", source.Code == 0 ? 1168 : source.Code, time));
                result.Add(new($"displayconfig:{path.Target.Adapter.High:X8}:{path.Target.Adapter.Low:X8}:{path.Target.Id}",
                    HardwareClass.Display, label, facts));
            }
            return result;
        }
        return [new("displayconfig:status", HardwareClass.Display, Source,
            [HardwareFact.Missing("paths", "Windows-Anzeigetopologie", Source, time,
                "Anzeigetopologie änderte sich während drei Abfrageversuchen.", DataAvailability.ProviderError)])];
    }

    internal static void ValidateLayout()
    {
        if (Marshal.SizeOf<DisplayPath>() != 72 || Marshal.SizeOf<ModeInfo>() != 64 ||
            Marshal.SizeOf<DeviceHeader>() != 20 || Marshal.SizeOf<TargetName>() != 420 ||
            Marshal.SizeOf<SourceName>() != 84 || Marshal.SizeOf<PreferredMode>() != 80 ||
            Marshal.SizeOf<AdvancedColor>() != 32 || Marshal.SizeOf<AdvancedColor2>() != 36 || Marshal.SizeOf<MonitorInfo>() != 104)
            throw new InvalidOperationException("Nicht unterstütztes Windows-DisplayConfig-ABI.");
    }

    internal static string ColorMode(uint mode) => mode switch { 0 => "SDR", 1 => "WCG", 2 => "HDR", _ => "Windows-Farbmodus " + mode };

    private static void AddMode(List<HardwareFact> facts, ModeInfo[] modes, uint count, uint index,
        uint type, Luid adapter, uint id, DateTimeOffset time)
    {
        var label = type == 1 ? "source" : "target";
        if (index >= count || modes[index].Type != type || modes[index].Id != id ||
            modes[index].Adapter.High != adapter.High || modes[index].Adapter.Low != adapter.Low)
        {
            facts.Add(HardwareFact.Missing(label + "-mode", "Aktueller " + label + "-Modus", Source, time,
                "Keine gültige passende Modusreferenz im aktiven Windows-Pfad."));
            return;
        }
        var mode = modes[index];
        facts.Add(Fact(label + "-width", type == 1 ? "Aktuelle Windows-Quellbreite" : "Aktive Windows-Zielsignalbreite", mode.Width, time, unit: "px"));
        facts.Add(Fact(label + "-height", type == 1 ? "Aktuelle Windows-Quellhöhe" : "Aktive Windows-Zielsignalhöhe", mode.Height, time, unit: "px"));
        if (type == 1)
        {
            facts.Add(Fact("source-x", "Windows-Quellposition X", mode.X, time, unit: "px"));
            facts.Add(Fact("source-y", "Windows-Quellposition Y", mode.Y, time, unit: "px"));
        }
    }

    private static HardwareFact Fact(string key, string name, object value, DateTimeOffset time,
        DataClassification privacy = DataClassification.Normal, string unit = "") =>
        new(key, name, SourceObservation<string>.Present(Convert.ToString(value, CultureInfo.InvariantCulture)!, unit,
            HardwareIntelligenceReader.Metadata(Source, time, classification: privacy)), HardwareCadence.Dynamic);
    private static HardwareFact Error(string key, int code, DateTimeOffset time) =>
        HardwareFact.Missing(key, key, Source, time, $"Windows-Anzeigeabfrage Fehlercode 0x{code:X8}.",
            code is 5 or unchecked((int)0x80070005) ? DataAvailability.AccessDenied :
            code is 50 or 87 or unchecked((int)0x80070057) ? DataAvailability.Unsupported :
            code == 1168 ? DataAvailability.Unavailable : DataAvailability.ProviderError);

    internal static string Connection(uint value) => value switch
    {
        0 => "HD15", 1 => "S-Video", 2 => "Composite", 3 => "Component", 4 => "DVI", 5 => "HDMI", 6 => "LVDS",
        10 => "DisplayPort extern", 11 => "DisplayPort eingebettet", 12 => "UDI extern", 13 => "UDI eingebettet",
        15 => "Miracast", 16 => "Indirekt / kabelgebunden", 17 => "Indirekt / virtuell", 18 => "DisplayPort / USB-Tunnel",
        0x80000000 => "Intern", 0xffffffff => "Andere Verbindung",
        _ => "Windows-Ausgabetechnologie " + value.ToString(CultureInfo.InvariantCulture)
    };

    private static DeviceHeader Header<T>(uint type, Luid luid, uint id) where T : struct =>
        new() { Type = type, Size = checked((uint)Marshal.SizeOf<T>()), Adapter = luid, Id = id };
    private static (int Code, T Value) Request<T>(T input) where T : struct
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        try
        {
            Marshal.StructureToPtr(input, memory, false);
            var code = DisplayConfigGetDeviceInfo(memory);
            return (code, code == 0 ? Marshal.PtrToStructure<T>(memory) : input);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static Dictionary<string, (int Code, int Value)> Scales()
    {
        var result = new Dictionary<string, (int Code, int Value)>(StringComparer.OrdinalIgnoreCase);
        var error = 0;
        MonitorCallback callback = (IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = checked((uint)Marshal.SizeOf<MonitorInfo>()) };
            if (!GetMonitorInfo(monitor, ref info)) { error = Marshal.GetLastWin32Error(); return false; }
            var code = GetScaleFactorForMonitor(monitor, out var scale);
            result[info.DeviceName] = (code, scale);
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(error == 0 ? Marshal.GetLastWin32Error() : error);
        return result;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct SourceInfo { public Luid Adapter; public uint Id, Mode, Status; }
    [StructLayout(LayoutKind.Sequential)] private struct TargetInfo
    {
        public Luid Adapter;
        public uint Id, Mode, OutputTechnology, Rotation, Scaling, RefreshNumerator, RefreshDenominator, Scanline;
        public int Available;
        public uint Status;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayPath { public SourceInfo Source; public TargetInfo Target; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] private struct ModeInfo
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid Adapter;
        [FieldOffset(16)] public uint SourceWidth;
        [FieldOffset(20)] public uint SourceHeight;
        [FieldOffset(28)] public int X;
        [FieldOffset(32)] public int Y;
        [FieldOffset(40)] public uint TargetWidth;
        [FieldOffset(44)] public uint TargetHeight;
        public readonly uint Width => Type == 1 ? SourceWidth : TargetWidth;
        public readonly uint Height => Type == 1 ? SourceHeight : TargetHeight;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceHeader { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SourceName
    {
        public DeviceHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TargetName
    {
        public DeviceHeader Header;
        public uint Flags, Technology;
        public ushort Manufacturer, Product;
        public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [StructLayout(LayoutKind.Explicit, Size = 80)] private struct PreferredMode
    {
        [FieldOffset(0)] public DeviceHeader Header;
        [FieldOffset(20)] public uint Width;
        [FieldOffset(24)] public uint Height;
    }
    [StructLayout(LayoutKind.Sequential)] private struct AdvancedColor { public DeviceHeader Header; public uint Flags, Encoding, BitsPerChannel; }
    [StructLayout(LayoutKind.Sequential)] private struct AdvancedColor2 { public DeviceHeader Header; public uint Flags, Encoding, BitsPerChannel, Mode; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DisplayPath[] pathData,
        ref uint modes, [Out] ModeInfo[] modeData, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(IntPtr data);
    [DllImport("shcore.dll")] private static extern int GetScaleFactorForMonitor(IntPtr monitor, out int scale);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr rect, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
