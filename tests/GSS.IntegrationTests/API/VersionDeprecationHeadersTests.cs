using System.Globalization;
using System.Net;
using Asp.Versioning;
using FluentAssertions;
using GSS.Api.Extensions.Features;
using GSS.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSS.IntegrationTests.API;

/// <summary>
/// The Api's versioning registration with deprecation enabled, on a real server response with a body: headers
/// written after the endpoint ran would fail there ("Headers are read-only"). Its own minimal host, because the
/// full app can be built only once per process.
/// </summary>
[Trait("Category", TestCategories.Integration)]
public class VersionDeprecationHeadersTests
{
    private static readonly DateTimeOffset DeprecationDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SunsetDate = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DeprecatedVersion_ResponseCarriesDeprecationSunsetAndLinkHeaders()
    {
        await using var app = BuildApp(new Dictionary<string, string?>
        {
            ["ApiPipeline:VersionDeprecation:Enabled"] = "true",
            ["ApiPipeline:VersionDeprecation:DeprecatedVersions:0:Version"] = "1.0",
            ["ApiPipeline:VersionDeprecation:DeprecatedVersions:0:DeprecationDate"] = DeprecationDate.ToString("O", CultureInfo.InvariantCulture),
            ["ApiPipeline:VersionDeprecation:DeprecatedVersions:0:SunsetDate"] = SunsetDate.ToString("O", CultureInfo.InvariantCulture),
            ["ApiPipeline:VersionDeprecation:DeprecatedVersions:0:DeprecationLink"] = "https://docs.example.com/v1-migration",
            ["ApiPipeline:VersionDeprecation:DeprecatedVersions:0:SunsetLink"] = "https://docs.example.com/v1-sunset",
        });
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/api/v1/deprecation-probe");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("probe");
        response.Headers.GetValues("Deprecation").Should().ContainSingle()
            .Which.Should().Be($"@{DeprecationDate.ToUnixTimeSeconds()}");
        response.Headers.GetValues("Sunset").Should().ContainSingle()
            .Which.Should().Be(SunsetDate.ToString("r", CultureInfo.InvariantCulture));
        string links = string.Join(", ", response.Headers.GetValues("Link"));
        links.Should().Contain("<https://docs.example.com/v1-migration>").And.Contain("rel=\"deprecation\"");
        links.Should().Contain("<https://docs.example.com/v1-sunset>").And.Contain("rel=\"sunset\"");
    }

    [Fact]
    public async Task InvalidDeprecationEntry_FailsStartup()
    {
        // A version without a DeprecationDate cannot produce an RFC 9745 header.
        Func<Task> start = async () =>
        {
            await using var app = BuildApp(new Dictionary<string, string?>
            {
                ["ApiPipeline:VersionDeprecation:Enabled"] = "true",
                ["ApiPipeline:VersionDeprecation:DeprecatedVersions:0:Version"] = "1.0",
            });
            await app.StartAsync();
        };

        (await start.Should().ThrowAsync<Microsoft.Extensions.Options.OptionsValidationException>())
            .Which.Message.Should().Contain("DeprecationDate");
    }

    private static WebApplication BuildApp(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddControllers().AddApplicationPart(typeof(DeprecationProbeController).Assembly);
        builder.Services.ConfigureApiVersioning(builder.Configuration);

        var app = builder.Build();
        app.MapControllers();
        return app;
    }
}

/// <summary>A v1 endpoint with a JSON body.</summary>
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/deprecation-probe")]
public sealed class DeprecationProbeController : ControllerBase
{
    /// <summary>Returns a small body.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new { name = "probe" });
}
