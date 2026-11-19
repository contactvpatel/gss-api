using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using GSS.Infrastructure.DbUp.Core;
using GSS.Infrastructure.Interfaces;
using GSS.IntegrationTests.Fixtures;
using GSS.Util.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace GSS.IntegrationTests.Persistence;

/// <summary>Startup migration behaviour: re-runnable scripts, and failures that throw instead of exiting the process.</summary>
[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class DatabaseMigrationIntegrationTests(WebAppFactory factory)
{
    private const string MigrationsPrefix = "GSS.Infrastructure.DbUp.Migrations.";

    [Fact]
    public async Task MigrationScripts_ReRunOnMigratedDatabase_Succeed()
    {
        _ = factory.Server; // startup migrations have run

        Assembly assembly = typeof(DatabaseMigrationInitFilter).Assembly;
        string[] scripts = [.. assembly.GetManifestResourceNames().Where(n => n.StartsWith(MigrationsPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        // Vacuous until the first feature migration lands; then every script must stay re-runnable.

        await using NpgsqlConnection connection = new(TestDatabaseSettings.ConnectionString());
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        foreach (string script in scripts)
        {
            await using Stream stream = assembly.GetManifestResourceStream(script)!;
            using StreamReader reader = new(stream);
            await using NpgsqlCommand command = new(await reader.ReadToEndAsync(), connection, transaction);

            Func<Task> rerun = () => command.ExecuteNonQueryAsync();

            await rerun.Should().NotThrowAsync($"{script} must be safe to run again (DbUp re-runs a renamed script)");
        }

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task MissingDatabase_Throws_AndIsNotCreated()
    {
        string databaseName = TestDatabaseSettings.DatabaseName + "_missing";
        await DropDatabaseAsync(databaseName);
        bool nextCalled = false;
        Action<IApplicationBuilder> pipeline = CreateFilter(databaseName, lockKey: 990003, lockTimeoutSeconds: 1)
            .Configure(_ => nextCalled = true);

        Action run = () => pipeline(CreateApplicationBuilder());

        run.Should().Throw<InvalidOperationException>()
            .WithMessage($"Database '{databaseName}' does not exist on server '*'*never creates databases*");
        nextCalled.Should().BeFalse();
        (await DatabaseExistsAsync(databaseName)).Should().BeFalse("the API must not create databases");
    }

    /// <summary>
    /// Money amounts are numeric(19,4) (decimal), never MONEY: MONEY reads and writes through lc_monetary, so its
    /// text form is locale-dependent, and it has a fixed scale.
    /// </summary>
    [Fact]
    public async Task NoColumn_UsesTheMoneyType()
    {
        _ = factory.Server; // startup migrations have run

        IReadOnlyList<string> moneyColumns = await QueryStringsAsync(TestDatabaseSettings.DatabaseName, """
            SELECT table_name || '.' || column_name
            FROM information_schema.columns
            WHERE table_schema = 'gss' AND data_type = 'money'
            ORDER BY 1
            """);

        moneyColumns.Should().BeEmpty(because: "money amounts are numeric(19,4), never MONEY");
    }

    /// <summary>The journal lives in the service's schema, so services sharing a database never share script names.</summary>
    [Fact]
    public async Task FreshDatabase_JournalsInTheServiceSchema_NotPublic()
    {
        string databaseName = TestDatabaseSettings.DatabaseName + "_journal";
        await RecreateDatabaseAsync(databaseName);
        try
        {
            CreateFilter(databaseName, lockKey: 990005, lockTimeoutSeconds: 5)
                .Configure(_ => { })(CreateApplicationBuilder());

            (await ScalarAsync<bool>(databaseName, "SELECT to_regclass('gss.schemaversions') IS NOT NULL"))
                .Should().BeTrue();
            (await ScalarAsync<bool>(databaseName, "SELECT to_regclass('public.schemaversions') IS NULL"))
                .Should().BeTrue("DbUp must not fall back to its default public journal");
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    /// <summary>A database migrated before the journal moved: its entries are carried over and nothing is lost.</summary>
    [Fact]
    public async Task LegacyPublicJournal_IsCarriedOver_AndKept()
    {
        string databaseName = TestDatabaseSettings.DatabaseName + "_legacy";
        await RecreateDatabaseAsync(databaseName);
        try
        {
            await ExecuteAsync(databaseName, """
                CREATE TABLE public.schemaversions
                (
                    schemaversionsid serial NOT NULL,
                    scriptname character varying(255) NOT NULL,
                    applied timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_schemaversions_Id" PRIMARY KEY (schemaversionsid)
                );
                INSERT INTO public.schemaversions (scriptname, applied) VALUES ('legacy.sql', timestamp '2025-01-01');
                """);

            CreateFilter(databaseName, lockKey: 990006, lockTimeoutSeconds: 5)
                .Configure(_ => { })(CreateApplicationBuilder());

            (await ScalarAsync<long>(databaseName, "SELECT COUNT(*) FROM gss.schemaversions"))
                .Should().Be(1, "the applied script is carried over");
            (await ScalarAsync<long>(databaseName, "SELECT COUNT(*) FROM public.schemaversions"))
                .Should().Be(1, "the legacy journal is kept for any other service still using it");
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task TestDatabaseReset_DoesNotEraseTheMigrationJournal()
    {
        _ = factory.Server;
        const string probeScript = "__cleaner-probe__.sql";
        await ExecuteAsync(TestDatabaseSettings.DatabaseName, $"""
            INSERT INTO gss.schemaversions (scriptname, applied) VALUES ('{probeScript}', timestamp '2025-01-01');
            """);

        try
        {
            await TestDatabaseReset.ResetAsync(TestDatabaseSettings.ConnectionString());

            (await ScalarAsync<long>(TestDatabaseSettings.DatabaseName,
                $"SELECT COUNT(*) FROM gss.schemaversions WHERE scriptname = '{probeScript}'"))
                .Should().Be(1, "resetting feature data must not make DbUp forget applied migrations");
        }
        finally
        {
            await ExecuteAsync(TestDatabaseSettings.DatabaseName,
                $"DELETE FROM gss.schemaversions WHERE scriptname = '{probeScript}'");
        }
    }

    [Fact]
    public async Task AdvisoryLockTimeout_Throws_InsteadOfStartingUnmigrated()
    {
        const long lockKey = 990002;
        await using NpgsqlConnection holder = new(TestDatabaseSettings.ConnectionString());
        await holder.OpenAsync();
        await using (NpgsqlCommand take = new($"SELECT pg_advisory_lock({lockKey});", holder))
        {
            await take.ExecuteNonQueryAsync();
        }

        bool nextCalled = false;
        Action<IApplicationBuilder> pipeline = CreateFilter(TestDatabaseSettings.DatabaseName, lockKey, lockTimeoutSeconds: 1)
            .Configure(_ => nextCalled = true);

        Action run = () => pipeline(CreateApplicationBuilder());

        run.Should().Throw<InvalidOperationException>().WithMessage("*advisory lock*");
        nextCalled.Should().BeFalse();
    }

    /// <summary>
    /// A migration that cannot get its table lock fails fast (lock_timeout) instead of waiting up to the 10-minute script
    /// timeout while every new query queues behind it.
    /// </summary>
    [Fact]
    public async Task MigrationStatements_HaveALockTimeout()
    {
        await using NpgsqlConnection holder = new(TestDatabaseSettings.ConnectionString());
        await holder.OpenAsync();
        await using NpgsqlTransaction transaction = await holder.BeginTransactionAsync();
        await using (NpgsqlCommand take = new("LOCK TABLE gss.schemaversions IN ACCESS EXCLUSIVE MODE;", holder, transaction))
        {
            await take.ExecuteNonQueryAsync();
        }

        Action<IApplicationBuilder> pipeline = CreateFilter(
                TestDatabaseSettings.DatabaseName,
                lockKey: 990003,
                lockTimeoutSeconds: 5,
                migrationLockTimeoutSeconds: 1)
            .Configure(_ => { });
        Stopwatch elapsed = Stopwatch.StartNew();

        Exception? thrown = Record.Exception(() => pipeline(CreateApplicationBuilder()));

        elapsed.Stop();
        await transaction.RollbackAsync();
        thrown.Should().NotBeNull();
        LockNotAvailable(thrown).Should().BeTrue("the journal read hit lock_timeout (SQLSTATE 55P03)");
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Migration connections are unpooled: once startup ends, successful or not, none of their sessions stays open
    /// (pooled ones idled for the pod's life, under MinPoolSize) and the advisory lock is gone with its session.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterStartup_NoMigrationSessionOrAdvisoryLockRemains(bool failing)
    {
        const long lockKey = 990007;
        string databaseName = TestDatabaseSettings.DatabaseName + "_sessions";
        await RecreateDatabaseAsync(databaseName);
        try
        {
            if (failing)
            {
                await ExecuteAsync(databaseName, "CREATE SCHEMA gss; CREATE TABLE gss.schemaversions (unexpected int);");
            }

            Exception? thrown = Record.Exception(() =>
                CreateFilter(databaseName, lockKey, lockTimeoutSeconds: 5).Configure(_ => { })(CreateApplicationBuilder()));

            (thrown != null).Should().Be(failing, thrown?.ToString());
            (await WaitForZeroAsync($"SELECT COUNT(*) FROM pg_stat_activity WHERE datname = '{databaseName}'"))
                .Should().Be(0, "no migration connection may stay open in a pool");
            (await WaitForZeroAsync($"SELECT COUNT(*) FROM pg_locks WHERE locktype = 'advisory' AND objid = {lockKey} AND objsubid = 1"))
                .Should().Be(0, "the advisory lock must not outlive the migration");
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    /// <summary>The count from <paramref name="sql"/> (run on <c>postgres</c>), polled briefly: a closed session's backend exits asynchronously.</summary>
    private static async Task<long> WaitForZeroAsync(string sql)
    {
        long count = 0;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            count = await ScalarAsync<long>("postgres", sql);
            if (count == 0)
            {
                return 0;
            }

            await Task.Delay(100);
        }

        return count;
    }

    private static bool LockNotAvailable(Exception? exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable })
            {
                return true;
            }
        }

        return false;
    }

    private static DatabaseMigrationInitFilter CreateFilter(
        string databaseName,
        long lockKey,
        int lockTimeoutSeconds,
        int migrationLockTimeoutSeconds = 5)
    {
        Dictionary<string, string?> settings = new(TestDatabaseSettings.ToConfigurationOverrides())
        {
            ["DbConnectionSettings:Read:DatabaseName"] = databaseName,
            ["DbConnectionSettings:Write:DatabaseName"] = databaseName,
        };
        DbConnectionModel connectionSettings = new ConfigurationBuilder().AddInMemoryCollection(settings).Build()
            .GetSection("DbConnectionSettings").Get<DbConnectionModel>()!;

        return new DatabaseMigrationInitFilter(
            new StaticOptionsMonitor<AppSettingModel>(new AppSettingModel { ApplicationName = "gss-migration-test", Environment = "Testing" }),
            new StaticOptionsMonitor<DatabaseMigrationOptions>(new DatabaseMigrationOptions
            {
                Enabled = true,
                PostgresqlAdvisoryLockKey = lockKey,
                AdvisoryLockTimeoutSeconds = lockTimeoutSeconds,
                LockTimeoutSeconds = migrationLockTimeoutSeconds,
                LockTimeoutAttempts = 1,
            }),
            Options.Create(connectionSettings),
            new TestConnectionFactory(databaseName),
            NullLogger<DatabaseMigrationInitFilter>.Instance);
    }

    private static ApplicationBuilder CreateApplicationBuilder()
    {
        return new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
    }

    private static async Task RecreateDatabaseAsync(string databaseName)
    {
        await DropDatabaseAsync(databaseName);
        await ExecuteAsync("postgres", $"CREATE DATABASE \"{databaseName}\";");
    }

    private static Task DropDatabaseAsync(string databaseName)
    {
        return ExecuteAsync("postgres", $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);");
    }

    private static async Task<bool> DatabaseExistsAsync(string databaseName)
    {
        NpgsqlConnectionStringBuilder builder = new(TestDatabaseSettings.ConnectionString()) { Database = "postgres", Pooling = false };
        await using NpgsqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)", connection);
        command.Parameters.AddWithValue("name", databaseName);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<T> ScalarAsync<T>(string databaseName, string sql)
    {
        NpgsqlConnectionStringBuilder builder = new(TestDatabaseSettings.ConnectionString()) { Database = databaseName, Pooling = false };
        await using NpgsqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(string databaseName, string sql)
    {
        NpgsqlConnectionStringBuilder builder = new(TestDatabaseSettings.ConnectionString()) { Database = databaseName, Pooling = false };
        await using NpgsqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        List<string> rows = [];
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task ExecuteAsync(string databaseName, string sql)
    {
        NpgsqlConnectionStringBuilder builder = new(TestDatabaseSettings.ConnectionString()) { Database = databaseName, Pooling = false };
        await using NpgsqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name)
        {
            return value;
        }

        public IDisposable? OnChange(Action<T, string?> listener)
        {
            return null;
        }
    }

    // Used only by the post-migration type reload.
    private sealed class TestConnectionFactory(string databaseName) : IDapperConnectionFactory
    {
        public DbConnection CreateReadConnection()
        {
            return CreateWriteConnection();
        }

        public DbConnection CreateWriteConnection()
        {
            NpgsqlConnectionStringBuilder builder = new(TestDatabaseSettings.ConnectionString()) { Database = databaseName, Pooling = false };
            return new NpgsqlConnection(builder.ConnectionString);
        }
    }
}
