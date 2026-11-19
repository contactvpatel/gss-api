using System.Text.Json;
using FluentAssertions;
using GSS.Api.Middleware;
using GSS.Api.Models;
using GSS.UnitTests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GSS.UnitTests.API.Middleware;

/// <summary>The envelope's message: the error's own detail or title, else one for the status (status-code pages pass only that).</summary>
[Trait("Category", TestCategories.Unit)]
public class ApiErrorProblemDetailsServiceTests
{
    [Theory]
    [InlineData(404, "The requested resource was not found.")]
    [InlineData(405, "The HTTP method is not allowed for this resource.")]
    [InlineData(415, "Unsupported media type: send application/json.")]
    [InlineData(406, "Not Acceptable")]
    [InlineData(599, "The request could not be processed.")]
    public async Task StatusOnly_UsesAMessageForTheStatus(int status, string expected)
    {
        var body = await WriteAsync(new ProblemDetails { Status = status });

        body.Message.Should().Be(expected);
        body.Errors.Should().ContainSingle().Which.StatusCode.Should().Be((short)status);
    }

    [Fact]
    public async Task DetailWins_ThenTitle()
    {
        (await WriteAsync(new ProblemDetails { Status = 429, Title = "Too many", Detail = "Slow down" })).Message.Should().Be("Slow down");
        (await WriteAsync(new ProblemDetails { Status = 429, Title = "Too many" })).Message.Should().Be("Too many");
    }

    private static async Task<Response<ApiError>> WriteAsync(ProblemDetails problem)
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();

        await new ApiErrorProblemDetailsService().WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });

        context.Response.StatusCode.Should().Be(problem.Status);
        context.Response.Body.Position = 0;
        return (await JsonSerializer.DeserializeAsync<Response<ApiError>>(context.Response.Body, JsonSerializerOptions.Web))!;
    }
}
