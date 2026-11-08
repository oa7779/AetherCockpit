using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using SystemCockpit.Services;

namespace SystemCockpit;

public partial class SidebarWindow : Window
{
    private const int RevealPixels = 12;
    private readonly CockpitModel _model;
    private readonly Action<CockpitSettings> _saveSettings;
    private readonly DispatcherTimer _behaviorTimer;
    private CockpitSettings _settings;
    private IReadOnlyList<SidebarDisplay> _displays = [];
    private bool _initializing;
    private bool _autoHidden;
    private DateTime _pointerLeftAt;

    public SidebarWindow(CockpitModel model, CockpitSettings settings,
        Action<CockpitSettings> saveSettings) : this(model, settings, saveSettings, false) { }

    internal SidebarWindow(CockpitModel model, CockpitSettings settings,
        Action<CockpitSettings> saveSettings, bool synthetic)
    {
        InitializeComponent();
        _model = model;
        _settings = settings;
        _saveSettings = saveSettings;
        _synthetic = synthetic;
        DataContext = model;
        _behaviorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _behaviorTimer.Tick += BehaviorTimerTick;
        _model.PropertyChanged += ModelChanged;
    }
    private readonly bool _synthetic;

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CockpitModel.Settings)) _settings = _model.Settings;
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_synthetic) return;
        try
        {
            _initializing = true;
            try
            {
                RefreshDisplays();
                MonitorSelect.ItemsSource = _displays;
                var remembered = _displays.Any(display => string.Equals(display.DeviceName,
                    _settings.SidebarMonitorDeviceName, StringComparison.OrdinalIgnoreCase));
                MonitorSelect.SelectedItem = SelectedDisplay();
                SideSelect.SelectedValue = _settings.SidebarDockSide;
                PinnedToggle.IsChecked = _settings.SidebarPinned;
                AutoHideToggle.IsChecked = _settings.SidebarAutoHide;
                CpuWidgetToggle.IsChecked = _settings.SidebarWidgetCpu;
                MemoryWidgetToggle.IsChecked = _settings.SidebarWidgetMemory;
                GpuWidgetToggle.IsChecked = _settings.SidebarWidgetGpu;
                TemperatureWidgetToggle.IsChecked = _settings.SidebarWidgetTemperatures;
                NetworkWidgetToggle.IsChecked = _settings.SidebarWidgetNetwork;
                DrivesWidgetToggle.IsChecked = _settings.SidebarWidgetDrives;
                OpacitySlider.Value = _settings.SidebarOpacity;
                if (!remembered && _settings.SidebarMonitorDeviceName.Length > 0)
                {
                    _settings = _settings with { SidebarMonitorDeviceName = SelectedDisplay().DeviceName };
                    SaveSettings();
                }
            }
            finally { _initializing = false; }

            ApplyPlacement();
            UpdateAutoHideAvailability();
            _behaviorTimer.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceError("AETHER xBar konnte nicht gestartet werden: {0}", ex);
            MessageBox.Show(this, ex.Message, DesktopUiText.Get("Sidebar.StartFailed"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void RefreshDisplays()
    {
        _displays = SidebarDisplays.GetAll();
        if (_displays.Count == 0) throw new InvalidOperationException(DesktopUiText.Get("Sidebar.NoMonitor"));
    }

    private SidebarDisplay SelectedDisplay()
    {
        return _displays.FirstOrDefault(display => string.Equals(display.DeviceName,
                   _settings.SidebarMonitorDeviceName, StringComparison.OrdinalIgnoreCase))
               ?? _displays.FirstOrDefault(display => display.IsPrimary)
               ?? _displays[0];
    }

    private void ApplyPlacement()
    {
        var display = SelectedDisplay();
        var handle = new WindowInteropHelper(this).Handle;
        if (SidebarDisplays.ForWindow(handle).Handle != display.Handle)
            MoveNative(display.WorkLeft, display.WorkTop);
        var dpi = GetDpiForWindow(handle);
        if (dpi == 0) throw new InvalidOperationException(DesktopUiText.Get("Sidebar.NoDpi"));
        var scale = dpi / 96.0;
        var bounds = SidebarPlacement.Restore(display.WorkArea, scale, _settings.SidebarPinned,
            _settings.SidebarDockSide, _settings.SidebarHorizontalOffset, _settings.SidebarVerticalOffset);
        MinHeight = Math.Min(480, bounds.Height / scale);
        Width = bounds.Width / scale;
        Height = bounds.Height / scale;
        if (!SetWindowPos(handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                SwpNoZOrder | SwpNoActivate))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        PinButton.Content = _settings.SidebarPinned ? "PIN" : "FLOAT";
        _autoHidden = false;
        _pointerLeftAt = DateTime.MinValue;
        UpdateAutoHideAvailability();
    }

    private void UpdateAutoHideAvailability()
    {
        AutoHideToggle.IsEnabled = _displays.Count == 1 && _settings.SidebarPinned;
        AutoHideToggle.ToolTip = _displays.Count > 1
            ? DesktopUiText.Get("Sidebar.AutoHideMulti")
            : !_settings.SidebarPinned
                ? DesktopUiText.Get("Sidebar.AutoHideFloating")
                : DesktopUiText.Get("Sidebar.AutoHideHint");
    }

    private void OptionsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _initializing = true;
            try
            {
                RefreshDisplays();
                MonitorSelect.ItemsSource = _displays;
                MonitorSelect.SelectedItem = SelectedDisplay();
                SideSelect.SelectedValue = _settings.SidebarDockSide;
                PinnedToggle.IsChecked = _settings.SidebarPinned;
                AutoHideToggle.IsChecked = _settings.SidebarAutoHide;
                CpuWidgetToggle.IsChecked = _settings.SidebarWidgetCpu;
                MemoryWidgetToggle.IsChecked = _settings.SidebarWidgetMemory;
                GpuWidgetToggle.IsChecked = _settings.SidebarWidgetGpu;
                TemperatureWidgetToggle.IsChecked = _settings.SidebarWidgetTemperatures;
                NetworkWidgetToggle.IsChecked = _settings.SidebarWidgetNetwork;
                DrivesWidgetToggle.IsChecked = _settings.SidebarWidgetDrives;
                OpacitySlider.Value = _settings.SidebarOpacity;
                UpdateAutoHideAvailability();
            }
            finally { _initializing = false; }
            OptionsPopup.IsOpen = true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _model.Footer = DesktopUiText.Format("Sidebar.ReadMonitorsFailed", ex.Message);
            SidebarStatus.Text = _model.Footer;
            MessageBox.Show(this, ex.Message, DesktopUiText.Get("Sidebar.SettingsUnavailable"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PinClicked(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { SidebarPinned = !_settings.SidebarPinned };
        ApplyPlacement();
        _initializing = true;
        try { PinnedToggle.IsChecked = _settings.SidebarPinned; }
        finally { _initializing = false; }
        SaveSettings();
    }

    private void MonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || MonitorSelect.SelectedItem is not SidebarDisplay display) return;
        _settings = _settings with { SidebarMonitorDeviceName = display.DeviceName };
        ApplyPlacement();
        SaveSettings();
    }

    private void SideChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || SideSelect.SelectedValue is not string side) return;
        _settings = _settings with
        {
            SidebarDockSide = side,
            SidebarHorizontalOffset = side == "left" ? 0 : 0.95
        };
        ApplyPlacement();
        SaveSettings();
    }

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing || PinnedToggle.IsChecked is not { } pinned) return;
        _settings = _settings with { SidebarPinned = pinned };
        ApplyPlacement();
        UpdateAutoHideAvailability();
        SaveSettings();
    }

    private void AutoHideChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing || AutoHideToggle.IsChecked is not { } autoHide) return;
        _settings = _settings with { SidebarAutoHide = autoHide };
        _autoHidden = false;
        ApplyPlacement();
        SaveSettings();
    }

    private void WidgetChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _settings = _settings with
        {
            SidebarWidgetCpu = CpuWidgetToggle.IsChecked == true,
            SidebarWidgetMemory = MemoryWidgetToggle.IsChecked == true,
            SidebarWidgetGpu = GpuWidgetToggle.IsChecked == true,
            SidebarWidgetTemperatures = TemperatureWidgetToggle.IsChecked == true,
            SidebarWidgetNetwork = NetworkWidgetToggle.IsChecked == true,
            SidebarWidgetDrives = DrivesWidgetToggle.IsChecked == true
        };
        _model.Settings = _settings;
        SaveSettings();
    }

    private void OpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || !IsLoaded) return;
        _settings = _model.Settings with { SidebarOpacity = e.NewValue };
        SaveSettings();
    }

    private void HeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (_settings.SidebarPinned || e.ButtonState != MouseButtonState.Pressed) return;
        try
        {
            _behaviorTimer.Stop();
            DragMove();
            CaptureFloatingPosition();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Trace.TraceError("AETHER xBar konnte nicht verschoben werden: {0}", ex);
            _model.Footer = DesktopUiText.Format("Sidebar.PositionFailed", ex.Message);
            SidebarStatus.Text = _model.Footer;
            MessageBox.Show(this, ex.Message, DesktopUiText.Get("Sidebar.MoveFailed"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { if (IsLoaded && IsVisible) _behaviorTimer.Start(); }
    }

    internal void CaptureFloatingPosition()
    {
        if (_settings.SidebarPinned) return;
        RefreshDisplays();
        var display = SidebarDisplays.ForWindow(new WindowInteropHelper(this).Handle);
        var bounds = SidebarDisplays.GetWindowBounds(new WindowInteropHelper(this).Handle);
        var offsets = SidebarPlacement.Capture(display.WorkArea, bounds);
        _settings = _settings with
        {
            SidebarMonitorDeviceName = display.DeviceName,
            SidebarHorizontalOffset = offsets.Horizontal,
            SidebarVerticalOffset = offsets.Vertical
        };
        ApplyPlacement();
        SaveSettings();
    }

    private void UpdateWindowBehavior()
    {
        if (!IsLoaded) return;
        if (!GetCursorPos(out var cursor))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            _model.Footer = DesktopUiText.Format("Sidebar.MouseFailed", error.Message);
            SidebarStatus.Text = _model.Footer;
            Trace.TraceError("AETHER xBar: {0}", error);
            _behaviorTimer.Stop();
            return;
        }
        var display = SelectedDisplay();
        var foreground = GetForegroundWindow();
        var isFullscreen = IsFullscreenWindow(foreground, display);
        Topmost = !isFullscreen;
        var autoHide = _settings.SidebarAutoHide && _settings.SidebarPinned && _displays.Count == 1;
        var shouldHide = false;
        if (!shouldHide && autoHide && !OptionsPopup.IsOpen)
        {
            var bounds = SidebarDisplays.GetWindowBounds(new WindowInteropHelper(this).Handle);
            var isPointerOnBar = bounds.Contains(cursor.X, cursor.Y);
            var isPointerAtRevealEdge = cursor.Y >= display.WorkTop && cursor.Y < display.WorkBottom &&
                (_settings.SidebarDockSide == "left"
                    ? cursor.X >= display.WorkLeft && cursor.X < display.WorkLeft + RevealPixels
                    : cursor.X < display.WorkRight && cursor.X >= display.WorkRight - RevealPixels);
            if (isPointerOnBar || isPointerAtRevealEdge) _pointerLeftAt = DateTime.MinValue;
            else if (_pointerLeftAt == DateTime.MinValue) _pointerLeftAt = DateTime.UtcNow;
            shouldHide = !isPointerOnBar && _pointerLeftAt != DateTime.MinValue &&
                DateTime.UtcNow - _pointerLeftAt > TimeSpan.FromMilliseconds(550);
        }
        if (shouldHide != _autoHidden) SetHidden(shouldHide, display);
    }

    private void BehaviorTimerTick(object? sender, EventArgs e)
    {
        try { UpdateWindowBehavior(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _model.Footer = DesktopUiText.Format("Sidebar.BehaviorFailed", ex.Message);
            SidebarStatus.Text = _model.Footer;
            Trace.TraceError("AETHER xBar: {0}", ex);
            _behaviorTimer.Stop();
        }
    }

    private bool IsFullscreenWindow(IntPtr window, SidebarDisplay display)
    {
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == Environment.ProcessId || !GetWindowRect(window, out var rect)) return false;
        return rect.Left <= display.Left + 2 && rect.Top <= display.Top + 2 &&
               rect.Right >= display.Right - 2 && rect.Bottom >= display.Bottom - 2;
    }

    private void SetHidden(bool hidden, SidebarDisplay display)
    {
        var bounds = SidebarDisplays.GetWindowBounds(new WindowInteropHelper(this).Handle);
        var x = hidden
            ? _settings.SidebarDockSide == "left" ? display.WorkLeft - bounds.Width + RevealPixels
                : display.WorkRight - RevealPixels
            : _settings.SidebarDockSide == "left" ? display.WorkLeft : display.WorkRight - bounds.Width;
        MoveNative(x, bounds.Top);
        _autoHidden = hidden;
    }

    private void MoveNative(int x, int y)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && !SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void SaveSettings()
    {
        _model.Settings = _settings;
        DataContext = null;
        DataContext = _model;
        try { _saveSettings(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _model.Footer = DesktopUiText.Format("Sidebar.SaveFailed", ex.Message);
            SidebarStatus.Text = _model.Footer;
            Trace.TraceError("AETHER xBar: {0}", ex);
        }
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        _behaviorTimer.Stop();
        _model.PropertyChanged -= ModelChanged;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

}
