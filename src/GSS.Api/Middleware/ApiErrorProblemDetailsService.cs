using GSS.Api.Helpers;
using GSS.Api.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace GSS.Api.Middleware;

/// <summary>
/// Writes framework and ApiPipeline.NET errors (429, 504, and every empty-bodied 4xx/5xx via
/// <c>UseStatusCodePages</c>: unmatched route, 405, 415) as the <see cref="Response{T}"/> envelope instead of
/// <c>application/problem+json</c>, so every error keeps one contract. Registered without <c>AddProblemDetails</c>.
/// </summary>
public sealed class ApiErrorProblemDetailsService : IProblemDetailsService
{
    /// <inheritdoc />
    public async ValueTask WriteAsync(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var httpContext = context.HttpContext;
        int statusCode = context.ProblemDetails.Status ?? httpContext.Response.StatusCode;
        string message = context.ProblemDetails.Detail ?? context.ProblemDetails.Title ?? DefaultMessage(statusCode);

        Response<ApiError> body = Response<ApiError>.Failure(message,
        [
            new ApiError
            {
                ErrorId = ApiErrorId.For(httpContext),
                StatusCode = (short)statusCode,
                Message = message
            }
        ]);

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(body, httpContext.RequestAborted);
    }

    /// <summary>Message for an error that names none (status-code pages pass only the status).</summary>
    private static string DefaultMessage(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status404NotFound => "The requested resource was not found.",
            StatusCodes.Status405MethodNotAllowed => "The HTTP method is not allowed for this resource.",
            StatusCodes.Status415UnsupportedMediaType => "Unsupported media type: send application/json.",
            _ => ReasonPhrases.GetReasonPhrase(statusCode) is { Length: > 0 } phrase ? phrase : "The request could not be processed."
        };
    }
}
