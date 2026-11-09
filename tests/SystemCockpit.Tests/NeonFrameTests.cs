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
            Check(visuals.Children.Count == 2 && glows.Children.OfType<Border>().Count() == 2,
                "Only complete border contours exist; no dash shapes or moving geometry");
            Check(!typeof(AetherNeonFrame.NeonAdorner).GetFields(System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).Any(field => field.FieldType.Name.Contains("Timer", StringComparison.Ordinal)),
                "Frame owner has no timer");
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
            border.IsEnabled = false;
            Check(!glows.HasAnimatedProperties, "Disabled owner has no breathing clock");
            border.IsEnabled = true;
            border.Margin = new Thickness(5);
            window.UpdateLayout();
            Check(glows.Children.OfType<Border>().All(b => b.Effect is System.Windows.Media.Effects.BlurEffect { Radius: <= 3 }),
                "Halo contracts within existing edge clearance without moving content");
            border.Margin = new Thickness(24);
            window.UpdateLayout();
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
            var released = new List<WeakReference>();
            for (var i = 0; i < 20; i++)
            {
                var old = Adorner();
                if (i > 0) released.Add(new WeakReference(old));
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
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Check(released.Take(released.Count - 1).All(reference => !reference.IsAlive),
                "Removed adorners are collectible; static and window events do not retain them");
            Check(ReferenceEquals(border.Child, text), "Content and geometry untouched");
        }
        finally { SkinCatalog.Apply(Application.Current, "retro-neon"); AetherNeonFrame.SetEnabled(border, false); window.Close(); }
        count += DpiGeometry();
        return count;

        AetherNeonFrame.NeonAdorner Adorner() => root.AdornerLayer.GetAdorners(border)?.OfType<AetherNeonFrame.NeonAdorner>().Single()
            ?? throw new InvalidOperationException("Neon adorner missing.");
    }

    private static int DpiGeometry()
    {
        var count = 0;
        foreach (var scale in new[] { 1d, 1.5, 2d })
        foreach (var width in new[] { 180d, 480d })
        {
            var border = new Border { Margin = new Thickness(5), Background = Brushes.Black,
                CornerRadius = new CornerRadius(18, 4, 18, 4) };
            var root = new AdornerDecorator { Child = border, Width = width, Height = 160 };
            VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
            AetherNeonFrame.SetEnabled(border, true);
            try
            {
                root.Measure(new Size(width, 160));
                root.Arrange(new Rect(0, 0, width, 160));
                border.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                root.UpdateLayout();
                if (VisualTreeHelper.GetDpi(border).DpiScaleX != scale)
                    throw new InvalidOperationException("Synthetic DPI did not propagate to frame.");
                count++;
                var adorner = root.AdornerLayer.GetAdorners(border)?.Single()
                    ?? throw new InvalidOperationException("DPI frame missing.");
                if (adorner.RenderSize != border.RenderSize || border.ActualWidth != width - 10)
                    throw new InvalidOperationException("DPI frame changed content geometry.");
                count++;
                var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(160 * scale),
                    96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var stride = bitmap.PixelWidth * 4;
                var pixels = new byte[stride * bitmap.PixelHeight];
                bitmap.CopyPixels(pixels, stride, 0);
                var lit = false;
                for (var y = 0; y < bitmap.PixelHeight; y++)
                for (var x = 0; x < bitmap.PixelWidth; x++)
                {
                    var alpha = pixels[y * stride + x * 4 + 3];
                    if ((x == 0 || y == 0 || x == bitmap.PixelWidth - 1 || y == bitmap.PixelHeight - 1) && alpha != 0)
                        throw new InvalidOperationException("Glow reaches/clips the render edge at scale " + scale);
                    lit |= alpha != 0;
                }
                if (!lit) throw new InvalidOperationException("DPI render is empty.");
                count += 2;
            }
            finally { AetherNeonFrame.SetEnabled(border, false); }
        }
        return count;
    }

    internal static int Motion()
    {
        var border = new Border { Margin = new Thickness(24), BorderBrush = Brushes.Cyan, Background = Brushes.Black };
        var root = new AdornerDecorator { Child = border };
        var window = new Window { Content = root, Width = 480, Height = 300, ShowInTaskbar = false };
        AetherNeonFrame.SetEnabled(border, true);
        var count = 0;
        try
        {
            window.Show();
            NeonFrameProof.Pump(TimeSpan.FromMilliseconds(100));
            var adorner = root.AdornerLayer.GetAdorners(border)!.Single();
            var visuals = (Grid)VisualTreeHelper.GetChild(adorner, 0);
            var glows = (Grid)visuals.Children[0];
            var core = (Border)visuals.Children[1];
            if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            {
                if (glows.HasAnimatedProperties) throw new InvalidOperationException("System motion restriction ignored.");
                Console.WriteLine("MOTION: system restriction active; no animated timing exercised.");
                return 1;
            }
            var size = border.RenderSize;
            var samples = new List<double>();
            for (var second = 0; second < 15; second++)
            {
                samples.Add(glows.Opacity);
                if (glows.Opacity is < .85 or > 1 || core.Opacity != 1 || core.HasAnimatedProperties || border.RenderSize != size)
                    throw new InvalidOperationException("Breathing moved geometry/core or exceeded its intensity bounds.");
                count++;
                NeonFrameProof.Pump(TimeSpan.FromSeconds(1));
            }
            if (samples.Max() - samples.Min() < .03) throw new InvalidOperationException("Breathing intensity never changed.");
            count++;
            foreach (var status in new[] { NeonFrameStatus.Notice, NeonFrameStatus.Error, NeonFrameStatus.Analysis })
            {
                AetherNeonFrame.SetBreathingDuration(border, TimeSpan.FromSeconds(8));
                AetherNeonFrame.SetStatus(border, status);
                if (!glows.HasAnimatedProperties) throw new InvalidOperationException("Status pulse did not start: " + status);
                NeonFrameProof.Pump(TimeSpan.FromSeconds(status == NeonFrameStatus.Notice ? 8.5 : 16.5));
                if (glows.HasAnimatedProperties || AetherNeonFrame.GetMotionActive(border) || core.Opacity == 0)
                    throw new InvalidOperationException("Finite status pulse retained a clock or hid the frame: " + status);
                count += 2;
            }
        }
        finally { AetherNeonFrame.SetEnabled(border, false); window.Close(); }
        return count;
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
                var path = Path.Combine(directory, $"{mode}-{scale * 100:0}pct.png");
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
