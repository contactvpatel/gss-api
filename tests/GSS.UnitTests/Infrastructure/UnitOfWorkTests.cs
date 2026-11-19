using System.Data;
using FluentAssertions;
using GSS.Infrastructure;
using GSS.Infrastructure.Interfaces;
using GSS.UnitTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GSS.UnitTests.Infrastructure;

/// <summary>
/// Unit tests for UnitOfWork, including DisposeAsync rollback and the async lifecycle.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class UnitOfWorkTests
{
    private readonly Mock<IDapperTransactionManager> _transactionManager = new();

    [Fact]
    public async Task BeginTransactionAsync_DelegatesToTransactionManager()
    {
        // Arrange
        using CancellationTokenSource cts = new();
        await using UnitOfWork unitOfWork = new(_transactionManager.Object, NullLogger<UnitOfWork>.Instance);

        // Act
        await unitOfWork.BeginTransactionAsync(IsolationLevel.RepeatableRead, cts.Token);

        // Assert
        _transactionManager.Verify(t => t.BeginTransactionAsync(IsolationLevel.RepeatableRead, cts.Token), Times.Once);
    }

    [Fact]
    public async Task CommitAndRollbackAsync_DelegateToTransactionManager()
    {
        // Arrange
        using CancellationTokenSource cts = new();
        await using UnitOfWork unitOfWork = new(_transactionManager.Object, NullLogger<UnitOfWork>.Instance);

        // Act
        await unitOfWork.CommitAsync(cts.Token);
        await unitOfWork.RollbackAsync(cts.Token);

        // Assert
        _transactionManager.Verify(t => t.CommitAsync(cts.Token), Times.Once);
        _transactionManager.Verify(t => t.RollbackAsync(cts.Token), Times.Once);
    }

    /// <summary>DisposeAsync rolls back an open transaction so it releases locks and the connection.</summary>
    [Fact]
    public async Task DisposeAsync_OpenTransaction_RollsItBack()
    {
        // Arrange
        Mock<IDbTransaction> transaction = new();
        _transactionManager.Setup(t => t.GetCurrentTransaction()).Returns(transaction.Object);
        UnitOfWork unitOfWork = new(_transactionManager.Object, NullLogger<UnitOfWork>.Instance);

        // Act
        await unitOfWork.DisposeAsync();

        // Assert
        _transactionManager.Verify(t => t.RollbackAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_NoOpenTransaction_DoesNotRollBack()
    {
        // Arrange
        _transactionManager.Setup(t => t.GetCurrentTransaction()).Returns((IDbTransaction?)null);
        UnitOfWork unitOfWork = new(_transactionManager.Object, NullLogger<UnitOfWork>.Instance);

        // Act
        await unitOfWork.DisposeAsync();

        // Assert
        _transactionManager.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_RollsBackOnce()
    {
        // Arrange
        Mock<IDbTransaction> transaction = new();
        _transactionManager.Setup(t => t.GetCurrentTransaction()).Returns(transaction.Object);
        UnitOfWork unitOfWork = new(_transactionManager.Object, NullLogger<UnitOfWork>.Instance);

        // Act
        await unitOfWork.DisposeAsync();
        await unitOfWork.DisposeAsync();

        // Assert
        _transactionManager.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_RollbackThrows_DoesNotPropagate()
    {
        // Arrange
        Mock<IDbTransaction> transaction = new();
        _transactionManager.Setup(t => t.GetCurrentTransaction()).Returns(transaction.Object);
        _transactionManager.Setup(t => t.RollbackAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        UnitOfWork unitOfWork = new(_transactionManager.Object, NullLogger<UnitOfWork>.Instance);

        // Act
        Func<Task> act = async () => await unitOfWork.DisposeAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }
}
