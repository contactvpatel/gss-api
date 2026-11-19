using System.Net;
using GSS.Core.Exceptions;
using HttpResilience.NET;

namespace GSS.Infrastructure.Helpers;

/// <summary>
/// Exceptions for outbound HTTP failures. Never logs: the failure is logged once, where it is handled
/// (the Api's exception handler, or the service that turns it into a result).
/// </summary>
public static class HttpErrorHandler
{
    /// <summary>Max error body bytes read.</summary>
    private const int MaxErrorContentSize = 10 * 1024;

    /// <summary>Throws for a non-success response; the message carries at most 10 KB of the sanitized error body.</summary>
    /// <exception cref="HttpRequestException">The response is not successful.</exception>
    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string endpoint,
        string operation,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? errorContent = null;
        try
        {
            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxErrorContentSize)
            {
                errorContent = string.Format("[Error content too large: {0} bytes, limit: {1} bytes]", contentLength, MaxErrorContentSize);
            }
            else
            {
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using MemoryStream memoryStream = new();

                byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(4096);
                int totalBytesRead = 0;
                try
                {
                    int bytesRead;

                    while (totalBytesRead < MaxErrorContentSize &&
                           (bytesRead = await contentStream.ReadAsync(
                               buffer.AsMemory(0, Math.Min(buffer.Length, MaxErrorContentSize - totalBytesRead)),
                               cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        totalBytesRead += bytesRead;
                        memoryStream.Write(buffer, 0, bytesRead);
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }

                memoryStream.Position = 0;
                using StreamReader reader = new(memoryStream, System.Text.Encoding.UTF8);
                errorContent = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

                if (totalBytesRead >= MaxErrorContentSize)
                {
                    errorContent += " [Truncated at " + MaxErrorContentSize + " bytes]";
                }
            }
        }
        catch
        {
            // Best effort: the status code is enough.
        }

        string sanitizedErrorContent = SensitiveDataSanitizer.Sanitize(errorContent);

        string exceptionMessage = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => string.Format("Bad request: {0} failed for {1}. Response: {2}", operation, endpoint, sanitizedErrorContent),
            HttpStatusCode.Unauthorized => string.Format("Unauthorized: {0} failed for {1}", operation, endpoint),
            HttpStatusCode.Forbidden => string.Format("Forbidden: {0} failed for {1}", operation, endpoint),
            HttpStatusCode.NotFound => string.Format("Not found: {0} failed for {1}", operation, endpoint),
            HttpStatusCode.TooManyRequests => string.Format("Rate limit exceeded: {0} failed for {1}", operation, endpoint),
            HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
                => string.Format("Server error: {0} failed for {1}. Response: {2}", operation, endpoint, sanitizedErrorContent),
            _ => string.Format("HTTP error: {0} failed for {1}. Response: {2}", operation, endpoint, sanitizedErrorContent)
        };

        throw new HttpRequestException(exceptionMessage, null, response.StatusCode);
    }

    /// <summary>
    /// Turns an outbound failure into <see cref="ExternalServiceException"/>; null for anything else (a caller
    /// cancellation, a bad payload, a bug), which callers rethrow unchanged.
    /// </summary>
    public static ExternalServiceException? ToExternalServiceException(
        Exception exception,
        string serviceName,
        string operation,
        string endpoint)
    {
        ExternalServiceFailure? failure = HttpResilienceFailure.Classify(exception) switch
        {
            HttpResilienceFailureType.Timeout => ExternalServiceFailure.Timeout,
            HttpResilienceFailureType.CircuitOpen or HttpResilienceFailureType.LimiterRejected => ExternalServiceFailure.Unavailable,
            HttpResilienceFailureType.RequestFailed => ExternalServiceFailure.Failed,
            _ => null
        };

        if (failure is null)
        {
            return null;
        }

        return new ExternalServiceException(
            serviceName,
            failure.Value,
            string.Concat(serviceName, " ", operation, " ", endpoint, " failed: ", failure.Value.ToString(), "."),
            exception)
        {
            StatusCode = (exception as HttpRequestException)?.StatusCode,
            RetryAfter = HttpResilienceFailure.TryGetRetryAfter(exception, out var wait) ? wait : null
        };
    }
}
