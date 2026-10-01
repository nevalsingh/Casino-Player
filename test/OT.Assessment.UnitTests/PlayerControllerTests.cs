using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OT.Assessment.App.Contracts;
using OT.Assessment.App.Controllers;
using OT.Assessment.App.Services;

namespace OT.Assessment.UnitTests;

public class PlayerControllerTests
{
    private readonly IPlayerService _service = Substitute.For<IPlayerService>();
    private readonly PlayerController _controller;

    public PlayerControllerTests()
    {
        // Problem() looks up a ProblemDetailsFactory from the request services, so give it a real one.
        var services = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider();

        _controller = new PlayerController(_service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
    }

    [Fact]
    public async Task CasinoWager_WhenPublished_Returns200()
    {
        _service.PublishWagerAsync(Arg.Any<CasinoWagerRequest>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _controller.CasinoWager(new CasinoWagerRequest(), CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task CasinoWager_WhenPublishFails_Returns503()
    {
        _service.PublishWagerAsync(Arg.Any<CasinoWagerRequest>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _controller.CasinoWager(new CasinoWagerRequest(), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, problem.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetCasinoHistory_BadPage_Returns400(int page)
    {
        var result = await _controller.GetCasinoHistory(Guid.NewGuid(), page, 10);

        Assert.Equal(400, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task GetCasinoHistory_BadPageSize_Returns400(int pageSize)
    {
        var result = await _controller.GetCasinoHistory(Guid.NewGuid(), 1, pageSize);

        Assert.Equal(400, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task GetCasinoHistory_Valid_Returns200WithData()
    {
        var playerId = Guid.NewGuid();
        var expected = new CasinoWagerHistoryResponse([], 1, 10, 0, 0);
        _service.GetCasinoHistoryAsync(playerId, 1, 10, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _controller.GetCasinoHistory(playerId, 1, 10);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task GetTopSpenders_BadCount_Returns400(int count)
    {
        var result = await _controller.GetTopSpenders(count);

        Assert.Equal(400, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task GetTopSpenders_Valid_Returns200WithData()
    {
        IReadOnlyList<TopSpenderResponse> expected = [new(Guid.NewGuid(), "big", 1500m)];
        _service.GetTopSpendersAsync(3, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _controller.GetTopSpenders(3);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
    }
}
