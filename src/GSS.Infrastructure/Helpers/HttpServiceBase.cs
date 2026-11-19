using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GSS.Core.Exceptions;
using GSS.Core.Models;
using GSS.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace GSS.Infrastructure.Helpers;

/// <summary>
/// Base for typed HTTP services: one send path (<see cref="SendAsync{T}"/>) that maps errors and deserializes JSON.
/// Response size is capped by the client's MaxResponseContentBufferSize.
/// The <see cref="HttpClient"/> is a typed client injected by IHttpClientFactory, already carrying base address,
/// app credential headers and the HttpResilience.NET pipeline; services add only per-call headers (bearer token).
/// Never called inside an open database transaction (<paramref name="transactionManager"/>): the call would hold the
/// transaction's pooled connection and row locks for the whole network round trip, retries included.
/// </summary>
public abstract class HttpServiceBase(
    HttpClient httpClient,
    IDapperTransactionManager transactionManager,
    ILogger logger,
    string serviceName)
{
    /// <summary>Logger for HTTP operations.</summary>
    protected ILogger Logger => logger;

    /// <summary>Source-generated JSON options.</summary>
    protected JsonSerializerOptions JsonOptions => JsonContext.Default.Options;

    /// <summary>GET and deserialize; default for an empty body. <paramref name="configureRequest"/> can set headers.</summary>
    protected Task<T?> GetAsync<T>(
        string endpoint,
        Action<HttpRequestMessage>? configureRequest,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Get, endpoint, null, configureRequest,
            (response, ct) => ReadJsonAsync<T>(response, endpoint, HttpMethod.Get, ct), cancellationToken);
    }

    /// <summary>POST JSON and deserialize the response; default for an empty body.</summary>
    protected Task<TResponse?> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        Action<HttpRequestMessage>? configureRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(HttpMethod.Post, endpoint, () => request.AsJsonContent(JsonOptions), configureRequest,
            (response, ct) => ReadJsonAsync<TResponse>(response, endpoint, HttpMethod.Post, ct), cancellationToken);
    }

    /// <summary>POST JSON; true on success, throws otherwise.</summary>
    protected Task<bool> PostAsync<TRequest>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(HttpMethod.Post, endpoint, () => request.AsJsonContent(JsonOptions), null,
            (_, _) => Task.FromResult(true), cancellationToken);
    }

    /// <summary>POST without a body; true on success, throws otherwise.</summary>
    protected Task<bool> PostWithoutBodyAsync(
        string endpoint,
        Action<HttpRequestMessage>? configureRequest,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Post, endpoint, null, configureRequest,
            (_, _) => Task.FromResult(true), cancellationToken);
    }

    /// <summary>
    /// GET an <see cref="ExternalApiResponse{T}"/> (MIS, ASM) and return its data (empty when <c>data</c> is null; null
    /// items dropped, since callers and filters dereference every item).
    /// No body or <c>Succeeded=false</c> throws <see cref="ExternalServiceException"/> (502), so a failure is never
    /// returned, or cached, as an empty list.
    /// </summary>
    protected async Task<IReadOnlyList<T>> GetListAsync<T>(
        string endpoint,
        Action<HttpRequestMessage>? configureRequest,
        CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<ExternalApiResponse<T>>(endpoint, configureRequest, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            throw UnsuccessfulResponse(endpoint, HttpMethod.Get, "no response body");
        }

        if (!response.Succeeded)
        {
            var first = response.Errors?.FirstOrDefault();
            throw UnsuccessfulResponse(endpoint, HttpMethod.Get, FailureDetail(first?.ErrorId, first?.StatusCode, first?.Message));
        }

        return response.Data is null ? [] : [.. response.Data.Where(item => item is not null)];
    }

    /// <summary>
    /// <see cref="GetListAsync{T}"/>, then the first item matching <paramref name="match"/> (never trust the upstream
    /// to have applied the filter); null when none matches or the upstream answers 404.
    /// </summary>
    protected async Task<T?> GetSingleOrNullAsync<T>(
        string endpoint,
        Func<T, bool> match,
        Action<HttpRequestMessage>? configureRequest,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(match);
        try
        {
            var items = await GetListAsync<T>(endpoint, configureRequest, cancellationToken).ConfigureAwait(false);
            return items.FirstOrDefault(match);
        }
        catch (ExternalServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// The one send path: validate the endpoint, build and configure the request, send, map a non-2xx answer, then
    /// <paramref name="readAsync"/>. An outbound failure becomes <see cref="ExternalServiceException"/>; anything else
    /// (caller cancellation, bug) propagates unchanged. Logged once, by the Api's exception handler.
    /// </summary>
    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string endpoint,
        Func<HttpContent>? createContent,
        Action<HttpRequestMessage>? configureRequest,
        Func<HttpResponseMessage, CancellationToken, Task<T>> readAsync,
        CancellationToken cancellationToken)
    {
        string operation = method.Method;
        if (transactionManager.GetCurrentTransaction() != null)
        {
            throw new InvalidOperationException(
                $"{serviceName} {operation} {endpoint} was called inside an open database transaction, which would hold " +
                "its connection and row locks for the whole network call. Call it before the Unit of Work begins.");
        }

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

            // Disposing the request disposes its content.
            using HttpRequestMessage request = new(method, endpoint) { Content = createContent?.Invoke() };
            configureRequest?.Invoke(request);

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            await HttpErrorHandler.EnsureSuccessAsync(response, endpoint, operation, cancellationToken).ConfigureAwait(false);
            return await readAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ExternalServiceException? translated = HttpErrorHandler.ToExternalServiceException(ex, serviceName, operation, endpoint);
            if (translated is null)
            {
                throw;
            }

            throw translated;
        }
    }

    /// <summary>
    /// Deserializes the buffered JSON body; default for an empty body. A malformed body is an upstream failure
    /// (<see cref="ExternalServiceFailure.Failed"/>, 502), not a 500; the <see cref="JsonException"/> stays as the inner exception.
    /// </summary>
    private async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, string endpoint, HttpMethod method, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength == 0)
        {
            Logger.EmptyResponseBody(endpoint);
            return default;
        }

        T? result;
        try
        {
            result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new ExternalServiceException(
                serviceName,
                ExternalServiceFailure.Failed,
                string.Concat(serviceName, " ", method.Method, " ", endpoint, " failed: malformed JSON response body."),
                ex);
        }

        if (method == HttpMethod.Get)
        {
            Logger.Fetched(endpoint);
        }
        else
        {
            Logger.Posted(endpoint);
        }

        return result;
    }

    /// <summary>
    /// For a 2xx answer with no body, a failure flag or missing required data: <see cref="ExternalServiceFailure.Failed"/>
    /// (502), so callers never turn it into an empty (and cached) result or a refusal. <paramref name="detail"/> is sanitized.
    /// </summary>
    protected ExternalServiceException UnsuccessfulResponse(string endpoint, HttpMethod method, string detail)
    {
        return new ExternalServiceException(
            serviceName,
            ExternalServiceFailure.Failed,
            string.Concat(serviceName, " ", method.Method, " ", endpoint, " failed: ", SensitiveDataSanitizer.Sanitize(detail), "."));
    }

    /// <summary>Detail for <see cref="UnsuccessfulResponse"/> from the upstream's first error.</summary>
    private static string FailureDetail(string? errorId, int? statusCode, string? message)
    {
        return string.Concat(
            "Succeeded=false (", errorId, "-", statusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture), "-", message, ")");
    }
}
