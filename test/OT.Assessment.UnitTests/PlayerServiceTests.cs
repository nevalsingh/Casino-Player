using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OT.Assessment.App.Contracts;
using OT.Assessment.App.Services;
using OT.Assessment.Core.Interfaces;
using OT.Assessment.Core.Messaging;
using OT.Assessment.Core.Models;

namespace OT.Assessment.UnitTests;

public class PlayerServiceTests
{
    private readonly ICasinoWagerPublisher _publisher = Substitute.For<ICasinoWagerPublisher>();
    private readonly IPlayerWagerReadRepository _repository = Substitute.For<IPlayerWagerReadRepository>();
    private readonly PlayerService _service;

    public PlayerServiceTests()
    {
        _service = new PlayerService(NullLogger<PlayerService>.Instance, _publisher, _repository);
    }

    private static CasinoWagerRequest NewRequest() => new()
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
        Amount = 123.45,
        CreatedDateTime = DateTimeOffset.Parse("2024-05-04T02:25:05+02:00"),
        NumberOfBets = 3,
        CountryCode = "BS",
        Duration = 1827254,
    };

    [Fact]
    public async Task Publish_Success_ReturnsTrueAndMapsTheEvent()
    {
        var request = NewRequest();

        var result = await _service.PublishWagerAsync(request, CancellationToken.None);

        Assert.True(result);
        await _publisher.Received(1).PublishAsync(
            Arg.Is<CasinoWagerEvent>(e =>
                e.WagerId == request.WagerId &&
                e.Provider == request.Provider &&
                e.GameName == request.GameName &&
                e.AccountId == request.AccountId &&
                e.Username == request.Username &&
                e.Amount == 123.45m &&
                e.CreatedDateTime == request.CreatedDateTime &&
                e.CountryCode == request.CountryCode),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_WhenPublisherThrows_ReturnsFalse()
    {
        _publisher.PublishAsync(Arg.Any<CasinoWagerEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("broker down"));

        var result = await _service.PublishWagerAsync(NewRequest(), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Publish_WhenCancelled_Propagates()
    {
        _publisher.PublishAsync(Arg.Any<CasinoWagerEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.PublishWagerAsync(NewRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task GetCasinoHistory_MapsRowsAndPagingInfo()
    {
        var accountId = Guid.NewGuid();
        var wager = new PlayerCasinoWager(Guid.NewGuid(), "Sweet Bonanza", "Pragmatic", 42m, DateTime.UtcNow);
        _repository.GetWagerPageAsync(accountId, 2, 10, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<PlayerCasinoWager>([wager], 2, 10, 35));

        var response = await _service.GetCasinoHistoryAsync(accountId, 2, 10, CancellationToken.None);

        Assert.Equal(2, response.Page);
        Assert.Equal(10, response.PageSize);
        Assert.Equal(35, response.Total);
        Assert.Equal(4, response.TotalPages);
        var item = Assert.Single(response.Data);
        Assert.Equal(wager.WagerId, item.WagerId);
        Assert.Equal("Sweet Bonanza", item.Game);
        Assert.Equal("Pragmatic", item.Provider);
        Assert.Equal(42m, item.Amount);
        Assert.Equal(wager.CreatedDate, item.CreatedDate);
    }

    [Fact]
    public async Task GetTopSpenders_MapsRowsInOrder()
    {
        var first = new TopSpender(Guid.NewGuid(), "big", 1500m);
        var second = new TopSpender(Guid.NewGuid(), "small", 250m);
        _repository.GetTopSpendersAsync(5, Arg.Any<CancellationToken>()).Returns(new[] { first, second });

        var response = await _service.GetTopSpendersAsync(5, CancellationToken.None);

        Assert.Equal(2, response.Count);
        Assert.Equal(new TopSpenderResponse(first.AccountId, "big", 1500m), response[0]);
        Assert.Equal(new TopSpenderResponse(second.AccountId, "small", 250m), response[1]);
    }
}
