using System.Data;
using FluentAssertions;
using GSS.Business.Helpers;
using GSS.Infrastructure;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using GSS.UnitTests.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Helpers;

/// <summary>
/// Unit tests for DapperTransactionManager, including the async transaction lifecycle.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DapperTransactionManagerTests
{
    private readonly FakeDbConnection _connection = new();
    private readonly Mock<IDapperConnectionFactory> _factory = new();

    public DapperTransactionManagerTests()
    {
        _factory.Setup(f => f.CreateWriteConnection()).Returns(_connection);
    }

    [Fact]
    public async Task BeginTransactionAsync_NoActiveTransaction_OpensConnectionAndStartsTransaction()
    {
        // Arrange
        await using DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);

        // Act
        var transaction = await manager.BeginTransactionAsync();

        // Assert
        transaction.Should().NotBeNull();
        transaction.IsolationLevel.Should().Be(IsolationLevel.ReadCommitted);
        manager.GetCurrentTransaction().Should().BeSameAs(transaction);
        _connection.OpenCount.Should().Be(1);
    }

    [Fact]
    public async Task BeginTransactionAsync_CustomIsolationLevel_UsesIt()
    {
        // Arrange
        await using DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);

        // Act
        var transaction = await manager.BeginTransactionAsync(IsolationLevel.Serializable);

        // Assert
        transaction.IsolationLevel.Should().Be(IsolationLevel.Serializable);
    }

    /// <summary>A transaction's connection serves one call at a time: a second call while one runs fails, naming why.</summary>
    [Fact]
    public async Task EnterCommand_WhileACallRuns_ThrowsUntilItEnds()
    {
        await using DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);

        var first = manager.EnterCommand();

        manager.Invoking(m => m.EnterCommand()).Should().Throw<InvalidOperationException>().WithMessage("*one at a time*Task.WhenAll*");
        first.Dispose();
        using (manager.EnterCommand())
        {
            // the connection is free again
        }
    }

    [Fact]
    public async Task CommitAsync_WhileACallRuns_Throws()
    {
        await using DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);
        using var call = manager.EnterCommand();

        Func<Task> act = () => manager.CommitAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*still running*");
    }

    [Fact]
    public async Task BeginTransactionAsync_TransactionAlreadyActive_Throws()
    {
        // Arrange
        await using DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);
        await manager.BeginTransactionAsync();

        // Act
        Func<Task> act = async () => await manager.BeginTransactionAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already active*");
    }

    [Fact]
    public async Task BeginTransactionAsync_OpenFails_DisposesConnectionAndClearsState()
    {
        InvalidOperationException startupFailure = new("open failed");
        _connection.OpenException = startupFailure;
        await using DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);

        Func<Task> act = () => manager.BeginTransactionAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(startupFailure);
        _connection.DisposeCount.Should().Be(1);
        manager.GetCurrentTransaction().Should().BeNull();
    }

    [Fact]
    public async Task BeginTransactionAsync_BeginFails_DisposesOpenedConnectionAndClearsState()
    {
        InvalidOperationException startupFailure = new("begin failed");
        _connection.BeginTransactionException = startupFailure;
        await using DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);

        Func<Task> act = () => manager.BeginTransactionAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(startupFailure);
        _connection.OpenCount.Should().Be(1);
        _connection.DisposeCount.Should().Be(1);
        manager.GetCurrentTransaction().Should().BeNull();
    }

    [Fact]
    public async Task BeginTransactionAsync_AfterFailure_RetriesWithNewConnection()
    {
        FakeDbConnection failedConnection = new()
        {
            BeginTransactionException = new InvalidOperationException("begin failed")
        };
        FakeDbConnection retryConnection = new();
        _factory.SetupSequence(factory => factory.CreateWriteConnection())
            .Returns(failedConnection)
            .Returns(retryConnection);
        await using DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);

        Func<Task> firstAttempt = () => manager.BeginTransactionAsync();
        await firstAttempt.Should().ThrowAsync<InvalidOperationException>().WithMessage("begin failed");
        var transaction = await manager.BeginTransactionAsync();

        failedConnection.DisposeCount.Should().Be(1);
        manager.GetCurrentTransaction().Should().BeSameAs(transaction);
        await manager.RollbackAsync();
        retryConnection.DisposeCount.Should().Be(1);
    }

    [Theory]
    [InlineData("begin")]
    [InlineData("commit")]
    [InlineData("rollback")]
    public async Task AsyncOperation_AfterDispose_ThrowsObjectDisposedException(string operation)
    {
        DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);
        await manager.DisposeAsync();
        Func<Task> act = operation switch
        {
            "begin" => () => manager.BeginTransactionAsync(),
            "commit" => () => manager.CommitAsync(),
            _ => () => manager.RollbackAsync()
        };

        await act.Should().ThrowAsync<ObjectDisposedException>();
        _factory.Verify(factory => factory.CreateWriteConnection(), Times.Never);
    }

    [Fact]
    public async Task GetCurrentTransaction_AfterDispose_ThrowsObjectDisposedException()
    {
        DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);
        await manager.DisposeAsync();

        Action act = () => manager.GetCurrentTransaction();

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task CommitAsync_ActiveTransaction_CommitsAndReleasesConnection()
    {
        // Arrange
        await using DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);
        FakeDbTransaction transaction = (FakeDbTransaction)await manager.BeginTransactionAsync();

        // Act
        await manager.CommitAsync();

        // Assert
        transaction.CommitCount.Should().Be(1);
        transaction.DisposeCount.Should().Be(1);
        manager.GetCurrentTransaction().Should().BeNull();
        _connection.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task CommitAsync_NoActiveTransaction_Throws()
    {
        // Arrange
        await using DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);

        // Act
        Func<Task> act = async () => await manager.CommitAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No active transaction to commit.");
    }

    [Fact]
    public async Task RollbackAsync_ActiveTransaction_RollsBackAndReleasesConnection()
    {
        // Arrange
        await using DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);
        FakeDbTransaction transaction = (FakeDbTransaction)await manager.BeginTransactionAsync();

        // Act
        await manager.RollbackAsync();

        // Assert
        transaction.RollbackCount.Should().Be(1);
        manager.GetCurrentTransaction().Should().BeNull();
        _connection.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task RollbackAsync_NoActiveTransaction_IsNoOp()
    {
        // Arrange
        await using DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);

        // Act
        Func<Task> act = async () => await manager.RollbackAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CommitAsync_TransactionThrows_ReleasesConnectionAndRethrows()
    {
        // Arrange
        await using DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);
        FakeDbTransaction transaction = (FakeDbTransaction)await manager.BeginTransactionAsync();
        transaction.CommitException = new InvalidOperationException("commit failed");

        // Act
        Func<Task> act = async () => await manager.CommitAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("commit failed");
        manager.GetCurrentTransaction().Should().BeNull();
        _connection.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// A failed commit is logged once, where it is handled (ApiExceptionHandler): the manager, the Unit of Work and the
    /// transaction helper add no Warning or Error (it used to be Error, Warning "no active transaction", then Error).
    /// </summary>
    [Fact]
    public async Task CommitFails_InsideExecuteInTransaction_NothingLogsAWarningOrError()
    {
        LevelRecorder<DapperTransactionManager> managerLog = new();
        LevelRecorder<UnitOfWork> unitOfWorkLog = new();
        LevelRecorder<DapperTransactionManagerTests> helperLog = new();
        await using DapperTransactionManager manager = new(_factory.Object, managerLog);
        await using UnitOfWork unitOfWork = new(manager, unitOfWorkLog);

        Func<Task> act = () => unitOfWork.ExecuteInTransactionAsync(
            _ =>
            {
                ((FakeDbTransaction)manager.GetCurrentTransaction()!).CommitException = new InvalidOperationException("commit failed");
                return Task.CompletedTask;
            },
            helperLog);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("commit failed");
        managerLog.Levels.Concat(unitOfWorkLog.Levels).Concat(helperLog.Levels).Should().NotContain(level => level >= LogLevel.Warning);
    }

    private sealed class LevelRecorder<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
        }
    }

    [Fact]
    public async Task DisposeAsync_OpenTransaction_RollsItBack()
    {
        // Arrange
        DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);
        FakeDbTransaction transaction = (FakeDbTransaction)await manager.BeginTransactionAsync();

        // Act
        await manager.DisposeAsync();

        // Assert
        transaction.RollbackCount.Should().Be(1);
        _connection.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_RollbackThrows_DoesNotPropagate()
    {
        // Arrange
        DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);
        FakeDbTransaction transaction = (FakeDbTransaction)await manager.BeginTransactionAsync();
        transaction.RollbackException = new InvalidOperationException("boom");

        // Act
        Func<Task> act = async () => await manager.DisposeAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_IsIdempotent()
    {
        // Arrange
        DapperTransactionManager manager = new(_factory.Object, NullLogger<DapperTransactionManager>.Instance);
        await manager.BeginTransactionAsync();

        // Act
        await manager.DisposeAsync();
        await manager.DisposeAsync();

        // Assert
        _connection.DisposeCount.Should().Be(1);
    }
}
