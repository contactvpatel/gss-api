using System.Net;

namespace GSS.Core.Exceptions;

/// <summary>How an external service call failed.</summary>
public enum ExternalServiceFailure
{
    /// <summary>The service answered with an error, or the connection failed.</summary>
    Failed = 0,

    /// <summary>Not sent: the circuit is open or the outbound limit was reached.</summary>
    Unavailable = 1,

    /// <summary>The service did not answer in time.</summary>
    Timeout = 2
}

/// <summary>
/// An external service (SSO, MIS, ASM) call failed. Thrown by Infrastructure, mapped to 502/503/504 by the Api.
/// </summary>
public sealed class ExternalServiceException(
    string serviceName,
    ExternalServiceFailure failure,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>Client name, e.g. "MisService".</summary>
    public string ServiceName { get; } = serviceName;

    /// <summary>How the call failed.</summary>
    public ExternalServiceFailure Failure { get; } = failure;

    /// <summary>The service's status code, when it answered.</summary>
    public HttpStatusCode? StatusCode { get; init; }

    /// <summary>When to retry, if the outbound rate limiter said.</summary>
    public TimeSpan? RetryAfter { get; init; }
}
