using System.Data.Common;
using System.Globalization;
using GSS.Util.Models;

namespace GSS.Infrastructure.Helpers;

/// <summary>
/// The connections every pod of this service can hold on one PostgreSQL server, against what the server accepts.
/// </summary>
public static class DatabaseConnectionBudget
{
    /// <summary>Pods assumed when <see cref="DbConnectionModel.MaxReplicas"/> is unset.</summary>
    public const int DefaultMaxReplicas = 1;

    /// <summary>Connections the server accepts from non-superusers (reserved_connections exists from PostgreSQL 16).</summary>
    public const string AvailableConnectionsSql = """
        SELECT current_setting('max_connections')::int
             - current_setting('superuser_reserved_connections')::int
             - COALESCE(current_setting('reserved_connections', true)::int, 0)
        """;

    private const string DefaultPort = "5432";

    /// <summary>
    /// Most pooled connections all pods open to the write server (<paramref name="write"/> true) or the read server:
    /// MaxReplicas times every pool on that server. Identical read and write settings share one pool.
    /// </summary>
    public static int Required(DbConnectionModel settings, bool write)
    {
        ArgumentNullException.ThrowIfNull(settings);
        (var target, var other) = write ? (settings.Write, settings.Read) : (settings.Read, settings.Write);

        int perPod = MaxPool(target);
        if (SameServer(target, other) && !SamePool(target, other))
        {
            perPod += MaxPool(other);
        }

        return (settings.MaxReplicas ?? DefaultMaxReplicas) * perPod;
    }

    /// <summary>True when both settings reach the same server (host and port), so one check covers both.</summary>
    public static bool SameServer(ConnectionModel first, ConnectionModel second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return string.Equals(first.Host.Trim(), second.Host.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Port(first), Port(second), StringComparison.Ordinal);
    }

    /// <summary>The connections the server accepts from non-superusers, read on an open connection.</summary>
    public static async Task<int> GetAvailableAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = AvailableConnectionsSql;
            object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
    }

    private static int MaxPool(ConnectionModel connection)
    {
        return connection.MaxPoolSize ?? DatabaseConnectionStrings.DefaultMaxPoolSize;
    }

    /// <summary>The factory shares one data source (one pool) only between identical connection strings.</summary>
    private static bool SamePool(ConnectionModel first, ConnectionModel second)
    {
        return string.Equals(
            DatabaseConnectionStrings.Create(first, string.Empty),
            DatabaseConnectionStrings.Create(second, string.Empty),
            StringComparison.Ordinal);
    }

    private static string Port(ConnectionModel connection)
    {
        return string.IsNullOrWhiteSpace(connection.Port) ? DefaultPort : connection.Port.Trim();
    }
}
