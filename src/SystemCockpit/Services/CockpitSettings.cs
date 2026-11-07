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
            throw new ArgumentException("Darstellungsmodus muss auto, day oder night sein.");
        static void Range(double value, double min, double max, string name)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                throw new ArgumentException($"{name}: Bereich {min} bis {max} erforderlich.");
        }
        Range(CpuWarningC, 40, 115, "CPU-Warnung");
        Range(GpuWarningC, 40, 110, "GPU-Warnung");
        Range(StorageWarningC, 30, 100, "Storage-Warnung");
        Range(FreeSpaceWarningPercent, 1, 50, "Freier Speicher");
        Range(VoltageTolerancePercent, 1, 15, "ATX-Toleranz");
        Range(CmosMinimumV, 1, 3.3, "CMOS-Minimum");
        Range(AppOpacity, 0.2, 1, "App-Deckkraft");
        Range(SidebarOpacity, 0.2, 1, "Leisten-Deckkraft");
        Range(GadgetOpacity, 0.2, 1, "Gadget-Deckkraft");
        if (GadgetLayouts is null || GadgetLayouts.Count > 100 ||
            GadgetLayouts.Any(layout => layout is null || string.IsNullOrWhiteSpace(layout.Id) ||
                layout.Id.Length > 256 || !double.IsFinite(layout.X) || !double.IsFinite(layout.Y) ||
                !double.IsFinite(layout.Opacity) || layout.Opacity is < 0.2 or > 1) ||
            GadgetLayouts.Select(layout => layout.Id).Distinct(StringComparer.Ordinal).Count() != GadgetLayouts.Count)
            throw new ArgumentException("Ungueltige Gadget-Position oder Deckkraft.");
        if (CaptureDevice is null || CaptureDevice.Length > 512 || CaptureDevice.Any(char.IsControl))
            throw new ArgumentException("Ungueltiges Capture-Geraet.");
        if (ConnectionAlertThreshold is < 5 or > 10000 || SynPortAlertThreshold is < 5 or > 10000 ||
            DnsFailureAlertThreshold is < 5 or > 10000)
            throw new ArgumentException("Netzwerk-Alarmschwellen: 5 bis 10000 erforderlich.");
        if (CaptureAtLogon && string.IsNullOrWhiteSpace(CaptureDevice))
            throw new ArgumentException("Automatischer Mitschnitt erfordert eine ausgewaehlte Npcap-Schnittstelle.");
        if (RecordIntervalSeconds is < 2 or > 60) throw new ArgumentException("Aufzeichnung: 2 bis 60 Sekunden erforderlich.");
        if (!SkinCatalog.Valid(Skin)) throw new ArgumentException("Unbekannter Skin. Verfuegbar: " +
            string.Join(", ", SkinCatalog.All.Select(s => s.Id)));
        if (OverlayMonitorDeviceName.Length > 256)
            throw new ArgumentException("Overlay-Monitorname ist zu lang.");
        if (OverlayDockSide is not ("left" or "right"))
            throw new ArgumentException("Overlay-Andockseite muss 'left' oder 'right' sein.");
        if (!double.IsFinite(OverlayVerticalOffset) || OverlayVerticalOffset is < 0 or > 1)
            throw new ArgumentException("Overlay-Position muss zwischen 0 und 1 liegen.");
        if (SidebarMonitorDeviceName.Length > 256)
            throw new ArgumentException("Seitenleisten-Monitorname ist zu lang.");
        if (SidebarDockSide is not ("left" or "right"))
            throw new ArgumentException("Seitenleisten-Andockseite muss 'left' oder 'right' sein.");
        if (!double.IsFinite(SidebarVerticalOffset) || SidebarVerticalOffset is < 0 or > 1)
            throw new ArgumentException("Seitenleisten-Position muss zwischen 0 und 1 liegen.");
        if (!double.IsFinite(SidebarHorizontalOffset) || SidebarHorizontalOffset is < 0 or > 1)
            throw new ArgumentException("Horizontale Seitenleisten-Position muss zwischen 0 und 1 liegen.");
        if (SidebarShortcuts is null || SidebarShortcuts.Count > 100)
            throw new ArgumentException("Maximal 100 eigene Seitenleisten-Verknuepfungen sind erlaubt.");
        if (ManagerServerUrl is null || ManagerServerUrl.Length > 2048)
            throw new ArgumentException("Die URL der zentralen Verwaltung ist zu lang.");
        if (!string.IsNullOrWhiteSpace(ManagerServerUrl))
        {
            if (!Uri.TryCreate(ManagerServerUrl, UriKind.Absolute, out var managerUri) ||
                managerUri.Scheme is not ("http" or "https") || managerUri.UserInfo.Length > 0 ||
                managerUri.Query.Length > 0 || managerUri.Fragment.Length > 0 ||
                managerUri.AbsolutePath != "/" ||
                (!managerUri.IsLoopback && managerUri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Zentrale Verwaltung: HTTPS fuer Netzwerkziele; HTTP nur fuer localhost. URL ohne Pfad, Zugangsdaten, Query oder Fragment eingeben.");
        }
        else if (ManagerReportingEnabled)
        {
            throw new ArgumentException("Fuer zentrale Berichte zuerst die Server-URL speichern.");
        }
        if (ManagerReportingEnabled && !ManagerClientMode)
            throw new ArgumentException("Zentrale Berichte sind nur im Clientmodus verfuegbar.");
        foreach (var shortcut in SidebarShortcuts)
        {
            if (shortcut is null || string.IsNullOrWhiteSpace(shortcut.Name) || shortcut.Name.Length > 80 ||
                string.IsNullOrWhiteSpace(shortcut.Path) || shortcut.Path.Length > 1024 ||
                shortcut.Category is not ("App" or "Datei" or "Ordner") ||
                !Path.IsPathFullyQualified(shortcut.Path))
                throw new ArgumentException("Eine eigene Seitenleisten-Verknuepfung ist ungueltig.");
        }
        foreach (var (name, path) in new[] { ("smartctl", SmartctlPath), ("PresentMon", PresentMonPath) })
            if (path is null || (path.Length > 0 && (!Path.IsPathFullyQualified(path) ||
                path.Any(char.IsControl) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
                !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException($"{name}: Werkzeugpfad muss ein absoluter EXE-Pfad sein.");
        if (OuiCsvPath is null || (OuiCsvPath.Length > 0 && (!Path.IsPathFullyQualified(OuiCsvPath) ||
            OuiCsvPath.Any(char.IsControl) || OuiCsvPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)))
            throw new ArgumentException("OUI-Datenbank muss ein absoluter Dateipfad sein.");
    }
    public IReadOnlyList<string> OptionalFileWarnings() =>
        new[] { (Name: "smartctl", Path: SmartctlPath), (Name: "PresentMon", Path: PresentMonPath),
            (Name: "OUI-Datenbank", Path: OuiCsvPath) }
            .Where(item => !string.IsNullOrEmpty(item.Path) && !File.Exists(item.Path))
            .Select(item => $"{item.Name} nicht verfuegbar: {item.Path}. " +
                "Gespeicherten Pfad unter Systemkontrolle -> Einstellungen neu auswaehlen oder leeren; keine automatische Ersetzung.")
            .ToArray();
    public static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AetherCockpit", "settings.json");
    public static CockpitSettings Load() => Load(PathName);
    internal static CockpitSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<CockpitSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Leere Einstellungsdatei.");
        if (settings.ManagerReportingEnabled && !settings.ManagerClientMode)
            settings = settings with { ManagerClientMode = true };
        settings.Validate();
        foreach (var warning in settings.OptionalFileWarnings())
            System.Diagnostics.Trace.TraceWarning("AETHER Einstellungen: {0}", warning);
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
        foreach (var sensor in sensors.Where(s => s.Type is "Voltage" or "Factor"))
        {
            if (sensor.Type == "Factor")
            {
                if (!sensor.Name.Contains("Thermal", StringComparison.OrdinalIgnoreCase) &&
                    !sensor.Name.Contains("Throttl", StringComparison.OrdinalIgnoreCase)) continue;
                checks.Add(new($"Drosselungsindikator / {sensor.Hardware}", sensor.Display,
                    sensor.Value is null ? "unknown" : sensor.Value > 0 ? "warning" : "ok",
                    $"Expliziter Sensor: {sensor.Name}; keine Ableitung allein aus Temperatur."));
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
            checks.Add(new($"Spannung / {sensor.Name}", sensor.Display,
                sensor.Value is null ? "unknown" : warning ? "warning" : nominal is not null || cmos ? "ok" : "info",
                cmos ? $"CMOS Minimum {settings.CmosMinimumV} V" : nominal is not null
                    ? $"ATX-Toleranz +/- {settings.VoltageTolerancePercent}%"
                    : "Kein pauschaler Sollwert fuer VCore/DRAM. Keine Bewertung ohne Hardwareprofil."));
        }
        foreach (var sensor in sensors.Where(s => s.Type == "Temperature" &&
                     SensorSemantics.IsStorage(s.Id) && !SensorSemantics.IsTemperatureLimit(s)))
            checks.Add(new($"Storage Temperatur / {sensor.Hardware} / {sensor.Name}", sensor.Display,
                sensor.Value is null ? "unknown" : sensor.Value >= settings.StorageWarningC ? "warning" : "ok",
                $"Aktueller Messkanal: {sensor.Name} | Quelle: LibreHardwareMonitor / SMART | " +
                $"AETHER-Warnschwelle {settings.StorageWarningC} C; keine Lebensdauerprognose."));
        if (!checks.Any(c => c.Name.StartsWith("Drosselungsindikator")))
            checks.Add(new("Drosselungsindikator", "Nicht gemeldet", "unknown", "Kein expliziter Thermal-/Throttling-Sensor verfuegbar."));
        return checks;
    }
}
