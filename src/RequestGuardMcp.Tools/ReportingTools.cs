using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Core.Util;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

public sealed class DriftReportTool : IMcpTool
{
    public string Name => "drift_report";
    public string Description => "Report on score and signal drift";

    public async Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.ParamsOrDefault<DriftReportRequest>(parameters);
        if (!state.Postgres.IsAvailable)
        {
            throw AppErrorException.IntegrationUnavailable("drift reporting requires PostgreSQL persistence");
        }

        if (request.Since is not null && request.WindowHours is not null)
        {
            throw AppErrorException.InvalidRequest("since and window_hours are mutually exclusive");
        }

        var now = DateTimeOffset.UtcNow;
        uint windowHours = request.WindowHours ?? 24;
        if (windowHours is < 1 or > 8760)
        {
            throw AppErrorException.InvalidRequest("window_hours must be between 1 and 8760");
        }
        DateTimeOffset start;
        if (request.Since is not null)
        {
            if (!DateTimeOffset.TryParse(request.Since, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out start))
            {
                throw AppErrorException.InvalidRequest("invalid since timestamp");
            }

            if (start > now)
            {
                throw AppErrorException.InvalidRequest("since timestamp cannot be in the future");
            }

            var elapsed = now - start;
            if (elapsed.TotalHours > 8760)
            {
                throw AppErrorException.InvalidRequest("since timestamp cannot be more than 8760 hours old");
            }
            windowHours = (uint)Math.Max(1, Math.Ceiling(elapsed.TotalHours));
        }
        else
        {
            start = now.AddHours(-windowHours);
        }

        var midpoint = start + TimeSpan.FromTicks((now - start).Ticks / 2);
        var summary = await ToolResult.UpstreamAsync(() => state.Postgres.DriftSummaryAsync(start, midpoint, cancellationToken)).ConfigureAwait(false);
        var drift = summary.PreviousSamples > 0 && summary.CurrentSamples > 0 &&
                    (Math.Abs(summary.ScoreMean - summary.PreviousScoreMean) >= 0.10 || summary.SignalDrift.Values.Any(value => Math.Abs(value) >= 0.25));
        return ToolResult.Json(new DriftReportResponse(windowHours, drift,
            new DriftMetrics(summary.Samples, summary.ScoreMean, summary.ScoreStddev, summary.VerdictDistribution, summary.SignalDrift), TimeUtil.NowRfc3339()));
    }
}

public sealed class CalibrationReportTool : IMcpTool
{
    public string Name => "calibration_report";
    public string Description => "Precision and recall calibration report";

    public async Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.ParamsOrDefault<CalibrationReportRequest>(parameters);
        if (!state.Postgres.IsAvailable)
        {
            throw AppErrorException.IntegrationUnavailable("calibration reporting requires PostgreSQL persistence");
        }
        var hours = request.WindowHours ?? 24;
        if (hours is < 1 or > 8760)
        {
            throw AppErrorException.InvalidRequest("window_hours must be between 1 and 8760");
        }
        var summary = await ToolResult.UpstreamAsync(() => state.Postgres.CalibrationSummaryAsync(DateTimeOffset.UtcNow.AddHours(-hours), cancellationToken)).ConfigureAwait(false);
        var precision = Ratio(summary.TruePositives, summary.TruePositives + summary.FalsePositives);
        var recall = Ratio(summary.TruePositives, summary.TruePositives + summary.FalseNegatives);
        var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
        var falsePositiveRate = Ratio(summary.FalsePositives, summary.FalsePositives + summary.TrueNegatives);
        var falseNegativeRate = Ratio(summary.FalseNegatives, summary.FalseNegatives + summary.TruePositives);
        var recommendations = new List<string>();
        if (summary.Samples == 0)
        {
            recommendations.Add("No labelled feedback is available for this window.");
        }
        else
        {
            if (falsePositiveRate >= 0.10)
            {
                recommendations.Add("Review blocking thresholds to reduce false positives.");
            }

            if (falseNegativeRate >= 0.10)
            {
                recommendations.Add("Review allow thresholds to reduce false negatives.");
            }

            if (recommendations.Count == 0)
            {
                recommendations.Add("Observed labelled performance is within the configured reporting thresholds.");
            }
        }

        return ToolResult.Json(new CalibrationReportResponse(hours, summary.Samples, precision, recall, f1, falsePositiveRate, falseNegativeRate, recommendations, TimeUtil.NowRfc3339()));
    }

    private static double Ratio(ulong numerator, ulong denominator) => denominator == 0 ? 0 : (double)numerator / denominator;
}

public sealed class QueueStatusTool : IMcpTool
{
    public string Name => "queue_status";
    public string Description => "Status of processing queues";

    public async Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.ParamsOrDefault<QueueStatusRequest>(parameters);
        if (!state.Redis.IsAvailable)
        {
            throw AppErrorException.IntegrationUnavailable("queue status requires Redis");
        }
        var stats = await ToolResult.UpstreamAsync(() => state.Redis.QueueStatsAsync(request.Queue, cancellationToken)).ConfigureAwait(false);
        var consumers = (uint)Math.Min(state.Config.Limits.GlobalConcurrency, uint.MaxValue);
        return ToolResult.Json(new QueueStatusResponse(stats.Select(value => new QueueInfo(value.Name, value.Active, consumers, value.CompletedLastMinute / 60.0)).ToList()));
    }
}
