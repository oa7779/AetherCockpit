using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SystemCockpit.Services;

internal static class WindowActivation
{
    public static void Show(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        if (!SetForegroundWindow(new WindowInteropHelper(window).Handle))
            System.Diagnostics.Trace.TraceInformation("Windows hat die Vordergrundaktivierung begrenzt: {0}", window.Title);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
