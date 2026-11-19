using System.Data;
using Dapper;
using GSS.Core.Interfaces;
using GSS.Core.Models;
using GSS.Infrastructure.Interfaces;

namespace GSS.Infrastructure.Repositories;

/// <summary>
/// <see cref="IIdempotencyRepository"/> over Dapper. Lock, read and save run on the Unit of Work's connection, so the
/// stored response commits with the create it belongs to.
/// </summary>
public sealed class IdempotencyRepository(
    IDapperConnectionFactory connectionFactory,
    IDapperTransactionManager transactionManager) : IIdempotencyRepository
{
    // The prefix keeps the lock key apart from any other advisory lock in the database.
    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended(@LockKey, 0))";

    private const string FindSql = @"SELECT ""userid"" AS UserId, ""idempotencykey"" AS Key, ""requesthash"" AS RequestHash,
       ""statuscode"" AS StatusCode, ""responsebody"" AS ResponseBody, ""location"" AS Location
FROM gss.idempotencykey
WHERE ""userid"" = @UserId AND ""idempotencykey"" = @Key AND ""createdat"" >= @SavedAfter";

    private const string SaveSql = @"INSERT INTO gss.idempotencykey
    (""userid"", ""idempotencykey"", ""requesthash"", ""statuscode"", ""responsebody"", ""location"", ""createdat"")
VALUES (@UserId, @Key, @RequestHash, @StatusCode, @ResponseBody, @Location, @CreatedAt)
ON CONFLICT (""userid"", ""idempotencykey"") DO UPDATE SET
    ""requesthash"" = EXCLUDED.""requesthash"", ""statuscode"" = EXCLUDED.""statuscode"",
    ""responsebody"" = EXCLUDED.""responsebody"", ""location"" = EXCLUDED.""location"", ""createdat"" = EXCLUDED.""createdat""";

    private const string DeleteExpiredSql = @"DELETE FROM gss.idempotencykey
WHERE (""userid"", ""idempotencykey"") IN (
    SELECT ""userid"", ""idempotencykey"" FROM gss.idempotencykey WHERE ""createdat"" < @CreatedBefore LIMIT @BatchSize)";

    /// <inheritdoc />
    public Task LockKeyAsync(int userId, string key, CancellationToken cancellationToken = default)
    {
        return InTransactionAsync((connection, transaction) => connection.ExecuteAsync(
            new CommandDefinition(LockSql, new { LockKey = $"idempotency:{userId}:{key}" }, transaction, cancellationToken: cancellationToken)));
    }

    /// <inheritdoc />
    public Task<IdempotencyRecord?> FindAsync(int userId, string key, DateTime savedAfterUtc, CancellationToken cancellationToken = default)
    {
        return InTransactionAsync((connection, transaction) => connection.QueryFirstOrDefaultAsync<IdempotencyRecord>(
            new CommandDefinition(FindSql, new { UserId = userId, Key = key, SavedAfter = savedAfterUtc }, transaction, cancellationToken: cancellationToken)));
    }

    /// <inheritdoc />
    public Task SaveAsync(IdempotencyRecord record, DateTime createdAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return InTransactionAsync((connection, transaction) => connection.ExecuteAsync(
            new CommandDefinition(SaveSql, new
            {
                record.UserId,
                record.Key,
                record.RequestHash,
                record.StatusCode,
                record.ResponseBody,
                record.Location,
                CreatedAt = createdAtUtc
            }, transaction, cancellationToken: cancellationToken)));
    }

    /// <inheritdoc />
    public async Task<int> DeleteExpiredAsync(DateTime createdBeforeUtc, int batchSize, CancellationToken cancellationToken = default)
    {
        // A background job: its own connection, never the request's transaction.
        var connection = connectionFactory.CreateWriteConnection();
        await using (connection.ConfigureAwait(false))
        {
            return await connection.ExecuteAsync(new CommandDefinition(
                DeleteExpiredSql, new { CreatedBefore = createdBeforeUtc, BatchSize = batchSize }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }
    }

    /// <summary>Runs one call on the Unit of Work transaction's connection; there is no useful call outside it.</summary>
    private async Task<TResult> InTransactionAsync<TResult>(Func<IDbConnection, IDbTransaction, Task<TResult>> call)
    {
        var transaction = transactionManager.GetCurrentTransaction();
        if (transaction?.Connection is not IDbConnection connection)
        {
            throw new InvalidOperationException(
                "Idempotency keys are locked, read and saved inside the request's Unit of Work transaction; none is open.");
        }

        using (transactionManager.EnterCommand())
        {
            return await call(connection, transaction).ConfigureAwait(false);
        }
    }
}
