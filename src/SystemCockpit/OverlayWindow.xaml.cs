using System.ComponentModel;
using System.Drawing;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SystemCockpit.Services;

namespace SystemCockpit;

/// <summary>
/// Kompaktes Always-on-top-Panel mit den Live-Warnstufenwerten des Cockpits.
/// Zeigt dieselben Modellwerte wie das Hauptfenster: nicht verfuegbare Sensoren
/// bleiben "Nicht verfuegbar". Bei exklusivem Vollbild kann Windows das
/// Overlay verdecken; das ist eine Plattformgrenze, kein Messfehler.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly CockpitModel _model;
    private readonly Action<CockpitSettings> _savePlacement;
    private CockpitSettings _settings;
    private bool _alertActive;

    public OverlayWindow(CockpitModel model, CockpitSettings settings, Action<CockpitSettings> savePlacement)
    {
        InitializeComponent();
        _model = model;
        _settings = settings;
        _savePlacement = savePlacement;
        DataContext = model;
        Loaded += (_, _) => RestorePlacement();
        model.PropertyChanged += ModelChanged;
        Closed += (_, _) => model.PropertyChanged -= ModelChanged;
        // Initialzustand uebernehmen, falls die Warnschwelle bereits erreicht ist.
        ModelChanged(this, new PropertyChangedEventArgs(nameof(CockpitModel.OverlayAlert)));
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CockpitModel.OverlayAlert)) return;
        var active = _model.OverlayAlert.Length > 0;
        AlertBanner.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        // Hinweiston nur beim Uebergang in den Warnzustand, nicht bei jedem Messpunkt.
        if (active && !_alertActive) SystemSounds.Exclamation.Play();
        _alertActive = active;
    }

    private void DragClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        DockToCurrentMonitor();
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void RestorePlacement()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var monitors = GetMonitors();
        var monitor = monitors.FirstOrDefault(s => string.Equals(s.DeviceName,
            _settings.OverlayMonitorDeviceName, StringComparison.OrdinalIgnoreCase));
        monitor ??= monitors.FirstOrDefault(s => s.IsPrimary) ?? monitors.FirstOrDefault();
        if (monitor is null) throw new InvalidOperationException("Windows meldet keinen verfuegbaren Bildschirm.");

        var window = ReadWindowBounds(handle);
        var position = OverlayDocking.Restore(monitor.WorkArea, window.Size,
            _settings.OverlayDockSide, _settings.OverlayVerticalOffset);
        MoveWindow(handle, position.Left, position.Top);
    }

    private void DockToCurrentMonitor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var monitorHandle = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var monitor = GetMonitors().FirstOrDefault(s => s.Handle == monitorHandle)
            ?? throw new InvalidOperationException("Der aktuelle Bildschirm konnte nicht ermittelt werden.");
        var result = OverlayDocking.Snap(monitor.WorkArea, ReadWindowBounds(handle));
        MoveWindow(handle, result.Left, result.Top);
        _settings = _settings with
        {
            OverlayMonitorDeviceName = monitor.DeviceName,
            OverlayDockSide = result.Side,
            OverlayVerticalOffset = result.VerticalOffset
        };
        _savePlacement(_settings);
    }

    private static Rectangle ReadWindowBounds(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var rect))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private static void MoveWindow(IntPtr handle, int left, int top)
    {
        if (!SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 0x00000002;

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

    private sealed record DisplayMonitor(IntPtr Handle, Rectangle WorkArea, string DeviceName, bool IsPrimary);

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr clipRect, IntPtr data);

    private static DisplayMonitor[] GetMonitors()
    {
        var monitors = new List<DisplayMonitor>();
        System.ComponentModel.Win32Exception? error = null;
        var success = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(handle, ref info))
            {
                error = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                return false;
            }
            monitors.Add(new(handle,
                Rectangle.FromLTRB(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom),
                info.DeviceName, (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        if (!success) throw error ?? new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return monitors.ToArray();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect,
        MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
}
