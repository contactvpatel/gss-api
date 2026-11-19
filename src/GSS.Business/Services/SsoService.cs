using Caching.NET;
using Caching.NET.Extensions;
using Caching.NET.Options;
using GSS.Core.Helpers;
using GSS.Util.Security;
using Mapster;
using Microsoft.Extensions.Logging;

namespace GSS.Business.Services;

/// <summary>
/// Wraps the Core SSO client. The single owner of token validation caching and of logout cache cleanup.
/// </summary>
public class SsoService(
    Core.Interfaces.Services.ISsoService coreSsoService,
    ICacheService cacheService,
    TimeProvider timeProvider,
    ILogger<SsoService> logger) : Interfaces.ISsoService
{
    /// <summary>How long an SSO rejection is cached: absorbs a burst of requests, then SSO decides again.</summary>
    private static readonly TimeSpan RejectionLifetime = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task<bool> ValidateTokenAsync(string token, DateTimeOffset? tokenExpiresAtUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Token validation failed: token is null or empty");
            return false;
        }

        // Cached until the token expires; no expiry (or under a second left), no caching.
        var lifetime = JwtTokenHelper.GetRemainingLifetime(tokenExpiresAtUtc, timeProvider.GetUtcNow());
        if (lifetime is not { } validLifetime)
        {
            return await ValidateWithSsoAsync(token, cancellationToken).ConfigureAwait(false);
        }

        // GetOrSetAsync is single-flight: concurrent first requests with one token share one SSO call.
        return await cacheService.GetOrSetAsync<bool>(
            CacheKeys.TokenValidation(token),
            async (ctx, cancel) =>
            {
                bool isValid = await ValidateWithSsoAsync(token, cancel).ConfigureAwait(false);
                if (!isValid)
                {
                    var rejection = validLifetime < RejectionLifetime ? validLifetime : RejectionLifetime;
                    ctx.Overrides.DistributedExpiration = rejection;
                    ctx.Overrides.LocalExpiration = rejection;
                }

                return isValid;
            },
            options: new CacheEntryOverrides
            {
                DistributedExpiration = validLifetime,
                LocalExpiration = validLifetime,
                // Authentication data: never serve a stale result.
                FailSafe = false
            },
            token: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SSO's verdict only. An outage (503) or an internal error (500) propagates and is never cached, so a bug is not
    /// mistaken for a rejected token (401 logs users out).
    /// </summary>
    private async Task<bool> ValidateWithSsoAsync(string token, CancellationToken cancellationToken)
    {
        bool isValid = await coreSsoService.ValidateTokenAsync(token, cancellationToken).ConfigureAwait(false);
        if (!isValid)
        {
            // Logged by the authentication handler.
            logger.LogDebug("Token validation failed: invalid token");
        }

        return isValid;
    }

    /// <inheritdoc />
    public async Task<DTOs.SsoAuthResponse?> RenewTokenAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken, nameof(accessToken));
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken, nameof(refreshToken));

        Core.Models.SsoAuthResponse? coreResponse;
        try
        {
            coreResponse = await coreSsoService.RenewTokenAsync(accessToken, refreshToken, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // The client went away mid-call: SSO may already have renewed, so stop trusting the old token anyway.
            await RemoveTokenEntriesAsync(accessToken).ConfigureAwait(false);
            throw;
        }

        if (coreResponse == null)
        {
            return null;
        }

        // The old access token is no longer trusted from cache: its next use is re-checked with SSO.
        await RemoveTokenEntriesAsync(accessToken).ConfigureAwait(false);

        return coreResponse.Adapt<DTOs.SsoAuthResponse>();
    }

    /// <inheritdoc />
    public async Task<bool> LogoutAsync(string token, string? userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        bool loggedOut;
        try
        {
            loggedOut = await coreSsoService.LogoutAsync(token, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // SSO may already have ended the session even though the caller disconnected.
            await RemoveLogoutEntriesAsync(token, userId).ConfigureAwait(false);
            throw;
        }

        // SSO answered (an outage throws first and clears nothing): cleared on a refusal too, so a token SSO no longer
        // accepts is not still trusted from cache until its exp.
        await RemoveLogoutEntriesAsync(token, userId).ConfigureAwait(false);
        return loggedOut;
    }

    /// <summary>Removes the token's entries and the user's cached MIS positions. Never cancelled: runs after SSO acted.</summary>
    private Task RemoveLogoutEntriesAsync(string token, string? userId)
    {
        List<string> keys = [CacheKeys.TokenValidation(token), CacheKeys.AsmSecurity(token)];
        if (!string.IsNullOrWhiteSpace(userId))
        {
            keys.Add(CacheKeys.PersonPositions(userId));
        }

        return cacheService.RemoveManyAsync(keys, token: CancellationToken.None);
    }

    /// <summary>
    /// Removes the token's cached validation and ASM security entries, concurrently. Never cancelled: it runs after
    /// SSO acted, when the request's token may already be cancelled.
    /// </summary>
    private Task RemoveTokenEntriesAsync(string token)
    {
        return cacheService.RemoveManyAsync([CacheKeys.TokenValidation(token), CacheKeys.AsmSecurity(token)], token: CancellationToken.None);
    }
}
