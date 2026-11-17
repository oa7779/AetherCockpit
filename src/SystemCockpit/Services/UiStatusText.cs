using AetherOperations;

namespace SystemCockpit.Services;

internal static class UiStatusText
{
    internal static string Availability(DataAvailability value) => value switch
    {
        DataAvailability.Available => "Verfügbar",
        DataAvailability.Unavailable => "Nicht verfügbar",
        DataAvailability.AccessDenied => "Zugriff verweigert",
        DataAvailability.Unsupported => "Nicht unterstützt",
        DataAvailability.ProviderError => "Fehler der Datenquelle",
        DataAvailability.Stale => "Veraltet",
        _ => "Unbekannt"
    };

    internal static string Source(SourceType value) => value switch
    {
        SourceType.Direct => "Direkt ermittelt",
        SourceType.Derived => "Berechnet",
        SourceType.Database => "Datenbank",
        _ => "Nicht bestätigt"
    };

    internal static string Confidence(DataConfidence value) => value switch
    {
        DataConfidence.ProviderReported => "Von Datenquelle gemeldet",
        DataConfidence.Corroborated => "Durch weitere Quelle bestätigt",
        DataConfidence.Unconfirmed => "Nicht bestätigt",
        _ => "Unbekannt"
    };

    internal static string Classification(DataClassification value) => value switch
    {
        DataClassification.Normal => "Normal",
        DataClassification.Sensitive => "Sensibel",
        _ => "Geheim"
    };

    internal static string YesNo(bool value) => value ? "Ja" : "Nein";

    internal static Check Protection(Check item)
    {
        static string Active(string value) => value.Trim() switch
        {
            "True" or "On" => "Aktiv",
            "False" or "Off" => "Deaktiviert",
            _ => value.Trim()
        };
        var value = item.Value;
        if (item.Name == "Defender Echtzeitschutz")
        {
            var parts = value.Split(" | ");
            if (parts.Length == 2 && parts[0].StartsWith("AV: ", StringComparison.Ordinal) &&
                parts[1].StartsWith("Echtzeit: ", StringComparison.Ordinal))
                value = $"Virenschutz: {Active(parts[0][4..])} | Echtzeitschutz: {Active(parts[1][10..])}";
        }
        else if (item.Name == "Windows Firewall")
            value = string.Join(" | ", value.Split(" | ").Select(part =>
            {
                var separator = part.IndexOf(": ", StringComparison.Ordinal);
                if (separator < 0) return part;
                var profile = part[..separator] switch
                {
                    "Domain" => "Domänenprofil", "Private" => "Privates Profil", "Public" => "Öffentliches Profil",
                    _ => part[..separator]
                };
                return profile + ": " + Active(part[(separator + 2)..]);
            }));
        else if (item.Name == "Secure Boot") value = Active(value);
        else if (item.Name == "Neustart-Indikatoren")
            value = value switch { "True" => "Neustart erforderlich", "False" => "Kein Neustartindikator gesetzt", _ => value };
        else if (item.Name == "BitLocker Systemlaufwerk")
        {
            var parts = value.Split(" | Schutz: ");
            if (parts.Length == 2)
            {
                var encryption = parts[0] switch
                {
                    "FullyDecrypted" => "Nicht verschlüsselt", "FullyEncrypted" => "Vollständig verschlüsselt",
                    "EncryptionInProgress" => "Verschlüsselung läuft", "DecryptionInProgress" => "Entschlüsselung läuft",
                    "EncryptionPaused" => "Verschlüsselung pausiert", "DecryptionPaused" => "Entschlüsselung pausiert",
                    _ => parts[0]
                };
                value = $"Verschlüsselung: {encryption} | Schutz: {Active(parts[1])}";
            }
        }
        return item with { Value = value };
    }

    internal static Check Drive(Check item) => item with
    {
        Value = item.Value switch
        {
            "Healthy" => "Fehlerfrei", "Warning" => "Warnung", "Unhealthy" => "Fehlerhaft",
            "Unknown" => "Unbekannt", _ => item.Value
        }
    };
}
