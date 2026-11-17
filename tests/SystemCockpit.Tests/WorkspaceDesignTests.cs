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

internal static class WorkspaceDesignTests
{
    internal static int Run(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || Directory.Exists(directory))
            throw new ArgumentException("Neuer absoluter Ordner für den Designnachweis erforderlich.");
        Directory.CreateDirectory(directory);
        var assertions = 0;
        var measurements = new List<object>();
        SkinCatalog.Apply(Application.Current, "retro-neon");
        foreach (var (width, height) in new[] { (1920, 1080), (2560, 1440), (3840, 2160) })
        foreach (var scale in new[] { 1d, 1.25, 1.5 })
        foreach (var name in new[] { "administration", "platform", "intelligence", "guardian" })
        {
            UserControl view = name switch
            {
                "administration" => new AdminWorkbench(),
                "platform" => new AdminPlatformView(),
                "intelligence" => new IntelligenceFabricView(),
                _ => new GuardianView(Path.Combine(directory, "guardian-store")) { Synthetic = true }
            };
            var root = new AdornerDecorator { Child = view };
            var window = new Window
            {
                Content = root, Width = width / scale, Height = height / scale,
                ShowInTaskbar = false, Background = (Brush)Application.Current.FindResource("SceneBackdrop")
            };
            AetherNeonFrame.SetReducedMotion(root, true);
            try
            {
                window.Show(); Pump();
                ArrangeRoot();
                Check(Math.Abs(VisualTreeHelper.GetDpi(view).DpiScaleX - scale) < 0.001,
                    "Tatsächliche Root-DPI stimmt nicht mit dem Prüffall überein.");
                var tabs = view.FindName("Areas") as TabControl;
                if (tabs is not null)
                {
                    foreach (TabItem tab in tabs.Items)
                    {
                        window.Content = root;
                        tabs.SelectedItem = tab; window.UpdateLayout(); Pump();
                        ArrangeRoot();
                        Check(tab.Visibility == Visibility.Visible && tab.ActualWidth > 0 && tab.ActualHeight > 0,
                            "Verschachtelter Bereich hat keinen sichtbaren Reiter.");
                        Check(tab.Content is FrameworkElement { ActualWidth: > 400, ActualHeight: > 100 },
                            $"Arbeitsbereich {name}/{tab.Header} hat bei {width}×{height}/{scale} keine nutzbare Inhaltsfläche: " +
                            $"{(tab.Content as FrameworkElement)?.ActualWidth}×{(tab.Content as FrameworkElement)?.ActualHeight}.");
                        CheckControls(view);
                    }
                    window.Content = root;
                    tabs.SelectedIndex = 0; window.UpdateLayout(); Pump(); ArrangeRoot();
                }
                else CheckControls(view);
                foreach (var field in name switch
                {
                    "platform" => new[] { "SetupPanel", "PlanPanel", "ActionPanel", "Areas", "Status" },
                    "intelligence" => new[] { "Status", "Modes", "ModelChoice", "Question", "Targets", "Explanation" },
                    "guardian" => new[] { "Status", "Area", "PrepareButton", "EmptyState", "DetailsExpander" },
                    _ => new[] { "SelectionControls", "Entities", "Console", "Status" }
                })
                {
                    var previousTab = tabs?.SelectedItem;
                    var details = field == "Explanation" ? view.FindName("InvestigationDetails") as Expander : null;
                    var wasExpanded = details?.IsExpanded ?? false;
                    var modelDetails = name == "intelligence" && field is "Modes" or "ModelChoice"
                        ? view.FindName("ModelDetails") as Expander : null;
                    var advancedDetails = modelDetails is null ? null : view.FindName("AdvancedModelManagement") as Expander;
                    var developerMode = modelDetails is null ? null : view.FindName("DeveloperMode") as CheckBox;
                    var wasModelDetailsExpanded = modelDetails?.IsExpanded ?? false;
                    var wasAdvancedDetailsExpanded = advancedDetails?.IsExpanded ?? false;
                    var wasDeveloperMode = developerMode?.IsChecked ?? false;
                    if (tabs is not null)
                    {
                        var header = field switch
                        {
                            "Modes" or "ModelChoice" => "Modelle",
                            "Targets" or "Explanation" => "Untersuchungen",
                            "Question" => "Assistent",
                            _ => null
                        };
                        if (header is not null)
                            tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Header, header));
                        if (details is not null) details.IsExpanded = true;
                        if (modelDetails is not null)
                        {
                            modelDetails.IsExpanded = true;
                            advancedDetails!.IsExpanded = true;
                            developerMode!.IsChecked = true;
                        }
                        window.UpdateLayout(); Pump(); ArrangeRoot();
                    }
                    var item = (FrameworkElement)view.FindName(field);
                    Rect bounds;
                    try { bounds = item.TransformToAncestor(view).TransformBounds(new Rect(item.RenderSize)); }
                    catch (InvalidOperationException ex)
                    { throw new InvalidOperationException($"Bedienelement {name}/{field} ist nicht im aktuellen visuellen Arbeitsbereich.", ex); }
                    Check(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1 &&
                        bounds.Right <= view.ActualWidth + 1 && bounds.Bottom <= view.ActualHeight + 1,
                        $"Bedienelement {field} ist bei {width}×{height}/{scale} abgeschnitten.");
                    if (details is not null) details.IsExpanded = wasExpanded;
                    if (modelDetails is not null)
                    {
                        developerMode!.IsChecked = wasDeveloperMode;
                        advancedDetails!.IsExpanded = wasAdvancedDetailsExpanded;
                        modelDetails.IsExpanded = wasModelDetailsExpanded;
                    }
                    if (tabs is not null)
                    {
                        tabs.SelectedItem = previousTab;
                        window.UpdateLayout(); Pump(); ArrangeRoot();
                    }
                }
                var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
                var white = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                    if (pixels[i] >= 235 && pixels[i + 1] >= 235 && pixels[i + 2] >= 235 && pixels[i + 3] >= 235) white++;
                var fraction = (double)white / (width * height);
                Check(fraction < 0.03, "Gerendertes Neon-Bild enthält eine große weiße Standardfläche.");
                if (view is IntelligenceFabricView intelligenceView)
                {
                    var areas = (TabControl)intelligenceView.FindName("Areas");
                    Check(areas.Items.Cast<TabItem>().Select(tab => tab.Header?.ToString()).SequenceEqual(
                            new[] { "Assistent", "Untersuchungen", "Modelle", "Wissen", "Aufträge", "Sicherheit" }),
                        "AETHER Intelligence retains all six primary areas.");
                    Check(intelligenceView.FindName("OperationProgressPanel") is FrameworkElement &&
                        intelligenceView.FindName("OperationPhase") is TextBlock &&
                        intelligenceView.FindName("OperationProgress") is ProgressBar &&
                        ((FrameworkElement)intelligenceView.FindName("OperationProgressPanel")).Visibility == Visibility.Collapsed &&
                        !((Expander)intelligenceView.FindName("ModelDetails")).IsExpanded &&
                        !((Expander)intelligenceView.FindName("AdvancedModelManagement")).IsExpanded,
                        "Progress UI remains available while model-expert actions stay collapsed by default.");
                }
                if (view is GuardianView guardian)
                {
                    var rows = (DataGrid)guardian.FindName("Rows");
                    var prepare = (Button)guardian.FindName("PrepareButton");
                    Check(rows.Visibility == Visibility.Collapsed &&
                        ((TextBlock)guardian.FindName("EmptyState")).IsVisible &&
                        ((Expander)guardian.FindName("ActionsExpander")).IsExpanded == false &&
                        ((Expander)guardian.FindName("DetailsExpander")).IsExpanded == false,
                        "Guardian uses a compact empty state and keeps actions/raw data collapsed.");
                    Check(prepare.ActualWidth > 0 && prepare.ActualWidth < 260 && prepare.ActualHeight < 50 &&
                        ((Button)guardian.FindName("OpenButton")).Visibility == Visibility.Collapsed &&
                        ((Button)guardian.FindName("DeleteButton")).Visibility == Visibility.Collapsed &&
                        ((Button)guardian.FindName("CancelButton")).Visibility == Visibility.Collapsed,
                        "Guardian primary action is compact and selection-only actions are not prominent.");
                }
                var filename = $"{name}-{width}x{height}-{scale * 100:0}.png";
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = new FileStream(Path.Combine(directory, filename), FileMode.CreateNew))
                    encoder.Save(stream);
                measurements.Add(new { View = name, Width = width, Height = height, RootDpi = scale, WhiteFraction = fraction, Image = filename });

                void ArrangeRoot()
                {
                    window.Content = null;
                    VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
                    root.Measure(new Size(width / scale, height / scale));
                    root.Arrange(new Rect(0, 0, width / scale, height / scale));
                    root.UpdateLayout();
                    window.Content = root;
                    window.UpdateLayout();
                }
            }
            finally
            {
                if (view is IntelligenceFabricView intelligence) intelligence.ShutdownAsync().GetAwaiter().GetResult();
                if (view is GuardianView guardian) guardian.ShutdownAsync().GetAwaiter().GetResult();
                window.Close(); Pump();
            }
        }
        EmbeddedNavigation();
        using (var stream = new FileStream(Path.Combine(directory, "measurements.json"), FileMode.CreateNew))
            JsonSerializer.Serialize(stream, measurements, new JsonSerializerOptions { WriteIndented = true });
        return assertions;

        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
        void CheckControls(DependencyObject view)
        {
            foreach (var control in Visuals(view).OfType<Control>().Where(c => c.Visibility == Visibility.Visible && c.ActualWidth > 0 && c.ActualHeight > 0))
            {
                if (control is TextBox or ComboBox or DataGrid or TabControl or ListBox)
                {
                    Check(control.Style is not null, $"{control.GetType().Name} hat keinen AETHER-Stil.");
                    Check(control.Background is not null && !Bright(control.Background),
                        $"{control.GetType().Name} verwendet eine weiße oder fehlende Inhaltsfläche.");
                    if (control.Background is SolidColorBrush background && background.Color.A == 255 &&
                        control.Foreground is SolidColorBrush foreground)
                        Check(Contrast(foreground.Color, background.Color) >= 4.5,
                            $"{control.GetType().Name} hat unzureichenden Textkontrast.");
                }
                if (control is Button)
                    Check(control.Style is not null && HasBase(control.Style, (Style)control.FindResource("NeonButton")),
                        "Lokaler Buttonstil überschattet das AETHER-Template.");
                if (control is DataGrid grid)
                    Check(grid.RowStyle is not null && grid.CellStyle is not null && grid.ColumnHeaderStyle is not null &&
                        grid.EnableRowVirtualization && grid.EnableColumnVirtualization,
                        "Tabelle hat ungestylte Zeilen/Zellen/Köpfe oder verlorene Virtualisierung.");
            }
            foreach (var border in Visuals(view).OfType<Border>().Where(b => b.Visibility == Visibility.Visible && b.ActualWidth > 100 && b.ActualHeight > 50))
                Check(!Bright(border.Background), "Eine große Template-Inhaltsfläche verwendet Weiß.");
        }
        void EmbeddedNavigation()
        {
            var main = new MainWindow(new CockpitSettings { Skin = "retro-neon", Language = "de" })
            { Width = 1440, Height = 940, ShowInTaskbar = false };
            AetherNeonFrame.SetReducedMotion(main, true);
            try
            {
                main.Show(); Pump();
                var pages = (TabControl)main.FindName("Pages");
                foreach (TabItem page in pages.Items)
                {
                    pages.SelectedItem = page; main.UpdateLayout(); Pump();
                    Check(page.Visibility == Visibility.Collapsed, "Hauptnavigation zeigt unbeabsichtigt zweite Reiter.");
                    Check(page.Content is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 },
                        $"Bestehende Seite {page.Tag} hat keine nutzbaren Bounds.");
                }
                pages.SelectedItem = pages.Items.Cast<TabItem>().Single(p => Equals(p.Tag, "Superadmin"));
                main.UpdateLayout(); Pump();
                var administration = (TabControl)main.FindName("AdministrationAreas");
                foreach (TabItem tab in administration.Items)
                {
                    administration.SelectedItem = tab; main.UpdateLayout(); Pump();
                    Check(tab.IsVisible && tab.ActualWidth > 0, "Hauptseitenstil versteckt Administrationsreiter.");
                }
                pages.SelectedItem = pages.Items.Cast<TabItem>().Single(p => Equals(p.Tag, "IntelligenceFabric"));
                main.UpdateLayout(); Pump();
                var intelligence = (IntelligenceFabricView)main.FindName("IntelligenceFabric");
                foreach (TabItem tab in ((TabControl)intelligence.FindName("Areas")).Items)
                    Check(tab.IsVisible && tab.ActualWidth > 0, "Hauptseitenstil versteckt Intelligence-Reiter.");
            }
            finally { main.Close(); Pump(); }
        }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static bool HasBase(Style style, Style expected)
    {
        for (Style? current = style; current is not null; current = current.BasedOn)
            if (ReferenceEquals(current, expected)) return true;
        return false;
    }
    private static bool Bright(Brush? brush) => brush switch
    {
        SolidColorBrush solid => solid.Color.A > 200 && solid.Color.R > 230 && solid.Color.G > 230 && solid.Color.B > 230,
        GradientBrush gradient => gradient.GradientStops.Any(stop => stop.Color.A > 200 && stop.Color.R > 230 && stop.Color.G > 230 && stop.Color.B > 230),
        _ => false
    };
    private static double Contrast(Color a, Color b)
    {
        static double Linear(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
        static double Luminance(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        var left = Luminance(a); var right = Luminance(b);
        return (Math.Max(left, right) + .05) / (Math.Min(left, right) + .05);
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
