using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GSS.Core.Exceptions;
using GSS.Core.Models;
using GSS.Infrastructure.Interfaces;
using GSS.Infrastructure.Services.External;
using GSS.UnitTests.Common;
using GSS.Util.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Services.External;

/// <summary>
/// Unit tests for SsoService.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class SsoServiceTests
{
    private readonly Mock<IOptions<SsoServiceOptions>> _mockOptions;
    private readonly Mock<ILogger<SsoService>> _mockLogger;
    private readonly SsoService _service;

    public SsoServiceTests()
    {
        _mockOptions = new Mock<IOptions<SsoServiceOptions>>();
        _mockLogger = new Mock<ILogger<SsoService>>();

        _mockOptions.Setup(o => o.Value).Returns(new SsoServiceOptions
        {
            Url = "https://sso.example.com",
            Endpoint = new SsoServiceEndpoints
            {
                ValidateToken = "/api/validate",
                RenewToken = "/api/renew",
                Logout = "/api/logout"
            }
        });


        _service = CreateService(new HttpClient());
    }

    private SsoService CreateService(HttpClient httpClient)
    {
        return new SsoService(httpClient, Mock.Of<IDapperTransactionManager>(), _mockOptions.Object, _mockLogger.Object);
    }

    #region ValidateTokenAsync Tests

    [Fact]
    public async Task PostBodies_AreCamelCaseUtf8Json_WithAContentLength()
    {
        // A known length: the body is not sent chunked, which some servers and proxies reject.
        CapturingHandler handler = new();
        var service = CreateService(new HttpClient(handler) { BaseAddress = new Uri("https://sso.example.com") });

        await service.RenewTokenAsync("access", "refresh");

        handler.Bodies.Should().HaveCount(1);
        handler.Bodies[0].Body.Should().Contain(@"""refreshToken"":""refresh""");
        foreach ((string body, long? length, string? mediaType, string? charSet) in handler.Bodies)
        {
            length.Should().Be(System.Text.Encoding.UTF8.GetByteCount(body));
            mediaType.Should().Be("application/json");
            charSet.Should().Be("utf-8");
        }
    }

    [Fact]
    public async Task ValidateTokenAsync_SendsTheRawTokenAsAuthorizationHeader_WithNoBody()
    {
        CapturingHandler handler = new();
        var service = CreateService(new HttpClient(handler) { BaseAddress = new Uri("https://sso.example.com") });

        await service.ValidateTokenAsync("valid-token");

        handler.Authorizations.Should().ContainSingle().Which.Should().Be("valid-token");
        handler.Bodies.Should().BeEmpty("the token is the bearer credential, not a body");
    }

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_ReturnsTrue()
    {
        // Arrange
        const string token = "valid-token";
        var mockHandler = CreateMockHandler(HttpStatusCode.OK, true);
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://sso.example.com") };
        var service = CreateService(httpClient);

        // Act
        bool result = await service.ValidateTokenAsync(token);

        // Assert
        result.Should().BeTrue();
        VerifyRequest(mockHandler, HttpMethod.Post, "/api/validate");
    }

    [Fact]
    public async Task ValidateTokenAsync_InvalidToken_ReturnsFalse()
    {
        // Arrange
        const string token = "invalid-token";
        var mockHandler = CreateMockHandler(HttpStatusCode.Unauthorized, false);
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://sso.example.com") };
        var service = CreateService(httpClient);

        // Act
        bool result = await service.ValidateTokenAsync(token);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ValidateTokenAsync_SsoRejectsTheToken_ReturnsFalse(HttpStatusCode status)
    {
        var service = CreateService(new HttpClient(CreateMockHandler(status, false).Object) { BaseAddress = new Uri("https://sso.example.com") });

        bool result = await service.ValidateTokenAsync("token");

        result.Should().BeFalse();
    }

    /// <summary>A rejection is logged once, by the authentication handler.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ValidateTokenAsync_SsoRejectsTheToken_IsNotLoggedHere(HttpStatusCode status)
    {
        var service = CreateService(new HttpClient(CreateMockHandler(status, false).Object) { BaseAddress = new Uri("https://sso.example.com") });

        await service.ValidateTokenAsync("token");

        _mockLogger.Verify(
            l => l.Log(
                It.Is<LogLevel>(level => level >= LogLevel.Information),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ValidateTokenAsync_SsoCannotAnswer_ThrowsExternalServiceException(HttpStatusCode status)
    {
        // An outage, or a 404/405 from a wrong URL or endpoint, is not a verdict on the token: never "invalid" (401).
        var service = CreateService(new HttpClient(CreateMockHandler(status, false).Object) { BaseAddress = new Uri("https://sso.example.com") });

        Func<Task> act = () => service.ValidateTokenAsync("token");

        (await act.Should().ThrowAsync<ExternalServiceException>()).Which.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task ValidateTokenAsync_ConnectionFails_ThrowsExternalServiceException()
    {
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));
        var service = CreateService(new HttpClient(mockHandler.Object) { BaseAddress = new Uri("https://sso.example.com") });

        Func<Task> act = () => service.ValidateTokenAsync("token");

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task ValidateTokenAsync_NullToken_ReturnsFalse()
    {
        // Act
        bool result = await _service.ValidateTokenAsync(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTokenAsync_EmptyToken_ReturnsFalse()
    {
        // Act
        bool result = await _service.ValidateTokenAsync(string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTokenAsync_WhitespaceToken_ReturnsFalse()
    {
        // Act
        bool result = await _service.ValidateTokenAsync("   ");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region RenewTokenAsync Tests

    [Theory]
    [InlineData("", "no response body")]
    [InlineData("null", "no response body")]
    [InlineData("{}", "no access or refresh token")]
    [InlineData("{\"accessToken\":\"new-access\",\"refreshToken\":\"  \"}", "no access or refresh token")]
    public async Task RenewTokenAsync_SuccessWithoutTokens_ThrowsExternalServiceExceptionFailed(string body, string reason)
    {
        Mock<HttpMessageHandler> handler = new(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        using HttpClient client = new(handler.Object, disposeHandler: false) { BaseAddress = new Uri("https://sso.example.com") };
        var service = CreateService(client);

        Func<Task> act = () => service.RenewTokenAsync("access", "refresh");

        var thrown = (await act.Should().ThrowAsync<ExternalServiceException>()).Which;
        thrown.Failure.Should().Be(ExternalServiceFailure.Failed);
        thrown.Message.Should().Contain(reason);
    }

    [Fact]
    public async Task RenewTokenAsync_ValidTokens_ReturnsAuthResponse()
    {
        // Arrange
        const string accessToken = "access-token";
        const string refreshToken = "refresh-token";
        SsoAuthResponse expectedResponse = new()
        {
            AccessToken = "new-access-token",
            RefreshToken = "new-refresh-token"
        };

        var mockHandler = CreateMockHandler(HttpStatusCode.OK, expectedResponse);
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://sso.example.com") };
        var service = CreateService(httpClient);

        // Act
        var result = await service.RenewTokenAsync(accessToken, refreshToken);

        // Assert
        result.Should().NotBeNull();
        result!.AccessToken.Should().Be(expectedResponse.AccessToken);
        result.RefreshToken.Should().Be(expectedResponse.RefreshToken);
        VerifyRequest(mockHandler, HttpMethod.Post, "/api/renew");
    }

    [Fact]
    public async Task RenewTokenAsync_MalformedJsonBody_ThrowsExternalServiceExceptionFailed()
    {
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>proxy error</html>", System.Text.Encoding.UTF8, "application/json")
            });
        var service = CreateService(new HttpClient(mockHandler.Object) { BaseAddress = new Uri("https://sso.example.com") });

        Func<Task> act = () => service.RenewTokenAsync("access", "refresh");

        var thrown = (await act.Should().ThrowAsync<ExternalServiceException>()).Which;
        thrown.Failure.Should().Be(ExternalServiceFailure.Failed);
        thrown.InnerException.Should().BeAssignableTo<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task RenewTokenAsync_NullAccessToken_ReturnsNull()
    {
        // Act
        var result = await _service.RenewTokenAsync(null!, "refresh-token");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task RenewTokenAsync_NullRefreshToken_ReturnsNull()
    {
        // Act
        var result = await _service.RenewTokenAsync("access-token", null!);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>E.g. an expired refresh token: null (401, log in again), not an outage (502) the client retries.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RenewTokenAsync_SsoRefuses_ReturnsNull(HttpStatusCode status)
    {
        var service = CreateService(new HttpClient(CreateMockHandler(status, false).Object) { BaseAddress = new Uri("https://sso.example.com") });

        var result = await service.RenewTokenAsync("access-token", "refresh-token");

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RenewTokenAsync_SsoCannotAnswer_ThrowsExternalServiceException(HttpStatusCode status)
    {
        var service = CreateService(new HttpClient(CreateMockHandler(status, false).Object) { BaseAddress = new Uri("https://sso.example.com") });

        Func<Task> act = () => service.RenewTokenAsync("access-token", "refresh-token");

        (await act.Should().ThrowAsync<ExternalServiceException>()).Which.StatusCode.Should().Be(status);
    }

    #endregion

    #region LogoutAsync Tests

    [Fact]
    public async Task LogoutAsync_ValidToken_ReturnsTrue()
    {
        // Arrange
        const string token = "valid-token";
        var mockHandler = CreateMockHandler(HttpStatusCode.OK, true);
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://sso.example.com") };
        var service = CreateService(httpClient);

        // Act
        bool result = await service.LogoutAsync(token);

        // Assert
        result.Should().BeTrue();
        VerifyRequest(mockHandler, HttpMethod.Post, "/api/logout");
    }

    [Fact]
    public async Task LogoutAsync_NullToken_ReturnsFalse()
    {
        // Act
        bool result = await _service.LogoutAsync(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task LogoutAsync_EmptyToken_ReturnsFalse()
    {
        // Act
        bool result = await _service.LogoutAsync(string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task LogoutAsync_SsoRefuses_ReturnsFalse(HttpStatusCode status)
    {
        var service = CreateService(new HttpClient(CreateMockHandler(status, false).Object) { BaseAddress = new Uri("https://sso.example.com") });

        bool result = await service.LogoutAsync("token");

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task LogoutAsync_SsoCannotAnswer_ThrowsExternalServiceException(HttpStatusCode status)
    {
        var service = CreateService(new HttpClient(CreateMockHandler(status, false).Object) { BaseAddress = new Uri("https://sso.example.com") });

        Func<Task> act = () => service.LogoutAsync("token");

        (await act.Should().ThrowAsync<ExternalServiceException>()).Which.StatusCode.Should().Be(status);
    }

    #endregion

    #region Helper Methods

    /// <summary>Answers 200 with an empty JSON object and records each request body as sent.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(string Body, long? Length, string? MediaType, string? CharSet)> Bodies { get; } = [];

        public List<string?> Authorizations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.Authorization?.ToString());
            if (request.Content is not { } content)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("true") };
            }

            // Headers before the body: reading buffers the content, which makes any length known.
            long? length = content.Headers.ContentLength;
            string? mediaType = content.Headers.ContentType?.MediaType;
            string? charSet = content.Headers.ContentType?.CharSet;
            Bodies.Add((await content.ReadAsStringAsync(cancellationToken), length, mediaType, charSet));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{""token"":""new-access"",""refreshToken"":""new-refresh""}") };
        }
    }

    private static Mock<HttpMessageHandler> CreateMockHandler<T>(HttpStatusCode statusCode, T response)
    {
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = JsonContent.Create(response)
            });
        return mockHandler;
    }

    private static void VerifyRequest(Mock<HttpMessageHandler> mockHandler, HttpMethod method, string endpoint)
    {
        mockHandler
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == method &&
                    req.RequestUri!.PathAndQuery.Contains(endpoint)),
                ItExpr.IsAny<CancellationToken>());
    }

    #endregion
}
