using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace SystemCockpit.Controls;

public partial class HardwareBoardView
{
    private LinearGradientBrush? _skyBrush;
    private RadialGradientBrush? _horizonGlow;

    private T AnimateHorizon<T>(T brush, params string[] colors) where T : GradientBrush
    {
        for (var index = 0; index < brush.GradientStops.Count; index++)
            _skyColors.Add((brush.GradientStops[index], Ink(colors[index]).Color));
        return brush;
    }

    private Brush LabelBackground(double y)
    {
        if (_skyBrush is null) return Ink("#080717");
        var brush = _skyBrush.Clone();
        brush.MappingMode = BrushMappingMode.Absolute;
        brush.StartPoint = new(0, -y);
        brush.EndPoint = new(0, _skyBrush.EndPoint.Y - y);
        AnimateSky(brush);
        return brush;
    }

    private void AnimateSky(LinearGradientBrush brush)
    {
        Color[] alternate = DataContext is CockpitModel { IsDaytime: true }
            ? [Ink("#183254").Color, Ink("#275078").Color, Ink("#28779B").Color, Ink("#885BA8").Color, Ink("#FFA87A").Color]
            : [Ink("#100928").Color, Ink("#17114F").Color, Ink("#123C83").Color,
            Ink("#6B22A9").Color, Ink("#F24DAA").Color];
        for (var index = 0; index < brush.GradientStops.Count; index++)
            _skyColors.Add((brush.GradientStops[index], alternate[index % alternate.Length]));
    }

    private static void NeonPath(Panel parent, Geometry geometry, Brush stroke, double thickness, Brush? fill = null)
    {
        if (fill is not null)
            parent.Children.Add(new Path { Data = geometry, Fill = fill, IsHitTestVisible = false });
        // Layered vector strokes avoid full-surface blur intermediates on every animated frame.
        foreach (var (spread, opacity) in new[] { (8.0, .08), (4.0, .18), (0.0, 1.0) })
            parent.Children.Add(new Path { Data = geometry, Stroke = stroke, StrokeThickness = thickness + spread,
                Opacity = opacity, IsHitTestVisible = false });
    }

    private void PrepareSky(double horizon)
    {
        var day = DataContext is CockpitModel { IsDaytime: true };
        _skyBrush = new LinearGradientBrush(new GradientStopCollection {
                new(Ink(day ? "#1C3456" : "#03051B").Color, 0), new(Ink(day ? "#314B83" : "#10064A").Color, .45),
                new(Ink(day ? "#546BA3" : "#3B087C").Color, .77), new(Ink(day ? "#CE77B3" : "#C80CB9").Color, .94),
                new(Ink(day ? "#FFB57B" : "#FF7066").Color, 1) }, new Point(0, 0), new Point(0, horizon)) {
                    MappingMode = BrushMappingMode.Absolute };
        AnimateSky(_skyBrush);
    }

    private void DrawSynthwaveLandscape(double horizon)
    {
        var width = BoardCanvas.Width;
        var sky = new Rectangle { Width = width, Height = horizon + 1, Tag = "synthwave-sky",
            Fill = _skyBrush,
            IsHitTestVisible = false };
        Place(sky, 0, 0);
        Panel.SetZIndex(sky, -5);

        var ground = new Canvas { Width = width, Height = _screensaverLayout ? Math.Max(280, BoardCanvas.Height - horizon) : 280,
            ClipToBounds = true,
            IsHitTestVisible = false, Tag = "synthwave-ground",
            Background = AnimateHorizon(new LinearGradientBrush(Ink("#152B82").Color, Ink("#07051F").Color, 90),
                "#382064", "#071F29") };
        _gridMotion = new ScaleTransform(1, 1);
        var gridLines = new Canvas();
        var gridInk = new LinearGradientBrush(new GradientStopCollection {
            new(Ink("#00DFFF").Color, 0), new(Ink("#2B8DFF").Color, .28),
            new(Ink("#FF21DC").Color, .5), new(Ink("#2B8DFF").Color, .72),
            new(Ink("#00DFFF").Color, 1) }, new Point(0, 0), new Point(width, 0)) {
                MappingMode = BrushMappingMode.Absolute };
        AnimateHorizon(gridInk, "#D765FF", "#24CDBB", "#4875EC", "#24CDBB", "#D765FF");
        var depth = Math.Max(350, ground.Height * 1.25);
        for (var x = -width; x <= width * 2; x += 100)
        {
            var delta = x - width / 2;
            var fraction = Math.Min(ground.Height / depth, delta == 0 ? 1 : width / 2 / Math.Abs(delta));
            var ray = new LineGeometry(new(width / 2, 0), new(width / 2 + delta * fraction, depth * fraction));
            ray.Freeze();
            // Separate rays avoid WPF's expensive stroke-union tessellation at their common vanishing point.
            NeonPath(gridLines, ray, gridInk, 1.4);
        }
        ground.Children.Add(gridLines);
        // Move only crossbars: scaling the rays changes their angle and makes the road sway.
        var crossbars = new Canvas { RenderTransform = _gridMotion, Tag = "road-crossbars" };
        var rows = new StreamGeometry();
        using (var context = rows.Open())
            for (var y = 7.0; y < Math.Max(430, ground.Height * 1.5); y *= 1.22)
            {
                context.BeginFigure(new(0, y), false, false);
                context.LineTo(new(width, y), true, false);
            }
        rows.Freeze();
        NeonPath(crossbars, rows, gridInk, 1.4);
        ground.Children.Add(crossbars);
        Place(ground, 0, horizon);
        Panel.SetZIndex(ground, -4);
        DrawBinaryMountains(horizon - 180);

        _horizonGlow = AnimateHorizon(new RadialGradientBrush(new GradientStopCollection {
                new(Ink("#B200DFFF").Color, 0), new(Ink("#4800BFFF").Color, .5),
                new(Colors.Transparent, 1) }), "#8CFF55CE", "#383CFFB9", "#00000000");
        var haze = new Ellipse { Width = width, Height = 65, Tag = "horizon-glow",
            Fill = _horizonGlow, IsHitTestVisible = false };
        Place(haze, 0, horizon - 30);
        Panel.SetZIndex(haze, -2);
    }

