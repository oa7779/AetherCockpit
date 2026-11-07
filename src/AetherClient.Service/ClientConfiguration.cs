using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AetherClient.Service;

internal sealed record ClientConfiguration(string ServerUrl, Guid DeviceId, string ProtectedToken,
    bool AllowChanges = false, bool AllowAutoRepair = false)
{
    internal static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AetherCockpit", "ManagedClient");
    internal static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("AETHER Managed Client v1"));

    public Uri ServerUri()
    {
        if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || !uri.IsLoopback && uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath != "/" || DeviceId == Guid.Empty || AllowAutoRepair && !AllowChanges)
            throw new InvalidDataException("Invalid managed client URL, identity or local permissions.");
        return uri;
    }

    public string Token()
    {
        var clear = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedToken), Entropy, DataProtectionScope.LocalMachine);
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public static async Task<ClientConfiguration> LoadAsync(CancellationToken token)
    {
        var json = await File.ReadAllBytesAsync(Path.Combine(Root, "client.json"), token);
        var config = JsonSerializer.Deserialize<ClientConfiguration>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Missing managed client configuration.");
        _ = config.ServerUri();
        return config;
    }
}
