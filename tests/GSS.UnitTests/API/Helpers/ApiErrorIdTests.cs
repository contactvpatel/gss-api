using FluentAssertions;
using GSS.Api.Helpers;
using GSS.UnitTests.Common;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GSS.UnitTests.API.Helpers;

/// <summary>
/// Unit tests for ApiErrorId fallbacks; the correlation-id path is covered by MiddlewarePipelineOrderTests and ErrorIdIntegrationTests.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ApiErrorIdTests
{
    [Fact]
    public void For_WithoutCorrelationMiddleware_ReturnsTraceIdentifier()
    {
        DefaultHttpContext context = new() { TraceIdentifier = "0HN-trace:00000001" };

        ApiErrorId.For(context).Should().Be("0HN-trace:00000001");
    }

    [Fact]
    public void For_WithoutRequest_ReturnsNewGuid()
    {
        string errorId = ApiErrorId.For(null);

        Guid.TryParse(errorId, out _).Should().BeTrue();
        ApiErrorId.For(null).Should().NotBe(errorId);
    }
}
