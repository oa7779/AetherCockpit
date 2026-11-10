using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using AetherOperations;
using Microsoft.Win32.SafeHandles;

namespace SystemCockpit.Services;

internal static class HardwareWifiReader
{
    private const string Source = "Windows Native Wi-Fi";
    internal static DataAvailability Availability(uint error) => error switch
    {
        5 => DataAvailability.AccessDenied,
        50 or 120 => DataAvailability.Unsupported,
        1168 or 1062 or 5023 => DataAvailability.Unavailable,
        _ => DataAvailability.ProviderError
    };

    private static HardwareFact Error(string key, string name, uint error, DateTimeOffset time) =>
        new(key, name, SourceObservation<string>.Missing("", new(Source, SourceType.Direct, time,
            DataConfidence.Unknown, Availability(error),
            error: $"Native Wi-Fi Fehlercode {error}; keine Rechte oder Windows-Einstellungen geändert.")),
            HardwareCadence.Dynamic);

    private static HardwareFact Fact(string key, string name, object value, DateTimeOffset time, string unit = "") =>
        new(key, name, SourceObservation<string>.Present(Convert.ToString(value, CultureInfo.InvariantCulture)!, unit,
            new(Source, SourceType.Direct, time, DataConfidence.ProviderReported, DataAvailability.Available,
                documentationLink: new("https://learn.microsoft.com/windows/win32/api/wlanapi/nf-wlanapi-wlanqueryinterface"))),
            HardwareCadence.Dynamic);

    internal static IReadOnlyList<HardwareComponent> Read(CancellationToken token)
    {
        var time = DateTimeOffset.UtcNow;
        var code = WlanOpenHandle(2, IntPtr.Zero, out _, out var client);
        using (client)
        {
            if (code != 0) return [new("wlan:status", HardwareClass.Wlan, Source, [Error("open", "WLAN-Dienst", code, time)])];
            code = WlanEnumInterfaces(client, IntPtr.Zero, out var list);
            try
            {
                if (code != 0) return [new("wlan:status", HardwareClass.Wlan, Source, [Error("interfaces", "WLAN-Adapter", code, time)])];
                if (list == IntPtr.Zero) throw new InvalidOperationException("Native Wi-Fi lieferte keinen Adapterpuffer.");
                var count = Marshal.ReadInt32(list);
                if (count is < 0 or > 1024) throw new InvalidOperationException("Ungültige Native-Wi-Fi-Adapterliste.");
                var result = new List<HardwareComponent>();
                for (var index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var item = Marshal.PtrToStructure<InterfaceInfo>(IntPtr.Add(list, checked(8 + index * Marshal.SizeOf<InterfaceInfo>())));
                    var id = item.Id;
                    var facts = new List<HardwareFact>
                    {
                        Fact("state", "WLAN-Verbindungsstatus (Windows-Code)", item.State, time),
                        new("interface", "Adapter-GUID", SourceObservation<string>.Present(id.ToString("D"), "",
                            HardwareIntelligenceReader.Metadata(Source, time, classification: DataClassification.Sensitive)))
                    };
                    try
                    {
                        if (item.State == 1)
                        {
                            var connection = Query(client, ref id, 7);
                            if (connection.Code == 0)
                            {
                                facts.AddRange(ParseConnection(connection.Data, time));
                                var channel = Query(client, ref id, 8);
                                facts.Add(channel.Code == 0 && channel.Data.Length == 4
                                    ? Fact("channel", "Kanal (Windows gemeldet)", BinaryPrimitives.ReadUInt32LittleEndian(channel.Data), time)
                                    : Error("channel", "WLAN-Kanal", channel.Code == 0 ? 13u : channel.Code, time));
                                facts.AddRange(ReadBss(client, ref id, connection.Data, time));
                            }
                            else facts.Add(Error("connection", "Aktuelle WLAN-Verbindung", connection.Code, time));
                        }
                        else facts.Add(HardwareFact.Missing("connection", "Aktuelle WLAN-Verbindung", Source, time,
                            "Adapter meldet keine verbundene WLAN-Sitzung; keine Linkwerte übernommen."));
                    }
                    catch (InvalidOperationException ex)
                    {
                        System.Diagnostics.Trace.TraceError("Hardware Native Wi-Fi: {0}", ex);
                        facts.Add(HardwareFact.Missing("connection-error", "WLAN-Detailstatus", Source, time,
                            ex.Message, DataAvailability.ProviderError));
                    }
                    result.Add(new(HardwareIntelligenceReader.Identity("wlan", id.ToString("D")), HardwareClass.Wlan,
                        "WLAN / " + item.Description, facts));
                }
                if (count == 0) result.Add(new("wlan:empty", HardwareClass.Wlan, Source,
                    [HardwareFact.Missing("interfaces", "WLAN-Adapter", Source, time, "Windows meldet keine Native-Wi-Fi-Schnittstellen.")]));
                return result;
            }
            finally { if (list != IntPtr.Zero) WlanFreeMemory(list); }
        }
    }

    private static (uint Code, byte[] Data) Query(ClientHandle client, ref Guid id, int opcode)
    {
        var code = WlanQueryInterface(client, ref id, opcode, IntPtr.Zero, out var size, out var data, out _);
        try
        {
            if (code != 0) return (code, []);
            if (data == IntPtr.Zero || size is 0 or > 4096) return (13, []);
            var bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return (0, bytes);
        }
        finally { if (data != IntPtr.Zero) WlanFreeMemory(data); }
    }

