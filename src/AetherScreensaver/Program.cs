using System.Windows;
using SystemCockpit.Services;

namespace AetherScreensaver;

internal enum SaverMode { Configure, Fullscreen, Preview }
internal sealed record SaverArguments(SaverMode Mode, nint Parent = 0)
{
    internal static SaverArguments Parse(string[] args)
    {
        if (args.Length == 0) return new(SaverMode.Configure);
        var parts = args[0].Split(':', 2);
        var mode = parts[0].ToLowerInvariant();
        if (mode is "/s" or "-s" && args.Length == 1) return new(SaverMode.Fullscreen);
        if (mode is "/c" or "-c") return new(SaverMode.Configure);
        if (mode is "/p" or "-p")
        {
            var value = parts.Length == 2 ? parts[1] : args.ElementAtOrDefault(1);
            if (long.TryParse(value, out var handle) && handle > 0) return new(SaverMode.Preview, checked((nint)handle));
        }
        throw new ArgumentException(DesktopUiText.Get("Saver.InvalidArguments"));
    }
}

internal static class Program
{
    internal static string ConfigurationMessage => DesktopUiText.Get("Saver.Configuration");

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener(true));
            System.Diagnostics.Trace.AutoFlush = true;
            if (!DesktopScreensaverPolicy.TryLoadCurrentUser(out var settings)) return 0;
            DesktopText.ConfigureSupportedUi(settings.Language);
            var options = SaverArguments.Parse(args);
            if (!DesktopScreensaverPolicy.IsEnabled(settings))
            {
                System.Diagnostics.Trace.TraceInformation("AETHER screensaver disabled by Windows Modern desktop preference.");
                if (options.Mode == SaverMode.Configure)
                    MessageBox.Show(DesktopText.Get("Screensaver.ModernDisabled"), DesktopUiText.Get("Saver.Title"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            }
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var source in new[] { "Controls/NeonInputs.xaml", "Controls/NeonGlass.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/SystemCockpit;component/{source}")
                });
            if (options.Mode == SaverMode.Configure)
            {
                MessageBox.Show(ConfigurationMessage, DesktopUiText.Get("Saver.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            }
            using var session = new SaverSession(app, options);
            app.Dispatcher.BeginInvoke(session.Start);
            return app.Run();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
            System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or System.Security.SecurityException)
        {
            System.Diagnostics.Trace.TraceError("AETHER Bildschirmschoner: {0}", ex);
            MessageBox.Show(ex.Message, DesktopUiText.Get("Saver.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
