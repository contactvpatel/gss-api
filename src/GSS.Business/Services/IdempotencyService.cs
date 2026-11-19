using System.Globalization;
using GSS.Business.Helpers;
using GSS.Business.Models;
using GSS.Business.Services.Interfaces;
using GSS.Core.Helpers;
using GSS.Core.Interfaces;
using GSS.Core.Interfaces.Base;
using GSS.Core.Models;
using Microsoft.Extensions.Logging;

namespace GSS.Business.Services;

/// <summary>
/// <see cref="IIdempotencyService"/>: a per-user advisory lock on the key and the stored response, both in the
/// request's Unit of Work transaction.
/// </summary>
public sealed class IdempotencyService(
    IIdempotencyRepository repository,
    IUnitOfWork unitOfWork,
    IUserContext userContext,
    TimeProvider timeProvider,
    ILogger<IdempotencyService> logger) : IIdempotencyService
{
    private bool _open;

    /// <inheritdoc />
    public async Task<IdempotencyStart> BeginAsync(string key, string requestHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(requestHash);
        int userId = UserId();

        await unitOfWork.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        _open = true;

        IdempotencyRecord? stored;
        try
        {
            await repository.LockKeyAsync(userId, key, cancellationToken).ConfigureAwait(false);
            DateTime savedAfter = (timeProvider.GetUtcNow() - IdempotencyLimits.Retention).UtcDateTime;
            stored = await repository.FindAsync(userId, key, savedAfter, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await AbortAsync().ConfigureAwait(false);
            throw;
        }

        if (stored is null)
        {
            return new IdempotencyStart(IdempotencyOutcome.Proceed);
        }

        // Nothing to keep: release the lock and the connection before answering.
        await AbortAsync().ConfigureAwait(false);

        if (!string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
        {
            logger.Mismatched();
            return new IdempotencyStart(IdempotencyOutcome.Mismatch);
        }

        logger.Replayed();
        return new IdempotencyStart(IdempotencyOutcome.Replay, new StoredResponse(stored.StatusCode, stored.ResponseBody, stored.Location));
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string key, string requestHash, StoredResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(requestHash);
        ArgumentNullException.ThrowIfNull(response);
        if (!_open)
        {
            throw new InvalidOperationException("CompleteAsync needs the transaction BeginAsync opened.");
        }

        var record = new IdempotencyRecord(UserId(), key, requestHash, response.StatusCode, response.Body, response.Location);
        await repository.SaveAsync(record, timeProvider.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);

        // A failed commit ends the transaction too, so it is closed either way.
        _open = false;
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AbortAsync()
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        try
        {
            await unitOfWork.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The caller is already unwinding from the real failure; never mask it.
            logger.RollbackFailed(ex);
        }
    }

    /// <summary>The caller's numeric user ID. Idempotent routes are authenticated, so a missing one is a bug.</summary>
    private int UserId()
    {
        return int.TryParse(userContext.GetUserId(), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0
            ? id
            : throw new InvalidOperationException("An idempotent request needs an authenticated user with a numeric ID.");
    }
}
