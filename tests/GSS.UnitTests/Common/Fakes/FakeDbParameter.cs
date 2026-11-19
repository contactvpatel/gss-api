using System.Data;
using System.Data.Common;

namespace GSS.UnitTests.Common;

/// <summary>
/// Minimal <see cref="DbParameter"/> test double.
/// </summary>
public sealed class FakeDbParameter : DbParameter
{
    /// <inheritdoc />
    public override DbType DbType { get; set; }

    /// <inheritdoc />
    public override ParameterDirection Direction { get; set; }

    /// <inheritdoc />
    public override bool IsNullable { get; set; }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ParameterName { get; set; } = string.Empty;

    /// <inheritdoc />
    public override int Size { get; set; }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool SourceColumnNullMapping { get; set; }

    /// <inheritdoc />
    public override object? Value { get; set; }

    /// <inheritdoc />
    public override void ResetDbType()
    {
    }
}

/// <summary>
/// Minimal <see cref="DbParameterCollection"/> test double.
/// </summary>
public sealed class FakeDbParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = [];

    /// <inheritdoc />
    public override int Count => _parameters.Count;

    /// <inheritdoc />
    public override object SyncRoot { get; } = new();

    /// <inheritdoc />
    public override int Add(object value)
    {
        _parameters.Add((DbParameter)value);
        return _parameters.Count - 1;
    }

    /// <inheritdoc />
    public override void AddRange(Array values)
    {
        foreach (object value in values)
        {
            Add(value);
        }
    }

    /// <inheritdoc />
    public override void Clear()
    {
        _parameters.Clear();
    }

    /// <inheritdoc />
    public override bool Contains(object value)
    {
        return _parameters.Contains((DbParameter)value);
    }

    /// <inheritdoc />
    public override bool Contains(string value)
    {
        return IndexOf(value) >= 0;
    }

    /// <inheritdoc />
    public override void CopyTo(Array array, int index)
    {
        ((System.Collections.ICollection)_parameters).CopyTo(array, index);
    }

    /// <inheritdoc />
    public override System.Collections.IEnumerator GetEnumerator()
    {
        return _parameters.GetEnumerator();
    }

    /// <inheritdoc />
    public override int IndexOf(object value)
    {
        return _parameters.IndexOf((DbParameter)value);
    }

    /// <inheritdoc />
    public override int IndexOf(string parameterName)
    {
        return _parameters.FindIndex(p => p.ParameterName == parameterName);
    }

    /// <inheritdoc />
    public override void Insert(int index, object value)
    {
        _parameters.Insert(index, (DbParameter)value);
    }

    /// <inheritdoc />
    public override void Remove(object value)
    {
        _parameters.Remove((DbParameter)value);
    }

    /// <inheritdoc />
    public override void RemoveAt(int index)
    {
        _parameters.RemoveAt(index);
    }

    /// <inheritdoc />
    public override void RemoveAt(string parameterName)
    {
        int index = IndexOf(parameterName);
        if (index >= 0)
        {
            _parameters.RemoveAt(index);
        }
    }

    /// <inheritdoc />
    protected override DbParameter GetParameter(int index)
    {
        return _parameters[index];
    }

    /// <inheritdoc />
    protected override DbParameter GetParameter(string parameterName)
    {
        return _parameters[IndexOf(parameterName)];
    }

    /// <inheritdoc />
    protected override void SetParameter(int index, DbParameter value)
    {
        _parameters[index] = value;
    }

    /// <inheritdoc />
    protected override void SetParameter(string parameterName, DbParameter value)
    {
        _parameters[IndexOf(parameterName)] = value;
    }
}
