using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SupportDesk.Presentation.Health;

/// <summary>A readiness probe for reachability, not a schema-migration or seed-data check.</summary>
public sealed class DatabaseHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // A probe must not inherit the application's transient-failure retry policy or
            // reuse a pooled connection that was opened before an outage.
            var settings = new SqlConnectionStringBuilder(configuration.GetConnectionString("Default"))
            {
                ConnectTimeout = 3,
                ConnectRetryCount = 0,
                Pooling = false
            };
            await using var connection = new SqlConnection(settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            // Probe output must not expose provider errors or connection strings, including on timeout.
            return HealthCheckResult.Unhealthy("Database is unavailable.");
        }
    }
}
