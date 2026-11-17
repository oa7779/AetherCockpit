using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace SystemCockpit.Services;

public static class WakeOnLan
{
    public const int DefaultPort = 9;

    public static byte[] ParseMacAddress(string macAddress)
    {
        ArgumentNullException.ThrowIfNull(macAddress);
        var value = macAddress.Trim();
        string[] octets;
        if (value.Contains(':') && value.Contains('-'))
            throw new ArgumentException("MAC-Adresse mit Doppelpunkten, Bindestrichen oder ohne Trennzeichen eingeben.");
        if (value.Contains(':') || value.Contains('-'))
            octets = value.Split(value.Contains(':') ? ':' : '-');
        else
            octets = value.Length == 12
                ? Enumerable.Range(0, 6).Select(index => value.Substring(index * 2, 2)).ToArray()
                : [];

        if (octets.Length != 6 || octets.Any(part => part.Length != 2 ||
                !byte.TryParse(part, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)))
            throw new ArgumentException("MAC-Adresse muss aus sechs hexadezimalen Bytes bestehen.");

        var bytes = octets.Select(part => byte.Parse(part, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)).ToArray();
        if ((bytes[0] & 1) != 0 || bytes.All(value => value == 0))
            throw new ArgumentException("Eine individuelle Unicast-MAC-Adresse ist erforderlich.");
        return bytes;
    }

    public static byte[] BuildMagicPacket(string macAddress)
    {
        var address = ParseMacAddress(macAddress);
        var packet = new byte[6 + 16 * address.Length];
        Array.Fill(packet, byte.MaxValue, 0, 6);
        for (var copy = 0; copy < 16; copy++)
            address.CopyTo(packet, 6 + copy * address.Length);
        return packet;
    }

    public static async Task<int> SendAsync(string macAddress, int port, CancellationToken cancellationToken)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port), "UDP-Port muss zwischen 1 und 65535 liegen.");
        var packet = BuildMagicPacket(macAddress);
        using var client = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        return await client.SendAsync(packet.AsMemory(), new IPEndPoint(IPAddress.Broadcast, port), cancellationToken);
    }
}
