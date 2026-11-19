using System.Data;
using System.Data.Common;
using GSS.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace GSS.Infrastructure.Helpers;

/// <summary>
/// Scoped Dapper transaction manager over the factory's <see cref="DbConnection"/> (async-only).
/// </summary>
public class DapperTransactionManager(
    IDapperConnectionFactory connectionFactory,
    ILogger<DapperTransactionManager> logger) : IDapperTransactionManager
{
    private DbConnection? _writeConnection;
    private DbTransaction? _currentTransaction;
    private bool _disposed;

    // 1 while a database call runs on the transaction's connection (see EnterCommand).
    private int _commandRunning;
    private CommandLease? _commandLease;

    /// <inheritdoc />
    public async Task<IDbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfTransactionActive();

        // Fields are set only once the transaction exists: a failed start keeps no connection, so a retry is safe.
        var connection = connectionFactory.CreateWriteConnection();
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            var transaction = await connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
            _writeConnection = connection;
            _currentTransaction = transaction;

            logger.TransactionStarted(isolationLevel);

            return transaction;
        }
        catch
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                logger.TransactionStartCleanupFailed(cleanupException);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public IDbTransaction? GetCurrentTransaction()
    {
        ThrowIfDisposed();
        return _currentTransaction;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Npgsql would fail the second call with "operation already in progress", far from the cause; this names it.
    /// </remarks>
    public IDisposable EnterCommand()
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _commandRunning, 1) == 1)
        {
            throw new InvalidOperationException(
                "A database call started while another call of the same Unit of Work was still running. A transaction has " +
                "one connection, so its calls run one at a time: await each before starting the next (no Task.WhenAll).");
        }

        return _commandLease ??= new CommandLease(this);
    }

    /// <inheritdoc />
    /// <remarks>A failure is not logged here: it propagates and is logged once where it is handled.</remarks>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _commandRunning) == 1)
        {
            throw new InvalidOperationException("Commit while a database call of the Unit of Work is still running: await it first.");
        }

        var transaction = GetTransactionToCommit();

        try
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            logger.TransactionCommitted();
        }
        finally
        {
            await DisposeTransactionAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// No transaction is a no-op (Debug): a failed commit has already ended it, and the caller's rollback after that is
    /// expected. A failure is not logged here: it propagates and is logged once where it is handled.
    /// </remarks>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var transaction = _currentTransaction;
        if (transaction == null)
        {
            logger.RollbackWithoutTransaction();
            return;
        }

        try
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            logger.TransactionRolledBack();
        }
        finally
        {
            await DisposeTransactionAsync().ConfigureAwait(false);
        }
    }

    private void ThrowIfTransactionActive()
    {
        if (_currentTransaction != null)
        {
            throw new InvalidOperationException("A transaction is already active. Nested transactions are not supported.");
        }
    }

    /// <summary>A disposed manager has no connection; using it would silently run writes outside the Unit of Work.</summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private DbTransaction GetTransactionToCommit()
    {
        return _currentTransaction ?? throw new InvalidOperationException("No active transaction to commit.");
    }

    private async Task DisposeTransactionAsync()
    {
        if (_currentTransaction != null)
        {
            await _currentTransaction.DisposeAsync().ConfigureAwait(false);
            _currentTransaction = null;
        }

        if (_writeConnection != null)
        {
            await _writeConnection.CloseAsync().ConfigureAwait(false);
            await _writeConnection.DisposeAsync().ConfigureAwait(false);
            _writeConnection = null;
        }
    }

    /// <summary>Ends the call started by <see cref="EnterCommand"/>.</summary>
    private sealed class CommandLease(DapperTransactionManager owner) : IDisposable
    {
        public void Dispose()
        {
            Volatile.Write(ref owner._commandRunning, 0);
        }
    }

    /// <summary>Rolls back an open transaction and releases its connection; never throws.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_currentTransaction != null)
        {
            try
            {
                await _currentTransaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                logger.TransactionRolledBackOnDispose();
            }
            catch (Exception ex)
            {
                logger.TransactionDisposeRollbackFailed(ex);
            }
        }

        await DisposeTransactionAsync().ConfigureAwait(false);
    }
}
