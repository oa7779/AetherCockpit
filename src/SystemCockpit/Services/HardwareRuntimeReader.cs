using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using AetherOperations;

namespace SystemCockpit.Services;

internal static class HardwareRuntimeReader
{
    private static HardwareFact Fact(string key, object value, string source, DateTimeOffset time,
        string unit = "", DataClassification privacy = DataClassification.Normal) =>
        new(key, key, SourceObservation<string>.Present(Convert.ToString(value, CultureInfo.InvariantCulture)!, unit,
            HardwareIntelligenceReader.Metadata(source, time, classification: privacy)), HardwareCadence.Dynamic);

    internal static IReadOnlyList<HardwareComponent> Network()
    {
        var time = DateTimeOffset.UtcNow;
        var result = new List<HardwareComponent>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            var source = ".NET NetworkInterface";
            var facts = new List<HardwareFact>
            {
                Fact("Modell", adapter.Description, source, time), Fact("Typ", adapter.NetworkInterfaceType, source, time),
                Fact("Linkstatus", adapter.OperationalStatus, source, time),
                Fact("Linkgeschwindigkeit", adapter.Speed, source, time, "bit/s"),
                Fact("MAC", adapter.GetPhysicalAddress(), source, time, privacy: DataClassification.Sensitive)
            };
            try
            {
                var ip = adapter.GetIPProperties();
                facts.Add(Fact("IP-Adressen", string.Join("; ", ip.UnicastAddresses.Select(a => a.Address)), source, time, privacy: DataClassification.Sensitive));
                facts.Add(Fact("Gateways", string.Join("; ", ip.GatewayAddresses.Select(a => a.Address)), source, time, privacy: DataClassification.Sensitive));
                facts.Add(Fact("DNS", string.Join("; ", ip.DnsAddresses), source, time, privacy: DataClassification.Sensitive));
                var ipv4 = ip.GetIPv4Properties();
                if (ipv4 is not null)
                {
                    facts.Add(Fact("IPv4 MTU", ipv4.Mtu, source, time, "B"));
                    facts.Add(Fact("DHCP", ipv4.IsDhcpEnabled, source, time));
                }
                var statistics = adapter.GetIPStatistics();
                facts.Add(Fact("Rx Bytes (kumuliert; keine Rate)", statistics.BytesReceived, source, time, "B"));
                facts.Add(Fact("Tx Bytes (kumuliert; keine Rate)", statistics.BytesSent, source, time, "B"));
                facts.Add(Fact("Rx Fehler", statistics.IncomingPacketsWithErrors, source, time));
                facts.Add(Fact("Tx Fehler", statistics.OutgoingPacketsWithErrors, source, time));
            }
            catch (NetworkInformationException ex)
            {
                System.Diagnostics.Trace.TraceError("Hardware Netzwerkdetails: {0}", ex);
                facts.Add(HardwareFact.Missing("network-error", "Adapterdetails", source, time,
                    "Netzwerk-API fehlgeschlagen (Fehlercode " + ex.ErrorCode + ").", DataAvailability.ProviderError));
            }
            result.Add(new(HardwareIntelligenceReader.Identity("net", adapter.Id), HardwareClass.Network, adapter.Name, facts));
        }
        return result;
    }

    internal static IReadOnlyList<HardwareComponent> Audio()
    {
        var time = DateTimeOffset.UtcNow;
        return AudioTopology.Read().Select(a => new HardwareComponent(HardwareIntelligenceReader.Identity("audio", a.Id),
            HardwareClass.Audio, a.Name,
            [Fact("Name", a.Name, "Windows Core Audio", time),
             Fact("Endpoint", a.Id, "IMMDevice.GetId", time, privacy: DataClassification.Sensitive),
             Fact("Status", a.Status, "IMMDevice.GetState", time),
             Fact("Standardrollen / Sitzungsstatus", a.Usage, "Windows Core Audio", time)])).ToArray();
    }

    internal static IReadOnlyList<HardwareComponent> Displays()
    {
        var time = DateTimeOffset.UtcNow;
        var result = DisplayTopology.Read([]).Select(d => new HardwareComponent(HardwareIntelligenceReader.Identity("screen", d.Id),
            HardwareClass.Display, d.Name,
            [Fact("Aktuelle Desktopauflösung / Rolle", d.Usage, "EnumDisplayDevices / Windows Screen", time),
             Fact("Monitorpfad", d.Id, "EnumDisplayDevices", time, privacy: DataClassification.Sensitive)])).ToList();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var mode = new DisplayMode { Size = checked((ushort)Marshal.SizeOf<DisplayMode>()) };
            if (!EnumDisplaySettingsEx(screen.DeviceName, -1, ref mode, 0))
            {
                result.Add(new(HardwareIntelligenceReader.Identity("mode", screen.DeviceName), HardwareClass.Display,
                    "Windows-Anzeigemodus", [HardwareFact.Missing("mode", "Anzeigemodus", "EnumDisplaySettingsEx", time,
                        "Windows lieferte keinen aktuellen Anzeigemodus.", DataAvailability.ProviderError)]));
                continue;
            }
            const string source = "EnumDisplaySettingsEx / ENUM_CURRENT_SETTINGS";
            result.Add(new(HardwareIntelligenceReader.Identity("mode", screen.DeviceName), HardwareClass.Display,
                "Anzeigemodus / " + screen.DeviceName,
                [Fact("Aktuelle Breite", mode.Width, source, time, "px"), Fact("Aktuelle Höhe", mode.Height, source, time, "px"),
                 Fact("Refresh Rate (Windows gemeldet; 0/1 = Default)", mode.Frequency, source, time, "Hz"),
                 Fact("Farbtiefe (Desktopmodus, nicht Panel)", mode.BitsPerPixel, source, time, "bit"),
                 Fact("Orientierung (0/1/2/3 = 0/90/180/270°)", mode.Orientation, source, time)]));
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int X, Y;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPixel, Width, Height, Flags, Frequency, IcmMethod, IcmIntent, MediaType, DitherType,
            Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsEx(string device, int mode, ref DisplayMode data, uint flags);
}
