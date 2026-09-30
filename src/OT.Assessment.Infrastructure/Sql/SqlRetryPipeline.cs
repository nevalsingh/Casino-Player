using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;

namespace OT.Assessment.Infrastructure.Sql;

internal static class SqlRetryPipeline
{
    private static readonly HashSet<int> TransientErrorNumbers =
    [
        1205, -2, 4060, 40197, 40501, 40613, 49918, 49919, 49920, 233, 64, 10053, 10054, 10060,
    ];

    public static readonly ResiliencePipeline Instance = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<SqlException>(IsTransient),
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(100),
        })
        .Build();

    private static bool IsTransient(SqlException ex) => ex.Errors.Cast<SqlError>().Any(e => TransientErrorNumbers.Contains(e.Number));
}
