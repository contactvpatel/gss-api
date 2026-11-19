namespace GSS.Core.Models;

/// <summary>
/// The first successful response of a request sent with an <c>Idempotency-Key</c>.
/// </summary>
/// <param name="UserId">Caller's user ID; keys are per user.</param>
/// <param name="Key">The client's key.</param>
/// <param name="RequestHash">Hash of the request (method, path, query, body); a retry must match it.</param>
/// <param name="StatusCode">Status code of the stored response.</param>
/// <param name="ResponseBody">JSON body of the stored response.</param>
/// <param name="Location">Its <c>Location</c> header, or null.</param>
public sealed record IdempotencyRecord(
    int UserId,
    string Key,
    string RequestHash,
    int StatusCode,
    string ResponseBody,
    string? Location);
