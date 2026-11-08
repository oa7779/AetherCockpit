using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SystemCockpit.Services;

namespace SystemCockpit;

internal sealed class DesktopSettingsWindow : Window
{
    private sealed record Choice(string Id, string Name);

    internal DesktopSettingsWindow(CockpitModel model, Action<CockpitSettings> save, Action network)
    {
        Title = DesktopText.Get("Desktop.Settings.Title");
        Width = 680;
        Height = 760;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(12, 9, 32));
        Foreground = Brushes.White;
        WindowAppearance.Apply(this);
        var modern = SkinCatalog.IsModern;
        Brush Accent() => modern ? (Brush)FindResource("ModernAccent") : Brushes.Turquoise;
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = DesktopText.Get("Desktop.Settings.Header"), FontSize = 23, Foreground = Accent() });
        panel.Children.Add(new TextBlock { Text = DesktopText.Get("Desktop.Settings.Copyright"), Margin = new Thickness(0, 8, 0, 16) });
        panel.Children.Add(new TextBlock { Text = DesktopText.Get("Desktop.Settings.Appearance"), Foreground = Accent() });

        var skin = new ComboBox
        {
            ItemsSource = SkinCatalog.All,
            DisplayMemberPath = nameof(SkinInfo.Display),
            SelectedValuePath = nameof(SkinInfo.Id),
            SelectedValue = SkinCatalog.CanonicalId(model.Settings.Skin),
            Margin = new Thickness(0, 6, 0, 6)
        };
        panel.Children.Add(new TextBlock { Text = DesktopText.Get("Desktop.Settings.Skin"), Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(skin);
        panel.Children.Add(new TextBlock
        {
            Text = DesktopText.Get("Skin.WindowsModernDescription"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var language = new ComboBox
        {
            ItemsSource = new[]
            {
                new Choice("de", DesktopText.Get("Language.German"))
            },
            DisplayMemberPath = nameof(Choice.Name),
            SelectedValuePath = nameof(Choice.Id),
            SelectedValue = "de",
            IsEnabled = false,
            Margin = new Thickness(0, 0, 0, 6)
        };
        panel.Children.Add(new TextBlock { Text = DesktopText.Get("Desktop.Settings.Language"), Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(language);
        panel.Children.Add(new TextBlock
        {
            Text = DesktopUiText.Get("Language.ReleaseNotice"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = DesktopText.Get("Desktop.Settings.RestartNote"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        var preferenceStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(preferenceStatus);
        var storingPreferences = false;
        void StorePreferences()
        {
            if (storingPreferences || !IsLoaded || skin.SelectedValue is not string skinId || language.SelectedValue is not string languageId) return;
            storingPreferences = true;
            try
            {
                var next = model.Settings with { Skin = SkinCatalog.CanonicalId(skinId), Language = languageId };
                if (next.Skin == model.Settings.Skin && next.Language == model.Settings.Language) return;
                save(next);
                if (model.Settings != next)
                {
                    preferenceStatus.Text = DesktopText.Get("Desktop.Settings.SaveFailed");
                    skin.SelectedValue = model.Settings.Skin;
                    language.SelectedValue = model.Settings.Language;
                    return;
                }
                preferenceStatus.Text = DesktopText.Get("Desktop.Settings.PreferenceSaved");
                if (DesktopRestart.Confirm(this))
                    DesktopRestart.RestartIfConfirmed(true, DesktopRestart.RestartApplication);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
            {
                preferenceStatus.Text = DesktopText.Get("Desktop.Settings.SaveFailed") + " " + ex.Message;
                skin.SelectedValue = model.Settings.Skin;
                language.SelectedValue = model.Settings.Language;
            }
            finally { storingPreferences = false; }
        }
        skin.SelectionChanged += (_, _) => StorePreferences();
        language.SelectionChanged += (_, _) => StorePreferences();

        panel.Children.Add(new TextBlock { Text = DesktopText.Get("Desktop.Settings.DisplayMode"), Foreground = Accent() });
        var mode = new ComboBox { Name = "DisplayModeSelector", IsEnabled = !modern, ItemsSource = new[]
        {
            new Choice("auto", DesktopText.Get("Desktop.Settings.Auto")),
            new Choice("day", DesktopText.Get("Desktop.Settings.Day")),
            new Choice("night", DesktopText.Get("Desktop.Settings.Night"))
        }, DisplayMemberPath = nameof(Choice.Name), SelectedValuePath = nameof(Choice.Id),
            SelectedValue = model.Settings.DisplayMode, Margin = new Thickness(0, 6, 0, 12) };
        mode.SelectionChanged += (_, _) =>
        {
            if (modern) return;
            if (mode.SelectedValue is string value) save(model.Settings with { DisplayMode = value });
        };
        panel.Children.Add(mode);
        if (modern) panel.Children.Add(new TextBlock
        {
            Text = DesktopText.Get("Desktop.Settings.ModernDisplayMode"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        void Toggle(string title, bool value, Func<CockpitSettings, bool, CockpitSettings> update)
        {
            var box = new CheckBox { Content = title, IsChecked = value, Margin = new Thickness(0, 5, 0, 5) };
            box.Click += (_, _) => save(update(model.Settings, box.IsChecked == true));
            panel.Children.Add(box);
        }
        void OpacityControl(string title, double value, Func<CockpitSettings, double, CockpitSettings> update)
        {
            panel.Children.Add(new TextBlock
            {
                Text = title + " / " + DesktopText.Get("Desktop.Settings.Opacity"),
                Margin = new Thickness(0, 12, 0, 2)
            });
            var slider = new Slider { Minimum = 0.2, Maximum = 1, Value = value, TickFrequency = 0.05 };
            var label = new TextBlock { Text = value.ToString("P0") };
            slider.ValueChanged += (_, e) => { label.Text = e.NewValue.ToString("P0"); save(update(model.Settings, e.NewValue)); };
            panel.Children.Add(slider);
            panel.Children.Add(label);
        }
        OpacityControl(DesktopText.Get("Desktop.Settings.MainWindow"), model.Settings.AppOpacity,
            (s, v) => s with { AppOpacity = v });
        OpacityControl(DesktopText.Get("Desktop.Settings.XBar"), model.Settings.SidebarOpacity,
            (s, v) => s with { SidebarOpacity = v });
        OpacityControl(DesktopText.Get("Desktop.Settings.Gadgets"), model.Settings.GadgetOpacity, (s, v) => s with
        {
            GadgetOpacity = v, GadgetLayouts = s.GadgetLayouts.Select(layout => layout with { Opacity = v }).ToList()
        });
        Toggle(DesktopText.Get("Desktop.Settings.GadgetsEnabled"), model.Settings.GadgetsEnabled,
            (s, v) => s with { GadgetsEnabled = v });
        panel.Children.Add(new TextBlock
        {
            Text = DesktopText.Get("Desktop.Settings.BackgroundDescription"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 4)
        });
        Toggle(DesktopText.Get("Desktop.Settings.BackgroundDiagnostics"), model.Settings.BackgroundDiagnosticsEnabled,
            (s, v) => s with { BackgroundDiagnosticsEnabled = v });
        panel.Children.Add(new TextBlock
        {
            Text = DesktopText.Get("Desktop.Settings.WidgetParameters"),
            Foreground = Accent(), Margin = new Thickness(0, 15, 0, 5)
        });
        Toggle(DesktopText.Get("Desktop.Settings.Cpu"), model.Settings.SidebarWidgetCpu, (s, v) => s with { SidebarWidgetCpu = v });
        Toggle(DesktopText.Get("Desktop.Settings.Memory"), model.Settings.SidebarWidgetMemory, (s, v) => s with { SidebarWidgetMemory = v });
        Toggle(DesktopText.Get("Desktop.Settings.Gpu"), model.Settings.SidebarWidgetGpu, (s, v) => s with { SidebarWidgetGpu = v });
        Toggle(DesktopText.Get("Desktop.Settings.Temperatures"), model.Settings.SidebarWidgetTemperatures,
            (s, v) => s with { SidebarWidgetTemperatures = v });
        Toggle(DesktopText.Get("Desktop.Settings.Network"), model.Settings.SidebarWidgetNetwork,
            (s, v) => s with { SidebarWidgetNetwork = v });
        Toggle(DesktopText.Get("Desktop.Settings.Drives"), model.Settings.SidebarWidgetDrives,
            (s, v) => s with { SidebarWidgetDrives = v });
        panel.Children.Add(new TextBlock
        {
            Text = DesktopText.Get("Desktop.Settings.GadgetHelp"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 10)
        });
        var button = new Button
        {
            Content = DesktopText.Get("Desktop.Settings.NetworkButton"),
            Style = (Style)FindResource("NeonButton"),
            Padding = new Thickness(10)
        };
        button.Click += (_, _) => network();
        panel.Children.Add(button);
        panel.Children.Add(new TextBlock
        {
            Text = DesktopText.Get("Desktop.Settings.ChangesSaved"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 10)
        });
    }
}
