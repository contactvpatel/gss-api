namespace GSS.Business.Models;

/// <summary>What to do with a request that carries an <c>Idempotency-Key</c>.</summary>
public enum IdempotencyOutcome
{
    /// <summary>First use of the key: run the request; its transaction is open.</summary>
    Proceed,

    /// <summary>The key was used by the same request: answer with the stored response.</summary>
    Replay,

    /// <summary>The key was used by a different request: answer 422.</summary>
    Mismatch
}

/// <summary>A stored successful response.</summary>
/// <param name="StatusCode">Status code.</param>
/// <param name="Body">JSON body.</param>
/// <param name="Location">The <c>Location</c> header, or null.</param>
public sealed record StoredResponse(int StatusCode, string Body, string? Location);

/// <summary>Result of starting an idempotent request.</summary>
/// <param name="Outcome">What to do.</param>
/// <param name="Response">The stored response when <paramref name="Outcome"/> is <see cref="IdempotencyOutcome.Replay"/>.</param>
public sealed record IdempotencyStart(IdempotencyOutcome Outcome, StoredResponse? Response = null);
