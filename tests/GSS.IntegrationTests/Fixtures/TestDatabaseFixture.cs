using System.Data.Common;
using DbUp;
using DbUp.Engine.Output;
using GSS.Core.Interfaces.Base;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using GSS.Util.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace GSS.IntegrationTests.Fixtures;

/// <summary>
/// Repository-test fixture on the real test PostgreSQL database (<see cref="TestDatabaseSettings"/>).
/// </summary>
public sealed class TestDatabaseFixture : IAsyncLifetime
{
    private static bool s_databaseEnsured;
    private readonly string _connectionString;
    private readonly TestConnectionFactory _connectionFactory;
    private readonly ServiceProvider _services;

    public TestDatabaseFixture()
    {
        _connectionString = BuildConnectionString();
        _connectionFactory = new TestConnectionFactory(_connectionString);
        _services = new ServiceCollection()
            .AddSingleton<IDapperConnectionFactory>(_connectionFactory)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IUserContext>(new TestUserContext(TestUserContext.DefaultUserId))
            .BuildServiceProvider();
    }

    public IDapperConnectionFactory ConnectionFactory => _connectionFactory;

    /// <summary>
    /// A repository built as DI builds it: this database, user <see cref="TestUserContext.DefaultUserId"/>, and
    /// <paramref name="transactionManager"/> or one with no transaction (writes commit on their own).
    /// </summary>
    public TRepository CreateRepository<TRepository>(IDapperTransactionManager? transactionManager = null)
        where TRepository : class
    {
        return CreateRepository<TRepository>(_connectionFactory, transactionManager);
    }

    /// <summary><see cref="CreateRepository{TRepository}(IDapperTransactionManager?)"/> on another connection factory.</summary>
    public TRepository CreateRepository<TRepository>(IDapperConnectionFactory connectionFactory, IDapperTransactionManager? transactionManager = null)
        where TRepository : class
    {
        return ActivatorUtilities.CreateInstance<TRepository>(
            _services, connectionFactory, transactionManager ?? CreateTransactionManager(connectionFactory));
    }

    /// <summary>A Unit of Work transaction manager on this database (or <paramref name="connectionFactory"/>).</summary>
    public DapperTransactionManager CreateTransactionManager(IDapperConnectionFactory? connectionFactory = null)
    {
        return new DapperTransactionManager(connectionFactory ?? _connectionFactory, NullLogger<DapperTransactionManager>.Instance);
    }

    /// <summary>Truncates all gss tables; call at the start of each test. Retries on deadlock (40P01).</summary>
    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await TestDatabaseReset.ResetAsync(_connectionString, cancellationToken).ConfigureAwait(false);
    }

    public async Task InitializeAsync()
    {
        if (s_databaseEnsured)
        {
            return;
        }

        EnsureDatabase.For.PostgresqlDatabase(_connectionString, new NullUpgradeLog());
        s_databaseEnsured = true;
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    private sealed class NullUpgradeLog : IUpgradeLog
    {
        public void LogTrace(string format, params object[] args) { }
        public void LogDebug(string format, params object[] args) { }
        public void LogInformation(string format, params object[] args) { }
        public void LogWarning(string format, params object[] args) { }
        public void LogError(string format, params object[] args) { }
        public void LogError(Exception ex, string format, params object[] args) { }
    }

    private static string BuildConnectionString()
    {
        return TestDatabaseSettings.ConnectionString("gss-repo-test");
    }

    private sealed class TestConnectionFactory(string connectionString) : IDapperConnectionFactory
    {
        private readonly string _connectionString = connectionString;

        public DbConnection CreateReadConnection()
        {
            return new NpgsqlConnection(_connectionString);
        }

        public DbConnection CreateWriteConnection()
        {
            return new NpgsqlConnection(_connectionString);
        }
    }
}
