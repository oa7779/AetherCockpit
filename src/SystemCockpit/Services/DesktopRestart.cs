using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;

namespace SystemCockpit.Services;

internal static class DesktopRestart
{
    private static bool _requested;

    internal static bool Confirm(Window owner)
    {
        var window = new Window
        {
            Owner = owner,
            Title = DesktopText.Get("Safety.RestartNow.Title"),
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        WindowAppearance.Apply(window);
        var content = new StackPanel { Margin = new Thickness(22) };
        content.Children.Add(new TextBlock
        {
            Text = DesktopText.Get("Safety.RestartNow.Body"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 22)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var restart = new Button
        {
            Content = DesktopText.Get("Safety.RestartNow.Confirm"),
            IsDefault = true,
            MinWidth = 120,
            Style = window.TryFindResource("NeonButton") as Style
        };
        var later = new Button
        {
            Content = DesktopText.Get("Safety.RestartNow.Defer"),
            IsCancel = true,
            MinWidth = 100,
            Margin = new Thickness(8, 0, 0, 0),
            Style = window.TryFindResource("NeonButton") as Style
        };
        restart.Click += (_, _) => window.DialogResult = true;
        later.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(restart);
        buttons.Children.Add(later);
        content.Children.Add(buttons);
        window.Content = content;
        return window.ShowDialog() == true;
    }

    internal static void RestartIfConfirmed(bool confirmed, Action restart)
    {
        ArgumentNullException.ThrowIfNull(restart);
        if (confirmed) restart();
    }

    internal static void RestartApplication()
    {
        if (_requested) return;
        _requested = true;
        if (Application.Current.MainWindow is MainWindow mainWindow) mainWindow.RequestExit();
        else Application.Current.Shutdown();
    }

    internal static ProcessStartInfo ReplacementStartInfo(string executable, IReadOnlyList<string> commandLine)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (System.IO.Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            commandLine.Count > 0 && commandLine[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(commandLine[0]);
        foreach (var argument in commandLine.Skip(1)) start.ArgumentList.Add(argument);
        return start;
    }

    internal static void StartReplacementAfterExit()
    {
        if (!_requested || string.IsNullOrWhiteSpace(Environment.ProcessPath)) return;
        try
        {
            Process.Start(ReplacementStartInfo(Environment.ProcessPath, Environment.GetCommandLineArgs()));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or
            UnauthorizedAccessException)
        {
            Trace.TraceError("AETHER restart launch failed: {0}", ex);
        }
        finally { _requested = false; }
    }
}
