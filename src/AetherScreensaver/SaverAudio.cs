using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace AetherScreensaver;

internal sealed record SaverAudioSettings(bool Enabled = false, string Path = "", double Volume = .35)
{
    internal static string FileName => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherCockpit", "screensaver-audio.json");

    internal void Validate()
    {
        if (!double.IsFinite(Volume) || Volume is < 0 or > 1)
            throw new ArgumentException("Musiklautstaerke muss zwischen 0 und 100 Prozent liegen.");
        if (Path is null || (Path.Length > 0 && (!System.IO.Path.IsPathFullyQualified(Path) ||
            Path.Any(char.IsControl) || Path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0 ||
            !System.IO.Path.GetExtension(Path).Equals(".wav", StringComparison.OrdinalIgnoreCase))) ||
            Enabled && string.IsNullOrWhiteSpace(Path))
            throw new ArgumentException("Fuer die Musik eine absolute lokale WAV-Datei auswaehlen.");
        if (Path.StartsWith(@"\\", StringComparison.Ordinal) ||
            Path.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("Musik muss lokal liegen; Netzwerkpfade werden nicht geoeffnet.");
    }

    internal static SaverAudioSettings Load(string? file = null)
    {
        file ??= FileName;
        if (!File.Exists(file)) return new();
        var settings = JsonSerializer.Deserialize<SaverAudioSettings>(File.ReadAllText(file))
            ?? throw new InvalidDataException("Leere Bildschirmschoner-Musikeinstellungen.");
        settings.Validate();
        return settings;
    }

    internal void Save(string? file = null)
    {
        Validate();
        file ??= FileName;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, file, overwrite: true);
    }
}

internal sealed class SaverAudio : IDisposable
{
    private readonly MediaPlayer _player = new();
    private readonly Action<string> _report;
    private bool _disposed, _started;
    internal bool IsPlaying { get; private set; }
    internal int LoopCount { get; private set; }
    internal double Volume => _player.Volume;

    internal SaverAudio(Action<string> report)
    {
        _report = report;
        _player.MediaOpened += (_, _) => { if (!_disposed) IsPlaying = true; };
        _player.MediaEnded += (_, _) =>
        {
            if (_disposed) return;
            LoopCount++;
            _player.Position = TimeSpan.Zero;
            _player.Play();
        };
        _player.MediaFailed += (_, e) => Fail(e.ErrorException.Message);
    }

    internal void Start(SaverMode mode, Func<SaverAudioSettings>? load = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SaverAudio));
        if (_started || mode != SaverMode.Fullscreen) return;
        _started = true;
        try
        {
            var settings = (load ?? (() => SaverAudioSettings.Load()))();
            settings.Validate();
            if (!settings.Enabled) return;
            if (!File.Exists(settings.Path))
                throw new FileNotFoundException("Musikdatei fehlt oder ist nicht lokal verfuegbar. WAV unter Bildschirmschoner -> Einstellungen neu auswaehlen.", settings.Path);
            _player.Volume = settings.Volume;
            _player.Open(new Uri(settings.Path, UriKind.Absolute));
            _player.Play();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or JsonException or NotSupportedException)
        {
            Fail(ex.Message);
        }
    }

    private void Fail(string message)
    {
        if (_disposed) return;
        _player.Close();
        IsPlaying = false;
        Trace.TraceError("AETHER Bildschirmschoner-Musik: {0}", message);
        _report("MUSIK NICHT VERFUEGBAR / " + message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsPlaying = false;
        _player.Close();
    }
}
