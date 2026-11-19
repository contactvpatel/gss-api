using System.Net;
using System.Text;
using FluentAssertions;
using GSS.Core.Interfaces.Services;
using GSS.Infrastructure.Extensions;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using GSS.UnitTests.Common;
using GSS.Util.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Extensions;

/// <summary>
/// App credentials are client defaults set once at registration: every MIS/ASM request carries each one exactly once,
/// and services add only per-call headers.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class HttpClientRegistrationTests
{
    private readonly CapturingHandler _handler = new();

    private ServiceProvider BuildServices(string misUrl = "https://mis.example.com/api/v1/", string misAppSecret = "mis-secret")
    {
        ServiceCollection services = new();
        services.AddLogging();
        // Registered by AddInfrastructure's data access; the HTTP services check it for an open transaction.
        services.AddSingleton(Mock.Of<IDapperTransactionManager>()); // an instance: the container never disposes it
        services.AddSingleton(Options.Create(new SsoServiceOptions { Url = "https://sso.example.com/api/" }));
        services.AddSingleton(Options.Create(new MisServiceOptions
        {
            Url = misUrl,
            Headers = new MisServiceHeaders { AuthAppId = "mis-id", AuthAppSecret = misAppSecret },
            Endpoint = new MisServiceEndpoints { Department = "Department" }
        }));
        services.AddSingleton(Options.Create(new AsmServiceOptions
        {
            Url = "https://asm.example.com/api/v2/",
            Headers = new AsmServiceHeaders { AuthAppId = "asm-id", AuthAppSecret = "asm-secret" },
            Endpoint = new AsmServiceEndpoints { ApplicationSecurity = "application-security" }
        }));
        services.AddInfrastructureHttpClients(new ConfigurationBuilder().Build());
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => _handler));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task MisRequest_CarriesEachAppCredentialOnce()
    {
        using var provider = BuildServices();

        await provider.GetRequiredService<IMisService>().GetDepartmentsAsync();

        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.RequestUri.Should().Be(new Uri("https://mis.example.com/api/v1/Department"));
        request.Headers.GetValues(HttpHeaderNames.MisAuthAppId).Should().Equal("mis-id");
        request.Headers.GetValues(HttpHeaderNames.MisAuthAppSecret).Should().Equal("mis-secret");
    }

    /// <summary>Without the trailing slash, "Department" would resolve to /api/Department (the "v1" segment dropped).</summary>
    [Theory]
    [InlineData("https://mis.example.com/api/v1")]
    [InlineData("https://mis.example.com/api/v1/")]
    public async Task BaseUrl_WithOrWithoutTrailingSlash_KeepsItsFullPath(string misUrl)
    {
        using var provider = BuildServices(misUrl);

        await provider.GetRequiredService<IMisService>().GetDepartmentsAsync();

        _handler.Requests.Should().ContainSingle().Which.RequestUri.Should().Be(new Uri("https://mis.example.com/api/v1/Department"));
    }

    [Fact]
    public async Task AsmRequest_CarriesEachAppCredentialOnce_PlusTheUsersToken()
    {
        using var provider = BuildServices();

        await provider.GetRequiredService<IAsmService>().GetApplicationSecurityAsync("user-token");

        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.Headers.GetValues(HttpHeaderNames.AsmAuthAppId).Should().Equal("asm-id");
        request.Headers.GetValues(HttpHeaderNames.AsmAuthAppSecret).Should().Equal("asm-secret");
        request.Headers.Authorization!.ToString().Should().Be("user-token");
    }

    /// <summary>A blank credential that got past validation fails the client, naming the key, instead of being left out.</summary>
    [Fact]
    public void BlankAppCredential_FailsWhenTheClientIsBuilt()
    {
        using var provider = BuildServices(misAppSecret: " ");

        Action act = () => provider.GetRequiredService<IMisService>();

        act.Should().Throw<InvalidOperationException>().WithMessage("MisService:Headers:AuthAppSecret is not configured.");
    }

    /// <summary>Records each request after HttpClient has merged its default headers.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"succeeded":true,"data":[]}""", Encoding.UTF8, "application/json")
            });
        }
    }
}
