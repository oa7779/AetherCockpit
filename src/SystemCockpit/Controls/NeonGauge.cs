using System.Windows;
using System.Windows.Media;

namespace SystemCockpit.Controls;

public sealed class NeonGauge : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(NeonGauge), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(NeonGauge), new FrameworkPropertyMetadata(Brushes.Turquoise, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    // Eingefrorene, wiederverwendete Stifte statt Neuanlage pro OnRender:
    // vermeidet GC-Druck im regelmaessig neu gezeichneten Renderpfad.
    private static readonly Pen DimLine = CreateDimPen(1);
    private static readonly Pen DimTick = CreateDimPen(4);
    private Pen? _accentPen;
    private Pen? _haloPen;
    private Pen? _bloomPen;

    private static Pen CreateDimPen(double thickness)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(45, 34, 72)), thickness);
        pen.Freeze();
        return pen;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 8);
        if (radius <= 15) return;
        if (_accentPen is null || !ReferenceEquals(_accentPen.Brush, Accent))
        {
            _accentPen = new Pen(Accent, 4);
            if (_accentPen.CanFreeze) _accentPen.Freeze();
            _haloPen = GlowPen(Accent, 10, .12);
            _bloomPen = GlowPen(Accent, 7, .3);
        }
        var lit = double.IsFinite(Level) ? (int)Math.Ceiling(Math.Clamp(Level, 0, 100) / 100 * 40) : 0;
        dc.DrawEllipse(null, DimLine, center, radius - 15, radius - 15);
        for (var i = 0; i < 40; i++)
        {
            var angle = (140 + i * 260d / 39) * Math.PI / 180;
            Point At(double distance) => new(center.X + Math.Cos(angle) * distance, center.Y + Math.Sin(angle) * distance);
            if (i < lit)
            {
                dc.DrawLine(_haloPen, At(radius - 9), At(radius));
                dc.DrawLine(_bloomPen, At(radius - 9), At(radius));
            }
            dc.DrawLine(i < lit ? _accentPen : DimTick, At(radius - 9), At(radius));
        }
        dc.DrawLine(DimLine, new(center.X - radius / 2, center.Y + radius / 2),
            new(center.X + radius / 2, center.Y + radius / 2));
    }

    private static Pen GlowPen(Brush accent, double thickness, double opacity)
    {
        var brush = accent.CloneCurrentValue();
        brush.Opacity *= opacity;
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
}
