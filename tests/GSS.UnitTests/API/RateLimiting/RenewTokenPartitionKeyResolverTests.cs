using ApiPipeline.NET;
using ApiPipeline.NET.RateLimiting;
using FluentAssertions;
using GSS.Api.RateLimiting;
using GSS.UnitTests.Common;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GSS.UnitTests.API.RateLimiting;

/// <summary>
/// SSO renew is limited per bearer token, not per client IP: renew usually carries an expired token, so the caller is
/// anonymous and the built-in chain would put everyone behind one NAT address in one bucket.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class RenewTokenPartitionKeyResolverTests
{
    private readonly RenewTokenPartitionKeyResolver _resolver = new();

    private static readonly ApiPipelineRateLimitPolicy Renew = new() { Name = RenewTokenPartitionKeyResolver.PolicyName };

    [Fact]
    public void Renew_WithBearerToken_KeysByTokenHash_NeverTheRawToken()
    {
        var key = _resolver.Resolve(Request("Bearer token-a"), Renew);

        key.Should().NotBeNull();
        key!.Type.Should().Be("token");
        key.Value.Should().HaveLength(64).And.NotContain("token-a");
    }

    [Fact]
    public void Renew_SameToken_SameBucket_DifferentToken_DifferentBucket()
    {
        string first = _resolver.Resolve(Request("Bearer token-a"), Renew)!.Value;
        string again = _resolver.Resolve(Request("bearer  token-a "), Renew)!.Value;
        string other = _resolver.Resolve(Request("Bearer token-b"), Renew)!.Value;

        again.Should().Be(first);
        other.Should().NotBe(first);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer ")]
    public void Renew_WithoutBearerToken_FallsBackToTheBuiltInChain(string? authorization)
    {
        _resolver.Resolve(Request(authorization), Renew).Should().BeNull("null lets the request be keyed by client IP");
    }

    [Theory]
    [InlineData("global")]
    [InlineData("strict")]
    public void OtherPolicies_AreLeftToTheBuiltInChain(string policyName)
    {
        ApiPipelineRateLimitPolicy policy = new() { Name = policyName };

        _resolver.Resolve(Request("Bearer token-a"), policy).Should().BeNull();
    }

    private static DefaultHttpContext Request(string? authorization)
    {
        DefaultHttpContext context = new();
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        return context;
    }
}
