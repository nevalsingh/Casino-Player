namespace OT.Assessment.Consumer.Processing;

public sealed class ThroughputLoggerService(IngestionMetrics metrics, ILogger<ThroughputLoggerService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastWritten = 0L;
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var written = metrics.MessagesWritten;
            var rate = (written - lastWritten) / Interval.TotalSeconds;
            lastWritten = written;

            logger.LogInformation(
                "Throughput: {Written} messages written total, {Rate:F0} msg/s, buffer depth {BufferDepth}",
                written, rate, metrics.CurrentBufferDepth);
        }
    }
}