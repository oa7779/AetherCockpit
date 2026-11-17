using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class GuardianProof
{
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    internal static void Run(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || Directory.Exists(directory)) throw new ArgumentException("Neuer absoluter Guardian-Proofordner erforderlich.");
        Directory.CreateDirectory(directory);
        var old = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { Ui(directory); }
        finally { SynchronizationContext.SetSynchronizationContext(old); }
    }
    private static void Ui(string directory)
    {
        var context = new GuardianContext("synthetic", "synthetic");
        var fixtures = Enumerable.Range(0, 256).Select(i => GuardianVault.Artifact(RecoveryKind.ConfigurationBackup,
            $"Guardian Fixture {i:000}", context, "Synthetische Zustandsdokumentation, kein reales Backup.")).ToArray();
        var measurements = new List<object>();
        foreach (var theme in new[] { "retro-neon", "windows-modern-light", "windows-modern-dark" })
        foreach (var scale in new[] { 1d, 1.5, 2d })
        {
            SkinCatalog.Apply(Application.Current, theme == "retro-neon" ? theme : "windows-modern", theme.EndsWith("light", StringComparison.Ordinal));
            var view = new GuardianView(Path.Combine(directory, "not-created")) { Synthetic = true };
            AetherNeonFrame.SetReducedMotion(view, true);
            var root = new AdornerDecorator { Child = view };
            var window = new Window { Content = root, Width = 1460, Height = 940, ShowInTaskbar = false };
            try
            {
                window.Show(); Pump();
                var prepare = (Button)view.FindName("PrepareButton");
                var actions = (Expander)view.FindName("ActionsExpander");
                if (!prepare.IsEnabled || actions.IsExpanded ||
                    ((DataGrid)view.FindName("Rows")).Visibility != Visibility.Collapsed ||
                    !((TextBlock)view.FindName("EmptyState")).IsVisible)
                    throw new InvalidOperationException("Guardian initial state must offer one compact preparation action and an empty state.");
                prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
                view.Pending.GetAwaiter().GetResult();
                if (!((TextBlock)view.FindName("Status")).Text.Contains("geprüfte Vault-Objekte", StringComparison.Ordinal))
                    throw new InvalidOperationException("Guardian preparation action did not load the local vault status.");

                var package = GuardianVault.Artifact(RecoveryKind.RescuePackage, "Guardian Package Fixture",
                    context, "Synthetischer Paketnachweis.");
                view.ApplyFixtures([package]);
                var areas = (ComboBox)view.FindName("Area");
                areas.SelectedIndex = 11;
                ((DataGrid)view.FindName("Rows")).SelectedIndex = 0;
                Pump();
                if (((Button)view.FindName("InspectPrivateButton")).Visibility != Visibility.Visible ||
                    ((Button)view.FindName("OpenButton")).Visibility != Visibility.Visible ||
                    ((Button)view.FindName("DeleteButton")).Visibility != Visibility.Visible)
                    throw new InvalidOperationException("Guardian selection did not reveal only its applicable package and entry actions.");
                areas.SelectedIndex = 0;
                if (((Button)view.FindName("InspectPrivateButton")).Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("Package inspection remained available outside the package context.");

                var clock = Stopwatch.StartNew(); view.ApplyFixtures(fixtures); window.UpdateLayout(); Pump();
                var ready = clock.Elapsed.TotalMilliseconds;
                var grid = (DataGrid)view.FindName("Rows");
                var rows = grid.Items.Count;
                var containers = grid.Items.Cast<object>().Count(e => grid.ItemContainerGenerator.ContainerFromItem(e) is DataGridRow);
                if (ready > 2500 || containers >= 100 || rows != 256) throw new InvalidOperationException("Guardian virtualization/first-render gate failed.");
                if (!grid.Columns.Select(column => column.Header?.ToString())
                        .SequenceEqual(new[] { "Typ", "Name", "Zeitpunkt", "Status", "Aktion" }))
                    throw new InvalidOperationException("Guardian Vorgangstabelle must retain its contextual action column.");
                clock.Restart(); ((TextBox)view.FindName("Search")).Text = "Fixture 255"; Pump();
                var filtered = clock.Elapsed.TotalMilliseconds;
                if (grid.Items.Count != 1 || filtered > 2500) throw new InvalidOperationException("Guardian exact search gate failed.");
                grid.SelectedIndex = 0; Pump();
                if (!((TextBox)view.FindName("Details")).Text.Contains("Fixture 255", StringComparison.Ordinal))
                    throw new InvalidOperationException("Guardian detail selection did not bind exact identity.");
                ((TextBox)view.FindName("Search")).Clear();
                areas = (ComboBox)view.FindName("Area");
                var areaLabels = new[]
                {
                    "Wiederherstellungsarchiv", "Änderungsjournal", "Rückwegplanung", "Wiederherstellungsstatus",
                    "Softwarebestand", "Installationsvergleich", "Updateverlauf", "Treiberstatus",
                    "Absturz- und Hangdiagnose", "ETW-Tiefendiagnose", "Wartungsablauf", "Sicherungs- und Supportpakete"
                };
                if (!areas.Items.Cast<ComboBoxItem>().Select(item => item.Content?.ToString()).SequenceEqual(areaLabels))
                    throw new InvalidOperationException("Guardian work areas are not fully localized.");
                foreach (ComboBoxItem area in areas.Items)
                {
                    var route = area.Tag?.ToString() ?? throw new InvalidOperationException("Guardian area route key is missing.");
                    view.QuickAction(route);
                    if (route == "Recovery Readiness") continue;
                    Pump();
                    if (!Equals(areas.SelectedItem, area)) throw new InvalidOperationException("Guardian area unreachable.");
                }
                var buttons = LogicalDescendants<Button>(view).ToArray();
                if (buttons.Length < 30 || buttons.Any(button => string.IsNullOrWhiteSpace(button.ToolTip?.ToString())))
                    throw new InvalidOperationException("Guardian action buttons need a specific tooltip.");
                var details = (Expander)view.FindName("DetailsExpander");
                if (details.IsExpanded || !details.Header.ToString()!.Contains("JSON", StringComparison.Ordinal))
                    throw new InvalidOperationException("Guardian technical JSON details must be clearly labelled and collapsed by default.");
                areas.SelectedIndex = 0; view.ApplyFixtures(fixtures); Pump();
                window.Content = null;
                VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
                root.Measure(new Size(1440, 900)); root.Arrange(new Rect(0, 0, 1440, 900)); root.UpdateLayout();
                Save(root, scale, Path.Combine(directory, $"guardian-{theme}-{scale * 100:0}.png"));
                window.Content = root; window.UpdateLayout(); Pump();
                view.QuickAction("Deep Trace");
                ((ComboBox)view.FindName("TraceProfile")).SelectedIndex = 6;
                ((Expander)view.FindName("ActionsExpander")).IsExpanded = true;
                ((Expander)view.FindName("TraceTools")).IsExpanded = true;
                window.UpdateLayout(); Pump();
                CheckTraceLayout(view);
                window.Content = null;
                VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
                root.Measure(new Size(1440, 900)); root.Arrange(new Rect(0, 0, 1440, 900)); root.UpdateLayout();
                if (((Button)view.FindName("TraceStart")).IsEnabled || ((WrapPanel)view.FindName("TraceComponents")).Visibility != Visibility.Visible)
                    throw new InvalidOperationException("Custom trace controls or production lock missing.");
                Save(root, scale, Path.Combine(directory, $"guardian-trace-{theme}-{scale * 100:0}.png"));
                window.Content = root; window.UpdateLayout(); Pump();
                window.Width = 1080; window.Height = 720; Pump();
                var empty = (TextBlock)view.FindName("EmptyState");
                if (grid.ActualHeight < 60 && (!empty.IsVisible || empty.ActualHeight < 30))
                    throw new InvalidOperationException("Guardian layout leaves no usable result area at minimum size.");
                CheckTraceLayout(view);
                measurements.Add(new { Theme = theme, Scale = scale, ReadyMs = ready, FilterMs = filtered, Rows = rows, Containers = containers });
                view.ShutdownAsync().GetAwaiter().GetResult();
            }
            finally { window.Close(); Pump(); }
        }
        if (Directory.Exists(Path.Combine(directory, "not-created"))) throw new InvalidOperationException("Synthetic navigation must not create real vault files.");
        File.WriteAllText(Path.Combine(directory, "measurements.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: nine Guardian theme/root-DPI cases, eighteen images including locked custom trace profiles, twelve areas, virtualized 256-object history, exact search and details.");
    }
    private static void Save(FrameworkElement root, double scale, string path)
    {
        var bitmap = new RenderTargetBitmap((int)(1440 * scale), (int)(900 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void CheckTraceLayout(GuardianView view)
    {
        var tools = (StackPanel)view.FindName("DiagnosticTools");
        var status = (TextBlock)view.FindName("Status");
        var toolBounds = tools.TransformToAncestor(view).TransformBounds(new Rect(tools.RenderSize));
        var statusBounds = status.TransformToAncestor(view).TransformBounds(new Rect(status.RenderSize));
        if (!tools.IsVisible || toolBounds.Top < statusBounds.Bottom || toolBounds.Right > view.ActualWidth + 1)
            throw new InvalidOperationException("Trace controls overlap the live status row or exceed the Guardian viewport.");
        if (((ComboBox)view.FindName("TraceProfile")).SelectedIndex != 6 ||
            (string)((ComboBoxItem)((ComboBox)view.FindName("Area")).SelectedItem).Tag != "Deep Trace")
            throw new InvalidOperationException("Trace screenshot selection does not match the live control state.");
    }
    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var descendant in LogicalDescendants<T>(child)) yield return descendant;
        }
    }
    internal static int Native(string output) => GuardianTraceAcceptance.Run(output);
}
