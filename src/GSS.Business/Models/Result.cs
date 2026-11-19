namespace GSS.Business.Models;

/// <summary>
/// Outcome of an operation: success with a value, not found, a business-rule failure (400) or a conflict (409).
/// <c>default(Result&lt;T&gt;)</c> is no outcome: every flag is false and <see cref="Value"/> throws.
/// </summary>
/// <typeparam name="T">The type of the value when successful.</typeparam>
public readonly struct Result<T>
{
    private const string UninitializedMessage = "Result is not initialized (default). Create it with Success, NotFound, Failure or Conflict.";

    private readonly T? _value;
    private readonly Outcome _outcome;

    private Result(T? value, Outcome outcome, string? errorMessage)
    {
        _value = value;
        _outcome = outcome;
        ErrorMessage = errorMessage;
    }

    // Uninitialized must stay 0 so default(Result<T>) is never a real outcome.
    private enum Outcome : byte
    {
        Uninitialized = 0,
        Success,
        NotFound,
        Failure,
        Conflict
    }

    /// <summary>
    /// Creates a successful result with the given value.
    /// </summary>
    public static Result<T> Success(T value)
    {
        return new(value, Outcome.Success, null);
    }

    /// <summary>
    /// Creates a not-found result (404), optionally naming what is missing (returned to the caller).
    /// </summary>
    public static Result<T> NotFound(string? message = null)
    {
        return new(default, Outcome.NotFound, string.IsNullOrWhiteSpace(message) ? null : message);
    }

    /// <summary>
    /// Creates a business-rule failure (400). The message is returned to the caller.
    /// </summary>
    public static Result<T> Failure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(default, Outcome.Failure, message);
    }

    /// <summary>
    /// Creates a conflict with existing state, e.g. a duplicate unique value (409). The message is returned to the caller.
    /// </summary>
    public static Result<T> Conflict(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(default, Outcome.Conflict, message);
    }

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess => _outcome == Outcome.Success;

    /// <summary>
    /// Gets a value indicating whether the entity was not found.
    /// </summary>
    public bool IsNotFound => _outcome == Outcome.NotFound;

    /// <summary>
    /// Gets a value indicating whether the operation failed with a message (<see cref="Failure"/> or <see cref="Conflict"/>).
    /// </summary>
    public bool IsFailure => _outcome is Outcome.Failure or Outcome.Conflict;

    /// <summary>
    /// Gets a value indicating whether the failure is a conflict (409).
    /// </summary>
    public bool IsConflict => _outcome == Outcome.Conflict;

    /// <summary>
    /// Gets the error message when <see cref="IsFailure"/> is true, or the optional <see cref="NotFound"/> message.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets the value when successful; throws <see cref="InvalidOperationException"/> otherwise.
    /// </summary>
    public T Value => _outcome switch
    {
        Outcome.Success => _value!,
        Outcome.Uninitialized => throw new InvalidOperationException(UninitializedMessage),
        _ => throw new InvalidOperationException($"Cannot access Value when result is {_outcome}.")
    };
}
