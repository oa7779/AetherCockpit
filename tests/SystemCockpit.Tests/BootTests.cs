using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Resources;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
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
        if (model.GpuTemperature != "Nicht verfügbar") throw new InvalidOperationException("Missing RTX temperature must not silently use Intel temperature");
        if (CockpitModel.SelectGpu([]).Count != 0) throw new InvalidOperationException("Missing GPU fabricated");
        model.Apply(new(DateTimeOffset.Now, 20, 30, 10, 32, 1, 2, [], []), false);
        if (model.Cards[2].Value != "--" || !model.GpuIdentity.Contains("ausgeschaltet"))
            throw new InvalidOperationException("Disabled hardware sensor state not explicit");
        var icon = Application.GetResourceStream(new Uri("/SystemCockpit;component/Assets/aether.ico", UriKind.Relative)) ??
            throw new InvalidOperationException("Embedded icon missing");
        using var reader = new BinaryReader(icon.Stream);
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != 9)
            throw new InvalidOperationException("Windows icon must have nine resolution entries");
        foreach (var expected in new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 })
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
        return 13 + RepositoryTestPathTests.Run() + VerifyTaskbarContract(decoder.Frames);
    }

    private static int VerifyTaskbarContract(IReadOnlyList<BitmapFrame> iconFrames)
    {
        var executable = Environment.GetEnvironmentVariable("AETHER_TASKBAR_EXE");
        executable = string.IsNullOrWhiteSpace(executable)
            ? Path.Combine(AppContext.BaseDirectory, "SystemCockpit.exe")
            : Path.GetFullPath(executable);
        if (!File.Exists(executable))
            throw new FileNotFoundException("AETHER executable for the taskbar identity test not found.", executable);
        using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(executable) ??
            throw new InvalidOperationException("Published AETHER executable has no associated icon"))
        using (var bitmap = icon.ToBitmap())
        {
            if (!MatchesIcon(ToBitmapSource(bitmap), iconFrames))
                throw new InvalidOperationException("Published AETHER executable icon differs from the AETHER ICO resource");
        }

        var result = TaskbarIdentity.SetForCurrentProcess();
        if (result < 0 || TaskbarIdentity.GetForCurrentProcess() != TaskbarIdentity.AppUserModelId)
            throw new InvalidOperationException("Process AppUserModelID differs from the AETHER taskbar identity");

        var window = new Window { Icon = iconFrames.Last(), ShowInTaskbar = true };
        try
        {
            new WindowInteropHelper(window).EnsureHandle();
            var actual = TaskbarIdentity.EnsureWindowIdentity(window);
            if (actual.WindowAppId != actual.ProcessAppId || actual.WindowAppId != TaskbarIdentity.AppUserModelId ||
                actual.BigIcon == "0x0" || actual.SmallIcon == "0x0" ||
                actual.ClassBigIcon == "0x0" || actual.ClassSmallIcon == "0x0")
                throw new InvalidOperationException("HWND identity or large/small window and class icons are missing");
            var repeated = TaskbarIdentity.EnsureWindowIdentity(window);
            if (repeated.ClassBigIcon != actual.ClassBigIcon || repeated.ClassSmallIcon != actual.ClassSmallIcon)
                throw new InvalidOperationException("Repeated HWND identity checks changed existing class icons");
        }
        finally { window.Close(); }

        var installerPath = RepositoryTestPaths.FindFile("build", "AetherCockpit.iss");
        var repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(installerPath)!, ".."));
        var appSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SystemCockpit", "App.xaml.cs"));
        var setIdentity = appSource.IndexOf("Services.TaskbarIdentity.SetForCurrentProcess()", StringComparison.Ordinal);
        var startupBase = appSource.IndexOf("base.OnStartup(e)", StringComparison.Ordinal);
        if (setIdentity < 0 || startupBase < 0 || setIdentity > startupBase)
            throw new InvalidOperationException("AETHER taskbar identity must be assigned before startup can create windows");

        var shortcuts = File.ReadAllLines(installerPath)
            .Where(line => line.Contains("SystemCockpit.exe", StringComparison.Ordinal) &&
                line.Contains("Name:", StringComparison.Ordinal))
            .ToArray();
        if (shortcuts.Length != 2 || shortcuts.Any(line =>
                !line.Contains("IconFilename: \"{app}\\SystemCockpit.exe\"", StringComparison.Ordinal) ||
                !line.Contains("IconIndex: 0", StringComparison.Ordinal) ||
                !line.Contains($"AppUserModelID: \"{TaskbarIdentity.AppUserModelId}\"", StringComparison.Ordinal)))
            throw new InvalidOperationException("AETHER start-menu and desktop shortcuts must explicitly use the app icon and matching identity");
        return 4;
    }

    private static int VerifyLogoResourceContract(string repositoryRoot)
    {
        var assertions = 0;
        void Check(bool valid, string message)
        {
            assertions++;
            if (!valid) throw new InvalidOperationException(message);
        }

        const string logoUri = "pack://application:,,,/SystemCockpit;component/Assets/aether-logo.png";
        const string logoKey = "Assets/aether-logo.png";
        var logoPath = Path.GetFullPath(Path.Combine(repositoryRoot, "src", "SystemCockpit", "Assets", "aether-logo.png"));
        var assembly = typeof(MainWindow).Assembly;
        var contentPaths = assembly.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType.FullName ==
                "System.Windows.Resources.AssemblyAssociatedContentFileAttribute")
            .Select(attribute => attribute.ConstructorArguments[0].Value as string)
            .Where(path => path is not null)
            .ToArray();
        Check(!contentPaths.Any(path => path!.Replace('\\', '/').Equals("assets/aether-logo.png", StringComparison.OrdinalIgnoreCase)),
            "The product assembly must not classify the embedded logo as an associated content file");

        var resourceContainer = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".g.resources", StringComparison.Ordinal));
        using (var stream = assembly.GetManifestResourceStream(resourceContainer) ??
            throw new InvalidOperationException("WPF resource container missing from SystemCockpit.dll"))
        using (var reader = new ResourceReader(stream))
        {
            var logoKeys = reader.Cast<DictionaryEntry>()
                .Where(entry => entry.Key is string key &&
                    key.Replace('\\', '/').Equals(logoKey, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Check(logoKeys.Length == 1, "The logo must exist exactly once in the canonical WPF resource container");
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "src", "SystemCockpit", "SystemCockpit.csproj"));
        startInfo.ArgumentList.Add("-getItem:Resource,Content,None,EmbeddedResource,ApplicationDefinition,Page");
        startInfo.ArgumentList.Add("-getProperty:OutputType,AssemblyName");
        startInfo.ArgumentList.Add("-property:Configuration=Release");
        startInfo.ArgumentList.Add("-property:RuntimeIdentifier=win-x64");
        startInfo.ArgumentList.Add("-property:PublishProfile=PortableBeta");
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start MSBuild item evaluation for the logo contract");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"MSBuild logo contract evaluation failed: {error}");

        using (var document = JsonDocument.Parse(output))
        {
            var root = document.RootElement;
            var properties = root.GetProperty("Properties");
            Check(properties.GetProperty("OutputType").GetString() == "WinExe" &&
                properties.GetProperty("AssemblyName").GetString() == "SystemCockpit",
                "The logo resource contract must be evaluated for the SystemCockpit WinExe");
            var items = root.GetProperty("Items");
            int CountLogoItems(string group) => items.GetProperty(group).EnumerateArray().Count(item =>
                item.TryGetProperty("FullPath", out var path) &&
                string.Equals(Path.GetFullPath(path.GetString()!), logoPath, StringComparison.OrdinalIgnoreCase));
            var resourceItems = items.GetProperty("Resource").EnumerateArray()
                .Where(item => item.TryGetProperty("FullPath", out var path) &&
                    string.Equals(Path.GetFullPath(path.GetString()!), logoPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var logicalName = resourceItems.Length == 1 && resourceItems[0].TryGetProperty("LogicalName", out var logicalNameProperty)
                ? logicalNameProperty.GetString()
                : null;
            var contentCount = CountLogoItems("Content");
            var noneCount = CountLogoItems("None");
            var otherCount = CountLogoItems("EmbeddedResource") +
                CountLogoItems("ApplicationDefinition") + CountLogoItems("Page");
            Check(resourceItems.Length == 1 && logicalName?.Replace('\\', '/') == logoKey &&
                contentCount == 0 && noneCount == 0 && otherCount == 0,
                $"Expected one Resource with LogicalName '{logoKey}' and no competing items; got Resource={resourceItems.Length}, Content={contentCount}, None={noneCount}, Other={otherCount}, LogicalName='{logicalName}'");
        }

        var originalDirectory = Environment.CurrentDirectory;
        var isolatedDirectory = Path.Combine(Path.GetTempPath(), "AetherLogoResource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedDirectory);
        try
        {
            Environment.CurrentDirectory = isolatedDirectory;
            Check(!Directory.Exists(Path.Combine(isolatedDirectory, "Assets")) &&
                !File.Exists(Path.Combine(isolatedDirectory, "SystemCockpit.csproj")),
                "Logo loading must not require a repository or a physical Assets directory in the working directory");

            var embeddedLogo = Application.GetResourceStream(new Uri(logoUri, UriKind.Absolute)) ??
                throw new InvalidOperationException("Absolute pack URI did not resolve the embedded logo");
            using (embeddedLogo.Stream)
            {
                var decoder = BitmapDecoder.Create(embeddedLogo.Stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Check(decoder.Frames.Count == 1 && decoder.Frames[0].PixelWidth > 100 && decoder.Frames[0].PixelHeight > 100,
                    "The embedded logo must decode from the application pack URI without a physical file");
            }

            var boot = new BootWindow();
            try
            {
                Check(boot.FindName("BootLogo") is Image { Source: BitmapSource { PixelWidth: > 100, PixelHeight: > 100 } },
                    "BootWindow must load its logo from the embedded WPF resource");
            }
            finally { boot.CloseAfterStartup(); }

            var main = new MainWindow();
            try
            {
                var brand = (SystemCockpit.Controls.AnimatedBrand)main.FindName("DashboardBrand");
                Check(brand.FindName("Logo") is Image { Source: BitmapSource { PixelWidth: > 100, PixelHeight: > 100 } },
                    "MainWindow must load dashboard branding from the embedded WPF resource");
            }
            finally { main.Close(); }
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            Directory.Delete(isolatedDirectory);
        }

        return assertions;
    }

    public static int PublishedResourceContract()
    {
        var assertions = 0;
        void Check(bool valid, string message)
        {
            assertions++;
            if (!valid) throw new InvalidOperationException(message);
        }

        const string logoUri = "pack://application:,,,/SystemCockpit;component/Assets/aether-logo.png";
        var assembly = typeof(MainWindow).Assembly;
        Check(Path.GetFileName(assembly.Location).Equals("SystemCockpit.dll", StringComparison.OrdinalIgnoreCase),
            "Published branding probe must load the staged SystemCockpit.dll");
        Check(string.Equals(Path.GetFullPath(assembly.Location),
            Path.Combine(AppContext.BaseDirectory, "SystemCockpit.dll"), StringComparison.OrdinalIgnoreCase),
            "Published branding probe must load SystemCockpit.dll from its isolated application directory");
        var logoContentAttributes = assembly.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType.FullName ==
                "System.Windows.Resources.AssemblyAssociatedContentFileAttribute")
            .Select(attribute => attribute.ConstructorArguments[0].Value as string)
            .Any(path => path is not null &&
                path.Replace('\\', '/').Equals("assets/aether-logo.png", StringComparison.OrdinalIgnoreCase));
        Check(!logoContentAttributes, "Published SystemCockpit.dll must not declare the logo as associated content");

        var resourceContainer = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".g.resources", StringComparison.Ordinal));
        using (var stream = assembly.GetManifestResourceStream(resourceContainer) ??
            throw new InvalidOperationException("Published WPF resource container missing"))
        using (var reader = new ResourceReader(stream))
        {
            var logoKeyCount = reader.Cast<DictionaryEntry>()
                .Count(entry => entry.Key is string key &&
                    key.Replace('\\', '/').Equals("Assets/aether-logo.png", StringComparison.OrdinalIgnoreCase));
            Check(logoKeyCount == 1, "Published assembly must contain exactly one canonical embedded logo resource");
        }

        var originalDirectory = Environment.CurrentDirectory;
        var isolatedDirectory = Path.Combine(Path.GetTempPath(), "AetherPublishedLogo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedDirectory);
        try
        {
            Environment.CurrentDirectory = isolatedDirectory;
            Check(!Directory.Exists(Path.Combine(isolatedDirectory, "Assets")) &&
                !File.Exists(Path.Combine(isolatedDirectory, "SystemCockpit.csproj")),
                "Published branding must work outside the repository and without an Assets directory");
            var resource = Application.GetResourceStream(new Uri(logoUri, UriKind.Absolute)) ??
                throw new InvalidOperationException("Published absolute pack URI did not resolve the logo");
            using (resource.Stream)
            {
                var decoder = BitmapDecoder.Create(resource.Stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Check(decoder.Frames.Count == 1 && decoder.Frames[0].PixelWidth > 100 && decoder.Frames[0].PixelHeight > 100,
                    "Published embedded logo must decode without a content file");
            }

            var boot = new BootWindow();
            try
            {
                Check(boot.FindName("BootLogo") is Image { Source: BitmapSource { PixelWidth: > 100, PixelHeight: > 100 } },
                    "Published BootWindow must load the embedded logo");
            }
            finally { boot.CloseAfterStartup(); }

            var main = new MainWindow();
            try
            {
                var brand = (SystemCockpit.Controls.AnimatedBrand)main.FindName("DashboardBrand");
                Check(brand.FindName("Logo") is Image { Source: BitmapSource { PixelWidth: > 100, PixelHeight: > 100 } },
                    "Published MainWindow must load dashboard branding from the embedded logo");
            }
            finally { main.Close(); }
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            Directory.Delete(isolatedDirectory);
        }

        return assertions;
    }

    private static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        var pixels = new byte[bitmap.Width * bitmap.Height * 4];
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            var offset = (y * bitmap.Width + x) * 4;
            pixels[offset] = color.B;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R;
            pixels[offset + 3] = color.A;
        }
        return BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32,
            null, pixels, bitmap.Width * 4);
    }

    private static bool MatchesIcon(BitmapSource actual, IEnumerable<BitmapFrame> frames)
    {
        var actualPixels = new FormatConvertedBitmap(actual, PixelFormats.Bgra32, null, 0);
        var actualBytes = new byte[actualPixels.PixelWidth * actualPixels.PixelHeight * 4];
        actualPixels.CopyPixels(actualBytes, actualPixels.PixelWidth * 4, 0);
        foreach (var frame in frames.Where(frame =>
                     frame.PixelWidth == actualPixels.PixelWidth && frame.PixelHeight == actualPixels.PixelHeight))
        {
            var expected = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var expectedBytes = new byte[expected.PixelWidth * expected.PixelHeight * 4];
            expected.CopyPixels(expectedBytes, expected.PixelWidth * 4, 0);
            if (actualBytes.SequenceEqual(expectedBytes)) return true;
        }
        return false;
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
        void VerifyWindowIcon(Window window)
        {
            var stream = Application.GetResourceStream(new Uri("/SystemCockpit;component/Assets/aether.ico", UriKind.Relative)) ??
                throw new InvalidOperationException("Embedded AETHER icon missing for the product window");
            using (stream.Stream)
            {
                var decoder = BitmapDecoder.Create(stream.Stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Check(window.Icon is BitmapSource icon && MatchesIcon(icon, decoder.Frames),
                    $"{window.GetType().Name} must use the exact embedded AETHER window icon");
            }
        }
        var installerPath = RepositoryTestPaths.FindFile("build", "AetherCockpit.iss");
        var repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(installerPath)!, ".."));
        var logoUri = "pack://application:,,,/SystemCockpit;component/Assets/aether-logo.png";
        var bootXaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SystemCockpit", "BootWindow.xaml"));
        var brandXaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SystemCockpit", "Controls", "AnimatedBrand.xaml"));
        var embeddedLogo = Application.GetResourceStream(new Uri(logoUri, UriKind.Absolute)) ??
            throw new InvalidOperationException("Canonical application pack URI did not resolve the embedded AETHER logo");
        using (embeddedLogo.Stream)
            Check(bootXaml.Contains($"Source=\"{logoUri}\"", StringComparison.Ordinal) &&
                brandXaml.Contains($"Source=\"{logoUri}\"", StringComparison.Ordinal) &&
                brandXaml.Contains($"ImageSource=\"{logoUri}\"", StringComparison.Ordinal) &&
                embeddedLogo.Stream.Length > 0,
                "Boot and dashboard branding must share one absolute application pack URI for embedded logo data");
        count += VerifyLogoResourceContract(repositoryRoot);
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
            VerifyWindowIcon(boot);
            var content = (FrameworkElement)boot.Content;
            boot.Content = null;
            CaptureSynthetic(content, boot, null, 980, 780, "-boot",
                _ => VerifyLogo((Image)boot.FindName("BootLogo")));
        }
        finally { boot.CloseAfterStartup(); }
        var main = new MainWindow();
        try
        {
            VerifyWindowIcon(main);
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
            boot.Report("[TEST HOST] Nicht erhöht; Produktstart verlangt weiterhin UAC", 3);
            var initialization = main.Dispatcher.InvokeAsync(() => main.InitializeHardwareAsync(boot.Report, boot.Token)).Task.Unwrap();
            Wait(initialization, TimeSpan.FromSeconds(60));
            var warnings = initialization.GetAwaiter().GetResult();
            var model = (CockpitModel)main.DataContext;
            if (!model.GpuIdentity.Contains("RTX 4080 SUPER") || model.Cards[2].Value == "--" ||
                model.GpuTemperature is "Nicht verfügbar" or "Nicht aktiviert")
                throw new InvalidOperationException("Real RTX readings missing after automatic boot initialization");
            if (((CheckBox)main.FindName("SensorToggle")).IsChecked != true ||
                !boot.Transcript.Contains("[SENSOR / OK] NVIDIA GeForce RTX 4080 SUPER") ||
                !boot.Transcript.Contains("[MODULE / LOADED]") || !boot.Transcript.Contains("[MODULE / ON DEMAND]"))
                throw new InvalidOperationException("Boot console must report actual sensor and module initialization");
            boot.Report("[TEST RESULT] Echte RTX-Sensoren gelesen / keine Systemreparatur ausgeführt", 97);
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
                warningBoot.Report("[WARN] Simulierter nicht verfügbarer Sensor; keine Erfolgsmeldung", 90);
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
