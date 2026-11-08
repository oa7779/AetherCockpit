using System.Buffers.Binary;
using System.Net;

namespace SystemCockpit.Services;

public sealed record PacketSummary(DateTimeOffset Time, string Source, string Destination, string Protocol,
    int SourcePort, int DestinationPort, int WireLength, bool Syn, bool DnsFailure);

internal static class PacketAnalysis
{
    public static PacketSummary Parse(ReadOnlySpan<byte> packet, int wireLength, DateTimeOffset time)
    {
        string source = "?", destination = "?", protocol = DesktopUiText.Get("Packet.EthernetIncomplete");
        var sourcePort = 0;
        var destinationPort = 0;
        var syn = false;
        var dnsFailure = false;
        var offset = 14;
        if (packet.Length < offset) return new(time, source, destination, protocol, 0, 0, wireLength, false, false);
        var type = BinaryPrimitives.ReadUInt16BigEndian(packet[12..]);
        for (var i = 0; i < 2 && type is 0x8100 or 0x88A8; i++)
        {
            if (packet.Length < offset + 4) return new(time, source, destination, DesktopUiText.Get("Packet.VlanTruncated"), 0, 0, wireLength, false, false);
            type = BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 2)..]);
            offset += 4;
        }
        byte next;
        var fragmented = false;
        if (type == 0x0800)
        {
            if (packet.Length < offset + 20 || packet[offset] >> 4 != 4)
                return new(time, source, destination, DesktopUiText.Get("Packet.Ipv4Truncated"), 0, 0, wireLength, false, false);
            var header = (packet[offset] & 15) * 4;
            if (header < 20 || packet.Length < offset + header)
                return new(time, source, destination, DesktopUiText.Get("Packet.Ipv4Invalid"), 0, 0, wireLength, false, false);
            var total = BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 2)..]);
            if (total < header)
                return new(time, source, destination, DesktopUiText.Get("Packet.Ipv4Length"), 0, 0, wireLength, false, false);
            packet = packet[..Math.Min(packet.Length, offset + total)];
            source = new IPAddress(packet.Slice(offset + 12, 4)).ToString();
            destination = new IPAddress(packet.Slice(offset + 16, 4)).ToString();
            next = packet[offset + 9];
            fragmented = (BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 6)..]) & 0x1FFF) != 0;
            offset += header;
        }
        else if (type == 0x86DD)
        {
            if (packet.Length < offset + 40 || packet[offset] >> 4 != 6)
                return new(time, source, destination, DesktopUiText.Get("Packet.Ipv6Truncated"), 0, 0, wireLength, false, false);
            source = new IPAddress(packet.Slice(offset + 8, 16)).ToString();
            destination = new IPAddress(packet.Slice(offset + 24, 16)).ToString();
            next = packet[offset + 6];
            var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 4)..]);
            packet = packet[..Math.Min(packet.Length, offset + 40 + payloadLength)];
            offset += 40;
            for (var i = 0; i < 8 && next is 0 or 43 or 60 or 44; i++)
            {
                if (packet.Length < offset + 2)
                    return new(time, source, destination, DesktopUiText.Get("Packet.Ipv6Extension"), 0, 0, wireLength, false, false);
                var length = next == 44 ? 8 : (packet[offset + 1] + 1) * 8;
                if (packet.Length < offset + length)
                    return new(time, source, destination, DesktopUiText.Get("Packet.Ipv6Extension"), 0, 0, wireLength, false, false);
                if (next == 44) fragmented |= (BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 2)..]) & 0xFFF8) != 0;
                next = packet[offset];
                offset += length;
            }
        }
        else return new(time, source, destination, type == 0x0806 ? "ARP" : $"Ethernet 0x{type:X4}", 0, 0, wireLength, false, false);
        protocol = fragmented ? "IP-Fragment" : next switch { 6 => "TCP", 17 => "UDP", 1 => "ICMP", 58 => "ICMPv6", _ => $"IP/{next}" };
        if (!fragmented && next is 6 or 17 && packet.Length >= offset + (next == 6 ? 20 : 8))
        {
            sourcePort = BinaryPrimitives.ReadUInt16BigEndian(packet[offset..]);
            destinationPort = BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 2)..]);
            syn = next == 6 && (packet[offset + 13] & 0x12) == 0x02;
            if (next == 17 && (sourcePort == 53 || destinationPort == 53))
            {
                protocol = "DNS / UDP";
                if (packet.Length >= offset + 20)
                {
                    var flags = BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 10)..]);
                    dnsFailure = (flags & 0x8000) != 0 && (flags & 15) != 0;
                }
            }
            else if (next == 6 && (sourcePort == 443 || destinationPort == 443)) protocol = DesktopUiText.Get("Packet.TlsUninspected");
        }
        return new(time, source, destination, protocol, sourcePort, destinationPort, wireLength, syn, dnsFailure);
    }
}

internal sealed class PacketAlertRules
{
    private DateTimeOffset _window;
    private readonly Dictionary<string, HashSet<int>> _synPorts = [];
    private readonly Dictionary<string, int> _dnsFailures = [];
    private readonly HashSet<string> _emitted = [];
    public string? Observe(PacketSummary packet, CockpitSettings settings)
    {
        if (_window == default || packet.Time - _window >= TimeSpan.FromMinutes(1) || packet.Time < _window)
        {
            _window = packet.Time;
            _synPorts.Clear();
            _dnsFailures.Clear();
            _emitted.Clear();
        }
        if (packet.Syn)
        {
            var key = packet.Source + " -> " + packet.Destination;
            if (!_synPorts.TryGetValue(key, out var ports))
            {
                if (_synPorts.Count >= 10000) return null;
                _synPorts[key] = ports = [];
            }
            if (ports.Count < settings.SynPortAlertThreshold) ports.Add(packet.DestinationPort);
            if (ports.Count >= settings.SynPortAlertThreshold && _emitted.Add("syn:" + key))
                return DesktopUiText.Format("Packet.SynAlert", key, ports.Count);
        }
        if (packet.DnsFailure)
        {
            var key = packet.Destination;
            if (!_dnsFailures.ContainsKey(key) && _dnsFailures.Count >= 10000) return null;
            _dnsFailures.TryGetValue(key, out var count);
            _dnsFailures[key] = ++count;
            if (count >= settings.DnsFailureAlertThreshold && _emitted.Add("dns:" + key))
                return DesktopUiText.Format("Packet.DnsAlert", key, count);
        }
        return null;
    }
}
