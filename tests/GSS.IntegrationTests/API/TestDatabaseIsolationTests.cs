using System.Data;
using FluentAssertions;
using GSS.Infrastructure.Interfaces;
using GSS.IntegrationTests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GSS.IntegrationTests.API;

/// <summary>Guards against the API under test writing to the developer database.</summary>
[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class TestDatabaseIsolationTests(WebAppFactory factory) : ApiIntegrationTestBase(factory)
{
    [Fact]
    public void AppUnderTest_ReadAndWrite_PointAtTestDatabase()
    {
        var configuration = Factory.Services.GetRequiredService<IConfiguration>();

        configuration["DbConnectionSettings:Write:DatabaseName"].Should().Be(TestDatabaseSettings.DatabaseName);
        configuration["DbConnectionSettings:Read:DatabaseName"].Should().Be(TestDatabaseSettings.DatabaseName);

        var connections = Factory.Services.GetRequiredService<IDapperConnectionFactory>();
        using IDbConnection write = connections.CreateWriteConnection();
        using IDbConnection read = connections.CreateReadConnection();
        write.Database.Should().Be(TestDatabaseSettings.DatabaseName);
        read.Database.Should().Be(TestDatabaseSettings.DatabaseName);
    }

    /// <summary>The reset discovers tables from the catalog and leaves DbUp's journal intact.</summary>
    [Fact]
    public async Task Reset_EmptiesEveryServiceTable_AndKeepsTheJournal()
    {
        await ResetDatabaseAsync();
        await ExecuteAsync("CREATE TABLE IF NOT EXISTS gss.reset_probe (id serial PRIMARY KEY, note text); INSERT INTO gss.reset_probe (note) VALUES ('x');");
        try
        {
            await ResetDatabaseAsync();

            (await ScalarAsync("SELECT COUNT(*) FROM gss.reset_probe")).Should().Be(0);
            (await ScalarAsync("SELECT COUNT(*) FROM pg_tables WHERE schemaname = 'gss' AND tablename = 'schemaversions'"))
                .Should().Be(1, "the journal table survives so applied scripts are not forgotten");
            await ExecuteAsync("INSERT INTO gss.reset_probe (note) VALUES ('y');");
            (await ScalarAsync("SELECT max(id) FROM gss.reset_probe")).Should().Be(1, "identities restart");
        }
        finally
        {
            await ExecuteAsync("DROP TABLE IF EXISTS gss.reset_probe;");
        }
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using NpgsqlConnection connection = new(TestDatabaseSettings.ConnectionString());
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(string sql)
    {
        await using NpgsqlConnection connection = new(TestDatabaseSettings.ConnectionString());
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
