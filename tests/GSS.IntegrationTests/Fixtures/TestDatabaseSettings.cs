using GSS.Infrastructure.Helpers;
using GSS.Util.Models;
using Microsoft.Extensions.Configuration;

namespace GSS.IntegrationTests.Fixtures;

/// <summary>
/// Single source of the integration-test database: <c>appsettings.Testing.json</c> in the test output, overridden by
/// <c>INTEGRATION_TEST_DB_*</c>. Used by the app under test, the reset and the repository fixture alike.
/// </summary>
public static class TestDatabaseSettings
{
    private static readonly Lazy<ConnectionModel> Lazy = new(Resolve);

    /// <summary>The resolved test connection (Read and Write use the same database).</summary>
    public static ConnectionModel Connection => Lazy.Value;

    /// <summary>Test database name.</summary>
    public static string DatabaseName => Connection.DatabaseName;

    /// <summary>Npgsql connection string for the test database.</summary>
    public static string ConnectionString(string applicationName = "gss-integration-test")
    {
        return DatabaseConnectionStrings.Create(Connection, applicationName);
    }

    /// <summary>Configuration overrides pointing both Read and Write at the test database.</summary>
    public static IReadOnlyDictionary<string, string?> ToConfigurationOverrides()
    {
        Dictionary<string, string?> overrides = [];
        foreach (string side in new[] { "Read", "Write" })
        {
            string prefix = $"DbConnectionSettings:{side}:";
            overrides[prefix + nameof(ConnectionModel.Host)] = Connection.Host;
            overrides[prefix + nameof(ConnectionModel.Port)] = Connection.Port;
            overrides[prefix + nameof(ConnectionModel.DatabaseName)] = Connection.DatabaseName;
            overrides[prefix + nameof(ConnectionModel.UserId)] = Connection.UserId;
            overrides[prefix + nameof(ConnectionModel.Password)] = Connection.Password;
            overrides[prefix + nameof(ConnectionModel.SslMode)] = Connection.SslMode;
        }

        return overrides;
    }

    private static ConnectionModel Resolve()
    {
        // appsettings.json is deliberately NOT loaded: it is the developer database.
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Testing.json", optional: true)
            .Build();

        var connection = config.GetSection("DbConnectionSettings:Write").Get<ConnectionModel>() ?? new ConnectionModel();
        connection.Host = Environment.GetEnvironmentVariable("INTEGRATION_TEST_DB_HOST") ?? Fallback(connection.Host, "localhost");
        connection.Port = Environment.GetEnvironmentVariable("INTEGRATION_TEST_DB_PORT") ?? Fallback(connection.Port, "5432");
        connection.DatabaseName = Environment.GetEnvironmentVariable("INTEGRATION_TEST_DB_NAME") ?? Fallback(connection.DatabaseName, "gss_test");
        connection.UserId = Environment.GetEnvironmentVariable("INTEGRATION_TEST_DB_USER") ?? Fallback(connection.UserId, "postgres");
        connection.Password = Environment.GetEnvironmentVariable("INTEGRATION_TEST_DB_PASSWORD") ?? Fallback(connection.Password, "postgres");
        connection.SslMode = Fallback(connection.SslMode, "Disable");

        // Tests TRUNCATE every table: refuse anything that does not look like a test database.
        if (!connection.DatabaseName.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Integration tests refuse to run against '{connection.DatabaseName}': the database name must contain 'test'.");
        }

        return connection;
    }

    private static string Fallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
