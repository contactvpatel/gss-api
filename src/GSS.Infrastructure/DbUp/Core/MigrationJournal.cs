using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GSS.Infrastructure.DbUp.Core;

/// <summary>
/// Where DbUp records applied scripts: the service's own schema, so services sharing a database never see each other's
/// script names. The migration advisory lock key is derived from the same identity, so two instances wait for each
/// other exactly when they share a journal.
/// </summary>
public static class MigrationJournal
{
    /// <summary>The service's schema.</summary>
    public const string Schema = "gss";

    /// <summary>DbUp's journal table name.</summary>
    public const string Table = "schemaversions";

    /// <summary>Where DbUp journaled by default before the journal moved; read once to carry its rows over.</summary>
    public const string LegacySchema = "public";

    /// <summary><c>schema.table</c>.</summary>
    public const string QualifiedName = Schema + "." + Table;

    /// <summary>Advisory lock key for migrations, derived from <see cref="QualifiedName"/>.</summary>
    public static long AdvisoryLockKey { get; } = DeriveLockKey(QualifiedName);

    /// <summary>
    /// Stable 64-bit key: the first 8 bytes of SHA-256 over <c>dbup-migrations:{journal}</c>. Never
    /// <see cref="string.GetHashCode()"/>, which differs per process. Changing it lets two versions migrate at once.
    /// </summary>
    public static long DeriveLockKey(string journalIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalIdentity);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("dbup-migrations:" + journalIdentity));
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }

    /// <summary>
    /// Run under the migration lock, before DbUp: creates <see cref="Schema"/> (DbUp creates the table, not the
    /// schema) and, when this schema has no journal yet but <c>public.schemaversions</c> exists, copies its rows into a
    /// new journal, so scripts already applied are not run again. The legacy table is left for any other service.
    /// </summary>
    public static void Prepare(NpgsqlConnection connection, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(logger);

        Execute(connection, null, $"CREATE SCHEMA IF NOT EXISTS {Schema};");

        bool carryOver;
        using (NpgsqlCommand check = new(
            $"SELECT to_regclass('{QualifiedName}') IS NULL AND to_regclass('{LegacySchema}.{Table}') IS NOT NULL;", connection))
        {
            carryOver = (bool)check.ExecuteScalar()!;
        }

        if (!carryOver)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        // Same DDL as DbUp's PostgreSQL journal, so DbUp uses the table as its own.
        Execute(connection, transaction, $"""
            CREATE TABLE {QualifiedName}
            (
                schemaversionsid serial NOT NULL,
                scriptname character varying(255) NOT NULL,
                applied timestamp without time zone NOT NULL,
                CONSTRAINT "PK_{Table}_Id" PRIMARY KEY (schemaversionsid)
            );
            """);
        int copied = Execute(connection, transaction, $"""
            INSERT INTO {QualifiedName} (scriptname, applied)
            SELECT scriptname, applied FROM {LegacySchema}.{Table} ORDER BY schemaversionsid;
            """);
        transaction.Commit();

        logger.LogWarning(
            "Moved the DbUp journal: copied {Count} entries from {LegacyJournal} to {Journal}. The legacy table is kept; drop it when no other service uses it.",
            copied,
            $"{LegacySchema}.{Table}",
            QualifiedName);
    }

    private static int Execute(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql)
    {
        using NpgsqlCommand command = new(sql, connection, transaction);
        return command.ExecuteNonQuery();
    }
}
