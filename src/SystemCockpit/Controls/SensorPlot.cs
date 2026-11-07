using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public sealed class SensorPlot : FrameworkElement
{
    public static readonly DependencyProperty GroupProperty = DependencyProperty.Register(nameof(Group),
        typeof(SensorChartGroup), typeof(SensorPlot), new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender, GroupChanged));
    public SensorChartGroup? Group { get => (SensorChartGroup?)GetValue(GroupProperty); set => SetValue(GroupProperty, value); }
    private SensorChartGroup? _subscribed;

    public SensorPlot()
    {
        Height = 225;
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    private static void GroupChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((SensorPlot)sender).Subscribe();
    private void Subscribe()
    {
        Unsubscribe();
        if (IsLoaded && Group is { } group) { _subscribed = group; group.PropertyChanged += Changed; }
        InvalidateVisual();
    }
    private void Unsubscribe()
    {
        if (_subscribed is not null) _subscribed.PropertyChanged -= Changed;
        _subscribed = null;
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    internal static IReadOnlyList<IReadOnlyList<Point>> Segments(IReadOnlyList<SensorChartPoint> points,
        DateTimeOffset start, DateTimeOffset end, double minimum, double maximum, Rect plot)
    {
        var result = new List<IReadOnlyList<Point>>();
        List<Point>? segment = null;
        var seconds = Math.Max(.001, (end - start).TotalSeconds);
        foreach (var point in points.Where(p => p.Time >= start && p.Time <= end))
        {
            if (point.Value is not { } value || !double.IsFinite(value)) { segment = null; continue; }
            if (segment is null) { segment = []; result.Add(segment); }
            segment.Add(new(plot.Left + plot.Width * (point.Time - start).TotalSeconds / seconds,
                plot.Bottom - plot.Height * (value - minimum) / (maximum - minimum)));
        }
        return result;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Group is not { } group || ActualWidth < 150) return;
        var plot = new Rect(76, 15, Math.Max(1, ActualWidth - 94), 170);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(8, 7, 23)), null, new Rect(RenderSize));
        var selected = group.Series.Where(s => s.IsSelected && s.MatchesFilter).ToArray();
        var values = selected.SelectMany(s => s.Points).Where(p => p.Time >= group.Start)
            .Select(p => p.Value).OfType<double>().Where(double.IsFinite).ToArray();
        var minimum = values.Length > 0 ? values.Min() : 0;
        var maximum = values.Length > 0 ? values.Max() : 1;
        var padding = Math.Max(.01, Math.Max(maximum - minimum, Math.Abs(maximum) * .1) * .1);
        minimum -= padding; maximum += padding;
        for (var index = 0; index <= 4; index++)
        {
            var y = plot.Top + index * plot.Height / 4;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(43, 30, 66)), 1), new(plot.Left, y), new(plot.Right, y));
            Label(dc, (maximum - index * (maximum - minimum) / 4).ToString("0.##", CultureInfo.CurrentCulture),
                2, y - 7, Brushes.LightSteelBlue, 11);
        }
        for (var index = 0; index <= 6; index++)
        {
            var x = plot.Left + index * plot.Width / 6;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(29, 22, 46)), 1), new(x, plot.Top), new(x, plot.Bottom));
        }
        dc.PushClip(new RectangleGeometry(plot));
        foreach (var series in selected)
            foreach (var segment in Segments(series.Points, group.Start, group.End, minimum, maximum, plot))
            {
                if (segment.Count > 1)
                {
                    var geometry = new StreamGeometry();
                    using (var context = geometry.Open())
                    {
                        context.BeginFigure(segment[0], false, false);
                        context.PolyLineTo(segment.Skip(1).ToArray(), true, false);
                    }
                    dc.DrawGeometry(null, new Pen(series.Accent, 1.6), geometry);
                }
                foreach (var point in segment) dc.DrawEllipse(series.Accent, null, point, 1.7, 1.7);
            }
        dc.Pop();
        Label(dc, group.Start.ToLocalTime().ToString("HH:mm:ss"), plot.Left, 192, Brushes.LightSteelBlue, 11);
        Label(dc, group.End.ToLocalTime().ToString("HH:mm:ss"), plot.Right - 67, 192, Brushes.LightSteelBlue, 11);
        Label(dc, group.Unit.Length == 0 ? "ohne Einheit / eigene Skala" : group.Unit + " / eigene Skala",
            plot.Left + 90, 192, Brushes.Turquoise, 11);
        if (values.Length == 0)
            Label(dc, selected.Length == 0 ? "Sensoren unten auswaehlen" : "Keine verfuegbaren Messwerte im Verlauf",
                plot.Left + 12, 86, Brushes.Gold, 13);
    }

    private void Label(DrawingContext dc, string text, double x, double y, Brush brush, int size) =>
        dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, y));
}
