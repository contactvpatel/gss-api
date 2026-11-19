using Asp.Versioning;
using GSS.Api.Authentication;
using GSS.Api.Models;
using GSS.Api.RateLimiting;
using GSS.Business.DTOs;
using GSS.Business.Services.Interfaces;
using GSS.Core.Interfaces.Base;
using GSS.Util.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GSS.Api.Controllers;

/// <summary>
/// SSO endpoints: token renewal and logout.
/// </summary>
[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/sso")]
[Produces("application/json")]
public class SsoController(ISsoService ssoService, IUserContext userContext) : ApiControllerBase
{
    // SSO accepts an expired access token and checks both tokens, so it is read from the header, not from
    // authentication, which rejects an expired token.

    /// <summary>
    /// Renews the access token with a refresh token. Send the current access token in the Authorization header as Bearer token (may be expired).
    /// </summary>
    /// <param name="request">Refresh token in body.</param>
    /// <returns>New access and refresh tokens, or 401.</returns>
    [HttpPost("renew-token")]
    [AllowAnonymous]
    [ReadsBearerToken]
    [EnableRateLimiting(RenewTokenPartitionKeyResolver.PolicyName)]
    [ProducesResponseType(typeof(Response<SsoAuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<SsoAuthResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Response<SsoAuthResponse>>> RenewToken(
        [FromBody] SsoRenewTokenRequest request,
        CancellationToken cancellationToken)
    {
        string? accessToken = JwtTokenHelper.ExtractBearerToken(Request.Headers.Authorization.FirstOrDefault());

        // JWT-shaped only, so garbage never costs an SSO call; SSO decides whether it is genuine.
        // Validate both tokens are present and shaped before calling service, avoiding ArgumentException (which maps to 500).
        if (!string.IsNullOrWhiteSpace(accessToken) && JwtTokenHelper.ParseToken(accessToken) is not null && !string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var authResponse = await ssoService.RenewTokenAsync(accessToken, request.RefreshToken, cancellationToken);

            if (authResponse != null)
            {
                return OkResponse(authResponse, "Token renewed successfully");
            }
        }

        // Missing or malformed tokens, or SSO refused the renewal (e.g. expired refresh token): the client must log in again.
        return UnauthorizedResponse<SsoAuthResponse>("Token renewal failed", "The access token or refresh token is missing, malformed or was refused.");
    }

    // Anonymous so a missing, rejected or expired token gets this action's 401 rather than the authentication challenge;
    // the token and user ID used here are only those the authentication handler validated with SSO.

    /// <summary>Logs out: ends the SSO session of the bearer token and clears the caller's cached data.</summary>
    /// <returns>200, or 401.</returns>
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting("strict")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Response<bool>>> Logout(CancellationToken cancellationToken)
    {
        string? token = userContext.GetToken();
        if (!string.IsNullOrWhiteSpace(token) && await ssoService.LogoutAsync(token, userContext.GetUserId(), cancellationToken))
        {
            return OkResponse(true, "Logout completed successfully");
        }

        return UnauthorizedResponse<bool>("Logout failed", "Logout failed");
    }
}
