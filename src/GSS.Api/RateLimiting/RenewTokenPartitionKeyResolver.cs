using System.Security.Cryptography;
using System.Text;
using ApiPipeline.NET;
using ApiPipeline.NET.RateLimiting;
using GSS.Util.Security;

namespace GSS.Api.RateLimiting;

/// <summary>
/// Partitions token-renewal requests by a one-way hash of the bearer token. This keeps the anonymous renewal endpoint
/// from sharing one IP-based quota while ensuring the raw token is never retained as rate-limiter state.
/// </summary>
public sealed class RenewTokenPartitionKeyResolver : IRateLimitPartitionKeyResolver
{
    /// <summary>The policy used by the token-renewal endpoint.</summary>
    public const string PolicyName = "renew";

    private const string PartitionType = "token";

    /// <inheritdoc />
    public RateLimitPartitionKey? Resolve(HttpContext httpContext, ApiPipelineRateLimitPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(policy);

        if (!string.Equals(policy.Name, PolicyName, StringComparison.Ordinal))
        {
            return null;
        }

        string? token = JwtTokenHelper.ExtractBearerToken(httpContext.Request.Headers.Authorization.FirstOrDefault());
        if (token is null)
        {
            return null;
        }

        string tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return new RateLimitPartitionKey(PartitionType, tokenHash);
    }
}
