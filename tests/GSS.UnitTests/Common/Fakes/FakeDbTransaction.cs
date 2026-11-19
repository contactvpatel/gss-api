using System.Data;
using System.Data.Common;

namespace GSS.UnitTests.Common;

/// <summary>
/// Minimal <see cref="DbTransaction"/> test double that records commit, rollback and dispose calls.
/// </summary>
public sealed class FakeDbTransaction(FakeDbConnection connection, IsolationLevel isolationLevel) : DbTransaction
{
    /// <summary>Number of times the transaction was committed (sync or async).</summary>
    public int CommitCount { get; private set; }

    /// <summary>Number of times the transaction was rolled back (sync or async).</summary>
    public int RollbackCount { get; private set; }

    /// <summary>Number of times the transaction was disposed.</summary>
    public int DisposeCount { get; private set; }

    /// <summary>Exception thrown by <see cref="Rollback"/>, when set.</summary>
    public Exception? RollbackException { get; set; }

    /// <summary>Exception thrown by <see cref="Commit"/>, when set.</summary>
    public Exception? CommitException { get; set; }

    /// <inheritdoc />
    public override IsolationLevel IsolationLevel => isolationLevel;

    /// <inheritdoc />
    protected override DbConnection DbConnection => connection;

    /// <inheritdoc />
    public override void Commit()
    {
        if (CommitException != null)
        {
            throw CommitException;
        }

        CommitCount++;
    }

    /// <inheritdoc />
    public override void Rollback()
    {
        if (RollbackException != null)
        {
            throw RollbackException;
        }

        RollbackCount++;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCount++;
        }

        base.Dispose(disposing);
    }
}
