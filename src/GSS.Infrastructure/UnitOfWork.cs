using System.Data;
using GSS.Core.Interfaces.Base;
using GSS.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace GSS.Infrastructure;

/// <summary>
/// <see cref="IUnitOfWork"/> over <c>IDapperTransactionManager</c>.
/// </summary>
public sealed class UnitOfWork(
    IDapperTransactionManager transactionManager,
    ILogger<UnitOfWork> logger) : IUnitOfWork
{
    private bool _disposed;

    /// <inheritdoc />
    public async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        await transactionManager.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await transactionManager.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await transactionManager.RollbackAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rolls back an open transaction so it does not hold locks and a pooled connection.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (transactionManager.GetCurrentTransaction() is null)
        {
            return;
        }

        try
        {
            await transactionManager.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            logger.LogWarning("Unit of work disposed with an open transaction; the transaction was rolled back");
        }
        catch (Exception ex)
        {
            // Never throw from dispose: it would mask the exception unwinding the scope.
            logger.LogError(ex, "Error rolling back the open transaction while disposing the unit of work");
        }
    }
}
