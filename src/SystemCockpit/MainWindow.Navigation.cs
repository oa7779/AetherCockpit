using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SystemCockpit;

public partial class MainWindow
{
    private readonly List<(string Group, string Route, bool External, Button Button, Expander Expander)> _navigation = [];

    private void InitializeNavigation()
    {
        (string Group, (string Caption, string Route, bool External)[] Items)[] groups =
        [
            ("Übersicht", [("Cockpit", "Cockpit", false), ("Prozesse / Leistung", "Taskmanager", false)]),
            ("Hardware", [("Mainframe / Board", "Mainboard", false), ("Sensoren", "Sensoren", false),
                ("Laufwerke", "Laufwerke", false), ("SMART ↗", "SMART", true)]),
            ("Diagnose", [("Windows-Diagnose", "Diagnose", false), ("Schutzstatus", "Sicherheit", false),
                ("Ereignisse", "Ereignisse", false), ("Netzwerk / Tests ↗", "Netzwerk", true),
                ("Messauswertung", "Messauswertung", false), ("Logs / Gaming ↗", "Logs / Gaming", true)]),
            ("Wartung", [("Cleaner", "Cleaner", false), ("Assistenten / Pläne", "Operations", false),
                ("Windows-Werkzeuge ↗", "Wartung", true), ("Pakete / Updates ↗", "Pakete", true)]),
            ("Verwaltung", [("Systemverwaltung", "Superadmin", false), ("Autostart ↗", "Autostart", true),
                ("Aufträge ↗", "Auftraege", true), ("Ordnerspuren ↗", "Spuren", true), ("Schlüssel ↗", "Schluessel", true)]),
            ("Hilfe", [("Anleitung / Suche", "Hilfe", false), ("Parameter / Server ↗", "Einstellungen", true)])
        ];
        foreach (var (group, items) in groups)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
            var expander = new Expander { Header = group, Content = panel, IsExpanded = group == "Übersicht",
                Foreground = Brushes.Turquoise, Margin = new Thickness(0, 0, 0, 6) };
            NavigationGroups.Children.Add(expander);
            foreach (var (caption, route, external) in items)
            {
                var button = new Button { Content = caption, Tag = route, HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 5), Padding = new Thickness(7, 7, 7, 7), FontSize = 11,
                    ToolTip = external ? "Öffnet das Modul in der Systemkontrolle; startet keine Aktion." :
                        "Öffnet den Bereich im Hauptfenster; startet keine Aktion." };
                if (external) button.Click += ControlCenterNavigate; else button.Click += Navigate;
                panel.Children.Add(button);
                _navigation.Add((group, route, external, button, expander));
                if (route == "Cockpit") RegisterName("CockpitNav", button);
            }
        }
        UpdateNavigation("Cockpit");
    }

    private string UpdateNavigation(string route)
    {
        var group = "Übersicht";
        foreach (var item in _navigation)
        {
            var selected = !item.External && item.Route == route;
            item.Button.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
            item.Button.BorderThickness = new Thickness(selected ? 2 : 1);
            if (!selected) continue;
            group = item.Group;
            item.Expander.IsExpanded = true;
        }
        return group;
    }
}
