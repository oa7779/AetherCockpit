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
        Das Cockpit benötigt Administratorrechte für Hardwarezugriff und
        Systemverwaltung. Erteile sie nur bei vertrauenswürdiger Herkunft.
        Vor Reparaturen wichtige Daten sichern; ein Wiederherstellungspunkt
        ersetzt kein Backup.

        4. Freigaben und Datenverarbeitung
        Geführte Änderungen benötigen deine Bestätigung. Die freie
        Administratorkonsole führt eingegebene Befehle mit Enter direkt aus.
        Einstellungen und lokale Berichte liegen unter
        %LOCALAPPDATA%\AetherCockpit des ausführenden Kontos.
        AETHER hat keine allgemeine App-Telemetrie. Im Einzelplatzmodus
        erfolgt kein Kontakt zum Verwaltungsserver. Optionaler Clientbetrieb
        überträgt Diagnoseberichte erst nach Anmeldung, manueller Freigabe
        und getrenntem Übertragungs-Opt-in. Netzwerkwerkzeuge, Updates und
        aufgerufene Programme können ihre jeweiligen Ziele kontaktieren.

        5. Diagnose und Datenschutz
        Das Erstinventar wird lokal angezeigt und kann als Text gespeichert
        werden. Inventare, Ereignisse, Backups und Mitschnitte können Namen,
        Geräte-IDs und private Pfade enthalten. Vor Weitergabe prüfen.
        Die Musterschwärzung des Text-Exports gilt nicht automatisch für
        andere Berichte oder die optionale Serverübertragung.

        6. Fremdkomponenten
        Die Software nutzt LibreHardwareMonitor (MPL 2.0), smartmontools
        (GPL v2) und PresentMon (MIT) unter deren eigenen Bedingungen.
        Die mitgelieferten Lizenz- und Herkunftshinweise gelten weiter.

        7. Desktop, Mitschnitt und Deinstallation
        Gadgets und aktivierte Hintergrunddiagnose laufen nach Ausblenden
        des Hauptfensters weiter. Vollständig beenden über das Tray.
        Anmeldestart und automatischer Mitschnitt benötigen eigene Freigaben.
        Npcap wird separat installiert und lizenziert, nicht mitgeliefert.
        Mitschnitte bleiben lokal und sind auf 128 Bytes je Paket,
        100.000.000 Bytes (100 MB) je Datei und 15 Minuten begrenzt.
        Eigene Captures werden nach sieben Tagen beim nächsten
        Aufbewahrungslauf entfernt. Sie können vertrauliche Daten enthalten.
        Netzwerkalarme sind Indizien, keine Angriffsnachweise.
        Der aktuelle Uninstaller entfernt nach Bestätigung Installation
        und AETHER-Daten des ausführenden Kontos. Benötigte Daten vorher
        außerhalb dieser Ordner sichern; externe Dateien bleiben erhalten.

        8. Zustimmung
        Mit Akzeptieren bestätigst du, die Bedingungen gelesen zu haben
        und mit ihnen einverstanden zu sein. Andernfalls endet die Software.
        """;
}
