using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

public sealed record CockpitSettings
{
    public double CpuWarningC { get; init; } = 90;
    public double GpuWarningC { get; init; } = 85;
    public double StorageWarningC { get; init; } = 70;
    public double FreeSpaceWarningPercent { get; init; } = 10;
    public double VoltageTolerancePercent { get; init; } = 5;
    public double CmosMinimumV { get; init; } = 2.5;
    public int RecordIntervalSeconds { get; init; } = 5;
    public string SmartctlPath { get; init; } = "";
    public string PresentMonPath { get; init; } = "";
    public string OuiCsvPath { get; init; } = "";
    public bool ManagerClientMode { get; init; }
    public string ManagerServerUrl { get; init; } = "";
    public bool ManagerReportingEnabled { get; init; }
    // Einsteiger (false): nur lesende Katalogbefehle, freie Konsole gesperrt. Experte (true): voller Umfang.
    public bool ExpertMode { get; init; } = true;
    // Skin der Fensteroberflaeche; wird beim Programmstart angewendet. Retro-Neon ist das Original.
    public string Skin { get; init; } = "retro-neon";
    public string Language { get; init; } = "de";
    public string DisplayMode { get; init; } = "auto";
    public string OverlayMonitorDeviceName { get; init; } = "";
    public string OverlayDockSide { get; init; } = "right";
    public double OverlayVerticalOffset { get; init; } = 0.02;
    public string SidebarMonitorDeviceName { get; init; } = "";
    public string SidebarDockSide { get; init; } = "right";
    public double SidebarVerticalOffset { get; init; } = 0.02;
    public double SidebarHorizontalOffset { get; init; } = 0.95;
    public bool SidebarPinned { get; init; } = true;
    public bool SidebarAutoHide { get; init; }
    public bool SidebarWidgetCpu { get; init; } = true;
    public bool SidebarWidgetMemory { get; init; } = true;
    public bool SidebarWidgetGpu { get; init; } = true;
    public bool SidebarWidgetTemperatures { get; init; } = true;
    public bool SidebarWidgetNetwork { get; init; } = true;
    public bool SidebarWidgetDrives { get; init; } = true;
    public List<SidebarShortcut> SidebarShortcuts { get; init; } = [];
    public double AppOpacity { get; init; } = 1;
    public double SidebarOpacity { get; init; } = 1;
    public double GadgetOpacity { get; init; } = 0.9;
    public bool GadgetsEnabled { get; init; }
    public List<GadgetLayout> GadgetLayouts { get; init; } = [];
    public bool BackgroundDiagnosticsEnabled { get; init; }
    public bool NetworkMonitorEnabled { get; init; }
    public bool CaptureAtLogon { get; init; }
    public string CaptureDevice { get; init; } = "";
    public int ConnectionAlertThreshold { get; init; } = 100;
    public int SynPortAlertThreshold { get; init; } = 30;
    public int DnsFailureAlertThreshold { get; init; } = 30;
    public void Validate()
    {
        if (DisplayMode is not ("auto" or "day" or "night"))
            throw new ArgumentException(DesktopUiText.Get("Settings.DisplayMode"));
        static void Range(double value, double min, double max, string name)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                throw new ArgumentException(DesktopUiText.Format("Settings.Range", DesktopUiText.Get(name), min, max));
        }
        Range(CpuWarningC, 40, 115, "Settings.CpuWarning");
        Range(GpuWarningC, 40, 110, "Settings.GpuWarning");
        Range(StorageWarningC, 30, 100, "Settings.StorageWarning");
        Range(FreeSpaceWarningPercent, 1, 50, "Settings.FreeSpace");
        Range(VoltageTolerancePercent, 1, 15, "Settings.VoltageTolerance");
        Range(CmosMinimumV, 1, 3.3, "Settings.CmosMinimum");
        Range(AppOpacity, 0.2, 1, "Settings.AppOpacity");
        Range(SidebarOpacity, 0.2, 1, "Settings.SidebarOpacity");
        Range(GadgetOpacity, 0.2, 1, "Settings.GadgetOpacity");
        if (GadgetLayouts is null || GadgetLayouts.Count > 100 ||
            GadgetLayouts.Any(layout => layout is null || string.IsNullOrWhiteSpace(layout.Id) ||
                layout.Id.Length > 256 || !double.IsFinite(layout.X) || !double.IsFinite(layout.Y) ||
                !double.IsFinite(layout.Opacity) || layout.Opacity is < 0.2 or > 1) ||
            GadgetLayouts.Select(layout => layout.Id).Distinct(StringComparer.Ordinal).Count() != GadgetLayouts.Count)
            throw new ArgumentException(DesktopUiText.Get("Settings.GadgetLayout"));
        if (CaptureDevice is null || CaptureDevice.Length > 512 || CaptureDevice.Any(char.IsControl))
            throw new ArgumentException(DesktopUiText.Get("Settings.CaptureDevice"));
        if (ConnectionAlertThreshold is < 5 or > 10000 || SynPortAlertThreshold is < 5 or > 10000 ||
            DnsFailureAlertThreshold is < 5 or > 10000)
            throw new ArgumentException(DesktopUiText.Get("Settings.NetworkThresholds"));
        if (CaptureAtLogon && string.IsNullOrWhiteSpace(CaptureDevice))
            throw new ArgumentException(DesktopUiText.Get("Settings.CaptureInterface"));
        if (RecordIntervalSeconds is < 2 or > 60) throw new ArgumentException(DesktopUiText.Get("Settings.RecordInterval"));
        if (!SkinCatalog.Valid(Skin)) throw new ArgumentException(DesktopUiText.Format("Settings.UnknownSkin",
            string.Join(", ", SkinCatalog.All.Select(s => s.Id))));
        if (!DesktopText.IsValidLanguage(Language))
            throw new ArgumentException(DesktopUiText.Get("Settings.Language"));
        if (OverlayMonitorDeviceName is null || OverlayMonitorDeviceName.Length > 256)
            throw new ArgumentException(DesktopUiText.Get("Settings.OverlayMonitor"));
        if (OverlayDockSide is not ("left" or "right"))
            throw new ArgumentException(DesktopUiText.Get("Settings.OverlayDock"));
        if (!double.IsFinite(OverlayVerticalOffset) || OverlayVerticalOffset is < 0 or > 1)
            throw new ArgumentException(DesktopUiText.Get("Settings.OverlayPosition"));
        if (SidebarMonitorDeviceName is null || SidebarMonitorDeviceName.Length > 256)
            throw new ArgumentException(DesktopUiText.Get("Settings.SidebarMonitor"));
        if (SidebarDockSide is not ("left" or "right"))
            throw new ArgumentException(DesktopUiText.Get("Settings.SidebarDock"));
        if (!double.IsFinite(SidebarVerticalOffset) || SidebarVerticalOffset is < 0 or > 1)
            throw new ArgumentException(DesktopUiText.Get("Settings.SidebarPosition"));
        if (!double.IsFinite(SidebarHorizontalOffset) || SidebarHorizontalOffset is < 0 or > 1)
            throw new ArgumentException(DesktopUiText.Get("Settings.SidebarHorizontal"));
        if (SidebarShortcuts is null || SidebarShortcuts.Count > 100)
            throw new ArgumentException(DesktopUiText.Get("Settings.ShortcutLimit"));
        if (ManagerServerUrl is null || ManagerServerUrl.Length > 2048)
            throw new ArgumentException(DesktopUiText.Get("Settings.ServerUrlLength"));
        if (!string.IsNullOrWhiteSpace(ManagerServerUrl))
        {
            if (!Uri.TryCreate(ManagerServerUrl, UriKind.Absolute, out var managerUri) ||
                managerUri.Scheme is not ("http" or "https") || managerUri.UserInfo.Length > 0 ||
                managerUri.Query.Length > 0 || managerUri.Fragment.Length > 0 ||
                managerUri.AbsolutePath != "/" ||
                (!managerUri.IsLoopback && managerUri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException(DesktopUiText.Get("Settings.ServerUrl"));
        }
        else if (ManagerReportingEnabled)
        {
            throw new ArgumentException(DesktopUiText.Get("Settings.ServerRequired"));
        }
        if (ManagerReportingEnabled && !ManagerClientMode)
            throw new ArgumentException(DesktopUiText.Get("Settings.ClientRequired"));
        foreach (var shortcut in SidebarShortcuts)
        {
            if (shortcut is null || string.IsNullOrWhiteSpace(shortcut.Name) || shortcut.Name.Length > 80 ||
                string.IsNullOrWhiteSpace(shortcut.Path) || shortcut.Path.Length > 1024 ||
                shortcut.Category is not ("App" or "Datei" or "Ordner") ||
                !Path.IsPathFullyQualified(shortcut.Path))
                throw new ArgumentException(DesktopUiText.Get("Settings.Shortcut"));
        }
        foreach (var (name, path) in new[] { ("smartctl", SmartctlPath), ("PresentMon", PresentMonPath) })
            if (path is null || (path.Length > 0 && (!Path.IsPathFullyQualified(path) ||
                path.Any(char.IsControl) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
                !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException(DesktopUiText.Format("Settings.ToolPath", name));
        if (OuiCsvPath is null || (OuiCsvPath.Length > 0 && (!Path.IsPathFullyQualified(OuiCsvPath) ||
            OuiCsvPath.Any(char.IsControl) || OuiCsvPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)))
            throw new ArgumentException(DesktopUiText.Get("Settings.OuiPath"));
    }
    public IReadOnlyList<string> OptionalFileWarnings() =>
        new[] { (Name: "smartctl", Path: SmartctlPath), (Name: "PresentMon", Path: PresentMonPath),
            (Name: DesktopUiText.Get("Settings.OuiName"), Path: OuiCsvPath) }
            .Where(item => !string.IsNullOrEmpty(item.Path) && !File.Exists(item.Path))
            .Select(item => DesktopUiText.Format("Settings.FileWarning", item.Name, item.Path))
            .ToArray();
    public static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AetherCockpit", "settings.json");
    public static CockpitSettings Load() => Load(PathName);
    internal static CockpitSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        var settings = Upgrade(JsonSerializer.Deserialize<CockpitSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException(DesktopUiText.Get("Settings.EmptyFile")));
        settings.Validate();
        foreach (var warning in settings.OptionalFileWarnings())
            System.Diagnostics.Trace.TraceWarning("AETHER Einstellungen: {0}", warning);
        return settings;
    }
    internal static CockpitSettings Upgrade(CockpitSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Language == "en")
            settings = settings with { Language = "de" };
        if (settings.ManagerReportingEnabled && !settings.ManagerClientMode)
            settings = settings with { ManagerClientMode = true };
        if (settings.Skin != SkinCatalog.CanonicalId(settings.Skin))
            settings = settings with { Skin = SkinCatalog.CanonicalId(settings.Skin) };
        return settings;
    }
    public void Save() => Save(PathName);
    internal void Save(string path)
    {
        Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }
}

public static class SensorDiagnostics
{
    public static List<Check> Evaluate(IEnumerable<SensorReading> readings, CockpitSettings settings)
    {
        var checks = new List<Check>();
        var sensors = readings.ToList();
        var hasThrottleIndicator = false;
        foreach (var sensor in sensors.Where(s => s.Type is "Voltage" or "Factor"))
        {
            if (sensor.Type == "Factor")
            {
                if (!sensor.Name.Contains("Thermal", StringComparison.OrdinalIgnoreCase) &&
                    !sensor.Name.Contains("Throttl", StringComparison.OrdinalIgnoreCase)) continue;
                hasThrottleIndicator = true;
                checks.Add(new(DesktopUiText.Format("Sensor.ThrottleName", sensor.Hardware), sensor.Display,
                    sensor.Value is null ? "unknown" : sensor.Value > 0 ? "warning" : "ok",
                    DesktopUiText.Format("Sensor.ThrottleEvidence", sensor.Name)));
                continue;
            }
            double? nominal = sensor.Name.Trim().ToUpperInvariant() switch
            {
                "+12V" or "12V" or "+12 V" or "12 V" => 12,
                "+5V" or "5V" or "+5 V" or "5 V" => 5,
                "+3.3V" or "3.3V" or "+3.3 V" or "3.3 V" => 3.3,
                _ => null
            };
            var cmos = sensor.Name.Contains("VBat", StringComparison.OrdinalIgnoreCase) ||
                sensor.Name.Contains("CMOS", StringComparison.OrdinalIgnoreCase);
            var warning = sensor.Value is { } v && (cmos ? v < settings.CmosMinimumV
                : nominal is { } n && Math.Abs(v - n) / n * 100 > settings.VoltageTolerancePercent);
            checks.Add(new(DesktopUiText.Format("Sensor.VoltageName", sensor.Name), sensor.Display,
                sensor.Value is null ? "unknown" : warning ? "warning" : nominal is not null || cmos ? "ok" : "info",
                cmos ? DesktopUiText.Format("Sensor.CmosMinimum", settings.CmosMinimumV) : nominal is not null
                    ? DesktopUiText.Format("Sensor.AtxTolerance", settings.VoltageTolerancePercent)
                    : DesktopUiText.Get("Sensor.NoVoltageProfile")));
        }
        foreach (var sensor in sensors.Where(s => s.Type == "Temperature" &&
                     SensorSemantics.IsStorage(s.Id) && !SensorSemantics.IsTemperatureLimit(s)))
            checks.Add(new(DesktopUiText.Format("Sensor.StorageTemperature", sensor.Hardware, sensor.Name), sensor.Display,
                sensor.Value is null ? "unknown" : sensor.Value >= settings.StorageWarningC ? "warning" : "ok",
                DesktopUiText.Format("Sensor.StorageEvidence", sensor.Name, settings.StorageWarningC)));
        if (!hasThrottleIndicator)
            checks.Add(new(DesktopUiText.Get("Sensor.Throttle"), DesktopUiText.Get("Sensor.NotReported"), "unknown",
                DesktopUiText.Get("Sensor.NoThrottle")));
        return checks;
    }
}
