using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GSS.Api.Models;
using GSS.IntegrationTests.Fixtures;
using Xunit;

namespace GSS.IntegrationTests.API;

/// <summary>
/// Request DTOs are validated by FluentValidation only: one bad field yields one error, never a duplicate
/// from DataAnnotations (explicit attributes or MVC's implicit [Required] on non-nullable strings).
/// </summary>
[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class RequestValidationIntegrationTests(WebAppFactory factory) : ApiIntegrationTestBase(factory)
{
    private const string RenewTokenPath = "/api/v1/sso/renew-token";

    [Fact]
    public async Task InvalidField_EmptyString_Returns400WithSingleError()
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(RenewTokenPath, new { refreshToken = "" });

        await AssertSingleRefreshTokenErrorAsync(response);
    }

    [Fact]
    public async Task InvalidField_Missing_Returns400WithSingleError()
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(RenewTokenPath, new { });

        await AssertSingleRefreshTokenErrorAsync(response);
    }

    [Fact]
    public async Task InvalidField_TooLong_Returns400WithSingleError()
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(RenewTokenPath, new { refreshToken = new string('a', 501) });

        await AssertSingleRefreshTokenErrorAsync(response);
    }

    private static async Task AssertSingleRefreshTokenErrorAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Response<object>? body = await response.Content.ReadFromJsonAsync<Response<object>>();
        body!.Succeeded.Should().BeFalse();
        body.Errors.Should().ContainSingle()
            .Which.Message.Should().StartWith("RefreshToken:");
    }
}
