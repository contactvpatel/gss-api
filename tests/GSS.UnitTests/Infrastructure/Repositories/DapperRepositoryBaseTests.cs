using System.Data;
using System.Data.Common;
using FluentAssertions;
using GSS.Core.Interfaces.Base;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using GSS.Infrastructure.Repositories.Base;
using GSS.UnitTests.Common;
using GSS.UnitTests.Common.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Repositories;

/// <summary>
/// Unit tests for DapperRepositoryBase: read connection selection (DATA-02), audit fields and table names.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DapperRepositoryBaseTests
{
    [Fact]
    public void DapperRepositoryBase_ShouldDefaultToGssSchema()
    {
        Mock<IDapperConnectionFactory> factory = new();
        TestRepository repository = new(factory.Object, transactionManager: null);

        repository.ExposeSchema().Should().Be("gss");
    }

    [Fact]
    public async Task QueryAsync_NoTransaction_RunsOnANewReadConnectionAndDisposesIt()
    {
        FakeDbConnection readConnection = new();
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateReadConnection()).Returns(readConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);

        IReadOnlyList<int> rows = await repository.ExposeQueryAsync("SELECT 1");

        rows.Should().Equal(1);
        readConnection.ExecutedTransactions.Should().Equal([null]);
        readConnection.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task QueryFirstOrDefaultAsync_NoTransaction_DisposesTheConnectionWhenTheQueryFails()
    {
        FakeDbConnection readConnection = new() { ExecuteException = new InvalidOperationException("query failed") };
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateReadConnection()).Returns(readConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);

        Func<Task> act = () => repository.ExposeQueryFirstOrDefaultAsync("SELECT 1");

        await act.Should().ThrowAsync<InvalidOperationException>();
        readConnection.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Reads_TransactionActive_RunInTheTransactionOnItsConnection_AndLeaveItOpen()
    {
        // Reads inside a Unit of Work see its uncommitted writes and must not close the connection it owns.
        FakeDbConnection writeConnection = new();
        writeConnection.Open();
        DbTransaction transaction = writeConnection.BeginTransaction();
        Mock<IDapperConnectionFactory> factory = new();
        Mock<IDapperTransactionManager> transactionManager = new();
        transactionManager.Setup(t => t.GetCurrentTransaction()).Returns(transaction);
        TestRepository repository = new(factory.Object, transactionManager.Object);

        (await repository.ExposeQueryAsync("SELECT 1")).Should().Equal(1);
        (await repository.ExposeQueryFirstOrDefaultAsync("SELECT 1")).Should().Be(1);

        writeConnection.ExecutedTransactions.Should().Equal([transaction, transaction]);
        writeConnection.State.Should().Be(ConnectionState.Open);
        writeConnection.CloseCount.Should().Be(0);
        writeConnection.DisposeCount.Should().Be(0);
        factory.Verify(f => f.CreateReadConnection(), Times.Never);
    }

    [Fact]
    public async Task UpdateRangeAsync_SendsOneCommand_InItsOwnCommittedTransaction()
    {
        FakeDbConnection writeConnection = new() { NonQueryResult = 3 };
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateWriteConnection()).Returns(writeConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);

        (await repository.UpdateRangeAsync([NewEntity(1), NewEntity(2), NewEntity(3)])).Should().BeTrue();

        writeConnection.ExecutedCommands.Should().ContainSingle("the batch is one round trip, not one per row");
        writeConnection.Transactions.Should().ContainSingle().Which.CommitCount.Should().Be(1);
        writeConnection.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task DeleteRangeAsync_FewerRowsAffectedThanEntities_SkipsTheDeletedAndCommits()
    {
        FakeDbConnection writeConnection = new() { NonQueryResult = 1 };
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateWriteConnection()).Returns(writeConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);

        await repository.DeleteRangeAsync([NewEntity(1), NewEntity(2)]);

        writeConnection.ExecutedCommands.Should().ContainSingle();
        writeConnection.Transactions.Should().ContainSingle().Which.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateRangeAsync_NoRowsAffected_ReturnsFalseAndDoesNotCommit()
    {
        FakeDbConnection writeConnection = new() { NonQueryResult = 0 };
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateWriteConnection()).Returns(writeConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);

        (await repository.UpdateRangeAsync([NewEntity(1), NewEntity(2)])).Should().BeFalse();

        writeConnection.Transactions.Should().ContainSingle().Which.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateRangeAsync_PartialRowCount_ThrowsAndDoesNotCommit()
    {
        FakeDbConnection writeConnection = new() { NonQueryResult = 1 };
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateWriteConnection()).Returns(writeConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);

        Func<Task> act = () => repository.UpdateRangeAsync([NewEntity(1), NewEntity(2)]);

        await act.Should().ThrowAsync<InvalidOperationException>();
        writeConnection.Transactions.Should().ContainSingle().Which.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateRangeAsync_TransactionActive_RunsInItOnItsConnection_WithoutCommitting()
    {
        FakeDbConnection writeConnection = new() { NonQueryResult = 2 };
        writeConnection.Open();
        DbTransaction transaction = writeConnection.BeginTransaction();
        Mock<IDapperConnectionFactory> factory = new();
        Mock<IDapperTransactionManager> transactionManager = new();
        transactionManager.Setup(t => t.GetCurrentTransaction()).Returns(transaction);
        TestRepository repository = new(factory.Object, transactionManager.Object);

        await repository.UpdateRangeAsync([NewEntity(1), NewEntity(2)]);

        writeConnection.ExecutedTransactions.Should().Equal([transaction]);
        writeConnection.Transactions.Should().ContainSingle().Which.CommitCount.Should().Be(0, "the Unit of Work commits");
        writeConnection.DisposeCount.Should().Be(0);
        factory.Verify(f => f.CreateWriteConnection(), Times.Never);
    }

    [Fact]
    public async Task RangeWrites_Empty_DoNothing()
    {
        Mock<IDapperConnectionFactory> factory = new();
        TestRepository repository = new(factory.Object, transactionManager: null);

        await repository.AddRangeAsync([]);
        await repository.UpdateRangeAsync([]);
        await repository.DeleteRangeAsync([]);

        factory.Verify(f => f.CreateWriteConnection(), Times.Never);
    }

    [Fact]
    public async Task FindExistingIdsAsync_Empty_DoesNotOpenConnection()
    {
        Mock<IDapperConnectionFactory> factory = new();
        TestRepository repository = new(factory.Object, transactionManager: null);

        IReadOnlyList<int> result = await repository.FindExistingIdsAsync(Array.Empty<int>(), false, CancellationToken.None);

        result.Should().BeEmpty();
        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetByIdAsync_UsesTheRepositorySelectAndActiveFilter()
    {
        FakeDbConnection readConnection = new();
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateReadConnection()).Returns(readConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);

        await repository.GetByIdAsync(7);

        readConnection.ExecutedCommands.Should().ContainSingle().Which.Should().Be(
            TestRepository.Select + @" WHERE ""id"" = @Id AND ""isactive"" = true");
    }

    [Fact]
    public async Task GetAllAsync_UsesTheRepositorySelectAndOrdersById()
    {
        FakeDbConnection readConnection = new();
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateReadConnection()).Returns(readConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);

        await repository.GetAllAsync();

        readConnection.ExecutedCommands.Should().ContainSingle().Which.Should().Be(
            TestRepository.Select + @" WHERE ""isactive"" = true ORDER BY ""id""");
    }

    [Fact]
    public async Task FindByIdsAsync_NoIds_ReturnsEmptyWithoutQuerying()
    {
        Mock<IDapperConnectionFactory> factory = new(MockBehavior.Strict);
        TestRepository repository = new(factory.Object, transactionManager: null);

        IReadOnlyList<TestEntity> result = await repository.FindByIdsAsync([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindByIdsAsync_UsesTheRepositorySelectAndActiveFilter()
    {
        FakeDbConnection readConnection = new();
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateReadConnection()).Returns(readConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);

        await repository.FindByIdsAsync([2, 4]);

        string sql = readConnection.ExecutedCommands.Should().ContainSingle().Subject;
        sql.Should().StartWith(TestRepository.Select + @" WHERE ""id"" = ANY(");
        sql.Should().EndWith(@") AND ""isactive"" = true");
    }

    [Fact]
    public async Task GetPagedAsync_PagePastEnd_DoesNotCountByDefault()
    {
        DataTable empty = new();
        empty.Columns.Add("id", typeof(int));
        FakeDbConnection readConnection = new() { ReaderResult = empty };
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateReadConnection()).Returns(readConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);

        var (items, nextId, totalCount) = await repository.GetPagedAsync(2, 1);

        items.Should().BeEmpty();
        nextId.Should().BeNull();
        totalCount.Should().BeNull();
        readConnection.ExecutedCommands.Should().ContainSingle().Which.Should().Contain("OFFSET");
    }

    [Fact]
    public async Task GetPagedAsync_PageWithRows_UsesTheRepositorySelect()
    {
        FakeDbConnection readConnection = new();
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateReadConnection()).Returns(readConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);

        var (items, nextId, totalCount) = await repository.GetPagedAsync(1, 20, includeTotal: true);

        items.Should().ContainSingle();
        nextId.Should().BeNull();
        totalCount.Should().Be(1);
        readConnection.ExecutedCommands.Should().ContainSingle().Which.Should().Be(
            TestRepository.Select + @" WHERE ""isactive"" = true ORDER BY ""id"" OFFSET @Offset ROWS FETCH NEXT @Take ROWS ONLY");
    }

    [Fact]
    public async Task GetPagedAfterAsync_ReturnsTheLastVisibleIdAndDoesNotCountByDefault()
    {
        DataTable rows = new();
        rows.Columns.Add("id", typeof(int));
        rows.Rows.Add(4);
        rows.Rows.Add(7);
        rows.Rows.Add(9);
        FakeDbConnection readConnection = new() { ReaderResult = rows };
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateReadConnection()).Returns(readConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);

        var page = await repository.GetPagedAfterAsync(2, 2);

        page.Items.Select(item => item.Id).Should().Equal(4, 7);
        page.NextAfterId.Should().Be(7);
        page.TotalCount.Should().BeNull();
        readConnection.ExecutedCommands.Should().ContainSingle().Which.Should().Contain(@"""id"" > @AfterId");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task FindExistingIdsAsync_NonEmpty_QueriesIdsAndAppliesTheActiveFilter(
        bool includeSoftDeleted,
        bool expectActiveFilter)
    {
        DataTable rows = new();
        rows.Columns.Add("id", typeof(int));
        rows.Rows.Add(2);
        rows.Rows.Add(1);
        FakeDbConnection readConnection = new() { ReaderResult = rows };
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateReadConnection()).Returns(readConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);

        IReadOnlyList<int> result = await repository.FindExistingIdsAsync([1, 2, 999], includeSoftDeleted);

        result.Should().Equal(2, 1);
        string sql = readConnection.ExecutedCommands.Should().ContainSingle().Subject;
        sql.Should().Contain(@"""id"" = ANY(");
        if (expectActiveFilter)
        {
            sql.Should().Contain(@"""isactive"" = true");
        }
        else
        {
            sql.Should().NotContain(@"""isactive"" = true");
        }
    }

    [Theory]
    [InlineData("add")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task RangeWrites_MoreThanRepositoryLimit_ThrowBeforeCreatingConnection(string operation)
    {
        Mock<IDapperConnectionFactory> factory = new();
        TestRepository repository = new(factory.Object, transactionManager: null);
        TestEntity[] entities = [.. Enumerable.Range(1, 1001).Select(NewEntity)];

        Func<Task> act = operation switch
        {
            "add" => () => repository.AddRangeAsync(entities),
            "update" => () => repository.UpdateRangeAsync(entities),
            _ => () => repository.DeleteRangeAsync(entities)
        };

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*A batch can contain at most 1000 items; this one has 1001.*");
        factory.Verify(f => f.CreateWriteConnection(), Times.Never);
    }

    [Fact]
    public async Task AddRangeAsync_ReturnedIdsOutOfOrder_AssignsThemByOrdinal()
    {
        FakeDbConnection writeConnection = new NpgsqlConnection { ReaderResult = InsertResults((2, 22), (1, 11)) };
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateWriteConnection()).Returns(writeConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);
        TestEntity[] entities = [NewEntity(0), NewEntity(0)];

        await repository.AddRangeAsync(entities);

        entities.Select(entity => entity.Id).Should().Equal(11, 22);
        writeConnection.ExecutedCommands.Single().Should().Contain("unnest(").And.NotContain(";\n");
        writeConnection.Transactions.Should().ContainSingle().Which.CommitCount.Should().Be(1);
    }

    public static TheoryData<int[]> InvalidInsertOrdinals => new()
    {
        new[] { 0, 2 },
        new[] { 1, 1 },
        new[] { 1, 3 },
        new[] { 1 }
    };

    [Theory]
    [MemberData(nameof(InvalidInsertOrdinals))]
    public async Task AddRangeAsync_InvalidOrdinal_ThrowsAndDoesNotCommit(int[] ordinals)
    {
        (int Ordinal, int Id)[] rows = [.. ordinals.Select((ordinal, index) => (ordinal, index + 10))];
        FakeDbConnection writeConnection = new() { ReaderResult = InsertResults(rows) };
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateWriteConnection()).Returns(writeConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);
        TestEntity[] entities = [NewEntity(0), NewEntity(0)];

        Func<Task> act = () => repository.AddRangeAsync(entities);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ordinal*");
        entities.Select(entity => entity.Id).Should().Equal(0, 0);
        writeConnection.Transactions.Should().ContainSingle().Which.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateRangeAsync_DifferentBatchSizes_UseTheSameCommandText()
    {
        FakeDbConnection first = new NpgsqlConnection { NonQueryResult = 2 };
        FakeDbConnection second = new NpgsqlConnection { NonQueryResult = 3 };
        Mock<IDapperConnectionFactory> factory = new();
        factory.SetupSequence(f => f.CreateWriteConnection()).Returns(first).Returns(second);
        TestRepository repository = new(factory.Object, transactionManager: null);

        await repository.UpdateRangeAsync([NewEntity(1), NewEntity(2)]);
        await repository.UpdateRangeAsync([NewEntity(1), NewEntity(2), NewEntity(3)]);

        first.ExecutedCommands.Should().ContainSingle();
        second.ExecutedCommands.Should().ContainSingle();
        second.ExecutedCommands.Single().Should().Be(first.ExecutedCommands.Single());
        second.ExecutedCommands.Single().Should().Contain("unnest(").And.NotContain(";\n");
    }

    [Fact]
    public async Task DeleteRangeAsync_DifferentBatchSizes_UseTheSameSetBasedCommandText()
    {
        FakeDbConnection first = new NpgsqlConnection { NonQueryResult = 2 };
        FakeDbConnection second = new NpgsqlConnection { NonQueryResult = 3 };
        Mock<IDapperConnectionFactory> factory = new();
        factory.SetupSequence(f => f.CreateWriteConnection()).Returns(first).Returns(second);
        TestRepository repository = new(factory.Object, transactionManager: null);

        await repository.DeleteRangeAsync([NewEntity(1), NewEntity(2)]);
        await repository.DeleteRangeAsync([NewEntity(1), NewEntity(2), NewEntity(3)]);

        first.ExecutedCommands.Should().ContainSingle();
        second.ExecutedCommands.Should().ContainSingle();
        second.ExecutedCommands.Single().Should().Be(first.ExecutedCommands.Single());
        second.ExecutedCommands.Single().Should()
            .Contain("unnest(@p0, @p1, @p2)")
            .And.NotContain(";\n");
    }

    [Theory]
    [InlineData("add")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task RangeWrite_CanceledDuringExecution_PropagatesAndRollsBack(string operation)
    {
        FakeDbConnection writeConnection = new NpgsqlConnection
        {
            NonQueryResult = 1,
            WaitForCancellationOnExecuteAsync = true
        };
        Mock<IDapperConnectionFactory> factory = new();
        factory.Setup(f => f.CreateWriteConnection()).Returns(writeConnection);
        TestRepository repository = new(factory.Object, transactionManager: null);
        using CancellationTokenSource cancellation = new();
        TestEntity entity = NewEntity(operation == "add" ? 0 : 1);

        Task operationTask = operation switch
        {
            "add" => repository.AddRangeAsync([entity], cancellation.Token),
            "update" => repository.UpdateRangeAsync([entity], cancellation.Token),
            _ => repository.DeleteRangeAsync([entity], cancellation.Token)
        };
        await writeConnection.ExecutionStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        await operationTask.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        writeConnection.Transactions.Should().ContainSingle().Which.Should().Match<FakeDbTransaction>(transaction =>
            transaction.CommitCount == 0 && transaction.DisposeCount == 1);
        writeConnection.DisposeCount.Should().Be(1);
        if (operation == "add")
        {
            entity.Id.Should().Be(0);
        }
    }

    private static DataTable InsertResults(params (int Ordinal, int Id)[] rows)
    {
        DataTable table = new();
        table.Columns.Add("Ordinal", typeof(int));
        table.Columns.Add("Id", typeof(int));
        foreach ((int ordinal, int id) in rows)
        {
            table.Rows.Add(ordinal, id);
        }

        return table;
    }

    private static TestEntity NewEntity(int id)
    {
        return new TestEntity { Id = id, Name = "e" + id, CreatedBy = 1, UpdatedBy = 1 };
    }

    [Fact]
    public void SetAuditFieldsForCreate_StampsTheAuthenticatedUser()
    {
        TestRepository repository = WithUser("42");
        TestEntity entity = new() { CreatedBy = 7, UpdatedBy = 7 };

        repository.ExposeSetAuditFieldsForCreate(entity);

        entity.CreatedBy.Should().Be(42);
        entity.UpdatedBy.Should().Be(42);
        entity.IsActive.Should().BeTrue();
    }

    [Fact]
    public void SetAuditFieldsForUpdate_StampsTheAuthenticatedUser_AndKeepsTheCreator()
    {
        TestRepository repository = WithUser("42");
        TestEntity entity = new() { CreatedBy = 7, UpdatedBy = 7 };

        repository.ExposeSetAuditFieldsForUpdate(entity);

        entity.UpdatedBy.Should().Be(42);
        entity.CreatedBy.Should().Be(7);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public void SetAuditFields_UserContextWithoutAUserId_Throws(string? userId)
    {
        // Never record 0 (or a caller-supplied value) for a write made in a request without a user.
        TestRepository repository = WithUser(userId);
        TestEntity entity = new() { CreatedBy = 7, UpdatedBy = 7 };

        repository.Invoking(r => r.ExposeSetAuditFieldsForCreate(entity)).Should().Throw<InvalidOperationException>();
        repository.Invoking(r => r.ExposeSetAuditFieldsForUpdate(entity)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetAuditFieldsForCreate_UsesInjectedClock()
    {
        ManualTimeProvider clock = new(new DateTimeOffset(2030, 6, 15, 12, 0, 0, TimeSpan.Zero));
        TestRepository repository = new(new Mock<IDapperConnectionFactory>().Object, transactionManager: null, timeProvider: clock);
        TestEntity entity = new();

        repository.ExposeSetAuditFieldsForCreate(entity);

        entity.CreatedAt.Should().Be(clock.UtcNow.UtcDateTime);
        entity.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void SetAuditFieldsForUpdate_UsesInjectedClock()
    {
        ManualTimeProvider clock = new(new DateTimeOffset(2030, 6, 15, 12, 0, 0, TimeSpan.Zero));
        TestRepository repository = new(new Mock<IDapperConnectionFactory>().Object, transactionManager: null, timeProvider: clock);
        TestEntity entity = new();

        repository.ExposeSetAuditFieldsForUpdate(entity);

        entity.UpdatedAt.Should().Be(clock.UtcNow.UtcDateTime);
    }

    private static TestRepository WithUser(string? userId)
    {
        return new TestRepository(new Mock<IDapperConnectionFactory>().Object, transactionManager: null, userContext: User(userId));
    }

    private static IUserContext User(string? userId)
    {
        Mock<IUserContext> userContext = new();
        userContext.Setup(u => u.GetUserId()).Returns(userId);
        return userContext.Object;
    }

    [Fact]
    public void QualifiedTableName_QuotesSchemaAndTable()
    {
        TestRepository repository = new(new Mock<IDapperConnectionFactory>().Object, transactionManager: null);

        repository.ExposeQualifiedTableName().Should().Be(@"""gss"".""test_entities""");
    }

    [Fact]
    public void QualifiedTableName_InvalidTableName_Throws()
    {
        InvalidTableRepository repository = new();

        Action act = () => repository.ExposeQualifiedTableName();

        act.Should().Throw<ArgumentException>().WithParameterName("TableName");
    }

    /// <summary>No transaction manager means one with no transaction; no user context means user 1.</summary>
    private sealed class TestRepository(
        IDapperConnectionFactory connectionFactory,
        IDapperTransactionManager? transactionManager,
        IUserContext? userContext = null,
        TimeProvider? timeProvider = null)
        : DapperRepositoryBase<TestEntity>(
            connectionFactory,
            transactionManager ?? new Mock<IDapperTransactionManager>().Object,
            NullLoggerFactory.Instance,
            timeProvider ?? TimeProvider.System,
            userContext ?? User("1"))
    {
        public const string Select = @"SELECT ""id"" AS Id, ""name"" AS Name FROM ""gss"".""test_entities""";

        protected override string TableName => "test_entities";

        protected override string SelectSql => Select;

        protected override IReadOnlyList<BatchColumn> BatchColumns =>
        [
            Column("name", entity => entity.Name)
        ];

        public string ExposeQualifiedTableName()
        {
            return QualifiedTableName;
        }

        public string ExposeSchema()
        {
            return Schema;
        }

        public Task<IReadOnlyList<int>> ExposeQueryAsync(string sql)
        {
            return QueryAsync<int>(sql, cancellationToken: CancellationToken.None);
        }

        public Task<int> ExposeQueryFirstOrDefaultAsync(string sql)
        {
            return QueryFirstOrDefaultAsync<int>(sql, cancellationToken: CancellationToken.None);
        }

        public void ExposeSetAuditFieldsForCreate(TestEntity entity)
        {
            SetAuditFieldsForCreate(entity);
        }

        public void ExposeSetAuditFieldsForUpdate(TestEntity entity)
        {
            SetAuditFieldsForUpdate(entity);
        }

        protected override string BuildInsertSql()
        {
            return @"INSERT INTO ""gss"".""test_entities"" (""name"") VALUES (@Name) RETURNING ""id""";
        }

        protected override string BuildUpdateSql()
        {
            return @"UPDATE ""gss"".""test_entities"" SET ""name"" = @Name WHERE ""id"" = @Id";
        }

    }

    private sealed class InvalidTableRepository()
        : DapperRepositoryBase<TestEntity>(
            new Mock<IDapperConnectionFactory>().Object,
            new Mock<IDapperTransactionManager>().Object,
            NullLoggerFactory.Instance,
            TimeProvider.System,
            User("1"))
    {
        protected override string TableName => "test_entities; DROP TABLE x";

        protected override string SelectSql => TestRepository.Select;

        protected override IReadOnlyList<BatchColumn> BatchColumns => [];

        public string ExposeQualifiedTableName() => QualifiedTableName;

        protected override string BuildInsertSql() => string.Empty;

        protected override string BuildUpdateSql() => string.Empty;
    }

    /// <summary>Dapper enables native array parameters by provider connection type name.</summary>
    private sealed class NpgsqlConnection : FakeDbConnection;
}
