using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace AetherScreensaver;

internal sealed class SaverConfigurationWindow : Window
{
    internal SaverConfigurationWindow()
    {
        Title = "AETHER / Bildschirmschoner-Einstellungen";
        Width = 600; Height = 330; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(17, 12, 38));
        Foreground = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = "AETHER / MAINFRAME 1986", FontSize = 23, Foreground = Brushes.Cyan });
        panel.Children.Add(new TextBlock
        {
            Text = "Komplettes Mainboard je Bildschirm. Live-Werte nur aus laufendem AETHER.\n" +
                "Windows-Aktivierung und Sperre bleiben in den Windows-Einstellungen.\n\n" +
                "Musik: einmal je Vollbild-Sitzung, Endlosschleife bis zur ersten Eingabe.\n" +
                "Keine Musik in der kleinen Windows-Vorschau. WAV bleibt extern;\n" +
                "sie wird nicht in Setup/ZIP kopiert. Windows-Lautstaerke bleibt unveraendert.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 14)
        });
        var enabled = new CheckBox { Content = "Musik an / aus", Foreground = Brushes.Cyan };
        var settings = new SaverAudioSettings();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gold, Margin = new Thickness(0, 8, 0, 8) };
        var save = new Button { Content = "Speichern und schliessen", Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var control in new UIElement[] { enabled, status, save }) panel.Children.Add(control);
        try
        {
            settings = SaverAudioSettings.Load();
            enabled.IsChecked = settings.Enabled;
            if (settings.Enabled && !File.Exists(settings.Path)) status.Text = "Gespeicherte WAV fehlt. Beim Speichern mit Musik an bitte neu zuordnen.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        { status.Text = "Musikeinstellungen nicht lesbar: " + ex.Message; }
        save.Click += (_, _) =>
        {
            try
            {
                var next = settings with { Enabled = enabled.IsChecked == true };
                if (next.Enabled && !File.Exists(next.Path))
                {
                    var dialog = new OpenFileDialog { Title = "Musik einmalig zuordnen (bleibt extern)",
                        Filter = "Lokale WAV-Datei (*.wav)|*.wav", CheckFileExists = true };
                    if (dialog.ShowDialog(this) != true) { status.Text = "Nicht gespeichert: Musikdatei nicht zugeordnet."; return; }
                    next = next with { Path = dialog.FileName };
                }
                next.Validate();
                next.Save();
                Close();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { status.Text = "Nicht gespeichert: " + ex.Message; }
        };
    }
}
