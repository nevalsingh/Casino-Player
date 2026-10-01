using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OT.Assessment.Core.Interfaces;
using OT.Assessment.Core.Models;
using OT.Assessment.Core.Options;
using OT.Assessment.Infrastructure.Redis;
using StackExchange.Redis;

namespace OT.Assessment.UnitTests;

public class CachedPlayerWagerReadRepositoryTests
{
    private static readonly TopSpender[] SqlResult =
    [
        new(Guid.NewGuid(), "big.spender", 1500m),
        new(Guid.NewGuid(), "small.spender", 250m),
    ];

    private readonly IPlayerWagerReadRepository _sql = Substitute.For<IPlayerWagerReadRepository>();
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly CachedPlayerWagerReadRepository _cache;

    public CachedPlayerWagerReadRepositoryTests()
    {
        _redis.IsConnected.Returns(true);
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);
        _sql.GetTopSpendersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(SqlResult);

        var options = Options.Create(new RedisOptions { KeyPrefix = "ot:", TopSpendersTtlSeconds = 10 });
        _cache = new CachedPlayerWagerReadRepository(_sql, _redis, options, NullLogger<CachedPlayerWagerReadRepository>.Instance);
    }

    [Fact]
    public async Task TopSpenders_CacheHit_SkipsSql()
    {
        var json = JsonSerializer.Serialize(SqlResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        _database.StringGetAsync("ot:topSpenders:10", Arg.Any<CommandFlags>()).Returns(new RedisValue(json));

        var result = await _cache.GetTopSpendersAsync(10, CancellationToken.None);

        Assert.Equal(SqlResult, result);
        await _sql.DidNotReceiveWithAnyArgs().GetTopSpendersAsync(default, default);
    }

    [Fact]
    public async Task TopSpenders_CacheMiss_QueriesSqlAndSetsWithTtl()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(RedisValue.Null);

        var result = await _cache.GetTopSpendersAsync(10, CancellationToken.None);

        Assert.Equal(SqlResult, result);
        _database.Received(1).StringSet(
            "ot:topSpenders:10", Arg.Any<RedisValue>(), TimeSpan.FromSeconds(10), When.Always, CommandFlags.FireAndForget);
    }

    [Fact]
    public async Task TopSpenders_RedisThrows_FallsBackToSql()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisConnectionException(
                ConnectionFailureType.SocketFailure, CommandFlags.None, "boom", null, CommandStatus.Unknown));

        var result = await _cache.GetTopSpendersAsync(10, CancellationToken.None);

        Assert.Equal(SqlResult, result);
    }

    [Fact]
    public async Task TopSpenders_RedisDisconnected_GoesStraightToSql()
    {
        _redis.IsConnected.Returns(false);

        var result = await _cache.GetTopSpendersAsync(10, CancellationToken.None);

        Assert.Equal(SqlResult, result);
        _redis.DidNotReceiveWithAnyArgs().GetDatabase();
    }

    [Fact]
    public async Task History_IsNotCached()
    {
        var accountId = Guid.NewGuid();
        var page = new PagedResult<PlayerCasinoWager>([], 1, 10, 0);
        _sql.GetWagerPageAsync(accountId, 1, 10, Arg.Any<CancellationToken>()).Returns(page);

        var result = await _cache.GetWagerPageAsync(accountId, 1, 10, CancellationToken.None);

        Assert.Same(page, result);
        _redis.DidNotReceiveWithAnyArgs().GetDatabase();
    }
}
