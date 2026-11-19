using System.Data.Common;
using System.Net;
using System.Text;
using FluentAssertions;
using GSS.Api.Middleware;
using GSS.Api.Models;
using GSS.Core.Exceptions;
using GSS.UnitTests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Xunit;

namespace GSS.UnitTests.API.Middleware;

/// <summary>
/// Unit tests for ApiExceptionHandler. The framework middleware around it is covered by MiddlewarePipelineOrderTests.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ApiExceptionHandlerTests
{
    private readonly Mock<ILogger<ApiExceptionHandler>> _mockLogger = new();
    private readonly ApiExceptionHandler _handler;

    public ApiExceptionHandlerTests()
    {
        _handler = new ApiExceptionHandler(_mockLogger.Object);
    }

    #region TryHandleAsync Tests

    [Fact]
    public async Task TryHandleAsync_AnyException_ReturnsTrue()
    {
        bool handled = await _handler.TryHandleAsync(CreateHttpContext(), new Exception("Boom"), CancellationToken.None);

        handled.Should().BeTrue();
    }

    [Fact]
    public async Task TryHandleAsync_UsesTheAppsHttpJsonOptions()
    {
        // Arrange - the body is serialized like every other response, not with private options
        ServiceCollection services = new();
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.WriteIndented = true);
        var context = CreateHttpContext();
        context.RequestServices = services.BuildServiceProvider();

        // Act
        await _handler.TryHandleAsync(context, new Exception("Boom"), CancellationToken.None);

        // Assert
        (await ReadBodyAsync(context)).Should().Contain("\n  \"succeeded\": false");
    }

    /// <summary>A database error reads like any other 500: the response never says which backend failed.</summary>
    [Fact]
    public async Task TryHandleAsync_PostgresException_DoesNotNameTheDatabase()
    {
        var context = CreateHttpContext();
        InvalidOperationException exception = new("Query failed", new PostgresException("boom", "ERROR", "ERROR", "XX000"));

        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        string body = await ReadBodyAsync(context);
        body.Should().NotContainEquivalentOf("database");
        body.Should().Contain("Error occurred in the API. Please use the ErrorId");
    }

    public static TheoryData<Exception, int> EveryOutcome() => new()
    {
        { new Exception("Boom"), 500 },
        { new InvalidOperationException("Query failed", new PostgresException("boom", "ERROR", "ERROR", "22012")), 500 },
        { new InvalidOperationException("Query failed", new PostgresException("boom", "ERROR", "ERROR", "23505")), 409 },
        { new InvalidOperationException("Query failed", new NpgsqlException("Failed to connect", new TimeoutException())), 503 },
        { Upstream(ExternalServiceFailure.Timeout), 504 },
        { Upstream(ExternalServiceFailure.Unavailable), 503 },
        { Upstream(ExternalServiceFailure.Failed), 502 },
        { new BadHttpRequestException("Request body too large.", 413), 413 },
        { new BadHttpRequestException("Reading the request body timed out.", 408), 408 },
        { new BadHttpRequestException("Unexpected end of request content.", 400), 400 },
    };

    /// <summary>One error, so the top-level message is that error's message, as in every other envelope; never blank.</summary>
    [Theory]
    [MemberData(nameof(EveryOutcome))]
    public async Task TryHandleAsync_TopLevelMessage_IsTheErrorsMessage(Exception exception, int expectedStatus)
    {
        var context = CreateHttpContext();

        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.StatusCode.Should().Be(expectedStatus);
        var body = (await System.Text.Json.JsonSerializer.DeserializeAsync<Response<ApiError>>(
            RewoundBody(context), System.Text.Json.JsonSerializerOptions.Web))!;
        body.Message.Should().NotBeNullOrWhiteSpace();
        body.Errors.Should().ContainSingle().Which.Message.Should().Be(body.Message);
    }

    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(ArgumentNullException))]
    [InlineData(typeof(ArgumentOutOfRangeException))]
    public async Task TryHandleAsync_ArgumentExceptions_AreServerErrors(Type exceptionType)
    {
        // Argument exceptions are broken guard clauses (bugs), not client input: input is rejected by validators,
        // route constraints or Result.Failure before code that guards its arguments.
        var context = CreateHttpContext();
        Exception exception = (Exception)Activator.CreateInstance(exceptionType, "Invalid argument")!;

        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        await VerifyErrorResponse(context, HttpStatusCode.InternalServerError);
        VerifyLoggedAt(LogLevel.Error);
    }

    [Fact]
    public async Task TryHandleAsync_UnauthorizedAccessException_IsServerError()
    {
        // A file-system/OS permission failure, not caller authorization (that is 401/403 from the auth handlers).
        var context = CreateHttpContext();

        await _handler.TryHandleAsync(context, new UnauthorizedAccessException("Access to the path is denied"), CancellationToken.None);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        VerifyLoggedAt(LogLevel.Error);
    }

    /// <summary>
    /// Nothing throws it on purpose (a missing resource is Result.NotFound), so it can only be a dictionary-indexer bug:
    /// a 500 at Error that alerting sees, never a client 404 at Information.
    /// </summary>
    [Fact]
    public async Task TryHandleAsync_KeyNotFoundException_IsServerError()
    {
        // Arrange
        var context = CreateHttpContext();
        KeyNotFoundException exception = new("The given key 'Customer' was not present in the dictionary.");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        await VerifyErrorResponse(context, HttpStatusCode.InternalServerError);
        VerifyLoggedAt(LogLevel.Error);
    }

    [Fact]
    public async Task TryHandleAsync_InvalidOperationException_WithNotFoundMessage_ReturnsInternalServerError()
    {
        // Arrange - message text never chooses the status (R-06)
        var context = CreateHttpContext();
        InvalidOperationException exception = new("Entity not found");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        await VerifyErrorResponse(context, HttpStatusCode.InternalServerError);
    }

    /// <summary>A refused or broken connection: 503 (retryable) and Critical, without naming the database.</summary>
    [Fact]
    public async Task TryHandleAsync_DatabaseConnectionRefused_Returns503AndLogsCritical()
    {
        // Arrange - Npgsql's shape for a refused connection, wrapped as callers often do
        var context = CreateHttpContext();
        InvalidOperationException exception = new("Query failed", new NpgsqlException("Failed to connect", new System.Net.Sockets.SocketException(61)));

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
        string body = await ReadBodyAsync(context);
        body.Should().Contain("temporarily unavailable").And.NotContainEquivalentOf("database");
        VerifyLoggedAt(LogLevel.Critical);
    }

    /// <summary>
    /// Npgsql 10 raises a command timeout, an exhausted pool and a connect timeout alike: no SQLSTATE, an inner
    /// TimeoutException. A slow query must not page on-call: 503 at Error.
    /// </summary>
    [Theory]
    [InlineData("Exception while reading from stream")]
    [InlineData("The connection pool has been exhausted")]
    public async Task TryHandleAsync_DatabaseTimeout_Returns503AndLogsError(string message)
    {
        var context = CreateHttpContext();

        await _handler.TryHandleAsync(context, new NpgsqlException(message, new TimeoutException()), CancellationToken.None);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
        VerifyLoggedAt(LogLevel.Error);
    }

    [Fact]
    public async Task TryHandleAsync_PostgresServerError_Returns500AndLogsError()
    {
        // Arrange - the server answered, so the database is reachable
        var context = CreateHttpContext();
        PostgresException exception = new("division by zero", "ERROR", "ERROR", "22012");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        VerifyLoggedAt(LogLevel.Error);
    }

    /// <summary>
    /// Concurrent requests: a unique index catching a race, or a deadlock/serialization failure (rolled back, safe to
    /// retry), is a 409 with a message that never names the database.
    /// </summary>
    [Theory]
    [InlineData("23505", "conflicts with existing data", LogLevel.Information)]
    [InlineData("40P01", "concurrent change", LogLevel.Warning)]
    [InlineData("40001", "concurrent change", LogLevel.Warning)]
    public async Task TryHandleAsync_ConcurrencySqlState_Returns409(string sqlState, string message, LogLevel expected)
    {
        // Arrange - wrapped, as a caller may rethrow it
        var context = CreateHttpContext();
        InvalidOperationException exception = new("Write failed", new PostgresException("server message", "ERROR", "ERROR", sqlState));

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        string body = await ReadBodyAsync(context);
        body.Should().Contain(message).And.NotContainEquivalentOf("database").And.NotContain("server message");
        VerifyLoggedAt(expected);
    }

    [Fact]
    public async Task TryHandleAsync_ConnectionWordingWithoutDatabaseException_IsNotCritical()
    {
        // Arrange - wording alone never escalates the log level
        var context = CreateHttpContext();
        Exception exception = new("An error occurred using the connection to database 'x'");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        VerifyLoggedAt(LogLevel.Error);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.ServiceUnavailable, LogLevel.Critical)]
    [InlineData("22012", HttpStatusCode.InternalServerError, LogLevel.Error)]
    [InlineData("40001", HttpStatusCode.Conflict, LogLevel.Warning)]
    public async Task TryHandleAsync_AnyProvidersDbException_Is503AndCriticalOnlyWithoutASqlState(string? sqlState, HttpStatusCode status, LogLevel expected)
    {
        // Arrange - no Npgsql type: the handler reads ADO.NET's DbException.SqlState
        var context = CreateHttpContext();
        InvalidOperationException exception = new("Query failed", new FakeDbException(sqlState));

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)status);
        VerifyLoggedAt(expected);
    }

    private sealed class FakeDbException(string? sqlState) : DbException("database failure")
    {
        public override string? SqlState => sqlState;
    }

    private void VerifyLoggedAt(LogLevel level)
    {
        _mockLogger.Verify(
            l => l.Log(level, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task TryHandleAsync_OperationCanceledException_ClientAborted_Returns499()
    {
        var context = CreateHttpContext();
        using CancellationTokenSource aborted = new();
        aborted.Cancel();
        context.RequestAborted = aborted.Token;

        await _handler.TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

        context.Response.StatusCode.Should().Be(499);
        VerifyLoggedAt(LogLevel.Information);
    }

    [Fact]
    public async Task TryHandleAsync_OperationCanceledException_NotClientAborted_IsServerError()
    {
        // A server-side cancellation (internal timeout, shutdown) is not the client's doing.
        var context = CreateHttpContext();

        await _handler.TryHandleAsync(context, new TaskCanceledException("internal timeout"), CancellationToken.None);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        (await ReadBodyAsync(context)).Should().NotContain("canceled by the client");
        VerifyLoggedAt(LogLevel.Error);
    }

    public static TheoryData<Exception, int, LogLevel> UpstreamFailures => new()
    {
        { Upstream(ExternalServiceFailure.Failed), 502, LogLevel.Error },
        { Upstream(ExternalServiceFailure.Unavailable), 503, LogLevel.Warning },
        { Upstream(ExternalServiceFailure.Timeout), 504, LogLevel.Error },
        // Found inside a wrapper too.
        { new InvalidOperationException("wrapped", Upstream(ExternalServiceFailure.Timeout)), 504, LogLevel.Error },
    };

    private static ExternalServiceException Upstream(ExternalServiceFailure failure) =>
        new("MisService", failure, "MisService GET /api/roles failed.", new HttpRequestException("inner detail"));

    [Theory]
    [MemberData(nameof(UpstreamFailures))]
    public async Task TryHandleAsync_UpstreamFailure_MapsToGatewayStatus(Exception exception, int expectedStatus, LogLevel expectedLevel)
    {
        var context = CreateHttpContext();

        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.StatusCode.Should().Be(expectedStatus);
        string body = await ReadBodyAsync(context);
        body.Should().Contain("depends on");
        body.Should().NotContain(exception.Message);
        VerifyLoggedAt(expectedLevel);
    }

    [Fact]
    public async Task TryHandleAsync_UpstreamUnavailable_WithRetryAfter_SetsRetryAfterHeader()
    {
        var context = CreateHttpContext();
        ExternalServiceException exception = new("MisService", ExternalServiceFailure.Unavailable, "limited")
        {
            RetryAfter = TimeSpan.FromSeconds(6.2)
        };

        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().Should().Be("7");
    }

    [Fact]
    public async Task TryHandleAsync_BareHttpRequestException_IsServerError()
    {
        // Outbound failures arrive as ExternalServiceException; anything else is not classified as upstream.
        var context = CreateHttpContext();

        await _handler.TryHandleAsync(context, new HttpRequestException("untranslated"), CancellationToken.None);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task TryHandleAsync_BadHttpRequestException_413_ReturnsPayloadTooLarge()
    {
        // Arrange
        var context = CreateHttpContext();
        BadHttpRequestException exception = new("Payload too large", 413);

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.RequestEntityTooLarge);
        await VerifyErrorResponse(context, HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task TryHandleAsync_BadHttpRequestException_413_WithExceedsMessage_ReturnsPayloadTooLarge()
    {
        // Arrange - tests GetPayloadTooLargeMessage "exceeds" branch
        var context = CreateHttpContext();
        BadHttpRequestException exception = new("Request size exceeds the limit", 413);

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.RequestEntityTooLarge);
        await VerifyErrorResponse(context, HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task TryHandleAsync_BadHttpRequestException_413_WithGenericMessage_ReturnsDefaultMessage()
    {
        // Arrange - tests GetPayloadTooLargeMessage default branch (neither "Request body too large" nor "exceeds")
        var context = CreateHttpContext();
        BadHttpRequestException exception = new("Payload too big", 413);

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.RequestEntityTooLarge);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        string body = await reader.ReadToEndAsync();
        body.Should().Contain("exceeds the maximum allowed limit");
    }

    /// <summary>
    /// Kestrel request-read failures are the client's fault: their own 4xx, logged at Information (413 at Warning),
    /// never a 500 at Error that would page on-call. The exception's text is never returned.
    /// </summary>
    [Theory]
    [InlineData(400, "Unexpected end of request content.", "could not be read", LogLevel.Information)]
    [InlineData(408, "Reading the request body timed out due to data arriving too slowly.", "not received in time", LogLevel.Information)]
    [InlineData(431, "Request headers too long.", "could not be read", LogLevel.Information)]
    [InlineData(413, "Request body too large.", "exceeds the maximum allowed limit", LogLevel.Warning)]
    [InlineData(500, "Odd status from the server.", "could not be read", LogLevel.Information)]
    public async Task TryHandleAsync_BadHttpRequestException_UsesItsOwnClientStatus(int status, string exceptionMessage, string clientMessage, LogLevel level)
    {
        var context = CreateHttpContext();

        await _handler.TryHandleAsync(context, new BadHttpRequestException(exceptionMessage, status), CancellationToken.None);

        int expected = status is >= 400 and < 500 ? status : 400;
        context.Response.StatusCode.Should().Be(expected);
        string body = await ReadBodyAsync(context);
        body.Should().Contain(clientMessage).And.NotContain(exceptionMessage);
        VerifyLoggedAt(level);
    }

    [Fact]
    public async Task TryHandleAsync_WrappedBadHttpRequestException_IsStillAClientError()
    {
        var context = CreateHttpContext();
        InvalidOperationException exception = new("Binding failed", new BadHttpRequestException("Unexpected end of request content.", 400));

        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        VerifyLoggedAt(LogLevel.Information);
    }

    [Fact]
    public async Task TryHandleAsync_ApplicationException_ReturnsInternalServerErrorWithoutEchoingMessage()
    {
        // Arrange - ApplicationException.Message is internal diagnostics, never client-facing
        var context = CreateHttpContext();
        ApplicationException exception = new("Application error");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        string body = await ReadBodyAsync(context);
        body.Should().NotContain("Application error");
        body.Should().Contain("contact support team");
    }

    [Fact]
    public async Task TryHandleAsync_InvalidOperationException_WithoutNotFoundMessage_ReturnsInternalServerError()
    {
        // Arrange - InvalidOperationException without "not found" falls to GetDefaultHandling
        var context = CreateHttpContext();
        InvalidOperationException exception = new("Invalid state");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        await VerifyErrorResponse(context, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task TryHandleAsync_GeneralException_ReturnsInternalServerError()
    {
        // Arrange
        var context = CreateHttpContext();
        Exception exception = new("General error");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        await VerifyErrorResponse(context, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task TryHandleAsync_ExceptionCarryingAnErrorId_StillUsesTheRequestsId()
    {
        // Arrange - without the correlation middleware the request id is the trace identifier
        var context = CreateHttpContext();
        context.TraceIdentifier = "request-id-1";
        Exception exception = new("Some error");
        exception.Data["ErrorId"] = "other-id";

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        string body = await ReadBodyAsync(context);
        body.Should().Contain("\"errorId\":\"request-id-1\"").And.NotContain("other-id");
    }

    #endregion

    #region exception messages are not echoed to clients

    [Fact]
    public async Task TryHandleAsync_ArgumentException_DoesNotEchoMessageOrParameterName()
    {
        // Arrange - ArgumentException.Message appends " (Parameter 'createDto')" and can act as an enumeration oracle
        var context = CreateHttpContext();
        ArgumentException exception = new("Shipping address 42 does not belong to customer 7.", "createDto");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        string body = await ReadBodyAsync(context);
        body.Should().NotContain("createDto");
        body.Should().NotContain("Shipping address 42");
        body.Should().Contain("Please use the ErrorId");
    }

    [Fact]
    public async Task TryHandleAsync_KeyNotFoundException_DoesNotEchoMessage()
    {
        // Arrange
        var context = CreateHttpContext();
        KeyNotFoundException exception = new("Resource 7 has no child with id 42");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        string body = await ReadBodyAsync(context);
        body.Should().NotContain("Resource 7");
        body.Should().Contain("Please use the ErrorId");
    }

    [Fact]
    public async Task TryHandleAsync_UnauthorizedAccessException_DoesNotEchoMessage()
    {
        // Arrange
        var context = CreateHttpContext();
        UnauthorizedAccessException exception = new("User 7 lacks permission GSS.APPLICATION.WRITE");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        string body = await ReadBodyAsync(context);
        body.Should().NotContain("GSS.APPLICATION.WRITE");
        body.Should().Contain("Please use the ErrorId");
    }

    #endregion

    #region Body

    [Fact]
    public async Task TryHandleAsync_ResponseNotStarted_WritesTheErrorBody()
    {
        // Arrange
        var context = CreateHttpContext();
        Exception exception = new("Boom");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        (await ReadBodyAsync(context)).Should().Contain("errorId");
    }

    #endregion

    #region Helper Methods

    private static Stream RewoundBody(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return context.Response.Body;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        DefaultHttpContext context = new();
        context.Request.Path = "/api/test";
        context.Request.Method = "GET";
        // Set up response body as MemoryStream so it can be read back
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task VerifyErrorResponse(HttpContext context, HttpStatusCode expectedStatusCode)
    {
        context.Response.StatusCode.Should().Be((int)expectedStatusCode);

        // Reset stream position to read from beginning
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        string responseBody = await reader.ReadToEndAsync();

        // JSON uses camelCase, so check for lowercase "succeeded"
        responseBody.Should().Contain("succeeded");
        responseBody.Should().Contain("false");
        // WriteAsJsonAsync sets ContentType to "application/json; charset=utf-8"
        context.Response.ContentType.Should().StartWith("application/json");
    }

    #endregion
}
