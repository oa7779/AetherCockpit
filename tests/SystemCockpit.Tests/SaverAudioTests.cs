using System.IO;
using System.Windows.Threading;
using AetherScreensaver;

internal static class SaverAudioTests
{
    internal static int Run(bool playback)
    {
        var assertions = 0;
        void Check(bool value, string message)
        {
            assertions++;
            if (!value) throw new InvalidOperationException(message);
        }
        void Reject(SaverAudioSettings settings)
        {
            try { settings.Validate(); }
            catch (ArgumentException) { assertions++; return; }
            throw new InvalidOperationException("Invalid audio settings accepted.");
        }
        var directory = Path.Combine(Path.GetTempPath(), "AetherAudioTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = Path.Combine(directory, "audio.json");
            var wave = Path.Combine(directory, "silent.wav");
            Check(!SaverAudioSettings.Load(config).Enabled, "Screensaver audio is opt-in for other users");
            Reject(new(true, "relative.wav"));
            Reject(new(true, ""));
            Reject(new(true, @"\\server\share\music.wav"));
            Reject(new(true, Path.Combine(directory, "track.exe")));
            Reject(new(false, "", double.NaN));
            Reject(new(false, "", 1.01));
            Reject(new(false, null!));
            Reject(new(true, Path.Combine(directory, "bad\nname.wav")));
            var settings = new SaverAudioSettings(true, wave, .27);
            settings.Save(config);
            Check(SaverAudioSettings.Load(config) == settings, "Music path, opt-in and volume roundtrip independently of cockpit settings");
            Check(File.ReadAllText(config).Contains("silent.wav") && !File.Exists(wave),
                "Missing local music remains a saved preference, not fabricated audio");
            var errors = new List<string>();
            using (var audio = new SaverAudio(errors.Add))
            {
                audio.Start(SaverMode.Preview, () => throw new InvalidOperationException("Preview must not read audio configuration"));
                audio.Start(SaverMode.Configure, () => throw new InvalidOperationException("Configure must not start audio"));
                Check(errors.Count == 0 && !audio.IsPlaying, "Preview/config modes never load settings or start audio");
                audio.Start(SaverMode.Fullscreen, () => settings);
                Check(errors.Count == 1 && errors[0].Contains("Musikdatei fehlt") && !audio.IsPlaying,
                    "Missing local WAV explicitly reports failure without stopping the scene");
            }
            errors.Clear();
            File.WriteAllText(config, "null");
            using (var audio = new SaverAudio(errors.Add))
            {
                audio.Start(SaverMode.Fullscreen, () => SaverAudioSettings.Load(config));
                Check(errors.Count == 1 && errors[0].StartsWith("MUSIK NICHT VERFUEGBAR"),
                    "Empty JSON audio configuration is explicitly reported instead of crashing the saver");
            }
            errors.Clear();
            using (var audio = new SaverAudio(errors.Add))
            {
                audio.Start(SaverMode.Fullscreen, () => new(false));
                Check(errors.Count == 0 && !audio.IsPlaying, "Disabled soundtrack never opens an audio file");
            }
            File.WriteAllText(config, "{broken");
            using (var audio = new SaverAudio(errors.Add))
            {
                audio.Start(SaverMode.Fullscreen, () => SaverAudioSettings.Load(config));
                Check(errors.Count == 1 && errors[0].StartsWith("MUSIK NICHT VERFUEGBAR"),
                    "Malformed preferences are reported explicitly without starting sound");
            }
            errors.Clear();
            if (!playback) return assertions;
            var configuration = new SaverConfigurationWindow { ShowInTaskbar = false };
            try
            {
                configuration.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var panel = (System.Windows.Controls.StackPanel)configuration.Content;
                var save = panel.Children.OfType<System.Windows.Controls.Button>().Last();
                Check(save.TranslatePoint(new System.Windows.Point(0, save.ActualHeight), configuration).Y <= configuration.ActualHeight - 8,
                    "Music settings and save button fit within the configuration window");
                Check(panel.Children.OfType<System.Windows.Controls.CheckBox>().Count() == 1 &&
                    !panel.Children.OfType<System.Windows.Controls.Slider>().Any() &&
                    !panel.Children.OfType<System.Windows.Controls.TextBox>().Any() &&
                    panel.Children.OfType<System.Windows.Controls.Button>().Count() == 1,
                    "Windows SCR configuration exposes only music on/off, no volume/path/other settings");
            }
            finally { configuration.Close(); }
            using (var writer = new BinaryWriter(File.Create(wave)))
            {
                const int samples = 2400;
                writer.Write("RIFF"u8); writer.Write(36 + samples); writer.Write("WAVEfmt "u8);
                writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(8000); writer.Write(8000); writer.Write((short)1); writer.Write((short)8);
                writer.Write("data"u8); writer.Write(samples);
                writer.Write(Enumerable.Repeat((byte)128, samples).ToArray());
            }
            using (var audio = new SaverAudio(errors.Add))
            {
                audio.Start(SaverMode.Fullscreen, () => settings with { Volume = 0 });
                WaitUntil(() => audio.LoopCount >= 2 || errors.Count > 0, TimeSpan.FromSeconds(10));
                Check(errors.Count == 0 && audio.IsPlaying && audio.LoopCount >= 2 && audio.Volume == 0,
                    "Real muted MediaPlayer opens synthetic silent WAV and loops at end without restarting per monitor: " + string.Join("; ", errors));
                audio.Start(SaverMode.Fullscreen, () => throw new InvalidOperationException("One music player per session"));
                Check(errors.Count == 0, "Repeated start cannot load a second music source");
                audio.Dispose();
                var loops = audio.LoopCount;
                WaitUntil(() => false, TimeSpan.FromMilliseconds(450));
                Check(!audio.IsPlaying && audio.LoopCount == loops, "Dispose immediately stops playback and prevents loop callbacks");
                using var exclusive = File.Open(wave, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Check(exclusive.Length > 0, "Closing player releases the external WAV file");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
        return assertions;
    }

    private static void WaitUntil(Func<bool> done, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
        {
            if (done() || Environment.TickCount64 >= deadline) { timer.Stop(); frame.Continue = false; }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
