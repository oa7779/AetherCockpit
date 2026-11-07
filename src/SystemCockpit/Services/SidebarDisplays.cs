using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SystemCockpit.Services;

public static class SidebarDisplays
{
    public static IReadOnlyList<SidebarDisplay> GetAll()
    {
        var displays = new List<SidebarDisplay>();
        Win32Exception? error = null;
        var success = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(handle, ref info))
            {
                error = new Win32Exception(Marshal.GetLastWin32Error());
                return false;
            }
            displays.Add(new(info.DeviceName,
                info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom,
                info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom,
                (info.Flags & MonitorInfoPrimary) != 0, handle));
            return true;
        }, IntPtr.Zero);
        if (!success) throw error ?? new Win32Exception(Marshal.GetLastWin32Error());
        return displays;
    }

    public static SidebarDisplay ForWindow(IntPtr handle)
    {
        var target = MonitorFromWindow(handle, MonitorDefaultToNearest);
        return GetAll().FirstOrDefault(display => display.Handle == target)
            ?? throw new InvalidOperationException("Der Bildschirm der Seitenleiste konnte nicht ermittelt werden.");
    }

    internal static System.Drawing.Rectangle GetWindowBounds(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var rect))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private const uint MonitorInfoPrimary = 1;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr clipRect, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect,
        MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
}
