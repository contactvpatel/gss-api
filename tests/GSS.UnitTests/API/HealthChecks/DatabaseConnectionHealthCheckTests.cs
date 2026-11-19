using System.Data;
using FluentAssertions;
using GSS.Api.HealthChecks;
using GSS.UnitTests.Common;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace GSS.UnitTests.API.HealthChecks;

/// <summary>
/// Unit tests for DatabaseConnectionHealthCheck.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DatabaseConnectionHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ConnectionSucceeds_ReturnsHealthyAndDisposesConnection()
    {
        // Arrange
        FakeDbConnection connection = new();
        DatabaseConnectionHealthCheck healthCheck = new(() => connection);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        connection.OpenCount.Should().Be(1);
        connection.ExecutedCommands.Should().ContainSingle().Which.Should().Be("SELECT 1");
        connection.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task CheckHealthAsync_ConnectionFails_ThrowsForSanitizingPipeline()
    {
        // Arrange
        FakeDbConnection connection = new() { OpenException = new InvalidOperationException("Host=db;Password=secret") };
        DatabaseConnectionHealthCheck healthCheck = new(() => connection);

        // Act
        Func<Task> act = () => healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        connection.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task CheckHealthAsync_NonDbConnectionProvider_ThrowsInvalidOperationException()
    {
        // Arrange - an IDbConnection that is not an ADO.NET DbConnection cannot be validated asynchronously
        Mock<IDbConnection> mockConnection = new();
        mockConnection.Setup(c => c.State).Returns(ConnectionState.Closed);
        DatabaseConnectionHealthCheck healthCheck = new(() => mockConnection.Object);

        // Act
        Func<Task> act = () => healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not derive from System.Data.Common.DbConnection*");
        mockConnection.Verify(c => c.Open(), Times.Never);
        mockConnection.Verify(c => c.Dispose(), Times.Once);
    }
}
