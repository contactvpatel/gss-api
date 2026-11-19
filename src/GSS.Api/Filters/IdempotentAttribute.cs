using System.Net;
using System.Security.Cryptography;
using System.Text;
using GSS.Api.Helpers;
using GSS.Api.Models;
using GSS.Business.Models;
using GSS.Business.Services.Interfaces;
using GSS.Core.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GSS.Api.Filters;

/// <summary>
/// Makes a create POST safe to retry. A request with an <c>Idempotency-Key</c> header runs once per user and key: the
/// first 2xx response is stored in the same transaction as the writes it made, and a retry of the same request gets that
/// response again (header <c>Idempotent-Replayed: true</c>) instead of creating a duplicate.
/// </summary>
/// <remarks>
/// Another request with the same key waits for the first to finish, then replays it, or runs if the first failed. The same
/// key with a different method, URL or body is 422. Only 2xx responses are stored: an error (400, 409, 5xx) rolls back
/// and frees the key, so the client can fix the request and resend it. Without the header the action runs as before, unless
/// <see cref="Required"/>. Keys are kept <see cref="IdempotencyLimits.Retention"/>.
/// Runs as a resource filter, so it sees the raw body before model binding and the finished response after the result
/// executed; the response is buffered until the commit succeeds, so a failed commit is a 5xx, never a lost duplicate guard.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncResourceFilter
{
    private const string JsonContentType = "application/json; charset=utf-8";

    /// <summary>When true, a request without the header is a 400.</summary>
    public bool Required { get; init; }

    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        // ContainsKey: a header sent empty is a bad key (400), never a silently ignored one.
        bool hasKey = http.Request.Headers.ContainsKey(IdempotencyLimits.HeaderName);
        var headers = http.Request.Headers[IdempotencyLimits.HeaderName];
        if (!hasKey)
        {
            if (Required)
            {
                context.Result = Error(http, HttpStatusCode.BadRequest, $"{IdempotencyLimits.HeaderName} header is required.");
                return;
            }

            await next();
            return;
        }

        string? key = headers.Count == 1 ? headers[0] : null;
        if (!IdempotencyLimits.IsValidKey(key))
        {
            context.Result = Error(http, HttpStatusCode.BadRequest, IdempotencyLimits.InvalidKey);
            return;
        }

        var service = http.RequestServices.GetRequiredService<IIdempotencyService>();
        var ct = http.RequestAborted;
        string requestHash = await HashRequestAsync(http.Request, ct);

        var start = await service.BeginAsync(key!, requestHash, ct);
        switch (start.Outcome)
        {
            case IdempotencyOutcome.Replay:
                context.Result = new ReplayResult(start.Response!);
                return;
            case IdempotencyOutcome.Mismatch:
                context.Result = Error(http, HttpStatusCode.UnprocessableEntity,
                    $"{IdempotencyLimits.HeaderName} was already used with a different request.");
                return;
        }

        await RunAndStoreAsync(http, service, key!, requestHash, next, ct);
    }

    /// <summary>
    /// Runs the action with the response held in memory, stores a 2xx response and commits, then sends it. On any other
    /// outcome the transaction is rolled back and the response is sent as it is.
    /// </summary>
    private static async Task RunAndStoreAsync(
        HttpContext http,
        IIdempotencyService service,
        string key,
        string requestHash,
        ResourceExecutionDelegate next,
        CancellationToken ct)
    {
        var response = http.Response;
        var original = response.Body;
        var buffer = new MemoryStream();
        response.Body = buffer;

        bool committed = false;
        try
        {
            var executed = await next();
            if (executed.Exception is null && response.StatusCode is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices)
            {
                string body = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                string? location = response.Headers.Location.Count > 0 ? response.Headers.Location.ToString() : null;
                await service.CompleteAsync(key, requestHash, new StoredResponse(response.StatusCode, body, location), ct);
                committed = true;
            }
        }
        finally
        {
            response.Body = original;
            if (!committed)
            {
                await service.AbortAsync();
            }
        }

        // Reached only when nothing threw: send what the action produced.
        buffer.Position = 0;
        await buffer.CopyToAsync(original, ct);
    }

    /// <summary>SHA-256 (hex) of method, path, query and body, so the same key cannot front a different request.</summary>
    private static async Task<string> HashRequestAsync(HttpRequest request, CancellationToken ct)
    {
        request.EnableBuffering();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{request.Method}\n{request.Path}{request.QueryString}\n"));

        byte[] chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            hash.AppendData(chunk, 0, read);
        }

        request.Body.Position = 0;
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static ObjectResult Error(HttpContext http, HttpStatusCode status, string message)
    {
        var errors = new List<ApiError>
        {
            new() { ErrorId = ApiErrorId.For(http), StatusCode = (short)status, Message = message }
        };
        return new ObjectResult(Response<object?>.Failure(message, errors)) { StatusCode = (int)status };
    }

    /// <summary>Writes a stored response: its status, <c>Location</c> and JSON body, marked as a replay.</summary>
    private sealed class ReplayResult(StoredResponse stored) : IActionResult
    {
        public Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.StatusCode = stored.StatusCode;
            response.Headers[IdempotencyLimits.ReplayedHeaderName] = "true";
            if (stored.Location is not null)
            {
                response.Headers.Location = stored.Location;
            }

            response.ContentType = JsonContentType;
            return response.WriteAsync(stored.Body, Encoding.UTF8, context.HttpContext.RequestAborted);
        }
    }
}
