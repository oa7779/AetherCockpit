using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;

namespace SystemCockpit.Services;

internal static class WindowAppearance
{
    internal static void Apply(Window window)
    {
        if (SkinCatalog.IsModern)
        {
            window.SetResourceReference(Control.BackgroundProperty, "WindowBackdrop");
            window.SetResourceReference(Control.ForegroundProperty, "ModernTextPrimary");
            window.FontFamily = new FontFamily("Segoe UI");
        }
    }

    public static int DarkCaption(Window window)
    {
        var enabled = SkinCatalog.IsModern && SkinCatalog.WindowsModernLight() ? 0 : 1;
        return DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
