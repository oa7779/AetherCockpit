using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Data;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;

namespace AetherScreensaver;

internal sealed class SaverWindow : Window
{
    private readonly HardwareBoardView _board;
    private readonly TextBlock _status;
    private readonly TextBlock _readings;
    private readonly CockpitModel _model = new();
    private BoardSnapshot? _hardware;
    private readonly System.Drawing.Rectangle _bounds;
    private readonly nint _parent;
    private readonly LinearGradientBrush _backdrop;
    private readonly TranslateTransform _headerDrift = new();
    private readonly Border _neonFrame;
    private bool? _motion;
    private bool _closed;
    private readonly DisplayPulse _displayPulse;
    private readonly string? _settingsIssue;

    internal SaverWindow(System.Drawing.Rectangle bounds, nint parent = 0)
    {
        _bounds = bounds; _parent = parent;
        try { _model.Settings = CockpitSettings.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            Trace.TraceError("AETHER Schoner-Darstellungseinstellungen: {0}", ex);
            _settingsIssue = "Darstellungseinstellungen nicht lesbar: " + ex.Message;
        }
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        _backdrop = new LinearGradientBrush(new GradientStopCollection {
            new(Colors.Black, 0), new(Colors.Black, .6),
            new(Colors.Black, 1) }, 90);
        Background = _backdrop; ShowInTaskbar = false; Topmost = parent == 0;
        Width = Math.Max(1, bounds.Width); Height = Math.Max(1, bounds.Height);
        _board = new HardwareBoardView { DataContext = _model };
        _board.UseScreensaverLayout();
        _status = new TextBlock { Foreground = Brushes.Cyan, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        _readings = new TextBlock { Foreground = Brushes.Gold, TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        var panel = new Grid { ClipToBounds = true };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12),
            IsHitTestVisible = false, Opacity = .75, MaxHeight = 75 };
        header.Children.Add(_status); header.Children.Add(_readings);
        var clock = new TextBlock { Foreground = Brushes.LightCyan, FontFamily = new FontFamily("Consolas"), FontSize = 16 };
        clock.SetBinding(TextBlock.TextProperty, new Binding(nameof(CockpitModel.SystemClock)) { Source = _model });
        header.Children.Add(clock);
        header.MaxHeight = 110;
        Grid.SetRow(_board, 1);
        panel.Children.Add(_board);
        panel.Children.Add(header);
        _neonFrame = new Border { BorderBrush = new LinearGradientBrush(
            Color.FromRgb(36, 255, 227), Color.FromRgb(255, 70, 224), 45),
            BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(12),
            Margin = new Thickness(8), IsHitTestVisible = false };
        FrameGlow.SetVisible(_neonFrame, true);
        Grid.SetRowSpan(_neonFrame, 2);
        panel.Children.Add(_neonFrame);
        header.RenderTransform = _headerDrift;
        Content = new AdornerDecorator { Child = panel };
        _displayPulse = new DisplayPulse(Dispatcher, _model, day =>
        {
            SetMotion(false);
            Color[] colors = day ? [Color.FromRgb(27, 48, 81), Color.FromRgb(46, 79, 125), Color.FromRgb(36, 63, 99)]
                : [Colors.Black, Colors.Black, Colors.Black];
            for (var i = 0; i < colors.Length; i++) _backdrop.GradientStops[i].Color = colors[i];
            SetMotion(_model.MotionEnabled);
        });
        SourceInitialized += (_, _) => PlaceNative();
        Loaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= AnimationSettingsChanged;
            SystemParameters.StaticPropertyChanged += AnimationSettingsChanged;
            SetMotion(_model.MotionEnabled);
            _displayPulse.Start();
        };
        IsVisibleChanged += (_, _) => { SetMotion(_model.MotionEnabled); if (IsVisible && IsLoaded) _displayPulse.Start(); else _displayPulse.Stop(); };
        Unloaded += (_, _) => { _displayPulse.Stop(); SystemParameters.StaticPropertyChanged -= AnimationSettingsChanged; SetMotion(false); };
        Closed += (_, _) =>
        {
            _closed = true;
            _displayPulse.Dispose();
            SystemParameters.StaticPropertyChanged -= AnimationSettingsChanged;
            _board.Shutdown(); SetMotion(false); FrameGlow.SetVisible(_neonFrame, false);
        };
    }

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
        if (frame is not null && frame.DisplayMode != _model.Settings.DisplayMode)
        {
            _model.Settings = _model.Settings with { DisplayMode = frame.DisplayMode };
            _displayPulse.Refresh(DateTimeOffset.Now);
        }
        if (_hardware != hardware)
        {
            _board.Apply(hardware);
            _hardware = hardware;
        }
        _model.MotionEnabled = SystemParameters.ClientAreaAnimation && (frame?.Motion ?? true);
        SetMotion(_model.MotionEnabled);
        _board.UpdateComponentReadings(frame);
        _status.Text = status + (_settingsIssue is null ? "" : "\n" + _settingsIssue) +
            (hardware.Warnings.Length == 0 ? "" : "\n" + string.Join(" / ", hardware.Warnings));
        _readings.Text = frame is null
            ? "KEINE LIVE-MESSWERTE / CPU -- / RAM -- / GPU -- / DOWN -- / UP -- / TEMP --"
            : $"LIVE / CPU {frame.Cpu}% / RAM {frame.Ram}% / GPU {frame.Gpu}% / DOWN {frame.Download} Mbit/s / " +
              $"UP {(frame.Upload is { } upload ? upload.ToString("0.0") : "--")} Mbit/s / CPU-TEMP {frame.Temperature}";
    }

    private void SetMotion(bool enabled)
    {
        enabled = enabled && !_closed && IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation;
        if (_motion == enabled) return;
        _motion = enabled;
        FrameGlow.SetEnabled(_neonFrame, enabled);
        foreach (var stop in _backdrop.GradientStops)
            stop.BeginAnimation(GradientStop.ColorProperty, enabled ? new ColorAnimation(stop.Color,
                _model.IsDaytime ? Color.FromRgb(62, 92, 128) : Colors.Black, TimeSpan.FromSeconds(43 + stop.Offset * 19)) {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        _headerDrift.BeginAnimation(TranslateTransform.XProperty, enabled ? new DoubleAnimation(0, 16, TimeSpan.FromSeconds(31)) {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        _headerDrift.BeginAnimation(TranslateTransform.YProperty, enabled ? new DoubleAnimation(0, 9, TimeSpan.FromSeconds(37)) {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
    }

    private void AnimationSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) SetMotion(_model.MotionEnabled);
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
