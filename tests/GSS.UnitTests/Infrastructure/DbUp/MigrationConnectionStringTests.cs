using FluentAssertions;
using GSS.Infrastructure.DbUp.Core;
using GSS.UnitTests.Common;
using Npgsql;
using Xunit;

namespace GSS.UnitTests.Infrastructure.DbUp;

/// <summary>Migration connections: lock_timeout, no pooling and their own application name; everything else kept.</summary>
[Trait("Category", TestCategories.Unit)]
public class MigrationConnectionStringTests
{
    private const string AppConnectionString =
        "Host=db;Port=5432;Database=gss;Username=app;Password=secret;Pooling=true;Minimum Pool Size=5;Maximum Pool Size=100;Application Name=gss-api;Timezone=UTC";

    [Fact]
    public void ForMigrations_DisablesPooling_SetsLockTimeoutAndNamesTheSessions()
    {
        NpgsqlConnectionStringBuilder builder = new(DatabaseMigrationInitFilter.ForMigrations(AppConnectionString, 5));

        builder.Pooling.Should().BeFalse("a pooled session would keep the advisory lock and idle for the pod's life");
        builder.MinPoolSize.Should().Be(0);
        builder.Options.Should().Be("-c lock_timeout=5s");
        builder.ApplicationName.Should().Be("gss-api-migrations");
    }

    [Fact]
    public void ForMigrations_KeepsTheTargetAndCredentials()
    {
        NpgsqlConnectionStringBuilder builder = new(DatabaseMigrationInitFilter.ForMigrations(AppConnectionString, 5));

        builder.Host.Should().Be("db");
        builder.Database.Should().Be("gss");
        builder.Username.Should().Be("app");
        builder.Password.Should().Be("secret");
        builder.Timezone.Should().Be("UTC");
    }

    [Fact]
    public void ForMigrations_WithoutAnApplicationName_StillNamesTheSessions()
    {
        NpgsqlConnectionStringBuilder builder = new(DatabaseMigrationInitFilter.ForMigrations("Host=db;Database=gss", 0));

        builder.ApplicationName.Should().Be("migrations");
        builder.Options.Should().Be("-c lock_timeout=0s");
    }
}
