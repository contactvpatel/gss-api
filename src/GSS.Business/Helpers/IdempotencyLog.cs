using Microsoft.Extensions.Logging;

namespace GSS.Business.Helpers;

/// <summary>Source-generated idempotency logs (EventIds 3100+).</summary>
internal static partial class IdempotencyLog
{
    [LoggerMessage(EventId = 3100, Level = LogLevel.Information, Message = "Idempotency key replayed the stored response")]
    public static partial void Replayed(this ILogger logger);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information, Message = "Idempotency key reused with a different request")]
    public static partial void Mismatched(this ILogger logger);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Error, Message = "Rollback failed while releasing an idempotency key")]
    public static partial void RollbackFailed(this ILogger logger, Exception exception);
}
