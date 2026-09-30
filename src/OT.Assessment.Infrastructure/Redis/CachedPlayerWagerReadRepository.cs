using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OT.Assessment.Core.Interfaces;
using OT.Assessment.Core.Models;
using OT.Assessment.Core.Options;
using StackExchange.Redis;

namespace OT.Assessment.Infrastructure.Redis;


public sealed class CachedPlayerWagerReadRepository(
    IPlayerWagerReadRepository inner,
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> options,
    ILogger<CachedPlayerWagerReadRepository> logger) : IPlayerWagerReadRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RedisOptions _options = options.Value;

    public Task<PagedResult<PlayerCasinoWager>> GetWagerPageAsync(Guid accountId, int page, int pageSize, CancellationToken cancellationToken) =>
        inner.GetWagerPageAsync(accountId, page, pageSize, cancellationToken);

    public async Task<IReadOnlyList<TopSpender>> GetTopSpendersAsync(int count, CancellationToken cancellationToken)
    {
        var key = $"{_options.KeyPrefix}topSpenders:{count}";

        var cached = await TryGetAsync(key);
        if (cached is not null)
            return cached;

        var topSpenders = await inner.GetTopSpendersAsync(count, cancellationToken);
        TrySet(key, topSpenders);
        return topSpenders;
    }

    private async Task<IReadOnlyList<TopSpender>?> TryGetAsync(string key)
    {
        // Skip straight to SQL while Redis is down instead of timeout on every request.
        if (!redis.IsConnected)
            return null;

        try
        {
            var value = await redis.GetDatabase().StringGetAsync(key);
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<List<TopSpender>>((string)value!, SerializerOptions);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or JsonException)
        {
            logger.LogWarning(ex, "Redis read failed for {CacheKey}; falling back to SQL Server", key);
            return null;
        }
    }

    private void TrySet(string key, IReadOnlyList<TopSpender> value)
    {
        if (!redis.IsConnected)
            return;

        try
        {
            // Fire-and-forget do not await cache write.
            redis.GetDatabase().StringSet(
                key,
                JsonSerializer.Serialize(value, SerializerOptions),
                TimeSpan.FromSeconds(_options.TopSpendersTtlSeconds),
                When.Always,
                CommandFlags.FireAndForget);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis write failed for {CacheKey}; result served uncached", key);
        }
    }
}