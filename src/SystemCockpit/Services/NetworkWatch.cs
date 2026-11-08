using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows.Threading;

namespace SystemCockpit.Services;

internal sealed class NetworkWatch : Observable, IDisposable
{
    private readonly Func<CockpitSettings> _settings;
    private readonly Action<string> _notify;
    private readonly DispatcherTimer _timer;
    private readonly ConcurrentQueue<PacketSummary> _queue = new();
    private readonly ConcurrentQueue<string> _alerts = new();
    private CancellationTokenSource? _captureStop;
    private Task _capture = Task.CompletedTask;
    private Task _connectionRead = Task.CompletedTask;
    private readonly CancellationTokenSource _stop = new();
    private DateTimeOffset _lastConnectionAlert;
    private DateTimeOffset _lastPrune;
    private DateTimeOffset _lastNotification;
    private string _status = DesktopUiText.Get("Network.NoCapture");
    private int _connections;
    private long _packets, _dropped;
    private long _captureCount, _captureDrops;
    private bool _disposed;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public int Connections { get => _connections; private set => Set(ref _connections, value); }
    public long Packets { get => _packets; private set => Set(ref _packets, value); }
    public long DisplayDropped { get => _dropped; private set => Set(ref _dropped, value); }
    public ObservableCollection<PacketSummary> RecentPackets { get; } = [];
    public ObservableCollection<string> Alerts { get; } = [];
    public bool IsCapturing => !_capture.IsCompleted;
    public static string CaptureDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AetherCockpit", "Captures");
    private static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AetherCockpit", "NetworkAlerts");

    public NetworkWatch(Func<CockpitSettings> settings, Action<string> notify)
    {
        _settings = settings;
        _notify = notify;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Tick();
    }

    public void StartMonitoring() => _timer.Start();

    public IReadOnlyList<CaptureDevice> Devices()
    {
        using var capture = new NpcapCapture();
        return capture.Devices();
    }

    public void StartCapture(string device)
    {
        if (IsCapturing) throw new InvalidOperationException(DesktopUiText.Get("Network.AlreadyCapturing"));
        if (string.IsNullOrWhiteSpace(device)) throw new ArgumentException(DesktopUiText.Get("Network.SelectDevice"));
        _captureStop?.Dispose();
        _captureStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        var token = _captureStop.Token;
        Status = DesktopUiText.Get("Network.OpeningCapture");
        Packets = 0;
        DisplayDropped = 0;
        Interlocked.Exchange(ref _captureCount, 0);
        Interlocked.Exchange(ref _captureDrops, 0);
        RecentPackets.Clear();
        _capture = CaptureAsync(device, token);
    }

    private async Task CaptureAsync(string device, CancellationToken token)
    {
        try
        {
            var rules = new PacketAlertRules();
            var progress = new Progress<string>(path => Status =
                DesktopUiText.Format("Network.CaptureActive", path));
            var path = await Task.Run(() =>
            {
                using var capture = new NpcapCapture();
                return capture.Run(device, CaptureDirectory, packet =>
                {
                    Interlocked.Increment(ref _captureCount);
                    if (_queue.Count < 500) _queue.Enqueue(packet);
                    else Interlocked.Increment(ref _captureDrops);
                    var alert = rules.Observe(packet, _settings());
                    if (alert is not null && _alerts.Count < 100) _alerts.Enqueue(alert);
                }, token, path => ((IProgress<string>)progress).Report(path));
            });
            Drain();
            Packets = Interlocked.Read(ref _captureCount);
            DisplayDropped = Interlocked.Read(ref _captureDrops);
            Status = DesktopUiText.Format("Network.CaptureFinished", path, Packets, DisplayDropped);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or
            InvalidOperationException or ArgumentException or NotSupportedException or EntryPointNotFoundException or BadImageFormatException)
        {
            Status = DesktopUiText.Format("Network.CaptureFailed", ex.Message);
            Report(Status);
        }
    }

    private void Tick()
    {
        Drain();
        Packets = Interlocked.Read(ref _captureCount);
        DisplayDropped = Interlocked.Read(ref _captureDrops);
        if (_settings().NetworkMonitorEnabled && _connectionRead.IsCompleted)
            _connectionRead = ReadConnectionsAsync();
        if (DateTimeOffset.UtcNow - _lastPrune > TimeSpan.FromHours(1))
        {
            _lastPrune = DateTimeOffset.UtcNow;
            try
            {
                if (!IsCapturing && Directory.Exists(CaptureDirectory)) NpcapCapture.Prune(CaptureDirectory, DateTime.UtcNow);
                if (Directory.Exists(LogDirectory))
                    foreach (var file in new DirectoryInfo(LogDirectory).EnumerateFiles("alerts-*.log"))
                        if (file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-7)) file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Report(DesktopUiText.Format("Network.RetentionFailed", ex.Message));
            }
        }
    }

    private void Drain()
    {
        while (_queue.TryDequeue(out var packet))
        {
            RecentPackets.Insert(0, packet);
            while (RecentPackets.Count > 200) RecentPackets.RemoveAt(RecentPackets.Count - 1);
        }
        while (_alerts.TryDequeue(out var alert)) Report(alert);
    }

    private async Task ReadConnectionsAsync()
    {
        try
        {
            var count = await Task.Run(() => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
                .Count(connection => connection.State is TcpState.Established or TcpState.SynSent), _stop.Token);
            Connections = count;
            if (_settings().NetworkMonitorEnabled && count >= _settings().ConnectionAlertThreshold &&
                DateTimeOffset.UtcNow - _lastConnectionAlert > TimeSpan.FromMinutes(1))
            {
                _lastConnectionAlert = DateTimeOffset.UtcNow;
                Report(DesktopUiText.Format("Network.ConnectionAlert", count, _settings().ConnectionAlertThreshold));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) when (ex is NetworkInformationException or Win32Exception)
        {
            Status = DesktopUiText.Format("Network.MonitorFailed", ex.Message);
            System.Diagnostics.Trace.TraceError("{0}", ex);
        }
    }

    private void Report(string message)
    {
        if (_disposed) return;
        var line = $"{DateTimeOffset.Now:O} | {message}";
        Alerts.Insert(0, line);
        while (Alerts.Count > 200) Alerts.RemoveAt(Alerts.Count - 1);
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"alerts-{DateTime.UtcNow:yyyyMMdd}.log");
            if (File.Exists(path) && new FileInfo(path).Length >= 2 * 1024 * 1024)
                throw new IOException(DesktopUiText.Get("Network.LogLimit"));
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = "Alarm sichtbar, aber Protokoll nicht schreibbar: " + ex.Message;
            System.Diagnostics.Trace.TraceError("Netzwerkprotokoll: {0}", ex);
        }
        if (DateTimeOffset.UtcNow - _lastNotification >= TimeSpan.FromSeconds(30))
        {
            _lastNotification = DateTimeOffset.UtcNow;
            _notify(message);
        }
    }

    public async Task StopCaptureAsync()
    {
        _captureStop?.Cancel();
        await _capture;
    }
    public async Task StopAsync()
    {
        _timer.Stop();
        _stop.Cancel();
        await StopCaptureAsync();
        await _connectionRead;
    }
    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _captureStop?.Dispose();
        _stop.Dispose();
    }
}
