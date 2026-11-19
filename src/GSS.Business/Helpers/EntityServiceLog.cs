using GSS.Business.Services.Base;
using Microsoft.Extensions.Logging;

namespace GSS.Business.Helpers;

/// <summary>
/// Source-generated logs for <see cref="EntityServiceBase{TEntity,TDto,TCreateDto,TUpdateDto}"/> writes.
/// EventIds 3000+ (Infrastructure 1000-1499, Api 2000+).
/// </summary>
internal static partial class EntityServiceLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "{Entity} {EntityId} created")]
    public static partial void Created(this ILogger logger, string entity, int entityId);

    /// <summary><paramref name="change"/> is "updated" or "patched".</summary>
    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "{Entity} {EntityId} {Change}")]
    public static partial void Changed(this ILogger logger, string entity, int entityId, string change);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "{Entity} {EntityId} deleted")]
    public static partial void Deleted(this ILogger logger, string entity, int entityId);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information, Message = "{Entity} {EntityId} already deleted")]
    public static partial void AlreadyDeleted(this ILogger logger, string entity, int entityId);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "{Entity} {EntityId} not updated: deleted by a concurrent request")]
    public static partial void DeletedConcurrently(this ILogger logger, string entity, int entityId);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Information, Message = "Batch created {Count} {Entity}")]
    public static partial void BatchCreated(this ILogger logger, string entity, int count);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Information, Message = "Batch updated {Count} {Entity}: {Changed} changed")]
    public static partial void BatchUpdated(this ILogger logger, string entity, int count, int changed);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Information, Message = "Batch deleted {Count} {Entity}: {AlreadyDeleted} already deleted")]
    public static partial void BatchDeleted(this ILogger logger, string entity, int count, int alreadyDeleted);

    [LoggerMessage(EventId = 3013, Level = LogLevel.Information, Message = "Batch update of {Entity} wrote nothing: {Count} deleted by a concurrent request")]
    public static partial void BatchDeletedConcurrently(this ILogger logger, string entity, int count);
}
