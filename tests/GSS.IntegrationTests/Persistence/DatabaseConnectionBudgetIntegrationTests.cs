using System.Collections.Concurrent;
using FluentAssertions;
using GSS.Api.HostedServices;
using GSS.Infrastructure.Helpers;
using GSS.IntegrationTests.Fixtures;
using GSS.Util.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GSS.IntegrationTests.Persistence;

/// <summary>Startup warns, without failing, when every pod's pools exceed the server's connection limit.</summary>
[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class DatabaseConnectionBudgetIntegrationTests
{
    [Fact]
    public async Task GetAvailableAsync_ReadsTheServersLimit()
    {
        await using var connection = new Npgsql.NpgsqlConnection(TestDatabaseSettings.ConnectionString());
        await connection.OpenAsync();

        int available = await DatabaseConnectionBudget.GetAvailableAsync(connection);

        available.Should().BePositive();
    }

    [Fact]
    public async Task StartAsync_PoolsOverTheServerLimit_WarnsAndStarts()
    {
        var warnings = await StartAsync(new DbConnectionModel { MaxReplicas = 10000 });

        warnings.Should().ContainSingle().Which.Should().Contain("too many clients").And.Contain("MaxReplicas");
    }

    [Fact]
    public async Task StartAsync_BehindConnectionPooler_SkipsTheCheck()
    {
        var warnings = await StartAsync(new DbConnectionModel { MaxReplicas = 10000, BehindConnectionPooler = true });

        warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_OnePodWithTheDefaultPool_DoesNotWarn()
    {
        var warnings = await StartAsync(new DbConnectionModel());

        warnings.Should().BeEmpty();
    }

    private static async Task<IReadOnlyCollection<string>> StartAsync(DbConnectionModel settings)
    {
        settings.Read = TestDatabaseSettings.Connection;
        settings.Write = TestDatabaseSettings.Connection;
        await using DapperConnectionFactory factory = new(
            Options.Create(settings), Options.Create(new AppSettingModel { ApplicationName = "budget-test" }), NullLoggerFactory.Instance);
        WarningLogger logger = new();

        await new DatabaseConnectionValidationHostedService(factory, Options.Create(settings), logger).StartAsync(CancellationToken.None);

        return logger.Warnings;
    }

    private sealed class WarningLogger : ILogger<DatabaseConnectionValidationHostedService>
    {
        public ConcurrentQueue<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Enqueue(formatter(state, exception));
            }
        }
    }
}
