using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SystemCockpit;
using SystemCockpit.Services;

internal static class BootTests
{
    public static int Run()
    {
        var sensors = new List<SensorReading>
        {
            new("/gpu-intel/0/load/0", "Intel Graphics", "GPU Core", "Load", 99, "%"),
            new("/gpu-intel/0/temperature/0", "Intel Graphics", "GPU Core", "Temperature", 60, "C"),
            new("/gpu-nvidia/0/load/0", "NVIDIA GeForce RTX 4080 SUPER", "GPU Core", "Load", 35, "%"),
            new("/gpu-nvidia/0/temperature/0", "NVIDIA GeForce RTX 4080 SUPER", "GPU Core", "Temperature", 28, "C"),
            new("/gpu-nvidia/0/temperature/1", "NVIDIA GeForce RTX 4080 SUPER", "GPU Hot Spot", "Temperature", 38, "C"),
            new("/gpu-nvidia/1/load/0", "Other NVIDIA GPU", "GPU Core", "Load", 10, "%")
        };
        var selected = CockpitModel.SelectGpu(sensors);
        if (selected.Count != 3 || selected.Any(s => !s.Id.StartsWith("/gpu-nvidia/0/")))
            throw new InvalidOperationException("Discrete RTX must be selected as one device, without mixing iGPU/secondary GPU");
        var model = new CockpitModel();
        model.Apply(new(DateTimeOffset.Now, 20, 30, 10, 32, 1, 2, [], sensors), true);
        if (model.Cards[2].Value != 35d.ToString("0.0") || !model.GpuIdentity.Contains("RTX 4080 SUPER") ||
            !model.GpuTemperature.StartsWith("28") || !model.Instruments.Single(c => c.Name == "GPU / HOTSPOT").Value.StartsWith("38"))
            throw new InvalidOperationException("RTX identity, GPU load, temperature and hotspot not aligned");
        sensors.RemoveAll(s => s.Id.StartsWith("/gpu-nvidia/0/temperature"));
        model.Apply(new(DateTimeOffset.Now, 20, 30, 10, 32, 1, 2, [], sensors), true);
        if (model.GpuTemperature != "Nicht verfuegbar") throw new InvalidOperationException("Missing RTX temperature must not silently use Intel temperature");
        if (CockpitModel.SelectGpu([]).Count != 0) throw new InvalidOperationException("Missing GPU fabricated");
        model.Apply(new(DateTimeOffset.Now, 20, 30, 10, 32, 1, 2, [], []), false);
        if (model.Cards[2].Value != "--" || !model.GpuIdentity.Contains("ausgeschaltet"))
            throw new InvalidOperationException("Disabled hardware sensor state not explicit");
        var icon = Application.GetResourceStream(new Uri("/SystemCockpit;component/Assets/aether.ico", UriKind.Relative)) ??
            throw new InvalidOperationException("Embedded icon missing");
        using var reader = new BinaryReader(icon.Stream);
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != 7)
            throw new InvalidOperationException("Windows icon must have seven resolution entries");
        foreach (var expected in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            if ((width == 0 ? 256 : width) != expected || (height == 0 ? 256 : height) != expected)
                throw new InvalidOperationException("Icon dimensions mismatch");
            reader.ReadBytes(14);
        }
        icon.Stream.Position = 0;
        var decoder = BitmapDecoder.Create(icon.Stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        foreach (var frame in decoder.Frames) VerifyIconContrast(frame);
        return 13;
    }

