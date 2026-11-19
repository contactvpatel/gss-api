using ApiPipeline.NET.Correlation;

namespace GSS.Api.Helpers;

/// <summary>
/// <see cref="Models.ApiError.ErrorId"/> for a request: its correlation id, which is also the <c>X-Correlation-Id</c>
/// response header and the <c>CorrelationId</c> log property, so an error response leads straight to its logs.
/// </summary>
public static class ApiErrorId
{
    /// <summary>The correlation id; the trace identifier if the correlation middleware has not run; a new GUID without a request.</summary>
    public static string For(HttpContext? context)
    {
        return context?.GetCorrelationId() ?? context?.TraceIdentifier ?? Guid.NewGuid().ToString();
    }
}
