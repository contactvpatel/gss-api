using System.Data;

namespace GSS.Infrastructure.Interfaces;

/// <summary>
/// Scoped (per-request) write transaction for Dapper. Async-only; disposing with an open transaction rolls it back.
/// Not thread-safe: the transaction has one connection, so its database calls run one at a time (never
/// <c>Task.WhenAll</c>); <see cref="EnterCommand"/> enforces it.
/// </summary>
public interface IDapperTransactionManager : IAsyncDisposable
{
    /// <summary>Begins a transaction on the write connection.</summary>
    Task<IDbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default);

    /// <summary>Active transaction, or null.</summary>
    IDbTransaction? GetCurrentTransaction();

    /// <summary>
    /// Marks the transaction's connection busy for one database call; dispose the result when the call ends. Throws
    /// when another call is still running on it (two calls of one Unit of Work started in parallel). No I/O.
    /// </summary>
    /// <exception cref="InvalidOperationException">Another call is running on the transaction's connection.</exception>
    IDisposable EnterCommand();

    /// <summary>Commits the transaction.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Rolls back the transaction.</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
