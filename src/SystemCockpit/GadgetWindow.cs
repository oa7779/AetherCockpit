using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SystemCockpit.Services;

namespace SystemCockpit;

internal sealed class GadgetWindow : Window
{
    public string GadgetId { get; }
    internal bool DesktopAlive => _desktop != IntPtr.Zero && IsWindow(_desktop) &&
        IsWindow(new WindowInteropHelper(this).Handle);
    private readonly Action<GadgetLayout> _save;
    private readonly ContentControl _chart;
    private readonly Slider _opacity;
    private Point? _dragStart;
    private System.Drawing.Rectangle _original;
    private IntPtr _desktop;

    public GadgetWindow(string id, object data, string template, GadgetLayout layout, Action<GadgetLayout> save,
        Action<string> failure)
    {
        GadgetId = id;
        _save = save;
        Title = "AETHER / " + id;
        Width = 310;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = Brushes.White;
        ShowInTaskbar = false;
        ShowActivated = false;
        Opacity = layout.Opacity;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SystemCockpit;component/Controls/DiagramTemplates.xaml")
        });
        var panel = new StackPanel();
        var handle = new TextBlock
        {
            Text = DesktopUiText.Get("Gadget.Drag"),
            Foreground = Brushes.MediumTurquoise,
            Margin = new Thickness(10, 5, 10, 0),
            FontSize = 10,
            Cursor = Cursors.SizeAll
        };
        panel.Children.Add(handle);
        _chart = new ContentControl { Content = data, ContentTemplate = (DataTemplate)Resources[template] };
        panel.Children.Add(_chart);
        _opacity = new Slider { Minimum = 0.2, Maximum = 1, Value = layout.Opacity,
            Margin = new Thickness(12, 0, 12, 6), ToolTip = DesktopUiText.Get("Gadget.Opacity") };
        panel.Children.Add(_opacity);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 12, 9, 32)),
            BorderBrush = Brushes.MediumPurple, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Child = panel
        };
        _opacity.ValueChanged += (_, e) =>
        {
            Opacity = e.NewValue;
            if (IsLoaded) Persist();
        };
        handle.MouseLeftButtonDown += (_, e) =>
        {
            if (!GetCursorPos(out var cursor)) { ReportNativeError(); return; }
            _dragStart = new Point(cursor.X, cursor.Y);
            _original = SidebarDisplays.GetWindowBounds(new WindowInteropHelper(this).Handle);
            handle.CaptureMouse();
            e.Handled = true;
        };
        handle.MouseMove += (_, _) =>
        {
            if (_dragStart is not { } start || Mouse.LeftButton != MouseButtonState.Pressed) return;
            if (!GetCursorPos(out var cursor)) { ReportNativeError(); return; }
            Guard(() => Place(_original.Left + (int)(cursor.X - start.X), _original.Top + (int)(cursor.Y - start.Y)));
        };
        handle.MouseLeftButtonUp += (_, _) =>
        {
            if (_dragStart is null) return;
            _dragStart = null;
            handle.ReleaseMouseCapture();
            Guard(() => { ClampToDisplay(); Persist(); });
        };
        Loaded += (_, _) =>
        {
            try
            {
                _desktop = DesktopLayer.Find();
                var windowHandle = new WindowInteropHelper(this).Handle;
                var style = GetWindowLongPtr(windowHandle, -16).ToInt64();
                Marshal.SetLastPInvokeError(0);
                var oldStyle = SetWindowLongPtr(windowHandle, -16, new IntPtr((style & ~0x80000000L) | 0x40000000L));
                if (oldStyle == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                Marshal.SetLastPInvokeError(0);
                var previous = SetParent(windowHandle, _desktop);
                if (previous == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                Place((int)layout.X, (int)layout.Y);
                ClampToDisplay();
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                System.Diagnostics.Trace.TraceError("AETHER Desktop-Gadget: {0}", ex);
                Close();
                failure(ex.Message);
            }
        };
    }

    public void Update(object data) => _chart.Content = data;
    public void SetOpacity(double opacity) => _opacity.Value = opacity;

    private void ClampToDisplay()
    {
        var bounds = SidebarDisplays.GetWindowBounds(new WindowInteropHelper(this).Handle);
        var display = SidebarDisplays.ForWindow(new WindowInteropHelper(this).Handle);
        Place(Math.Clamp(bounds.Left, display.WorkLeft, Math.Max(display.WorkLeft, display.WorkRight - bounds.Width)),
            Math.Clamp(bounds.Top, display.WorkTop, Math.Max(display.WorkTop, display.WorkBottom - bounds.Height)));
    }

    private void Place(int screenX, int screenY)
    {
        var point = new NativePoint { X = screenX, Y = screenY };
        if (_desktop != IntPtr.Zero && !ScreenToClient(_desktop, ref point))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(1), point.X, point.Y, 0, 0, 0x0011))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void Persist()
    {
        var bounds = SidebarDisplays.GetWindowBounds(new WindowInteropHelper(this).Handle);
        _save(new(GadgetId, bounds.Left, bounds.Top, Opacity));
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            System.Diagnostics.Trace.TraceError("Gadget-Position: {0}", ex);
            MessageBox.Show(ex.Message, DesktopUiText.Get("Gadget.PositionFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void ReportNativeError()
    {
        var error = new Win32Exception(Marshal.GetLastWin32Error());
        System.Diagnostics.Trace.TraceError("Gadget-Mausposition: {0}", error);
        MessageBox.Show(error.Message, DesktopUiText.Get("Gadget.MoveFailed"));
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}

internal static class DesktopLayer
{
    public static IntPtr Find()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) throw new InvalidOperationException("Windows-Desktop (Explorer) nicht verfuegbar.");
        if (SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 2, 1000, out _) == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var childHost = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (childHost != IntPtr.Zero) return childHost;
        var result = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                result = FindWindowEx(IntPtr.Zero, window, "WorkerW", null);
            return result == IntPtr.Zero;
        }, IntPtr.Zero);
        return result != IntPtr.Zero ? result :
            throw new InvalidOperationException("Die Desktop-Gadget-Ebene wird von dieser Explorer-Konfiguration nicht angeboten.");
    }
    private delegate bool EnumCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string name, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string name, string? title);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
}