    internal static IReadOnlyList<HardwareFact> ParseConnection(byte[] data, DateTimeOffset time)
    {
        // WLAN_CONNECTION_ATTRIBUTES: two DWORDs, WCHAR[256], then association and security attributes.
        if (data.Length < 604 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(520)) > 32 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data) != 1)
            throw new InvalidOperationException("Ungültige oder nicht mehr verbundene Native-Wi-Fi-Verbindung.");
        var phy = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(568));
        var signal = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(576));
        if (signal > 100) throw new InvalidOperationException("Ungültige WLAN-Signalqualität.");
        return
        [
            Fact("phy", "PHY / WLAN-Standard", Phy(phy), time),
            Fact("signal", "Signalqualität (keine RSSI-Messung)", signal, time, "%"),
            Fact("rx-link", "Rx Linkrate (keine gemessene Datenrate)", BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(580)), time, "bit/s"),
            Fact("tx-link", "Tx Linkrate (keine gemessene Datenrate)", BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(584)), time, "bit/s")
        ];
    }

    internal static string Phy(uint value) => value switch
    {
        1 => "FHSS", 2 => "DSSS", 3 => "IR", 4 => "OFDM (802.11a)", 5 => "HR-DSSS (802.11b)",
        6 => "ERP (802.11g)", 7 => "HT (802.11n)", 8 => "VHT (802.11ac)",
        9 => "DMG (802.11ad)", 10 => "HE (802.11ax)", 11 => "EHT (802.11be)",
        _ => "Windows PHY-Code " + value.ToString(CultureInfo.InvariantCulture)
    };

    private static IReadOnlyList<HardwareFact> ReadBss(ClientHandle client, ref Guid id, byte[] connection, DateTimeOffset time)
    {
        var code = WlanGetNetworkBssList(client, ref id, IntPtr.Zero, 3, false, IntPtr.Zero, out var pointer);
        try
        {
            if (code != 0) return [Error("frequency", "WLAN-Frequenz / Band", code, time)];
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Native Wi-Fi lieferte keinen BSS-Puffer.");
            var size = Marshal.ReadInt32(pointer);
            if (size is < 8 or > 16 * 1024 * 1024) throw new InvalidOperationException("Ungültige Native-Wi-Fi-BSS-Länge.");
            var data = new byte[size];
            Marshal.Copy(pointer, data, 0, size);
            var frequency = FindFrequency(data, connection);
            if (frequency is null) return [HardwareFact.Missing("frequency", "WLAN-Frequenz / Band", Source, time,
                "Keine eindeutige aktuelle BSSID-/SSID-Zuordnung in der zwischengespeicherten Windows-BSS-Liste; kein aktiver Scan.")];
            return
            [
                Fact("frequency", "Mittenfrequenz", frequency.Value, time, "kHz"),
                HardwareIntelligenceReader.Derived("band", "Band", Band(frequency.Value), Source + " / ulChCenterFrequency", time,
                    "Band aus gemeldeter Mittenfrequenz; nicht aus überlappenden Kanalnummern geraten.") with { Cadence = HardwareCadence.Dynamic }
            ];
        }
        finally { if (pointer != IntPtr.Zero) WlanFreeMemory(pointer); }
    }

    internal static uint? FindFrequency(byte[] bss, byte[] connection)
    {
        if (connection.Length < 604 || bss.Length < 8) throw new InvalidOperationException("Unvollständige WLAN-BSS-Daten.");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(bss);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bss.AsSpan(4));
        const int stride = 360;
        if (length != bss.Length || count > (bss.Length - 8) / stride ||
            BinaryPrimitives.ReadUInt32LittleEndian(connection.AsSpan(520)) > 32)
            throw new InvalidOperationException("Ungültige WLAN-BSS-Liste.");
        var ssidLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(connection.AsSpan(520)));
        var frequencies = new List<uint>();
        for (var index = 0; index < count; index++)
        {
            var entry = bss.AsSpan(8 + index * stride, stride);
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != ssidLength ||
                !entry.Slice(4, ssidLength).SequenceEqual(connection.AsSpan(524, ssidLength)) ||
                !entry.Slice(40, 6).SequenceEqual(connection.AsSpan(560, 6)) ||
                BinaryPrimitives.ReadUInt32LittleEndian(entry[48..]) != BinaryPrimitives.ReadUInt32LittleEndian(connection.AsSpan(556)))
                continue;
            frequencies.Add(BinaryPrimitives.ReadUInt32LittleEndian(entry[92..]));
        }
        return frequencies.Count == 1 && frequencies[0] != 0 ? frequencies[0] : null;
    }

    internal static string Band(uint khz) => khz switch
    {
        >= 2_400_000 and <= 2_500_000 => "2,4 GHz",
        >= 4_900_000 and < 5_925_000 => "5 GHz",
        >= 5_925_000 and <= 7_125_000 => "6 GHz",
        >= 57_000_000 and <= 71_000_000 => "60 GHz",
        _ => "Unbekannt (Frequenz außerhalb der ausgewerteten Bänder)"
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InterfaceInfo
    {
        public Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public uint State;
    }

    private sealed class ClientHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle()
        {
            var code = WlanCloseHandle(handle, IntPtr.Zero);
            if (code != 0) System.Diagnostics.Trace.TraceError("WlanCloseHandle: Fehlercode {0}", code);
            return code == 0;
        }
    }

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out ClientHandle client);
    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanCloseHandle(IntPtr client, IntPtr reserved);
    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanEnumInterfaces(ClientHandle client, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanQueryInterface(ClientHandle client, ref Guid id, int opcode, IntPtr reserved,
        out uint size, out IntPtr data, out int type);
    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanGetNetworkBssList(ClientHandle client, ref Guid id, IntPtr ssid, int bssType,
        [MarshalAs(UnmanagedType.Bool)] bool security, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern void WlanFreeMemory(IntPtr memory);
}
