using GSS.Business.DTOs;

namespace GSS.Business.Services.Interfaces;

/// <summary>
/// SSO operations: wraps the Core SSO client and owns every token-keyed cache entry (validation, ASM security).
/// </summary>
public interface ISsoService
{
    /// <summary>
    /// Validates the token with SSO. A valid result is cached until <paramref name="tokenExpiresAtUtc"/> (the token's
    /// <c>exp</c>, already parsed by the caller; null = not cached); a rejection is cached for 5 seconds.
    /// False when SSO rejects the token or validation fails unexpectedly (fail closed).
    /// </summary>
    /// <exception cref="Core.Exceptions.ExternalServiceException">SSO could not answer; the caller answers 503, not 401.</exception>
    Task<bool> ValidateTokenAsync(string token, DateTimeOffset? tokenExpiresAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews tokens; null when SSO refuses the renewal. On success or cancellation during the call, the old access
    /// token's cached validation and ASM security are removed, so its next use is re-checked with SSO.
    /// </summary>
    /// <exception cref="Core.Exceptions.ExternalServiceException">SSO could not answer (outage, timeout, open circuit).</exception>
    Task<SsoAuthResponse?> RenewTokenAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the SSO session for the token. After an SSO answer or cancellation during the call, removes the token's
    /// cached validation and ASM security and <paramref name="userId"/>'s cached MIS positions. An outage clears nothing.
    /// Returns false when the token is missing or SSO refuses it.
    /// </summary>
    /// <param name="token">Bearer token to end.</param>
    /// <param name="userId">Person ID of the authenticated caller, or null. Pass only an ID taken from a validated token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Core.Exceptions.ExternalServiceException">SSO could not answer (outage, timeout, open circuit).</exception>
    Task<bool> LogoutAsync(string token, string? userId, CancellationToken cancellationToken = default);
}
