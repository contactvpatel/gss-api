using System.Data.Common;
using System.Globalization;
using System.Net;
using GSS.Api.Helpers;
using GSS.Api.Models;
using GSS.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace GSS.Api.Middleware;

/// <summary>
/// Turns unhandled exceptions into the <see cref="Response{T}"/> envelope for the framework's exception handler
/// middleware (<c>UseExceptionHandler</c>). Status, client message and log level come from the exception type, never
/// its message; exception messages are logged, never returned. Expected outcomes are <c>Result&lt;T&gt;</c>, not
/// exceptions. The body is written with the app's HTTP JSON options, the same ones every other response uses.
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Structured log template.</summary>
    private const string LogTemplate = "Area: {Area}, RequestPath: {RequestPath}, RequestMethod: {RequestMethod}, ErrorId: {ErrorId}, Message: {Message}, InnerException: {InnerException}";

    private const string CanceledMessage = "Request was canceled by the client.";
    private const string PayloadTooLargeMessage = "Request body size exceeds the maximum allowed limit. Please reduce the request size and try again.";
    private const string RequestTimeoutMessage = "The request body was not received in time. Please try again.";
    private const string BadRequestMessage = "The request could not be read. Please check the request and try again.";
    private const string UpstreamFailedMessage = "A service this API depends on returned an error. Please try again later.";
    private const string UpstreamUnavailableMessage = "A service this API depends on is temporarily unavailable. Please try again later.";
    private const string UpstreamTimeoutMessage = "A service this API depends on did not respond in time. Please try again later.";
    private const string TemporarilyUnavailableMessage = "The service is temporarily unavailable. Please try again later.";
    private const string DuplicateMessage = "The request conflicts with existing data. Please reload and try again.";
    private const string ConcurrentChangeMessage = "The request conflicted with a concurrent change. Please try again.";

    // SQLSTATEs (ADO.NET DbException.SqlState, so Api needs no provider types).
    private const string UniqueViolation = "23505";
    private const string SerializationFailure = "40001";
    private const string DeadlockDetected = "40P01";

    /// <summary>Logs the exception and writes the error envelope. Always handles it.</summary>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (var statusCode, string? message, var logLevel) = Classify(exception, httpContext.RequestAborted.IsCancellationRequested);

        // Always the request's correlation id, so the response leads straight to the request's logs.
        string errorId = ApiErrorId.For(httpContext);

        string finalMessage = message ??
            $"Error occurred in the API. Please use the ErrorId [{errorId}] and contact support team if the problem persists.";

        // One error, so the top-level message is that error's message, as in every other envelope. It depends on the
        // status class only: never which backend failed (a database error reads like any other 500; the logs say more).
        Response<ApiError> errorResponse = Response<ApiError>.Failure(
            finalMessage,
            [new ApiError { ErrorId = errorId, StatusCode = (short)statusCode, Message = finalMessage }]);

        logger.Log(
            logLevel,
            exception,
            LogTemplate,
            nameof(ApiExceptionHandler),
            httpContext.Request.Path,
            httpContext.Request.Method,
            errorId,
            finalMessage,
            GetInnermostExceptionMessage(exception));

        httpContext.Response.StatusCode = (int)statusCode;
        if (statusCode == HttpStatusCode.ServiceUnavailable && FindInChain<ExternalServiceException>(exception)?.RetryAfter is { } retryAfter)
        {
            httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await httpContext.Response.WriteAsJsonAsync(errorResponse, cancellationToken);
        return true;
    }

    /// <summary>
    /// Status, client message and log level from the exception type (first match wins), never its message. A null
    /// message means the generic text with the ErrorId.
    /// </summary>
    private static (HttpStatusCode StatusCode, string? Message, LogLevel LogLevel) Classify(Exception exception, bool clientAborted)
    {
        // Deliberately absent, so they are 500s: ArgumentException (a failed guard is a bug; bad input is rejected by
        // validators, route constraints or Result.Failure), UnauthorizedAccessException (an OS/file permission error;
        // caller authorization is the auth handlers' 401/403) and KeyNotFoundException (a dictionary-indexer bug; a
        // missing resource is Result.NotFound, mapped by the controller).
        return exception switch
        {
            // The framework usually answers client aborts itself; a cancellation the client did not cause is a server error.
            OperationCanceledException when clientAborted => ((HttpStatusCode)499, CanceledMessage, LogLevel.Information),
            // Kestrel could not read the request (truncated or malformed body, body too slow, too large, headers too
            // large): the client's fault, answered with the exception's own 4xx, not a 500.
            _ when FindInChain<BadHttpRequestException>(exception) is { } badRequest => ClassifyBadRequest(badRequest.StatusCode),
            // A database exception without a SQLSTATE never got a server answer: transient, so 503 (clients and gateways
            // retry), never naming the database. A server error (Npgsql's PostgresException) has one and stays a 500.
            // ADO.NET's DbException, so Api needs no provider types. Critical only for a refused or broken connection: a
            // timeout (slow command, exhausted pool, or connect timeout; Npgsql's types can't tell them apart) is Error,
            // so a slow query never pages anyone; the readiness probe's database check reports an outage.
            _ when HasInChain(exception, e => e is DbException { SqlState: null }) =>
                (HttpStatusCode.ServiceUnavailable, TemporarilyUnavailableMessage, HasInChain(exception, e => e is TimeoutException) ? LogLevel.Error : LogLevel.Critical),
            // A unique index caught what the service's check missed (two requests raced): a 409, not a 500.
            _ when HasInChain(exception, e => e is DbException { SqlState: UniqueViolation }) =>
                (HttpStatusCode.Conflict, DuplicateMessage, LogLevel.Information),
            // The transaction was rolled back in full, so the client can retry. Warning: frequent ones mean lock-order bugs.
            _ when HasInChain(exception, e => e is DbException { SqlState: DeadlockDetected or SerializationFailure }) =>
                (HttpStatusCode.Conflict, ConcurrentChangeMessage, LogLevel.Warning),
            // Outbound SSO/MIS/ASM failure, translated by Infrastructure (possibly wrapped, so search the chain).
            _ when FindInChain<ExternalServiceException>(exception) is { } upstream => upstream.Failure switch
            {
                ExternalServiceFailure.Timeout => (HttpStatusCode.GatewayTimeout, UpstreamTimeoutMessage, LogLevel.Error),
                // Circuit open or outbound limit: already logged by the resilience pipeline, so Warning.
                ExternalServiceFailure.Unavailable => (HttpStatusCode.ServiceUnavailable, UpstreamUnavailableMessage, LogLevel.Warning),
                _ => (HttpStatusCode.BadGateway, UpstreamFailedMessage, LogLevel.Error)
            },
            _ => (HttpStatusCode.InternalServerError, null, LogLevel.Error)
        };
    }

    /// <summary>A request-read failure: its own 4xx (any other code becomes 400) with a fixed message, logged as a client fault.</summary>
    private static (HttpStatusCode StatusCode, string? Message, LogLevel LogLevel) ClassifyBadRequest(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status413PayloadTooLarge => (HttpStatusCode.RequestEntityTooLarge, PayloadTooLargeMessage, LogLevel.Warning),
            StatusCodes.Status408RequestTimeout => (HttpStatusCode.RequestTimeout, RequestTimeoutMessage, LogLevel.Information),
            >= 400 and < 500 => ((HttpStatusCode)statusCode, BadRequestMessage, LogLevel.Information),
            _ => (HttpStatusCode.BadRequest, BadRequestMessage, LogLevel.Information)
        };
    }

    /// <summary>True if the exception or any inner exception matches.</summary>
    private static bool HasInChain(Exception exception, Func<Exception, bool> match)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (match(current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The exception or the first inner exception of type <typeparamref name="T"/>.</summary>
    private static T? FindInChain<T>(Exception exception) where T : Exception
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>Innermost exception message.</summary>
    private static string GetInnermostExceptionMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException != null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }
}
