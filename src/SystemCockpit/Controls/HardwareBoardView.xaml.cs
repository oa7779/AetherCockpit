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

    public HardwareBoardView()
    {
        InitializeComponent();
        BoardScale.RenderTransform = _sceneDrift;
        _readoutTimer.Tick += (_, _) => RefreshReadouts();
        DataContextChanged += (_, _) => ConnectModel();
        Loaded += (_, _) => { ConnectModel(); UpdateMotion(); };
        Unloaded += (_, _) => { DisconnectModel(); UpdateMotion(); };
        IsVisibleChanged += (_, _) => UpdateMotion();
    }

    private void ConnectModel()
    {
        DisconnectModel();
        if (!_shutdown && IsLoaded && DataContext is CockpitModel model)
        {
            _model = model;
            _model.PropertyChanged += ModelChanged;
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
    }

    private void UpdateMotion()
    {
        SceneAnimating = !_shutdown && IsLoaded && IsVisible && _model?.MotionEnabled == true;
        if (!_shutdown && IsLoaded && IsVisible) _readoutTimer.Start(); else _readoutTimer.Stop();
        FlowAnimating = SceneAnimating && _flows.Count > 0;
        foreach (var flow in _flows)
            flow.BeginAnimation(Shape.StrokeDashOffsetProperty, FlowAnimating
                ? new DoubleAnimation(0, -40 / flow.StrokeThickness, TimeSpan.FromSeconds(2.4)) { RepeatBehavior = RepeatBehavior.Forever,
                    AutoReverse = flow.Tag is string tag && tag.Contains("callout-route:", StringComparison.Ordinal) }
                : null);
        foreach (var fan in _fans)
            fan.BeginAnimation(RotateTransform.AngleProperty, SceneAnimating
                ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(18)) { RepeatBehavior = RepeatBehavior.Forever } : null);
        _gridMotion?.BeginAnimation(ScaleTransform.ScaleYProperty, SceneAnimating
            ? new DoubleAnimation(1, 1 / 1.22, TimeSpan.FromSeconds(3)) { RepeatBehavior = RepeatBehavior.Forever } : null);
        foreach (var row in _binaryRows)
            row.BeginAnimation(TranslateTransform.XProperty, SceneAnimating
                ? new DoubleAnimation(0, -BinaryPeriod, TimeSpan.FromSeconds(34 + _binaryRows.IndexOf(row) * 2))
                    { RepeatBehavior = RepeatBehavior.Forever } : null);
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
        foreach (var flow in _flows) flow.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
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
        _flows.Clear(); _fans.Clear(); _binaryRows.Clear(); _gridMotion = null;
    }

    internal void UseScreensaverLayout()
    {
        _screensaverLayout = true;
        PresentationHeader.Visibility = Visibility.Collapsed;
        DeviceDetails.Visibility = Visibility.Collapsed;
        ((Panel)BoardScale.Parent).Children.Remove(BoardScale);
        BoardScroller.Visibility = Visibility.Collapsed;
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
            var metrics = BoardComponentReadings.Metrics(key, _lastFrame);
            var metric = metrics.Count > 0 ? metrics[(int)(slot / 2 % metrics.Count)] : null;
            controls.Text.Visibility = slot % 2 == 0 ? Visibility.Visible : Visibility.Collapsed;
            controls.Dial.Visibility = slot % 2 == 0 ? Visibility.Collapsed : Visibility.Visible;
            controls.Gauge.Level = metric?.Level ?? double.NaN;
            controls.Name.Text = metric?.Name ?? "Keine Messwerte verfuegbar";
            controls.Value.Text = metric?.Display ?? "--";
            controls.Dial.ToolTip = metric is null ? "Kein zuordenbarer Messkanal" :
                $"{metric.Name}\nSkala 0 bis {metric.Scale:0.##} {metric.Unit}; keine gemessene Grenze.";
        }
    }

    internal void EnsureLoaded()
    {
        if (!_loaded && !_busy && !_shutdown) Pending = LoadAsync();
    }

    internal void Shutdown()
    {
        _shutdown = true;
        _stop.Cancel();
        DisconnectModel();
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
        StatusText.Text = "Windows-Hardware und vorhandene Komponenten werden rein lesend erfasst ...";
        try
        {
            Apply(await HardwareTopology.ReadAsync(_stop.Token));
            _loaded = true;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            StatusText.Text = "Hardware-Abfrage abgebrochen.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or Win32Exception or
            JsonException or TimeoutException or UnauthorizedAccessException)
        {
            Trace.TraceError("Hardware-Schaubild: {0}", ex);
            StatusText.Text = "Hardware-Schaubild nicht aktualisiert: " + ex.Message;
        }
        finally { _busy = false; RefreshButton.IsEnabled = !_shutdown; }
    }

    internal void Apply(BoardSnapshot snapshot)
    {
        ClearMotion();
        Snapshot = snapshot;
        var visible = _screensaverLayout ? HardwareTopology.ScreensaverSnapshot(snapshot) : snapshot;
        Groups = HardwareTopology.Group(visible);
        BoardCanvas.Children.Clear();
        _componentReadings.Clear();
        _componentDials.Clear();
        DrawBoard(visible);
        UpdateComponentReadings(null);
        StatusText.Text = $"{snapshot.Devices.Length} Windows-Geraeteeintraege inkl. Audio-Endpunktstatus / Momentaufnahme {DateTime.Now:HH:mm:ss}" +
            (snapshot.Warnings.Length > 0 ? "\n" + string.Join("\n", snapshot.Warnings) : "");
        InternetText.Text = snapshot.Internet;
        Select(Groups.First(group => group.Devices.Count > 0 || group.Key == "other"));
        UpdateMotion();
    }

    private void Select(BoardGroup group)
    {
        DetailTitle.Text = $"{group.Title} / {group.Devices.Count} WINDOWS-EINTRAEGE";
        DeviceList.ItemsSource = group.Devices;
    }
}
