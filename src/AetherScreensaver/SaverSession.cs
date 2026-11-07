using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SystemCockpit.Services;
using Screen = System.Windows.Forms.Screen;

namespace AetherScreensaver;

internal sealed class SaverSession(Application app, SaverArguments arguments) : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<SaverWindow> _windows = [];
    private readonly DispatcherTimer _input = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private Task _poll = Task.CompletedTask;
    private uint? _lastInput;
    private readonly long _started = Environment.TickCount64;
    private bool _stopping;

    internal void Start()
    {
        var screens = arguments.Mode == SaverMode.Preview
            ? new[] { Screen.PrimaryScreen ?? throw new InvalidOperationException("Kein Bildschirm vorhanden.") }
            : Screen.AllScreens;
        foreach (var screen in screens)
        {
            var window = new SaverWindow(screen.Bounds, arguments.Parent);
            _windows.Add(window);
            window.Closed += (_, _) => Stop();
            window.Present(Empty, null, "HARDWARE WIRD LOKAL GELESEN / KEINE LIVE-MESSWERTE");
            window.Show();
        }
        if (arguments.Mode != SaverMode.Preview)
        {
            Mouse.OverrideCursor = Cursors.None;
            SystemEvents.DisplaySettingsChanged += ScreensChanged;
        }
        _input.Tick += CheckInput;
        _input.Start();
        _poll = PollAsync();
    }

    internal static BoardSnapshot Empty => new([], "Hardware noch nicht verfuegbar",
        "Internetstatus nicht verfuegbar", []);

    internal static bool SameHardware(BoardSnapshot a, BoardSnapshot b) =>
        a.Board == b.Board && a.Internet == b.Internet && a.Devices.SequenceEqual(b.Devices) &&
        a.Warnings.SequenceEqual(b.Warnings);

    private async Task PollAsync()
    {
        BoardSnapshot hardware = Empty;
        var fallbackRead = false;
        while (!_stop.IsCancellationRequested)
        {
            BoardFeedFrame? frame = null;
            var status = "AETHER NICHT VERBUNDEN / HARDWARE-MOMENTAUFNAHME / KEINE LIVE-MESSWERTE";
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                frame = await BoardLiveFeed.ReadAsync(deadline.Token);
                if (!BoardLiveFeed.IsFresh(frame, DateTimeOffset.UtcNow))
                    throw new InvalidDataException("AETHER-Messwerte sind veraltet.");
                if (!SameHardware(hardware, frame.Hardware)) hardware = frame.Hardware;
                status = $"AETHER LOKAL VERBUNDEN / {frame.Timestamp.ToLocalTime():HH:mm:ss} / LUEFTER UND DATENFLUSS SYMBOLISCH";
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or
                OperationCanceledException or JsonException or InvalidOperationException)
            {
                Trace.TraceWarning("AETHER Bildschirmschoner-Livefeed: {0}", ex.Message);
                frame = null;
                status += " / " + (ex is OperationCanceledException ? "Verbindung nicht verfuegbar" : ex.Message);
            }
            if (frame is null && !fallbackRead)
            {
                fallbackRead = true;
                try { hardware = await HardwareTopology.ReadAsync(_stop.Token); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
                    System.ComponentModel.Win32Exception or JsonException or TimeoutException)
                {
                    Trace.TraceError("AETHER Bildschirmschoner-Hardware: {0}", ex);
                    hardware = Empty with { Warnings = [ex.Message] };
                    status += " / Hardware: " + ex.Message;
                }
            }
            if (_stop.IsCancellationRequested) break;
            await app.Dispatcher.InvokeAsync(() =>
            {
                if (!_stop.IsCancellationRequested)
                    foreach (var window in _windows)
                        window.Present(hardware, frame, status);
            });
            try { await Task.Delay(TimeSpan.FromSeconds(2), _stop.Token); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
        }
    }

    private void CheckInput(object? sender, EventArgs e)
    {
        if (arguments.Mode == SaverMode.Preview)
        {
            if (_windows.Any(window => !window.ResizePreview())) Stop();
            return;
        }
        var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        if (!GetLastInputInfo(ref info))
        {
            Trace.TraceError("GetLastInputInfo fehlgeschlagen: {0}", Marshal.GetLastWin32Error());
            Stop();
            return;
        }
        if (Environment.TickCount64 - _started < 1200) { _lastInput = info.Time; return; }
        if (_lastInput is { } previous && info.Time != previous) Stop();
        _lastInput = info.Time;
    }

    private void ScreensChanged(object? sender, EventArgs e) => app.Dispatcher.BeginInvoke(Stop);

    private async void Stop()
    {
        if (_stopping) return;
        _stopping = true;
        Trace.TraceInformation("AETHER Bildschirmschoner wird beendet.");
        _input.Stop();
        _stop.Cancel();
        await _poll;
        Trace.TraceInformation("AETHER Bildschirmschoner: Datenabfragen beendet.");
        foreach (var window in _windows) window.Close();
        app.Shutdown();
    }

    public void Dispose()
    {
        _stopping = true;
        _input.Stop();
        SystemEvents.DisplaySettingsChanged -= ScreensChanged;
        Mouse.OverrideCursor = null;
        _stop.Cancel();
        foreach (var window in _windows.ToArray()) window.Close();
        _stop.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput { public uint Size, Time; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInput info);
}
