using System.Text.Json.Serialization;

namespace GSS.Api.Models;

// Built only with Success/Failure; immutable once built. See .docs/architecture/error-contract.md.

/// <summary>Envelope of every response: <c>succeeded</c>, <c>data</c>, <c>message</c>, and <c>errors</c> on failure.</summary>
public sealed class Response<T>
{
    /// <summary>For JSON deserialization only.</summary>
    [JsonConstructor]
    private Response()
    {
    }

    /// <summary>True on success.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Payload.</summary>
    public T? Data { get; init; }

    /// <summary>Human-readable message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Errors; null on success.</summary>
    public List<ApiError>? Errors { get; init; }

    /// <summary>Success with data.</summary>
    public static Response<T> Success(T? data, string message = "")
    {
        return new Response<T> { Succeeded = true, Data = data, Message = message };
    }

    /// <summary>Failure with optional errors.</summary>
    public static Response<T> Failure(string message, List<ApiError>? errors = null)
    {
        return new Response<T> { Succeeded = false, Message = message, Errors = errors ?? [] };
    }
}
