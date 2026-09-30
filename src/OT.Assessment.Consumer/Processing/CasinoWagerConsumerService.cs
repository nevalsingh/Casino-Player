using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using OT.Assessment.Core.Interfaces;
using OT.Assessment.Core.Messaging;
using OT.Assessment.Core.Options;
using OT.Assessment.Infrastructure.RabbitMq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace OT.Assessment.Consumer.Processing;

// <summary>
/// Consumes casino wager messages from RabbitMQ and hands them to a <see cref="WagerBatchProcessor"/> for
/// batched persistence. The RabbitMQ callback only deserializes and enqueues, keeping channel operations
/// (ack/nack) single-threaded on the batch processor's loop.
/// </summary>
public sealed class CasinoWagerConsumerService(
    RabbitMqConnectionProvider connectionProvider,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    IOptions<IngestionOptions> ingestionOptions,
    ICasinoWagerBatchWriter batchWriter,
    IngestionMetrics metrics,
    IHostApplicationLifetime appLifetime,
    ILoggerFactory loggerFactory,
    ILogger<CasinoWagerConsumerService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqOptions _rabbitMqOptions = rabbitMqOptions.Value;

    private IChannel? _channel;
    private string? _consumerTag;
    private Channel<IncomingWager>? _internalChannel;
    private Task? _processingTask;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = await connectionProvider.GetConnectionAsync(stoppingToken);
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.BasicQosAsync(0, _rabbitMqOptions.PrefetchCount, global: false, stoppingToken);

        _internalChannel = Channel.CreateBounded<IncomingWager>(new BoundedChannelOptions(_rabbitMqOptions.PrefetchCount)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var acknowledger = new RabbitMqDeliveryAcknowledger(_channel, loggerFactory.CreateLogger<RabbitMqDeliveryAcknowledger>());
        var processor = new WagerBatchProcessor(
            _internalChannel.Reader,
            batchWriter,
            acknowledger,
            ingestionOptions,
            metrics,
            loggerFactory.CreateLogger<WagerBatchProcessor>());

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            CasinoWagerEvent? wager = null;
            try
            {
                wager = JsonSerializer.Deserialize<CasinoWagerEvent>(ea.Body.Span, SerializerOptions);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to deserialize message, delivery tag {DeliveryTag}", ea.DeliveryTag);
            }

            await _internalChannel.Writer.WriteAsync(new IncomingWager(ea.DeliveryTag, wager), CancellationToken.None);
        };

        _consumerTag = await _channel.BasicConsumeAsync(_rabbitMqOptions.Queue, autoAck: false, consumer, stoppingToken);

        var processingTask = processor.RunAsync(CancellationToken.None);
        _processingTask = processingTask;

        // The processor is designed to retry infrastructure failures forever and never throw for them; if it
        // still faults (a bug, an unhandled edge case), the safest thing is to stop the host rather than hang
        // silently with prefetch full. On restart, RabbitMQ redelivers everything left unacked.
        _ = processingTask.ContinueWith(
            t =>
            {
                logger.LogCritical("Batch processor faulted unexpectedly; stopping the host so unacked messages are redelivered on restart");
                appLifetime.StopApplication();
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

        logger.LogInformation("Consuming from queue {Queue} with prefetch {Prefetch}", _rabbitMqOptions.Queue, _rabbitMqOptions.PrefetchCount);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping consumer: cancelling subscription and draining in-flight batches");
        try
        {
            if (_channel is not null && _consumerTag is not null)
                await _channel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken);
        }
        catch (OperationInterruptedException)
        {
            logger.LogInformation("Channel already closed; nothing to cancel");
        }

        _internalChannel?.Writer.TryComplete();

        if (_processingTask is not null)
        {
            try
            {
                await _processingTask;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Processing task ended with an exception during shutdown");
            }
        }

        try
        {
            if (_channel is not null)
                await _channel.CloseAsync(cancellationToken);
        }
        catch (OperationInterruptedException)
        {
            logger.LogInformation("Channel already closed");
        }

        await base.StopAsync(cancellationToken);
    }
}
