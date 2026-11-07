using System.IO;
using System.Text.Json;
using SystemCockpit.Services;

internal static class SettingsUpgradeTests
{
    internal static int Run()
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
        void Reject(CockpitSettings settings)
        {
            try { settings.Validate(); }
            catch (ArgumentException) { assertions++; return; }
            throw new InvalidOperationException("Invalid optional path was accepted.");
        }
        var directory = Path.Combine(Path.GetTempPath(), "AetherSettingsUpgrade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var smart = Path.Combine(directory, "old-portable", "tools", "smartctl.exe");
            var present = Path.Combine(directory, "old-portable", "tools", "PresentMon.exe");
            var oui = Path.Combine(directory, "old-vendor-data.csv");
            var settingsPath = Path.Combine(directory, "settings.json");
            var profilePath = Path.Combine(directory, "desktop-defaults.json");
            var marker = Path.Combine(directory, "setup-profile.id");
            var original = new CockpitSettings
            {
                SmartctlPath = smart, PresentMonPath = present, OuiCsvPath = oui,
                AppOpacity = .67, CpuWarningC = 94, SidebarWidgetMemory = true,
                GadgetLayouts = [new("cpu", -1200, 44, .75)]
            };
            // Write the old settings verbatim, as if their tools disappeared after a previous release.
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(original));
            var loaded = CockpitSettings.Load(settingsPath);
            Check(loaded.SmartctlPath == smart && loaded.PresentMonPath == present && loaded.AppOpacity == .67,
                "Missing optional executable must not reject persisted settings");
            Check(loaded.OptionalFileWarnings().Count == 3 &&
                loaded.OptionalFileWarnings().Any(w => w.Contains("smartctl") && w.Contains(smart)) &&
                loaded.OptionalFileWarnings().Any(w => w.Contains("PresentMon") && w.Contains(present)),
                "All missing optional files are explicitly reported with exact setting/path");
            var profile = new SetupProfile("upgrade-regression", true, true, false, false, true, true, true, false, "");
            File.WriteAllText(profilePath, JsonSerializer.Serialize(profile));
            SetupProfile.ApplyInstalledProfile(profilePath, settingsPath, marker);
            var upgraded = CockpitSettings.Load(settingsPath);
            Check(upgraded.GadgetsEnabled && !upgraded.SidebarWidgetMemory && !upgraded.SidebarWidgetGpu,
                "Real file-based setup profile applies despite missing tools");
            Check(upgraded.SmartctlPath == smart && upgraded.PresentMonPath == present && upgraded.OuiCsvPath == oui,
                "Upgrade retains missing optional paths without redirecting executable selection");
            Check(upgraded.AppOpacity == .67 && upgraded.CpuWarningC == 94 &&
                upgraded.GadgetLayouts.Single() == original.GadgetLayouts.Single(),
                "Unrelated preferences and negative monitor gadget positions survive upgrade");
            Check(File.ReadAllText(marker) == profile.Id, "Profile marker commits only after settings saved");
            var customized = upgraded with { SidebarWidgetMemory = true };
            customized.Save(settingsPath);
            var before = File.ReadAllBytes(settingsPath);
            SetupProfile.ApplyInstalledProfile(profilePath, settingsPath, marker);
            Check(before.SequenceEqual(File.ReadAllBytes(settingsPath)), "Already applied profile remains byte-preserving and idempotent");
            Directory.CreateDirectory(Path.GetDirectoryName(smart)!);
            File.WriteAllText(smart, "test fixture, never executed");
            File.WriteAllText(present, "test fixture, never executed");
            File.WriteAllText(oui, "test fixture, never executed");
            Check(CockpitSettings.Load(settingsPath).OptionalFileWarnings().Count == 0,
                "Restored optional files clear warnings without rewriting preferences");
            File.Delete(smart); File.Delete(present); File.Delete(oui);
            Check(CockpitSettings.Load(settingsPath).OptionalFileWarnings().Count == 3,
                "Files disappearing after save are availability warnings, not corrupt settings");
            var cleared = upgraded with { SmartctlPath = "", PresentMonPath = "", OuiCsvPath = "" };
            cleared.Save(settingsPath);
            Check(CockpitSettings.Load(settingsPath).OptionalFileWarnings().Count == 0,
                "Explicitly clearing optional paths disables overrides cleanly");
            Reject(original with { SmartctlPath = "relative.exe" });
            Reject(original with { PresentMonPath = Path.Combine(directory, "wrong.cmd") });
            Reject(original with { SmartctlPath = null! });
            Reject(original with { PresentMonPath = present + "\r\n" });
            Reject(original with { SmartctlPath = Path.Combine(directory, "bad|name.exe") });
            Reject(original with { OuiCsvPath = "relative.csv" });
            Reject(original with { OuiCsvPath = null! });
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(original with { CpuWarningC = 12 }));
            File.Delete(marker);
            before = File.ReadAllBytes(settingsPath);
            try { SetupProfile.ApplyInstalledProfile(profilePath, settingsPath, marker); Check(false, "Invalid thresholds accepted during setup"); }
            catch (ArgumentException) { Check(!File.Exists(marker) && before.SequenceEqual(File.ReadAllBytes(settingsPath)),
                "Genuinely invalid settings still reject profile atomically without marker or overwriting data"); }
            try { new SmartService(smart).ScanAsync(CancellationToken.None).GetAwaiter().GetResult(); Check(false, "Missing smartctl was executed"); }
            catch (FileNotFoundException) { Check(true, "Missing configured smartctl remains explicitly blocked at execution time"); }
            try { NetworkTools.ReadOui(oui); Check(false, "Missing OUI database silently ignored"); }
            catch (FileNotFoundException) { Check(true, "Missing configured OUI database explicitly fails at usage time"); }
        }
        finally { Directory.Delete(directory, recursive: true); }
        return assertions;
    }
}
