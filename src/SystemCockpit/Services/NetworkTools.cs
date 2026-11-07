using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.Http;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace SystemCockpit.Services;

public sealed record LanDevice(string Address, string Status, long? Milliseconds, string Detail, string Mac = "", string Manufacturer = "");

public static class NetworkTools
{
    public static string DefaultGateway() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().GatewayAddresses)
        .Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))?.ToString()
        ?? throw new InvalidOperationException("Kein IPv4-Standardgateway gemeldet.");

    public static async Task<string> PingAsync(string host, CancellationToken token)
    {
        if (host.Length > 253 || (!IPAddress.TryParse(host, out _) && Uri.CheckHostName(host) == UriHostNameType.Unknown))
            throw new ArgumentException("Gueltige IP-Adresse oder Hostname erforderlich.");
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(2), cancellationToken: token);
        return $"{host}: {reply.Status}" + (reply.Status == IPStatus.Success ? $" / {reply.RoundtripTime} ms / {reply.Address}" : "");
    }

    public static (uint Start, int Count) ParseLocalRange(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
            !int.TryParse(parts[1], out var prefix) || prefix is < 24 or > 30)
            throw new ArgumentException("Privates IPv4-Netz /24 bis /30 erforderlich, maximal 254 Hosts.");
        var octets = ip.GetAddressBytes();
        if (!(octets[0] == 10 || octets[0] == 192 && octets[1] == 168 || octets[0] == 172 && octets[1] is >= 16 and <= 31))
            throw new ArgumentException("LAN-Scan ist auf private RFC1918-Netze begrenzt.");
        var address = (uint)(octets[0] << 24 | octets[1] << 16 | octets[2] << 8 | octets[3]);
        var mask = uint.MaxValue << (32 - prefix);
        return ((address & mask) + 1, (1 << (32 - prefix)) - 2);
    }

    public static async Task<List<LanDevice>> ScanAsync(string cidr, CancellationToken token, IProgress<string>? progress = null, string ouiCsv = "")
    {
        var (start, count) = ParseLocalRange(cidr);
        var vendors = ReadOui(ouiCsv);
        var devices = new System.Collections.Concurrent.ConcurrentBag<LanDevice>();
        await Parallel.ForEachAsync(Enumerable.Range(0, count),
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = token }, async (offset, cancellation) =>
            {
                var numeric = start + (uint)offset;
                var address = new IPAddress(new byte[] { (byte)(numeric >> 24), (byte)(numeric >> 16), (byte)(numeric >> 8), (byte)numeric });
                using var ping = new Ping();
                try
                {
                    var response = await ping.SendPingAsync(address, TimeSpan.FromMilliseconds(700), cancellationToken: cancellation);
                    if (response.Status == IPStatus.Success)
                    {
                        var macBytes = new byte[6];
                        uint length = 6;
                        var arpResult = SendARP(BitConverter.ToUInt32(address.GetAddressBytes()), 0, macBytes, ref length);
                        var mac = arpResult == 0 && length == 6 ? string.Join(":", macBytes.Select(b => b.ToString("X2"))) : "";
                        var vendor = mac.Length == 0 ? "MAC nicht gemeldet" : (macBytes[0] & 2) != 0 ? "Lokal/randomisierte MAC"
                            : vendors.TryGetValue(Convert.ToHexString(macBytes[..3]), out var name) ? name
                            : ouiCsv.Length == 0 ? "OUI-Datenbank nicht geladen" : "OUI nicht gefunden";
                        devices.Add(new(address.ToString(), "Antwort", response.RoundtripTime,
                            arpResult == 0 ? "ICMP/ARP. OUI identifiziert die Registrierung, nicht zwingend den Endgeraetehersteller."
                                : $"ICMP-Antwort; ARP-Status {arpResult}. Nur im direkt erreichbaren IPv4-Segment.", mac, vendor));
                        progress?.Report($"{address}: {response.RoundtripTime} ms");
                    }
                }
                catch (PingException ex)
                {
                    devices.Add(new(address.ToString(), "Abfragefehler", null, ex.Message));
                    progress?.Report($"{address}: Fehler {ex.Message}");
                }
            });
        return devices.OrderBy(d => IPAddress.Parse(d.Address).GetAddressBytes()[3]).ToList();
    }

    public static Dictionary<string, string> ReadOui(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (path.Length == 0) return result;
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("OUI-Datei groesser als 16 MiB.");
        using var parser = new TextFieldParser(path) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var header = parser.ReadFields() ?? throw new InvalidDataException("OUI-CSV leer.");
        var assignment = Array.IndexOf(header, "Assignment");
        var organization = Array.IndexOf(header, "Organization Name");
        if (assignment < 0 || organization < 0) throw new InvalidDataException("IEEE-OUI-CSV-Spalten fehlen.");
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields() ?? throw new InvalidDataException("OUI-CSV-Zeile ungueltig.");
            if (row.Length != header.Length) throw new InvalidDataException("OUI-CSV-Spaltenanzahl ungueltig.");
            var prefix = row[assignment];
            if (prefix.Length != 6 || !prefix.All(Uri.IsHexDigit)) continue;
            result[prefix] = row[organization];
            if (result.Count > 100000) throw new InvalidDataException("OUI-Datenbanklimit erreicht.");
        }
        return result;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint SendARP(uint destination, uint source, byte[] mac, ref uint length);

    public static async Task<string> CaptivePortalAsync(CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.GetAsync("http://www.msftconnecttest.com/connecttest.txt",
                HttpCompletionOption.ResponseHeadersRead, limit.Token);
            if ((int)response.StatusCode is >= 300 and < 400)
                return $"Moegliches Captive Portal: HTTP {(int)response.StatusCode}, Umleitung {response.Headers.Location}. Kein automatisches Oeffnen.";
            using var bodyStream = await response.Content.ReadAsStreamAsync(limit.Token);
            var buffer = new byte[1025];
            var bytesRead = 0;
            while (bytesRead < buffer.Length)
            {
                var read = await bodyStream.ReadAsync(buffer.AsMemory(bytesRead), limit.Token);
                if (read == 0) break;
                bytesRead += read;
            }
            if (bytesRead > 1024) return "Abweichende Konnektivitaetsantwort groesser als 1 KiB; moegliches Portal/Proxy. Inhalt nicht geladen.";
            var body = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            return response.IsSuccessStatusCode && body.Trim() == "Microsoft Connect Test"
                ? "Microsoft-Konnektivitaetsprobe erfolgreich. Kein Nachweis, dass jede Verbindung frei ist."
                : $"Abweichende Konnektivitaetsantwort HTTP {(int)response.StatusCode}; moegliches Portal, Proxy oder Filter.";
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Konnektivitaetsprobe hat das Zeitlimit von 10 Sekunden erreicht.");
        }
    }
}
