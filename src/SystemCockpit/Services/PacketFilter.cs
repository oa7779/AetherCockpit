using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace SystemCockpit.Services;

public sealed record PacketFilter(string Text, string Protocol, int? Port, bool SynOnly, bool DnsErrorsOnly)
{
    public static IReadOnlyList<string> Protocols { get; } = ["Alle", "TCP", "UDP", "DNS", "ICMP", "ARP"];

    public static PacketFilter Create(string text, string protocol, string port, bool synOnly, bool dnsErrorsOnly)
    {
        if (text.Length > 200 || text.Any(char.IsControl) || !Protocols.Contains(protocol))
            throw new ArgumentException("Adress-/Textfilter: maximal 200 Zeichen; bekanntes Protokoll waehlen.");
        int? parsed = null;
        if (port.Trim().Length > 0)
        {
            if (!int.TryParse(port.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 1 or > 65535)
                throw new ArgumentException("Port leer lassen oder eine Zahl von 1 bis 65535 eingeben.");
            parsed = value;
        }
        return new(text.Trim(), protocol, parsed, synOnly, dnsErrorsOnly);
    }

    public bool Matches(PacketSummary packet)
    {
        var protocolMatch = Protocol switch
        {
            "Alle" => true,
            "UDP" => packet.Protocol == "UDP" || packet.Protocol.StartsWith("DNS / UDP", StringComparison.Ordinal),
            "DNS" => packet.Protocol.StartsWith("DNS", StringComparison.Ordinal),
            "ICMP" => packet.Protocol is "ICMP" or "ICMPv6",
            _ => packet.Protocol == Protocol
        };
        if (!protocolMatch || Port.HasValue && packet.SourcePort != Port && packet.DestinationPort != Port ||
            SynOnly && !packet.Syn || DnsErrorsOnly && !packet.DnsFailure) return false;
        var haystack = $"{packet.Source} {packet.Destination} {packet.Protocol} {packet.SourcePort} {packet.DestinationPort}";
        return Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static string Describe(PacketSummary packet) =>
        $"{packet.Time:O}\n{Endpoint(packet.Source, packet.SourcePort)} -> {Endpoint(packet.Destination, packet.DestinationPort)}\n" +
        $"{packet.Protocol} / gemeldete Paketlaenge: {packet.WireLength} Bytes / SYN ohne ACK: {packet.Syn} / DNS-Fehlerantwort: {packet.DnsFailure}\n" +
        "Port 0 kann fehlende/nicht dekodierte Transportdaten bedeuten. 128-Byte-Mitschnitt: keine vollstaendige Nutzlast-/TLS-Analyse.";

    private static string Endpoint(string address, int port) => address.Contains(':') ? $"[{address}]:{port}" : $"{address}:{port}";

    public static string Csv(IEnumerable<PacketSummary> packets)
    {
        var csv = new StringBuilder("Time,Source,Destination,Protocol,SourcePort,DestinationPort,WireLength,Syn,DnsFailure\r\n");
        foreach (var p in packets)
            csv.AppendLine(string.Join(",", MainWindow.CsvCell(p.Time.ToString("O", CultureInfo.InvariantCulture)),
                MainWindow.CsvCell(p.Source), MainWindow.CsvCell(p.Destination), MainWindow.CsvCell(p.Protocol),
                p.SourcePort.ToString(CultureInfo.InvariantCulture), p.DestinationPort.ToString(CultureInfo.InvariantCulture),
                p.WireLength.ToString(CultureInfo.InvariantCulture), p.Syn, p.DnsFailure));
        return csv.ToString();
    }
}

public static class CaptureHandoff
{
    public static ProcessStartInfo Wireshark(string executable, string capture)
    {
        if (!Path.IsPathFullyQualified(executable) || !Path.GetFileName(executable).Equals("Wireshark.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(executable))
            throw new ArgumentException("Vorhandene Wireshark.exe bewusst auswaehlen; AETHER installiert kein Analyseprogramm.");
        if (!Path.IsPathFullyQualified(capture) || !File.Exists(capture) ||
            !new[] { ".pcap", ".pcapng" }.Contains(Path.GetExtension(capture), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Vorhandene PCAP-/PCAPNG-Datei auswaehlen.");
        var info = new ProcessStartInfo { FileName = executable, UseShellExecute = false };
        info.ArgumentList.Add("-r");
        info.ArgumentList.Add(capture);
        return info;
    }
}
