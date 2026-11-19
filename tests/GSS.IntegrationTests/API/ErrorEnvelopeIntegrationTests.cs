using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using GSS.Api.Models;
using GSS.IntegrationTests.Fixtures;
using Xunit;

namespace GSS.IntegrationTests.API;

/// <summary>Framework-generated client errors still use the API error envelope.</summary>
[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class ErrorEnvelopeIntegrationTests(WebAppFactory factory) : ApiIntegrationTestBase(factory)
{
    private const string CorrelationHeader = "X-Correlation-Id";

    public static TheoryData<string, string, string?, HttpStatusCode, string> FrameworkErrors() => new()
    {
        { "GET", "/api/v1/nope", null, HttpStatusCode.NotFound, "The requested resource was not found." },
        { "GET", "/nope", null, HttpStatusCode.NotFound, "The requested resource was not found." },
        { "GET", "/api/v9/mis/divisions", null, HttpStatusCode.NotFound, "The requested resource was not found." },
        { "GET", "/api/v1/mis/departments/0", null, HttpStatusCode.NotFound, "The requested resource was not found." },
        { "DELETE", "/api/v1/mis/divisions", null, HttpStatusCode.MethodNotAllowed, "The HTTP method is not allowed for this resource." },
        { "POST", "/api/v1/sso/renew-token", "text/plain", HttpStatusCode.UnsupportedMediaType, "Unsupported media type: send application/json." },
        { "POST", "/api/v1/sso/renew-token", "", HttpStatusCode.UnsupportedMediaType, "Unsupported media type: send application/json." },
    };

    [Theory]
    [MemberData(nameof(FrameworkErrors))]
    public async Task FrameworkError_ReturnsTheEnvelope(
        string method,
        string path,
        string? contentType,
        HttpStatusCode expected,
        string message)
    {
        HttpRequestMessage request = new(new HttpMethod(method), path);
        if (contentType != null)
        {
            ByteArrayContent content = new(Encoding.UTF8.GetBytes("{}"));
            if (contentType.Length > 0)
            {
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            }

            request.Content = content;
        }

        HttpResponseMessage response = await Client.SendAsync(request);

        await AssertEnvelopeAsync(response, expected, message);
    }

    [Fact]
    public async Task UnmatchedRoute_WithoutAToken_Returns401Envelope()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        HttpResponseMessage response = await Client.GetAsync("/api/v1/nope");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Response<ApiError>? body = await response.Content.ReadFromJsonAsync<Response<ApiError>>();
        body!.Succeeded.Should().BeFalse();
        body.Errors.Should().ContainSingle().Which.StatusCode.Should().Be(401);
    }

    private static async Task AssertEnvelopeAsync(HttpResponseMessage response, HttpStatusCode expected, string message)
    {
        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        Response<ApiError>? body = await response.Content.ReadFromJsonAsync<Response<ApiError>>();
        body.Should().NotBeNull();
        body!.Succeeded.Should().BeFalse();
        body.Message.Should().Be(message);
        ApiError error = body.Errors.Should().ContainSingle().Subject;
        error.StatusCode.Should().Be((short)expected);
        error.ErrorId.Should().Be(response.Headers.GetValues(CorrelationHeader).Single());
    }
}
