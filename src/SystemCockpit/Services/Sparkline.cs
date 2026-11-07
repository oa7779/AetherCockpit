using System.Windows;
using System.Windows.Media;

namespace SystemCockpit.Services;

public static class Sparkline
{
    public static PointCollection Create(IReadOnlyList<double> values, double scale, double width = 280, double height = 56)
    {
        if (scale <= 0 || !double.IsFinite(scale)) throw new ArgumentOutOfRangeException(nameof(scale));
        var points = new PointCollection();
        for (var i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i])) throw new ArgumentException("Verlauf enthaelt ungueltige Werte.", nameof(values));
            points.Add(new Point(values.Count == 1 ? width : i * width / (values.Count - 1),
                height * (1 - Math.Clamp(values[i] / scale, 0, 1))));
        }
        points.Freeze();
        return points;
    }
}
