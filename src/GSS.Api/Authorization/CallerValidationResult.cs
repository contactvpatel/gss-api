namespace GSS.Api.Authorization;

/// <summary>Outcome of <see cref="ICallerValidator.ValidateAsync"/>: the validated caller, or why validation failed.</summary>
/// <param name="Caller">The validated caller; null when validation failed.</param>
/// <param name="FailureReason">Why validation failed; null when it succeeded.</param>
public sealed record CallerValidationResult(ValidatedCaller? Caller, string? FailureReason)
{
    /// <summary>True when the headers match the caller's MIS position.</summary>
    public bool Succeeded => Caller != null;

    /// <summary>A successful result for <paramref name="caller"/>.</summary>
    public static CallerValidationResult Valid(ValidatedCaller caller)
    {
        return new CallerValidationResult(caller, null);
    }

    /// <summary>A failed result with <paramref name="reason"/>.</summary>
    public static CallerValidationResult Invalid(string reason)
    {
        return new CallerValidationResult(null, reason);
    }
}
