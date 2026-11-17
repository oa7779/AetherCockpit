using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SystemCockpit.Controls;

public partial class HardwareBoardView
{
    private void AddNeonBloom()
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            foreach (var shape in BoardCanvas.Children.OfType<Shape>())
            {
                if (shape.Stroke is null || shape.Tag is string tag &&
                    tag.StartsWith("neon-halo:", StringComparison.Ordinal)) continue;
                Geometry? geometry = shape switch
                {
                    Line line => new LineGeometry(new(line.X1, line.Y1), new(line.X2, line.Y2)),
                    Polyline line => Outline(line.Points, false),
                    Polygon face => Outline(face.Points, true),
                    _ => null
                };
                if (geometry is null) continue;
                var brush = shape.Stroke.CloneCurrentValue();
                brush.Opacity = .7;
                var left = Canvas.GetLeft(shape);
                var top = Canvas.GetTop(shape);
                context.PushTransform(new TranslateTransform(double.IsFinite(left) ? left : 0,
                    double.IsFinite(top) ? top : 0));
                context.PushTransform(shape.RenderTransform.CloneCurrentValue());
                context.DrawGeometry(null, new Pen(brush, Math.Max(3, shape.StrokeThickness + 2)), geometry);
                context.Pop();
                context.Pop();
            }
        }
        drawing.Freeze();
        var bloom = new Image
        {
            Source = new DrawingImage(drawing), Stretch = Stretch.Fill,
            Width = drawing.Bounds.Width, Height = drawing.Bounds.Height,
            IsHitTestVisible = false, Tag = "board-neon-bloom",
            Effect = new BlurEffect { Radius = 9, RenderingBias = RenderingBias.Performance },
            CacheMode = new BitmapCache(), Opacity = .9
        };
        Canvas.SetLeft(bloom, drawing.Bounds.Left);
        Canvas.SetTop(bloom, drawing.Bounds.Top);
        BoardCanvas.Children.Add(bloom);

        static Geometry Outline(PointCollection points, bool closed)
        {
            var geometry = new StreamGeometry();
            if (points.Count == 0) return geometry;
            using var context = geometry.Open();
            context.BeginFigure(points[0], false, closed);
            context.PolyLineTo(points.Skip(1).ToArray(), true, false);
            return geometry;
        }
    }
}
