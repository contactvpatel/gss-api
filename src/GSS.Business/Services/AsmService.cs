using System.Collections.Immutable;
using Caching.NET;
using Caching.NET.Options;
using GSS.Business.DTOs;
using GSS.Core.Helpers;
using GSS.Core.Models;
using GSS.Util.Security;
using Mapster;

namespace GSS.Business.Services;

/// <summary>
/// A person's ASM access per role and position, cached until the token expires.
/// </summary>
public class AsmService(
    Core.Interfaces.Services.IAsmService asmService,
    ICacheService cacheService,
    TimeProvider timeProvider) : Interfaces.IAsmService
{

    /// <inheritdoc />
    public async Task<IReadOnlyList<AsmResponseDto>> GetApplicationSecurityAsync(
        string token,
        DateTimeOffset? tokenExpiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token, nameof(token));

        // Not cached without an expiry.
        var cacheExpiration = JwtTokenHelper.GetRemainingLifetime(tokenExpiresAtUtc, timeProvider.GetUtcNow());
        if (cacheExpiration == null)
        {
            return await FetchAsync(token, cancellationToken).ConfigureAwait(false);
        }

        var result = await cacheService.GetOrSetAsync(
            CacheKeys.AsmSecurity(token),
            cancel => FetchAsync(token, cancel),
            new CacheEntryOverrides
            {
                DistributedExpiration = cacheExpiration,
                LocalExpiration = cacheExpiration,
                // Never serve stale permissions.
                FailSafe = false
            },
            token: cancellationToken).ConfigureAwait(false);

        return result.IsDefaultOrEmpty ? Array.Empty<AsmResponseDto>() : result;
    }

    // Frozen: the L1 cache shares this instance.
    private async Task<ImmutableArray<AsmResponseDto>> FetchAsync(string token, CancellationToken cancellationToken)
    {
        var items = await asmService.GetApplicationSecurityAsync(token, cancellationToken).ConfigureAwait(false);
        return items.Adapt<List<AsmResponseDto>>().ToImmutableArray();
    }
}
