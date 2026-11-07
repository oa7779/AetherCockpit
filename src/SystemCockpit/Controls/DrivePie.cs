using System.Windows;
using System.Windows.Media;

namespace SystemCockpit.Controls;

public sealed class DrivePie : FrameworkElement
{
    public static readonly DependencyProperty FreePercentProperty = DependencyProperty.Register(
        nameof(FreePercent), typeof(double?), typeof(DrivePie),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public double? FreePercent { get => (double?)GetValue(FreePercentProperty); set => SetValue(FreePercentProperty, value); }

    private static readonly Brush FreeBrush = FrozenBrush("#24FFE3");
    private static readonly Brush UsedBrush = FrozenBrush("#A84FFF");
    private static readonly Brush UnknownBrush = FrozenBrush("#40374F");
    private static readonly Pen Outline = new(FrozenBrush("#72608F"), 1);

    private static Brush FrozenBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    internal static Geometry UsedSector(double freePercent)
    {
        if (!double.IsFinite(freePercent) || freePercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(freePercent));
        if (freePercent == 100) return Geometry.Empty;
        if (freePercent == 0) return new EllipseGeometry(new Point(0.5, 0.5), 0.5, 0.5);
        var angle = (100 - freePercent) / 100 * 2 * Math.PI - Math.PI / 2;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0.5, 0.5), true, true);
            context.LineTo(new Point(0.5, 0), true, false);
            context.ArcTo(new Point(0.5 + 0.5 * Math.Cos(angle), 0.5 + 0.5 * Math.Sin(angle)),
                new Size(0.5, 0.5), 0, freePercent < 50, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var diameter = Math.Max(0, Math.Min(ActualWidth, ActualHeight) - 4);
        if (diameter == 0) return;
        var x = (ActualWidth - diameter) / 2;
        var y = (ActualHeight - diameter) / 2;
        var available = FreePercent is { } free && double.IsFinite(free) && free is >= 0 and <= 100;
        dc.DrawEllipse(available ? FreeBrush : UnknownBrush, Outline,
            new Point(ActualWidth / 2, ActualHeight / 2), diameter / 2, diameter / 2);
        if (!available) return;
        dc.PushTransform(new MatrixTransform(diameter, 0, 0, diameter, x, y));
        dc.DrawGeometry(UsedBrush, null, UsedSector(FreePercent!.Value));
        dc.Pop();
    }
}
