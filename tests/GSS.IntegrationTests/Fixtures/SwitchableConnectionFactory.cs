using System.Data.Common;
using GSS.Infrastructure.Interfaces;
using Npgsql;

namespace GSS.IntegrationTests.Fixtures;

/// <summary>
/// Delegates to the real connection factory; once <see cref="FailReads"/> is set, read connections point at a closed
/// port. Lets a test simulate a database outage after startup validation and migrations have passed.
/// </summary>
public sealed class SwitchableConnectionFactory(IDapperConnectionFactory inner) : IDapperConnectionFactory, IDisposable
{
    /// <summary>Password in the failing connection string; must never appear in a health response.</summary>
    public const string SecretPassword = "must-not-leak";

    /// <summary>When true, <see cref="CreateReadConnection"/> returns a connection that cannot open.</summary>
    public bool FailReads { get; set; }

    /// <inheritdoc />
    public DbConnection CreateReadConnection()
    {
        return FailReads
            ? new NpgsqlConnection($"Host=127.0.0.1;Port=1;Database=gss_test;Username=postgres;Password={SecretPassword};Timeout=2")
            : inner.CreateReadConnection();
    }

    /// <inheritdoc />
    public DbConnection CreateWriteConnection()
    {
        return inner.CreateWriteConnection();
    }

    /// <summary>Disposes the wrapped factory (and its data sources).</summary>
    public void Dispose()
    {
        (inner as IDisposable)?.Dispose();
    }
}
