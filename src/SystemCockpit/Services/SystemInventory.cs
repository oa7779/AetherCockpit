using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using Microsoft.Win32;

namespace SystemCockpit.Services;

public sealed record InventoryItem(string Category, string Name, string Detail);
public sealed record InstalledSoftware(string Name, string Version, string Publisher);
internal sealed record SoftwareRegistration(string Id, string Name, string Version, string Publisher,
    string Location, string InstallDate, string Architecture);
public sealed record InventorySnapshot(
    DateTimeOffset GeneratedAt, IReadOnlyList<InventoryItem> Hardware, IReadOnlyList<InstalledSoftware> Software);

/// <summary>
/// Erstellt ein rein lesendes Inventar aus Hardware (ueber den bestehenden
/// WindowsProbe und native Metriken) und installierter Software (Registry).
/// Nicht verfuegbare Werte bleiben als solche erhalten; nichts wird erfunden.
/// </summary>
public static class SystemInventory
{
    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public static List<InstalledSoftware> ReadInstalledSoftware()
    {
        var result = new Dictionary<string, InstalledSoftware>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in ReadRegistrations(CancellationToken.None))
        {
            var item = new InstalledSoftware(registration.Name, registration.Version, registration.Publisher);
            result[$"{item.Name}|{item.Version}"] = item;
        }
        return result.Values.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    internal static IReadOnlyList<SoftwareRegistration> ReadRegistrations(CancellationToken token)
    {
        var result = new List<SoftwareRegistration>();
        void Scan(RegistryKey root, string path)
        {
            using var key = root.OpenSubKey(path, writable: false);
            if (key is null) return;
            foreach (var name in key.GetSubKeyNames())
            {
                token.ThrowIfCancellationRequested();
                using var entry = key.OpenSubKey(name, writable: false);
                if (entry?.GetValue("DisplayName") is not string display || display.Trim().Length == 0) continue;
                if (entry.GetValue("SystemComponent") is int component && component == 1) continue;
                if (entry.GetValue("ParentKeyName") is string) continue;
                if (entry.GetValue("ReleaseType") is string release &&
                    (release.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                     release.Contains("Hotfix", StringComparison.OrdinalIgnoreCase))) continue;
                var version = entry.GetValue("DisplayVersion") as string ?? "";
                var publisher = entry.GetValue("Publisher") as string ?? "";
                result.Add(new(root.Name + "\\" + path + "\\" + name, display.Trim(), version.Trim(), publisher.Trim(),
                    entry.GetValue("InstallLocation") as string ?? "", entry.GetValue("InstallDate") as string ?? "",
                    path.Contains("WOW6432Node", StringComparison.Ordinal) ? "32-Bit-Registrierung; keine Prozessarchitektur" : "Native Registrierung; Architektur nicht bewiesen"));
            }
        }
        foreach (var path in UninstallRoots)
        {
            Scan(Registry.LocalMachine, path);
            Scan(Registry.CurrentUser, path);
        }
        return result;
    }

    public static async Task<InventorySnapshot> BuildAsync(CancellationToken token, IProgress<string>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        progress?.Report(DesktopUiText.Get("Inventory.ReadHardware"));
        var hardware = new List<InventoryItem>();
        void Add(string category, string name, string detail) => hardware.Add(new(category, name, detail));

        Add("System", DesktopUiText.Get("Inventory.MachineName"), Environment.MachineName);
        try
        {
            var report = await new WindowsProbe().ReadAsync(token);
            Add("System", "Windows", report.Windows);
            Add("System", "Mainboard", report.Board);
            Add(DesktopUiText.Get("Inventory.Processor"), "CPU", report.Cpu);
            Add(DesktopUiText.Get("Inventory.Graphics"), "GPU", report.Gpu);
            Add(DesktopUiText.Get("Inventory.Memory"), "RAM", report.Memory);
            foreach (var disk in report.Disks)
                Add(DesktopUiText.Get("Inventory.Disk"), disk.Name, $"{disk.Value} | {disk.Detail}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or OperationCanceledException
            && !token.IsCancellationRequested)
        {
            Add("System", DesktopUiText.Get("Inventory.WindowsQuery"), DesktopUiText.Format("Common.UnavailableDetail", ex.Message));
        }

        token.ThrowIfCancellationRequested();
        progress?.Report(DesktopUiText.Get("Inventory.ReadMemory"));
        try
        {
            var live = await Task.Run(() => new NativeMetrics().Read(), token);
            Add(DesktopUiText.Get("Inventory.Memory"), DesktopUiText.Get("Inventory.TotalRam"), $"{live.MemoryTotalGb:0.0} GB");
            foreach (var volume in live.Volumes)
                Add("Volume", volume.Name, $"{volume.Value} | {volume.Detail}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Add(DesktopUiText.Get("Inventory.Memory"), DesktopUiText.Get("Inventory.NativeMetrics"), DesktopUiText.Format("Common.UnavailableDetail", ex.Message));
        }

        token.ThrowIfCancellationRequested();
        progress?.Report(DesktopUiText.Get("Inventory.ReadNetwork"));
        try
        {
            // Nur echte Adapter: physische MAC, aktive Verbindung mit Bandbreite.
            // WFP-/QoS-/Npcap-Filter- und WiFi-Direct-Pseudoadapter werden ausgeschlossen.
            string[] noise = ["-0000", "Loopback", "LightWeight Filter", "QoS Packet Scheduler",
                "Npcap Packet Driver", "Native WiFi Filter", "WFP ", "Miniport"];
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up && n.Speed > 0 &&
                                     n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 &&
                                     n.GetPhysicalAddress().GetAddressBytes().Length == 6 &&
                                     !noise.Any(term => n.Name.Contains(term, StringComparison.OrdinalIgnoreCase))))
                Add(DesktopUiText.Get("Common.Network"), adapter.Name,
                    $"{adapter.NetworkInterfaceType} | {adapter.Speed / 1_000_000} Mbit/s");
        }
        catch (NetworkInformationException ex) { Add(DesktopUiText.Get("Common.Network"), "Adapter", DesktopUiText.Format("Common.UnavailableDetail", ex.Message)); }

        token.ThrowIfCancellationRequested();
        progress?.Report(DesktopUiText.Get("Inventory.ReadSoftware"));
        var software = await Task.Run(ReadInstalledSoftware, token);
        token.ThrowIfCancellationRequested();
        return new(DateTimeOffset.Now, hardware, software);
    }

    public static string ToText(InventorySnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine(DesktopUiText.Format("Inventory.ExportHeader", snapshot.GeneratedAt));
        builder.AppendLine(DesktopUiText.Get("Inventory.ExportNotice"));
        builder.AppendLine();
        builder.AppendLine("== HARDWARE / SYSTEM ==");
        foreach (var group in snapshot.Hardware.GroupBy(h => h.Category))
        {
            builder.AppendLine($"[{group.Key}]");
            foreach (var item in group) builder.AppendLine($"  {item.Name}: {item.Detail}");
        }
        builder.AppendLine();
        builder.AppendLine("== " + DesktopUiText.Format("Inventory.SoftwareCount", snapshot.Software.Count) + " ==");
        foreach (var app in snapshot.Software)
            builder.AppendLine($"  {app.Name} | {(app.Version.Length > 0 ? app.Version : "-")} | " +
                $"{(app.Publisher.Length > 0 ? app.Publisher : "-")}");
        return builder.ToString();
    }
}
