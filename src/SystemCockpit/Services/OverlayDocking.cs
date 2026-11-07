using System.Drawing;

namespace SystemCockpit.Services;

internal readonly record struct OverlayDockResult(int Left, int Top, string Side, double VerticalOffset);

internal static class OverlayDocking
{
    public static (int Left, int Top) Restore(Rectangle workArea, Size windowSize, string side, double verticalOffset)
    {
        var maxTop = Math.Max(workArea.Top, workArea.Bottom - windowSize.Height);
        var top = workArea.Top + (int)Math.Round((maxTop - workArea.Top) * Math.Clamp(verticalOffset, 0, 1));
        var left = side == "left" ? workArea.Left : workArea.Right - windowSize.Width;
        return (left, top);
    }

    public static OverlayDockResult Snap(Rectangle workArea, Rectangle window)
    {
        var leftDistance = Math.Abs(window.Left - workArea.Left);
        var rightDistance = Math.Abs(workArea.Right - window.Right);
        var side = leftDistance <= rightDistance ? "left" : "right";
        var maxTop = Math.Max(workArea.Top, workArea.Bottom - window.Height);
        var top = Math.Clamp(window.Top, workArea.Top, maxTop);
        var travel = maxTop - workArea.Top;
        var offset = travel == 0 ? 0 : (double)(top - workArea.Top) / travel;
        var left = side == "left" ? workArea.Left : workArea.Right - window.Width;
        return new(left, top, side, offset);
    }
}
