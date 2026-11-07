using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AetherScreensaver;
using SystemCockpit.Services;

internal static class ScreensaverTests
{
    internal static int Run()
    {
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new InvalidOperationException(message); }
        Check(SaverArguments.Parse([]).Mode == SaverMode.Configure, "Invocation without arguments shows configuration notice, not fullscreen");
        Check(SaverArguments.Parse(["/S"]).Mode == SaverMode.Fullscreen, "Standard fullscreen argument is case insensitive");
        Check(SaverArguments.Parse(["/p:123"]).Parent == 123 &&
            SaverArguments.Parse(["/p", "456"]).Mode == SaverMode.Preview, "Both standard preview handle forms parse");
        Check(SaverArguments.Parse(["/c:123"]).Mode == SaverMode.Configure, "Windows configuration owner form parses");
        Check(SaverArguments.Parse(["/c"]).Mode == SaverMode.Configure &&
            SaverArguments.Parse(["-c"]).Mode == SaverMode.Configure,
            "Configuration switches still route to the settings notice");
        Check(AetherScreensaver.Program.ConfigurationMessage.Contains("keine eigenen Einstellungen") &&
            AetherScreensaver.Program.ConfigurationMessage.Contains("Windows-Bildschirmschonereinstellungen"),
            "Configuration explains the absence of custom settings and points to Windows");
        var saverAssembly = typeof(SaverSession).Assembly;
        Check(new[] { "SaverAudio", "SaverAudioSettings", "SaverConfigurationWindow" }.All(name =>
            saverAssembly.GetType("AetherScreensaver." + name) is null),
            "Removed music player, preferences and settings window are absent from the compiled saver");
        Check(typeof(SaverSession).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .All(field => field.FieldType != typeof(System.Windows.Media.MediaPlayer) &&
                field.FieldType != typeof(System.Media.SoundPlayer)),
            "Screensaver session owns no music playback object");
        Check(typeof(SaverWindow).GetMethod("ShowAudioError",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) is null,
            "Screensaver scene no longer exposes a music error display");
        foreach (var args in new[] { new[] { "/p" }, new[] { "/p:0" }, new[] { "/p:-1" }, new[] { "/unknown" } })
        {
            try { SaverArguments.Parse(args); Check(false, "Invalid saver argument accepted"); }
            catch (ArgumentException) { Check(true, "Invalid saver argument rejected"); }
        }
        var now = DateTimeOffset.UtcNow;
        var frame = new BoardFeedFrame(now, HardwareBoardTests.Example, "42", "31", "--", "12", null, "--", true,
            [new("/nvme/0/temperature/0", "SSD", "Composite", "Temperature", 36, "C"),
             new("/nvme/0/temperature/1", "SSD", "Critical Temperature", "Temperature", 109, "C")]);
        Check(BoardLiveFeed.IsFresh(frame, now), "New local feed is live");
        using (var malformed = new MemoryStream())
        {
            BoardLiveFeed.WriteAsync(malformed, frame with { Sensors = [null!] }, CancellationToken.None).GetAwaiter().GetResult();
            malformed.Position = 0;
            try
            {
                BoardLiveFeed.ReadFrameAsync(malformed, CancellationToken.None).GetAwaiter().GetResult();
                Check(false, "Null sensor in board feed was accepted");
            }
            catch (InvalidDataException) { Check(true, "Malformed sensor feed rejected explicitly"); }
        }
        Check(!BoardLiveFeed.IsFresh(frame with { Timestamp = now.AddSeconds(-11) }, now),
            "Stale feed must never be shown as live");
        Check(!BoardLiveFeed.IsFresh(frame with { Timestamp = now.AddSeconds(5) }, now),
            "Unexpected future feed is not live");
        var model = new SystemCockpit.CockpitModel();
        Check(model.LastSampleAt == DateTimeOffset.MinValue, "Uninitialized model cannot advertise live readings");
        var measuredAt = now.AddSeconds(-7);
        model.Apply(new SystemCockpit.LiveSample(measuredAt, 42, 30, 3, 10, null, null, [], []), false);
        Check(model.LastSampleAt == measuredAt, "Feed freshness uses original measurement time, not transmission time");
        model.MeasurementFailed("synthetic sampling failure");
        Check(model.LastSampleAt == DateTimeOffset.MinValue, "Sampling failure invalidates the feed timestamp");
        using var bytes = new MemoryStream();
        BoardLiveFeed.WriteAsync(bytes, frame, CancellationToken.None).GetAwaiter().GetResult();
        bytes.Position = 0;
        var read = BoardLiveFeed.ReadFrameAsync(bytes, CancellationToken.None).GetAwaiter().GetResult();
        Check(read.Gpu == "--" && read.Upload is null && read.Hardware.Devices.SequenceEqual(frame.Hardware.Devices),
            "Local feed preserves real device identity and unavailable readings");
        Check(read.Sensors!.SequenceEqual(frame.Sensors!) && BoardComponentReadings.Describe("storage", read).Contains("36 C") &&
            !BoardComponentReadings.Describe("storage", read).Contains("109"),
            "Component feed preserves sensor readings but excludes temperature limits from live component values");
        Check(BoardComponentReadings.Describe("cpu", frame).Contains("42 %") &&
            BoardComponentReadings.Describe("ram", frame).Contains("31 %") &&
            BoardComponentReadings.Describe("gpu", frame).Contains("--") &&
            BoardComponentReadings.Describe("cpu", null).Contains("LAST --"),
            "Component labels use real CPU/RAM load, preserve unavailable GPU and clear on loss of feed");
        var metricsFrame = frame with { Sensors = [
            new("/intelcpu/0/clock/0", "CPU", "Core 1", "Clock", 4321, "MHz"),
            new("/lpc/nct/fan/0", "Board", "Fan 1", "Fan", 1234, "RPM"),
            new("/gpu-nvidia/0/clock/0", "GPU A", "Core", "Clock", 2100, "MHz"),
            new("/gpu-intel/0/clock/0", "GPU B", "Core", "Clock", 900, "MHz"),
            new("/nvme/0/temperature/1", "SSD", "Critical Temperature", "Temperature", 109, "C"),
            new("/nvme/0/temperature/0", "SSD", "Composite", "Temperature", 36, "C")] };
        Check(BoardComponentReadings.Metrics("cpu", metricsFrame).Any(m => m.Display == "4321 MHz") &&
            BoardComponentReadings.Metrics("controller", metricsFrame).Any(m => m.Display == "1234 RPM"),
            "Clock and actual SuperIO fan readings are assigned to CPU/mainboard instruments");
        Check(BoardComponentReadings.Metrics("gpu", metricsFrame).Count(m => m.Unit == "MHz") == 2,
            "Rotating GPU instruments include all reported GPUs, not only the selected primary one");
        Check(BoardComponentReadings.Metrics("storage", metricsFrame).Count == 1 &&
            BoardComponentReadings.Metrics("storage", metricsFrame)[0].Display == "36 C" &&
            BoardComponentReadings.Metrics("cpu", null).Count == 0,
            "Dial channels exclude thermal limits and never invent measurements when disconnected");
        var conflicts = metricsFrame with { Sensors = [
            new("/lpc/nct/fan/0", "Board", "Fan 1", "Fan", 1234, "RPM"),
            new("/lpc/nct/fan/0", "Board", "Fan 1", "Fan", 2000, "RPM")] };
        Check(BoardComponentReadings.Metrics("controller", conflicts).Single() is { Value: null, Display: "--" } &&
            double.IsNaN(BoardComponentReadings.Metrics("controller", conflicts).Single().Level),
            "Conflicting identical sensor channels leave the dial unavailable, never zero or an arbitrary value");
        Check(SaverSession.SameHardware(read.Hardware, frame.Hardware),
            "Repeated identical snapshots do not restart visual animations");
        foreach (var length in new[] { -1, 0, 2_000_001 })
        {
            using var invalid = new MemoryStream(BitConverter.GetBytes(length));
            try { BoardLiveFeed.ReadFrameAsync(invalid, CancellationToken.None).GetAwaiter().GetResult(); Check(false, "Bad frame accepted"); }
            catch (InvalidDataException) { Check(true, "Oversized/invalid frame rejected before allocating"); }
        }
        using (var truncated = new MemoryStream(BitConverter.GetBytes(32)))
        {
            try { BoardLiveFeed.ReadFrameAsync(truncated, CancellationToken.None).GetAwaiter().GetResult(); Check(false, "Truncated frame accepted"); }
            catch (EndOfStreamException) { Check(true, "Truncated feed is explicit failure"); }
        }
        var pipeName = "AetherBoardTest-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var server = BoardLiveFeed.CreateServer(pipeName);
        var send = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(deadline.Token);
            await BoardLiveFeed.WriteAsync(server, frame, deadline.Token);
        }, deadline.Token);
        var received = BoardLiveFeed.ReadFromPipeAsync(pipeName, deadline.Token).GetAwaiter().GetResult();
        send.GetAwaiter().GetResult();
        Check(received.Cpu == "42" && received.Hardware.Devices.Length == frame.Hardware.Devices.Length,
            "Same-user local named pipe transports board and readings without starting sensors");
        return count;
    }

    internal static int Ui(string? screenshot)
    {
        var host = new Window { Width = 1200, Height = 1080, ShowInTaskbar = false };
        SaverWindow? saver = null;
        try
        {
            host.Show();
            saver = new SaverWindow(new System.Drawing.Rectangle(0, 0, 1200, 1080), new WindowInteropHelper(host).Handle);
            var hardware = HardwareBoardTests.IllustrationExample with {
                Devices = HardwareBoardTests.IllustrationExample.Devices.Concat(HardwareBoardTests.ExternalDrives).ToArray() };
            var frame = new BoardFeedFrame(DateTimeOffset.UtcNow, hardware, "42", "31", "56", "12", null, "51 C", true,
                [new("/gpu-nvidia/0/temperature/0", "GPU", "GPU Core", "Temperature", 62, "C"),
                 new("/nvme/0/temperature/0", "NVMe SSD / 2 TB", "Composite", "Temperature", 35, "C"),
                 new("/hdd/4/temperature/0", "External SSD", "Temperature", "Temperature", 90, "C")]);
            saver.Present(frame.Hardware, frame, "SYNTHETISCHER VORSCHAUTEST / KEINE ECHTEN LIVE-MESSWERTE");
            saver.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (!saver.ResizePreview()) throw new InvalidOperationException("SCR preview failed to attach to parent");
            var board = FindVisual<SystemCockpit.Controls.HardwareBoardView>(saver)
                ?? throw new InvalidOperationException("Saver scene missing.");
            var canvas = (System.Windows.Controls.Canvas)board.FindName("BoardCanvas");
            if (canvas.Children.OfType<System.Windows.Controls.Button>().Any(b => b.Tag is string key && key.StartsWith("device:")))
                throw new InvalidOperationException("Saver must not render lower device inventory.");
            if (board.DriveCount != 4 || canvas.Children.OfType<System.Windows.Controls.Button>().Any(b =>
                b.Tag is string key && HardwareBoardTests.ExternalDrives.Any(d => key == "drive:" + d.Id)))
                throw new InvalidOperationException("Saver rendered USB or other non-internal drives.");
            var layoutAssertions = HardwareBoardTests.VerifyLabels(board) + HardwareBoardTests.VerifyLandscape(board);
            var labels = Descendants(canvas).OfType<System.Windows.Controls.TextBlock>()
                .Where(t => t.Tag is string key && key.StartsWith("component-live:")).ToArray();
            if (!labels.Any(t => Equals(t.Tag, "component-live:cpu") && t.Text.Contains("42 %") && t.Text.Contains("51 C")) ||
                !labels.Any(t => Equals(t.Tag, "component-live:gpu") && t.Text.Contains("56 %") && t.Text.Contains("62 C")))
                throw new InvalidOperationException("Live component labels did not update in actual saver.");
            if (!labels.Any(t => Equals(t.Tag, "component-live:storage") && t.Text.Contains("35 C") && !t.Text.Contains("90 C")))
                throw new InvalidOperationException("Saver storage values included an excluded external drive.");
            if (screenshot is not null) ModuleTests.SaveScreenshot(saver, screenshot);
            foreach (var size in new[] { (Width: 1920, Height: 1080), (Width: 800, Height: 600), (Width: 600, Height: 1000) })
            {
                host.Width = size.Width;
                host.Height = size.Height;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                if (!saver.ResizePreview()) throw new InvalidOperationException("Saver lost its preview parent during resize.");
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                layoutAssertions += HardwareBoardTests.VerifyLabels(board);
            }
            // A detached WPF layout exercises real 4K geometry without opening a fullscreen saver.
            var scene4K = new SaverWindow(new System.Drawing.Rectangle(0, 0, 3840, 2160));
            try
            {
                scene4K.Present(frame.Hardware, frame, "SYNTHETISCHER 4K-LAYOUTTEST");
                var root = (System.Windows.FrameworkElement)scene4K.Content;
                root.Measure(new Size(3840, 2160));
                root.Arrange(new Rect(0, 0, 3840, 2160));
                root.UpdateLayout();
                var board4K = FindVisual<SystemCockpit.Controls.HardwareBoardView>(root)!;
                board4K.ConfigureViewport(new Size(3816, 2136));
                root.Measure(new Size(3840, 2160)); root.Arrange(new Rect(0, 0, 3840, 2160)); root.UpdateLayout();
                var source = (System.Windows.Controls.Canvas)board4K.FindName("BoardCanvas");
                var scale = (System.Windows.Controls.Viewbox)board4K.FindName("BoardScale");
                var projected = source.TransformToAncestor(root).TransformBounds(new Rect(0, 0, source.Width, source.Height));
                if (Math.Abs(root.ActualWidth - 3840) > 1 || projected.Width < 3750 || projected.Height < 2090 ||
                    projected.Left < 0 || projected.Top < 0 || projected.Right > 3840 || projected.Bottom > 2160 ||
                    scale.StretchDirection != System.Windows.Controls.StretchDirection.Both)
                    throw new InvalidOperationException("4K scene must fill the viewport with vector upscaling and no clipping/scrolling.");
                layoutAssertions += HardwareBoardTests.VerifyLabels(board4K);
                var metrics = BoardComponentReadings.Metrics("gpu", frame);
                for (var index = 0; index < metrics.Count; index++)
                {
                    board4K.UpdateDials(TimeSpan.FromSeconds(index * 12 + 6));
                    root.UpdateLayout();
                    var dial = FindTagged(root, "component-dial:gpu") as System.Windows.Controls.Grid;
                    if (dial is null || dial.Visibility != Visibility.Visible ||
                        dial.ActualHeight < 87 || dial.Children.OfType<SystemCockpit.Controls.NeonGauge>().Single().ActualWidth < 87 ||
                        dial.Children.OfType<SystemCockpit.Controls.NeonGauge>().Single().Level != metrics[index].Level)
                        throw new InvalidOperationException("Every actual GPU metric must rotate into a visible instrument.");
                    layoutAssertions++;
                }
                board4K.UpdateDials(TimeSpan.FromSeconds(6));
                root.UpdateLayout();
                var storageDial = FindTagged(root, "component-dial:storage")!;
                if (!Descendants(storageDial).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == "35 C") ||
                    Descendants(storageDial).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text.Contains("90 C")))
                    throw new InvalidOperationException("Rotating saver instruments must use the same internal-drive filtering as text summaries.");
                layoutAssertions++;
                var rendered = new System.Windows.Media.Imaging.RenderTargetBitmap(3840, 2160, 96, 96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                rendered.Render(root);
                if (rendered.PixelWidth != 3840 || rendered.PixelHeight != 2160)
                    throw new InvalidOperationException("The vector scene must render to an actual 3840x2160 target.");
                layoutAssertions++;
                if (screenshot is not null)
                {
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rendered));
                    using var file = File.Create(System.IO.Path.ChangeExtension(screenshot, ".4k.png"));
                    encoder.Save(file);
                }
                foreach (var size in new[] { new Size(2160, 3840), new Size(5120, 1440), new Size(7680, 4320) })
                {
                    root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
                    board4K.ConfigureViewport(new Size(size.Width - 24, size.Height - 24));
                    root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
                    source = (System.Windows.Controls.Canvas)board4K.FindName("BoardCanvas");
                    projected = source.TransformToAncestor(root).TransformBounds(new Rect(0, 0, source.Width, source.Height));
                    if (Math.Abs(projected.Width - (size.Width - 24)) > 2 || Math.Abs(projected.Height - (size.Height - 24)) > 2 ||
                        projected.Left < 0 || projected.Top < 0 || projected.Right > size.Width || projected.Bottom > size.Height)
                        throw new InvalidOperationException("Portrait, ultrawide and 8K layouts must fill their full viewport without distorting/cropping the board.");
                    layoutAssertions++;
                }
                board4K.UpdateComponentReadings(null);
                board4K.UpdateDials(TimeSpan.FromSeconds(6));
                if (FindTagged(root, "component-dial:gpu") is not System.Windows.Controls.Grid unavailable ||
                    !double.IsNaN(unavailable.Children.OfType<SystemCockpit.Controls.NeonGauge>().Single().Level))
                    throw new InvalidOperationException("Loss of feed must clear the instrument rather than displaying zero.");
                layoutAssertions += 2;
                board4K.Shutdown();
            }
            finally { scene4K.Close(); }
            // Resizing intentionally rebuilds responsive scenery; reacquire the current labels.
            labels = Descendants(canvas).OfType<System.Windows.Controls.TextBlock>()
                .Where(t => t.Tag is string key && key.StartsWith("component-live:")).ToArray();
            var firstElement = canvas.Children[0];
            saver.Present(frame.Hardware, frame with { Cpu = "73" }, "TEST / AKTUALISIERUNG");
            if (!ReferenceEquals(firstElement, canvas.Children[0]) ||
                !labels.Any(t => Equals(t.Tag, "component-live:cpu") && t.Text.Contains("73 %")))
                throw new InvalidOperationException("Updating component readings rebuilt the scene or missed the new load.");
            saver.Present(frame.Hardware, frame with { Timestamp = DateTimeOffset.UtcNow.AddSeconds(-11) }, "TEST / VERALTET");
            if (labels.Any(t => t.Tag is string key && key.StartsWith("component-live:") &&
                (t.Text.Contains("42 %") || t.Text.Contains("62 C"))))
                throw new InvalidOperationException("Stale feed retained component measurements.");
            saver.Present(frame.Hardware, null, "AETHER NICHT VERBUNDEN / KEINE LIVE-MESSWERTE");
            if (labels.Any(t => t.Tag is string key && key.StartsWith("component-live:") &&
                (t.Text.Contains("42 %") || t.Text.Contains("62 C"))))
                throw new InvalidOperationException("Disconnected component retained stale measurements.");
            var previewSource = HwndSource.FromHwnd(new WindowInteropHelper(saver).Handle)
                ?? throw new InvalidOperationException("Preview source unavailable before teardown test.");
            previewSource.CompositionTarget.Dispose();
            saver.InvalidateMeasure();
            saver.Measure(new Size(1200, 1080));
            saver.Arrange(new Rect(0, 0, 1200, 1080));
            host.Close();
            saver.InvalidateMeasure();
            saver.Measure(new Size(1200, 1080));
            saver.UpdateLayout();
            if (saver.ResizePreview())
                throw new InvalidOperationException("Destroyed preview parent must terminate preview resizing.");
            return 11 + layoutAssertions;
        }

        finally { saver?.Close(); host.Close(); }
    }

    private static T? FindVisual<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        if (root is T match) return match;
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindVisual<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, index)) is { } child) return child;
        return null;
    }

    private static System.Windows.FrameworkElement? FindTagged(System.Windows.DependencyObject root, string tag)
    {
        if (root is System.Windows.FrameworkElement element && Equals(element.Tag, tag)) return element;
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindTagged(System.Windows.Media.VisualTreeHelper.GetChild(root, index), tag) is { } child) return child;
        return null;
    }

    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    internal static int ProcessPreview(string path)
    {
        var metadataAssertions = 0;
        if (System.IO.Path.GetFileName(path).Equals("Aether.scr", StringComparison.OrdinalIgnoreCase))
        {
            var module = LoadLibraryEx(System.IO.Path.GetFullPath(path), 0, 0x22);
            if (module == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var name = new System.Text.StringBuilder(128);
                if (LoadString(module, 1, name, name.Capacity) == 0 || name.ToString() != "AETHER Mainframe 1986")
                    throw new InvalidOperationException("Windows saver enumeration requires resource string 1 with the display name.");
                metadataAssertions++;
            }
            finally { FreeLibrary(module); }
        }
        var host = new Window { Width = 700, Height = 520, ShowInTaskbar = false };
        Process? process = null;
        try
        {
            host.Show();
            var handle = new WindowInteropHelper(host).Handle;
            var start = new ProcessStartInfo(path) { UseShellExecute = false, RedirectStandardError = true };
            start.ArgumentList.Add("/p"); start.ArgumentList.Add(handle.ToInt64().ToString());
            process = Process.Start(start) ?? throw new InvalidOperationException("SCR did not start.");
            var errors = process.StandardError.ReadToEndAsync();
            var frame = new DispatcherFrame();
            var childFound = false;
            var observed = new List<string>();
            var readyDeadline = Environment.TickCount64 + 15000;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) =>
            {
                EnumChildWindows(handle, (child, _) =>
                {
                    GetWindowThreadProcessId(child, out var owner);
                    if (owner == process.Id) childFound = true;
                    else if (System.IO.Path.GetFileName(path).Equals("Aether.scr", StringComparison.OrdinalIgnoreCase))
                    {
                        using var forwarded = Process.GetProcessById(checked((int)owner));
                        var module = forwarded.MainModule?.FileName;
                        if (!observed.Contains(module ?? "(no module)")) observed.Add(module ?? "(no module)");
                        if (string.Equals(module,
                            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!, "AetherMainboard.scr"),
                            StringComparison.OrdinalIgnoreCase)) childFound = true;
                    }
                    return true;
                }, 0);
                if (childFound || process.HasExited || Environment.TickCount64 >= readyDeadline)
                {
                    timer.Stop(); frame.Continue = false;
                }
            };
            timer.Start(); Dispatcher.PushFrame(frame);
            if (process.HasExited) throw new InvalidOperationException("Packaged SCR exited early: " + process.ExitCode + "\n" +
                errors.GetAwaiter().GetResult());
            if (!childFound) throw new InvalidOperationException("Packaged SCR did not attach its preview to the supplied parent. Observed: " +
                string.Join("; ", observed) + " Launcher: " + process.MainWindowTitle);
            host.Close();
            var exitFrame = new DispatcherFrame();
            var exitDeadline = Environment.TickCount64 + 15000;
            var exitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            exitTimer.Tick += (_, _) =>
            {
                if (process.HasExited || Environment.TickCount64 >= exitDeadline)
                {
                    exitTimer.Stop(); exitFrame.Continue = false;
                }
            };
            exitTimer.Start(); Dispatcher.PushFrame(exitFrame);
            if (!process.HasExited)
            {
                var title = process.MainWindowTitle;
                process.Kill(true); process.WaitForExit();
                throw new InvalidOperationException("SCR did not exit after preview parent closed: " + title + "\n" +
                    errors.GetAwaiter().GetResult());
            }
            if (process.ExitCode != 0) throw new InvalidOperationException("SCR preview failed: " + process.ExitCode + "\n" +
                errors.GetAwaiter().GetResult());
            return 3 + metadataAssertions;
        }

        finally
        {
            host.Close();
            if (process is not null)
            {
                if (!process.HasExited) process.Kill(true);
                process.Dispose();
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryEx(string path, nint file, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int LoadString(nint module, uint id, System.Text.StringBuilder text, int length);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint module);

    internal static int PipeServer(string name)
    {
        using var server = BoardLiveFeed.CreateServer(name);
        using var token = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        server.WaitForConnectionAsync(token.Token).GetAwaiter().GetResult();
        BoardLiveFeed.WriteAsync(server, new BoardFeedFrame(DateTimeOffset.UtcNow, HardwareBoardTests.Example,
            "42", "--", "--", "--", null, "--", true), token.Token).GetAwaiter().GetResult();
        return 0;
    }

    internal static int ProcessFullscreen(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = false, ArgumentList = { "/s" }
        }) ?? throw new InvalidOperationException("SCR did not start.");
        var windows = new List<nint>();
        try
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var wait = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                timer.Tick += (_, _) => { timer.Stop(); wait.Continue = false; };
                timer.Start(); Dispatcher.PushFrame(wait);
                if (process.HasExited) throw new InvalidOperationException("Fullscreen SCR exited before monitor verification.");
                windows.Clear();
                EnumWindows((window, _) =>
                {
                    GetWindowThreadProcessId(window, out var owner);
                    if (owner == process.Id && IsWindowVisible(window)) windows.Add(window);
                    return true;
                }, 0);
                if (windows.Count == System.Windows.Forms.Screen.AllScreens.Length) break;
            }
            var oldDpi = SetThreadDpiAwarenessContext(new nint(-4));
            try
            {
                var monitors = new List<NativeRect>();
                EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref NativeRect rect, nint data) =>
                {
                    monitors.Add(rect); return true;
                }, 0);
                var rects = windows.Select(window =>
                {
                    var result = DwmGetWindowAttribute(window, 9, out var rect, Marshal.SizeOf<NativeRect>());
                    if (result != 0) throw new InvalidOperationException("Cannot inspect fullscreen physical window bounds.");
                    return rect;
                }).ToArray();
                if (rects.Length != monitors.Count || monitors.Any(monitor => !rects.Contains(monitor)))
                    throw new InvalidOperationException("SCR does not cover every physical monitor with one correctly placed window.");
                Console.WriteLine($"SCR: {rects.Length} monitors; physical bounds match exactly.");
            }
            finally { if (oldDpi != 0) SetThreadDpiAwarenessContext(oldDpi); }
            PostMessage(windows[0], 0x10, 0, 0);
            if (!process.WaitForExit(15000) || process.ExitCode != 0)
                throw new InvalidOperationException("Fullscreen SCR did not exit cleanly after closing one monitor window.");
            return 2;
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }

    internal static int PipeClient(string name)
    {
        using var token = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var frame = BoardLiveFeed.ReadFromPipeAsync(name, token.Token).GetAwaiter().GetResult();
        if (frame.Cpu != "42") throw new InvalidOperationException("Cross-elevation synthetic feed did not match.");
        Console.WriteLine("PASS: elevated publisher to unprivileged current-user client; no sensors initialized.");
        return 0;
    }

    private delegate bool EnumWindow(nint window, nint parameter);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect : IEquatable<NativeRect>
    {
        public int Left, Top, Right, Bottom;
        public bool Equals(NativeRect other) => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
        public override bool Equals(object? other) => other is NativeRect rect && Equals(rect);
        public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
    }
    private delegate bool MonitorCallback(nint monitor, nint dc, ref NativeRect rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint w, nint l);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out NativeRect rect, int size);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int process);
}
