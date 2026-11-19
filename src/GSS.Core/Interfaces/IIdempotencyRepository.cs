using GSS.Core.Models;

namespace GSS.Core.Interfaces;

/// <summary>
/// Stored responses of requests sent with an <c>Idempotency-Key</c>. Not an entity: no soft delete, no audit columns.
/// </summary>
public interface IIdempotencyRepository
{
    /// <summary>
    /// Waits for, then holds until the transaction ends, an exclusive lock on the user's key, so two requests with one key
    /// run one after the other. Needs an open Unit of Work transaction.
    /// </summary>
    /// <exception cref="InvalidOperationException">No transaction is open (the lock would end at once).</exception>
    Task LockKeyAsync(int userId, string key, CancellationToken cancellationToken = default);

    /// <summary>The stored response of the key if it was saved at or after <paramref name="savedAfterUtc"/>, else null.</summary>
    Task<IdempotencyRecord?> FindAsync(int userId, string key, DateTime savedAfterUtc, CancellationToken cancellationToken = default);

    /// <summary>Stores the response, replacing an expired row of the same key. Joins the open transaction.</summary>
    Task SaveAsync(IdempotencyRecord record, DateTime createdAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Deletes up to <paramref name="batchSize"/> rows created before <paramref name="createdBeforeUtc"/>; returns how many.</summary>
    Task<int> DeleteExpiredAsync(DateTime createdBeforeUtc, int batchSize, CancellationToken cancellationToken = default);
}
