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

    public Task RecordDecisionAsync(string callerScope, ClassifyRequest request, ClassifyResponse response, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("PostgreSQL integration is disabled");

    public Task<PersistedDecision?> GetDecisionAsync(string callerScope, string requestId, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("PostgreSQL integration is disabled");

    public Task<string?> RecordFeedbackAsync(string callerScope, string requestId, string correctVerdict, string? notes, string? reporter, PersistedDecision expected, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("PostgreSQL integration is disabled");

    public Task<DriftSummary> DriftSummaryAsync(string callerScope, DateTimeOffset start, DateTimeOffset midpoint, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("PostgreSQL integration is disabled");

    public Task<CalibrationSummary> CalibrationSummaryAsync(string callerScope, DateTimeOffset start, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("PostgreSQL integration is disabled");
}
