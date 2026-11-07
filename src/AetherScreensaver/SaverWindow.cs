using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;

namespace AetherScreensaver;

internal sealed class SaverWindow : Window
{
    private readonly HardwareBoardView _board;
    private readonly TextBlock _status;
    private readonly TextBlock _readings;
    private readonly TextBlock _audioStatus;
    private readonly CockpitModel _model = new();
    private BoardSnapshot? _hardware;
    private readonly System.Drawing.Rectangle _bounds;
    private readonly nint _parent;
    private readonly LinearGradientBrush _backdrop;
    private readonly TranslateTransform _headerDrift = new();
    private bool? _motion;

    internal SaverWindow(System.Drawing.Rectangle bounds, nint parent = 0)
    {
        _bounds = bounds; _parent = parent;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        _backdrop = new LinearGradientBrush(new GradientStopCollection {
            new(Color.FromRgb(3, 5, 27), 0), new(Color.FromRgb(59, 8, 124), .6),
            new(Color.FromRgb(12, 20, 66), 1) }, 90);
        Background = _backdrop; ShowInTaskbar = false; Topmost = parent == 0;
        Width = Math.Max(1, bounds.Width); Height = Math.Max(1, bounds.Height);
        _board = new HardwareBoardView { DataContext = _model };
        _board.UseScreensaverLayout();
        _status = new TextBlock { Foreground = Brushes.Cyan, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        _readings = new TextBlock { Foreground = Brushes.Gold, TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        _audioStatus = new TextBlock { Foreground = Brushes.Gold, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        var panel = new Grid { ClipToBounds = true };
        var header = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12),
            IsHitTestVisible = false, Opacity = .75, MaxHeight = 75 };
        header.Children.Add(_status); header.Children.Add(_readings); header.Children.Add(_audioStatus);
        panel.Children.Add(_board);
        panel.Children.Add(header);
        header.RenderTransform = _headerDrift;
        Content = panel;
        SourceInitialized += (_, _) => PlaceNative();
        Closed += (_, _) => { _board.Shutdown(); SetMotion(false); };
    }

    internal void ShowAudioError(string message) => _audioStatus.Text = message;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_parent == 0) return base.MeasureOverride(availableSize);
        // The preview parent owns sizing. Top-level Window min/max layout can FailFast during external HWND teardown.
        if (!IsWindow(_parent) || VisualChildrenCount == 0 || GetVisualChild(0) is not UIElement child)
            return default;
        child.Measure(availableSize);
        return child.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_parent == 0) return base.ArrangeOverride(finalSize);
        if (IsWindow(_parent) && VisualChildrenCount > 0 && GetVisualChild(0) is UIElement child)
            child.Arrange(new System.Windows.Rect(finalSize));
        return finalSize;
    }

    internal void Present(BoardSnapshot hardware, BoardFeedFrame? frame, string status)
    {
        if (frame is not null && !BoardLiveFeed.IsFresh(frame, DateTimeOffset.UtcNow)) frame = null;
        if (_hardware != hardware)
        {
            _board.Apply(hardware);
            _hardware = hardware;
        }
        _model.MotionEnabled = SystemParameters.ClientAreaAnimation && (frame?.Motion ?? true);
        SetMotion(_model.MotionEnabled);
        _board.UpdateComponentReadings(frame);
        _status.Text = status + (hardware.Warnings.Length == 0 ? "" : "\n" + string.Join(" / ", hardware.Warnings));
        _readings.Text = frame is null
            ? "KEINE LIVE-MESSWERTE / CPU -- / RAM -- / GPU -- / DOWN -- / UP -- / TEMP --"
            : $"LIVE / CPU {frame.Cpu}% / RAM {frame.Ram}% / GPU {frame.Gpu}% / DOWN {frame.Download} Mbit/s / " +
              $"UP {(frame.Upload is { } upload ? upload.ToString("0.0") : "--")} Mbit/s / CPU-TEMP {frame.Temperature}";
    }

    private void SetMotion(bool enabled)
    {
        if (_motion == enabled) return;
        _motion = enabled;
        foreach (var stop in _backdrop.GradientStops)
            stop.BeginAnimation(GradientStop.ColorProperty, enabled ? new ColorAnimation(stop.Color,
                Color.FromRgb(22, 38, 90), TimeSpan.FromSeconds(43 + stop.Offset * 19)) {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        _headerDrift.BeginAnimation(TranslateTransform.XProperty, enabled ? new DoubleAnimation(0, 16, TimeSpan.FromSeconds(31)) {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        _headerDrift.BeginAnimation(TranslateTransform.YProperty, enabled ? new DoubleAnimation(0, 9, TimeSpan.FromSeconds(37)) {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
    }

    private void PlaceNative()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (_parent != 0)
        {
            var style = GetWindowLong(handle, -16);
            SetWindowLong(handle, -16, (style & ~unchecked((int)0x80000000)) | 0x40000000);
            Marshal.SetLastPInvokeError(0);
            if (SetParent(handle, _parent) == 0 && Marshal.GetLastPInvokeError() != 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            ResizePreview();
        }
        else if (!SetWindowPos(handle, new nint(-1), _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, 0x40))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal bool ResizePreview()
    {
        if (_parent == 0) return true;
        if (!IsWindow(_parent)) return false;
        if (!GetClientRect(_parent, out var rect)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!SetWindowPos(new WindowInteropHelper(this).Handle, 0, 0, 0, Math.Max(1, rect.Right), Math.Max(1, rect.Bottom), 0x14))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint h, nint after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(nint h, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(nint h, int index, int value);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint h);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(nint h, out Rect rect);
}
