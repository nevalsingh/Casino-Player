using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace OT.Assessment.Infrastructure.HealthChecks;

/// <summary>
/// Pings Redis. Reports <see cref="HealthStatus.Degraded"/> rather than Unhealthy when it is unreachable,
/// because the cache is optional - the API keeps serving every endpoint from SQL Server without it.
/// </summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!redis.IsConnected)
            return HealthCheckResult.Degraded("Redis is not connected; reads are served uncached.");

        try
        {
            var latency = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Ping {latency.TotalMilliseconds:F1} ms");
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return HealthCheckResult.Degraded("Redis ping failed; reads are served uncached.", ex);
        }
    }
}