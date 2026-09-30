using Microsoft.Extensions.Diagnostics.HealthChecks;
using OT.Assessment.Infrastructure.RabbitMq;

namespace OT.Assessment.Infrastructure.HealthChecks;

/// <summary>Verifies that the shared RabbitMQ connection is open.</summary>
public sealed class RabbitMqHealthCheck(RabbitMqConnectionProvider connectionProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
            return connection.IsOpen
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("RabbitMQ connection is not open.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is not reachable.", ex);
        }
    }
}