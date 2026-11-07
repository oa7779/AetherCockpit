namespace AetherManager.Server.Services;

internal sealed class ReportRetentionWorker(IManagerStore store, ILogger<ReportRetentionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var removed = await store.PurgeExpiredReportsAsync(stoppingToken);
            logger.LogInformation("Removed {ReportCount} diagnostic reports older than the 90-day retention period.", removed);
        }
    }
}
