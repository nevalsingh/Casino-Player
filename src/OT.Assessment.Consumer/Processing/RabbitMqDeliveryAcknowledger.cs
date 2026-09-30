using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace OT.Assessment.Consumer.Processing;

public sealed class RabbitMqDeliveryAcknowledger(IChannel channel, ILogger<RabbitMqDeliveryAcknowledger> logger) : IDeliveryAcknowledger
{
    public async Task AckUpToAsync(ulong deliveryTag, CancellationToken cancellationToken)
    {
        try
        {
            await channel.BasicAckAsync(deliveryTag, multiple: true, cancellationToken);
        }
        catch (OperationInterruptedException ex)
        {
            logger.LogWarning(ex, "Channel closed before ack; messages will be redelivered and duplicates skipped");
        }
    }

    public async Task RejectAsync(ulong deliveryTag, CancellationToken cancellationToken)
    {
        try
        {
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken);
        }
        catch (OperationInterruptedException ex)
        {
            logger.LogWarning(ex, "Channel closed before nack; the message will be redelivered");
        }
    }
}