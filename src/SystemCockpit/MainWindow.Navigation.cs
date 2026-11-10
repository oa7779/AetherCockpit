using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SystemCockpit.Services;

namespace SystemCockpit;

public partial class MainWindow
{
    private readonly List<(string Group, string Route, bool External, Button Button, Expander Expander)> _navigation = [];

    private void InitializeNavigation()
    {
        (string Group, (string Caption, string Route, bool External)[] Items)[] groups =
        [
            ("Navigation.Overview", [("Navigation.Cockpit", "Cockpit", false), ("Navigation.Processes", "Taskmanager", false)]),
            ("Navigation.Hardware", [("Hardware Intelligence", "HardwareIntelligence", false),
                ("Navigation.Board", "Mainboard", false), ("Navigation.Sensors", "Sensoren", false),
                ("Navigation.Drives", "Laufwerke", false), ("Navigation.Smart", "SMART", true)]),
            ("Navigation.Diagnostics", [("Navigation.WindowsDiagnostics", "Diagnose", false), ("Navigation.Protection", "Sicherheit", false),
                ("Navigation.Events", "Ereignisse", false), ("Navigation.NetworkTests", "Netzwerk", true),
                ("Navigation.Measurements", "Messauswertung", false), ("Navigation.LogsGaming", "Logs / Gaming", true)]),
            ("Navigation.Maintenance", [("Navigation.Cleaner", "Cleaner", false), ("Navigation.Plans", "Operations", false),
                ("Navigation.WindowsTools", "Wartung", true), ("Navigation.Packages", "Pakete", true)]),
            ("Navigation.Administration", [("Navigation.SystemAdministration", "Superadmin", false), ("Navigation.Startup", "Autostart", true),
                ("Navigation.Jobs", "Auftraege", true), ("Navigation.Traces", "Spuren", true), ("Navigation.Keys", "Schluessel", true)]),
            ("Navigation.Help", [("Navigation.HelpSearch", "Hilfe", false), ("Navigation.Parameters", "Einstellungen", true)])
        ];
        foreach (var (group, items) in groups)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
            var groupCaption = DesktopUiText.Get(group);
            var expander = new Expander { Header = groupCaption, Content = panel, IsExpanded = group == "Navigation.Overview",
                Foreground = Brushes.Turquoise, Margin = new Thickness(0, 0, 0, 6) };
            NavigationGroups.Children.Add(expander);
            foreach (var (caption, route, external) in items)
            {
                var displayedCaption = SkinCatalog.IsModern && route == "Mainboard"
                    ? DesktopText.Get("Navigation.SystemSchema")
                    : caption.StartsWith("Navigation.", StringComparison.Ordinal) ? DesktopUiText.Get(caption) : caption;
                var button = new Button { Content = displayedCaption, Tag = route, HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 5), Padding = new Thickness(7, 7, 7, 7), FontSize = 11,
                    ToolTip = DesktopUiText.Get(external ? "Navigation.ExternalTooltip" : "Navigation.InternalTooltip") };
                if (external) button.Click += ControlCenterNavigate; else button.Click += Navigate;
                panel.Children.Add(button);
                _navigation.Add((groupCaption, route, external, button, expander));
                if (route == "Cockpit") RegisterName("CockpitNav", button);
            }
        }
        UpdateNavigation("Cockpit");
    }

    private string UpdateNavigation(string route)
    {
        var group = DesktopUiText.Get("Navigation.Overview");
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
