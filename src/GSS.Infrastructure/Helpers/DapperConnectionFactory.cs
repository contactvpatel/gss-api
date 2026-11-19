using System.Data.Common;
using GSS.Infrastructure.Interfaces;
using GSS.Util.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
// using Microsoft.Data.SqlClient; // SQL Server
using Npgsql;

namespace GSS.Infrastructure.Helpers;

/// <summary>
/// Creates Npgsql read and write connections from one <see cref="NpgsqlDataSource"/> per connection string, which owns
/// that string's pool and type cache. Identical read and write settings share one data source (one pool).
/// Connections never expose the password in <see cref="DbConnection.ConnectionString"/>.
/// </summary>
public sealed class DapperConnectionFactory : IDapperConnectionFactory, IDisposable, IAsyncDisposable
{
    private readonly NpgsqlDataSource _readDataSource;
    private readonly NpgsqlDataSource _writeDataSource;
    private readonly ILogger<DapperConnectionFactory> _logger;

    /// <summary>
    /// Builds the data sources from the validated <c>DbConnectionSettings</c>. They log through
    /// <paramref name="loggerFactory"/> (parameter values are never logged) and trace on the "Npgsql" ActivitySource.
    /// </summary>
    public DapperConnectionFactory(IOptions<DbConnectionModel> settings, IOptions<AppSettingModel> appSettings, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(appSettings);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger<DapperConnectionFactory>();
        string read = DatabaseConnectionStrings.Create(settings.Value.Read, appSettings.Value.ApplicationName);
        string write = DatabaseConnectionStrings.Create(settings.Value.Write, appSettings.Value.ApplicationName);

        _writeDataSource = Build(write, loggerFactory);
        _readDataSource = string.Equals(read, write, StringComparison.Ordinal) ? _writeDataSource : Build(read, loggerFactory);
        // If SQL Server: replace the data sources with connection strings and new SqlConnection(...).
    }

    private static NpgsqlDataSource Build(string connectionString, ILoggerFactory loggerFactory)
    {
        return new NpgsqlDataSourceBuilder(connectionString).UseLoggerFactory(loggerFactory).Build();
    }

    /// <inheritdoc />
    public DbConnection CreateReadConnection()
    {
        _logger.CreatedReadConnection();
        return _readDataSource.CreateConnection();
    }

    /// <inheritdoc />
    public DbConnection CreateWriteConnection()
    {
        _logger.CreatedWriteConnection();
        return _writeDataSource.CreateConnection();
    }

    /// <summary>Disposes the data sources and their pools.</summary>
    public void Dispose()
    {
        _writeDataSource.Dispose();
        if (!ReferenceEquals(_readDataSource, _writeDataSource))
        {
            _readDataSource.Dispose();
        }
    }

    /// <summary>Disposes the data sources and their pools.</summary>
    public async ValueTask DisposeAsync()
    {
        await _writeDataSource.DisposeAsync().ConfigureAwait(false);
        if (!ReferenceEquals(_readDataSource, _writeDataSource))
        {
            await _readDataSource.DisposeAsync().ConfigureAwait(false);
        }
    }
}
