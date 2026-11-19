using System.Net.Http.Headers;
using System.Text.Json;

namespace GSS.Infrastructure.Helpers;

/// <summary>
/// HTTP helpers. Header helpers target <see cref="HttpRequestMessage"/> because factory clients are shared
/// and <c>DefaultRequestHeaders</c> is not thread-safe.
/// </summary>
public static class HttpClientExtensions
{
    /// <summary>
    /// UTF-8 JSON content (source-generated options by default), serialized straight to bytes. Unlike
    /// <c>JsonContent</c> it has a Content-Length, so the body is never sent chunked.
    /// </summary>
    public static HttpContent AsJsonContent<T>(this T obj, JsonSerializerOptions? options = null)
    {
        ByteArrayContent content = new(JsonSerializer.SerializeToUtf8Bytes(obj, options ?? JsonContext.Default.Options));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    /// <summary>Ensures the URL ends with exactly one slash.</summary>
    public static string EnsureTrailingSlash(this string url)
    {
        return string.IsNullOrWhiteSpace(url) ? url : url.TrimEnd('/') + "/";
    }

    /// <summary>
    /// Sets <c>Authorization</c> to the raw token, without a <c>Bearer</c> prefix (the BAPS services expect it that way);
    /// no-op for a blank token.
    /// </summary>
    public static HttpRequestMessage SetBearerToken(this HttpRequestMessage request, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return request;
        }

        request.Headers.Remove("Authorization");
        request.Headers.TryAddWithoutValidation("Authorization", token);
        return request;
    }
}
