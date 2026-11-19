using FluentAssertions;
using GSS.IntegrationTests.Fixtures;
using Xunit;

namespace GSS.IntegrationTests.API;

/// <summary>
/// Every response is authenticated, so none may be stored by a shared cache (RFC 9111 §3.5): a GET without
/// <c>[ClientCache]</c> is <c>no-store</c>.
/// </summary>
[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class CacheControlIntegrationTests(WebAppFactory factory) : ApiIntegrationTestBase(factory)
{
    [Fact]
    public async Task AuthenticatedGet_IsNoStore()
    {
        HttpResponseMessage response = await Client.GetAsync(ProtectedProbeController.Path);

        response.EnsureSuccessStatusCode();
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.CacheControl.Public.Should().BeFalse();
    }
}
