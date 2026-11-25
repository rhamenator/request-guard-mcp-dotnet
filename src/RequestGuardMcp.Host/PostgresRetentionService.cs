using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Integrations;

namespace RequestGuardMcp.Host;

/// <summary>
/// Deletes persisted decisions (and their feedback) older than <c>Postgres.RetentionDays</c> in bounded batches (issue 84).
/// Only registered when retention is enabled and PostgreSQL is configured.
/// </summary>
internal sealed class PostgresRetentionService(IPostgresClient postgres, PostgresConfig config, TimeProvider clock, ILogger<PostgresRetentionService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(config.RetentionIntervalMinutes), clock);
        do
        {
            try
            {
                var removed = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (removed > 0 && logger.IsEnabled(LogLevel.Information))
                {
                    var days = config.RetentionDays;
                    logger.LogInformation("Retention removed {Count} decisions older than {Days} days.", removed, days);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                // A failed run is retried at the next interval; it must never stop the server.
                logger.LogWarning(error, "Retention run failed; it will be retried.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>Run batches until one comes back smaller than the batch size; returns the total removed.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow().AddDays(-config.RetentionDays);
        var total = 0;
        while (true)
        {
            var removed = await postgres.PurgeExpiredAsync(cutoff, config.RetentionBatchSize, cancellationToken).ConfigureAwait(false);
            total += removed;
            if (removed < config.RetentionBatchSize)
            {
                return total;
            }
        }
    }
}
