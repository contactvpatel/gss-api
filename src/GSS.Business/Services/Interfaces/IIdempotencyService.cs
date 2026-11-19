using GSS.Business.Models;

namespace GSS.Business.Services.Interfaces;

/// <summary>
/// Makes a POST safe to retry. <see cref="BeginAsync"/> opens the request's Unit of Work transaction, so everything the
/// request writes and the stored response commit together in <see cref="CompleteAsync"/>, or roll back in
/// <see cref="AbortAsync"/>. Scoped: one request, one key.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Waits while another request holds the same key, then reports whether the key was already used. Only
    /// <see cref="IdempotencyOutcome.Proceed"/> leaves the transaction open.
    /// </summary>
    /// <param name="key">The client's key.</param>
    /// <param name="requestHash">Hash of the request (method, path, query, body).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdempotencyStart> BeginAsync(string key, string requestHash, CancellationToken cancellationToken = default);

    /// <summary>Stores the response and commits the request's writes with it.</summary>
    Task CompleteAsync(string key, string requestHash, StoredResponse response, CancellationToken cancellationToken = default);

    /// <summary>Rolls the request back and releases the key; no-op when no transaction is open. Never throws.</summary>
    Task AbortAsync();
}
