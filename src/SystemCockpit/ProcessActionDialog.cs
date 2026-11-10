using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SystemCockpit.Services;

namespace SystemCockpit;

internal sealed class ProcessActionDialog : Window
{
    internal Button CancelButton { get; } = new() { Content = "Abbrechen", IsCancel = true, IsDefault = true, Margin = new(8) };
    internal Button ConfirmButton { get; } = new() { Margin = new(8) };
    internal ProcessActionPlan Plan { get; }
    internal ProcessActionDialog(ProcessActionPlan plan)
    {
        Plan = plan;
        Title = plan.Tree ? "Prozessbaum beenden" : "Prozess beenden";
        Width = 700; Height = 510; MinWidth = 480; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBackdrop");
        Foreground = Brushes.White;
        var panel = new DockPanel { Margin = new(20) };
        Content = panel;
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        ConfirmButton.Content = Title;
        foreach (var button in new[] { CancelButton, ConfirmButton })
        {
            button.SetResourceReference(StyleProperty, "NeonButton");
            AutomationProperties.SetName(button, button.Content.ToString());
            buttons.Children.Add(button);
        }
        CancelButton.Click += (_, _) => DialogResult = false;
        ConfirmButton.Click += (_, _) => DialogResult = true;
        panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 15,
            Text = $"Harte Beendigung – kein geordnetes Herunterfahren.\nNicht gespeicherte Daten können verloren gehen.\n\n" +
                $"{plan.Root.Name} / PID {plan.Root.Identity.Pid}\nStartidentität: {plan.Root.Identity.StartTicks}\n" +
                $"Pfad: {plan.Root.Path}\nBenutzer-SID: {plan.Root.User}\nSitzung: {plan.Root.Session}\n" +
                "Herausgeber: in dieser Schutzprüfung nicht verifiziert.\n" +
                $"Betroffene Prozesse: {plan.Targets.Count}\n" +
                (plan.Tree ? "Eingefrorene Zielmenge; neue Kinder werden nicht still hinzugefügt.\n" : "Nur dieser Prozess. Kinder werden nicht automatisch beendet.\n") +
                "\nWindows-Schutz und Eigenkomponenten werden vor der Aktion erneut geprüft.\n\n" +
                string.Join("\n", plan.Targets.Select(t => $"{t.Name} / {t.Identity}"))
        }});
        Loaded += (_, _) => { ModernWindowAppearance.Apply(this); CancelButton.Focus(); };
    }
}
