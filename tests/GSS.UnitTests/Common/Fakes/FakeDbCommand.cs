using System.Data;
using System.Data.Common;

namespace GSS.UnitTests.Common;

/// <summary>
/// <see cref="DbCommand"/> double from <see cref="FakeDbConnection"/>; records executions on it.
/// </summary>
public sealed class FakeDbCommand(FakeDbConnection connection) : DbCommand
{
    private readonly FakeDbConnection _owner = connection;

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText { get; set; } = string.Empty;

    /// <inheritdoc />
    public override int CommandTimeout { get; set; }

    /// <inheritdoc />
    public override CommandType CommandType { get; set; } = CommandType.Text;

    /// <inheritdoc />
    public override bool DesignTimeVisible { get; set; }

    /// <inheritdoc />
    public override UpdateRowSource UpdatedRowSource { get; set; }

    /// <inheritdoc />
    protected override DbConnection? DbConnection { get; set; } = connection;

    /// <inheritdoc />
    protected override DbParameterCollection DbParameterCollection { get; } = new FakeDbParameterCollection();

    /// <inheritdoc />
    protected override DbTransaction? DbTransaction { get; set; }

    /// <inheritdoc />
    public override void Cancel()
    {
    }

    /// <inheritdoc />
    public override int ExecuteNonQuery()
    {
        _owner.RecordExecution(CommandText, DbTransaction);
        return _owner.NonQueryResult;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        _owner.RecordExecution(CommandText, DbTransaction);
        await _owner.WaitForExecutionAsync(cancellationToken);
        return _owner.NonQueryResult;
    }

    /// <inheritdoc />
    public override object? ExecuteScalar()
    {
        _owner.RecordExecution(CommandText, DbTransaction);
        return 1;
    }

    /// <inheritdoc />
    public override void Prepare()
    {
    }

    /// <inheritdoc />
    protected override DbParameter CreateDbParameter()
    {
        return new FakeDbParameter();
    }

    /// <inheritdoc />
    /// <summary>One row with one column, <c>value</c> = 1.</summary>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        _owner.RecordExecution(CommandText, DbTransaction);
        return CreateReader();
    }

    /// <inheritdoc />
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior,
        CancellationToken cancellationToken)
    {
        _owner.RecordExecution(CommandText, DbTransaction);
        await _owner.WaitForExecutionAsync(cancellationToken);
        return CreateReader();
    }

    private DbDataReader CreateReader()
    {
        if (_owner.ReaderResult != null)
        {
            return _owner.ReaderResult.CreateDataReader();
        }

        DataTable table = new();
        table.Columns.Add("value", typeof(int));
        table.Rows.Add(1);
        return table.CreateDataReader();
    }
}
