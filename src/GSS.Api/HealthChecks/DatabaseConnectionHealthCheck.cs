using System.Data;
using GSS.Infrastructure.Helpers;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GSS.Api.HealthChecks;

/// <summary>
/// Opens a connection and runs <c>SELECT 1</c>. Exceptions propagate; HealthChecks.NET reports them sanitized.
/// </summary>
public sealed class DatabaseConnectionHealthCheck(Func<IDbConnection> connectionFactory) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory();
        await DatabaseConnectionValidator.ValidateAsync(connection, cancellationToken).ConfigureAwait(false);

        return HealthCheckResult.Healthy("database reachable");
    }
}
