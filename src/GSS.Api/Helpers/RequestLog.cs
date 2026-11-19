namespace GSS.Api.Helpers;

/// <summary>
/// Source-generated logs for rejected requests (EventIds 2010+; <see cref="ControllerLog"/> has 2000). A client's own
/// mistake is Information, never Warning: it needs no action, anyone can trigger it, and authentication runs before
/// rate limiting.
/// </summary>
internal static partial class RequestLog
{
    /// <summary>A 400 from request validation; field names only, never values.</summary>
    [LoggerMessage(EventId = 2010, Level = LogLevel.Information, Message = "Request validation failed for {Controller}.{Action}: {Fields}")]
    public static partial void ValidationFailed(this ILogger logger, string? controller, string? action, string fields);

    /// <summary>A JSON binding error; its message names .NET types, so it is logged here and never returned.</summary>
    [LoggerMessage(EventId = 2011, Level = LogLevel.Debug, Message = "Request binding failed for {Key}: {BindingError}")]
    public static partial void BindingFailed(this ILogger logger, string key, string bindingError);

    /// <summary>A token rejected by the local checks (malformed, expired, no expiry or user id): no SSO call.</summary>
    [LoggerMessage(EventId = 2100, Level = LogLevel.Information, Message = "Token rejected: {Reason}. Path: {Path}")]
    public static partial void TokenRejectedLocally(this ILogger logger, string reason, string? path);

    /// <summary>A well-formed, unexpired token that SSO refused (forged, revoked or logged out).</summary>
    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "Token rejected by SSO. Path: {Path}")]
    public static partial void TokenRejectedBySso(this ILogger logger, string? path);
}
