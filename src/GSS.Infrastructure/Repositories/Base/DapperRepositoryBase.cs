using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Dapper;
using GSS.Core.Entities;
using GSS.Core.Helpers;
using GSS.Core.Interfaces.Base;
using GSS.Core.Models;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace GSS.Infrastructure.Repositories.Base;

/// <summary>
/// Shared Dapper reads by ID/all/page, writes and connection handling. Derived repositories supply
/// <see cref="SelectSql"/> and write their other reads.
/// </summary>
public abstract class DapperRepositoryBase<T>(
    IDapperConnectionFactory connectionFactory,
    IDapperTransactionManager transactionManager,
    ILoggerFactory loggerFactory,
    TimeProvider timeProvider,
    IUserContext userContext) where T : EntityBase
{
    /// <summary>
    /// The SQL of each repository type, built on first use and shared by every instance: repositories are scoped, so
    /// an instance cache would be rebuilt on every request. Keyed by the concrete type, not <typeparamref name="T"/>,
    /// since two repositories of one entity may differ.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, RepositorySql> SqlByType = new();

    // SQLSTATEs after which a whole transaction can safely run again.
    private const string SerializationFailure = "40001";
    private const string DeadlockDetected = "40P01";

    private readonly ILogger<DapperRepositoryBase<T>> _logger = loggerFactory.CreateLogger<DapperRepositoryBase<T>>();
    private RepositorySql? _sql;

    /// <summary>Table name (lower case). A constant: the SQL built from it is shared by every instance.</summary>
    protected abstract string TableName { get; }

    /// <summary>Schema name. A constant: the SQL built from it is shared by every instance.</summary>
    protected virtual string Schema => "gss";

    /// <summary><c>"schema"."table"</c>; both names are validated once per repository type.</summary>
    protected string QualifiedTableName => Sql.QualifiedTableName;

    /// <summary>
    /// <c>SELECT</c> of every column, aliased to <typeparamref name="T"/>'s properties, <c>FROM</c>
    /// <see cref="QualifiedTableName"/>. Every read starts with it, the shared ones below included. A constant: the
    /// SQL built from it is shared by every instance.
    /// </summary>
    protected abstract string SelectSql { get; }

    /// <summary>
    /// This repository type's SQL, built once from <see cref="TableName"/>, <see cref="Schema"/>,
    /// <see cref="SelectSql"/>, <see cref="BatchColumns"/>, <see cref="BuildInsertSql"/> and
    /// <see cref="BuildUpdateSql"/>, which must therefore not depend on instance state (<c>RepositorySqlTests</c>).
    /// </summary>
    protected RepositorySql Sql => _sql ??= SqlByType.GetOrAdd(GetType(), static (_, self) => self.BuildSql(), this);

    /// <summary>The active entity with the ID, or null.</summary>
    public virtual Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return QueryFirstOrDefaultAsync<T>(Sql.GetById, new { Id = id }, cancellationToken);
    }

    /// <summary>All active entities, by ID.</summary>
    public virtual Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync<T>(Sql.GetAll, cancellationToken: cancellationToken);
    }

    /// <summary>One page (1-based, OFFSET) of active entities by ID; the total only when <paramref name="includeTotal"/>.</summary>
    public virtual Task<PageSlice<T>> GetPagedAsync(int pageNumber, int pageSize, bool includeTotal = false, CancellationToken cancellationToken = default)
    {
        return QueryPageAsync(Sql.Count, Sql.Page, pageNumber, pageSize, includeTotal, cancellationToken);
    }

    /// <summary>The active entities after <paramref name="afterId"/> by ID (keyset); the total only when <paramref name="includeTotal"/>.</summary>
    public virtual Task<PageSlice<T>> GetPagedAfterAsync(int afterId, int pageSize, bool includeTotal = false, CancellationToken cancellationToken = default)
    {
        return QueryPageAfterAsync(Sql.Count, Sql.PageAfter, afterId, pageSize, includeTotal, cancellationToken);
    }

    /// <summary>The active entities with the IDs, in any order; missing IDs are skipped.</summary>
    public virtual async Task<IReadOnlyList<T>> FindByIdsAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return Array.Empty<T>();
        }

        return await QueryAsync<T>(Sql.FindByIds, new { Ids = ids.ToArray() }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs an ID query that takes the IDs as <c>@Ids</c> (<c>= ANY(@Ids)</c>), e.g. "which of these parents have an
    /// active child". An empty list runs no query.
    /// </summary>
    protected async Task<IReadOnlyList<int>> QueryIdsAsync(string sql, IReadOnlyList<int> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        return await QueryAsync<int>(sql, new { Ids = ids.ToArray() }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rows of a read query, buffered. Connection and transaction: <see cref="ReadAsync{TResult}"/>.</summary>
    protected Task<IReadOnlyList<TRow>> QueryAsync<TRow>(string sql, object? param = null, CancellationToken cancellationToken = default)
    {
        return ReadAsync<IReadOnlyList<TRow>>(async (connection, transaction) =>
            (await connection.QueryAsync<TRow>(new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).AsList());
    }

    /// <summary>First row of a read query, or default. Connection and transaction: <see cref="ReadAsync{TResult}"/>.</summary>
    protected Task<TRow?> QueryFirstOrDefaultAsync<TRow>(string sql, object? param = null, CancellationToken cancellationToken = default)
    {
        return ReadAsync((connection, transaction) =>
            connection.QueryFirstOrDefaultAsync<TRow>(new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken)));
    }

    /// <summary>
    /// Runs a read. Inside a Unit of Work transaction: on the transaction's connection, enlisted, so it sees the
    /// transaction's uncommitted writes; the connection stays open (the transaction owns it) and serves one call at a
    /// time (<see cref="IDapperTransactionManager.EnterCommand"/>). Otherwise: on a new read connection, disposed
    /// afterwards.
    /// </summary>
    private async Task<TResult> ReadAsync<TResult>(Func<IDbConnection, IDbTransaction?, Task<TResult>> read)
    {
        var transaction = transactionManager.GetCurrentTransaction();
        if (transaction?.Connection is IDbConnection transactionConnection)
        {
            _logger.ReadingInTransaction(typeof(T).Name);
            using (transactionManager.EnterCommand())
            {
                return await read(transactionConnection, transaction).ConfigureAwait(false);
            }
        }

        _logger.CreatingReadConnection(typeof(T).Name);
        var connection = connectionFactory.CreateReadConnection();
        await using (connection.ConfigureAwait(false))
        {
            return await read(connection, null).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs a single-statement write. Inside a Unit of Work transaction: on its connection, one call at a time
    /// (<see cref="IDapperTransactionManager.EnterCommand"/>), left open for the transaction. Otherwise: on a new write
    /// connection, disposed afterwards.
    /// </summary>
    private async Task<TResult> WriteAsync<TResult>(Func<IDbConnection, IDbTransaction?, Task<TResult>> write)
    {
        var transaction = transactionManager.GetCurrentTransaction();
        if (transaction?.Connection is IDbConnection transactionConnection)
        {
            _logger.ReusingTransactionConnection(typeof(T).Name);
            using (transactionManager.EnterCommand())
            {
                return await write(transactionConnection, transaction).ConfigureAwait(false);
            }
        }

        _logger.CreatingWriteConnection(typeof(T).Name);
        var connection = connectionFactory.CreateWriteConnection();
        await using (connection.ConfigureAwait(false))
        {
            return await write(connection, null).ConfigureAwait(false);
        }
    }

    /// <summary>Sets CreatedAt, IsActive, and CreatedBy/UpdatedBy to the acting user (<see cref="AuditUserId"/>).</summary>
    protected void SetAuditFieldsForCreate(T entity)
    {
        int userId = AuditUserId();
        entity.CreatedAt = timeProvider.GetUtcNow().UtcDateTime;
        entity.IsActive = true;
        entity.CreatedBy = userId;
        entity.UpdatedBy = userId;
    }

    /// <summary>Sets UpdatedAt and UpdatedBy to the acting user (<see cref="AuditUserId"/>).</summary>
    protected void SetAuditFieldsForUpdate(T entity)
    {
        int userId = AuditUserId();
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        entity.UpdatedBy = userId;
    }

    /// <summary>The acting user's ID from <see cref="IUserContext"/>. Never 0: an unknown user throws.</summary>
    /// <exception cref="InvalidOperationException">No positive numeric user ID is available.</exception>
    private int AuditUserId()
    {
        string? userId = userContext.GetUserId();
        return int.TryParse(userId, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0
            ? id
            : throw new InvalidOperationException(
                $"{typeof(T).Name} write without an authenticated user: CreatedBy/UpdatedBy need the caller's numeric user ID. A background writer must register its own IUserContext.");
    }

    /// <summary>Inserts the entity and sets its Id.</summary>
    public virtual async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        SetAuditFieldsForCreate(entity);

        entity.Id = await WriteAsync((connection, transaction) => connection.QuerySingleAsync<int>(
            new CommandDefinition(Sql.Insert, entity, transaction, cancellationToken: cancellationToken))).ConfigureAwait(false);

        _logger.Added(typeof(T).Name, entity.Id);
        return entity;
    }

    /// <summary>Updates the entity; false if it is no longer active (deleted by a concurrent request).</summary>
    public virtual async Task<bool> UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        SetAuditFieldsForUpdate(entity);

        int rowsAffected = await WriteAsync((connection, transaction) => connection.ExecuteAsync(
            new CommandDefinition(Sql.Update, entity, transaction, cancellationToken: cancellationToken))).ConfigureAwait(false);

        if (rowsAffected == 0)
        {
            return false;
        }

        _logger.Updated(typeof(T).Name, entity.Id);
        return true;
    }

    /// <summary>Soft-deletes (IsActive = false). A row already deleted (e.g. by a concurrent request) stays deleted.</summary>
    public virtual async Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        SetAuditFieldsForUpdate(entity);

        int rowsAffected = await WriteAsync((connection, transaction) => connection.ExecuteAsync(
            new CommandDefinition(Sql.SoftDelete, entity, transaction, cancellationToken: cancellationToken))).ConfigureAwait(false);

        entity.IsActive = false;
        if (rowsAffected > 0)
        {
            _logger.SoftDeleted(typeof(T).Name, entity.Id);
        }
    }

    /// <summary>
    /// Inserts the entities with one set-based statement and sets their Ids. All or none.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">More than <see cref="BatchLimits.MaxBatchSize"/> entities.</exception>
    public virtual async Task AddRangeAsync(IReadOnlyList<T> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ValidateBatchSize(entities);
        if (entities.Count == 0)
        {
            return;
        }

        foreach (var entity in entities)
        {
            SetAuditFieldsForCreate(entity);
        }

        var parameters = BatchParameters(Sql.InsertColumns, entities);
        int[] ids = new int[entities.Count];
        await WriteBatchAsync(async (connection, transaction) =>
        {
            var rows = await connection.QueryAsync<(int Ordinal, int Id)>(
                new CommandDefinition(Sql.BatchInsert, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            MapInsertedIds(rows.AsList(), ids);
            return true;
        }, cancellationToken).ConfigureAwait(false);

        for (int i = 0; i < ids.Length; i++)
        {
            entities[i].Id = ids[i];
        }

        _logger.AddedRange(entities.Count, typeof(T).Name);
    }

    /// <summary>
    /// Updates the entities with one set-based statement, all or nothing: false, changing nothing, if any is missing,
    /// no longer active (deleted by a concurrent request) or repeated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">More than <see cref="BatchLimits.MaxBatchSize"/> entities.</exception>
    public virtual async Task<bool> UpdateRangeAsync(IReadOnlyList<T> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ValidateBatchSize(entities);
        if (entities.Count == 0)
        {
            return true;
        }

        foreach (var entity in entities)
        {
            SetAuditFieldsForUpdate(entity);
        }

        return await ExecuteRangeAsync(Sql.BatchUpdate, Sql.UpdateColumns, entities, "updated", allOrNothing: true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Soft-deletes the entities with one set-based statement; rows already deleted (e.g. by a concurrent request) are skipped.</summary>
    /// <exception cref="ArgumentOutOfRangeException">More than <see cref="BatchLimits.MaxBatchSize"/> entities.</exception>
    public virtual async Task DeleteRangeAsync(IReadOnlyList<T> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ValidateBatchSize(entities);
        if (entities.Count == 0)
        {
            return;
        }

        foreach (var entity in entities)
        {
            SetAuditFieldsForUpdate(entity);
        }

        await ExecuteRangeAsync(Sql.BatchDelete, DeleteBatchColumns, entities, "deleted", allOrNothing: false, cancellationToken)
            .ConfigureAwait(false);
        foreach (var entity in entities)
        {
            entity.IsActive = false;
        }
    }

    /// <summary>
    /// Runs one set-based UPDATE. All or nothing: the statement writes every row or none (see
    /// <see cref="BuildBatchUpdateSql"/>), so false means none; any other partial count is a bug and throws.
    /// </summary>
    private async Task<bool> ExecuteRangeAsync(
        string sql,
        IReadOnlyList<BatchColumn> columns,
        IReadOnlyList<T> entities,
        string action,
        bool allOrNothing,
        CancellationToken cancellationToken)
    {
        var parameters = BatchParameters(columns, entities);
        int rowsAffected = 0;
        bool written = await WriteBatchAsync(async (connection, transaction) =>
        {
            rowsAffected = await connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (!allOrNothing || rowsAffected == entities.Count)
            {
                return true;
            }

            return rowsAffected == 0
                ? false
                : throw new InvalidOperationException(
                    $"Batch {action} wrote {rowsAffected} of {entities.Count} {typeof(T).Name} rows; it must write all or none.");
        }, cancellationToken).ConfigureAwait(false);

        if (written)
        {
            _logger.BatchWritten(action, rowsAffected, typeof(T).Name);
        }

        return written;
    }

    /// <exception cref="ArgumentOutOfRangeException">
    /// More than <see cref="BatchLimits.MaxBatchSize"/> entities: a bug (a 500), since the service batch methods return a
    /// Failure first.
    /// </exception>
    private static void ValidateBatchSize(IReadOnlyList<T> entities)
    {
        if (entities.Count > BatchLimits.MaxBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(entities), entities.Count, BatchLimits.TooLarge(entities.Count));
        }
    }

    /// <summary>Assigns each returned Id to its input position; every ordinal 1..n must appear exactly once.</summary>
    private static void MapInsertedIds(IReadOnlyList<(int Ordinal, int Id)> rows, int[] ids)
    {
        bool[] seen = new bool[ids.Length];
        foreach ((int ordinal, int id) in rows)
        {
            int index = ordinal - 1;
            if ((uint)index >= (uint)ids.Length || seen[index])
            {
                throw InvalidInsertResult(rows.Count, ids.Length);
            }

            seen[index] = true;
            ids[index] = id;
        }

        if (rows.Count != ids.Length)
        {
            throw InvalidInsertResult(rows.Count, ids.Length);
        }
    }

    private static InvalidOperationException InvalidInsertResult(int returned, int expected)
    {
        return new InvalidOperationException(
            $"Batch insert of {typeof(T).Name} returned an invalid ordinal mapping: {returned} rows for {expected} entities.");
    }

    /// <summary>
    /// Runs a batch write inside the Unit of Work transaction (its owner commits or rolls back), or else in a
    /// transaction of its own on a new write connection, committed only if <paramref name="write"/> returns true. Its own
    /// transaction is retried once on a deadlock or serialization failure. Returns what <paramref name="write"/> returned.
    /// </summary>
    private async Task<bool> WriteBatchAsync(Func<IDbConnection, IDbTransaction, Task<bool>> write, CancellationToken cancellationToken)
    {
        var current = transactionManager.GetCurrentTransaction();
        if (current?.Connection is IDbConnection transactionConnection)
        {
            // Not retried: the failure aborts the owner's whole transaction.
            using (transactionManager.EnterCommand())
            {
                return await write(transactionConnection, current).ConfigureAwait(false);
            }
        }

        try
        {
            return await WriteInOwnTransactionAsync(write, cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex) when (ex.SqlState is DeadlockDetected or SerializationFailure)
        {
            // Rolled back in full, so running it again is safe. Once only: a second failure reaches the client (409).
            _logger.BatchRetried(typeof(T).Name, ex.SqlState);
            return await WriteInOwnTransactionAsync(write, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Commits only when <paramref name="write"/> returns true; otherwise disposing rolls back.</summary>
    private async Task<bool> WriteInOwnTransactionAsync(Func<IDbConnection, IDbTransaction, Task<bool>> write, CancellationToken cancellationToken)
    {
        var connection = connectionFactory.CreateWriteConnection();
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                if (!await write(connection, transaction).ConfigureAwait(false))
                {
                    return false;
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
        }
    }

    /// <summary>True if the ID exists.</summary>
    public virtual async Task<bool> ExistsAsync(int id, bool includeSoftDeleted = false, CancellationToken cancellationToken = default)
    {
        string sql = includeSoftDeleted ? Sql.ExistsIncludingDeleted : Sql.Exists;
        return await QueryFirstOrDefaultAsync<bool>(sql, new { Id = id }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The given IDs that exist, in one query.</summary>
    public virtual async Task<IReadOnlyList<int>> FindExistingIdsAsync(
        IReadOnlyList<int> ids,
        bool includeSoftDeleted = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return Array.Empty<int>();
        }

        string sql = includeSoftDeleted ? Sql.FindExistingIdsIncludingDeleted : Sql.FindExistingIds;
        return await QueryAsync<int>(sql, new { Ids = ids.ToArray() }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One OFFSET page (1-based). Cost grows with the offset: prefer <see cref="QueryPageAfterAsync"/> for deep or
    /// unbounded scrolling. Page number and size are clamped to <see cref="PageLimits"/>.
    /// </summary>
    /// <param name="countSql">Counts the rows of the same filter as <paramref name="pageSql"/>; run only for the total.</param>
    /// <param name="pageSql">Select ordered by <c>"id"</c>, ending in <c>OFFSET @Offset ROWS FETCH NEXT @Take ROWS ONLY</c>.</param>
    protected Task<PageSlice<T>> QueryPageAsync(
        string countSql,
        string pageSql,
        int pageNumber,
        int pageSize,
        bool includeTotal,
        CancellationToken cancellationToken)
    {
        pageSize = ClampPageSize(pageSize);
        long offset = (long)(Math.Max(1, pageNumber) - 1) * pageSize;
        return QuerySliceAsync(countSql, pageSql, new { Offset = offset, Take = pageSize + 1 }, pageSize, includeTotal, rowsBefore: offset, cancellationToken);
    }

    /// <summary>
    /// One keyset page: the rows after <paramref name="afterId"/> (0 for the first page). An index seek at any depth.
    /// Page size is clamped to <see cref="PageLimits"/>.
    /// </summary>
    /// <param name="countSql">Counts the rows of the same filter as <paramref name="pageSql"/>; run only for the total.</param>
    /// <param name="pageSql">Select with <c>"id" &gt; @AfterId</c>, ordered by <c>"id"</c>, ending in <c>FETCH FIRST @Take ROWS ONLY</c>.</param>
    protected Task<PageSlice<T>> QueryPageAfterAsync(
        string countSql,
        string pageSql,
        int afterId,
        int pageSize,
        bool includeTotal,
        CancellationToken cancellationToken)
    {
        pageSize = ClampPageSize(pageSize);
        afterId = Math.Max(0, afterId);
        return QuerySliceAsync(countSql, pageSql, new { AfterId = afterId, Take = pageSize + 1 }, pageSize, includeTotal,
            rowsBefore: afterId == 0 ? 0 : null, cancellationToken);
    }

    private static int ClampPageSize(int pageSize)
    {
        return Math.Clamp(pageSize, PageLimits.MinPageSize, PageLimits.MaxPageSize);
    }

    /// <summary>
    /// Reads pageSize + 1 rows (the extra one only tells whether more follow). COUNT runs only for the total, and not
    /// even then when the page is the last one and the rows before it are known (<paramref name="rowsBefore"/>).
    /// </summary>
    private Task<PageSlice<T>> QuerySliceAsync(
        string countSql,
        string pageSql,
        object parameters,
        int pageSize,
        bool includeTotal,
        long? rowsBefore,
        CancellationToken cancellationToken)
    {
        return ReadAsync(async (connection, transaction) =>
        {
            var rows = (await connection.QueryAsync<T>(
                new CommandDefinition(pageSql, parameters, transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).AsList();

            bool hasMore = rows.Count > pageSize;
            if (hasMore)
            {
                rows.RemoveAt(pageSize);
            }

            int? total = null;
            if (includeTotal)
            {
                // A last page that is not past the end gives the total without COUNT.
                total = !hasMore && rowsBefore is { } before && (rows.Count > 0 || before == 0)
                    ? (int)(before + rows.Count)
                    : await connection.QuerySingleAsync<int>(
                        new CommandDefinition(countSql, transaction: transaction, cancellationToken: cancellationToken))
                        .ConfigureAwait(false);
            }

            return new PageSlice<T>(rows, hasMore ? rows[^1].Id : null, total);
        });
    }

    /// <summary>INSERT statement for the entity's columns. A constant, read once per repository type.</summary>
    protected abstract string BuildInsertSql();

    /// <summary>UPDATE statement for the entity's columns. A constant, read once per repository type.</summary>
    protected abstract string BuildUpdateSql();

    /// <summary>
    /// Columns written by <see cref="AddRangeAsync"/> and <see cref="UpdateRangeAsync"/>: the same columns as
    /// <see cref="BuildInsertSql"/> without <c>id</c> and the audit columns, which the base adds. Expression-bodied
    /// (<c>=&gt; [...]</c>), never <c>{ get; } = [...]</c>: it is read once per repository type, while an initializer
    /// would build the list in every (per-request) instance.
    /// </summary>
    protected abstract IReadOnlyList<BatchColumn> BatchColumns { get; }

    /// <summary>The SQL the base runs for one repository type.</summary>
    protected sealed record RepositorySql(
        string QualifiedTableName,
        string GetById,
        string GetAll,
        string Count,
        string Page,
        string PageAfter,
        string FindByIds,
        string Exists,
        string ExistsIncludingDeleted,
        string FindExistingIds,
        string FindExistingIdsIncludingDeleted,
        string Insert,
        string Update,
        string SoftDelete,
        IReadOnlyList<BatchColumn> InsertColumns,
        IReadOnlyList<BatchColumn> UpdateColumns,
        string BatchInsert,
        string BatchUpdate,
        string BatchDelete);

    /// <summary>Builds and validates this repository type's SQL; runs once per type (see <see cref="Sql"/>).</summary>
    private RepositorySql BuildSql()
    {
        string table = $@"""{SqlIdentifierValidator.Validate(Schema, nameof(Schema))}"".""{SqlIdentifierValidator.Validate(TableName, nameof(TableName))}""";
        string select = SelectSql;
        var columns = BatchColumns;
        IReadOnlyList<BatchColumn> insertColumns =
        [
            .. columns,
            Column("isactive", entity => entity.IsActive),
            Column("created", entity => entity.CreatedAt),
            Column("createdby", entity => entity.CreatedBy),
            Column("updatedby", entity => entity.UpdatedBy)
        ];
        IReadOnlyList<BatchColumn> updateColumns =
        [
            Column("id", entity => entity.Id),
            .. columns,
            Column("updated", entity => entity.UpdatedAt),
            Column("updatedby", entity => entity.UpdatedBy)
        ];

        return new RepositorySql(
            QualifiedTableName: table,
            GetById: select + @" WHERE ""id"" = @Id AND ""isactive"" = true",
            GetAll: select + @" WHERE ""isactive"" = true ORDER BY ""id""",
            Count: $@"SELECT COUNT(*) FROM {table} WHERE ""isactive"" = true",
            Page: select + @" WHERE ""isactive"" = true ORDER BY ""id"" OFFSET @Offset ROWS FETCH NEXT @Take ROWS ONLY",
            PageAfter: select + @" WHERE ""isactive"" = true AND ""id"" > @AfterId ORDER BY ""id"" FETCH FIRST @Take ROWS ONLY",
            FindByIds: select + @" WHERE ""id"" = ANY(@Ids) AND ""isactive"" = true",
            Exists: $@"SELECT EXISTS(SELECT 1 FROM {table} WHERE ""id"" = @Id AND ""isactive"" = true)",
            ExistsIncludingDeleted: $@"SELECT EXISTS(SELECT 1 FROM {table} WHERE ""id"" = @Id)",
            FindExistingIds: $@"SELECT ""id"" FROM {table} WHERE ""id"" = ANY(@Ids) AND ""isactive"" = true",
            FindExistingIdsIncludingDeleted: $@"SELECT ""id"" FROM {table} WHERE ""id"" = ANY(@Ids)",
            Insert: BuildInsertSql(),
            Update: BuildUpdateSql(),
            SoftDelete: $@"
            UPDATE {table}
            SET ""isactive"" = false, ""updated"" = @UpdatedAt, ""updatedby"" = @UpdatedBy
            WHERE ""id"" = @Id AND ""isactive"" = true",
            InsertColumns: insertColumns,
            UpdateColumns: updateColumns,
            BatchInsert: BuildBatchInsertSql(table, insertColumns),
            BatchUpdate: BuildBatchUpdateSql(table, updateColumns, string.Empty, allOrNothing: true),
            BatchDelete: BuildBatchUpdateSql(table, DeleteBatchColumns, @"""isactive"" = false, ", allOrNothing: false));
    }

    /// <summary>
    /// A column written by batch inserts and updates. <see cref="Values"/> gives the batch's values as one typed array
    /// (one array parameter); <see cref="SqlType"/> casts it when its type is not the column's (enum, range).
    /// </summary>
    protected sealed record BatchColumn(string Name, Func<IReadOnlyList<T>, Array> Values, string? SqlType = null);

    /// <summary>Batch column whose values are <paramref name="value"/> of each entity.</summary>
    protected static BatchColumn Column<TValue>(string name, Func<T, TValue> value, string? sqlType = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new BatchColumn(name, entities => entities.Select(value).ToArray(), sqlType);
    }

    private static readonly IReadOnlyList<BatchColumn> DeleteBatchColumns =
    [
        Column("id", entity => entity.Id),
        Column("updated", entity => entity.UpdatedAt),
        Column("updatedby", entity => entity.UpdatedBy)
    ];

    /// <summary>
    /// Inserts every row of the arrays; returns each row's 1-based input position and new Id. Ids are drawn from the
    /// id column's sequence before the insert, so the mapping never depends on RETURNING order.
    /// </summary>
    private static string BuildBatchInsertSql(string table, IReadOnlyList<BatchColumn> columns)
    {
        ValidateBatchColumns(columns);
        return $@"
            WITH input AS MATERIALIZED (
                SELECT nextval(pg_get_serial_sequence('{table}', 'id'))::integer AS id, source.*
                FROM unnest({BatchParameterList(columns)}) WITH ORDINALITY AS source({BatchAliasList(columns)}, ordinal)
            ),
            inserted AS (
                INSERT INTO {table} (""id"", {string.Join(", ", columns.Select(c => $@"""{c.Name}"""))})
                SELECT id, {string.Join(", ", columns.Select((c, i) => BatchValue(c, $"v{i}")))}
                FROM input
                RETURNING ""id""
            )
            SELECT input.ordinal::integer AS ""Ordinal"", input.id AS ""Id""
            FROM input
            INNER JOIN inserted USING (id)";
    }

    /// <summary>
    /// Updates (or soft-deletes) the row of each array entry (column 0 is the id); active rows only. The active rows are
    /// first locked in id order (the count is a one-time filter, run before the update), so overlapping batches never
    /// deadlock and a row deleted meanwhile is seen as gone. <paramref name="allOrNothing"/>: writes nothing unless every
    /// id is active and distinct; otherwise rows already deleted are skipped.
    /// </summary>
    private static string BuildBatchUpdateSql(string table, IReadOnlyList<BatchColumn> columns, string extraAssignments, bool allOrNothing)
    {
        ValidateBatchColumns(columns);
        string assignments = string.Join(", ", columns.Select((c, i) => (c, i)).Skip(1)
            .Select(x => $@"""{x.c.Name}"" = {BatchValue(x.c, $"input.v{x.i}")}"));
        string gate = allOrNothing ? "= cardinality(@p0)" : "> 0";
        return $@"
            WITH locked AS (
                SELECT ""id"" FROM {table}
                WHERE ""id"" = ANY(@p0) AND ""isactive"" = true
                ORDER BY ""id""
                FOR NO KEY UPDATE
            )
            UPDATE {table} AS target
            SET {extraAssignments}{assignments}
            FROM unnest({BatchParameterList(columns)}) AS input({BatchAliasList(columns)})
            WHERE target.""id"" = input.v0 AND target.""isactive"" = true
                AND (SELECT count(*) FROM locked) {gate}";
    }

    private static string BatchParameterList(IReadOnlyList<BatchColumn> columns)
    {
        return string.Join(", ", columns.Select((_, i) => $"@p{i}"));
    }

    private static string BatchAliasList(IReadOnlyList<BatchColumn> columns)
    {
        return string.Join(", ", columns.Select((_, i) => $"v{i}"));
    }

    private static string BatchValue(BatchColumn column, string alias)
    {
        return column.SqlType == null ? alias : $"CAST({alias} AS {column.SqlType})";
    }

    /// <exception cref="InvalidOperationException">A column name or cast is not a plain identifier, or a column repeats.</exception>
    private static void ValidateBatchColumns(IReadOnlyList<BatchColumn> columns)
    {
        foreach (var column in columns)
        {
            SqlIdentifierValidator.Validate(column.Name, nameof(BatchColumns));
            if (column.SqlType != null)
            {
                SqlIdentifierValidator.Validate(column.SqlType.Replace(".", string.Empty, StringComparison.Ordinal), nameof(BatchColumn.SqlType));
            }
        }

        string? repeated = columns.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (repeated != null)
        {
            throw new InvalidOperationException($"Batch column '{repeated}' of {typeof(T).Name} appears more than once.");
        }
    }

    /// <summary>One array parameter per column, in column order (<c>@p0</c>, <c>@p1</c>, ...).</summary>
    private static DynamicParameters BatchParameters(IReadOnlyList<BatchColumn> columns, IReadOnlyList<T> entities)
    {
        DynamicParameters parameters = new();
        for (int i = 0; i < columns.Count; i++)
        {
            parameters.Add($"p{i}", new ArrayParameter(columns[i].Values(entities)));
        }

        return parameters;
    }

    /// <summary>
    /// Binds an array as one parameter. Dapper expands a plain array into <c>(@p1, @p2, ...)</c> unless it recognises the
    /// connection as Npgsql; this binds it as is on any connection.
    /// </summary>
    private sealed class ArrayParameter(Array values) : SqlMapper.ICustomQueryParameter
    {
        public void AddParameter(IDbCommand command, string name)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = values;
            command.Parameters.Add(parameter);
        }
    }
}
