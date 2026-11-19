using System.Data;
using System.Data.Common;
using Dapper;
using FluentAssertions;
using GSS.Business.Helpers;
using GSS.Business.Models;
using GSS.Core.Entities;
using GSS.Core.Interfaces.Base;
using GSS.Infrastructure;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using GSS.Infrastructure.Repositories.Base;
using GSS.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace GSS.IntegrationTests.Persistence;

/// <summary>
/// Reads inside a Unit of Work transaction use the transaction's session; reads outside it never leak connections.
/// Range writes (AddRangeAsync / UpdateRangeAsync / DeleteRangeAsync) are one command per batch, all rows or none.
/// </summary>
[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class TransactionScopeIntegrationTests(TestDatabaseFixture fixture)
{
    private const string ProbeTableDdl = """
        CREATE SCHEMA IF NOT EXISTS gss;
        CREATE TABLE IF NOT EXISTS gss.transaction_probe (
            id serial PRIMARY KEY,
            name text NOT NULL,
            isactive boolean NOT NULL DEFAULT true,
            created timestamptz NOT NULL,
            createdby integer NOT NULL,
            updated timestamptz NULL,
            updatedby integer NOT NULL
        );
        """;

    /// <summary>The scaffold ships no migrations, so the test owns a probe table; then every gss table is truncated.</summary>
    private async Task ResetAsync()
    {
        using (IDbConnection connection = fixture.ConnectionFactory.CreateWriteConnection())
        {
            await connection.ExecuteAsync(ProbeTableDdl);
        }

        await fixture.ResetDatabaseAsync();
    }

    [Fact]
    public async Task TransactionReturningConflict_RollsBackEarlierWrite()
    {
        await ResetAsync();
        await using var manager = fixture.CreateTransactionManager();
        await using UnitOfWork unitOfWork = new(manager, NullLogger<UnitOfWork>.Instance);
        var repository = fixture.CreateRepository<ProbeRepository>(manager);

        var result = await unitOfWork.ExecuteInTransactionAsync(async cancellationToken =>
        {
            await repository.AddAsync(NewProbe("Before conflict"), cancellationToken);
            return Result<int>.Conflict("Probe conflict");
        }, NullLogger.Instance);

        result.IsConflict.Should().BeTrue();
        result.ErrorMessage.Should().Be("Probe conflict");
        manager.GetCurrentTransaction().Should().BeNull();
        (await fixture.CreateRepository<ProbeRepository>().GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task CursorPaging_ContinuesAfterTheLastVisibleRow()
    {
        await ResetAsync();
        var repository = fixture.CreateRepository<ProbeRepository>();
        await repository.AddRangeAsync([NewProbe("First"), NewProbe("Second"), NewProbe("Third")]);

        var first = await repository.GetPagedAfterAsync(0, 2);
        var last = await repository.GetPagedAfterAsync(first.NextAfterId!.Value, 2, includeTotal: true);

        first.Items.Select(item => item.Name).Should().Equal("First", "Second");
        first.TotalCount.Should().BeNull();
        last.Items.Select(item => item.Name).Should().Equal("Third");
        last.NextAfterId.Should().BeNull();
        last.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task ReadInsideTransaction_SeesRowWrittenEarlierInTheSameTransaction()
    {
        await ResetAsync();

        await using DapperTransactionManager transactionManager = fixture.CreateTransactionManager();
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>(transactionManager);

        await transactionManager.BeginTransactionAsync();

        ProbeEntity probe = new() { Name = "Uncommitted", CreatedBy = 1, UpdatedBy = 1 };
        await repository.AddAsync(probe);

        // These reads run on the transaction's session.
        bool exists = await repository.ExistsAsync(probe.Id);
        ProbeEntity? readBack = await repository.GetByIdAsync(probe.Id);
        IReadOnlyList<ProbeEntity> all = await repository.GetAllAsync();

        exists.Should().BeTrue("a read inside the transaction must see a row written earlier in that transaction");
        readBack.Should().NotBeNull();
        readBack!.Name.Should().Be("Uncommitted");
        all.Should().ContainSingle(p => p.Id == probe.Id);

        await transactionManager.CommitAsync();
    }

    [Fact]
    public async Task ReadInsideTransaction_UsesTheTransactionSessionNotAnAutonomousOne()
    {
        await ResetAsync();

        await using DapperTransactionManager transactionManager = fixture.CreateTransactionManager();
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>(transactionManager);

        await transactionManager.BeginTransactionAsync();
        ProbeEntity probe = new() { Name = "RolledBack", CreatedBy = 1, UpdatedBy = 1 };
        await repository.AddAsync(probe);
        (await repository.ExistsAsync(probe.Id)).Should().BeTrue();
        await transactionManager.RollbackAsync();

        // Outside the transaction the rolled-back row is gone, so the reads above saw uncommitted state.
        ProbeRepository autonomous = fixture.CreateRepository<ProbeRepository>();
        (await autonomous.ExistsAsync(probe.Id)).Should().BeFalse();
        (await autonomous.GetByIdAsync(probe.Id)).Should().BeNull();
    }

    [Fact]
    public async Task ReadInsideTransaction_DoesNotCloseTheTransactionConnection()
    {
        await ResetAsync();

        await using DapperTransactionManager transactionManager = fixture.CreateTransactionManager();
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>(transactionManager);

        IDbTransaction transaction = await transactionManager.BeginTransactionAsync();

        // Each read disposes its connection; that must not close the transaction's connection.
        await repository.GetAllAsync();
        await repository.ExistsAsync(1);
        await repository.GetPagedAsync(1, 10);

        transaction.Connection.Should().NotBeNull();
        transaction.Connection!.State.Should().Be(ConnectionState.Open);

        ProbeEntity probe = new() { Name = "StillWorks", CreatedBy = 1, UpdatedBy = 1 };
        await repository.AddAsync(probe);
        probe.Id.Should().BeGreaterThan(0);

        await transactionManager.CommitAsync();

        ProbeRepository autonomous = fixture.CreateRepository<ProbeRepository>();
        (await autonomous.ExistsAsync(probe.Id)).Should().BeTrue();
    }

    /// <summary>A scope that ends with the Unit of Work still open rolls it back and releases its connection (async disposal).</summary>
    [Fact]
    public async Task DisposingTheScope_WithAnOpenTransaction_RollsItBack()
    {
        await ResetAsync();
        TrackingConnectionFactory factory = new(fixture.ConnectionFactory);
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IDapperConnectionFactory>(factory);
        services.AddScoped<IDapperTransactionManager, DapperTransactionManager>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        ProbeEntity probe = NewProbe("Abandoned");

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            ProbeRepository repository = fixture.CreateRepository<ProbeRepository>(factory, scope.ServiceProvider.GetRequiredService<IDapperTransactionManager>());
            await unitOfWork.BeginTransactionAsync();
            await repository.AddAsync(probe);
        }

        factory.WriteConnections.Should().ContainSingle().Which.State.Should().Be(ConnectionState.Closed);
        (await fixture.CreateRepository<ProbeRepository>().ExistsAsync(probe.Id)).Should().BeFalse();
    }

    /// <summary>Without a transaction, every read connection is closed after use.</summary>
    [Fact]
    public async Task ReadWithoutTransaction_DisposesEveryConnectionItOpens()
    {
        await ResetAsync();

        TrackingConnectionFactory factory = new(fixture.ConnectionFactory);
        await using DapperTransactionManager transactionManager = fixture.CreateTransactionManager(factory);
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>(factory, transactionManager);

        await repository.AddAsync(new ProbeEntity { Name = "Autonomous", CreatedBy = 1, UpdatedBy = 1 });

        for (int i = 0; i < 5; i++)
        {
            await repository.GetAllAsync();
            await repository.ExistsAsync(1);
        }

        transactionManager.GetCurrentTransaction().Should().BeNull();
        factory.ReadConnectionsCreated.Should().Be(10, "each read outside a transaction opens its own connection");
        factory.ReadConnections.Should().OnlyContain(
            c => c.State == ConnectionState.Closed,
            "a connection the repository owns must be closed by the caller's using block");
    }

    /// <summary>Same check end to end: a leak exhausts a three-connection pool.</summary>
    [Fact]
    public async Task ReadWithoutTransaction_DoesNotExhaustASmallConnectionPool()
    {
        await ResetAsync();

        using PoolLimitedConnectionFactory factory = new(fixture.ConnectionFactory, maxPoolSize: 3, timeoutSeconds: 5);
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>(factory);

        await repository.AddAsync(new ProbeEntity { Name = "Pooled", CreatedBy = 1, UpdatedBy = 1 });

        // Far past the pool size, so any leak exhausts it.
        for (int i = 0; i < 20; i++)
        {
            (await repository.GetAllAsync()).Should().ContainSingle();
        }
    }

    /// <summary>AddRangeAsync sends one command and sets every Id, in row order.</summary>
    [Fact]
    public async Task AddRangeAsync_InsertsEveryRow_AndSetsIdsInOrder()
    {
        await ResetAsync();
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>();
        List<ProbeEntity> probes = [NewProbe("A"), NewProbe("B"), NewProbe("C")];

        await repository.AddRangeAsync(probes);

        probes.Select(p => p.Id).Should().OnlyHaveUniqueItems().And.NotContain(0);
        foreach (ProbeEntity probe in probes)
        {
            (await repository.GetByIdAsync(probe.Id))!.Name.Should().Be(probe.Name);
        }
    }

    [Fact]
    public async Task UpdateRangeAsync_UpdatesEveryRow()
    {
        await ResetAsync();
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>();
        List<ProbeEntity> probes = [NewProbe("A"), NewProbe("B")];
        await repository.AddRangeAsync(probes);

        probes[0].Name = "A2";
        probes[1].Name = "B2";
        (await repository.UpdateRangeAsync(probes)).Should().BeTrue();

        (await repository.GetByIdAsync(probes[0].Id))!.Name.Should().Be("A2");
        (await repository.GetByIdAsync(probes[1].Id))!.Name.Should().Be("B2");
    }

    [Fact]
    public async Task UpdateRangeAsync_ARowIsGone_ReturnsFalseAndUpdatesNothing()
    {
        await ResetAsync();
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>();
        List<ProbeEntity> probes = [NewProbe("A"), NewProbe("B")];
        await repository.AddRangeAsync(probes);
        await repository.DeleteAsync(probes[1]);

        probes[0].Name = "A2";
        probes[1].Name = "B2";
        (await repository.UpdateRangeAsync(probes)).Should().BeFalse();

        (await repository.GetByIdAsync(probes[0].Id))!.Name.Should().Be("A", "the batch is all or nothing");
    }

    [Fact]
    public async Task UpdateRangeAsync_DuplicateId_ReturnsFalseAndUpdatesNothing()
    {
        await ResetAsync();
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>();
        ProbeEntity probe = NewProbe("A");
        await repository.AddAsync(probe);
        probe.Name = "A2";

        (await repository.UpdateRangeAsync([probe, probe])).Should().BeFalse();

        (await repository.GetByIdAsync(probe.Id))!.Name.Should().Be("A", "the duplicate batch is all or nothing");
    }

    [Fact]
    public async Task DeleteRangeAsync_SoftDeletesEveryRow()
    {
        await ResetAsync();
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>();
        List<ProbeEntity> probes = [NewProbe("A"), NewProbe("B")];
        await repository.AddRangeAsync(probes);

        await repository.DeleteRangeAsync(probes);

        (await repository.GetAllAsync()).Should().BeEmpty();
        probes.Should().OnlyContain(p => !p.IsActive);
        (await repository.ExistsAsync(probes[0].Id, includeSoftDeleted: true)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteRangeAsync_ARowIsAlreadyGone_IsSkippedAndTheRestAreDeleted()
    {
        await ResetAsync();
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>();
        List<ProbeEntity> probes = [NewProbe("A"), NewProbe("B")];
        await repository.AddRangeAsync(probes);
        await repository.DeleteAsync(probes[1]);
        probes[1].IsActive = true;

        await repository.DeleteRangeAsync(probes);

        (await repository.GetByIdAsync(probes[0].Id)).Should().BeNull("rows already deleted are skipped, the others deleted");
    }

    [Fact]
    public async Task DeleteRangeAsync_DuplicateId_DeletesTheRowOnce()
    {
        await ResetAsync();
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>();
        ProbeEntity probe = NewProbe("A");
        await repository.AddAsync(probe);

        await repository.DeleteRangeAsync([probe, probe]);

        (await repository.GetByIdAsync(probe.Id)).Should().BeNull();
    }

    [Fact]
    public async Task FindExistingIdsAsync_ReturnsOnlyExistingIds_SoftDeletedOnlyWhenAsked()
    {
        await ResetAsync();
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>();
        List<ProbeEntity> probes = [NewProbe("Red"), NewProbe("Blue")];
        await repository.AddRangeAsync(probes);
        await repository.DeleteAsync(probes[1]);
        int[] ids = [probes[0].Id, probes[1].Id, 999999];

        (await repository.FindExistingIdsAsync(ids)).Should().BeEquivalentTo([probes[0].Id]);
        (await repository.FindExistingIdsAsync(ids, includeSoftDeleted: true))
            .Should().BeEquivalentTo([probes[0].Id, probes[1].Id]);
        (await repository.FindExistingIdsAsync([])).Should().BeEmpty();
    }

    [Fact]
    public async Task AddRangeAsync_InsideUnitOfWork_RollsBackWithIt()
    {
        await ResetAsync();
        await using DapperTransactionManager transactionManager = fixture.CreateTransactionManager();
        ProbeRepository repository = fixture.CreateRepository<ProbeRepository>(transactionManager);

        await transactionManager.BeginTransactionAsync();
        await repository.AddRangeAsync([NewProbe("A"), NewProbe("B")]);
        (await repository.GetAllAsync()).Should().HaveCount(2, "reads in the transaction see its writes");
        await transactionManager.RollbackAsync();

        ProbeRepository outside = fixture.CreateRepository<ProbeRepository>();
        (await outside.GetAllAsync()).Should().BeEmpty();
    }

    private static ProbeEntity NewProbe(string name)
    {
        return new ProbeEntity { Name = name, CreatedBy = 1, UpdatedBy = 1 };
    }

    /// <summary>Test-only entity standing in for a feature entity.</summary>
    private sealed class ProbeEntity : EntityBase
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Minimal feature-style repository: one shared select, base-class common reads and writes.</summary>
    private sealed class ProbeRepository(
        IDapperConnectionFactory connectionFactory,
        IDapperTransactionManager transactionManager,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        IUserContext userContext) : DapperRepositoryBase<ProbeEntity>(connectionFactory, transactionManager, loggerFactory, timeProvider, userContext)
    {
        private const string Select = """
            SELECT "id" AS Id, "name" AS Name, "isactive" AS IsActive, "created" AS CreatedAt,
                   "createdby" AS CreatedBy, "updated" AS UpdatedAt, "updatedby" AS UpdatedBy
            FROM "gss"."transaction_probe"
            """;

        protected override string TableName => "transaction_probe";

        protected override string SelectSql => Select;

        protected override IReadOnlyList<BatchColumn> BatchColumns =>
        [
            Column("name", entity => entity.Name)
        ];

        protected override string BuildInsertSql()
        {
            return """
                INSERT INTO "gss"."transaction_probe" ("name", "isactive", "created", "createdby", "updatedby")
                VALUES (@Name, @IsActive, @CreatedAt, @CreatedBy, @UpdatedBy)
                RETURNING "id"
                """;
        }

        protected override string BuildUpdateSql()
        {
            return """
                UPDATE "gss"."transaction_probe"
                SET "name" = @Name, "updated" = @UpdatedAt, "updatedby" = @UpdatedBy
                WHERE "id" = @Id AND "isactive" = true
                """;
        }

    }

    /// <summary>Records every connection handed out.</summary>
    private sealed class TrackingConnectionFactory(IDapperConnectionFactory inner) : IDapperConnectionFactory
    {
        public List<IDbConnection> ReadConnections { get; } = [];

        public List<IDbConnection> WriteConnections { get; } = [];

        public int ReadConnectionsCreated => ReadConnections.Count;

        public DbConnection CreateReadConnection()
        {
            DbConnection connection = inner.CreateReadConnection();
            ReadConnections.Add(connection);
            return connection;
        }

        public DbConnection CreateWriteConnection()
        {
            DbConnection connection = inner.CreateWriteConnection();
            WriteConnections.Add(connection);
            return connection;
        }
    }

    /// <summary>Private Npgsql pool of <c>maxPoolSize</c> connections (pools are keyed by connection string).</summary>
    private sealed class PoolLimitedConnectionFactory : IDapperConnectionFactory, IDisposable
    {
        private readonly string _connectionString;

        public PoolLimitedConnectionFactory(IDapperConnectionFactory inner, int maxPoolSize, int timeoutSeconds)
        {
            using IDbConnection probe = inner.CreateReadConnection();
            NpgsqlConnectionStringBuilder builder = new(probe.ConnectionString)
            {
                MaxPoolSize = maxPoolSize,
                MinPoolSize = 0,
                Timeout = timeoutSeconds,
                ApplicationName = "gss-pool-limited-test"
            };
            _connectionString = builder.ConnectionString;
        }

        public DbConnection CreateReadConnection()
        {
            return new NpgsqlConnection(_connectionString);
        }

        public DbConnection CreateWriteConnection()
        {
            return new NpgsqlConnection(_connectionString);
        }

        public void Dispose()
        {
            NpgsqlConnection.ClearPool(new NpgsqlConnection(_connectionString));
        }
    }
}
