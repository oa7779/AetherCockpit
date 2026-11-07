using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SystemCockpit.Services;

public static class DisplayTime
{
    public static bool IsDay(string mode, DateTimeOffset localTime) => mode switch
    {
        "auto" => localTime.Hour is >= 7 and < 19,
        "day" => true,
        "night" => false,
        _ => throw new ArgumentException("Darstellungsmodus muss auto, day oder night sein.", nameof(mode))
    };

    public static void Apply(Window window, bool day)
    {
        foreach (var key in new[] { "WindowBackdrop", "GlossPanel", "ChromeSidebar", "SceneBackdrop" })
            window.Resources.Remove(key);
        if (!day)
        {
            foreach (var key in new[] { "WindowBackdrop", "GlossPanel", "ChromeSidebar", "SceneBackdrop" })
                window.Resources[key] = Frozen("#000000");
            return;
        }
        window.Resources["WindowBackdrop"] = Frozen("#15243D");
        window.Resources["SceneBackdrop"] = Frozen("#15243D");
        window.Resources["GlossPanel"] = new LinearGradientBrush(
            Color.FromRgb(48, 65, 94), Color.FromRgb(20, 33, 57), 90).GetAsFrozen();
        window.Resources["ChromeSidebar"] = new LinearGradientBrush(
            Color.FromRgb(42, 55, 86), Color.FromRgb(22, 30, 54), 0).GetAsFrozen();
    }

    private static Brush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}

internal sealed class DisplayPulse : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly CockpitModel _model;
    private readonly Action<bool> _appearance;
    private bool? _day;

    internal DisplayPulse(Dispatcher dispatcher, CockpitModel model, Action<bool> appearance)
    {
        _model = model;
        _appearance = appearance;
        _timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += Tick;
    }

    internal void Start() { Refresh(DateTimeOffset.Now); _timer.Start(); }
    internal void Stop() => _timer.Stop();
    internal void Refresh(DateTimeOffset localTime)
    {
        _model.UpdateDisplayTime(localTime);
        var day = DisplayTime.IsDay(_model.Settings.DisplayMode, localTime);
        _model.IsDaytime = day;
        if (_day == day) return;
        _day = day;
        _appearance(day);
    }
    private void Tick(object? sender, EventArgs e) => Refresh(DateTimeOffset.Now);
    public void Dispose() { _timer.Stop(); _timer.Tick -= Tick; }
}