    private static void VerifyIconContrast(BitmapSource source)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var bright = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] > 180 && Math.Max(pixels[i + 1], pixels[i + 2]) > 180) bright++;
        if (bright < bitmap.PixelWidth * bitmap.PixelHeight / 10)
            throw new InvalidOperationException($"Application icon too faint at {bitmap.PixelWidth} pixels");
    }

    public static int BrandingUi(string? screenshotPrefix)
    {
        var count = 0;
        void Check(bool valid, string message) { count++; if (!valid) throw new InvalidOperationException(message); }
        void VerifyLogo(Image image)
        {
            Check(image.Source is BitmapSource { PixelWidth: > 100, PixelHeight: > 100 },
                "Actual compiled window must decode the embedded AETHER logo");
            var source = (BitmapSource)image.Source;
            var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var bright = 0;
            for (var i = 0; i < pixels.Length; i += 4)
                if (pixels[i + 3] > 100 && Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) > 160) bright++;
            Check(bright > bitmap.PixelWidth * bitmap.PixelHeight / 20, "Decoded logo contains the actual luminous brand pixels");
            Check(image.ActualWidth >= 90 && image.ActualHeight >= 90 && image.IsVisible && image.Opacity == 1,
                "Logo is visible at readable dimensions, not clipped or hidden");
            var window = Window.GetWindow(image);
            Check(new Rect(window.RenderSize).Contains(
                    image.TransformToAncestor(window).TransformBounds(new Rect(image.RenderSize))),
                "Logo lies inside the visible window");
        }
        void CaptureSynthetic(FrameworkElement content, Window original, object? model,
            double width, double height, string suffix, Action<Window> verify)
        {
            var host = new Window { Content = content, DataContext = model, Width = width, Height = height, ShowInTaskbar = false,
                Background = original.Background, Foreground = original.Foreground, FontFamily = original.FontFamily };
            host.Resources.MergedDictionaries.Add(original.Resources);
            if (original is MainWindow mainWindow)
            {
                var now = DateTimeOffset.Now;
                var day = DisplayTime.IsDay(mainWindow.Model.Settings.DisplayMode, now);
                mainWindow.Model.UpdateDisplayTime(now);
                mainWindow.Model.IsDaytime = day;
                DisplayTime.Apply(mainWindow, day);
                DisplayTime.Apply(host, day);
            }
            try
            {
                host.Show();
                Pump();
                verify(host);
                Capture(host, screenshotPrefix is null ? null : screenshotPrefix + suffix + ".png");
            }
            finally { host.Close(); Pump(); }
        }
        var boot = new BootWindow();
        try
        {
            var content = (FrameworkElement)boot.Content;
            boot.Content = null;
            CaptureSynthetic(content, boot, null, 980, 780, "-boot",
                _ => VerifyLogo((Image)boot.FindName("BootLogo")));
        }
        finally { boot.CloseAfterStartup(); }
        var main = new MainWindow();
        try
        {
            var content = (FrameworkElement)main.Content;
            main.Content = null;
            CaptureSynthetic(content, main, main.Model, 1100, 760, "-dashboard", host =>
            {
                var brand = (SystemCockpit.Controls.AnimatedBrand)main.FindName("DashboardBrand");
                main.Model.MotionEnabled = false; Pump();
                VerifyLogo((Image)brand.FindName("Logo"));
                Check(((TextBlock)brand.FindName("Wordmark")).FontStyle == FontStyles.Italic,
                    "Dashboard wordmark uses the slanted retro chrome treatment");
                main.Model.MotionEnabled = false; Pump();
                Check(!brand.MotionActive, "Brand animation stops with application motion disabled");
                main.Model.MotionEnabled = true; Pump();
                Check(brand.MotionActive == (SystemParameters.ClientAreaAnimation && !SkinCatalog.IsModern),
                    "Brand motion follows Windows animation permission and desktop design");
                Check(((Image)brand.FindName("Logo")).HasAnimatedProperties == brand.MotionActive &&
                    ((TranslateTransform)brand.FindName("Sweep")).HasAnimatedProperties == brand.MotionActive,
                    "Logo pulse and reflection have animation clocks only when motion is permitted");
                brand.Visibility = Visibility.Hidden; Pump();
                Check(!brand.MotionActive && !((Image)brand.FindName("Logo")).HasAnimatedProperties,
                    "Hidden branding removes its animation clocks");
                brand.Visibility = Visibility.Visible; Pump();
                main.Model.MotionEnabled = false; Pump();
                Check(!brand.MotionActive && !((TranslateTransform)brand.FindName("Sweep")).HasAnimatedProperties,
                    "Disabling branding motion removes reflection clocks");
                var pages = (TabControl)main.FindName("Pages");
                Check(pages.ActualWidth > 600 && pages.ActualHeight > 350,
                    "Dashboard retains usable content dimensions at minimum supported test window");
                Check(host.Content == content, "Dashboard is rendered without starting the production MainWindow services");
            });
        }
        finally { main.Close(); }
        var model = new CockpitModel { MotionEnabled = false };
        var bar = new SidebarWindow(model, model.Settings, _ => throw new InvalidOperationException("Synthetic bar must not save settings"));
        try
        {
            var content = (FrameworkElement)bar.Content;
            bar.Content = null;
            CaptureSynthetic(content, bar, model, 350, 760, "-bar", _ =>
            {
                var frame = (Border)bar.FindName("BarNeonFrame");
                var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(frame);
                Check(layer?.GetAdorners(frame)?.Length == 1 && !SystemCockpit.Controls.AetherNeonFrame.GetMotionActive(frame),
                    "Bar has a static halo with motion disabled and its own adorner layer");
                model.MotionEnabled = true;
                Pump();
                Check(SystemCockpit.Controls.AetherNeonFrame.GetMotionActive(frame) == SystemParameters.ClientAreaAnimation,
                    "Bar motion follows both the AETHER switch and Windows animation permission");
                model.MotionEnabled = false;
                Pump();
                Check(!SystemCockpit.Controls.AetherNeonFrame.GetMotionActive(frame), "Disabling bar motion immediately removes breathing clocks");
                var curves = ((DataTemplate)bar.Resources["CurveTemplate"]).LoadContent() as Viewbox;
                var canvas = (Canvas)curves!.Child;
                Check(canvas.Children.OfType<System.Windows.Shapes.Polyline>().Count() == 3,
                    "Bar curve has layered glow without an effect on labels or measurement text");
            });
        }
        finally { bar.Close(); }
        return count;
    }

    public static int LiveUi(string? bootScreenshot, string? cockpitScreenshot)
    {
        var boot = new BootWindow();
        var main = new MainWindow();
        var shown = false;
        boot.Show();
        try
        {
            boot.Report("[TEST HOST] Nicht erhoeht; Produktstart verlangt weiterhin UAC", 3);
            var initialization = main.Dispatcher.InvokeAsync(() => main.InitializeHardwareAsync(boot.Report, boot.Token)).Task.Unwrap();
            Wait(initialization, TimeSpan.FromSeconds(60));
            var warnings = initialization.GetAwaiter().GetResult();
            var model = (CockpitModel)main.DataContext;
            if (!model.GpuIdentity.Contains("RTX 4080 SUPER") || model.Cards[2].Value == "--" ||
                model.GpuTemperature is "Nicht verfuegbar" or "Nicht aktiviert")
                throw new InvalidOperationException("Real RTX readings missing after automatic boot initialization");
            if (((CheckBox)main.FindName("SensorToggle")).IsChecked != true ||
                !boot.Transcript.Contains("[SENSOR / OK] NVIDIA GeForce RTX 4080 SUPER") ||
                !boot.Transcript.Contains("[MODULE / LOADED]") || !boot.Transcript.Contains("[MODULE / ON DEMAND]"))
                throw new InvalidOperationException("Boot console must report actual sensor and module initialization");
            boot.Report("[TEST RESULT] Echte RTX-Sensoren gelesen / keine Systemreparatur ausgefuehrt", 97);
            Capture(boot, bootScreenshot);
            var completion = boot.FinishAsync(warnings);
            if (warnings) ((Button)boot.FindName("ContinueButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(completion, TimeSpan.FromSeconds(5));
            main.SetBootTranscript(boot.Transcript);
            main.Show();
            shown = true;
            Pump();
            boot.CloseAfterStartup();
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(4)) { Pump(); Thread.Sleep(20); }
            Capture(main, cockpitScreenshot);
            var center = new ControlCenterWindow(model);
            try
            {
                center.SetBootTranscript(boot.Transcript);
                if (!((TextBox)center.FindName("Output")).Text.Contains("[BOOT JOURNAL / NUR RAM]"))
                    throw new InvalidOperationException("Boot transcript unavailable in action console");
            }
            finally { center.Close(); Pump(); }
            var warningBoot = new BootWindow();
            warningBoot.Show();
            try
            {
                warningBoot.Report("[WARN] Simulierter nicht verfuegbarer Sensor; keine Erfolgsmeldung", 90);
                var warningTask = warningBoot.FinishAsync(true);
                if (warningTask.IsCompleted || ((Button)warningBoot.FindName("ContinueButton")).Visibility != Visibility.Visible)
                    throw new InvalidOperationException("Boot warnings must remain available until acknowledged");
                ((Button)warningBoot.FindName("ContinueButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Wait(warningTask, TimeSpan.FromSeconds(3));
            }
            finally { warningBoot.CloseAfterStartup(); }
            var lifetime = Stopwatch.StartNew();
            var timedBoot = new BootWindow();
            timedBoot.Show();
            try
            {
                Wait(timedBoot.FinishAsync(false), TimeSpan.FromSeconds(5));
                if (lifetime.Elapsed.TotalSeconds < 3.55)
                    throw new InvalidOperationException("Successful boot display must last doubled minimum 3.6 seconds");
            }
            finally { timedBoot.CloseAfterStartup(); }
            var cancelBoot = new BootWindow();
            cancelBoot.Show();
            cancelBoot.Close();
            if (!cancelBoot.Token.IsCancellationRequested) throw new InvalidOperationException("Closing boot must request cancellation");
            cancelBoot.CloseAfterStartup();
            Console.WriteLine($"BOOT / GPU: {model.GpuIdentity} / load {model.Cards[2].Value}% / {model.GpuTemperature}; " +
                "real automatic initialization, CRT boot/log, warning acknowledgement, cancellation and logo resources verified");
            return 9;
        }
        finally
        {
            if (boot.IsVisible) boot.CloseAfterStartup();
            if (shown)
            {
                main.Close();
                var timer = Stopwatch.StartNew();
                while (main.IsVisible && timer.Elapsed < TimeSpan.FromSeconds(15)) { Pump(); Thread.Sleep(20); }
                if (main.IsVisible) throw new InvalidOperationException("Boot UI test left cockpit running");
            }
            else Wait(main.ReleaseBeforeStartupAsync(), TimeSpan.FromSeconds(10));
        }
    }

    private static void Wait(Task task, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < timeout) { Pump(); Thread.Sleep(20); }
        if (!task.IsCompleted) throw new TimeoutException("Boot UI test exceeded bounded wait");
        task.GetAwaiter().GetResult();
    }

    private static void Capture(Window window, string? path)
    {
        if (path is null) return;
        Pump();
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
