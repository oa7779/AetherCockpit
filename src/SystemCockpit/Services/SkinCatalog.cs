using System.Windows;

namespace SystemCockpit.Services;

public sealed record SkinInfo(string Id, string Display, string? File);

/// <summary>
/// Skins stylen die Fensteroberflaeche (Hintergruende, Panels, Buttons, Leisten)
/// ueber die zentralen Theme-Brushes. Retro-Neon ist das Original und der Standard;
/// die Neon-Akzentfarben der Inhalte bleiben in allen Skins erhalten (dunkle Varianten).
/// Ein Skin wird beim Programmstart angewendet.
/// </summary>
public static class SkinCatalog
{
    public static readonly IReadOnlyList<SkinInfo> All =
    [
        new("retro-neon", "Retro-Neon (Original)", null),
        new("vista-aero", "Vista Aero Glas (dunkel)", "VistaAero.xaml"),
        new("candy-gloss", "Candy Glossy (dunkel)", "CandyGloss.xaml"),
        new("fluent-26h2", "Windows 11 26H2 Fluent (dunkel)", "Fluent26H2.xaml")
    ];

    public static bool Valid(string id) => All.Any(s => s.Id == id);

    public static Uri Source(string id)
    {
        var skin = All.FirstOrDefault(s => s.Id == id) ?? throw new ArgumentException($"Unbekannter Skin: {id}");
        if (skin.File is null) throw new ArgumentException("Retro-Neon ist der eingebaute Standard ohne Override-Datei.");
        return new($"pack://application:,,,/SystemCockpit;component/Skins/{skin.File}");
    }

    // Die zuletzt gemergte Dictionary gewinnt bei gleichen Schluesseln: der Skin
    // ueberlagert die Retro-Defaults aus NeonGlass.xaml, ohne sie zu veraendern.
    // Verbraucher nutzen DynamicResource; ein Wechsel wirkt daher sofort auf
    // alle offenen Fenster. "retro-neon" entfernt nur die Ueberlagerung.
    private static ResourceDictionary? _active;

    public static void Apply(Application application, string id)
    {
        if (!Valid(id)) return;
        if (_active is not null)
        {
            application.Resources.MergedDictionaries.Remove(_active);
            _active = null;
        }
        var skin = All.First(s => s.Id == id);
        if (skin.File is null) return;
        _active = new ResourceDictionary { Source = Source(id) };
        application.Resources.MergedDictionaries.Add(_active);
    }

    public static readonly string[] RequiredKeys =
        ["WindowBackdrop", "GlossPanel", "GlassRim", "GlossButton", "ChromeSidebar", "ButtonBorder", "ButtonBorderAlt"];
}
