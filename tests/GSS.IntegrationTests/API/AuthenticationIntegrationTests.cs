using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using GSS.Api.Models;
using GSS.IntegrationTests.Fixtures;
using Xunit;

namespace GSS.IntegrationTests.API;

/// <summary>Global auth filter is enforced on controller actions.</summary>
[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class AuthenticationIntegrationTests(WebAppFactory factory) : ApiIntegrationTestBase(factory)
{
    private const string ProtectedPath = ProtectedProbeController.Path;

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401BearerChallengeInEnvelope()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Be("Bearer");
        var body = await response.Content.ReadFromJsonAsync<Response<object>>();
        body!.Succeeded.Should().BeFalse();
        body.Errors.Should().ContainSingle(e => e.Message == "Token is missing or invalid");
    }

    [Fact]
    public async Task OpenApiDocument_WithoutToken_IsAnonymous()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithMalformedToken_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var response = await Client.GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTokenRejectedBySso_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FakeSsoService.RejectedToken);

        var response = await Client.GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_DuringSsoOutage_Returns503NotUnauthorized()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FakeSsoService.OutageToken);

        var response = await Client.GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<Response<object>>();
        body!.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AnonymousEndpoint_DuringSsoOutage_IsNotBlocked()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FakeSsoService.OutageToken);

        var response = await Client.GetAsync("/internal/health/liveness");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoint_ValidationError_Returns500NotUnauthorized()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FakeSsoService.ErrorToken);

        var response = await Client.GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.WwwAuthenticate.Should().BeEmpty("a 401 challenge would make clients log the user out");
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"succeeded\":false").And.NotContain("Simulated validation bug");
    }

    [Fact]
    public async Task AnonymousEndpoint_ValidationError_IsNotBlocked()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FakeSsoService.ErrorToken);

        var response = await Client.GetAsync("/internal/health/liveness");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The usual renewal: the access token has expired, so authentication rejects it, but SSO renews it.</summary>
    [Fact]
    public async Task RenewToken_WithExpiredAccessToken_Returns200()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FakeSsoService.ExpiredToken);

        var response = await Client.PostAsJsonAsync(
            "/api/v1/sso/renew-token", new { refreshToken = FakeSsoService.ValidRefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<Response<object>>();
        body!.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RenewToken_RefusedRefreshToken_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FakeSsoService.ExpiredToken);

        var response = await Client.PostAsJsonAsync("/api/v1/sso/renew-token", new { refreshToken = "revoked" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RenewToken_WithoutAccessToken_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.PostAsJsonAsync(
            "/api/v1/sso/renew-token", new { refreshToken = FakeSsoService.ValidRefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidToken_Returns200()
    {
        var response = await Client.GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthProbe_WithoutToken_IsNotBlockedByAuthentication()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.GetAsync("/internal/health/liveness");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_WithValidToken_Returns200()
    {
        var response = await Client.PostAsync("/api/v1/sso/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("expired")]
    [InlineData("none")]
    public async Task Logout_WithoutAValidatedToken_Returns401(string scenario)
    {
        // Logout is anonymous, but only a token SSO has validated reaches the controller (UserContext reads it from the handler).
        string? token = scenario switch
        {
            "rejected" => FakeSsoService.RejectedToken,
            "expired" => FakeSsoService.ExpiredToken,
            _ => null
        };
        Client.DefaultRequestHeaders.Authorization = token == null ? null : new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.PostAsync("/api/v1/sso/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<Response<bool>>();
        body!.Message.Should().Be("Logout failed", because: "the controller, not the authentication challenge, answers");
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidToken_TagsTheRequestSpanWithTheUserId()
    {
        // OpenTelemetry.NET tags user.id before authentication runs, so the handler must tag the request span itself.
        ActivityTraceId traceId = ActivityTraceId.CreateRandom();
        ConcurrentBag<Activity> requests = [];
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId)
                {
                    requests.Add(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        using HttpRequestMessage request = new(HttpMethod.Get, ProtectedPath);
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        requests.Should().ContainSingle().Which.GetTagItem("user.id").Should().Be(FakeSsoService.TestUserId);
    }
}
