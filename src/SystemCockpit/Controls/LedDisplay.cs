using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Automation.Peers;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public sealed class LedDisplay : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(LedDisplay),
        new FrameworkPropertyMetadata("--", FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(LedDisplay),
        new FrameworkPropertyMetadata(Brushes.Lime, FrameworkPropertyMetadataOptions.AffectsRender));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    protected override AutomationPeer OnCreateAutomationPeer() => new LedPeer(this);
    private sealed class LedPeer(LedDisplay owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() => owner.Text;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetClassNameCore() => nameof(LedDisplay);
    }
    private static readonly int[] Masks = [0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f];
    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(availableSize.Width, Math.Max(24, (Text ?? "--").Length * 20)), Math.Min(availableSize.Height, 32));
    protected override void OnRender(DrawingContext dc)
    {
        var text = Text ?? "--";
        if (SkinCatalog.IsModern)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            var fontSize = Math.Max(1, ActualHeight * .76);
            var value = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), fontSize, Accent,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if (value.Width > ActualWidth)
                value.SetFontSize(Math.Max(1, fontSize * ActualWidth / value.Width));
            dc.DrawText(value, new Point(Math.Max(0, (ActualWidth - value.Width) / 2),
                Math.Max(0, (ActualHeight - value.Height) / 2)));
            return;
        }
        var length = Math.Max(1, text.Sum(c => c is '.' or ',' or ':' ? 0.45 : 1));
        var scale = Math.Max(0, Math.Min(ActualHeight / 32, ActualWidth / (length * 20)));
        if (scale <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - length * 20 * scale) / 2, (ActualHeight - 32 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        var dim = new SolidColorBrush(Color.FromArgb(35, 124, 160, 128));
        double x = 0;
        foreach (var c in text)
        {
            if (c is '.' or ',' or ':')
            {
                dc.DrawEllipse(Accent, null, new(x + 3, c == ':' ? 23 : 29), 1.7, 1.7);
                if (c == ':') dc.DrawEllipse(Accent, null, new(x + 3, 10), 1.7, 1.7);
                x += 9;
                continue;
            }
            if (char.IsAsciiDigit(c) || c == '-')
            {
                var mask = c == '-' ? 0x40 : Masks[c - '0'];
                Rect[] segments = [new(x + 4, 1, 10, 3), new(x + 14, 4, 3, 10), new(x + 14, 18, 3, 10),
                    new(x + 4, 28, 10, 3), new(x + 1, 18, 3, 10), new(x + 1, 4, 3, 10), new(x + 4, 14.5, 10, 3)];
                for (var i = 0; i < segments.Length; i++) dc.DrawRoundedRectangle((mask & (1 << i)) != 0 ? Accent : dim, null, segments[i], 1, 1);
            }
            else if (!char.IsWhiteSpace(c))
                dc.DrawText(new FormattedText(c.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Consolas"), 19, Accent, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, 8));
            x += 20;
        }
        dc.Pop();
        dc.Pop();
    }
}
