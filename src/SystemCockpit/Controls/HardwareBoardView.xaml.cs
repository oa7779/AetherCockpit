using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Threading;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public partial class HardwareBoardView : UserControl
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Polyline> _flows = [];
    private readonly List<RotateTransform> _fans = [];
    private readonly List<TranslateTransform> _binaryRows = [];
    private readonly List<UIElement> _modernIndicators = [];
    private readonly List<(GradientStop Stop, Color Alternate)> _skyColors = [];
    private readonly List<(SolidColorBrush Brush, Color Alternate)> _routeColors = [];
    private readonly TranslateTransform _sceneDrift = new();
    private readonly DispatcherTimer _readoutTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Stopwatch _sceneTime = Stopwatch.StartNew();
    private ScaleTransform? _gridMotion;
    private CockpitModel? _model;
    private bool _loaded, _busy, _shutdown;
    internal Task Pending { get; private set; } = Task.CompletedTask;
    internal IReadOnlyList<BoardGroup> Groups { get; private set; } = [];
    internal bool FlowAnimating { get; private set; }
    internal bool SceneAnimating { get; private set; }
    internal int DriveCount { get; private set; }
    internal BoardSnapshot? Snapshot { get; private set; }
    private bool _screensaverLayout;
    private double _viewportAspect;
    private readonly Dictionary<string, TextBlock> _componentReadings = [];
    private readonly Dictionary<string, (FrameworkElement Text, Grid Dial, NeonGauge Gauge, TextBlock Name, TextBlock Value)> _componentDials = [];
    private BoardFeedFrame? _lastFrame;
    private readonly Dictionary<string, IReadOnlyList<BoardComponentReadings.Metric>> _dialMetrics = [];
    private bool? _motionState;
    private bool _windowsAnimations = SystemParameters.ClientAreaAnimation;
    private string? _selectedGroup;
    private string? _selectedDevice;
    private bool _drawnDay;
    internal int AnimationStarts { get; private set; }
    internal static bool AnimationAllowed(bool loaded, bool visible, bool motionEnabled, bool windowsAnimations, bool shutdown) =>
        !shutdown && loaded && visible && motionEnabled && windowsAnimations;

    public HardwareBoardView()
    {
        InitializeComponent();
        ApplyModernChrome();
        BoardScale.RenderTransform = _sceneDrift;
        _readoutTimer.Tick += (_, _) => RefreshReadouts();
        DataContextChanged += (_, _) => ConnectModel();
        Loaded += (_, _) =>
        {
            _windowsAnimations = SystemParameters.ClientAreaAnimation;
            SystemParameters.StaticPropertyChanged -= WindowsMotionChanged;
            SystemParameters.StaticPropertyChanged += WindowsMotionChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= WindowsThemeChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += WindowsThemeChanged;
            ConnectModel(); UpdateMotion();
        };
        Unloaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= WindowsMotionChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= WindowsThemeChanged;
            DisconnectModel();
            UpdateMotion();
        };
        IsVisibleChanged += (_, _) => UpdateMotion();
    }

    private void ConnectModel()
    {
        DisconnectModel();
        if (!_shutdown && IsLoaded && DataContext is CockpitModel model)
        {
            _model = model;
            _model.PropertyChanged += ModelChanged;
            if (Snapshot is { } snapshot && _drawnDay != model.IsDaytime)
            {
                var frame = _lastFrame;
                Apply(snapshot);
                UpdateComponentReadings(frame);
            }
        }
        UpdateMotion();
    }

    private void DisconnectModel()
    {
        if (_model is not null) _model.PropertyChanged -= ModelChanged;
        _model = null;
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CockpitModel.MotionEnabled)) UpdateMotion();
        if (e.PropertyName == nameof(CockpitModel.IsDaytime) && Snapshot is { } snapshot)
        {
            var frame = _lastFrame;
            Apply(snapshot);
            UpdateComponentReadings(frame);
        }
    }

    private void WindowsMotionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        _windowsAnimations = SystemParameters.ClientAreaAnimation;
        UpdateMotion();
    }

    private void WindowsThemeChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.Color || !SkinCatalog.IsModern ||
            !IsLoaded || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded && SkinCatalog.IsModern && Snapshot is { } snapshot)
            {
                var frame = _lastFrame;
                Apply(snapshot);
                UpdateComponentReadings(frame);
            }
        });
    }

    private void UpdateMotion()
    {
        SceneAnimating = AnimationAllowed(IsLoaded, IsVisible, _model?.MotionEnabled == true, _windowsAnimations, _shutdown);
        if (!_shutdown && IsLoaded && IsVisible) _readoutTimer.Start(); else _readoutTimer.Stop();
        FlowAnimating = SceneAnimating && _flows.Count > 0;
        if (_motionState == SceneAnimating) return;
        _motionState = SceneAnimating;
        if (SceneAnimating) AnimationStarts++;
        foreach (var flow in _flows)
            flow.BeginAnimation(Shape.StrokeDashOffsetProperty, FlowAnimating
                ? new DoubleAnimation(0,
                    flow.Tag is string { } flowTag && flowTag.StartsWith("modern-schema-flow:", StringComparison.Ordinal)
                        ? -8 / flow.StrokeThickness : -40 / flow.StrokeThickness,
                    flow.Tag is string { } durationTag && durationTag.StartsWith("modern-schema-flow:", StringComparison.Ordinal)
                        ? TimeSpan.FromSeconds(7) : TimeSpan.FromSeconds(2.4)) { RepeatBehavior = RepeatBehavior.Forever,
                    AutoReverse = flow.Tag is string tag && tag.Contains("callout-route:", StringComparison.Ordinal) }
                : null);
        foreach (var indicator in _modernIndicators)
            indicator.BeginAnimation(UIElement.OpacityProperty, SceneAnimating
                ? new DoubleAnimation(0.55, 1, TimeSpan.FromSeconds(2.8))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
                : null);
        foreach (var fan in _fans)
            fan.BeginAnimation(RotateTransform.AngleProperty, SceneAnimating
                ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(18)) { RepeatBehavior = RepeatBehavior.Forever } : null);
        _gridMotion?.BeginAnimation(ScaleTransform.ScaleYProperty, SceneAnimating
            ? new DoubleAnimation(1, 1 / 1.22, TimeSpan.FromSeconds(3)) { RepeatBehavior = RepeatBehavior.Forever } : null);
        for (var index = 0; index < _binaryRows.Count; index++)
        {
            var row = _binaryRows[index];
            row.BeginAnimation(TranslateTransform.XProperty, SceneAnimating
                ? new DoubleAnimation(0, -BinaryPeriod, TimeSpan.FromSeconds(34 + index * 2))
                    { RepeatBehavior = RepeatBehavior.Forever } : null);
        }
        foreach (var (stop, alternate) in _skyColors)
        {
            stop.BeginAnimation(GradientStop.ColorProperty, SceneAnimating
                ? new ColorAnimation(stop.Color, alternate, TimeSpan.FromSeconds(29 + stop.Offset * 17))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
            var offset = (double)stop.GetAnimationBaseValue(GradientStop.OffsetProperty);
            stop.BeginAnimation(GradientStop.OffsetProperty, SceneAnimating && offset is > 0 and < 1
                ? new DoubleAnimation(offset, Math.Min(.99, offset + .04), TimeSpan.FromSeconds(37))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        }
        foreach (var (brush, alternate) in _routeColors)
            brush.BeginAnimation(SolidColorBrush.ColorProperty, SceneAnimating
                ? new ColorAnimation(brush.Color, alternate, TimeSpan.FromSeconds(11))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        _horizonGlow?.BeginAnimation(RadialGradientBrush.CenterProperty, SceneAnimating
            ? new PointAnimation(new Point(.25, .35), new Point(.75, .65), TimeSpan.FromSeconds(23))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        _horizonGlow?.BeginAnimation(RadialGradientBrush.GradientOriginProperty, SceneAnimating
            ? new PointAnimation(new Point(.25, .35), new Point(.75, .65), TimeSpan.FromSeconds(23))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        _sceneDrift.BeginAnimation(TranslateTransform.XProperty, SceneAnimating
            ? new DoubleAnimation(-8, 8, TimeSpan.FromSeconds(41)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        _sceneDrift.BeginAnimation(TranslateTransform.YProperty, SceneAnimating
            ? new DoubleAnimation(-5, 5, TimeSpan.FromSeconds(53)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
    }

    private void ClearMotion()
    {
        _motionState = null;
        foreach (var flow in _flows) flow.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        foreach (var indicator in _modernIndicators) indicator.BeginAnimation(UIElement.OpacityProperty, null);
        foreach (var fan in _fans) fan.BeginAnimation(RotateTransform.AngleProperty, null);
        foreach (var row in _binaryRows) row.BeginAnimation(TranslateTransform.XProperty, null);
        _gridMotion?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        foreach (var (stop, _) in _skyColors)
        {
            stop.BeginAnimation(GradientStop.ColorProperty, null);
            stop.BeginAnimation(GradientStop.OffsetProperty, null);
        }
        foreach (var (brush, _) in _routeColors) brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        _horizonGlow?.BeginAnimation(RadialGradientBrush.CenterProperty, null);
        _horizonGlow?.BeginAnimation(RadialGradientBrush.GradientOriginProperty, null);
        _horizonGlow = null;
        _skyColors.Clear(); _routeColors.Clear();
        _flows.Clear(); _fans.Clear(); _binaryRows.Clear(); _modernIndicators.Clear(); _gridMotion = null;
    }

    internal void UseScreensaverLayout()
    {
        _screensaverLayout = true;
        PresentationHeader.Visibility = Visibility.Collapsed;
        DeviceDetails.Visibility = Visibility.Collapsed;
        BoardScroller.Content = null;
        BoardWorkspace.Visibility = Visibility.Collapsed;
        var viewport = new Grid { Margin = new Thickness(12), ClipToBounds = true };
        viewport.SizeChanged += (_, e) => ConfigureViewport(e.NewSize);
        viewport.Children.Add(BoardScale);
        Grid.SetRow(viewport, 1);
        ((Grid)Content).Children.Add(viewport);
        BoardScale.VerticalAlignment = VerticalAlignment.Center;
        BoardScale.StretchDirection = StretchDirection.Both;
        if (Snapshot is { } snapshot) Apply(snapshot);
    }

    internal void ConfigureViewport(Size size)
    {
        if (!_screensaverLayout || size.Width <= 0 || size.Height <= 0) return;
        var aspect = size.Width / size.Height;
        if (!double.IsFinite(aspect) || Math.Abs(aspect - _viewportAspect) < .01) return;
        _viewportAspect = aspect;
        if (Snapshot is { } snapshot)
        {
            var frame = _lastFrame;
            Apply(snapshot);
            UpdateComponentReadings(frame);
        }
    }

    internal void UpdateComponentReadings(BoardFeedFrame? frame)
    {
        if (frame is not null && !BoardLiveFeed.IsFresh(frame, DateTimeOffset.UtcNow)) frame = null;
        if (_screensaverLayout && frame?.Sensors is { } sensors)
        {
            var disks = Groups.Single(g => g.Key == "storage").Devices
                .Where(d => d.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase)).ToArray();
            var names = disks.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (Snapshot is { } snapshot)
                names.ExceptWith(snapshot.Devices.Where(d => d.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase) &&
                    !disks.Contains(d)).Select(d => d.Name));
            frame = frame with { Sensors = sensors.Where(s => !SensorSemantics.IsStorage(s.Id) ||
                names.Contains(s.Hardware)).ToArray() };
        }
        foreach (var (key, text) in _componentReadings)
            text.Text = BoardComponentReadings.Describe(key, frame);
        if (!ReferenceEquals(_lastFrame, frame) || _dialMetrics.Count == 0)
        {
            _dialMetrics.Clear();
            foreach (var key in _componentDials.Keys) _dialMetrics[key] = BoardComponentReadings.Metrics(key, frame);
        }
        _lastFrame = frame;
        UpdateDials(_sceneTime.Elapsed);
    }

    private void RefreshReadouts()
    {
        if (!_screensaverLayout && _model is { } model && Snapshot is { } hardware)
            UpdateComponentReadings(model.LastSampleAt != DateTimeOffset.MinValue ? new BoardFeedFrame(
                model.LastSampleAt, hardware, model.Cards[0].Value, model.Cards[1].Value, model.Cards[2].Value,
                model.Cards[3].Value, double.TryParse(model.SidebarUpload.NumericValue, out var upload) ? upload : null,
                model.CpuTemperature, model.MotionEnabled, model.Sensors.ToArray()) : null);
        else
            UpdateComponentReadings(_lastFrame is { } frame && BoardLiveFeed.IsFresh(frame, DateTimeOffset.UtcNow) ? frame : null);
    }

    internal void UpdateDials(TimeSpan elapsed)
    {
        var slot = Math.Max(0, (long)(elapsed.TotalSeconds / 6));
        foreach (var (key, controls) in _componentDials)
        {
            var metrics = _dialMetrics.GetValueOrDefault(key) ?? [];
            var metric = metrics.Count > 0 ? metrics[(int)(slot / 2 % metrics.Count)] : null;
            controls.Text.Visibility = slot % 2 == 0 ? Visibility.Visible : Visibility.Collapsed;
            controls.Dial.Visibility = slot % 2 == 0 ? Visibility.Collapsed : Visibility.Visible;
            controls.Gauge.Level = metric?.Level ?? double.NaN;
            controls.Name.Text = metric?.Name ?? DesktopUiText.Get("Board.NoReadings");
            controls.Value.Text = metric?.Display ?? "--";
            controls.Dial.ToolTip = metric is null ? DesktopUiText.Get("Board.NoChannel") :
                DesktopUiText.Format("Board.DialScale", metric.Name, metric.Scale, metric.Unit);
        }
    }

    internal void EnsureLoaded()
    {
        if (!_loaded && Snapshot is null && !_busy && !_shutdown) Pending = LoadAsync();
    }

    private void ZoomChanged(object sender, SelectionChangedEventArgs e) => ApplyZoom();

    private void ApplyZoom()
    {
        if (_screensaverLayout || BoardScale is null || BoardZoom.SelectedItem is not ComboBoxItem { Tag: string value }) return;
        var scale = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        BoardScroller.HorizontalScrollBarVisibility = scale == 0 ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        BoardScale.StretchDirection = scale == 0 ? StretchDirection.DownOnly : StretchDirection.Both;
        BoardScale.Width = scale == 0 ? double.NaN : BoardCanvas.Width * scale;
    }

    internal void Shutdown()
    {
        _shutdown = true;
        _stop.Cancel();
        DisconnectModel();
        SystemParameters.StaticPropertyChanged -= WindowsMotionChanged;
        UpdateMotion();
    }

    private void RefreshClicked(object sender, RoutedEventArgs e)
    {
        if (!_busy && !_shutdown) Pending = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _busy = true;
        RefreshButton.IsEnabled = false;
        StatusText.Text = DesktopUiText.Get("Board.Loading");
        try
        {
            Apply(await HardwareTopology.ReadAsync(_stop.Token));
            _loaded = true;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            StatusText.Text = SkinCatalog.IsModern ? DesktopText.Get("Board.ReadCancelled") :
                DesktopUiText.Get("Board.NeonReadCancelled");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or Win32Exception or
            JsonException or TimeoutException or UnauthorizedAccessException)
        {
            Trace.TraceError("Hardware-Schaubild: {0}", ex);
            StatusText.Text = SkinCatalog.IsModern
                ? string.Format(DesktopText.Get("Board.ReadError"), ex.Message)
                : DesktopUiText.Format("Board.NeonReadError", ex.Message);
        }
        finally { _busy = false; RefreshButton.IsEnabled = !_shutdown; }
    }

    internal void Apply(BoardSnapshot snapshot)
    {
        ClearMotion();
        Snapshot = snapshot;
        var visible = _screensaverLayout ? HardwareTopology.ScreensaverSnapshot(snapshot) : snapshot;
        Groups = HardwareTopology.Group(visible);
        _drawnDay = DataContext is CockpitModel { IsDaytime: true };
        BoardCanvas.Children.Clear();
        _componentReadings.Clear();
        _componentDials.Clear();
        _dialMetrics.Clear();
        DrawBoard(visible);
        if (!SkinCatalog.IsModern) AddNeonBloom();
        ApplyZoom();
        UpdateComponentReadings(null);
        StatusText.Text = (SkinCatalog.IsModern
            ? string.Format(DesktopText.Get("Board.SnapshotStatus"), snapshot.Devices.Length, DateTime.Now.ToString("HH:mm:ss"))
            : DesktopUiText.Format("Board.NeonSnapshotStatus", snapshot.Devices.Length, DateTime.Now.ToString("HH:mm:ss"))) +
            (snapshot.Warnings.Length > 0 ? "\n" + string.Join("\n", snapshot.Warnings) : "");
        InternetText.Text = snapshot.Internet;
        var selected = Groups.FirstOrDefault(group => group.Key == _selectedGroup) ??
            Groups.First(group => group.Devices.Count > 0 || group.Key == "other");
        if (_selectedDevice is { } id && selected.Devices.FirstOrDefault(device => device.Id == id) is { } device)
            selected = selected with { Devices = new[] { device } };
        Select(selected);
        UpdateMotion();
    }

    private void Select(BoardGroup group)
    {
        _selectedGroup = group.Key;
        var complete = Groups.FirstOrDefault(item => item.Key == group.Key);
        _selectedDevice = group.Devices.Count == 1 && complete?.Devices.Count > 1 ? group.Devices[0].Id : null;
        DetailTitle.Text = SkinCatalog.IsModern
            ? string.Format(DesktopText.Get("Board.DeviceDetailTitle"),
                DesktopText.Get("Board.Category." + group.Key), group.Devices.Count)
            : DesktopUiText.Format("Board.NeonDetailCount",
                System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en"
                    ? DesktopText.Get("Board.Category." + group.Key) : group.Title, group.Devices.Count);
        DeviceList.ItemsSource = group.Devices;
    }
}
