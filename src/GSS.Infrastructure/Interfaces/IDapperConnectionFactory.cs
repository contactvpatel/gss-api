using System.Data.Common;

namespace GSS.Infrastructure.Interfaces;

/// <summary>
/// Creates read and write database connections (unopened; the caller disposes them).
/// </summary>
public interface IDapperConnectionFactory
{
    /// <summary>New connection to the read database.</summary>
    DbConnection CreateReadConnection();

    /// <summary>New connection to the write database.</summary>
    DbConnection CreateWriteConnection();
}
