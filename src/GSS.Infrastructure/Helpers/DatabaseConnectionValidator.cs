using System.Data;
using System.Data.Common;

namespace GSS.Infrastructure.Helpers;

/// <summary>
/// Opens and pings database connections (startup validation and health checks).
/// </summary>
public static class DatabaseConnectionValidator
{
    private const string ValidationSql = "SELECT 1";

    // SQLSTATE invalid_catalog_name: database does not exist.
    private const string DatabaseMissingSqlState = "3D000";

    /// <summary>Opens the connection if closed and runs a trivial query, fully async.</summary>
    /// <exception cref="InvalidOperationException">The connection is not a <see cref="DbConnection"/>.</exception>
    public static async Task ValidateAsync(IDbConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection is not DbConnection dbConnection)
        {
            throw new InvalidOperationException(
                $"Connection type '{connection.GetType().FullName}' does not derive from System.Data.Common.DbConnection, " +
                "so it cannot be validated asynchronously.");
        }

        if (dbConnection.State != ConnectionState.Open)
        {
            await OpenAsync(dbConnection, cancellationToken).ConfigureAwait(false);
        }

        var command = dbConnection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = ValidationSql;
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Opens the connection; a missing database fails with its name and server.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the database does not exist.</exception>
    public static void Open(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            connection.Open();
        }
        catch (DbException ex) when (ex.SqlState == DatabaseMissingSqlState)
        {
            throw DatabaseMissing(connection, ex);
        }
    }

    /// <inheritdoc cref="Open(DbConnection)"/>
    public static async Task OpenAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex) when (ex.SqlState == DatabaseMissingSqlState)
        {
            throw DatabaseMissing(connection, ex);
        }
    }

    private static InvalidOperationException DatabaseMissing(DbConnection connection, DbException ex)
    {
        return new InvalidOperationException(
            $"Database '{connection.Database}' does not exist on server '{connection.DataSource}'. " +
            "The API never creates databases: create it, grant the application user access, " +
            "or fix DbConnectionSettings, then restart.", ex);
    }
}
