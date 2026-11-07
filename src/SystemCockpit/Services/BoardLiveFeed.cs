using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text.Json;
using System.Windows.Threading;

namespace SystemCockpit.Services;

public sealed record BoardFeedFrame(DateTimeOffset Timestamp, BoardSnapshot Hardware, string Cpu, string Ram,
    string Gpu, string Download, double? Upload, string Temperature, bool Motion,
    SensorReading[]? Sensors = null, string DisplayMode = "auto");

public static class BoardLiveFeed
{
    internal static string PipeName => "AetherBoard-" +
        (WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Windows SID fehlt.")) +
        "-" + Process.GetCurrentProcess().SessionId;
    private const int MaxFrameBytes = 2_000_000;

    public static bool IsFresh(BoardFeedFrame frame, DateTimeOffset now) =>
        frame.Timestamp <= now.AddSeconds(3) && now - frame.Timestamp <= TimeSpan.FromSeconds(10);

    internal static async Task ServeAsync(CockpitModel model, Dispatcher dispatcher, CancellationToken token)
    {
        BoardSnapshot? hardware = null;
        var refresh = DateTimeOffset.MinValue;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = CreateServer(PipeName);
                await server.WaitForConnectionAsync(token);
                if (hardware is null || DateTimeOffset.UtcNow - refresh > TimeSpan.FromMinutes(1))
                {
                    hardware = await HardwareTopology.ReadAsync(token);
                    refresh = DateTimeOffset.UtcNow;
                }
                var frame = await dispatcher.InvokeAsync(() => new BoardFeedFrame(model.LastSampleAt, hardware,
                    model.Cards[0].Value, model.Cards[1].Value, model.Cards[2].Value, model.Cards[3].Value,
                    double.TryParse(model.SidebarUpload.NumericValue, out var upload) ? upload : null,
                    model.CpuTemperature, model.MotionEnabled, model.Sensors.Select(s =>
                        s.Value is { } value && !double.IsFinite(value) ? s with { Value = null } : s).ToArray(),
                    model.Settings.DisplayMode));
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                await WriteAsync(server, frame, deadline.Token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
                System.ComponentModel.Win32Exception or JsonException or TimeoutException or OperationCanceledException)
            {
                Trace.TraceError("AETHER Board-Livefeed: {0}", ex);
                try { await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        }
    }

    public static Task<BoardFeedFrame> ReadAsync(CancellationToken token) => ReadFromPipeAsync(PipeName, token);

    internal static NamedPipeServerStream CreateServer(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("Windows SID fehlt.");
        var security = new PipeSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        // CurrentUserOnly also matches elevation; this explicit SID ACL permits a read-only medium-integrity saver.
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, security);
    }

    internal static async Task<BoardFeedFrame> ReadFromPipeAsync(string pipeName, CancellationToken token)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Anonymous);
        await pipe.ConnectAsync(token);
        using var identity = WindowsIdentity.GetCurrent();
        if (!Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)), identity.User))
            throw new UnauthorizedAccessException("Board-Livefeed gehoert nicht zum aktuellen Windows-Konto.");
        return await ReadFrameAsync(pipe, token);
    }

    internal static async Task WriteAsync(Stream stream, BoardFeedFrame frame, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame);
        if (bytes.Length > MaxFrameBytes) throw new InvalidDataException("Board-Livefeed ist zu gross.");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    internal static async Task<BoardFeedFrame> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, token);
        var count = BitConverter.ToInt32(prefix);
        if (count is <= 0 or > MaxFrameBytes) throw new InvalidDataException("Board-Livefeed-Laenge ist ungueltig.");
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, token);
        var frame = JsonSerializer.Deserialize<BoardFeedFrame>(bytes)
            ?? throw new InvalidDataException("Board-Livefeed ist leer.");
        if (frame.DisplayMode is not ("auto" or "day" or "night"))
            throw new InvalidDataException("Board-Livefeed-Darstellungsmodus ist ungueltig.");
        if (frame.Hardware is null || frame.Hardware.Devices is null || frame.Hardware.Warnings is null ||
            frame.Hardware.Board is null || frame.Hardware.Internet is null ||
            frame.Cpu is null || frame.Ram is null || frame.Gpu is null || frame.Download is null || frame.Temperature is null)
            throw new InvalidDataException("Board-Livefeed ist unvollstaendig.");
        if (frame.Sensors?.Any(s => s is null || s.Id is null || s.Hardware is null ||
            s.Name is null || s.Type is null || s.Unit is null) == true)
            throw new InvalidDataException("Board-Livefeed enthaelt unvollstaendige Sensordaten.");
        return frame;
    }
}
