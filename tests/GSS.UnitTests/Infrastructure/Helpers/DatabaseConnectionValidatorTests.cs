using System.Data;
using System.Data.Common;
using FluentAssertions;
using GSS.Infrastructure.Helpers;
using GSS.UnitTests.Common;
using Moq;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Helpers;

/// <summary>
/// Unit tests for DatabaseConnectionValidator.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DatabaseConnectionValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ClosedConnection_OpensItAndRunsValidationQuery()
    {
        // Arrange
        FakeDbConnection connection = new();

        // Act
        await DatabaseConnectionValidator.ValidateAsync(connection);

        // Assert
        connection.OpenCount.Should().Be(1);
        connection.ExecutedCommands.Should().ContainSingle().Which.Should().Be("SELECT 1");
    }

    [Fact]
    public async Task ValidateAsync_AlreadyOpenConnection_DoesNotReopenIt()
    {
        // Arrange
        FakeDbConnection connection = new();
        connection.Open();

        // Act
        await DatabaseConnectionValidator.ValidateAsync(connection);

        // Assert
        connection.OpenCount.Should().Be(1);
        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Fact]
    public async Task ValidateAsync_NonDbConnection_ThrowsInvalidOperationException()
    {
        // Arrange
        Mock<IDbConnection> connection = new();
        connection.Setup(c => c.State).Returns(ConnectionState.Closed);

        // Act
        Func<Task> act = async () => await DatabaseConnectionValidator.ValidateAsync(connection.Object);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not derive from System.Data.Common.DbConnection*");
        connection.Verify(c => c.Open(), Times.Never);
    }

    [Fact]
    public async Task ValidateAsync_NullConnection_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await DatabaseConnectionValidator.ValidateAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ValidateAsync_OpenFails_PropagatesException()
    {
        // Arrange
        FakeDbConnection connection = new() { OpenException = new TimeoutException("unreachable") };

        // Act
        Func<Task> act = async () => await DatabaseConnectionValidator.ValidateAsync(connection);

        // Assert
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task ValidateAsync_QueryFails_PropagatesException()
    {
        // Arrange
        FakeDbConnection connection = new() { ExecuteException = new InvalidOperationException("query failed") };

        // Act
        Func<Task> act = async () => await DatabaseConnectionValidator.ValidateAsync(connection);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("query failed");
    }

    [Fact]
    public async Task ValidateAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        FakeDbConnection connection = new();
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        // Act
        Func<Task> act = async () => await DatabaseConnectionValidator.ValidateAsync(connection, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ValidateAsync_DatabaseMissing_ThrowsWithDatabaseAndServer()
    {
        // Arrange
        FakeDbException missing = new("3D000");
        FakeDbConnection connection = new() { OpenException = missing };

        // Act
        Func<Task> act = async () => await DatabaseConnectionValidator.ValidateAsync(connection);

        // Assert
        (await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database 'fake' does not exist on server 'fake'*never creates databases*"))
            .Which.InnerException.Should().BeSameAs(missing);
    }

    [Fact]
    public void Open_DatabaseMissing_ThrowsWithDatabaseAndServer()
    {
        // Arrange
        FakeDbConnection connection = new() { OpenException = new FakeDbException("3D000") };

        // Act
        Action act = () => DatabaseConnectionValidator.Open(connection);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("Database 'fake' does not exist*");
    }

    [Fact]
    public async Task ValidateAsync_OtherDatabaseError_PropagatesUnchanged()
    {
        // Arrange
        FakeDbConnection connection = new() { OpenException = new FakeDbException("28P01") };

        // Act
        Func<Task> act = async () => await DatabaseConnectionValidator.ValidateAsync(connection);

        // Assert
        await act.Should().ThrowAsync<FakeDbException>();
    }

    private sealed class FakeDbException(string sqlState) : DbException("fake database error")
    {
        public override string? SqlState => sqlState;
    }
}
