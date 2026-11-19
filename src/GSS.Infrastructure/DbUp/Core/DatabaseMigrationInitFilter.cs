using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using DbUp;
using DbUp.Builder;
using DbUp.Engine;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using GSS.Util.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GSS.Infrastructure.DbUp.Core;

/// <summary>
/// Runs DbUp migrations and environment seeds under an advisory lock before the pipeline is built.
/// Failures throw, so the host does not start. The database must already exist.
/// </summary>
public class DatabaseMigrationInitFilter(
    IOptionsMonitor<AppSettingModel> appSettingModel,
    IOptionsMonitor<DatabaseMigrationOptions> databaseMigrationOptions,
    IOptions<DbConnectionModel> connectionSettings,
    IDapperConnectionFactory connectionFactory,
    ILogger<DatabaseMigrationInitFilter> logger) : IStartupFilter
{
    private static readonly string BaseNamespace = typeof(DatabaseMigrationInitFilter).Namespace!
        .Replace(".Core", ""); // GSS.Infrastructure.DbUp

    private static readonly string MigrationsNamespace = $"{BaseNamespace}.Migrations";
    private static readonly string SeedsNamespacePrefix = $"{BaseNamespace}.Seeds.";
    private static readonly TimeSpan LockPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>Appended to the application name of migration connections.</summary>
    public const string MigrationApplicationNameSuffix = "-migrations";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        if (!databaseMigrationOptions.CurrentValue.Enabled)
        {
            logger.LogInformation("Database migration is disabled; skipping migration and seed.");
            return next;
        }

        return app =>
        {
            RunMigrationsAndSeeds();
            next(app);
        };
    }

    private void RunMigrationsAndSeeds()
    {
        // The validated Write connection (settings are never missing here: ValidateOnStart), made for migrations only.
        string dbConnectionString = ForMigrations(
            DatabaseConnectionStrings.Create(connectionSettings.Value.Write, appSettingModel.CurrentValue.ApplicationName),
            databaseMigrationOptions.CurrentValue.LockTimeoutSeconds);

        DbUpLoggerExtension dbUpLogger = new(logger);

        using NpgsqlConnection lockConnection = new(dbConnectionString);
        DatabaseConnectionValidator.Open(lockConnection); // missing database → detailed error, never created

        // Derived from the journal identity unless configured; see MigrationJournal.
        long lockKey = databaseMigrationOptions.CurrentValue.PostgresqlAdvisoryLockKey ?? MigrationJournal.AdvisoryLockKey;
        TimeSpan lockTimeout = TimeSpan.FromSeconds(databaseMigrationOptions.CurrentValue.AdvisoryLockTimeoutSeconds);
        if (!TryAcquireAdvisoryLock(lockConnection, lockKey, lockTimeout))
        {
            // Starting anyway could serve requests against an unmigrated schema.
            throw new InvalidOperationException(
                $"Could not acquire PostgreSQL advisory lock {lockKey} within {lockTimeout}; another instance may still be migrating.");
        }

        try
        {
            MigrationJournal.Prepare(lockConnection, logger);

            RunMigrations(dbConnectionString, dbUpLogger);

            // Also needed when another instance migrated while this one waited for the lock.
            ReloadDatabaseTypes();
            RunSeeds(dbConnectionString, dbUpLogger);
        }
        finally
        {
            ReleaseAdvisoryLock(lockConnection, lockKey);
        }
    }

    /// <summary>
    /// Pending migrations in order, each in its own transaction (so an enum value added by one script is committed
    /// before the next uses it), except <c>-NoTransaction</c> scripts (<see cref="MigrationBatches"/>).
    /// </summary>
    private void RunMigrations(string connectionString, DbUpLoggerExtension dbUpLogger)
    {
        var pending = BuildUpgrader(connectionString, MigrationsNamespace, dbUpLogger).GetScriptsToExecute();
        if (pending.Count == 0)
        {
            logger.LogInformation("No pending migrations detected; skipping migration.");
            return;
        }

        logger.LogInformation("{Count} pending migrations found; starting migration.", pending.Count);
        foreach ((bool transactional, var scripts) in MigrationBatches.Plan(pending.Select(s => s.Name)))
        {
            HashSet<string> batch = [.. scripts];
            PerformUpgrade(
                () => BuildUpgrader(connectionString, MigrationsNamespace, dbUpLogger, transactional, batch.Contains),
                script => $"Database migration failed in script '{script}'.");
        }

        logger.LogInformation("Database migrations completed successfully.");
    }

    /// <summary>
    /// Runs the upgrade; a script that hit lock_timeout is retried (this instance still holds the advisory lock, and
    /// journaled scripts are not re-run) up to <see cref="DatabaseMigrationOptions.LockTimeoutAttempts"/> times.
    /// </summary>
    private void PerformUpgrade(Func<UpgradeEngine> buildUpgrader, Func<string?, string> failureMessage)
    {
        int attempts = databaseMigrationOptions.CurrentValue.LockTimeoutAttempts;
        for (int attempt = 1; ; attempt++)
        {
            var result = buildUpgrader().PerformUpgrade();
            if (result.Successful)
            {
                return;
            }

            if (attempt >= attempts || !IsLockTimeout(result.Error))
            {
                throw new InvalidOperationException(failureMessage(result.ErrorScript?.Name), result.Error);
            }

            logger.LogWarning(
                "Script {Script} timed out waiting for a lock (attempt {Attempt} of {Attempts}); retrying in {Delay}.",
                result.ErrorScript?.Name, attempt, attempts, LockRetryDelay);
            Thread.Sleep(LockRetryDelay);
        }
    }

    private static bool IsLockTimeout(Exception? exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The migration connection string (lock and DbUp connections):
    /// <list type="bullet">
    /// <item><c>lock_timeout</c> on every statement, so DDL that cannot get its lock fails fast instead of queueing live
    /// queries behind it;</item>
    /// <item>no pooling: closing a connection ends its session, so the advisory lock can never outlive it (even when
    /// the unlock fails) and nothing idles for the pod's life (a pool keeps <c>MinPoolSize</c> sessions; nothing else
    /// uses this string) or is reused after the server killed it;</item>
    /// <item>its own application name, so a running migration stands out in <c>pg_stat_activity</c>.</item>
    /// </list>
    /// </summary>
    public static string ForMigrations(string connectionString, int lockTimeoutSeconds)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString)
        {
            Options = $"-c lock_timeout={lockTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}s",
            Pooling = false,
            MinPoolSize = 0
        };
        builder.ApplicationName = string.IsNullOrEmpty(builder.ApplicationName)
            ? MigrationApplicationNameSuffix.TrimStart('-')
            : builder.ApplicationName + MigrationApplicationNameSuffix;
        return builder.ConnectionString;
    }

    private static UpgradeEngine BuildUpgrader(
        string connectionString,
        string scriptNamespace,
        DbUpLoggerExtension dbUpLogger,
        bool transactional = true,
        Func<string, bool>? includeScript = null)
    {
        var assembly = typeof(DatabaseMigrationInitFilter).Assembly;
        string prefix = scriptNamespace.EndsWith('.') ? scriptNamespace : scriptNamespace + ".";
        var builder = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScripts(new PrefixStrippingScriptProvider(
                assembly,
                scriptNamespace,
                s => s.StartsWith(prefix, StringComparison.Ordinal) && (includeScript is null || includeScript(s[prefix.Length..]))))
            .WithVariablesDisabled()
            .WithExecutionTimeout(ScriptTimeout)
            .JournalToPostgresqlTable(MigrationJournal.Schema, MigrationJournal.Table)
            .LogTo(dbUpLogger);

        return (transactional ? builder.WithTransactionPerScript() : builder.WithoutTransaction()).Build();
    }

    /// <summary>
    /// Reloads Npgsql's type catalog for both connections; it was cached before migrations created the enum types.
    /// </summary>
    private void ReloadDatabaseTypes()
    {
        ReloadDatabaseTypes(connectionFactory.CreateWriteConnection());
        ReloadDatabaseTypes(connectionFactory.CreateReadConnection());
        logger.LogInformation("Reloaded database types after migration.");
    }

    private static void ReloadDatabaseTypes(IDbConnection connection)
    {
        using (connection)
        {
            if (connection is NpgsqlConnection npgsqlConnection)
            {
                npgsqlConnection.Open();
                npgsqlConnection.ReloadTypes();
            }
        }
    }

    private bool TryAcquireAdvisoryLock(NpgsqlConnection connection, long key, TimeSpan timeout)
    {
        long start = Stopwatch.GetTimestamp();
        while (true)
        {
            using NpgsqlCommand command = new("SELECT pg_try_advisory_lock(@key);", connection);
            command.Parameters.AddWithValue("key", key);
            if ((bool)command.ExecuteScalar()!)
            {
                logger.LogInformation("Acquired PostgreSQL advisory lock {Key}.", key);
                return true;
            }

            var remaining = timeout - Stopwatch.GetElapsedTime(start);
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            Thread.Sleep(remaining < LockPollInterval ? remaining : LockPollInterval);
        }
    }

    private void ReleaseAdvisoryLock(NpgsqlConnection connection, long key)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            using NpgsqlCommand command = new("SELECT pg_advisory_unlock(@key);", connection);
            command.Parameters.AddWithValue("key", key);
            command.ExecuteNonQuery();
            logger.LogInformation("Released PostgreSQL advisory lock {Key}.", key);
        }
        catch (NpgsqlException ex)
        {
            // Must not mask a migration failure. The connection is unpooled, so disposing it ends the session and the lock.
            logger.LogWarning(ex, "Could not release PostgreSQL advisory lock {Key}.", key);
        }
    }

    private void RunSeeds(string connectionString, DbUpLoggerExtension dbUpLogger)
    {
        string environmentName = appSettingModel.CurrentValue.Environment;
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            logger.LogInformation("AppSettings:Environment not set; skipping seed scripts.");
            return;
        }

        string seedsNamespace = SeedsNamespacePrefix + environmentName + ".";
        logger.LogInformation("Running seed scripts for environment {Environment}.", environmentName);

        if (!BuildUpgrader(connectionString, seedsNamespace, dbUpLogger).IsUpgradeRequired())
        {
            logger.LogInformation("No new seed data scripts found; skipping seeding for {Environment}.", environmentName);
            return;
        }

        PerformUpgrade(
            () => BuildUpgrader(connectionString, seedsNamespace, dbUpLogger),
            script => $"Seed scripts for {environmentName} failed in script '{script}'.");

        logger.LogInformation("Seed scripts for {Environment} completed successfully.", environmentName);
    }
}
