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
/// Unit tests for AsmService.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class AsmServiceTests
{
    private readonly Mock<IOptions<AsmServiceOptions>> _mockOptions;
    private readonly Mock<ILogger<AsmService>> _mockLogger;

    public AsmServiceTests()
    {
        _mockOptions = new Mock<IOptions<AsmServiceOptions>>();
        _mockLogger = new Mock<ILogger<AsmService>>();

        _mockOptions.Setup(o => o.Value).Returns(new AsmServiceOptions
        {
            Endpoint = new AsmServiceEndpoints
            {
                ApplicationSecurity = "/api/applicationsecurity"
            }
        });
    }

    private AsmService CreateService(HttpClient httpClient)
    {
        return new AsmService(httpClient, Mock.Of<IDapperTransactionManager>(), _mockOptions.Object, _mockLogger.Object);
    }

    #region GetApplicationSecurityAsync Tests

    [Fact]
    public async Task GetApplicationSecurityAsync_ValidToken_ReturnsSecurityInfo()
    {
        // Arrange - API returns ExternalApiResponse<AsmResponseModel> with { data, succeeded }
        const string token = "bearer-token";
        ExternalApiResponse<AsmResponseModel> apiResponse = new ExternalApiResponse<AsmResponseModel>
        {
            Data =
            [
                new AsmResponseModel
                {
                    PositionId = 111,
                    RoleId = 111,
                    ApplicationAccess =
                    [
                        new ApplicationAccessModel { ModuleCode = "app-1", ModuleName = "Test App", HasViewAccess = true }
                    ]
                }
            ],
            Succeeded = true
        };

        var mockHandler = CreateMockHandler(HttpStatusCode.OK, apiResponse);
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://asm.example.com") };
        var service = CreateService(httpClient);

        // Act - Infrastructure returns list of AsmResponseModel (parsed from API wrapper)
        var result = await service.GetApplicationSecurityAsync(token);

        // Assert
        result.Should().NotBeNull().And.HaveCount(1);
        result[0].ApplicationAccess.Should().HaveCount(1);
        result[0].ApplicationAccess[0].ModuleCode.Should().Be("app-1");
        result[0].ApplicationAccess[0].ModuleName.Should().Be("Test App");
        VerifyRequest(mockHandler, HttpMethod.Get, "/api/applicationsecurity/");
    }

    [Fact]
    public async Task GetApplicationSecurityAsync_NoSecurity_ReturnsEmptyList()
    {
        // Arrange - API returns { data: [], succeeded: true }
        const string token = "bearer-token";
        ExternalApiResponse<AsmResponseModel> apiResponse = new ExternalApiResponse<AsmResponseModel> { Data = [], Succeeded = true };
        var mockHandler = CreateMockHandler(HttpStatusCode.OK, apiResponse);
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://asm.example.com") };
        var service = CreateService(httpClient);

        // Act
        var result = await service.GetApplicationSecurityAsync(token);

        // Assert
        result.Should().NotBeNull().And.BeEmpty();
    }

    /// <summary>A 200 reporting failure is an outage, not "no permissions": an empty list would be cached until the token expires.</summary>
    [Fact]
    public async Task GetApplicationSecurityAsync_SucceededFalse_ThrowsExternalServiceExceptionFailed()
    {
        ExternalApiResponse<AsmResponseModel> apiResponse = new()
        {
            Succeeded = false,
            Errors = [new ApiErrorModel { ErrorId = "ASM-500", StatusCode = 500, Message = "lookup failed" }]
        };
        var service = CreateService(new HttpClient(CreateMockHandler(HttpStatusCode.OK, apiResponse).Object) { BaseAddress = new Uri("https://asm.example.com") });

        Func<Task> act = () => service.GetApplicationSecurityAsync("bearer-token");

        var thrown = (await act.Should().ThrowAsync<ExternalServiceException>()).Which;
        thrown.ServiceName.Should().Be(nameof(AsmService));
        thrown.Failure.Should().Be(ExternalServiceFailure.Failed);
        thrown.Message.Should().Contain("ASM-500");
        VerifyNothingLoggedAboveDebug();
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public async Task GetApplicationSecurityAsync_NoResponseBody_ThrowsExternalServiceExceptionFailed(string body)
    {
        var service = CreateService(new HttpClient(CreateRawMockHandler(body).Object) { BaseAddress = new Uri("https://asm.example.com") });

        Func<Task> act = () => service.GetApplicationSecurityAsync("bearer-token");

        (await act.Should().ThrowAsync<ExternalServiceException>()).Which.Failure.Should().Be(ExternalServiceFailure.Failed);
        VerifyNothingLoggedAboveDebug();
    }

    #endregion

    #region Helper Methods

    /// <summary>Logged once, by the Api's exception handler.</summary>
    private void VerifyNothingLoggedAboveDebug()
    {
        _mockLogger.Verify(
            l => l.Log(
                It.Is<LogLevel>(level => level >= LogLevel.Information),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    private static Mock<HttpMessageHandler> CreateRawMockHandler(string json)
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
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        return mockHandler;
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
