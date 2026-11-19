using FluentAssertions;
using GSS.Infrastructure.Helpers;
using GSS.UnitTests.Common;
using GSS.Util.Models;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Helpers;

/// <summary>
/// Unit tests for DatabaseConnectionStrings.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DatabaseConnectionStringsTests
{
    #region Create Tests

    [Fact]
    public void Create_NullModel_ThrowsArgumentNullException()
    {
        // Act & Assert
        Action act = () => DatabaseConnectionStrings.Create(null!, "TestApp");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_SetsUtcSessionTimezone()
    {
        string result = DatabaseConnectionStrings.Create(
            new ConnectionModel { Host = "localhost", DatabaseName = "db", UserId = "u", Password = "p" }, "TestApp");

        new Npgsql.NpgsqlConnectionStringBuilder(result).Timezone.Should().Be("UTC");
    }

    [Fact]
    public void Create_ValidModel_ReturnsConnectionString()
    {
        // Arrange
        ConnectionModel model = new()
        {
            Host = "localhost",
            DatabaseName = "testdb",
            UserId = "user",
            Password = "pass",
            Port = "5432"
        };

        // Act
        string result = DatabaseConnectionStrings.Create(model, "TestApp");

        // Assert
        result.Should().Contain("Host=localhost");
        result.Should().Contain("Database=testdb");
        result.Should().Contain("Username=user");
        result.Should().Contain("Password=pass");
        result.Should().Contain("Port=5432");
        result.Should().Contain("Application Name=TestApp");
    }

    [Fact]
    public void Create_EmptyPort_OmitsPortFromConnectionString()
    {
        // Arrange
        ConnectionModel model = new()
        {
            Host = "localhost",
            DatabaseName = "testdb",
            UserId = "user",
            Password = "pass",
            Port = ""
        };

        // Act
        string result = DatabaseConnectionStrings.Create(model, "TestApp");

        // Assert
        result.Should().Contain("Host=localhost");
        result.Should().NotContain("Port=");
    }

    [Fact]
    public void Create_InvalidPort_OmitsPortFromConnectionString()
    {
        // Arrange
        ConnectionModel model = new()
        {
            Host = "localhost",
            DatabaseName = "testdb",
            UserId = "user",
            Password = "pass",
            Port = "not-a-number"
        };

        // Act
        string result = DatabaseConnectionStrings.Create(model, "TestApp");

        // Assert
        result.Should().Contain("Host=localhost");
        result.Should().NotContain("Port=not-a-number");
    }

    [Fact]
    public void Create_ValidPort_IncludesPortInConnectionString()
    {
        // Arrange
        ConnectionModel model = new()
        {
            Host = "localhost",
            DatabaseName = "testdb",
            UserId = "user",
            Password = "pass",
            Port = "5433"
        };

        // Act
        string result = DatabaseConnectionStrings.Create(model, "TestApp");

        // Assert
        result.Should().Contain("Port=5433");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrEmptySslMode_UsesPrefer(string? sslMode)
    {
        // Arrange
        ConnectionModel model = new()
        {
            Host = "localhost",
            DatabaseName = "testdb",
            UserId = "user",
            Password = "pass",
            SslMode = sslMode
        };

        // Act
        string result = DatabaseConnectionStrings.Create(model, "TestApp");

        // Assert - Prefer is the default for null/empty (Npgsql uses "SSL Mode")
        result.Should().Contain("SSL Mode=Prefer");
    }

    [Theory]
    [InlineData("DISABLE")]
    [InlineData("disable")]
    [InlineData("Allow")]
    [InlineData("ALLOW")]
    [InlineData("PREFER")]
    [InlineData("REQUIRE")]
    [InlineData("VERIFYCA")]
    [InlineData("VERIFYFULL")]
    public void Create_ValidSslModes_ParsesCorrectly(string sslMode)
    {
        // Arrange
        ConnectionModel model = new()
        {
            Host = "localhost",
            DatabaseName = "testdb",
            UserId = "user",
            Password = "pass",
            SslMode = sslMode
        };

        // Act
        string result = DatabaseConnectionStrings.Create(model, "TestApp");

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("SSL Mode");
    }

    [Theory]
    [InlineData("INVALID_MODE")]
    [InlineData("VerifyFul")]
    public void Create_UnknownSslMode_Throws(string sslMode)
    {
        // Arrange
        ConnectionModel model = new()
        {
            Host = "localhost",
            DatabaseName = "testdb",
            UserId = "user",
            Password = "pass",
            SslMode = sslMode
        };

        // Act & Assert - never guess a weaker mode for a typo
        Action act = () => DatabaseConnectionStrings.Create(model, "TestApp");
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("sslMode");
    }

    /// <summary>
    /// The MinPoolSize connections never idle out, so without keepalive a load balancer or NAT drops them silently and
    /// the next request on one fails.
    /// </summary>
    [Theory]
    [InlineData(null, DatabaseConnectionStrings.DefaultKeepAlive)]
    [InlineData(10, 10)]
    [InlineData(0, 0)]
    public void Create_KeepAlive_DefaultsToThirtySecondsAndCanBeChanged(int? configured, int expected)
    {
        string result = DatabaseConnectionStrings.Create(
            new ConnectionModel { Host = "localhost", DatabaseName = "db", UserId = "u", Password = "p", KeepAlive = configured }, "TestApp");

        new Npgsql.NpgsqlConnectionStringBuilder(result).KeepAlive.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(250, 250)]
    [InlineData(0, 0)]
    public void Create_MaxAutoPrepare_UsesConfiguredValueOrHundred(int? configured, int expected)
    {
        string result = DatabaseConnectionStrings.Create(
            new ConnectionModel { Host = "localhost", DatabaseName = "db", UserId = "u", Password = "p", MaxAutoPrepare = configured }, "TestApp");

        new Npgsql.NpgsqlConnectionStringBuilder(result).MaxAutoPrepare.Should().Be(expected);
    }

    [Fact]
    public void Create_CustomPoolSettings_UsesProvidedValues()
    {
        // Arrange
        ConnectionModel model = new()
        {
            Host = "localhost",
            DatabaseName = "testdb",
            UserId = "user",
            Password = "pass",
            MaxPoolSize = 50,
            MinPoolSize = 2,
            CommandTimeout = 60,
            Timeout = 30
        };

        // Act
        string result = DatabaseConnectionStrings.Create(model, "TestApp");

        // Assert
        result.Should().Contain("Maximum Pool Size=50");
        result.Should().Contain("Minimum Pool Size=2");
        result.Should().Contain("Command Timeout=60");
        result.Should().Contain("Timeout=30");
    }

    #endregion
}
