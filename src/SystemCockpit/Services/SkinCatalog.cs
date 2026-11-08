using System.Windows;
using System.IO;
using Microsoft.Win32;

namespace SystemCockpit.Services;

public sealed record SkinInfo(string Id, string Display, string? File);

/// <summary>
/// Skins stylen die Fensteroberflaeche (Hintergruende, Panels, Buttons, Leisten)
/// ueber die zentralen Theme-Brushes. Retro-Neon bleibt das Original und der Standard;
/// Windows Modern setzt eine eigene neutrale Control-Palette.
/// </summary>
public static class SkinCatalog
{
    public static IReadOnlyList<SkinInfo> All =>
    [
        new("retro-neon", DesktopText.Get("Skin.Retro"), null),
        new("vista-aero", DesktopText.Get("Skin.Vista"), "VistaAero.xaml"),
        new("candy-gloss", DesktopText.Get("Skin.Candy"), "CandyGloss.xaml"),
        new("windows-modern", DesktopText.Get("Skin.WindowsModern"), "WindowsModernDark.xaml")
    ];

    public static bool Valid(string id) => id == "fluent-26h2" || All.Any(s => s.Id == id);
    public static string CanonicalId(string id) => id == "fluent-26h2" ? "windows-modern" : id;
    public static bool IsModernSkin(string id) => CanonicalId(id) == "windows-modern";
    public static bool IsModern => IsModernSkin(_activeId);

    public static Uri Source(string id, bool? isLight = null)
    {
        id = CanonicalId(id);
        var skin = All.FirstOrDefault(s => s.Id == id) ?? throw new ArgumentException($"Unbekannter Skin: {id}");
        if (skin.File is null) throw new ArgumentException("Retro-Neon ist der eingebaute Standard ohne Override-Datei.");
        var file = id == "windows-modern"
            ? WindowsModernLight(isLight) ? "WindowsModernLight.xaml" : "WindowsModernDark.xaml"
            : skin.File;
        return new($"pack://application:,,,/SystemCockpit;component/Skins/{file}");
    }

    private static readonly List<ResourceDictionary> _active = [];
    private static string _activeId = "retro-neon";

    public static void Apply(Application application, string id, bool? isLight = null)
    {
        if (!Valid(id)) return;
        id = CanonicalId(id);
        var skin = All.First(s => s.Id == id);
        var next = new List<ResourceDictionary>();
        if (skin.File is not null)
        {
            next.Add(new ResourceDictionary { Source = Source(id, isLight) });
            if (IsModernSkin(id))
                next.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/SystemCockpit;component/Controls/WindowsModernControls.xaml")
                });
        }
        foreach (var dictionary in _active)
            application.Resources.MergedDictionaries.Remove(dictionary);
        _active.Clear();
        foreach (var dictionary in next)
        {
            application.Resources.MergedDictionaries.Add(dictionary);
            _active.Add(dictionary);
        }
        _activeId = id;
    }

    public static readonly string[] RequiredKeys =
        ["WindowBackdrop", "GlossPanel", "GlassRim", "GlossButton", "ChromeSidebar", "ButtonBorder", "ButtonBorderAlt"];

    public static bool WindowsModernLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool WindowsModernLight(bool? overrideValue) => overrideValue ?? WindowsModernLight();
}
