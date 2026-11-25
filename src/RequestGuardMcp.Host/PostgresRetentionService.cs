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
                if ((removed.Decisions > 0 || removed.Feedback > 0) && logger.IsEnabled(LogLevel.Information))
                {
                    var days = config.RetentionDays;
                    logger.LogInformation("Retention removed {Decisions} decisions and {Feedback} feedback rows older than {Days} days.", removed.Decisions, removed.Feedback, days);
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

    /// <summary>Run batches until neither table fills a batch; returns the totals removed.</summary>
    internal async Task<PurgeResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow().AddDays(-config.RetentionDays);
        int decisions = 0, feedback = 0;
        while (true)
        {
            var batch = await postgres.PurgeExpiredAsync(cutoff, config.RetentionBatchSize, cancellationToken).ConfigureAwait(false);
            decisions += batch.Decisions;
            feedback += batch.Feedback;
            // Keep going while either table filled its batch: expired feedback can outlive its decision's timestamp.
            if (batch.Largest < config.RetentionBatchSize)
            {
                return new PurgeResult(decisions, feedback);
            }
        }
    }
}
