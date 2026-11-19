using GSS.Core.Models;

namespace GSS.Core.Interfaces.Services;

/// <summary>
/// SSO HTTP client: token validation, renewal and logout.
/// </summary>
public interface ISsoService
{
    /// <summary>
    /// Validates the token with the SSO server: false when SSO rejects it (4xx).
    /// </summary>
    /// <exception cref="Exceptions.ExternalServiceException">SSO could not answer (outage, timeout, open circuit).</exception>
    Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews tokens: null when a token is missing or SSO refuses the renewal (400, 401, 403).
    /// </summary>
    /// <exception cref="Exceptions.ExternalServiceException">
    /// SSO could not answer (outage, timeout, open circuit), or answered 2xx without both tokens.
    /// </exception>
    Task<SsoAuthResponse?> RenewTokenAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the SSO session for the token: false when the token is missing or SSO refuses the logout (4xx).
    /// </summary>
    /// <exception cref="Exceptions.ExternalServiceException">SSO could not answer (outage, timeout, open circuit).</exception>
    Task<bool> LogoutAsync(string token, CancellationToken cancellationToken = default);
}
