using System.Net;
using GSS.Core.Exceptions;
using GSS.Core.Models;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using GSS.Util.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GSS.Infrastructure.Services.External;

/// <summary>
/// SSO HTTP client: validate, renew and logout.
/// </summary>
public class SsoService(
    HttpClient httpClient,
    IDapperTransactionManager transactionManager,
    IOptions<SsoServiceOptions> options,
    ILogger<SsoService> logger) : HttpServiceBase(httpClient, transactionManager, logger, nameof(SsoService)), Core.Interfaces.Services.ISsoService
{
    /// <inheritdoc />
    public async Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            Logger.LogWarning("Token validation failed: token is null or empty");
            return false;
        }

        try
        {
            // SSO takes the token as the bearer credential, with no body.
            string endpoint = options.Value.Endpoint.ValidateToken;
            return await PostWithoutBodyAsync(
                endpoint,
                request => request.SetBearerToken(token),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ExternalServiceException ex) when (IsRejection(ex))
        {
            // Debug: the authentication handler logs the rejection once.
            Logger.LogDebug("SSO rejected the token ({StatusCode})", (int?)ex.StatusCode);
            return false;
        }
    }

    /// <summary>
    /// SSO refused the token (400, 401 or 403): validate, renew and logout then return false/null (401). Anything else
    /// is not a verdict on the token and propagates (502/503/504, not 401): 5xx, 408, 429, timeouts, an open circuit,
    /// and 404/405 too, which mean a wrong URL or endpoint, where a 401 would log every user out.
    /// </summary>
    private static bool IsRejection(ExternalServiceException ex)
    {
        return ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    }

    /// <inheritdoc />
    public async Task<SsoAuthResponse?> RenewTokenAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Renewing token");

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken))
        {
            Logger.LogWarning("Renew token failed: access token or refresh token is missing");
            return null;
        }

        string endpoint = options.Value.Endpoint.RenewToken;
        RenewTokenRequest request = new() { RefreshToken = refreshToken };

        SsoAuthResponse? response;
        try
        {
            response = await PostAsync<RenewTokenRequest, SsoAuthResponse>(
                endpoint,
                request,
                httpRequest => httpRequest.SetBearerToken(accessToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ExternalServiceException ex) when (IsRejection(ex))
        {
            // E.g. an expired refresh token: the client must log in again (401), not retry as if SSO were down.
            Logger.LogInformation("SSO refused token renewal ({StatusCode})", (int?)ex.StatusCode);
            return null;
        }

        // A 2xx without both tokens is an SSO fault (502), never a refusal: null would answer 401 and log the user out,
        // and blank tokens would hand the client nothing usable.
        if (response is null)
        {
            throw UnsuccessfulResponse(endpoint, HttpMethod.Post, "no response body");
        }

        if (string.IsNullOrWhiteSpace(response.AccessToken) || string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            throw UnsuccessfulResponse(endpoint, HttpMethod.Post, "response has no access or refresh token");
        }

        return response;
    }

    /// <inheritdoc />
    public async Task<bool> LogoutAsync(string token, CancellationToken cancellationToken = default)
    {
        const string area = "SsoService.LogoutAsync";
        Logger.LogInformation("{Area}: Logging out user", area);

        if (string.IsNullOrWhiteSpace(token))
        {
            Logger.LogWarning("{Area}: Logout failed: token is missing", area);
            return false;
        }

        string endpoint = options.Value.Endpoint.Logout;

        try
        {
            return await PostWithoutBodyAsync(
                endpoint,
                request => request.SetBearerToken(token),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ExternalServiceException ex) when (IsRejection(ex))
        {
            Logger.LogInformation("{Area}: SSO refused logout ({StatusCode})", area, (int?)ex.StatusCode);
            return false;
        }
    }
}
