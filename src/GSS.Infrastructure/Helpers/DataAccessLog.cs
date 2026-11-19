using System.Data;
using Microsoft.Extensions.Logging;

namespace GSS.Infrastructure.Helpers;

/// <summary>
/// Source-generated log messages for the data-access hot path (repositories, connections, transactions):
/// no allocation or boxing when the level is off.
/// </summary>
internal static partial class DataAccessLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Reading {EntityType} inside the current transaction")]
    public static partial void ReadingInTransaction(this ILogger logger, string entityType);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Creating read connection for {EntityType}")]
    public static partial void CreatingReadConnection(this ILogger logger, string entityType);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Reusing transaction connection for {EntityType}")]
    public static partial void ReusingTransactionConnection(this ILogger logger, string entityType);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "Creating write connection for {EntityType}")]
    public static partial void CreatingWriteConnection(this ILogger logger, string entityType);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Debug, Message = "Added {EntityType} with Id {Id}")]
    public static partial void Added(this ILogger logger, string entityType, int id);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Debug, Message = "Updated {EntityType} with Id {Id}")]
    public static partial void Updated(this ILogger logger, string entityType, int id);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Debug, Message = "Soft deleted {EntityType} with Id {Id}")]
    public static partial void SoftDeleted(this ILogger logger, string entityType, int id);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Debug, Message = "Added {Count} {EntityType} rows")]
    public static partial void AddedRange(this ILogger logger, int count, string entityType);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Debug, Message = "Batch {Action} {Count} {EntityType} rows")]
    public static partial void BatchWritten(this ILogger logger, string action, int count, string entityType);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Warning, Message = "Batch write of {EntityType} failed with SQLSTATE {SqlState}; retrying once")]
    public static partial void BatchRetried(this ILogger logger, string entityType, string sqlState);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Debug, Message = "Created read connection for Dapper")]
    public static partial void CreatedReadConnection(this ILogger logger);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Debug, Message = "Created write connection for Dapper")]
    public static partial void CreatedWriteConnection(this ILogger logger);

    [LoggerMessage(EventId = 1200, Level = LogLevel.Debug, Message = "Transaction started with isolation level {IsolationLevel}")]
    public static partial void TransactionStarted(this ILogger logger, IsolationLevel isolationLevel);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Error, Message = "Error disposing the connection after the transaction failed to start")]
    public static partial void TransactionStartCleanupFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Debug, Message = "Transaction committed")]
    public static partial void TransactionCommitted(this ILogger logger);

    // 1203 (commit failed) and 1206 (rollback failed) are retired: a failure propagates and is logged once where handled.

    [LoggerMessage(EventId = 1204, Level = LogLevel.Debug, Message = "Rollback called with no active transaction (already ended, e.g. by a failed commit)")]
    public static partial void RollbackWithoutTransaction(this ILogger logger);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Debug, Message = "Transaction rolled back")]
    public static partial void TransactionRolledBack(this ILogger logger);

    [LoggerMessage(EventId = 1207, Level = LogLevel.Warning, Message = "Transaction was rolled back during disposal")]
    public static partial void TransactionRolledBackOnDispose(this ILogger logger);

    [LoggerMessage(EventId = 1208, Level = LogLevel.Error, Message = "Error rolling back transaction during disposal")]
    public static partial void TransactionDisposeRollbackFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Debug, Message = "Empty response body from {Endpoint}")]
    public static partial void EmptyResponseBody(this ILogger logger, string endpoint);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Debug, Message = "Successfully fetched data from {Endpoint}")]
    public static partial void Fetched(this ILogger logger, string endpoint);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Debug, Message = "Successfully posted to {Endpoint}")]
    public static partial void Posted(this ILogger logger, string endpoint);

    [LoggerMessage(EventId = 1400, Message = "{DbUpMessage}")]
    public static partial void DbUp(this ILogger logger, LogLevel level, Exception? exception, string dbUpMessage);
}
