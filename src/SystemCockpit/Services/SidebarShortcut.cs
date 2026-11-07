namespace SystemCockpit.Services;

public sealed record SidebarShortcut(string Name, string Path, string Category);

public sealed record SidebarDisplay(string DeviceName, int Left, int Top, int Right, int Bottom,
    int WorkLeft, int WorkTop, int WorkRight, int WorkBottom, bool IsPrimary, IntPtr Handle)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int WorkWidth => WorkRight - WorkLeft;
    public int WorkHeight => WorkBottom - WorkTop;
    public System.Drawing.Rectangle WorkArea =>
        System.Drawing.Rectangle.FromLTRB(WorkLeft, WorkTop, WorkRight, WorkBottom);
}
