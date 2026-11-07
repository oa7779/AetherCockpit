using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

/// <summary>
/// Speichert die einmalige Zustimmung zur Endnutzer-Lizenzvereinbarung lokal.
/// Ohne Zustimmung startet das Cockpit nicht. Rein lokal, keine Uebertragung.
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

    public const string Text = """
        ENDNUTZER-LIZENZVEREINBARUNG (EULA) - AETHER System Control

        1. Herausgeber und Kostenfreiheit
        AETHER System Control ("die Software") ist ein kostenloses, privat
        entwickeltes Windows-Werkzeug von Olaf Weins. Es wird unveraendert und
        ohne Gebuehr zur Verfuegung gestellt.

        2. Nutzung auf eigenes Risiko / kein Gewaehrleistungsausschluss-Umgehung
        Die Software liest Systemzustaende und kann auf ausdrueckliche Bestaetigung
        Windows-eigene Wartungs- und Verwaltungsaktionen ausloesen. Du nutzt die
        Software auf eigene Verantwortung. Es wird KEINE Gewaehrleistung
        uebernommen - weder fuer Fehlerfreiheit noch fuer Eignung fuer einen
        bestimmten Zweck. Soweit gesetzlich zulaessig, ist jede Haftung fuer
        mittelbare oder Folgeschaeden ausgeschlossen. Zwingende gesetzliche
        Rechte (z.B. bei Vorsatz/grober Fahrlaessigkeit) bleiben unberuehrt.

        3. Administratorrechte
        Die Software benoetigt Administratorrechte, um Hardware-Sensoren und
        Systeminformationen zu lesen. Erteile diese nur, wenn du der Quelle
        vertraust. Vor Reparaturen einen Wiederherstellungspunkt anlegen.

        4. Keine automatischen Aenderungen / lokale Verarbeitung
        Ohne deine ausdrueckliche Bestaetigung nimmt die Software keine
        veraendernden Aktionen vor. Es findet keine Cloud-Uebertragung und kein
        Tracking statt. Einstellungen, Protokolle und das Rechner-Inventar
        bleiben lokal unter %LOCALAPPDATA%\AetherCockpit.

        5. Datenschutz des Inventars
        Das beim ersten Start erstellte Rechner-Inventar (Hardware und
        installierte Software) wird nur lokal angezeigt/gespeichert. Es kann
        Rechnernamen und Seriennummern enthalten; teile es nur bewusst.

        6. Fremdkomponenten
        Die Software nutzt Fremdkomponenten unter deren eigenen Lizenzen,
        u.a. LibreHardwareMonitor (MPL 2.0) und smartmontools (GPL v2).
        Deren Lizenzbedingungen gelten fuer die jeweiligen Komponenten fort.

        7. Optionale Desktop- und Netzwerkfunktionen
        Desktop-Gadgets und Tray koennen nach Ausblenden des Hauptfensters
        weiterlaufen; vollstaendiges Beenden ueber das Symbol bei der Uhr.
        Eine erhoehte Autostart-Aufgabe und automatische Paketmitschnitte
        werden nur nach gesonderter ausdruecklicher Freigabe eingerichtet.
        Npcap wird nicht mitgeliefert; Installation und Lizenz separat pruefen.
        Mitschnitte koennen trotz 128-Byte-Grenze vertrauliche Daten enthalten.
        Sie bleiben lokal, sind auf 100 MiB/15 Minuten begrenzt und werden
        nach sieben Tagen beim naechsten Aufbewahrungslauf entfernt.
        Netzwerkalarme sind regelbasierte Indizien, keine Angriffsnachweise
        oder Zusage einer vollstaendigen Sicherheitsueberwachung.

        8. Zustimmung
        Durch Akzeptieren bestaetigst du, diese Bedingungen gelesen zu haben
        und mit ihnen einverstanden zu sein. Andernfalls wird die Software
        beendet und nicht weiter ausgefuehrt.
        """;
}
