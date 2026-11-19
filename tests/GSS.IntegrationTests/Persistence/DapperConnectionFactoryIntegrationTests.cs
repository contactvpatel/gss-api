using System.Collections.Concurrent;
using System.Data.Common;
using FluentAssertions;
using GSS.Infrastructure.Helpers;
using GSS.IntegrationTests.Fixtures;
using GSS.Util.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GSS.IntegrationTests.Persistence;

/// <summary>The data sources log through the app's logger factory, so driver logs carry the app's scopes and sinks.</summary>
[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class DapperConnectionFactoryIntegrationTests
{
    [Fact]
    public async Task NpgsqlLogs_GoThroughTheAppsLoggerFactory()
    {
        CapturingLoggerProvider logs = new();
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        DbConnectionModel settings = new() { Read = TestDatabaseSettings.Connection, Write = TestDatabaseSettings.Connection };
        await using DapperConnectionFactory factory = new(
            Options.Create(settings), Options.Create(new AppSettingModel { ApplicationName = "factory-test" }), loggerFactory);

        await using (var connection = factory.CreateReadConnection())
        {
            await connection.OpenAsync();
        }

        logs.Categories.Should().Contain(c => c.StartsWith("Npgsql", StringComparison.Ordinal));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Categories { get; } = [];

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(categoryName, Categories);
        }

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentBag<string> categories) : ILogger
        {
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
                categories.Add(category);
            }
        }
    }
}
