using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SystemCockpit.Controls;

public static class FrameGlow
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled",
        typeof(bool), typeof(FrameGlow), new PropertyMetadata(false, Changed));
    private static readonly DependencyProperty AdornerProperty = DependencyProperty.RegisterAttached("Adorner",
        typeof(GlowAdorner), typeof(FrameGlow));
    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

    private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        if (obj is not FrameworkElement element) return;
        element.Loaded -= Loaded;
        element.Unloaded -= Unloaded;
        if ((bool)e.NewValue)
        {
            element.Loaded += Loaded;
            element.Unloaded += Unloaded;
            if (element.IsLoaded) Add(element);
        }
        else Remove(element);
    }
    private static void Loaded(object sender, RoutedEventArgs e) => Add((FrameworkElement)sender);
    private static void Unloaded(object sender, RoutedEventArgs e) => Remove((FrameworkElement)sender);
    private static void Add(FrameworkElement element)
    {
        if (element.GetValue(AdornerProperty) is not null || !SystemParameters.ClientAreaAnimation) return;
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer is null) return;
        var adorner = new GlowAdorner(element, layer);
        element.SetValue(AdornerProperty, adorner);
        layer.Add(adorner);
    }
    private static void Remove(FrameworkElement element)
    {
        if (element.GetValue(AdornerProperty) is not GlowAdorner adorner) return;
        adorner.Stop();
        adorner.Layer.Remove(adorner);
        element.ClearValue(AdornerProperty);
    }
    private sealed class GlowAdorner : Adorner
    {
        private readonly Rectangle _rim;
        public AdornerLayer Layer { get; }
        public GlowAdorner(UIElement element, AdornerLayer layer) : base(element)
        {
            Layer = layer;
            IsHitTestVisible = false;
            _rim = new Rectangle
            {
                Stroke = new LinearGradientBrush(Color.FromRgb(36, 255, 227), Color.FromRgb(255, 70, 224), 45),
                StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection([14, 80]), RadiusX = 12, RadiusY = 12, Opacity = 0.75
            };
            AddVisualChild(_rim);
            _rim.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(0, -94, TimeSpan.FromSeconds(7)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        public void Stop() => _rim.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _rim;
        protected override Size MeasureOverride(Size constraint) { _rim.Measure(AdornedElement.RenderSize); return AdornedElement.RenderSize; }
        protected override Size ArrangeOverride(Size finalSize) { _rim.Arrange(new Rect(finalSize)); return finalSize; }
    }
}
