using System.Data;
using FluentAssertions;
using GSS.Business.Helpers;
using GSS.Business.Models;
using GSS.Core.Interfaces.Base;
using GSS.UnitTests.Common;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GSS.UnitTests.Business.Helpers;

/// <summary>
/// Unit tests for UnitOfWorkExtensions.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class UnitOfWorkExtensionsTests
{
    private readonly Mock<ILogger> _logger = new();

    #region ExecuteInTransactionAsync Tests

    [Fact]
    public async Task ExecuteInTransactionAsync_OperationSucceeds_CommitsTransaction()
    {
        // Arrange
        Mock<IUnitOfWork> mockUnitOfWork = new();
        bool operationInvoked = false;
        CancellationToken? receivedToken = null;

        // Act
        await UnitOfWorkExtensions.ExecuteInTransactionAsync(
            mockUnitOfWork.Object,
            ct =>
            {
                operationInvoked = true;
                receivedToken = ct;
                return Task.CompletedTask;
            },
            _logger.Object,
            CancellationToken.None);

        // Assert
        operationInvoked.Should().BeTrue();
        receivedToken.Should().Be(CancellationToken.None);
        mockUnitOfWork.Verify(u => u.BeginTransactionAsync(IsolationLevel.ReadCommitted, It.IsAny<CancellationToken>()), Times.Once);
        mockUnitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockUnitOfWork.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_OperationThrows_RollsBackAndPropagatesException()
    {
        // Arrange
        Mock<IUnitOfWork> mockUnitOfWork = new();
        InvalidOperationException expectedException = new("Database error");

        // Act
        Func<Task> act = async () => await UnitOfWorkExtensions.ExecuteInTransactionAsync(
            mockUnitOfWork.Object,
            _ => throw expectedException,
            _logger.Object,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database error");
        mockUnitOfWork.Verify(u => u.BeginTransactionAsync(IsolationLevel.ReadCommitted, It.IsAny<CancellationToken>()), Times.Once);
        mockUnitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        mockUnitOfWork.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>A failed begin (e.g. a transaction is already open) must not roll back the caller's transaction.</summary>
    [Fact]
    public async Task ExecuteInTransactionAsync_BeginThrows_DoesNotRollBackCallersTransaction()
    {
        // Arrange
        Mock<IUnitOfWork> mockUnitOfWork = new();
        mockUnitOfWork
            .Setup(u => u.BeginTransactionAsync(It.IsAny<IsolationLevel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("A transaction is already active."));
        bool operationInvoked = false;

        // Act
        Func<Task> act = async () => await UnitOfWorkExtensions.ExecuteInTransactionAsync(
            mockUnitOfWork.Object,
            _ =>
            {
                operationInvoked = true;
                return Task.CompletedTask;
            },
            _logger.Object,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("A transaction is already active.");
        operationInvoked.Should().BeFalse();
        mockUnitOfWork.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        mockUnitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A failing rollback never replaces the original exception.</summary>
    [Fact]
    public async Task ExecuteInTransactionAsync_RollbackThrows_PreservesOriginalException()
    {
        // Arrange
        Mock<IUnitOfWork> mockUnitOfWork = new();
        mockUnitOfWork
            .Setup(u => u.RollbackAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("rollback exploded"));

        // Act
        Func<Task> act = async () => await UnitOfWorkExtensions.ExecuteInTransactionAsync(
            mockUnitOfWork.Object,
            _ => throw new InvalidOperationException("duplicate key value violates unique constraint"),
            _logger.Object,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("duplicate key value violates unique constraint");
        _logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.Is<Exception?>(e => e is TimeoutException),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once, "a failed rollback is logged, never swallowed");
    }

    /// <summary>The logger is required: without one a failed rollback would vanish.</summary>
    [Fact]
    public async Task ExecuteInTransactionAsync_NullLogger_ThrowsArgumentNullException()
    {
        Mock<IUnitOfWork> mockUnitOfWork = new();

        Func<Task> act = async () => await UnitOfWorkExtensions.ExecuteInTransactionAsync(
            mockUnitOfWork.Object,
            _ => Task.CompletedTask,
            null!,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
        mockUnitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<IsolationLevel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Rollback still runs when the request was cancelled.</summary>
    [Fact]
    public async Task ExecuteInTransactionAsync_OperationCancelled_RollsBackWithUncancelledToken()
    {
        // Arrange
        Mock<IUnitOfWork> mockUnitOfWork = new();
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        // Act
        Func<Task> act = async () => await UnitOfWorkExtensions.ExecuteInTransactionAsync(
            mockUnitOfWork.Object,
            ct => Task.FromCanceled(ct),
            _logger.Object,
            cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        mockUnitOfWork.Verify(u => u.RollbackAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WhenCalled_OperationReceivesCancellationToken()
    {
        // Arrange
        Mock<IUnitOfWork> mockUnitOfWork = new();
        using CancellationTokenSource cts = new();
        var token = cts.Token;
        CancellationToken? receivedToken = null;

        // Act
        await UnitOfWorkExtensions.ExecuteInTransactionAsync(
            mockUnitOfWork.Object,
            ct =>
            {
                receivedToken = ct;
                return Task.CompletedTask;
            },
            _logger.Object,
            token);

        // Assert
        receivedToken.Should().Be(token);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_NullUnitOfWork_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await UnitOfWorkExtensions.ExecuteInTransactionAsync(
            null!,
            _ => Task.CompletedTask,
            _logger.Object,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region ExecuteInTransactionAsync (Result) Tests

    [Fact]
    public async Task ExecuteInTransactionAsync_ResultSucceeds_CommitsAndReturnsIt()
    {
        Mock<IUnitOfWork> mockUnitOfWork = new();

        var result = await mockUnitOfWork.Object.ExecuteInTransactionAsync(
            _ => Task.FromResult(Result<int>.Success(7)), _logger.Object, CancellationToken.None);

        result.Value.Should().Be(7);
        mockUnitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockUnitOfWork.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    public static TheoryData<Result<int>> FailedResults() => new()
    {
        Result<int>.Failure("Rule broken"),
        Result<int>.Conflict("Already exists"),
        Result<int>.NotFound("Missing"),
    };

    /// <summary>A composed service's failed result undoes the earlier writes without anything being thrown.</summary>
    [Theory]
    [MemberData(nameof(FailedResults))]
    public async Task ExecuteInTransactionAsync_ResultFails_RollsBackAndReturnsIt(Result<int> failed)
    {
        Mock<IUnitOfWork> mockUnitOfWork = new();

        var result = await mockUnitOfWork.Object.ExecuteInTransactionAsync(
            _ => Task.FromResult(failed), _logger.Object, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.IsNotFound.Should().Be(failed.IsNotFound);
        result.IsConflict.Should().Be(failed.IsConflict);
        result.ErrorMessage.Should().Be(failed.ErrorMessage);
        mockUnitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        mockUnitOfWork.Verify(u => u.RollbackAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ResultOperationThrows_RollsBackAndPropagates()
    {
        Mock<IUnitOfWork> mockUnitOfWork = new();

        Func<Task> act = () => mockUnitOfWork.Object.ExecuteInTransactionAsync<int>(
            _ => throw new InvalidOperationException("Database error"), _logger.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Database error");
        mockUnitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        mockUnitOfWork.Verify(u => u.RollbackAsync(CancellationToken.None), Times.Once);
    }

    #endregion
}
