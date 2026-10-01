using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OT.Assessment.Consumer.Processing;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace OT.Assessment.UnitTests;

public class RabbitMqDeliveryAcknowledgerTests
{
    [Fact]
    public async Task AckAndReject_OnClosedChannel_DoNotThrow()
    {
        var closed = new AlreadyClosedException(new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "broker restarted"));
        var channel = Substitute.For<IChannel>();
        channel.BasicAckAsync(default, default, default).ReturnsForAnyArgs(_ => ValueTask.FromException(closed));
        channel.BasicNackAsync(default, default, default, default).ReturnsForAnyArgs(_ => ValueTask.FromException(closed));
        var acknowledger = new RabbitMqDeliveryAcknowledger(channel, NullLogger<RabbitMqDeliveryAcknowledger>.Instance);

        await acknowledger.AckUpToAsync(5, CancellationToken.None);
        await acknowledger.RejectAsync(5, CancellationToken.None);
    }
}
