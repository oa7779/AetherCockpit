using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

internal sealed record SetupProfile(string Id, bool Gadgets, bool Cpu, bool Ram, bool Gpu,
    bool Temperatures, bool Network, bool Drives, bool CaptureAtLogon, string CaptureDevice)
{
    internal static string MarkerPath => Path.Combine(Path.GetDirectoryName(CockpitSettings.PathName)!, "setup-profile.id");
    internal static void ApplyInstalledProfile() => ApplyInstalledProfile(
        Path.Combine(AppContext.BaseDirectory, "desktop-defaults.json"), CockpitSettings.PathName, MarkerPath);

    internal static void ApplyInstalledProfile(string path, string settingsPath, string markerPath)
    {
        if (!File.Exists(path)) return;
        var profile = JsonSerializer.Deserialize<SetupProfile>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Leeres Desktop-Setup-Profil.");
        if (string.IsNullOrWhiteSpace(profile.Id) || profile.Id.Length > 100)
            throw new InvalidDataException("Ungueltige Setup-Profil-ID.");
        if (File.Exists(markerPath) && File.ReadAllText(markerPath) == profile.Id) return;
        var settings = Merge(CockpitSettings.Load(settingsPath), profile);
        settings.Save(settingsPath);
        File.WriteAllText(markerPath, profile.Id);
    }

    internal static CockpitSettings Merge(CockpitSettings settings, SetupProfile profile)
    {
        var result = settings with
        {
            GadgetsEnabled = profile.Gadgets, SidebarWidgetCpu = profile.Cpu, SidebarWidgetMemory = profile.Ram,
            SidebarWidgetGpu = profile.Gpu, SidebarWidgetTemperatures = profile.Temperatures,
            SidebarWidgetNetwork = profile.Network, SidebarWidgetDrives = profile.Drives,
            CaptureAtLogon = profile.CaptureAtLogon, CaptureDevice = profile.CaptureDevice
        };
        result.Validate();
        return result;
    }
}
