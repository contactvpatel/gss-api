using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace GSS.Api.Filters;

/// <summary>
/// Browser revalidation for a GET: a 200 gets <c>Cache-Control: private, no-cache</c> and a weak ETag (hash of the JSON
/// body), and a matching <c>If-None-Match</c> gets 304 with no body. The browser always asks, so it never shows a value
/// older than the last write; an unchanged value costs a 304. Any other status keeps the pipeline's <c>no-store</c>,
/// so 404s and errors are never cached. Use instead of <see cref="ResponseCacheAttribute"/> with a duration.
/// </summary>
/// <remarks>
/// The body is serialized once: the filter hashes those bytes and writes them itself, so MVC's formatter never runs.
/// JSON only (every controller is <c>[Produces("application/json")]</c>).
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ClientCacheAttribute : Attribute, IAsyncResultFilter
{
    /// <summary><c>Cache-Control</c> of a cacheable response: browser only, revalidated on every use.</summary>
    public const string CacheControlValue = "private, no-cache";

    /// <summary>Content type MVC's JSON formatter writes.</summary>
    internal const string JsonContentType = "application/json; charset=utf-8";

    /// <inheritdoc />
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var request = context.HttpContext.Request;
        if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            && context.Result is ObjectResult { Value: { } value } result
            && GetEffectiveStatusCode(result, context.HttpContext.Response) == StatusCodes.Status200OK)
        {
            var jsonOptions = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), jsonOptions);
            EntityTagHeaderValue etag = new($"\"{Base64Url(SHA256.HashData(body).AsSpan(0, 16))}\"", isWeak: true);

            var headers = context.HttpContext.Response.Headers;
            headers.ETag = etag.ToString();
            headers.CacheControl = CacheControlValue;

            context.Result = MatchesIfNoneMatch(request, etag)
                ? new StatusCodeResult(StatusCodes.Status304NotModified)
                : new JsonBytesResult(body);
        }

        return next();
    }

    private static int GetEffectiveStatusCode(ObjectResult result, HttpResponse response)
    {
        return result.StatusCode
            ?? (result.Value as ProblemDetails)?.Status
            ?? response.StatusCode;
    }

    private static bool MatchesIfNoneMatch(HttpRequest request, EntityTagHeaderValue etag)
    {
        var candidates = request.GetTypedHeaders().IfNoneMatch;
        return candidates.Any(c => c.Equals(EntityTagHeaderValue.Any) || c.Compare(etag, useStrongComparison: false));
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Writes already-serialized JSON as a 200 (headers only for HEAD).</summary>
    private sealed class JsonBytesResult(byte[] body) : IActionResult
    {
        public Task ExecuteResultAsync(ActionContext context)
        {
            var httpContext = context.HttpContext;
            var response = httpContext.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = JsonContentType;
            response.ContentLength = body.Length;

            return HttpMethods.IsHead(httpContext.Request.Method)
                ? Task.CompletedTask
                : response.Body.WriteAsync(body, httpContext.RequestAborted).AsTask();
        }
    }
}
