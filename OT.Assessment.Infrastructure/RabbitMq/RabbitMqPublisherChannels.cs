using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OT.Assessment.Core.Options;
using RabbitMQ.Client;

namespace OT.Assessment.Infrastructure.RabbitMq;

/// <summary>
/// A fixed set of confirm-enabled channels opened at startup. Each publish takes the next channel round-robin
/// and holds its lock for the duration, since a channel can't publish concurrently. If the broker restarts,
/// the connection's automatic recovery restores the channels, so nothing here recreates them.
/// </summary>
public sealed class RabbitMqPublisherChannels(
    RabbitMqConnectionProvider connectionProvider,
    IOptions<RabbitMqOptions> options) : IHostedService
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly List<(IChannel Channel, SemaphoreSlim Lock)> _channels = [];
    private int _next = -1;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
        var channelOptions = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);

        for (var i = 0; i < _options.PublisherChannelCount; i++)
            _channels.Add((await connection.CreateChannelAsync(channelOptions, cancellationToken), new SemaphoreSlim(1, 1)));
    }

    /// <summary>Publishes to the configured exchange and completes once the broker has confirmed the message.</summary>
    public async Task PublishAsync(BasicProperties properties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var (channel, channelLock) = _channels[(int)((uint)Interlocked.Increment(ref _next) % (uint)_channels.Count)];

        await channelLock.WaitAsync(cancellationToken);
        try
        {
            await channel.BasicPublishAsync(_options.Exchange, _options.RoutingKey, mandatory: false, properties, body, cancellationToken);
        }
        finally
        {
            channelLock.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}