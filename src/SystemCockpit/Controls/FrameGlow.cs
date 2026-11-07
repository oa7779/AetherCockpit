using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.ComponentModel;

namespace SystemCockpit.Controls;

public static class FrameGlow
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled",
        typeof(bool), typeof(FrameGlow), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty VisibleProperty = DependencyProperty.RegisterAttached("Visible",
        typeof(bool), typeof(FrameGlow), new PropertyMetadata(false, Changed));
    private static readonly DependencyPropertyKey MotionActivePropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "MotionActive", typeof(bool), typeof(FrameGlow), new PropertyMetadata(false));
    public static readonly DependencyProperty MotionActiveProperty = MotionActivePropertyKey.DependencyProperty;
    private static readonly DependencyProperty AdornerProperty = DependencyProperty.RegisterAttached("Adorner",
        typeof(GlowAdorner), typeof(FrameGlow));
    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);
    public static bool GetVisible(DependencyObject obj) => (bool)obj.GetValue(VisibleProperty);
    public static void SetVisible(DependencyObject obj, bool value) => obj.SetValue(VisibleProperty, value);
    public static bool GetMotionActive(DependencyObject obj) => (bool)obj.GetValue(MotionActiveProperty);

    private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        if (obj is not FrameworkElement element) return;
        element.Loaded -= Loaded;
        element.Unloaded -= Unloaded;
        if (GetVisible(element) || GetEnabled(element))
        {
            element.Loaded += Loaded;
            element.Unloaded += Unloaded;
            if (element.GetValue(AdornerProperty) is GlowAdorner adorner) adorner.UpdateMotion();
            else if (element.IsLoaded) Add(element);
        }
        else Remove(element);
    }
    private static void Loaded(object sender, RoutedEventArgs e) => Add((FrameworkElement)sender);
    private static void Unloaded(object sender, RoutedEventArgs e) => Remove((FrameworkElement)sender);
    private static void Add(FrameworkElement element)
    {
        if (element.GetValue(AdornerProperty) is not null) return;
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
        private readonly Grid _rims = new() { IsHitTestVisible = false };
        private readonly Rectangle _trail;
        private readonly Rectangle _core;
        private bool _moving;
        public AdornerLayer Layer { get; }
        public GlowAdorner(UIElement element, AdornerLayer layer) : base(element)
        {
            Layer = layer;
            IsHitTestVisible = false;
            var brush = new LinearGradientBrush(Color.FromRgb(36, 255, 227), Color.FromRgb(255, 70, 224), 45);
            brush.Freeze();
            var corners = element is Border border ? border.CornerRadius : new CornerRadius(12);
            // Layered strokes give a halo without blurring/rasterizing the panel's text and children.
            foreach (var (width, opacity) in new[] { (10d, .10), (5d, .22), (1.5, .85) })
                _rims.Children.Add(new Border { BorderBrush = brush, BorderThickness = new Thickness(width),
                    CornerRadius = corners, Opacity = opacity, Margin = new Thickness(-width / 2) });
            _trail = new Rectangle
            {
                Stroke = brush, StrokeThickness = 5, StrokeDashArray = new DoubleCollection([8, 32]),
                RadiusX = 12, RadiusY = 12, Opacity = .25, Visibility = Visibility.Hidden
            };
            _core = new Rectangle
            {
                Stroke = Brushes.LightCyan, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection([12, 121.3333333333]),
                RadiusX = 12, RadiusY = 12, Opacity = .8, Visibility = Visibility.Hidden
            };
            _rims.Children.Add(_trail);
            _rims.Children.Add(_core);
            AddVisualChild(_rims);
            element.IsVisibleChanged += VisibilityChanged;
            SystemParameters.StaticPropertyChanged += SystemSettingChanged;
            UpdateMotion();
        }
        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateMotion();
        private void SystemSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) UpdateMotion();
        }
        public void UpdateMotion()
        {
            var moving = GetEnabled(AdornedElement) && SystemParameters.ClientAreaAnimation && AdornedElement.IsVisible;
            if (_moving == moving) return;
            _moving = moving;
            AdornedElement.SetValue(MotionActivePropertyKey, moving);
            _trail.Visibility = _core.Visibility = moving ? Visibility.Visible : Visibility.Hidden;
            _trail.BeginAnimation(Shape.StrokeDashOffsetProperty, moving
                ? new DoubleAnimation(0, -40, TimeSpan.FromSeconds(5)) { RepeatBehavior = RepeatBehavior.Forever } : null);
            _core.BeginAnimation(Shape.StrokeDashOffsetProperty, moving
                ? new DoubleAnimation(0, -133.3333333333, TimeSpan.FromSeconds(5)) { RepeatBehavior = RepeatBehavior.Forever } : null);
            _trail.BeginAnimation(OpacityProperty, moving
                ? new DoubleAnimation(.18, .42, TimeSpan.FromSeconds(2.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        }
        public void Stop()
        {
            ((FrameworkElement)AdornedElement).IsVisibleChanged -= VisibilityChanged;
            SystemParameters.StaticPropertyChanged -= SystemSettingChanged;
            AdornedElement.SetValue(MotionActivePropertyKey, false);
            _trail.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            _core.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            _trail.BeginAnimation(OpacityProperty, null);
        }
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => index == 0 ? _rims : throw new ArgumentOutOfRangeException(nameof(index));
        protected override Size MeasureOverride(Size constraint) { _rims.Measure(AdornedElement.RenderSize); return AdornedElement.RenderSize; }
        protected override Size ArrangeOverride(Size finalSize) { _rims.Arrange(new Rect(finalSize)); return finalSize; }
    }
}
