using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SystemCockpit;

internal sealed class ActionConfirmationWindow : Window
{
    internal TextBox Preview { get; }
    internal CheckBox Consent { get; }
    internal Button Confirm { get; }

    internal ActionConfirmationWindow(string title, string warning)
    {
        Title = "AETHER | " + title;
        Width = 900; Height = 640; MinWidth = 680; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBackdrop");
        Foreground = Brushes.White;
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Content = root;
        root.Children.Add(new TextBlock { Text = "AKTIONSVORSCHAU / noch nichts ausgefuehrt", Foreground = Brushes.Gold, FontSize = 21 });
        Preview = new TextBox { Text = warning, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Style = (Style)FindResource("NeonTextBox"), Margin = new Thickness(0, 12, 0, 12) };
        Grid.SetRow(Preview, 1); root.Children.Add(Preview);
        var footer = new StackPanel();
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        Consent = new CheckBox { Content = "Auswahl, Quellen und Auswirkungen geprueft; diese Aktion freigeben.", Foreground = Brushes.Gold, Margin = new Thickness(0, 0, 0, 12) };
        footer.Children.Add(Consent);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(buttons);
        Confirm = new Button { Content = "Jetzt bewusst ausfuehren", IsEnabled = false, Style = (Style)FindResource("NeonButton") };
        var cancel = new Button { Content = "Abbrechen / nichts ausfuehren", IsDefault = true, IsCancel = true,
            Style = (Style)FindResource("NeonButton"), Margin = new Thickness(12, 0, 0, 0) };
        buttons.Children.Add(Confirm); buttons.Children.Add(cancel);
        Consent.Checked += (_, _) => Confirm.IsEnabled = true;
        Consent.Unchecked += (_, _) => Confirm.IsEnabled = false;
        Confirm.Click += (_, _) => DialogResult = true;
        cancel.Click += (_, _) => DialogResult = false;
    }
}
