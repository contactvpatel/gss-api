using System.Data;
using System.Data.Common;

namespace GSS.UnitTests.Common;

/// <summary>
/// In-memory <see cref="DbConnection"/> (Moq can't mock its protected members). Records opens, closes, disposes and SQL.
/// </summary>
public class FakeDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;
    private readonly TaskCompletionSource _executionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Number of times the connection was opened (sync or async).</summary>
    public int OpenCount { get; private set; }

    /// <summary>Number of times <see cref="Close"/> was called.</summary>
    public int CloseCount { get; private set; }

    /// <summary>Number of times the connection was disposed.</summary>
    public int DisposeCount { get; private set; }

    /// <summary>Command text of every command executed through this connection.</summary>
    public List<string> ExecutedCommands { get; } = [];

    /// <summary>The transaction each executed command was enlisted in (null when none).</summary>
    public List<DbTransaction?> ExecutedTransactions { get; } = [];

    /// <summary>Transactions begun on this connection.</summary>
    public List<FakeDbTransaction> Transactions { get; } = [];

    /// <summary>Rows affected reported by non-query commands (default 1).</summary>
    public int NonQueryResult { get; set; } = 1;

    /// <summary>Rows returned by reader commands; null keeps the default single integer row.</summary>
    public DataTable? ReaderResult { get; set; }

    /// <summary>Exception thrown by <see cref="Open"/> / <c>OpenAsync</c>, when set.</summary>
    public Exception? OpenException { get; set; }

    /// <summary>Exception thrown when beginning a transaction, when set.</summary>
    public Exception? BeginTransactionException { get; set; }

    /// <summary>Exception thrown when a command created by this connection is executed, when set.</summary>
    public Exception? ExecuteException { get; set; }

    /// <summary>Whether asynchronous command execution waits until its cancellation token is cancelled.</summary>
    public bool WaitForCancellationOnExecuteAsync { get; set; }

    /// <summary>Completes when an asynchronous command starts executing.</summary>
    public Task ExecutionStarted => _executionStarted.Task;

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString { get; set; } = "fake";

    /// <inheritdoc />
    public override string Database => "fake";

    /// <inheritdoc />
    public override string DataSource => "fake";

    /// <inheritdoc />
    public override string ServerVersion => "0.0";

    /// <inheritdoc />
    public override ConnectionState State => _state;

    /// <inheritdoc />
    public override void ChangeDatabase(string databaseName)
    {
    }

    /// <inheritdoc />
    public override void Close()
    {
        CloseCount++;
        _state = ConnectionState.Closed;
    }

    /// <inheritdoc />
    public override void Open()
    {
        if (OpenException != null)
        {
            throw OpenException;
        }

        OpenCount++;
        _state = ConnectionState.Open;
    }

    /// <summary>Forces the reported state, for tests that need an already-open connection.</summary>
    public void SetState(ConnectionState state)
    {
        _state = state;
    }

    /// <inheritdoc />
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        if (BeginTransactionException != null)
        {
            throw BeginTransactionException;
        }

        FakeDbTransaction transaction = new(this, isolationLevel);
        Transactions.Add(transaction);
        return transaction;
    }

    /// <inheritdoc />
    protected override DbCommand CreateDbCommand()
    {
        return new FakeDbCommand(this);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCount++;
            _state = ConnectionState.Closed;
        }

        base.Dispose(disposing);
    }

    internal void RecordExecution(string commandText, DbTransaction? transaction)
    {
        if (ExecuteException != null)
        {
            throw ExecuteException;
        }

        ExecutedCommands.Add(commandText);
        ExecutedTransactions.Add(transaction);
    }

    internal async Task WaitForExecutionAsync(CancellationToken cancellationToken)
    {
        _executionStarted.TrySetResult();
        if (WaitForCancellationOnExecuteAsync)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
