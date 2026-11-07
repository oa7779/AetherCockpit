using System.Drawing;

namespace SystemCockpit.Services;

internal static class SidebarPlacement
{
    public static Rectangle Restore(Rectangle workArea, double scale, bool pinned, string side,
        double horizontalOffset, double verticalOffset)
    {
        if (!double.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));
        if (workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea));

        var width = Math.Min(workArea.Width, (int)Math.Round(350 * scale));
        var height = pinned ? workArea.Height : Math.Min(workArea.Height, (int)Math.Round(760 * scale));
        var travelX = workArea.Width - width;
        var travelY = workArea.Height - height;
        var left = pinned
            ? side == "left" ? workArea.Left : workArea.Right - width
            : workArea.Left + (int)Math.Round(travelX * Math.Clamp(horizontalOffset, 0, 1));
        var top = workArea.Top + (int)Math.Round(travelY * Math.Clamp(verticalOffset, 0, 1));
        return new(left, top, width, height);
    }

    public static (double Horizontal, double Vertical) Capture(Rectangle workArea, Rectangle window)
    {
        var travelX = Math.Max(0, workArea.Width - window.Width);
        var travelY = Math.Max(0, workArea.Height - window.Height);
        return (travelX == 0 ? 0 : Math.Clamp((double)(window.Left - workArea.Left) / travelX, 0, 1),
            travelY == 0 ? 0 : Math.Clamp((double)(window.Top - workArea.Top) / travelY, 0, 1));
    }
}
