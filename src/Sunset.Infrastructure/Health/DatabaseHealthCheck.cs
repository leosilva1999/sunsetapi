using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Sunset.Infrastructure.Persistence;

namespace Sunset.Infrastructure.Health;

public class DatabaseHealthCheck(SunsetDbContext context, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthContext, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database is unreachable.");
        }
        catch (Exception exception)
        {
            // Logged here; the response only says "unhealthy" so connection details never leak.
            logger.LogError(exception, "Database health check failed");
            return HealthCheckResult.Unhealthy("Database is unreachable.");
        }
    }
}
