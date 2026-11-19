using System.Net;
using System.Text;
using FluentAssertions;
using GSS.Core.Exceptions;
using GSS.Infrastructure.Helpers;
using GSS.UnitTests.Common;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Helpers;

/// <summary>
/// Unit tests for HttpErrorHandler.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class HttpErrorHandlerTests
{
    #region EnsureSuccessAsync Tests

    [Fact]
    public async Task EnsureSuccessAsync_SuccessStatusCode_DoesNotThrow()
    {
        // Arrange
        HttpResponseMessage response = new(HttpStatusCode.OK);

        // Act
        Func<Task> act = async () => await HttpErrorHandler.EnsureSuccessAsync(response, "/api/test", "GET");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureSuccessAsync_BadRequest_ThrowsHttpRequestException()
    {
        // Arrange
        HttpResponseMessage response = new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"Bad request\"}", Encoding.UTF8, "application/json")
        };

        // Act
        Func<Task> act = async () => await HttpErrorHandler.EnsureSuccessAsync(response, "/api/test", "GET");

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EnsureSuccessAsync_Unauthorized_ThrowsHttpRequestException()
    {
        // Arrange
        HttpResponseMessage response = new(HttpStatusCode.Unauthorized);

        // Act
        Func<Task> act = async () => await HttpErrorHandler.EnsureSuccessAsync(response, "/api/test", "GET");

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EnsureSuccessAsync_NotFound_ThrowsHttpRequestException()
    {
        // Arrange
        HttpResponseMessage response = new(HttpStatusCode.NotFound);

        // Act
        Func<Task> act = async () => await HttpErrorHandler.EnsureSuccessAsync(response, "/api/test", "GET");

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EnsureSuccessAsync_InternalServerError_ThrowsHttpRequestException()
    {
        // Arrange
        HttpResponseMessage response = new(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Server error", Encoding.UTF8, "text/plain")
        };

        // Act
        Func<Task> act = async () => await HttpErrorHandler.EnsureSuccessAsync(response, "/api/test", "GET");

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task EnsureSuccessAsync_Forbidden_ThrowsHttpRequestException()
    {
        // Arrange
        HttpResponseMessage response = new(HttpStatusCode.Forbidden);

        // Act
        Func<Task> act = async () => await HttpErrorHandler.EnsureSuccessAsync(response, "/api/test", "GET");

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EnsureSuccessAsync_TooManyRequests_ThrowsHttpRequestException()
    {
        // Arrange
        HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);

        // Act
        Func<Task> act = async () => await HttpErrorHandler.EnsureSuccessAsync(response, "/api/test", "GET");

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task EnsureSuccessAsync_ServiceUnavailable_ThrowsHttpRequestException()
    {
        // Arrange
        HttpResponseMessage response = new(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("Service unavailable", Encoding.UTF8, "text/plain")
        };

        // Act
        Func<Task> act = async () => await HttpErrorHandler.EnsureSuccessAsync(response, "/api/test", "GET");

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task EnsureSuccessAsync_ContentLengthExceedsLimit_DoesNotReadContent()
    {
        // Arrange - response with Content-Length > 10KB triggers the "too large" path
        HttpResponseMessage response = new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("small", Encoding.UTF8, "text/plain")
        };
        response.Content.Headers.ContentLength = 15 * 1024; // 15KB

        // Act
        Func<Task> act = async () => await HttpErrorHandler.EnsureSuccessAsync(response, "/api/test", "GET");

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("too large");
    }

    [Fact]
    public async Task EnsureSuccessAsync_LargeErrorContent_TruncatesContent()
    {
        // Arrange
        string largeContent = new string('A', 20 * 1024); // 20KB
        HttpResponseMessage response = new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(largeContent, Encoding.UTF8, "text/plain")
        };

        // Act
        Func<Task> act = async () => await HttpErrorHandler.EnsureSuccessAsync(response, "/api/test", "GET");

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().ContainAny("Truncated", "too large");
    }

    #endregion

    #region ToExternalServiceException Tests

    public static TheoryData<Exception, ExternalServiceFailure> OutboundFailures => new()
    {
        { new HttpRequestException("Connection refused"), ExternalServiceFailure.Failed },
        { new HttpRequestException("MIS returned 500", null, HttpStatusCode.InternalServerError), ExternalServiceFailure.Failed },
        { new TaskCanceledException("client timeout", new TimeoutException()), ExternalServiceFailure.Timeout },
        { new TimeoutRejectedException("attempt timed out"), ExternalServiceFailure.Timeout },
        { new BrokenCircuitException("circuit open"), ExternalServiceFailure.Unavailable },
        { new RateLimiterRejectedException("limited"), ExternalServiceFailure.Unavailable },
    };

    [Theory]
    [MemberData(nameof(OutboundFailures))]
    public void ToExternalServiceException_OutboundFailure_KeepsTheCauseAndNamesTheCall(Exception failure, ExternalServiceFailure expected)
    {
        ExternalServiceException? result = HttpErrorHandler.ToExternalServiceException(failure, "MisService", "GET", "/api/roles");

        result.Should().NotBeNull();
        result!.Failure.Should().Be(expected);
        result.ServiceName.Should().Be("MisService");
        result.InnerException.Should().BeSameAs(failure);
        result.Message.Should().Contain("MisService").And.Contain("GET").And.Contain("/api/roles");
    }

    [Fact]
    public void ToExternalServiceException_ErrorResponse_KeepsTheUpstreamStatus()
    {
        HttpRequestException failure = new("Not found", null, HttpStatusCode.NotFound);

        ExternalServiceException? result = HttpErrorHandler.ToExternalServiceException(failure, "MisService", "GET", "/api/roles");

        result!.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void ToExternalServiceException_RateLimited_KeepsTheRetryAfter()
    {
        RateLimiterRejectedException failure = new(TimeSpan.FromSeconds(7));

        ExternalServiceException? result = HttpErrorHandler.ToExternalServiceException(failure, "MisService", "GET", "/api/roles");

        result!.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
    }

    public static TheoryData<Exception> NotOutboundFailures => new()
    {
        new TaskCanceledException("caller canceled"),
        new OperationCanceledException(),
        new System.Text.Json.JsonException("bad payload"),
        new ArgumentException("blank endpoint"),
    };

    [Theory]
    [MemberData(nameof(NotOutboundFailures))]
    public void ToExternalServiceException_CancellationOrBug_IsNotTranslated(Exception failure)
    {
        HttpErrorHandler.ToExternalServiceException(failure, "MisService", "GET", "/api/roles").Should().BeNull();
    }

    #endregion
}
