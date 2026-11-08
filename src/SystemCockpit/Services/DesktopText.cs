using System.Globalization;
using System.Resources;

namespace SystemCockpit.Services;

internal static class DesktopText
{
    private static readonly ResourceManager Resources = new(
        "SystemCockpit.Resources.DesktopText", typeof(DesktopText).Assembly);
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    internal static IReadOnlyList<string> CatalogKeys { get; } =
    [
        "App.AdminRequired.Body", "App.AdminRequired.Title",
        "Desktop.Settings.Title", "Desktop.Settings.Header", "Desktop.Settings.Appearance",
        "Desktop.Settings.Copyright", "Desktop.Settings.BackgroundDescription",
        "Desktop.Settings.Skin", "Desktop.Settings.Language", "Desktop.Settings.RestartNote",
        "Desktop.Settings.RestartPrompt", "Desktop.Settings.RestartTitle", "Desktop.Settings.RestartYes",
        "Desktop.Settings.RestartNo", "Desktop.Settings.PreferenceSaved", "Desktop.Settings.SaveFailed",
        "Desktop.Settings.DisplayMode", "Desktop.Settings.Auto", "Desktop.Settings.Day", "Desktop.Settings.Night",
        "Desktop.Settings.Opacity", "Desktop.Settings.MainWindow", "Desktop.Settings.XBar",
        "Desktop.Settings.Gadgets", "Desktop.Settings.GadgetsEnabled", "Desktop.Settings.BackgroundDiagnostics",
        "Desktop.Settings.WidgetParameters", "Desktop.Settings.Cpu", "Desktop.Settings.Memory",
        "Desktop.Settings.Gpu", "Desktop.Settings.Temperatures", "Desktop.Settings.Network",
        "Desktop.Settings.Drives", "Desktop.Settings.GadgetHelp", "Desktop.Settings.NetworkButton",
        "Desktop.Settings.ChangesSaved",
        "Skin.Retro", "Skin.Vista", "Skin.Candy", "Skin.WindowsModern",
        "Skin.WindowsModernDescription", "Skin.ControlCenterHelp", "Language.German", "Language.English",
        "Action.Preview", "Action.Consent", "Action.Confirm", "Action.Cancel",
        "Board.Title", "Board.Refresh", "Board.InitialStatus", "Board.Description",
        "Board.SystemSchemaTitle", "Board.SystemSchemaDescription", "Board.NoDevices",
        "Board.SnapshotStatus", "Board.DeviceCount", "Board.ReadCancelled", "Board.ReadError",
        "Board.Category.cpu", "Board.Category.ram", "Board.Category.gpu", "Board.Category.monitor",
        "Board.Category.controller", "Board.Category.network", "Board.Category.usb",
        "Board.Category.storage", "Board.Category.sound", "Board.Category.other",
        "Navigation.SystemSchema",
        "Board.LabelStatus", "Board.LabelClass", "Board.LabelDevice", "Board.LabelParentId",
        "Board.MotionToggle", "Board.ZoomFit", "Board.Zoom75", "Board.Zoom100", "Board.Zoom125",
        "Board.ZoomTooltip", "Board.LiveMetricsNotice", "Board.Download", "Board.Upload", "Board.CpuTemperature",
        "Board.ActiveDeviceTooltip",
        "Board.DeviceDetailTitle",
        "Assistant.System.Name", "Assistant.System.Scope", "Assistant.System.NextSteps",
        "Assistant.Update.Name", "Assistant.Update.Scope", "Assistant.Update.NextSteps",
        "Assistant.Network.Name", "Assistant.Network.Scope", "Assistant.Network.NextSteps",
        "Assistant.Devices.Name", "Assistant.Devices.Scope", "Assistant.Devices.NextSteps",
        "Screensaver.ModernDisabled",
        "Tray.NotifyTip", "Tray.Open", "Tray.Settings", "Tray.Network", "Tray.GadgetsToggle",
        "Tray.GadgetsClose", "Tray.Exit", "Tray.GadgetsDisabled", "Tray.NetworkNotice", "Tray.DiagnosticsNotice",
        "Safety.RestartNow.Title", "Safety.RestartNow.Body", "Safety.RestartNow.Confirm",
        "Safety.RestartNow.Defer"
    ];

    internal static bool IsValidLanguage(string? language) => language is "de" or "en";

    internal static void ConfigureSupportedUi(string language)
    {
        if (!IsValidLanguage(language))
            throw new ArgumentException("Unsupported desktop language.", nameof(language));
        Configure("de");
    }

    internal static void Configure(string language)
    {
        if (!IsValidLanguage(language))
            throw new ArgumentException("Language must be 'de' or 'en'.", nameof(language));
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
    }

    internal static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en"
            ? English : CultureInfo.InvariantCulture;
        return Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)?.GetObject(key) as string
            ?? throw new InvalidOperationException($"Desktop resource '{key}' is missing for '{CultureInfo.CurrentUICulture.Name}'.");
    }

    internal static bool HasExplicitValue(string key, string language)
    {
        if (!IsValidLanguage(language)) return false;
        var culture = language == "en" ? English : CultureInfo.InvariantCulture;
        var resourceSet = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        return resourceSet?.GetObject(key, ignoreCase: false) is string value &&
            !string.IsNullOrWhiteSpace(value);
    }
}
