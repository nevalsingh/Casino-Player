using System.Threading.Channels;
using Microsoft.Extensions.Options;
using OT.Assessment.Core.Exceptions;
using OT.Assessment.Core.Interfaces;
using OT.Assessment.Core.Messaging;
using OT.Assessment.Core.Options;

namespace OT.Assessment.Consumer.Processing;

/// <summary>
/// Accumulates deliveries read off <see cref="ChannelReader{T}"/> into batches (by size or by delay,
/// whichever comes first) and writes each batch through <see cref="ICasinoWagerBatchWriter"/>.
/// Contains no RabbitMQ types, so it is fully unit-testable with fakes.
/// </summary>
/// <remarks>
/// A batch (or an isolated single message) is only ever dead-lettered for a <see cref="NonRetryableWagerDataException"/>
/// - a genuine data problem the database will never accept. Any other exception is treated as a transient
/// infrastructure problem (SQL down, network blip, timeout): the write is retried with capped exponential
/// backoff until it succeeds, and the message is never acked or rejected in the meantime, so it stays safely
/// unacked in RabbitMQ and would be redelivered if this process restarted.
/// </remarks>
public sealed class WagerBatchProcessor(
    ChannelReader<IncomingWager> reader,
    ICasinoWagerBatchWriter batchWriter,
    IDeliveryAcknowledger acknowledger,
    IOptions<IngestionOptions> options,
    IngestionMetrics metrics,
    ILogger<WagerBatchProcessor> logger,
    TimeSpan? initialRetryDelay = null,
    TimeSpan? maxRetryDelay = null)
{
    private readonly IngestionOptions _options = options.Value;
    private readonly TimeSpan _initialRetryDelay = initialRetryDelay ?? TimeSpan.FromSeconds(1);
    private readonly TimeSpan _maxRetryDelay = maxRetryDelay ?? TimeSpan.FromSeconds(30);

    /// <summary>Runs until the channel completes, flushing any partial batch before returning.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var batch = new List<IncomingWager>(_options.MaxBatchSize);
        var maxDelay = TimeSpan.FromMilliseconds(_options.MaxBatchDelayMs);

        // Wait for the first message of a batch, then keep filling it until it is full or the delay runs out.
        while (await WaitForDataAsync(timeout: null, cancellationToken))
        {
            var deadlineUtc = DateTime.UtcNow + maxDelay;
            do
            {
                while (batch.Count < _options.MaxBatchSize && reader.TryRead(out var item))
                    batch.Add(item);
                metrics.SetBufferDepth(batch.Count);
            }
            while (batch.Count < _options.MaxBatchSize
                   && await WaitForDataAsync(deadlineUtc - DateTime.UtcNow, cancellationToken));

            await FlushAsync(batch, cancellationToken);
            batch.Clear();
        }
    }

    /// <summary>Waits for a message; returns false on timeout (a <c>null</c> timeout waits forever) or when the channel completes.</summary>
    private async Task<bool> WaitForDataAsync(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
            return false;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } delay)
            timeoutCts.CancelAfter(delay);

        try
        {
            return await reader.WaitToReadAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false; // CancelAfter fired: a real timeout, not the caller cancelling.
        }
    }

    private async Task FlushAsync(List<IncomingWager> batch, CancellationToken cancellationToken)
    {
        var valid = new List<IncomingWager>(batch.Count);

        foreach (var item in batch)
        {
            if (item.Wager is null)
            {
                logger.LogWarning("Rejecting undeserializable message, delivery tag {DeliveryTag}", item.DeliveryTag);
                await acknowledger.RejectAsync(item.DeliveryTag, cancellationToken);
            }
            else
            {
                valid.Add(item);
            }
        }

        if (valid.Count > 0 && !await TryWriteAsync(valid.ConvertAll(i => i.Wager!), cancellationToken))
            await IsolateBadDataAsync(valid, cancellationToken);

        await acknowledger.AckUpToAsync(batch.Max(i => i.DeliveryTag), cancellationToken);
        metrics.SetBufferDepth(0);
    }

    /// <summary>
    /// The whole batch was rejected for bad data: write each message on its own and dead-letter only the ones
    /// the database still refuses.
    /// </summary>
    private async Task IsolateBadDataAsync(List<IncomingWager> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (await TryWriteAsync([item.Wager!], cancellationToken))
                continue;

            logger.LogError("Wager {WagerId} (tag {DeliveryTag}) rejected due to bad data; routing to dead-letter queue",
                item.Wager!.WagerId, item.DeliveryTag);
            await acknowledger.RejectAsync(item.DeliveryTag, cancellationToken);
        }
    }

    /// <summary>
    /// Writes the wagers and returns true. Returns false only for bad data (<see cref="NonRetryableWagerDataException"/>).
    /// Any other failure is treated as infrastructure trouble and retried forever with capped exponential backoff;
    /// nothing is acked or rejected in the meantime, so the messages stay safely unacked in RabbitMQ.
    /// </summary>
    private async Task<bool> TryWriteAsync(IReadOnlyList<CasinoWagerEvent> wagers, CancellationToken cancellationToken)
    {
        var delay = _initialRetryDelay;

        while (true)
        {
            try
            {
                metrics.AddWritten(await batchWriter.WriteAsync(wagers, cancellationToken));
                return true;
            }
            catch (NonRetryableWagerDataException ex)
            {
                logger.LogError(ex, "Write of {Count} wager(s) rejected due to bad data", wagers.Count);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Write of {Count} wager(s) failed with an infrastructure error; retrying in {DelaySeconds:F0}s. Messages remain unacked.",
                    wagers.Count, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxRetryDelay.Ticks));
            }
        }
    }
}