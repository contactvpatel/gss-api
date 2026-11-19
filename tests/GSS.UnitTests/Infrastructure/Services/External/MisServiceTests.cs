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
using Polly.CircuitBreaker;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Services.External;

/// <summary>
/// Unit tests for MisService.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class MisServiceTests
{
    private readonly Mock<IOptions<MisServiceOptions>> _mockOptions;
    private readonly Mock<ILogger<MisService>> _mockLogger;

    public MisServiceTests()
    {
        _mockOptions = new Mock<IOptions<MisServiceOptions>>();
        _mockLogger = new Mock<ILogger<MisService>>();

        _mockOptions.Setup(o => o.Value).Returns(new MisServiceOptions
        {
            Endpoint = new MisServiceEndpoints
            {
                Department = "/api/departments",
                RoleType = "/api/roletypes",
                Role = "/api/roles",
                Position = "/api/positions",
                PersonPosition = "/api/personpositions"
            }
        });
    }

    private MisService CreateService(HttpClient httpClient)
    {
        return new MisService(httpClient, Mock.Of<IDapperTransactionManager>(), _mockOptions.Object, _mockLogger.Object);
    }

    #region GetAllDepartmentsAsync Tests

    [Fact]
    public async Task GetDepartmentsAsync_ReturnsAllDepartments()
    {
        // Arrange
        List<DepartmentModel> expectedDepartments = new()
        {
            new DepartmentModel { DepartmentId = 1, DepartmentName = "IT", DivisionId = 1 },
            new DepartmentModel { DepartmentId = 2, DepartmentName = "HR", DivisionId = 2 }
        };

        var mockHandler = CreateMockHandler(HttpStatusCode.OK, OkResponse(expectedDepartments));
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") };
        var service = CreateService(httpClient);

        // Act
        var result = await service.GetDepartmentsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        VerifyRequest(mockHandler, HttpMethod.Get, "/api/departments");
    }

    [Fact]
    public async Task GetDepartmentsAsync_NoDepartments_ReturnsEmptyList()
    {
        // Arrange
        var mockHandler = CreateMockHandler(HttpStatusCode.OK, OkResponse(new List<DepartmentModel>()));
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") };
        var service = CreateService(httpClient);

        // Act
        var result = await service.GetDepartmentsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    #endregion

    #region Outbound failures

    [Fact]
    public async Task ErrorResponse_ThrowsExternalServiceException_WithTheUpstreamStatus()
    {
        var mockHandler = CreateMockHandler(HttpStatusCode.ServiceUnavailable, new { });
        var service = CreateService(new HttpClient(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") });

        Func<Task> act = () => service.GetDepartmentsAsync();

        var thrown = (await act.Should().ThrowAsync<ExternalServiceException>()).Which;
        thrown.ServiceName.Should().Be(nameof(MisService));
        thrown.Failure.Should().Be(ExternalServiceFailure.Failed);
        thrown.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>Logged once, by the Api's exception handler: the outbound layer only throws.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ErrorResponse_IsNotLoggedBeforeItReachesTheBoundary(HttpStatusCode status)
    {
        var mockHandler = CreateMockHandler(status, new { detail = "upstream detail" });
        var service = CreateService(new HttpClient(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") });

        Func<Task> act = () => service.GetDepartmentsAsync();

        await act.Should().ThrowAsync<ExternalServiceException>();
        _mockLogger.Verify(
            l => l.Log(
                It.Is<LogLevel>(level => level >= LogLevel.Information),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    /// <summary>The boundary log still has the upstream's (sanitized) error body, via the inner exception.</summary>
    [Fact]
    public async Task ErrorResponse_CarriesTheUpstreamBodyInTheInnerException()
    {
        var mockHandler = CreateMockHandler(HttpStatusCode.InternalServerError, new { detail = "upstream detail" });
        var service = CreateService(new HttpClient(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") });

        Func<Task> act = () => service.GetDepartmentsAsync();

        (await act.Should().ThrowAsync<ExternalServiceException>()).Which.InnerException
            .Should().BeOfType<HttpRequestException>().Which.Message.Should().Contain("upstream detail");
    }

    [Fact]
    public async Task OpenCircuit_ThrowsExternalServiceException_Unavailable()
    {
        var service = CreateService(new HttpClient(CreateThrowingHandler(new BrokenCircuitException("open")).Object) { BaseAddress = new Uri("https://mis.example.com") });

        Func<Task> act = () => service.GetDepartmentsAsync();

        (await act.Should().ThrowAsync<ExternalServiceException>()).Which.Failure.Should().Be(ExternalServiceFailure.Unavailable);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesUntranslated()
    {
        var service = CreateService(new HttpClient(CreateThrowingHandler(new TaskCanceledException("caller")).Object) { BaseAddress = new Uri("https://mis.example.com") });

        Func<Task> act = () => service.GetDepartmentsAsync();

        await act.Should().ThrowExactlyAsync<TaskCanceledException>();
    }

    /// <summary>A 200 reporting failure maps to 502 like any upstream failure, not to a 500.</summary>
    [Fact]
    public async Task SucceededFalse_ThrowsExternalServiceExceptionFailed_WithoutLogging()
    {
        ExternalApiResponse<DepartmentModel> misResponse = new()
        {
            Succeeded = false,
            Errors = [new ApiErrorModel { ErrorId = "MIS-500", StatusCode = 500, Message = "lookup failed" }]
        };
        var service = CreateService(new HttpClient(CreateMockHandler(HttpStatusCode.OK, misResponse).Object) { BaseAddress = new Uri("https://mis.example.com") });

        Func<Task> act = () => service.GetPersonPositionsAsync("person-123");

        var thrown = (await act.Should().ThrowAsync<ExternalServiceException>()).Which;
        thrown.ServiceName.Should().Be(nameof(MisService));
        thrown.Failure.Should().Be(ExternalServiceFailure.Failed);
        thrown.Message.Should().Contain("MIS-500");
        VerifyNothingLoggedAboveDebug();
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public async Task NoResponseBody_ThrowsExternalServiceExceptionFailed_WithoutLogging(string body)
    {
        var service = CreateService(new HttpClient(CreateRawMockHandler(body).Object) { BaseAddress = new Uri("https://mis.example.com") });

        Func<Task> act = () => service.GetPersonPositionsAsync("person-123");

        (await act.Should().ThrowAsync<ExternalServiceException>()).Which.Failure.Should().Be(ExternalServiceFailure.Failed);
        VerifyNothingLoggedAboveDebug();
    }

    /// <summary>A malformed 200 body is an upstream failure (502), not a 500; the parse error stays inner.</summary>
    [Theory]
    [InlineData("<html>gateway error</html>")]
    [InlineData("""{"succeeded":true,"data":[{"deptId":"not-a-number"}]}""")]
    public async Task MalformedJsonBody_ThrowsExternalServiceExceptionFailed_WithoutLogging(string body)
    {
        var service = CreateService(new HttpClient(CreateRawMockHandler(body).Object) { BaseAddress = new Uri("https://mis.example.com") });

        Func<Task> act = () => service.GetDepartmentsAsync();

        var thrown = (await act.Should().ThrowAsync<ExternalServiceException>()).Which;
        thrown.ServiceName.Should().Be(nameof(MisService));
        thrown.Failure.Should().Be(ExternalServiceFailure.Failed);
        thrown.InnerException.Should().BeAssignableTo<System.Text.Json.JsonException>();
        VerifyNothingLoggedAboveDebug();
    }

    #endregion

    #region GetAllRoleTypesAsync Tests

    [Fact]
    public async Task GetAllRoleTypesAsync_ValidDivisionId_ReturnsRoleTypes()
    {
        // Arrange
        const int divisionId = 7;
        List<RoleTypeModel> expectedRoleTypes = new()
        {
            new RoleTypeModel { Id = 1, Name = "Manager", DivisionId = divisionId },
            new RoleTypeModel { Id = 2, Name = "Employee", DivisionId = divisionId }
        };

        var mockHandler = CreateMockHandler(HttpStatusCode.OK, OkResponse(expectedRoleTypes));
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") };
        var service = CreateService(httpClient);

        // Act
        var result = await service.GetAllRoleTypesAsync(divisionId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        VerifyRequest(mockHandler, HttpMethod.Get, "/api/roletypes?divId=7");
    }

    #endregion

    #region GetAllRolesAsync Tests

    [Fact]
    public async Task GetAllRolesAsync_ValidDivisionId_ReturnsRoles()
    {
        // Arrange
        const int divisionId = 7;
        List<RoleModel> expectedRoles = new()
        {
            new RoleModel { Id = 1, Name = "Admin", DivisionId = divisionId },
            new RoleModel { Id = 2, Name = "User", DivisionId = divisionId }
        };

        var mockHandler = CreateMockHandler(HttpStatusCode.OK, OkResponse(expectedRoles));
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") };
        var service = CreateService(httpClient);

        // Act
        var result = await service.GetAllRolesAsync(divisionId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        VerifyRequest(mockHandler, HttpMethod.Get, "/api/roles?divId=7");
    }

    #endregion

    #region GetRoleByIdAsync Tests

    [Fact]
    public async Task GetRoleByIdAsync_ValidId_ReturnsRole()
    {
        // Arrange
        const int roleId = 1;
        RoleModel expectedRole = new() { Id = roleId, Name = "Admin", DivisionId = 1 };

        var mockHandler = CreateMockHandler(HttpStatusCode.OK, OkResponseSingle(expectedRole));
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") };
        var service = CreateService(httpClient);

        // Act
        var result = await service.GetRoleByIdAsync(roleId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(roleId);
        VerifyRequest(mockHandler, HttpMethod.Get, "/api/roles?roleId=1");
    }

    [Fact]
    public async Task GetRoleByIdAsync_EmptyData_ReturnsNull()
    {
        // Arrange
        var mockHandler = CreateMockHandler(HttpStatusCode.OK, OkResponse(new List<RoleModel>()));
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") };
        var service = CreateService(httpClient);

        // Act
        var result = await service.GetRoleByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRoleByIdAsync_ResponseWithOtherRoles_ReturnsOnlyRequestedId()
    {
        // Arrange: MIS ignoring roleId would return unrelated roles; never take the first one blindly.
        var requestedHandler = CreateMockHandler(HttpStatusCode.OK, OkResponse(new List<RoleModel>
        {
            new() { Id = 5, Name = "Other", DivisionId = 2 },
            new() { Id = 1, Name = "Requested", DivisionId = 1 }
        }));
        var requestedService = CreateService(new HttpClient(requestedHandler.Object) { BaseAddress = new Uri("https://mis.example.com") });

        var absentHandler = CreateMockHandler(HttpStatusCode.OK, OkResponse(new List<RoleModel>
        {
            new() { Id = 5, Name = "Other", DivisionId = 2 }
        }));
        var absentService = CreateService(new HttpClient(absentHandler.Object) { BaseAddress = new Uri("https://mis.example.com") });

        // Act
        var requested = await requestedService.GetRoleByIdAsync(1);
        var absent = await absentService.GetRoleByIdAsync(9);

        // Assert
        requested.Should().NotBeNull();
        requested!.Id.Should().Be(1);
        absent.Should().BeNull();
    }

    #endregion

    #region GetRolesByDepartmentIdAsync Tests

    [Fact]
    public async Task GetRolesByDepartmentIdAsync_ValidDepartmentId_ReturnsRoles()
    {
        // Arrange
        const int departmentId = 1;
        List<RoleModel> expectedRoles = new()
        {
            new RoleModel { Id = 1, Name = "Admin", DepartmentId = departmentId }
        };

        var mockHandler = CreateMockHandler(HttpStatusCode.OK, OkResponse(expectedRoles));
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") };
        var service = CreateService(httpClient);

        // Act
        var result = await service.GetRolesByDepartmentIdAsync(departmentId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        VerifyRequest(mockHandler, HttpMethod.Get, $"/api/roles?deptId={departmentId}");
    }

    #endregion

    #region GetPositionsByRoleIdAsync Tests

    [Fact]
    public async Task GetPositionsByRoleIdAsync_ValidRoleId_ReturnsPositions()
    {
        // Arrange
        const int roleId = 1;
        List<PositionModel> expectedPositions = new()
        {
            new PositionModel { Id = 1, Name = "Senior Developer", RoleId = roleId }
        };

        var mockHandler = CreateMockHandler(HttpStatusCode.OK, OkResponse(expectedPositions));
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") };
        var service = CreateService(httpClient);

        // Act
        var result = await service.GetPositionsByRoleIdAsync(roleId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        VerifyRequest(mockHandler, HttpMethod.Get, $"/api/positions?roleId={roleId}");
    }

    #endregion

    #region GetPersonPositionsAsync Tests

    [Fact]
    public async Task GetPersonPositionsAsync_ValidPersonId_ReturnsPositions()
    {
        // Arrange
        const string personId = "123";
        // Literal MIS wire shape: numeric personId, departments under "dept" with deptId/divId
        const string misJson = """
            {"succeeded":true,"data":[{"personId":123,"positionId":1,"roleId":4,"name":"Coordinator",
              "dept":[{"deptId":20,"name":"IT","divId":5},{"deptId":21,"name":"HR","divId":5}]}]}
            """;

        var mockHandler = CreateRawMockHandler(misJson);
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") };
        var service = CreateService(httpClient);

        // Act
        var result = await service.GetPersonPositionsAsync(personId);

        // Assert
        result.Should().NotBeNull();
        var position = result.Should().ContainSingle().Subject;
        position.PersonId.Should().Be(123);
        position.PositionId.Should().Be(1);
        position.RoleId.Should().Be(4);
        position.RoleName.Should().Be("Coordinator");
        position.Department!.Select(d => (d.DepartmentId, d.DivisionId)).Should().Equal((20, 5), (21, 5));
    }

    /// <summary>A null item in "data" is dropped, so lookups and filters never dereference null.</summary>
    [Fact]
    public async Task GetDepartmentsAsync_NullItemInData_IsDropped()
    {
        var mockHandler = CreateRawMockHandler("""{"succeeded":true,"data":[null,{"deptId":20,"name":"IT","divId":3}]}""");
        var service = CreateService(new HttpClient(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") });

        var departments = await service.GetDepartmentsAsync();

        departments.Should().ContainSingle().Which.DepartmentId.Should().Be(20);
    }

    [Fact]
    public async Task GetPersonPositionsAsync_PersonIdWithQueryCharacters_IsEscaped()
    {
        // Arrange
        var mockHandler = CreateRawMockHandler("""{"succeeded":true,"data":[]}""");
        HttpClient httpClient = new(mockHandler.Object) { BaseAddress = new Uri("https://mis.example.com") };
        var service = CreateService(httpClient);

        // Act
        await service.GetPersonPositionsAsync("1&roleId=9");

        // Assert
        VerifyRequest(mockHandler, HttpMethod.Get, "/api/personpositions?personId=1%26roleId%3D9");
    }

    #endregion

    #region MIS wire format

    // Raw MIS JSON (wire names as in asm-api's models), never a serialized GSS model, so a missing [JsonPropertyName] shows up as 0/empty.

    [Fact]
    public async Task GetRoleByIdAsync_MisWireJson_MapsRoleIdNameAndDivision()
    {
        const string misJson = """
            {"succeeded":true,"data":[{"roleId":42,"roleUUID":"6f1c2a3e-0000-4000-8000-000000000001","name":"Coordinator","shortName":"Coord","divId":3,"geoLevelId":2,"geoLevel":"Region","dept":[{"deptId":20,"name":"IT","divId":3}]}]}
            """;
        var service = CreateService(new HttpClient(CreateRawMockHandler(misJson).Object) { BaseAddress = new Uri("https://mis.example.com") });

        var result = await service.GetRoleByIdAsync(42);

        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.Name.Should().Be("Coordinator");
        result.DivisionId.Should().Be(3);
    }

    [Fact]
    public async Task GetAllRoleTypesAsync_MisWireJson_MapsRoleTypeIdAndName()
    {
        const string misJson = """
            {"succeeded":true,"data":[{"roleTypeId":7,"roleTypeUUID":"6f1c2a3e-0000-4000-8000-000000000002","name":"Sanchalak","code":"SAN","geoLevelId":2,"geoLevel":"Region"}]}
            """;
        var service = CreateService(new HttpClient(CreateRawMockHandler(misJson).Object) { BaseAddress = new Uri("https://mis.example.com") });

        var result = await service.GetAllRoleTypesAsync(3);

        var roleType = result.Should().ContainSingle().Subject;
        roleType.Id.Should().Be(7);
        roleType.Name.Should().Be("Sanchalak");
    }

    [Fact]
    public async Task GetPositionsByRoleIdAsync_MisWireJson_MapsPositionIdNameAndRoleId()
    {
        const string misJson = """
            {"succeeded":true,"data":[{"positionId":10,"name":"Regional Coordinator","shortName":"RC","roleId":42,"roleName":"Coordinator","geoLevelId":2,"entityId":1,"entityName":"North","occurance":1,"personId":null,"dept":[{"deptId":20,"name":"IT","divId":3}]}]}
            """;
        var service = CreateService(new HttpClient(CreateRawMockHandler(misJson).Object) { BaseAddress = new Uri("https://mis.example.com") });

        var result = await service.GetPositionsByRoleIdAsync(42);

        var position = result.Should().ContainSingle().Subject;
        position.Id.Should().Be(10);
        position.Name.Should().Be("Regional Coordinator");
        position.RoleId.Should().Be(42);
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

    /// <summary>Wraps list data in the MIS API response shape so the service can deserialize MisResponse{T}.</summary>
    private static ExternalApiResponse<T> OkResponse<T>(List<T> data)
    {
        return new() { Succeeded = true, Data = data };
    }

    /// <summary>Wraps a single item in the MIS API response shape (Data list with one element).</summary>
    private static ExternalApiResponse<T> OkResponseSingle<T>(T item)
    {
        return new() { Succeeded = true, Data = new List<T> { item } };
    }

    private static Mock<HttpMessageHandler> CreateThrowingHandler(Exception exception)
    {
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(exception);
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
