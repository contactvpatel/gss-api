using FluentAssertions;
using GSS.Infrastructure.Helpers;
using GSS.UnitTests.Common;
using GSS.Util.Models;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Helpers;

/// <summary>
/// Every pod's pools count against one server's max_connections; identical read and write settings share one pool.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DatabaseConnectionBudgetTests
{
    [Fact]
    public void Defaults_OnePodOnePool_OfTheDefaultSize()
    {
        DbConnectionModel settings = new() { Read = Connection("db"), Write = Connection("db") };

        DatabaseConnectionBudget.Required(settings, write: true).Should().Be(DatabaseConnectionStrings.DefaultMaxPoolSize);
        DatabaseConnectionStrings.DefaultMaxPoolSize.Should().BeLessThanOrEqualTo(20, "a pod needs few busy connections, and pods share max_connections");
    }

    [Fact]
    public void IdenticalReadAndWrite_ShareOnePool_TimesReplicas()
    {
        DbConnectionModel settings = new() { Read = Connection("db", 30), Write = Connection("db", 30), MaxReplicas = 4 };

        DatabaseConnectionBudget.Required(settings, write: true).Should().Be(120);
    }

    [Fact]
    public void DifferentSettingsOnOneServer_CountBothPools()
    {
        var read = Connection("db", 10);
        read.UserId = "reader";
        DbConnectionModel settings = new() { Read = read, Write = Connection("DB ", 20), MaxReplicas = 3 };

        DatabaseConnectionBudget.SameServer(settings.Read, settings.Write).Should().BeTrue("host matching ignores case and spaces");
        DatabaseConnectionBudget.Required(settings, write: true).Should().Be(90);
        DatabaseConnectionBudget.Required(settings, write: false).Should().Be(90);
    }

    [Fact]
    public void ReadReplicaServer_CountsOnlyItsOwnPool()
    {
        DbConnectionModel settings = new() { Read = Connection("db-replica", 40), Write = Connection("db", 20), MaxReplicas = 2 };

        DatabaseConnectionBudget.SameServer(settings.Read, settings.Write).Should().BeFalse();
        DatabaseConnectionBudget.Required(settings, write: false).Should().Be(80);
        DatabaseConnectionBudget.Required(settings, write: true).Should().Be(40);
    }

    [Fact]
    public void SameHostOtherPort_IsAnotherServer()
    {
        var read = Connection("db");
        read.Port = "5433";

        DatabaseConnectionBudget.SameServer(read, Connection("db")).Should().BeFalse();
        DatabaseConnectionBudget.SameServer(Connection("db"), new ConnectionModel { Host = "db", Port = "", DatabaseName = "shop", UserId = "app" })
            .Should().BeTrue("a blank port is the default 5432");
    }

    private static ConnectionModel Connection(string host, int? maxPoolSize = null)
    {
        return new ConnectionModel { Host = host, Port = "5432", DatabaseName = "shop", UserId = "app", MaxPoolSize = maxPoolSize };
    }
}
