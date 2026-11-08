namespace SystemCockpit.Services;

internal static class DesktopScreensaverPolicy
{
    internal static bool IsEnabled(CockpitSettings settings) =>
        !SkinCatalog.IsModernSkin(settings.Skin);

    internal static bool LiveFeedEnabled => !SkinCatalog.IsModern;

    internal static bool IsCurrentUserEnabled() =>
        TryLoadCurrentUser(out var settings) && IsEnabled(settings);

    internal static bool TryLoadCurrentUser(out CockpitSettings settings)
    {
        CockpitSettings? loaded = null;
        ReadSafely(() => loaded = LoadCurrentUser());
        settings = loaded ?? new CockpitSettings();
        return loaded is not null;
    }

    private static CockpitSettings LoadCurrentUser()
    {
        try { System.IO.File.GetAttributes(CockpitSettings.PathName); }
        catch (System.IO.FileNotFoundException) { return new CockpitSettings(); }
        catch (System.IO.DirectoryNotFoundException) { return new CockpitSettings(); }
        return CockpitSettings.Load();
    }

    internal static bool ReadSafely(Func<CockpitSettings> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        try { return IsEnabled(read()); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or ArgumentException or System.Security.SecurityException)
        {
            System.Diagnostics.Trace.TraceError("AETHER screensaver settings unavailable; stopping: {0}", ex);
            return false;
        }
    }
}
