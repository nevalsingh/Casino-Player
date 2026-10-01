using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OT.Assessment.Consumer.Processing;
using OT.Assessment.Core.Exceptions;
using OT.Assessment.Core.Interfaces;
using OT.Assessment.Core.Messaging;
using OT.Assessment.Core.Options;

namespace OT.Assessment.UnitTests;

public class WagerBatchProcessorTests
{
    private readonly Channel<IncomingWager> _channel = Channel.CreateUnbounded<IncomingWager>();
    private readonly ICasinoWagerBatchWriter _writer = Substitute.For<ICasinoWagerBatchWriter>();
    private readonly IDeliveryAcknowledger _acknowledger = Substitute.For<IDeliveryAcknowledger>();

    private static CasinoWagerEvent NewWager() => new()
    {
        WagerId = Guid.NewGuid(),
        Theme = "Slots",
        Provider = "Pragmatic",
        GameName = "Sweet Bonanza",
        TransactionId = Guid.NewGuid(),
        BrandId = Guid.NewGuid(),
        AccountId = Guid.NewGuid(),
        Username = "player1",
        ExternalReferenceId = Guid.NewGuid(),
        TransactionTypeId = Guid.NewGuid(),
        Amount = 10m,
        CreatedDateTime = DateTimeOffset.UtcNow,
        NumberOfBets = 1,
        CountryCode = "US",
        Duration = 100,
    };

    private WagerBatchProcessor CreateProcessor(int maxBatchSize, int maxBatchDelayMs)
    {
        var options = Options.Create(new IngestionOptions { MaxBatchSize = maxBatchSize, MaxBatchDelayMs = maxBatchDelayMs });
        var retryDelay = TimeSpan.FromMilliseconds(5); // keeps the retry test fast

        return new WagerBatchProcessor(
            _channel.Reader, _writer, _acknowledger, options, new IngestionMetrics(),
            NullLogger<WagerBatchProcessor>.Instance, retryDelay, retryDelay);
    }

    private async Task QueueAsync(ulong tag, CasinoWagerEvent? wager) =>
        await _channel.Writer.WriteAsync(new IncomingWager(tag, wager));

    [Fact]
    public async Task FlushesWhenBatchIsFull()
    {
        // Long delay, so the only reason to flush at 2 is the size limit.
        var processor = CreateProcessor(maxBatchSize: 2, maxBatchDelayMs: 60_000);
        await QueueAsync(1, NewWager());
        await QueueAsync(2, NewWager());
        await QueueAsync(3, NewWager());
        _channel.Writer.Complete();

        await processor.RunAsync(CancellationToken.None);

        Received.InOrder(() =>
        {
            _writer.WriteAsync(Arg.Is<IReadOnlyList<CasinoWagerEvent>>(b => b.Count == 2), Arg.Any<CancellationToken>());
            _writer.WriteAsync(Arg.Is<IReadOnlyList<CasinoWagerEvent>>(b => b.Count == 1), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task FlushesAfterDelayEvenWhenBatchIsNotFull()
    {
        var acked = new TaskCompletionSource();
        _acknowledger.AckUpToAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                acked.TrySetResult();
                return Task.CompletedTask;
            });
        var processor = CreateProcessor(maxBatchSize: 100, maxBatchDelayMs: 50);
        await QueueAsync(1, NewWager());

        // The channel stays open, so a flush can only come from the delay timer.
        var run = processor.RunAsync(CancellationToken.None);
        await acked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _channel.Writer.Complete();
        await run;

        await _writer.Received(1).WriteAsync(Arg.Is<IReadOnlyList<CasinoWagerEvent>>(b => b.Count == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcksTheHighestDeliveryTagOfTheBatch()
    {
        var processor = CreateProcessor(maxBatchSize: 10, maxBatchDelayMs: 60_000);
        await QueueAsync(1, NewWager());
        await QueueAsync(2, NewWager());
        await QueueAsync(3, NewWager());
        _channel.Writer.Complete();

        await processor.RunAsync(CancellationToken.None);

        await _acknowledger.Received(1).AckUpToAsync(3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsUndeserializableMessagesAndWritesTheRest()
    {
        var processor = CreateProcessor(maxBatchSize: 10, maxBatchDelayMs: 60_000);
        await QueueAsync(1, null);
        await QueueAsync(2, NewWager());
        _channel.Writer.Complete();

        await processor.RunAsync(CancellationToken.None);

        await _acknowledger.Received(1).RejectAsync(1, Arg.Any<CancellationToken>());
        await _writer.Received(1).WriteAsync(Arg.Is<IReadOnlyList<CasinoWagerEvent>>(b => b.Count == 1), Arg.Any<CancellationToken>());
        await _acknowledger.Received(1).AckUpToAsync(2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DataException_IsIsolatedAndOnlyTheBadMessageIsRejected()
    {
        var good = NewWager();
        var bad = NewWager();
        var dataError = new NonRetryableWagerDataException("bad row", new Exception());

        // The batch of two fails, then the good one succeeds alone and the bad one fails alone.
        _writer.WriteAsync(Arg.Is<IReadOnlyList<CasinoWagerEvent>>(b => b.Count == 2), Arg.Any<CancellationToken>())
            .ThrowsAsync(dataError);
        _writer.WriteAsync(Arg.Is<IReadOnlyList<CasinoWagerEvent>>(b => b.Count == 1 && b[0].WagerId == good.WagerId), Arg.Any<CancellationToken>())
            .Returns(1);
        _writer.WriteAsync(Arg.Is<IReadOnlyList<CasinoWagerEvent>>(b => b.Count == 1 && b[0].WagerId == bad.WagerId), Arg.Any<CancellationToken>())
            .ThrowsAsync(dataError);

        var processor = CreateProcessor(maxBatchSize: 10, maxBatchDelayMs: 60_000);
        await QueueAsync(1, good);
        await QueueAsync(2, bad);
        _channel.Writer.Complete();

        await processor.RunAsync(CancellationToken.None);

        await _acknowledger.Received(1).RejectAsync(2, Arg.Any<CancellationToken>());
        await _acknowledger.DidNotReceive().RejectAsync(1, Arg.Any<CancellationToken>());
        await _acknowledger.Received(1).AckUpToAsync(2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransientFailure_RetriesAndNeverRejects()
    {
        var attempts = 0;
        _writer.WriteAsync(Arg.Any<IReadOnlyList<CasinoWagerEvent>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempts++;
                if (attempts < 4)
                    throw new TimeoutException("sql is down");
                return Task.FromResult(1);
            });

        var processor = CreateProcessor(maxBatchSize: 10, maxBatchDelayMs: 60_000);
        await QueueAsync(1, NewWager());
        _channel.Writer.Complete();

        await processor.RunAsync(CancellationToken.None);

        Assert.Equal(4, attempts);
        await _acknowledger.DidNotReceiveWithAnyArgs().RejectAsync(default, default);
        await _acknowledger.Received(1).AckUpToAsync(1, Arg.Any<CancellationToken>());
    }
}