    private void DrawBinaryMountains(double y)
    {
        var width = BoardCanvas.Width;
        const double height = 180;
        Point[] left = [new(0, 82), new(width * .045, 38), new(width * .10, 78),
            new(width * .16, 8), new(width * .22, 68), new(width * .28, 48),
            new(width * .34, 120), new(width * .39, 103), new(width * .44, height)];
        Point[] right = left.Reverse().Select(p => new Point(width - p.X, p.Y + (height - p.Y) * .12)).ToArray();
        var outline = new StreamGeometry();
        using (var context = outline.Open())
            foreach (var ridge in new[] { left, right })
            {
                context.BeginFigure(ridge[0], true, true);
                context.PolyLineTo(ridge.Skip(1).Concat(new[] {
                    new Point(ridge[^1].X, height), new Point(ridge[0].X, height) }).ToArray(), true, false);
            }
        outline.Freeze();
        var viewport = new Canvas { Width = width, Height = height, Clip = outline,
            IsHitTestVisible = false, Tag = "binary-mountains" };
        var ridgeInk = Ink("#EE3BFF");
        _routeColors.Add((ridgeInk, Ink("#37D9C8").Color));
        NeonPath(viewport, outline, ridgeInk, 2, AnimateHorizon(new LinearGradientBrush(
            Ink("#2A085E").Color, Ink("#092B61").Color, 90), "#103448", "#342052"));
        var mesh = new StreamGeometry();
        using (var context = mesh.Open())
        {
            foreach (var ridge in new[] { left, right })
            {
                foreach (var peak in ridge)
                {
                    context.BeginFigure(peak, false, false);
                    context.LineTo(new(peak.X + (peak.X - width / 2) * .25, height), true, false);
                }
                for (var band = 1; band < 7; band++)
                {
                    var contour = ridge.Select(p => new Point(p.X, p.Y + (height - p.Y) * band / 7)).ToArray();
                    context.BeginFigure(contour[0], false, false);
                    context.PolyLineTo(contour.Skip(1).ToArray(), true, false);
                }
            }
        }
        mesh.Freeze();
        var meshInk = Ink("#27DCFF");
        _routeColors.Add((meshInk, Ink("#DF6EF0").Color));
        NeonPath(viewport, mesh, meshInk, 1);
        var copies = (int)Math.Ceiling(width / BinaryPeriod) + 2;
        for (var index = 0; index < 7; index++)
        {
            var shift = new TranslateTransform();
            _binaryRows.Add(shift);
            var row = new Canvas { RenderTransform = shift };
            for (var copy = 0; copy < copies; copy++)
            {
                var text = new TextBlock { Text = BinarySignature, FontFamily = new FontFamily("Consolas"),
                    FontSize = 14, Foreground = Ink(index % 2 == 0 ? "#59F4FF" : "#F572FF"), Opacity = .65 };
                Canvas.SetLeft(text, copy * BinaryPeriod - index * 19);
                row.Children.Add(text);
            }
            Canvas.SetTop(row, 14 + index * 23);
            viewport.Children.Add(row);
        }
        Place(viewport, 0, y);
        Panel.SetZIndex(viewport, -3);
    }
}
