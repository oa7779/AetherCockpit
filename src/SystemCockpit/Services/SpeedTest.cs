using System.Diagnostics;
using System.Globalization;
using System.Net.Http;

namespace SystemCockpit.Services;

/// <summary>
/// Expliziter Download-Durchsatztest gegen das dokumentierte Cloudflare-Messziel.
/// Laeuft nur nach Freigabe im aufrufenden Dialog, nie automatisch.
/// Eine einzelne HTTPS-Verbindung; das Ergebnis ist der real erreichte
/// Anwendungsdurchsatz, keine Leitungs- oder Tarifgarantie.
/// </summary>
public static class SpeedTest
{
    public const string TargetHost = "speed.cloudflare.com";
    public const int MinMiB = 1, MaxMiB = 200;

    public static string FormatResult(long bytes, double seconds)
    {
        if (bytes < 0 || !double.IsFinite(seconds) || seconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        return string.Create(CultureInfo.CurrentCulture,
            $"{bytes / 1048576d:0.0} MiB in {seconds:0.0} s = {bytes * 8 / seconds / 1_000_000:0.0} Mbit/s");
    }

    public static async Task<string> DownloadAsync(int mib, CancellationToken token, IProgress<string>? progress = null)
    {
        if (mib is < MinMiB or > MaxMiB)
            throw new ArgumentException(DesktopUiText.Format("Speed.InvalidSize", MinMiB, MaxMiB));
        var requested = (long)mib * 1048576;
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(120));
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await client.GetAsync(
                $"https://{TargetHost}/__down?bytes={requested}", HttpCompletionOption.ResponseHeadersRead, limit.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(limit.Token);
            var buffer = new byte[81920];
            long total = 0;
            var lastReport = TimeSpan.Zero;
            int read;
            while ((read = await stream.ReadAsync(buffer, limit.Token)) > 0)
            {
                total += read;
                if (watch.Elapsed - lastReport >= TimeSpan.FromSeconds(2))
                {
                    lastReport = watch.Elapsed;
                    progress?.Report($"[SPEEDTEST] {FormatResult(total, watch.Elapsed.TotalSeconds)}{Environment.NewLine}");
                }
            }
            watch.Stop();
            if (total == 0) throw new InvalidOperationException(DesktopUiText.Get("Speed.NoData"));
            return DesktopUiText.Format("Speed.Result", FormatResult(total, watch.Elapsed.TotalSeconds),
                TargetHost, total / 1048576d);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException(DesktopUiText.Get("Speed.Timeout"));
        }
    }
}
