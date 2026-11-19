using System.Data.Common;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using GSS.Util.Models;
using Microsoft.Extensions.Options;

namespace GSS.Api.HostedServices;

/// <summary>
/// Validates read and write connections at startup; fails startup before any request is served. Also warns when every
/// pod's pools together exceed the server's connection limit (skipped behind a connection pooler).
/// </summary>
public class DatabaseConnectionValidationHostedService(
    IDapperConnectionFactory connectionFactory,
    IOptions<DbConnectionModel> settings,
    ILogger<DatabaseConnectionValidationHostedService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Validating database connections...");
        var options = settings.Value;
        bool checkBudget = !options.BehindConnectionPooler;

        try
        {
            // One server for both: the write check counts both pools.
            bool separateReadServer = !DatabaseConnectionBudget.SameServer(options.Read, options.Write);
            await ValidateConnectionAsync(connectionFactory.CreateReadConnection(), write: false,
                checkBudget && separateReadServer, cancellationToken).ConfigureAwait(false);
            await ValidateConnectionAsync(connectionFactory.CreateWriteConnection(), write: true,
                checkBudget, cancellationToken).ConfigureAwait(false);

            logger.LogDebug("Database connections validated successfully");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Database connection validation failed. Application will not start.");
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task ValidateConnectionAsync(
        DbConnection connection,
        bool write,
        bool checkBudget,
        CancellationToken cancellationToken)
    {
        string connectionType = write ? "write" : "read";
        await using (connection.ConfigureAwait(false))
        {
            await DatabaseConnectionValidator.ValidateAsync(connection, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("Successfully validated {ConnectionType} database connection", connectionType);

            if (checkBudget)
            {
                await CheckConnectionBudgetAsync(connection, write, connectionType, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// A warning, not a failure: the server's limit is shared with other clients and pods rarely fill their pools,
    /// but a budget over the limit fails under load with "too many clients".
    /// </summary>
    private async Task CheckConnectionBudgetAsync(DbConnection connection, bool write, string connectionType, CancellationToken cancellationToken)
    {
        int required = DatabaseConnectionBudget.Required(settings.Value, write);
        int available = await DatabaseConnectionBudget.GetAvailableAsync(connection, cancellationToken).ConfigureAwait(false);
        if (required <= available)
        {
            logger.LogDebug("Database {ConnectionType} server: pools allow {Required} connections of {Available}", connectionType, required, available);
            return;
        }

        logger.LogWarning(
            "DbConnectionSettings let this service open {Required} connections to the {ConnectionType} server " +
            "({MaxReplicas} pods times their MaxPoolSize), but it accepts {Available} from all clients. Under load, " +
            "connections will fail with \"too many clients\". Lower MaxPoolSize, set MaxReplicas to the real pod count, " +
            "or put PgBouncer in front and set BehindConnectionPooler.",
            required, connectionType, settings.Value.MaxReplicas ?? DatabaseConnectionBudget.DefaultMaxReplicas, available);
    }
}
