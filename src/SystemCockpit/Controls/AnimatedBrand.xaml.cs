using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public partial class AnimatedBrand : UserControl
{
    public static readonly DependencyProperty MotionEnabledProperty = DependencyProperty.Register(
        nameof(MotionEnabled), typeof(bool), typeof(AnimatedBrand),
        new PropertyMetadata(false, (sender, _) => ((AnimatedBrand)sender).UpdateMotion()));

    public bool MotionEnabled
    {
        get => (bool)GetValue(MotionEnabledProperty);
        set => SetValue(MotionEnabledProperty, value);
    }

    internal bool MotionActive { get; private set; }

    public AnimatedBrand()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= AnimationSettingsChanged;
            SystemParameters.StaticPropertyChanged += AnimationSettingsChanged;
            UpdateMotion();
        };
        Unloaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= AnimationSettingsChanged;
            UpdateMotion(false);
        };
        IsVisibleChanged += (_, _) => UpdateMotion();
    }

    private void AnimationSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) UpdateMotion();
    }

    private void UpdateMotion(bool allowMotion = true)
    {
        if (Logo is null) return;
        var active = allowMotion && MotionEnabled && IsLoaded && IsVisible &&
            SystemParameters.ClientAreaAnimation && !SkinCatalog.IsModern;
        if (MotionActive == active) return;
        MotionActive = active;
        Logo.BeginAnimation(OpacityProperty, active
            ? new DoubleAnimation(.78, 1, TimeSpan.FromSeconds(2.8))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        Reflection.Opacity = active ? .65 : 0;
        Sweep.BeginAnimation(TranslateTransform.XProperty, active
            ? new DoubleAnimation(-1.5, 1.5, TimeSpan.FromSeconds(7))
                { RepeatBehavior = RepeatBehavior.Forever } : null);
    }
}
