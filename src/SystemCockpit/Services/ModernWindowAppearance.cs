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
            element.Effect = null;
            SystemCockpit.Controls.FrameGlow.SetVisible(element, false);
            SystemCockpit.Controls.FrameGlow.SetEnabled(element, false);
            if (element is Control styled && styled is Button or ComboBox or ComboBoxItem or TabItem or DataGrid)
            {
                if (Application.Current.TryFindResource(styled.GetType()) is Style modernStyle &&
                    !ReferenceEquals(styled.Style, modernStyle))
                    styled.Style = modernStyle;
            }
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
            if (element is TextBlock text && !IsSemanticTextBrush(text.Foreground))
                text.SetResourceReference(TextBlock.ForegroundProperty, "ModernTextPrimary");
            if (element is Control control && control.TemplatedParent is not ComboBox)
            {
                if (control is ComboBox or ComboBoxItem)
                {
                    control.ClearValue(Control.ForegroundProperty);
                    control.ClearValue(Control.BackgroundProperty);
                    control.ClearValue(Control.BorderBrushProperty);
                }
                else if (!IsSemanticTextBrush(control.Foreground))
                    control.SetResourceReference(Control.ForegroundProperty, "ModernTextPrimary");
                if (control is TextBox or PasswordBox)
                {
                    ApplyInputPalette(control);
                    control.IsEnabledChanged -= InputEnabledChanged;
                    control.IsEnabledChanged += InputEnabledChanged;
                    control.SetResourceReference(Control.BorderBrushProperty, "ModernBorder");
                }
                else if (control is Button)
                {
                    control.SetResourceReference(Control.BackgroundProperty, "GlossButton");
                    control.SetResourceReference(Control.BorderBrushProperty, "ButtonBorder");
                }
                else if (control is ListBox or DataGrid)
                {
                    control.SetResourceReference(Control.BackgroundProperty, "SceneBackdrop");
                    control.SetResourceReference(Control.BorderBrushProperty, "ModernBorder");
                }
            }
            if (element is Border border && border.TemplatedParent is null)
            {
                if (HasPaintedBackground(border.Background))
                    border.SetResourceReference(Border.BackgroundProperty, "GlossPanel");
                if (border.BorderBrush is not null)
                    border.SetResourceReference(Border.BorderBrushProperty, "ModernBorder");
            }
            else if (element is Panel panel && panel.TemplatedParent is null && HasPaintedBackground(panel.Background))
                panel.SetResourceReference(Panel.BackgroundProperty, "SceneBackdrop");
            if (element is Shape shape && shape.TemplatedParent is null)
            {
                if (shape is Polyline && shape.StrokeThickness > 2 && shape.Opacity < 0.5)
                    shape.Visibility = Visibility.Collapsed;
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

    private static bool IsSemanticTextBrush(Brush brush) =>
        new[] { "ModernTextPrimary", "ModernTextSecondary", "ModernWarning", "ModernPositive", "ModernTextDisabled" }
            .Any(key => ReferenceEquals(brush, Application.Current.TryFindResource(key)));

    private static bool HasPaintedBackground(Brush? brush) =>
        brush is not null && brush is not SolidColorBrush { Color.A: 0 };

    private static void ApplyInputPalette(Control control)
    {
        control.SetResourceReference(Control.ForegroundProperty,
            control.IsEnabled ? "ModernTextPrimary" : "ModernTextDisabled");
        control.SetResourceReference(Control.BackgroundProperty,
            control.IsEnabled ? "ModernInput" : "ModernDisabledBackground");
    }

    private static void InputEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (SkinCatalog.IsModern && sender is Control control)
            ApplyInputPalette(control);
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
