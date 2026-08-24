using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;

namespace RequestGuardMcp.Core.Integrations;

/// <summary>Ports src/integrations/postgres.rs's <c>PersistedDecision</c>.</summary>
public sealed record PersistedDecision(ClassifyRequest Request, ClassifyResponse Response);

/// <summary>Ports src/integrations/postgres.rs's <c>DriftSummary</c>.</summary>
public sealed record DriftSummary(
    ulong Samples,
    ulong PreviousSamples,
    ulong CurrentSamples,
    double ScoreMean,
    double ScoreStddev,
    double PreviousScoreMean,
    IReadOnlyDictionary<string, ulong> VerdictDistribution,
    IReadOnlyDictionary<string, double> SignalDrift);

/// <summary>Ports src/integrations/postgres.rs's <c>CalibrationSummary</c>.</summary>
public sealed record CalibrationSummary(
    ulong Samples,
    ulong TruePositives,
    ulong FalsePositives,
    ulong TrueNegatives,
    ulong FalseNegatives);

/// <summary>
/// PostgreSQL-backed decision persistence, feedback, drift, and calibration reporting. Ports
/// src/integrations/postgres.rs's <c>PostgresClient</c>. When not configured, every read/write
/// method throws <see cref="Errors.AppErrorException.IntegrationUnavailable"/> except
/// <see cref="PingAsync"/>, which returns false.
/// </summary>
public interface IPostgresClient
{
    public bool IsAvailable { get; }

    public Task<bool> PingAsync(CancellationToken cancellationToken = default);

    public Task RecordDecisionAsync(ClassifyRequest request, ClassifyResponse response, CancellationToken cancellationToken = default);

    public Task<PersistedDecision?> GetDecisionAsync(string requestId, CancellationToken cancellationToken = default);

    public Task<string> RecordFeedbackAsync(string requestId, string correctVerdict, string? notes, string? reporter, CancellationToken cancellationToken = default);

    public Task<DriftSummary> DriftSummaryAsync(DateTimeOffset start, DateTimeOffset midpoint, CancellationToken cancellationToken = default);

    public Task<CalibrationSummary> CalibrationSummaryAsync(DateTimeOffset start, CancellationToken cancellationToken = default);
}
