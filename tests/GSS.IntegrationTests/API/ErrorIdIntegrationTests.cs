using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GSS.Api.Models;
using GSS.IntegrationTests.Fixtures;
using Xunit;

namespace GSS.IntegrationTests.API;

/// <summary>Every error's ErrorId is the request's correlation id (the X-Correlation-Id response header).</summary>
[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class ErrorIdIntegrationTests(WebAppFactory factory) : ApiIntegrationTestBase(factory)
{
    private const string CorrelationHeader = "X-Correlation-Id";

    [Fact]
    public async Task ControllerError_ErrorIdIsCallersCorrelationId()
    {
        // Logout is anonymous: with no token the controller itself answers 401 through ApiControllerBase.
        Client.DefaultRequestHeaders.Authorization = null;
        string correlationId = Guid.NewGuid().ToString();

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/v1/sso/logout", correlationId);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.GetValues(CorrelationHeader).Should().ContainSingle().Which.Should().Be(correlationId);
        await AssertErrorIdsAsync(response, correlationId);
    }

    [Fact]
    public async Task ValidationFailure_EveryErrorIdIsCorrelationId()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/v1/sso/renew-token", correlationId: null, JsonContent.Create(new { }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorIdsAsync(response, response.Headers.GetValues(CorrelationHeader).Single());
    }

    [Fact]
    public async Task AuthenticationChallenge_ErrorIdIsCorrelationId()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        HttpResponseMessage response = await SendAsync(HttpMethod.Get, ProtectedProbeController.Path, correlationId: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorIdsAsync(response, response.Headers.GetValues(CorrelationHeader).Single());
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? correlationId, HttpContent? content = null)
    {
        HttpRequestMessage request = new(method, path) { Content = content };
        if (correlationId != null)
        {
            request.Headers.Add(CorrelationHeader, correlationId);
        }

        return Client.SendAsync(request);
    }

    private static async Task AssertErrorIdsAsync(HttpResponseMessage response, string expectedErrorId)
    {
        Response<object>? body = await response.Content.ReadFromJsonAsync<Response<object>>();
        body!.Errors.Should().NotBeEmpty();
        body.Errors!.Should().OnlyContain(e => e.ErrorId == expectedErrorId);
    }
}
