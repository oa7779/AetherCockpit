using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SystemCockpit.Services;

internal static class DisplayTopology
{
    internal static IReadOnlyList<BoardDevice> Read(IEnumerable<BoardDevice> inventory)
    {
        var known = inventory.ToDictionary(device => device.Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<BoardDevice>();
        foreach (var screen in Screen.AllScreens)
        {
            var found = false;
            for (uint index = 0; ; index++)
            {
                var monitor = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                if (!EnumDisplayDevices(screen.DeviceName, index, ref monitor, 1)) break;
                if ((monitor.Flags & 1) == 0) continue;
                var id = DeviceId(monitor.Id);
                if (id is null)
                    throw new InvalidOperationException("Aktiver Monitorpfad hat kein zuordenbares Windows-Geraeteformat: " + monitor.Id);
                var existing = known.GetValueOrDefault(id);
                var usage = $"Desktop aktiv / {(screen.Primary ? "Hauptbildschirm" : "Erweiterung/Spiegelung")} / " +
                    $"{screen.Bounds.Width} x {screen.Bounds.Height} / {screen.DeviceName}";
                result.Add(new(id, existing?.Name ?? monitor.Name, "Monitor", existing?.Status ?? "OK",
                    existing?.ParentId ?? "", usage, screen.Primary, true));
                found = true;
            }
            if (!found)
                result.Add(new("SCREEN:" + screen.DeviceName, screen.DeviceName, "Monitor", "Unknown", "",
                    $"Aktive Windows-Anzeigeflaeche / {(screen.Primary ? "Hauptbildschirm" : "Nebenbildschirm")} / " +
                    $"{screen.Bounds.Width} x {screen.Bounds.Height}; physische Monitorzuordnung nicht verfuegbar", screen.Primary, true));
        }
        return result;
    }

    internal static string? DeviceId(string interfacePath)
    {
        var parts = interfacePath.Split('#');
        return parts.Length == 4 && parts[0].Equals(@"\\?\DISPLAY", StringComparison.OrdinalIgnoreCase)
            ? "DISPLAY\\" + parts[1] + "\\" + parts[2] : null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Name;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string adapter, uint index, ref DisplayDevice device, uint flags);
}
