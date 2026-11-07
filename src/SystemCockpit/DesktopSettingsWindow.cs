using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SystemCockpit.Services;

namespace SystemCockpit;

internal sealed class DesktopSettingsWindow : Window
{
    internal DesktopSettingsWindow(CockpitModel model, Action<CockpitSettings> save, Action network)
    {
        Title = "AETHER / Desktop, Transparenz und Parameter";
        Width = 620;
        Height = 720;
        Background = new SolidColorBrush(Color.FromRgb(12, 9, 32));
        Foreground = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "DESKTOP / GADGETS", FontSize = 23, Foreground = Brushes.Turquoise });
        panel.Children.Add(new TextBlock { Text = "Copyright (c) 2026 Olaf Weins", Margin = new Thickness(0, 8, 0, 16) });
        void Toggle(string title, bool value, Func<CockpitSettings, bool, CockpitSettings> update)
        {
            var box = new CheckBox { Content = title, IsChecked = value, Margin = new Thickness(0, 5, 0, 5) };
            box.Click += (_, _) => save(update(model.Settings, box.IsChecked == true));
            panel.Children.Add(box);
        }
        void OpacityControl(string title, double value, Func<CockpitSettings, double, CockpitSettings> update)
        {
            panel.Children.Add(new TextBlock { Text = title + " / Deckkraft 20-100 %", Margin = new Thickness(0, 12, 0, 2) });
            var slider = new Slider { Minimum = 0.2, Maximum = 1, Value = value, TickFrequency = 0.05 };
            var label = new TextBlock { Text = value.ToString("P0") };
            slider.ValueChanged += (_, e) => { label.Text = e.NewValue.ToString("P0"); save(update(model.Settings, e.NewValue)); };
            panel.Children.Add(slider);
            panel.Children.Add(label);
        }
        OpacityControl("Hauptfenster", model.Settings.AppOpacity, (s, v) => s with { AppOpacity = v });
        OpacityControl("xBar", model.Settings.SidebarOpacity, (s, v) => s with { SidebarOpacity = v });
        OpacityControl("Alle Gadgets (einzeln auch am Gadget regelbar)", model.Settings.GadgetOpacity, (s, v) => s with
        {
            GadgetOpacity = v, GadgetLayouts = s.GadgetLayouts.Select(layout => layout with { Opacity = v }).ToList()
        });
        Toggle("Desktop-Gadgets anzeigen (unabhaengig vom Hauptfenster)", model.Settings.GadgetsEnabled,
            (s, v) => s with { GadgetsEnabled = v });
        panel.Children.Add(new TextBlock { Text = "PARAMETER / fuer Gadgets und xBar", Foreground = Brushes.Turquoise, Margin = new Thickness(0, 15, 0, 5) });
        Toggle("CPU", model.Settings.SidebarWidgetCpu, (s, v) => s with { SidebarWidgetCpu = v });
        Toggle("RAM", model.Settings.SidebarWidgetMemory, (s, v) => s with { SidebarWidgetMemory = v });
        Toggle("GPU", model.Settings.SidebarWidgetGpu, (s, v) => s with { SidebarWidgetGpu = v });
        Toggle("Temperaturen: CPU, GPU, SSD", model.Settings.SidebarWidgetTemperatures, (s, v) => s with { SidebarWidgetTemperatures = v });
        Toggle("Netzwerk: Download / Upload", model.Settings.SidebarWidgetNetwork, (s, v) => s with { SidebarWidgetNetwork = v });
        Toggle("Laufwerke: Belegt-/Frei-Torten", model.Settings.SidebarWidgetDrives, (s, v) => s with { SidebarWidgetDrives = v });
        panel.Children.Add(new TextBlock { Text = "Am Gadget-Titel ziehen. Position und Deckkraft werden gespeichert. Minimieren, Wiederherstellen und Beenden ueber das AETHER-Symbol bei der Uhr.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) });
        var button = new Button { Content = "Netzwerkueberwachung / Npcap / Alarme",
            Style = (Style)FindResource("NeonButton"), Padding = new Thickness(10) };
        button.Click += (_, _) => network();
        panel.Children.Add(button);
        panel.Children.Add(new TextBlock { Text = "Aenderungen werden sofort lokal gespeichert. Autostart-Aufgabe nur ueber das installierte Setup einrichten oder entfernen.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) });
    }
}
