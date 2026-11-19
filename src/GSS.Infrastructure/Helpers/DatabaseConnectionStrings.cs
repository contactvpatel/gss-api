// using Microsoft.Data.SqlClient; // SQL Server: see .docs/guides/database-migration-postgresql-to-sqlserver.md
using GSS.Util.Models;
using Npgsql;

namespace GSS.Infrastructure.Helpers;

/// <summary>
/// Builds provider connection strings from <see cref="ConnectionModel"/> settings. Lives in Infrastructure so only
/// this layer references the database provider.
/// </summary>
public static class DatabaseConnectionStrings
{
    /// <summary>
    /// Pool size used when <see cref="ConnectionModel.MaxPoolSize"/> is unset. Per pod and connection string: busy
    /// connections are about requests per second times query seconds (1000/s at 5 ms is 5), and every pod's pool counts
    /// against the server's max_connections (PostgreSQL default 100).
    /// </summary>
    public const int DefaultMaxPoolSize = 20;

    /// <summary>Pool size used when <see cref="ConnectionModel.MinPoolSize"/> is unset.</summary>
    public const int DefaultMinPoolSize = 5;

    /// <summary>Keepalive seconds used when <see cref="ConnectionModel.KeepAlive"/> is unset.</summary>
    public const int DefaultKeepAlive = 30;

    /// <summary>
    /// Auto-prepared statements kept per connection when <see cref="ConnectionModel.MaxAutoPrepare"/> is unset. Each
    /// concrete repository can contribute up to 16 base statements plus its own queries; 100 leaves room for the hot
    /// set as features are added. Each prepared statement holds a plan in its server backend.
    /// </summary>
    public const int DefaultMaxAutoPrepare = 100;

    /// <summary>Uses on a connection before a statement is prepared, when <see cref="ConnectionModel.AutoPrepareMinUsages"/> is unset.</summary>
    public const int DefaultAutoPrepareMinUsages = 2;

    /// <summary>Builds the connection string (PostgreSQL).</summary>
    public static string Create(ConnectionModel databaseConnectionModel, string applicationName)
    {
        ArgumentNullException.ThrowIfNull(databaseConnectionModel);

        return CreatePostgreSQLConnectionString(databaseConnectionModel, applicationName);

        // SQL Server: return this instead and uncomment CreateSQLServerConnectionString below.
        // return CreateSQLServerConnectionString(databaseConnectionModel, applicationName);
    }

    /// <summary>PostgreSQL connection string; unset options get the defaults below.</summary>
    private static string CreatePostgreSQLConnectionString(ConnectionModel databaseConnectionModel, string applicationName)
    {
        NpgsqlConnectionStringBuilder builder = new()
        {
            Host = databaseConnectionModel.Host,
            Database = databaseConnectionModel.DatabaseName,
            Username = databaseConnectionModel.UserId,
            Password = databaseConnectionModel.Password,

            // UTC session so timestamptz output and casts never use the server's zone.
            Timezone = "UTC",

            // Use VerifyFull in production to also validate the certificate.
            SslMode = ParseSslMode(databaseConnectionModel.SslMode),

            MaxPoolSize = databaseConnectionModel.MaxPoolSize ?? DefaultMaxPoolSize,
            MinPoolSize = databaseConnectionModel.MinPoolSize ?? DefaultMinPoolSize,

            // Seconds before idle pooled connections close (never below MinPoolSize).
            ConnectionIdleLifetime = databaseConnectionModel.ConnectionIdleLifetime ?? 300,

            // Seconds of inactivity before a keepalive query: keeps the MinPoolSize connections alive through load
            // balancer/NAT idle timeouts and drops a dead one (failover) before a request uses it.
            KeepAlive = databaseConnectionModel.KeepAlive ?? DefaultKeepAlive,

            // Seconds; raise CommandTimeout for long queries.
            CommandTimeout = databaseConnectionModel.CommandTimeout ?? 30,
            Timeout = databaseConnectionModel.Timeout ?? 15,

            // 0 = unlimited; set to rebalance connections behind a load balancer.
            ConnectionLifetime = databaseConnectionModel.ConnectionLifetime ?? 0,

            // Shown in pg_stat_activity.
            ApplicationName = applicationName,

            // Auto-prepare statements used at least AutoPrepareMinUsages times. Behind PgBouncer in transaction mode
            // this needs PgBouncer 1.21+ with max_prepared_statements set; otherwise set MaxAutoPrepare to 0.
            MaxAutoPrepare = databaseConnectionModel.MaxAutoPrepare ?? DefaultMaxAutoPrepare,
            AutoPrepareMinUsages = databaseConnectionModel.AutoPrepareMinUsages ?? DefaultAutoPrepareMinUsages
        };

        if (!string.IsNullOrEmpty(databaseConnectionModel.Port) && int.TryParse(databaseConnectionModel.Port, out int port))
        {
            builder.Port = port;
        }

        return builder.ConnectionString;
    }

    // SQL Server connection string; uncomment to enable.
    /*
    private static string CreateSQLServerConnectionString(ConnectionModel databaseConnectionModel, string applicationName)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = $"{databaseConnectionModel.Host}{(string.IsNullOrEmpty(databaseConnectionModel.Port) ? "" : $",{databaseConnectionModel.Port}")}",
            InitialCatalog = databaseConnectionModel.DatabaseName,
            UserID = databaseConnectionModel.UserId,
            Password = databaseConnectionModel.Password,

            Encrypt = databaseConnectionModel.SslMode?.ToUpperInvariant() switch
            {
                "REQUIRE" or "VERIFYCA" or "VERIFYFULL" => true,
                _ => false
            },

            MaxPoolSize = databaseConnectionModel.MaxPoolSize ?? 100,
            MinPoolSize = databaseConnectionModel.MinPoolSize ?? 5,

            CommandTimeout = databaseConnectionModel.CommandTimeout ?? 30,
            ConnectTimeout = databaseConnectionModel.Timeout ?? 15,

            ApplicationName = applicationName,

            TrustServerCertificate = databaseConnectionModel.SslMode?.ToUpperInvariant() switch
            {
                "ALLOW" or "DISABLE" => true,
                _ => false
            },
            MultipleActiveResultSets = true,
            PersistSecurityInfo = false
        };

        return builder.ConnectionString;
    }
    */

    /// <summary>
    /// Parses the SSL mode: blank gives Prefer. An unknown value throws instead of guessing a mode;
    /// <see cref="DbConnectionModelValidator"/> rejects it at startup first.
    /// </summary>
    private static SslMode ParseSslMode(string? sslMode)
    {
        if (string.IsNullOrWhiteSpace(sslMode))
        {
            return SslMode.Prefer;
        }

        return sslMode.ToUpperInvariant() switch
        {
            "DISABLE" => SslMode.Disable,
            "ALLOW" => SslMode.Allow,
            "PREFER" => SslMode.Prefer,
            "REQUIRE" => SslMode.Require,
            "VERIFYCA" => SslMode.VerifyCA,
            "VERIFYFULL" => SslMode.VerifyFull,
            _ => throw new ArgumentOutOfRangeException(
                nameof(sslMode), sslMode, "SslMode must be Disable, Allow, Prefer, Require, VerifyCA or VerifyFull.")
        };
    }
}
