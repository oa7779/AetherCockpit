using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

/// <summary>
/// Speichert die einmalige Zustimmung zur Endnutzer-Lizenzvereinbarung lokal.
/// Ohne Zustimmung startet das Cockpit nicht; die Zustimmungsdatei wird nicht übertragen.
/// </summary>
public sealed class EulaConsent(string directory)
{
    public const string Version = "1.1";
    private string MarkerPath => Path.Combine(directory, "eula-consent.json");

    public static EulaConsent Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherCockpit"));

    public bool IsAccepted()
    {
        try
        {
            if (!File.Exists(MarkerPath)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(MarkerPath));
            return document.RootElement.TryGetProperty("version", out var version) &&
                version.GetString() == Version &&
                document.RootElement.TryGetProperty("accepted", out var accepted) && accepted.GetBoolean();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public void Accept()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(MarkerPath, JsonSerializer.Serialize(new
        {
            version = Version,
            accepted = true,
            acceptedAt = DateTimeOffset.Now
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string Text => DesktopUiText.Get("FirstRun.Eula");
}
