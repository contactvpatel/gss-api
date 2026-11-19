using System.Net;
using FluentAssertions;
using GSS.IntegrationTests.Fixtures;
using Xunit;

namespace GSS.IntegrationTests.API;

/// <summary>Deprecation is off in appsettings.json, so the current version carries no deprecation headers.</summary>
[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class VersionDeprecationIntegrationTests(WebAppFactory factory) : ApiIntegrationTestBase(factory)
{
    [Fact]
    public async Task CurrentVersion_HasNoDeprecationHeaders()
    {
        HttpResponseMessage response = await Client.GetAsync(ProtectedProbeController.Path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Deprecation").Should().BeFalse();
        response.Headers.Contains("Sunset").Should().BeFalse();
    }
}
