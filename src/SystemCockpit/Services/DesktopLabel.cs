using System.Collections;
using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace SystemCockpit.Services;

/// <summary>Explicit authored desktop resources; values never translate device or command output.</summary>
public static class DesktopUiText
{
    private static readonly ResourceManager Resources = new(
        "SystemCockpit.Resources.DesktopUiText", typeof(DesktopUiText).Assembly);

    public static IReadOnlyList<string> Keys => KeysForLanguage("de");

    public static IReadOnlyList<string> KeysForLanguage(string language) => Resources.GetResourceSet(
        ResourceCulture(language), true, false)!.Cast<DictionaryEntry>()
        .Select(entry => (string)entry.Key).Order(StringComparer.Ordinal).ToArray();

    public static string Get(string key)
    {
        var culture = ResourceCulture(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "de");
        return Resources.GetResourceSet(culture, true, false)?.GetObject(key) as string
            ?? throw new MissingManifestResourceException($"Missing authored desktop resource '{key}' for '{culture.Name}'.");
    }

    public static string Format(string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), values);

    public static bool HasExplicitValue(string key, string language) =>
        Resources.GetResourceSet(ResourceCulture(language), true, false)?.GetObject(key) is string value &&
        !string.IsNullOrWhiteSpace(value);

    private static CultureInfo ResourceCulture(string language) => language switch
    {
        "de" => CultureInfo.InvariantCulture,
        "en" => CultureInfo.GetCultureInfo("en"),
        _ => throw new ArgumentException("Unsupported desktop resource language.", nameof(language))
    };
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class DesktopLabelExtension : MarkupExtension
{
    public DesktopLabelExtension() { }
    public DesktopLabelExtension(string key) => Key = key;
    [ConstructorArgument("key")]
    public string Key { get; set; } = "";
    public string? ModernKey { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        DesktopUiText.Get(SkinCatalog.IsModern && ModernKey is not null ? ModernKey : Key);
}
