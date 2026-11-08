using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Windows.Data;
using System.Runtime.CompilerServices;

namespace SystemCockpit.Services;

internal static class ModernWindowAppearance
{
    private static bool _enabled;
    private static readonly ConditionalWeakTable<FrameworkElement, ChartAppearance> ChartDefaults = new();
    private static readonly List<WeakReference<FrameworkElement>> Charts = [];

    private sealed record ChartValue(DependencyProperty Property, object Value, BindingBase? Binding, string? Resource);
    private sealed record ChartAppearance(ChartValue[] Values);

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
            RememberChart(element);
            element.Effect = null;
            SystemCockpit.Controls.FrameGlow.SetVisible(element, false);
            SystemCockpit.Controls.FrameGlow.SetEnabled(element, false);
            if (element is Control styled && styled is Button or ComboBox or ComboBoxItem or TabItem or DataGrid or ProgressBar)
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
            if (element is TextBlock text)
            {
                text.FontFamily = new FontFamily("Segoe UI");
                if (CaptionOwner(text) is { } captionOwner)
                {
                    text.Style = (Style)Application.Current.FindResource(typeof(TextBlock));
                    text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground)) { Source = captionOwner });
                }
                else
                {
                    ApplyAuthoredTextBrush(text, TextBlock.ForegroundProperty, text.Foreground);
                    foreach (var run in text.Inlines.OfType<Run>())
                        ApplyAuthoredTextBrush(run, TextElement.ForegroundProperty, run.Foreground);
                }
            }
            if (element is Control control && control.TemplatedParent is not ComboBox)
            {
                if (control is not TextBox { IsReadOnly: true } and not RichTextBox)
                    control.FontFamily = new FontFamily("Segoe UI");
                if (control is ComboBox or ComboBoxItem or Button)
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
                else if (control is ProgressBar)
                {
                    control.SetResourceReference(Control.BackgroundProperty, "ModernSelection");
                    control.SetResourceReference(Control.ForegroundProperty, "ModernAccent");
                    control.SetResourceReference(Control.BorderBrushProperty, "ModernBorder");
                }
                else if (control is ListBox or DataGrid)
                {
                    control.SetResourceReference(Control.BackgroundProperty, "SceneBackdrop");
                    control.SetResourceReference(Control.BorderBrushProperty, "ModernBorder");
                }
            }
            if (element is Border border && IsAuthoredVisual(border))
            {
                if (HasPaintedBackground(border.Background))
                    border.SetResourceReference(Border.BackgroundProperty, "GlossPanel");
                if (border.BorderBrush is not null)
                    border.SetResourceReference(Border.BorderBrushProperty, "ModernBorder");
            }
            else if (element is Panel panel && IsAuthoredVisual(panel) &&
                (HasPaintedBackground(panel.Background) || Equals(panel.Tag, "chart-canvas")))
                panel.SetResourceReference(Panel.BackgroundProperty, "SceneBackdrop");
            if (element is Shape shape && IsAuthoredVisual(shape))
            {
                if (shape is Polyline && shape.StrokeThickness > 2 && shape.Opacity < 0.5)
                    shape.Visibility = Visibility.Collapsed;
                var modernTag = shape.Tag as string ?? "";
                shape.SetResourceReference(Shape.StrokeProperty,
                    shape is Polyline || modernTag.StartsWith("modern-schema-flow:", StringComparison.Ordinal)
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

    private static bool IsAuthoredVisual(FrameworkElement element) =>
        element.TemplatedParent is null or ContentPresenter || IsChartVisual(element);

    private static bool IsChartVisual(FrameworkElement element)
    {
        if (Equals(element.Tag, "chart-panel") || Equals(element.Tag, "chart-canvas")) return true;
        if (element is not Shape) return false;
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null;
            parent = VisualTreeHelper.GetParent(parent))
            if (parent is Canvas canvas) return Equals(canvas.Tag, "chart-canvas");
        return false;
    }

    private static void RememberChart(FrameworkElement element)
    {
        if (!IsChartVisual(element) || ChartDefaults.TryGetValue(element, out _)) return;
        DependencyProperty[] properties = element switch
        {
            Border => [Border.BackgroundProperty, Border.BorderBrushProperty],
            Canvas => [Panel.BackgroundProperty],
            Shape => [Shape.StrokeProperty, Shape.FillProperty, UIElement.VisibilityProperty],
            _ => []
        };
        var values = properties.Select(property =>
        {
            var local = element.ReadLocalValue(property);
            var value = local == DependencyProperty.UnsetValue ? local : element.GetValue(property);
            var resource = new[] { "GlossPanel", "GlassRim" }.FirstOrDefault(key =>
                ReferenceEquals(value, Application.Current.TryFindResource(key)));
            return new ChartValue(property, value, BindingOperations.GetBindingBase(element, property), resource);
        }).ToArray();
        ChartDefaults.Add(element, new ChartAppearance(values));
        Charts.Add(new WeakReference<FrameworkElement>(element));
    }

    internal static void RefreshCharts(Application application)
    {
        if (!_enabled) return;
        if (SkinCatalog.IsModern)
        {
            foreach (Window window in application.Windows)
                foreach (var element in Descendants(window).Where(IsChartVisual))
                    ApplyElement(element);
        }
        for (var index = Charts.Count - 1; index >= 0; index--)
        {
            if (!Charts[index].TryGetTarget(out var element))
            {
                Charts.RemoveAt(index);
                continue;
            }
            if (SkinCatalog.IsModern) ApplyElement(element);
            else if (ChartDefaults.TryGetValue(element, out var original))
                foreach (var saved in original.Values)
                {
                    if (saved.Binding is not null) BindingOperations.SetBinding(element, saved.Property, saved.Binding);
                    else if (saved.Resource is not null) element.SetResourceReference(saved.Property, saved.Resource);
                    else if (saved.Value == DependencyProperty.UnsetValue) element.ClearValue(saved.Property);
                    else element.SetValue(saved.Property, saved.Value);
                }
            element.InvalidateVisual();
        }
    }

    private static Control? CaptionOwner(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null;
            parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is Button or ComboBoxItem or ComboBox) return (Control)parent;
            if (parent is Window or UserControl) break;
        }
        return null;
    }

    private static void ApplyAuthoredTextBrush(FrameworkContentElement element, DependencyProperty property, Brush brush)
    {
        if (!IsSemanticTextBrush(brush))
            element.SetResourceReference(property, IsWarningBrush(brush) ? "ModernWarning" : "ModernTextPrimary");
    }

    private static void ApplyAuthoredTextBrush(FrameworkElement element, DependencyProperty property, Brush brush)
    {
        if (!IsSemanticTextBrush(brush))
            element.SetResourceReference(property, IsWarningBrush(brush) ? "ModernWarning" : "ModernTextPrimary");
    }

    private static bool IsWarningBrush(Brush brush) =>
        brush is SolidColorBrush solid && (solid.Color == Colors.Gold ||
            solid.Color == Color.FromRgb(255, 184, 108));

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
        if (sender is Window or UserControl && sender is FrameworkElement root && ReferenceEquals(e.OriginalSource, sender))
            root.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (!SkinCatalog.IsModern || !root.IsLoaded) return;
                foreach (var descendant in Descendants(root)) ApplyElement(descendant);
            });
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
