using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SystemCockpit.Services;

internal static class ModernWindowAppearance
{
    private static bool _enabled;

    internal static void Enable()
    {
        if (_enabled) return;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(WindowLoaded), handledEventsToo: true);
        _enabled = true;
    }

    internal static void Apply(Window window)
    {
        if (!SkinCatalog.IsModern) return;
        window.SetResourceReference(Control.BackgroundProperty, "WindowBackdrop");
        window.SetResourceReference(Control.ForegroundProperty, "ModernTextPrimary");
        window.FontFamily = new FontFamily("Segoe UI");
        foreach (var element in Descendants(window))
            ApplyElement(element);
    }

    private static void ApplyElement(FrameworkElement element)
    {
            if (element is SystemCockpit.Controls.NeonGauge gauge)
                gauge.SetResourceReference(SystemCockpit.Controls.NeonGauge.AccentProperty, "ModernAccent");
            if (element is SystemCockpit.Controls.LedDisplay led)
                led.SetResourceReference(SystemCockpit.Controls.LedDisplay.AccentProperty, "ModernTextPrimary");
            if (element is SystemCockpit.Controls.DrivePie pie)
            {
                pie.SetResourceReference(SystemCockpit.Controls.DrivePie.UsedBrushProperty, "ModernAccent");
                pie.SetResourceReference(SystemCockpit.Controls.DrivePie.FreeBrushProperty, "ModernSelection");
            }
            if (element is SystemCockpit.Controls.SensorPlot plot)
                plot.SetResourceReference(SystemCockpit.Controls.SensorPlot.PaletteBrushProperty, "ModernAccent");
            if (element is TextBlock text)
                text.SetResourceReference(TextBlock.ForegroundProperty, "ModernTextPrimary");
            if (element is Control control)
            {
                control.SetResourceReference(Control.ForegroundProperty, "ModernTextPrimary");
                if (control is TextBox or PasswordBox)
                {
                    control.SetResourceReference(Control.BackgroundProperty, "ModernInput");
                    control.SetResourceReference(Control.BorderBrushProperty, "ModernBorder");
                }
                else if (control is Button)
                {
                    control.SetResourceReference(Control.BackgroundProperty, "GlossButton");
                    control.SetResourceReference(Control.BorderBrushProperty, "ButtonBorder");
                }
                else if (control is ListBox or ComboBox or DataGrid)
                {
                    control.SetResourceReference(Control.BackgroundProperty, "SceneBackdrop");
                    control.SetResourceReference(Control.BorderBrushProperty, "ModernBorder");
            }
            if (element is Border border)
            {
                border.SetResourceReference(Border.BackgroundProperty, "GlossPanel");
                border.SetResourceReference(Border.BorderBrushProperty, "ModernBorder");
            }
            else if (element is Panel panel)
                panel.SetResourceReference(Panel.BackgroundProperty, "SceneBackdrop");
            if (element is Shape shape)
            {
                var modernTag = shape.Tag as string ?? "";
                shape.SetResourceReference(Shape.StrokeProperty,
                    modernTag.StartsWith("modern-schema-flow:", StringComparison.Ordinal)
                        ? "ModernAccent" : "ModernBorder");
                if (shape.Fill is not null)
                    shape.SetResourceReference(Shape.FillProperty,
                        modernTag.StartsWith("modern-schema-active-device:", StringComparison.Ordinal)
                            ? "ModernAccent" : "ModernSelection");
            }
        }
    }

    private static void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (!SkinCatalog.IsModern) return;
        if (sender is Window window) Apply(window);
        else if (sender is FrameworkElement element && ReferenceEquals(e.OriginalSource, sender))
            ApplyElement(element);
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (VisualTreeHelper.GetChild(root, index) is not FrameworkElement child) continue;
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
