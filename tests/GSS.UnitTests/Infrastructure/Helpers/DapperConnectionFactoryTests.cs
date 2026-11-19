using System.Data.Common;
using FluentAssertions;
using GSS.Infrastructure.Helpers;
using GSS.UnitTests.Common;
using GSS.Util.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Helpers;

/// <summary>
/// Unit tests for DapperConnectionFactory. Settings binding and validation: <see cref="DbConnectionOptionsTests"/>.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DapperConnectionFactoryTests
{
    [Fact]
    public void CreateConnections_UseTheReadAndWriteDatabases()
    {
        using var factory = Create(Settings("testdb_read", "testdb_write"));

        using var read = factory.CreateReadConnection();
        using var write = factory.CreateWriteConnection();

        read.Should().BeOfType<NpgsqlConnection>();
        read.ConnectionString.Should().Contain("testdb_read");
        write.ConnectionString.Should().Contain("testdb_write");
    }

    [Fact]
    public void CreateConnections_ReturnANewConnectionEachTime()
    {
        using var factory = Create(Settings("testdb_read", "testdb_write"));

        using var first = factory.CreateReadConnection();
        using var second = factory.CreateReadConnection();
        using var firstWrite = factory.CreateWriteConnection();
        using var secondWrite = factory.CreateWriteConnection();

        first.Should().NotBeSameAs(second);
        firstWrite.Should().NotBeSameAs(secondWrite);
    }

    [Fact]
    public void CreateReadConnection_UsesTheApplicationName()
    {
        using var factory = Create(Settings("testdb", "testdb"), applicationName: "CustomApp");

        using var connection = factory.CreateReadConnection();

        connection.ConnectionString.Should().Contain("Application Name=CustomApp");
    }

    [Fact]
    public void CreateConnections_ConnectionStringDoesNotExposePassword()
    {
        using var factory = Create(Settings("testdb_read", "testdb_write"));

        using var read = factory.CreateReadConnection();
        using var write = factory.CreateWriteConnection();

        read.ConnectionString.Should().NotContain("testpass", because: "connections from a data source never carry the password");
        write.ConnectionString.Should().NotContain("testpass");
    }

    [Fact]
    public void Dispose_ClosesTheDataSources()
    {
        var factory = Create(Settings("testdb_read", "testdb_write"));

        factory.Dispose();

        // A disposed data source refuses to open connections, before any network call.
        FluentActions.Invoking(() => factory.CreateReadConnection().Open()).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => factory.CreateWriteConnection().Open()).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task DisposeAsync_SameReadAndWriteSettings_DisposesSharedDataSourceOnce()
    {
        var factory = Create(Settings("testdb", "testdb"));

        Func<Task> dispose = async () => await factory.DisposeAsync();

        await dispose.Should().NotThrowAsync();
        await FluentActions.Invoking(() => factory.CreateReadConnection().OpenAsync()).Should().ThrowAsync<ObjectDisposedException>();
    }

    private static DapperConnectionFactory Create(DbConnectionModel settings, string applicationName = "test-app")
    {
        return new DapperConnectionFactory(
            Options.Create(settings), Options.Create(new AppSettingModel { ApplicationName = applicationName }), NullLoggerFactory.Instance);
    }

    private static DbConnectionModel Settings(string readDatabase, string writeDatabase)
    {
        return new DbConnectionModel { Read = Connection(readDatabase), Write = Connection(writeDatabase) };
    }

    private static ConnectionModel Connection(string database)
    {
        return new ConnectionModel { Host = "localhost", Port = "5432", DatabaseName = database, UserId = "testuser", Password = "testpass" };
    }
}
