namespace GSS.Api.Authorization;

/// <summary>Validates the x-app-* headers against the authenticated caller's MIS position.</summary>
public interface ICallerValidator
{
    /// <summary>
    /// Validates the x-app-* headers against the caller's MIS position; the result is cached in
    /// <see cref="HttpContext.Items"/> for the rest of the request.
    /// </summary>
    Task<CallerValidationResult> ValidateAsync(HttpContext httpContext, CancellationToken cancellationToken);
}
