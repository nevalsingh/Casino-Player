using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OT.Assessment.Core.Options;
using RabbitMQ.Client;

namespace OT.Assessment.Infrastructure.RabbitMq;

/// <summary>
/// Declares the exchange, queue and dead-letter queue on startup. /// </summary>
public sealed class RabbitMqTopologyInitializer(
    RabbitMqConnectionProvider connectionProvider,
    IOptions<RabbitMqOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        // Dead-letter side first, so the main queue can reference it.
        await channel.ExchangeDeclareAsync(settings.DeadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(settings.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(settings.DeadLetterQueue, settings.DeadLetterExchange, routingKey: string.Empty, cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(settings.Exchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
        var queueArguments = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = settings.DeadLetterExchange };
        await channel.QueueDeclareAsync(settings.Queue, durable: true, exclusive: false, autoDelete: false, arguments: queueArguments, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(settings.Queue, settings.Exchange, settings.RoutingKey, cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}