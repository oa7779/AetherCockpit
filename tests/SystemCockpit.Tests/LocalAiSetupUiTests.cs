using System.Collections;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class LocalAiSetupUiTests
{
    public static int Run()
    {
        var assertions = 0;
        void Check(bool valid, string message)
        {
            assertions++;
            if (!valid) throw new InvalidOperationException(message);
        }

        var projectPath = FindRepositoryFile("src", "AetherLocalAiSetup", "AetherLocalAiSetup.csproj");
        var repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, "..", ".."));
        var project = File.ReadAllText(projectPath);
        Check(project.Split("<Resource").Length - 1 == 2,
            "Local AI setup must register exactly the logo and icon as WPF resources");
        Check(project.Contains("LogicalName=\"Assets\\aether-logo.png\"", StringComparison.Ordinal) &&
            !project.Contains("<Content Include=\"..\\SystemCockpit\\Assets\\aether-logo.png\"", StringComparison.Ordinal),
            "Local AI setup logo must have one canonical resource contract and no content-file contract");

        var assembly = typeof(AetherLocalAiSetup.MainWindow).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".g.resources", StringComparison.Ordinal));
        using (var stream = assembly.GetManifestResourceStream(resources)!)
        using (var reader = new ResourceReader(stream))
        {
            var logoKeys = reader.Cast<DictionaryEntry>()
                .Count(entry => entry.Key is string key &&
                    key.Replace('\\', '/').Equals("Assets/aether-logo.png", StringComparison.OrdinalIgnoreCase));
            Check(logoKeys == 1, "Local AI setup assembly must contain exactly one embedded logo");
        }

        var xamlPath = Path.Combine(repositoryRoot, "src", "AetherLocalAiSetup", "MainWindow.xaml");
        var codePath = Path.Combine(repositoryRoot, "src", "AetherLocalAiSetup", "MainWindow.xaml.cs");
        var xaml = File.ReadAllText(xamlPath);
        var code = File.ReadAllText(codePath);
        Check(xaml.Contains("MouseLeftButtonDown=\"WindowHeader_OnMouseLeftButtonDown\"", StringComparison.Ordinal) &&
            code.Contains("DragMove();", StringComparison.Ordinal),
            "Local AI setup must expose a dedicated draggable header and use Window.DragMove");
        Check(xaml.Contains("MinimizeButton_OnClick", StringComparison.Ordinal) &&
            xaml.Contains("CloseButton_OnClick", StringComparison.Ordinal),
            "Header controls must remain separately interactive");
        Check(code.Contains("SystemParameters.ClientAreaAnimation", StringComparison.Ordinal) &&
            code.Contains("BeginAnimation(OpacityProperty", StringComparison.Ordinal),
            "Local AI branding animation must remain restrained and honor Windows animation permission");

        var originalDirectory = Environment.CurrentDirectory;
        var isolated = Path.Combine(Path.GetTempPath(), "AetherLocalAiSetupUi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolated);
        try
        {
            Environment.CurrentDirectory = isolated;
            var logo = Application.GetResourceStream(new Uri(
                "pack://application:,,,/AetherLocalAiSetup;component/Assets/aether-logo.png", UriKind.Absolute))
                ?? throw new InvalidOperationException("Local AI setup logo resource was not found");
            using (logo.Stream)
            {
                var decoder = BitmapDecoder.Create(logo.Stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Check(decoder.Frames.SingleOrDefault() is { PixelWidth: > 100, PixelHeight: > 100 },
                    "Local AI setup must decode the canonical embedded logo outside the repository");
            }

            var window = new AetherLocalAiSetup.MainWindow();
            try
            {
                Check(window.WindowStyle == WindowStyle.None,
                    "Local AI setup must use its AETHER header instead of the generic title bar");
                var image = (Image)window.FindName("BrandLogo");
                var wordmark = (TextBlock)window.FindName("Wordmark");
                var setupButton = (Button)window.FindName("SetupButton");
                Check(image.Source is BitmapSource { PixelWidth: > 100, PixelHeight: > 100 },
                    "Local AI setup must display the embedded AETHER logo");
                Check(wordmark.Text.Replace(" ", "", StringComparison.Ordinal).Equals("AETHER", StringComparison.Ordinal) &&
                    wordmark.FontStyle == FontStyles.Italic,
                    "Local AI setup must display the AETHER retro-neon wordmark");
                Check(setupButton is { Width: 190, Height: 40 },
                    "Primary setup action must retain a usable button size");
                var content = (FrameworkElement)window.Content;

                foreach (var scale in new[] { 1d, 1.25d, 1.5d })
                {
                    var available = new Size(Math.Ceiling(window.MinWidth * scale), Math.Ceiling(window.MinHeight * scale));
                    content.Measure(available);
                    content.Arrange(new Rect(0, 0, available.Width, available.Height));
                    Check(content.DesiredSize.Width <= available.Width + .5 &&
                        content.DesiredSize.Height <= available.Height + .5,
                        $"Local AI setup must fit the {scale:P0} layout probe");
                    Check(((FrameworkElement)window.FindName("ResultPanel")).HorizontalAlignment == HorizontalAlignment.Stretch &&
                        window.MinWidth >= 760,
                        $"Status content must remain stretchable and readable at {scale:P0}");
                }
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(isolated)) Directory.Delete(isolated, true);
        }

        return assertions;
    }

    private static string FindRepositoryFile(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. path]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Repository file not found: " + string.Join(Path.DirectorySeparatorChar, path));
    }
}
