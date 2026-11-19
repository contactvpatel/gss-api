using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GSS.Api.Models;
using GSS.IntegrationTests.Fixtures;
using Xunit;

namespace GSS.IntegrationTests.API;

/// <summary>
/// Bad client input is rejected at the edge (route constraints, validators), never by a guard clause deeper in the
/// stack: an <see cref="ArgumentException"/> from a guard is a bug and answers 500.
/// </summary>
[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class InputBoundaryIntegrationTests(WebAppFactory factory) : ApiIntegrationTestBase(factory)
{
    [Theory]
    [InlineData("/api/v1/mis/roles/departments/0")]
    [InlineData("/api/v1/mis/roles/departments/-1")]
    public async Task NonPositiveDepartmentId_Returns404(string url)
    {
        HttpResponseMessage response = await Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Response<ApiError>? body = await response.Content.ReadFromJsonAsync<Response<ApiError>>();
        body!.Message.Should().Be("The requested resource was not found.",
            because: "routing answered through status-code pages; no controller action ran");
    }

    /// <summary>A wrong-typed JSON value answers 400 naming the field, never the .NET type or parser position.</summary>
    [Fact]
    public async Task WrongTypedJsonValue_Returns400WithoutTypeNames()
    {
        using StringContent body = new("""{"refreshToken":123}""", System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await Client.PostAsync("/api/v1/sso/renew-token", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string content = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(content);
        document.RootElement.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("message").GetString())
            .Should().Contain("$.refreshToken: The value is invalid.");
        content.Should().NotContain("System.").And.NotContain("LineNumber").And.NotContain("BytePosition");
    }

    [Theory]
    [InlineData("/api/v1/mis/departments")]
    [InlineData("/api/v1/mis/roletypes")]
    [InlineData("/api/v1/mis/roles")]
    [InlineData("/api/v1/mis/departments/1")]
    [InlineData("/api/v1/mis/roles/1")]
    [InlineData("/api/v1/mis/roles/departments/1")]
    [InlineData("/api/v1/mis/positions/roles/1")]
    public async Task MissingCallerHeaders_Returns403(string url)
    {
        HttpResponseMessage response = await Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        string content = await response.Content.ReadAsStringAsync();
        using JsonDocument body = JsonDocument.Parse(content);
        body.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        body.RootElement.GetProperty("message").GetString().Should().Be("Access denied");
        JsonElement errors = body.RootElement.GetProperty("errors");
        errors.GetArrayLength().Should().BeGreaterThan(0);
        errors.EnumerateArray().Select(e => e.GetProperty("message").GetString()).Should().OnlyContain(m => m == "Access denied");

        // The denial reason is logged, never returned.
        content.Should().NotContainEquivalentOf("x-app");
        content.Should().NotContainEquivalentOf("position");
    }
}
