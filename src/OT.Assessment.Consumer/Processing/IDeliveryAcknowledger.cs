namespace OT.Assessment.Consumer.Processing;

public interface IDeliveryAcknowledger
{
    /// <summary>Acknowledges every delivery up to and including <paramref name="deliveryTag"/>.</summary>
    Task AckUpToAsync(ulong deliveryTag, CancellationToken cancellationToken);

    /// <summary>Rejects a single delivery without requeueing (routes it to the dead-letter queue).</summary>
    Task RejectAsync(ulong deliveryTag, CancellationToken cancellationToken);
}
