using GSS.Business.Models;
using GSS.Core.Interfaces.Base;
using Microsoft.Extensions.Logging;

namespace GSS.Business.Helpers;

/// <summary>
/// Multi-step writes in one Unit of Work transaction (e.g. a derived service writing two tables).
/// </summary>
public static class UnitOfWorkExtensions
{
    /// <summary>
    /// Runs the operation in a transaction: commit when it completes, rollback on exception. For operations that can
    /// fail with a <see cref="Result{T}"/> (composed service calls), use the <see cref="Result{T}"/> overload, which also
    /// rolls back a failed result.
    /// </summary>
    public static Task ExecuteInTransactionAsync(
        this IUnitOfWork unitOfWork,
        Func<CancellationToken, Task> operation,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RunAsync(unitOfWork, async ct =>
        {
            await operation(ct).ConfigureAwait(false);
            return true;
        }, logger, cancellationToken);
    }

    /// <summary>
    /// Runs the operation in a transaction and commits only when it returns a successful result. A failed, not-found
    /// or conflict result is rolled back and returned as is, so composed services stay all or nothing without throwing
    /// (a thrown business failure would be a 500). An exception also rolls back.
    /// </summary>
    public static async Task<Result<T>> ExecuteInTransactionAsync<T>(
        this IUnitOfWork unitOfWork,
        Func<CancellationToken, Task<Result<T>>> operation,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Result<T> result = default;
        await RunAsync(unitOfWork, async ct =>
        {
            result = await operation(ct).ConfigureAwait(false);
            return result.IsSuccess;
        }, logger, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Begin, run, then commit when the operation returns true, else roll back. Begin is outside the try so a nested
    /// call cannot roll back the caller's transaction. After an exception a rollback failure is logged, not thrown, so
    /// the original exception surfaces. Rollback uses CancellationToken.None: it runs even when the request was cancelled.
    /// </summary>
    private static async Task RunAsync(
        IUnitOfWork unitOfWork,
        Func<CancellationToken, Task<bool>> operation,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(logger);

        await unitOfWork.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        bool commit;
        try
        {
            commit = await operation(cancellationToken).ConfigureAwait(false);
            if (commit)
            {
                await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            try
            {
                await unitOfWork.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackException)
            {
                logger.LogError(rollbackException, "Rollback failed after a transaction error; the original error is rethrown");
            }

            throw;
        }

        if (!commit)
        {
            // A failed result: no error to preserve, so a rollback failure propagates.
            await unitOfWork.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
