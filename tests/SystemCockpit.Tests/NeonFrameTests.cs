using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class NeonFrameTests
{
    internal static int Run()
    {
        var count = 0;
        void Check(bool condition, string message) { count++; if (!condition) throw new InvalidOperationException(message); }
        var text = new TextBlock { Text = "SCHARFER INHALT / 42 %", Margin = new Thickness(16), Foreground = Brushes.White };
        var border = new Border { Child = text, CornerRadius = new CornerRadius(22, 5, 22, 5), Margin = new Thickness(24),
            BorderBrush = Brushes.Cyan, Background = Brushes.Black };
        var root = new AdornerDecorator { Child = border };
        var window = new Window { Content = root, Width = 480, Height = 300, ShowInTaskbar = false };
        AetherNeonFrame.SetEnabled(border, true);
        Check(Enum.GetValues<NeonFrameStatus>().Length == 9, "All nine semantic frame states exist");
        Check(AetherNeonFrame.GetBreathingDuration(border) >= TimeSpan.FromSeconds(8) &&
            AetherNeonFrame.GetBreathingMinimum(border) > 0 && AetherNeonFrame.GetBreathingMaximum(border) <= 1,
            "Central defaults never disappear or animate rapidly");
        try { AetherNeonFrame.SetOuterGlowRadius(border, double.NaN); Check(false, "NaN accepted"); } catch (ArgumentException) { count++; }
        try { AetherNeonFrame.SetBreathingDuration(border, TimeSpan.FromSeconds(1)); Check(false, "Rapid animation accepted"); } catch (ArgumentException) { count++; }
        try
        {
            window.Show();
            NeonFrameProof.Pump(TimeSpan.FromMilliseconds(80));
            var adorner = Adorner();
            Check(adorner is { IsHitTestVisible: false, SubscriptionsActive: true }, "Exactly one noninteractive owner-bound adorner");
            var visuals = (Grid)VisualTreeHelper.GetChild(adorner, 0);
            var glows = (Grid)visuals.Children[0];
            var core = (Border)visuals.Children[1];
            Check(core.Effect is null && core.CornerRadius == border.CornerRadius && text.Effect is null && border.Effect is null,
                "Only empty halo layers blur; asymmetric corners and content stay intact");
            Check(glows.Children.Count == 2 && glows.Children.OfType<Border>().All(b => b.Effect is not null && b.CacheMode is BitmapCache) &&
                glows.Clip is { IsFrozen: true }, "Inner and outer glow cached separately with bounded hollow clip");
            Check(AetherNeonFrame.GetMotionActive(border) == SystemParameters.ClientAreaAnimation &&
                glows.HasAnimatedProperties == AetherNeonFrame.GetMotionActive(border), "Breathing honors Windows motion permission");
            foreach (var status in Enum.GetValues<NeonFrameStatus>())
            {
                AetherNeonFrame.SetStatus(border, status);
                Check(border.TryFindResource("NeonFrame" + status) is Brush, "State has central palette: " + status);
                Check(core.BorderBrush.IsFrozen && core.Opacity > 0, "Core remains visible: " + status);
                if (status is NeonFrameStatus.Disabled or NeonFrameStatus.Offline or NeonFrameStatus.Unknown)
                    Check(!glows.HasAnimatedProperties, "Nonanimated state: " + status);
            }
            AetherNeonFrame.SetStatus(border, NeonFrameStatus.Normal);
            AetherNeonFrame.SetBreathingEnabled(border, false);
            Check(!glows.HasAnimatedProperties && !AetherNeonFrame.GetMotionActive(border) && core.Opacity > 0,
                "Motion off removes clock but retains core");
            AetherNeonFrame.SetBreathingEnabled(border, true);
            foreach (var property in new[] { AetherNeonFrame.ReducedMotionProperty, AetherNeonFrame.IsActiveProperty, AetherNeonFrame.IsVisibleProperty })
            {
                var pausedValue = property == AetherNeonFrame.ReducedMotionProperty;
                border.SetValue(property, pausedValue);
                Check(!glows.HasAnimatedProperties, "Motion gate removes clock: " + property.Name);
                border.SetValue(property, !pausedValue);
            }
            window.WindowState = WindowState.Minimized;
            Check(!glows.HasAnimatedProperties, "Minimizing stops clocks independently of telemetry");
            window.WindowState = WindowState.Normal;
            window.Hide();
            Check(!glows.HasAnimatedProperties, "Hidden host has no breathing clock");
            window.Show();
            border.Visibility = Visibility.Collapsed;
            Check(!glows.HasAnimatedProperties, "Collapsed owner stops clock");
            border.Visibility = Visibility.Visible;
            foreach (var modern in new[] { true, false })
            {
                AetherNeonFrame.SetThemeMode(border, modern ? NeonFrameTheme.Modern : NeonFrameTheme.Auto);
                Check(glows.Visibility == (modern ? Visibility.Collapsed : Visibility.Visible), "Explicit modern suppresses halos");
                if (modern) Check(!glows.HasAnimatedProperties, "Modern has no neon clocks");
            }
            foreach (var skin in SkinCatalog.All)
            {
                SkinCatalog.Apply(Application.Current, skin.Id);
                Check(!SkinCatalog.IsModern || glows.Visibility == Visibility.Collapsed && !glows.HasAnimatedProperties,
                    "Skin integration: " + skin.Id);
            }
            SkinCatalog.Apply(Application.Current, "retro-neon");
            border.CornerRadius = new CornerRadius(8, 2, 8, 2);
            window.Width += 80;
            window.UpdateLayout();
            Check(core.CornerRadius == border.CornerRadius, "Dynamic corners refresh without replacing owner");
            for (var i = 0; i < 20; i++)
            {
                var old = Adorner();
                root.Child = null;
                NeonFrameProof.Pump(TimeSpan.FromMilliseconds(5));
                Check(!old.SubscriptionsActive && !((Grid)((Grid)VisualTreeHelper.GetChild(old, 0)).Children[0]).HasAnimatedProperties,
                    "Unload removes all clock and external subscriptions");
                Check(root.AdornerLayer.GetAdorners(border) is not { Length: > 0 }, "Unloaded adorner removed");
                root.Child = border;
                NeonFrameProof.Pump(TimeSpan.FromMilliseconds(5));
                Check(root.AdornerLayer.GetAdorners(border)?.Length == 1, "Reload creates one adorner, never accumulates");
            }
            AetherNeonFrame.SetEnabled(border, false);
            Check(root.AdornerLayer.GetAdorners(border) is not { Length: > 0 }, "Disable removes adorner");
            Check(ReferenceEquals(border.Child, text), "Content and geometry untouched");
        }
        finally { SkinCatalog.Apply(Application.Current, "retro-neon"); AetherNeonFrame.SetEnabled(border, false); window.Close(); }
        return count;

        AetherNeonFrame.NeonAdorner Adorner() => root.AdornerLayer.GetAdorners(border)?.OfType<AetherNeonFrame.NeonAdorner>().Single()
            ?? throw new InvalidOperationException("Neon adorner missing.");
    }

    internal static int Gallery(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || Directory.Exists(directory))
            throw new ArgumentException("Choose a new absolute gallery directory.");
        Directory.CreateDirectory(directory);
        var count = 0;
        foreach (var scale in new[] { 1d, 1.5, 2d })
        foreach (var mode in new[] { "night", "day", "motion-off", "modern-light", "modern-dark", "vista-aero", "candy-gloss" })
        {
            SkinCatalog.Apply(Application.Current, mode.StartsWith("modern", StringComparison.Ordinal) ? "windows-modern" :
                mode is "vista-aero" or "candy-gloss" ? mode : "retro-neon", mode == "modern-light");
            var grid = new UniformGrid { Columns = 3, Margin = new Thickness(24) };
            foreach (var status in Enum.GetValues<NeonFrameStatus>())
            {
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = status.ToString().ToUpperInvariant(), FontWeight = FontWeights.Bold, FontSize = 17 });
                content.Children.Add(new TextBlock { Text = "CPU 42 % · RAM 31 %\nSynthetische Daten", FontSize = 13, Margin = new Thickness(0, 10, 0, 0) });
                content.Children.Add(new TextBox { Text = "Text bleibt scharf", Margin = new Thickness(0, 10, 0, 0) });
                var border = new Border { Child = content, CornerRadius = new CornerRadius(18, 5, 18, 5),
                    Padding = new Thickness(20), Margin = new Thickness(12) };
                border.SetResourceReference(Border.BackgroundProperty, "GlossPanel");
                content.SetResourceReference(TextBlock.ForegroundProperty, "ModernTextPrimary");
                AetherNeonFrame.SetEnabled(border, true);
                AetherNeonFrame.SetStatus(border, status);
                AetherNeonFrame.SetBreathingEnabled(border, mode != "motion-off");
                grid.Children.Add(border);
            }
            var root = new AdornerDecorator { Child = grid, Width = 1000, Height = 680 };
            var window = new Window { Content = root, Width = 1040, Height = 760, ShowInTaskbar = false };
            window.SetResourceReference(Control.BackgroundProperty, "WindowBackdrop");
            if (mode == "day") DisplayTime.Apply(window, true);
            try
            {
                window.Show();
                NeonFrameProof.Pump(TimeSpan.FromMilliseconds(80));
                var bitmap = new RenderTargetBitmap((int)(1000 * scale), (int)(680 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var path = Path.Combine(directory, $"{mode}-{scale * 100:0}dpi.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new FileStream(path, FileMode.CreateNew);
                encoder.Save(stream);
                Console.WriteLine("FRAME IMAGE: " + path);
                count++;
            }
            finally
            {
                foreach (Border border in grid.Children) AetherNeonFrame.SetEnabled(border, false);
                window.Close();
            }
        }
        SkinCatalog.Apply(Application.Current, "retro-neon");
        return count;
    }
}
