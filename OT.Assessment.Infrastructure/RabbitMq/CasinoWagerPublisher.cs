using System.Text.Json;
using OT.Assessment.Core.Interfaces;
using OT.Assessment.Core.Messaging;
using RabbitMQ.Client;

namespace OT.Assessment.Infrastructure.RabbitMq;

/// <summary>
/// Publishes casino wager events with publisher confirms, so a completed <see cref="PublishAsync"/> call
/// means the broker has durably accepted the message before the API returns 200 OK to the caller.
/// </summary>
public sealed class CasinoWagerPublisher(RabbitMqPublisherChannels channels) : ICasinoWagerPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task PublishAsync(CasinoWagerEvent wager, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = wager.WagerId.ToString(),
            Type = nameof(CasinoWagerEvent),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        };

        return channels.PublishAsync(properties, JsonSerializer.SerializeToUtf8Bytes(wager, SerializerOptions),
            cancellationToken);
    }
}