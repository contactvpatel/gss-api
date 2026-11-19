using System.Data;

namespace GSS.Core.Interfaces.Base;

/// <summary>
/// Runs repository writes in one transaction. Async-only; disposing with an open transaction rolls it back.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Begins a transaction for repositories in this scope.</summary>
    Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default);

    /// <summary>Commits the transaction.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Rolls back the transaction.</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
