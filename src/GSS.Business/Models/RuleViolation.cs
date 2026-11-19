namespace GSS.Business.Models;

/// <summary>
/// A business rule a request breaks, from an <c>EntityServiceBase</c> validation hook. The message is returned to the caller.
/// </summary>
public sealed class RuleViolation
{
    private RuleViolation(bool isConflict, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        IsConflict = isConflict;
        Message = message;
    }

    /// <summary>True for a conflict with existing state (409), false for an invalid request (400).</summary>
    public bool IsConflict { get; }

    /// <summary>What is wrong, for the caller.</summary>
    public string Message { get; }

    /// <summary>The request is invalid, e.g. it references a row that does not exist (400).</summary>
    public static RuleViolation Invalid(string message)
    {
        return new RuleViolation(isConflict: false, message);
    }

    /// <summary>The request conflicts with existing state, e.g. a duplicate unique value (409).</summary>
    public static RuleViolation Conflict(string message)
    {
        return new RuleViolation(isConflict: true, message);
    }

    /// <summary>A 409 naming <paramref name="ids"/> after <paramref name="message"/>, or null when there are none.</summary>
    public static RuleViolation? ConflictIfAny(IReadOnlyCollection<int> ids, string message)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Count == 0 ? null : Conflict($"{message} IDs: {string.Join(", ", ids.Order())}.");
    }

    /// <summary>The matching failed <see cref="Result{T}"/>.</summary>
    public Result<T> ToResult<T>()
    {
        return IsConflict ? Result<T>.Conflict(Message) : Result<T>.Failure(Message);
    }
}
