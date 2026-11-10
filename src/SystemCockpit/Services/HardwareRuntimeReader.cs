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
            var facts = new List<HardwareFact>();
            void Read(string key, Func<object> read, string unit = "", DataClassification privacy = DataClassification.Normal)
            {
                try { facts.Add(Fact(key, read(), source, time, unit, privacy)); }
                catch (Exception ex) when (ex is NetworkInformationException or NotSupportedException)
                {
                    var state = NetworkError(ex);
                    if (state == DataAvailability.ProviderError) System.Diagnostics.Trace.TraceError("Hardware Netzwerkfeld {0}: {1}", key, ex);
                    else System.Diagnostics.Trace.TraceWarning("Hardware Netzwerkfeld {0}: {1}; {2}", key, state, ex.Message);
                    facts.Add(HardwareFact.Missing(key, key, source, time,
                        ex is NetworkInformationException native
                            ? $"Netzwerkfeld nicht lesbar (Windows-Fehlercode {native.ErrorCode}, HRESULT 0x{ex.HResult:X8})."
                            : $"Netzwerkfeld nicht lesbar ({ex.GetType().Name}, HRESULT 0x{ex.HResult:X8}).", state,
                        unit, privacy));
                }
            }
            Read("Modell", () => adapter.Description);
            Read("Typ", () => adapter.NetworkInterfaceType);
            Read("Linkstatus", () => adapter.OperationalStatus);
            Read("Linkgeschwindigkeit", () => adapter.Speed, "bit/s");
            Read("MAC", () => adapter.GetPhysicalAddress(), privacy: DataClassification.Sensitive);
            Read("IP-Adressen", () => string.Join("; ", adapter.GetIPProperties().UnicastAddresses.Select(a => a.Address)), privacy: DataClassification.Sensitive);
            Read("Gateways", () => string.Join("; ", adapter.GetIPProperties().GatewayAddresses.Select(a => a.Address)), privacy: DataClassification.Sensitive);
            Read("DNS", () => string.Join("; ", adapter.GetIPProperties().DnsAddresses), privacy: DataClassification.Sensitive);
            Read("IPv4 MTU", () => adapter.GetIPProperties().GetIPv4Properties()?.Mtu
                ?? throw new NotSupportedException("Keine IPv4-Eigenschaften."), "B");
            Read("DHCP", () => adapter.GetIPProperties().GetIPv4Properties()?.IsDhcpEnabled
                ?? throw new NotSupportedException("Keine IPv4-Eigenschaften."));
            Read("Rx Bytes (kumuliert; keine Rate)", () => adapter.GetIPStatistics().BytesReceived, "B");
            Read("Tx Bytes (kumuliert; keine Rate)", () => adapter.GetIPStatistics().BytesSent, "B");
            Read("Rx Fehler", () => adapter.GetIPStatistics().IncomingPacketsWithErrors);
            Read("Tx Fehler", () => adapter.GetIPStatistics().OutgoingPacketsWithErrors);
            result.Add(new(HardwareIntelligenceReader.Identity("net", adapter.Id), HardwareClass.Network, adapter.Name, facts));
        }
        return result;
    }

    internal static DataAvailability NetworkError(Exception error) => error switch
    {
        NotSupportedException => DataAvailability.Unsupported,
        NetworkInformationException { ErrorCode: 50 or 10043 or 10047 } => DataAvailability.Unsupported,
        NetworkInformationException { ErrorCode: 5 } => DataAvailability.AccessDenied,
        _ => DataAvailability.ProviderError
    };

    internal static IReadOnlyList<HardwareComponent> Audio()
    {
        var time = DateTimeOffset.UtcNow;
        var formats = new Dictionary<string, AudioTopology.FormatResult>(StringComparer.OrdinalIgnoreCase);
        var devices = AudioTopology.Read((id, format) => formats.Add("SWD\\MMDEVAPI\\" + id, format));
        return devices.Select(a =>
        {
            var facts = new List<HardwareFact>
            {
                Fact("Name", a.Name, "Windows Core Audio", time),
                Fact("Endpoint", a.Id, "IMMDevice.GetId", time, privacy: DataClassification.Sensitive),
                Fact("Status", a.Status, "IMMDevice.GetState", time),
                Fact("Standardrollen / Sitzungsstatus", a.Usage, "Windows Core Audio", time)
            };
            const string source = "IAudioClient.GetMixFormat (Shared-Mode-Audioengine; kein physisches Ausgabeformat)";
            if (formats.TryGetValue(a.Id, out var result) && result.Format is { } format)
            {
                facts.AddRange(new[]
                {
                    Fact("Audioengine / Formattag (Windows-Code)", format.Tag, source, time),
                    Fact("Audioengine / Kanäle", format.Channels, source, time),
                    Fact("Audioengine / Abtastrate", format.SamplesPerSecond, source, time, "Hz"),
                    Fact("Audioengine / Containerbreite", format.BitsPerSample, source, time, "bit/Sample"),
                    Fact("Audioengine / Datenrate", format.BytesPerSecond, source, time, "B/s"),
                    Fact("Audioengine / Blockausrichtung", format.BlockAlign, source, time, "B")
                });
                if (format.ValidBits is { } valid) facts.Add(Fact("Audioengine / Gültige Samplebits", valid, source, time, "bit/Sample"));
                if (format.ChannelMask is { } mask) facts.Add(Fact("Audioengine / Kanalmaske", $"0x{mask:X8}", source, time));
                if (format.Subformat is { } subformat) facts.Add(Fact("Audioengine / Subformat-GUID", subformat, source, time));
            }
            else facts.Add(HardwareFact.Missing("mix-format", "Audioengine-Mixformat", source, time,
                result is null ? "Endpunkt nicht aktiv; kein Mixformat abgefragt." : $"Formatabfrage HRESULT 0x{result.HResult:X8}.",
                result is null ? DataAvailability.Unavailable :
                    result.HResult == unchecked((int)0x80070005) ? DataAvailability.AccessDenied : DataAvailability.ProviderError));
            return new HardwareComponent(HardwareIntelligenceReader.Identity("audio", a.Id), HardwareClass.Audio, a.Name, facts);
        }).ToArray();
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
