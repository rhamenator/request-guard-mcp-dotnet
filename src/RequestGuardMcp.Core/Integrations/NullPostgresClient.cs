using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;

namespace RequestGuardMcp.Core.Integrations;

/// <summary>The disabled-state <see cref="IPostgresClient"/>, used until <c>postgres.url</c> is configured.</summary>
public sealed class NullPostgresClient : IPostgresClient
{
    public static readonly NullPostgresClient Instance = new();

    public bool IsAvailable => false;

    public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task RecordDecisionAsync(ClassifyRequest request, ClassifyResponse response, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("PostgreSQL integration is disabled");

    public Task<PersistedDecision?> GetDecisionAsync(string requestId, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("PostgreSQL integration is disabled");

    public Task<string> RecordFeedbackAsync(string requestId, string correctVerdict, string? notes, string? reporter, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("PostgreSQL integration is disabled");

    public Task<DriftSummary> DriftSummaryAsync(DateTimeOffset start, DateTimeOffset midpoint, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("PostgreSQL integration is disabled");

    public Task<CalibrationSummary> CalibrationSummaryAsync(DateTimeOffset start, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("PostgreSQL integration is disabled");
}
