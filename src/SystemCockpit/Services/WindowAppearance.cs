using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SystemCockpit.Services;

internal static class WindowAppearance
{
    public static int DarkCaption(Window window)
    {
        var enabled = 1;
        return DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
