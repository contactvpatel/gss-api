namespace GSS.Core.Helpers;

/// <summary>
/// Batch size limit, checked where each check pays off: <c>ValidationFilter</c> (HTTP 400 before any item is validated),
/// <c>EntityServiceBase</c> batch methods (Failure before any rule or query runs, for every caller) and the repositories
/// (throws: a caller skipped the service, a bug).
/// </summary>
public static class BatchLimits
{
    /// <summary>Maximum items in one batch write.</summary>
    public const int MaxBatchSize = 1000;

    /// <summary>The one message for an oversized batch, wherever it is caught.</summary>
    public static string TooLarge(int count) => $"A batch can contain at most {MaxBatchSize} items; this one has {count}.";
}
