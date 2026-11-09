using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public enum NeonFrameStatus { Normal, Active, Analysis, Notice, Error, Admin, Offline, Disabled, Unknown }
public enum NeonFrameTheme { Auto, Neon, Modern }

public static class AetherNeonFrame
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(AetherNeonFrame), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty StatusProperty = Property("Status", NeonFrameStatus.Normal,
        value => value is NeonFrameStatus status && Enum.IsDefined(status));
    public static readonly DependencyProperty ThemeModeProperty = Property("ThemeMode", NeonFrameTheme.Auto,
        value => value is NeonFrameTheme theme && Enum.IsDefined(theme), true);
    public static readonly DependencyProperty AccentColorProperty = ColorProperty("AccentColor");
    public static readonly DependencyProperty CoreColorProperty = ColorProperty("CoreColor");
    public static readonly DependencyProperty OuterGlowColorProperty = ColorProperty("OuterGlowColor");
    public static readonly DependencyProperty InnerGlowColorProperty = ColorProperty("InnerGlowColor");
    public static readonly DependencyProperty CoreThicknessProperty = Number("CoreThickness", 1.25, .5, 3);
    public static readonly DependencyProperty OuterGlowRadiusProperty = Number("OuterGlowRadius", 9, 0, 16);
    public static readonly DependencyProperty OuterGlowOpacityProperty = Number("OuterGlowOpacity", .55, 0, 1);
    public static readonly DependencyProperty InnerGlowRadiusProperty = Number("InnerGlowRadius", 3, 0, 6);
    public static readonly DependencyProperty InnerGlowOpacityProperty = Number("InnerGlowOpacity", .28, 0, 1);
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius?), typeof(AetherNeonFrame), new PropertyMetadata(null, Changed),
        value => value is null || value is CornerRadius corners && new[]
            { corners.TopLeft, corners.TopRight, corners.BottomLeft, corners.BottomRight }.All(v => double.IsFinite(v) && v >= 0));
    public static readonly DependencyProperty BreathingEnabledProperty = Property("BreathingEnabled", true, null, true);
    public static readonly DependencyProperty BreathingMinimumProperty = Number("BreathingMinimum", .86, .65, 1);
    public static readonly DependencyProperty BreathingMaximumProperty = Number("BreathingMaximum", 1, .65, 1);
    public static readonly DependencyProperty BreathingDurationProperty = DependencyProperty.RegisterAttached(
        "BreathingDuration", typeof(TimeSpan), typeof(AetherNeonFrame), new PropertyMetadata(TimeSpan.FromSeconds(14), Changed),
        value => value is TimeSpan duration && duration >= TimeSpan.FromSeconds(8) && duration <= TimeSpan.FromMinutes(2));
    public static readonly DependencyProperty ReducedMotionProperty = Property("ReducedMotion", false, null, true);
    public static readonly DependencyProperty IsActiveProperty = Property("IsActive", true, null, true);
    public static readonly DependencyProperty IsVisibleProperty = Property("IsVisible", true, null, true);
    private static readonly DependencyPropertyKey MotionActiveKey = DependencyProperty.RegisterAttachedReadOnly(
        "MotionActive", typeof(bool), typeof(AetherNeonFrame), new PropertyMetadata(false));
    public static readonly DependencyProperty MotionActiveProperty = MotionActiveKey.DependencyProperty;
    private static readonly DependencyProperty AdornerProperty = DependencyProperty.RegisterAttached(
        "Adorner", typeof(NeonAdorner), typeof(AetherNeonFrame));

    private static DependencyProperty Property<T>(string name, T value, ValidateValueCallback? validate = null, bool inherits = false) =>
        DependencyProperty.RegisterAttached(name, typeof(T), typeof(AetherNeonFrame),
            new FrameworkPropertyMetadata(value, inherits ? FrameworkPropertyMetadataOptions.Inherits : FrameworkPropertyMetadataOptions.None, Changed), validate);
    private static DependencyProperty Number(string name, double value, double minimum, double maximum) =>
        Property(name, value, candidate => candidate is double number && double.IsFinite(number) && number >= minimum && number <= maximum);
    private static DependencyProperty ColorProperty(string name) => DependencyProperty.RegisterAttached(
        name, typeof(Color?), typeof(AetherNeonFrame), new PropertyMetadata(null, Changed));

    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);
    public static NeonFrameStatus GetStatus(DependencyObject obj) => (NeonFrameStatus)obj.GetValue(StatusProperty);
    public static void SetStatus(DependencyObject obj, NeonFrameStatus value) => obj.SetValue(StatusProperty, value);
    public static NeonFrameTheme GetThemeMode(DependencyObject obj) => (NeonFrameTheme)obj.GetValue(ThemeModeProperty);
    public static void SetThemeMode(DependencyObject obj, NeonFrameTheme value) => obj.SetValue(ThemeModeProperty, value);
    public static Color? GetAccentColor(DependencyObject obj) => (Color?)obj.GetValue(AccentColorProperty);
    public static void SetAccentColor(DependencyObject obj, Color? value) => obj.SetValue(AccentColorProperty, value);
    public static Color? GetCoreColor(DependencyObject obj) => (Color?)obj.GetValue(CoreColorProperty);
    public static void SetCoreColor(DependencyObject obj, Color? value) => obj.SetValue(CoreColorProperty, value);
    public static Color? GetOuterGlowColor(DependencyObject obj) => (Color?)obj.GetValue(OuterGlowColorProperty);
    public static void SetOuterGlowColor(DependencyObject obj, Color? value) => obj.SetValue(OuterGlowColorProperty, value);
    public static Color? GetInnerGlowColor(DependencyObject obj) => (Color?)obj.GetValue(InnerGlowColorProperty);
    public static void SetInnerGlowColor(DependencyObject obj, Color? value) => obj.SetValue(InnerGlowColorProperty, value);
    public static double GetCoreThickness(DependencyObject obj) => (double)obj.GetValue(CoreThicknessProperty);
    public static void SetCoreThickness(DependencyObject obj, double value) => obj.SetValue(CoreThicknessProperty, value);
    public static double GetOuterGlowRadius(DependencyObject obj) => (double)obj.GetValue(OuterGlowRadiusProperty);
    public static void SetOuterGlowRadius(DependencyObject obj, double value) => obj.SetValue(OuterGlowRadiusProperty, value);
    public static double GetOuterGlowOpacity(DependencyObject obj) => (double)obj.GetValue(OuterGlowOpacityProperty);
    public static void SetOuterGlowOpacity(DependencyObject obj, double value) => obj.SetValue(OuterGlowOpacityProperty, value);
    public static double GetInnerGlowRadius(DependencyObject obj) => (double)obj.GetValue(InnerGlowRadiusProperty);
    public static void SetInnerGlowRadius(DependencyObject obj, double value) => obj.SetValue(InnerGlowRadiusProperty, value);
    public static double GetInnerGlowOpacity(DependencyObject obj) => (double)obj.GetValue(InnerGlowOpacityProperty);
    public static void SetInnerGlowOpacity(DependencyObject obj, double value) => obj.SetValue(InnerGlowOpacityProperty, value);
    public static CornerRadius? GetCornerRadius(DependencyObject obj) => (CornerRadius?)obj.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject obj, CornerRadius? value) => obj.SetValue(CornerRadiusProperty, value);
    public static bool GetBreathingEnabled(DependencyObject obj) => (bool)obj.GetValue(BreathingEnabledProperty);
    public static void SetBreathingEnabled(DependencyObject obj, bool value) => obj.SetValue(BreathingEnabledProperty, value);
    public static double GetBreathingMinimum(DependencyObject obj) => (double)obj.GetValue(BreathingMinimumProperty);
    public static void SetBreathingMinimum(DependencyObject obj, double value) => obj.SetValue(BreathingMinimumProperty, value);
    public static double GetBreathingMaximum(DependencyObject obj) => (double)obj.GetValue(BreathingMaximumProperty);
    public static void SetBreathingMaximum(DependencyObject obj, double value) => obj.SetValue(BreathingMaximumProperty, value);
    public static TimeSpan GetBreathingDuration(DependencyObject obj) => (TimeSpan)obj.GetValue(BreathingDurationProperty);
    public static void SetBreathingDuration(DependencyObject obj, TimeSpan value) => obj.SetValue(BreathingDurationProperty, value);
    public static bool GetReducedMotion(DependencyObject obj) => (bool)obj.GetValue(ReducedMotionProperty);
    public static void SetReducedMotion(DependencyObject obj, bool value) => obj.SetValue(ReducedMotionProperty, value);
    public static bool GetIsActive(DependencyObject obj) => (bool)obj.GetValue(IsActiveProperty);
    public static void SetIsActive(DependencyObject obj, bool value) => obj.SetValue(IsActiveProperty, value);
    public static bool GetIsVisible(DependencyObject obj) => (bool)obj.GetValue(IsVisibleProperty);
    public static void SetIsVisible(DependencyObject obj, bool value) => obj.SetValue(IsVisibleProperty, value);
    public static bool GetMotionActive(DependencyObject obj) => (bool)obj.GetValue(MotionActiveProperty);

    private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        if (obj is not FrameworkElement element) return;
        if (e.Property == EnabledProperty)
        {
            element.Loaded -= Loaded;
            element.Unloaded -= Unloaded;
            if (GetEnabled(element))
            {
                element.Loaded += Loaded;
                element.Unloaded += Unloaded;
                if (element.IsLoaded) Add(element);
            }
            else Remove(element);
        }
        if (element.GetValue(AdornerProperty) is NeonAdorner adorner) adorner.Refresh();
    }

    private static void Loaded(object sender, RoutedEventArgs e) => Add((FrameworkElement)sender);
    private static void Unloaded(object sender, RoutedEventArgs e) => Remove((FrameworkElement)sender);
    private static void Add(FrameworkElement element)
    {
        if (!GetEnabled(element) || element.GetValue(AdornerProperty) is not null) return;
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer is null) return;
        var adorner = new NeonAdorner(element, layer);
        element.SetValue(AdornerProperty, adorner);
        layer.Add(adorner);
        adorner.Refresh();
    }

    private static void Remove(FrameworkElement element)
    {
        if (element.GetValue(AdornerProperty) is not NeonAdorner adorner) return;
        adorner.Stop();
        adorner.Layer.Remove(adorner);
        element.ClearValue(AdornerProperty);
    }

    internal sealed class NeonAdorner : Adorner
    {
        private readonly FrameworkElement _owner;
        private readonly Window? _window;
        private readonly Grid _visuals = new() { IsHitTestVisible = false };
        private readonly Grid _glows = new() { IsHitTestVisible = false };
        private readonly Border _outer = new();
        private readonly Border _inner = new();
        private readonly Border _core = new();
        private bool _moving;
        private bool _stopped;
        private double _extent;
        internal AdornerLayer Layer { get; }
        internal bool SubscriptionsActive => !_stopped;

        internal NeonAdorner(FrameworkElement owner, AdornerLayer layer) : base(owner)
        {
            _owner = owner;
            Layer = layer;
            _window = Window.GetWindow(owner);
            IsHitTestVisible = false;
            _glows.Children.Add(_outer);
            _glows.Children.Add(_inner);
            _visuals.Children.Add(_glows);
            _visuals.Children.Add(_core);
            AddVisualChild(_visuals);
            owner.IsVisibleChanged += VisibilityChanged;
            owner.IsEnabledChanged += VisibilityChanged;
            owner.LayoutUpdated += LayoutChanged;
            SystemParameters.StaticPropertyChanged += SettingsChanged;
            SkinCatalog.ThemeChanged += ThemeChanged;
            if (_window is not null)
            {
                _window.StateChanged += WindowChanged;
                _window.IsVisibleChanged += VisibilityChanged;
                _window.DpiChanged += DpiChanged;
            }
        }

        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateMotion();
        private void WindowChanged(object? sender, EventArgs e) => UpdateMotion();
        private void DpiChanged(object sender, DpiChangedEventArgs e) => Refresh();
        private void ThemeChanged(object? sender, EventArgs e) => Refresh();
        private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast)) Refresh();
        }

        private CornerRadius Corners => GetCornerRadius(_owner) ?? (_owner as Border)?.CornerRadius ?? new CornerRadius(12);
        private Brush? _lastRim;
        private CornerRadius _lastCorners;
        private void LayoutChanged(object? sender, EventArgs e)
        {
            var rim = (_owner as Border)?.BorderBrush;
            if (!ReferenceEquals(rim, _lastRim) || Corners != _lastCorners) Refresh();
        }

        private bool Modern => SkinCatalog.IsModern || GetThemeMode(_owner) == NeonFrameTheme.Modern;
        private Brush Resource(string key) => _owner.TryFindResource(key) as Brush ??
            throw new InvalidOperationException("Missing neon frame theme resource: " + key);
        private static Brush Frozen(Brush brush)
        {
            var copy = brush.CloneCurrentValue();
            copy.Freeze();
            return copy;
        }
        private Brush ColorOr(Color? color, Brush fallback) => Frozen(color is { } value ? new SolidColorBrush(value) : fallback);

        internal void Refresh()
        {
            if (_stopped) return;
            StopClock();
            _lastRim = (_owner as Border)?.BorderBrush;
            _lastCorners = Corners;
            var status = GetStatus(_owner);
            var highContrast = SystemParameters.HighContrast;
            var accent = highContrast ? SystemColors.WindowTextBrush : Modern ? Resource("ModernBorder") :
                status == NeonFrameStatus.Normal ? _lastRim ?? Resource("NeonFrameNormal") :
                Resource("NeonFrame" + status);
            if (!Modern && !highContrast) accent = ColorOr(GetAccentColor(_owner), accent);
            _core.BorderBrush = ColorOr(Modern || highContrast ? null : GetCoreColor(_owner), accent);
            _outer.BorderBrush = ColorOr(GetOuterGlowColor(_owner), accent);
            _inner.BorderBrush = ColorOr(GetInnerGlowColor(_owner), accent);
            _core.BorderThickness = new Thickness(GetCoreThickness(_owner));
            _outer.BorderThickness = new Thickness(3);
            _inner.BorderThickness = new Thickness(2);
            foreach (var border in new[] { _core, _outer, _inner }) border.CornerRadius = Corners;
            var factor = status is NeonFrameStatus.Disabled ? .3 : status is NeonFrameStatus.Offline ? .45 :
                status is NeonFrameStatus.Active or NeonFrameStatus.Analysis ? 1.1 : 1;
            _core.Opacity = status == NeonFrameStatus.Disabled ? .6 : 1;
            _outer.Opacity = Math.Min(1, GetOuterGlowOpacity(_owner) * factor);
            _inner.Opacity = Math.Min(1, GetInnerGlowOpacity(_owner) * factor);
            SetBlur(_outer, GetOuterGlowRadius(_owner));
            SetBlur(_inner, GetInnerGlowRadius(_owner));
            _glows.Visibility = Modern || highContrast ? Visibility.Collapsed : Visibility.Visible;
            _extent = Math.Max(GetOuterGlowRadius(_owner) + 3, GetInnerGlowRadius(_owner) + 2);
            Visibility = GetIsVisible(_owner) ? Visibility.Visible : Visibility.Hidden;
            InvalidateMeasure();
            UpdateMotion();
        }

        private static void SetBlur(Border border, double radius)
        {
            var blur = new BlurEffect { Radius = radius, RenderingBias = RenderingBias.Performance };
            blur.Freeze();
            border.Effect = radius > 0 ? blur : null;
            border.CacheMode = new BitmapCache();
        }

        private void StopClock()
        {
            _glows.BeginAnimation(OpacityProperty, null);
            _glows.Opacity = 1;
            _moving = false;
            _owner.SetValue(MotionActiveKey, false);
        }

        private void UpdateMotion()
        {
            var status = GetStatus(_owner);
            var moving = !_stopped && _owner.IsLoaded && _owner.IsVisible && _owner.IsEnabled &&
                GetIsVisible(_owner) && GetIsActive(_owner) && GetBreathingEnabled(_owner) &&
                !GetReducedMotion(_owner) && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast &&
                !Modern && status is not (NeonFrameStatus.Disabled or NeonFrameStatus.Offline or NeonFrameStatus.Unknown) &&
                (_window is null || _window.IsVisible && _window.WindowState != WindowState.Minimized);
            if (_moving == moving) return;
            StopClock();
            if (!moving) return;
            _moving = true;
            _owner.SetValue(MotionActiveKey, true);
            var minimum = Math.Min(GetBreathingMinimum(_owner), GetBreathingMaximum(_owner));
            var maximum = Math.Max(GetBreathingMinimum(_owner), GetBreathingMaximum(_owner));
            var animation = new DoubleAnimation(minimum, maximum, new Duration(GetBreathingDuration(_owner) / 2))
            {
                AutoReverse = true,
                RepeatBehavior = status is NeonFrameStatus.Notice ? new RepeatBehavior(1) :
                    status is NeonFrameStatus.Error or NeonFrameStatus.Analysis ? new RepeatBehavior(2) : RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            };
            Timeline.SetDesiredFrameRate(animation, 10);
            animation.Completed += AnimationCompleted;
            _glows.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private void AnimationCompleted(object? sender, EventArgs e) => StopClock();

        internal void Stop()
        {
            _stopped = true;
            StopClock();
            _owner.IsVisibleChanged -= VisibilityChanged;
            _owner.IsEnabledChanged -= VisibilityChanged;
            _owner.LayoutUpdated -= LayoutChanged;
            SystemParameters.StaticPropertyChanged -= SettingsChanged;
            SkinCatalog.ThemeChanged -= ThemeChanged;
            if (_window is not null)
            {
                _window.StateChanged -= WindowChanged;
                _window.IsVisibleChanged -= VisibilityChanged;
                _window.DpiChanged -= DpiChanged;
            }
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => index == 0 ? _visuals : throw new ArgumentOutOfRangeException(nameof(index));
        protected override Size MeasureOverride(Size constraint)
        {
            _visuals.Measure(AdornedElement.RenderSize);
            return AdornedElement.RenderSize;
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            _visuals.Arrange(new Rect(finalSize));
            // Only the empty rim is blurred. Clip the halo away from text and controls.
            var outer = new RectangleGeometry(new Rect(-_extent, -_extent,
                finalSize.Width + 2 * _extent, finalSize.Height + 2 * _extent));
            var inset = Math.Min(3, Math.Min(finalSize.Width, finalSize.Height) / 2);
            var interior = new RectangleGeometry(new Rect(inset, inset,
                Math.Max(0, finalSize.Width - 2 * inset), Math.Max(0, finalSize.Height - 2 * inset)),
                Math.Max(0, Math.Min(Corners.TopLeft, Corners.BottomRight) - inset),
                Math.Max(0, Math.Min(Corners.TopRight, Corners.BottomLeft) - inset));
            var clip = new CombinedGeometry(GeometryCombineMode.Exclude, outer, interior);
            clip.Freeze();
            _glows.Clip = clip;
            return finalSize;
        }
    }
}
