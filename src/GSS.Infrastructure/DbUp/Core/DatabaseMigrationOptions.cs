using System.ComponentModel.DataAnnotations;

namespace GSS.Infrastructure.DbUp.Core;

/// <summary>DbUp migration settings, section <c>DatabaseMigration</c>.</summary>
public class DatabaseMigrationOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "DatabaseMigration";

    /// <summary>Whether migrations and seeds run at startup.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Override for the migration advisory lock key. Leave unset: the key is derived from the journal identity
    /// (<see cref="MigrationJournal.AdvisoryLockKey"/>), so each service gets its own.
    /// </summary>
    public long? PostgresqlAdvisoryLockKey { get; set; }

    /// <summary>How long to wait for another instance's migration lock before startup fails.</summary>
    [Range(1, 3600)]
    public int AdvisoryLockTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// PostgreSQL <c>lock_timeout</c> for migration and seed statements: how long one waits for a table lock before it
    /// fails. Short, so DDL behind a running query fails fast instead of making every new query queue behind it during
    /// a rolling deploy. 0 waits forever (not recommended).
    /// </summary>
    [Range(0, 600)]
    public int LockTimeoutSeconds { get; set; } = 5;

    /// <summary>Attempts at a script that hit <see cref="LockTimeoutSeconds"/> (a few seconds apart) before startup fails.</summary>
    [Range(1, 10)]
    public int LockTimeoutAttempts { get; set; } = 3;
}
