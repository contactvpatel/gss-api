using GSS.Core.Helpers;
using GSS.Core.Interfaces;

namespace GSS.Api.HostedServices;

/// <summary>
/// Deletes stored idempotency responses older than <see cref="IdempotencyLimits.Retention"/>, hourly, in batches. Reads
/// already ignore expired rows, so this only keeps the table small. Every pod runs it; a delete another pod did first
/// just removes fewer rows.
/// </summary>
public sealed partial class IdempotencyCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<IdempotencyCleanupHostedService> logger) : BackgroundService
{
    /// <summary>Time between runs; also the wait before the first, so startup is not slowed.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>Rows per delete, so one statement never holds many locks for long.</summary>
    public const int BatchSize = 1000;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        while (await WaitForNextRunAsync(timer, stoppingToken))
        {
            try
            {
                await DeleteExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed run must not stop the host (an unhandled exception in a BackgroundService does); the next run retries.
                LogCleanupFailed(ex);
            }
        }
    }

    /// <summary>One run: deletes batches until one comes back short. Returns the rows deleted.</summary>
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        DateTime createdBefore = (timeProvider.GetUtcNow() - IdempotencyLimits.Retention).UtcDateTime;
        int total = 0;

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var repository = scope.ServiceProvider.GetRequiredService<IIdempotencyRepository>();
            int deleted;
            do
            {
                deleted = await repository.DeleteExpiredAsync(createdBefore, BatchSize, cancellationToken).ConfigureAwait(false);
                total += deleted;
            }
            while (deleted == BatchSize);
        }

        if (total > 0)
        {
            LogDeleted(total);
        }

        return total;
    }

    private static async Task<bool> WaitForNextRunAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(EventId = 2200, Level = LogLevel.Information, Message = "Deleted {Count} expired idempotency keys")]
    private partial void LogDeleted(int count);

    [LoggerMessage(EventId = 2201, Level = LogLevel.Error, Message = "Deleting expired idempotency keys failed; the next run retries")]
    private partial void LogCleanupFailed(Exception exception);
}
