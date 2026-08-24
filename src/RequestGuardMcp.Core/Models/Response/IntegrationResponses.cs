using System.Text.Json.Nodes;

namespace RequestGuardMcp.Core.Models.Response;

public sealed record FeedbackResponse(bool Accepted, string FeedbackId, string Message);
public sealed record ReplayResponse(string RequestId, JsonNode? Original, ClassifyResponse? Replayed, bool MatchesOriginal);
public sealed record EnrichIpResponse(string Ip, string? Country, string? City, uint? Asn, string? Org, bool IsProxy, bool IsDatacenter, bool IsTor, double RiskScore);
public sealed record EnrichAsnResponse(uint Asn, string? Organization, string? Country, double RiskScore, bool IsHosting);
public sealed record EnrichUaResponse(string UserAgent, string? Browser, string? Os, string? DeviceType, bool IsBot, string? BotName, double RiskScore);
public sealed record ThreatLookupResponse(string Indicator, bool Found, string? ThreatType, string? Severity, string? Source, string? LastSeen);
public sealed record CanaryEvalResponse(string Token, bool Triggered, string? CanaryId, JsonNode? Metadata);
public sealed record AbusePatternMatchResponse(bool Matched, IReadOnlyList<PatternMatch> Patterns, double RiskScore);
public sealed record PatternMatch(string Pattern, string Category, double Confidence, string? MatchedText);
public sealed record DriftReportResponse(uint WindowHours, bool DriftDetected, DriftMetrics Metrics, string GeneratedAt);
public sealed record DriftMetrics(ulong Samples, double ScoreMean, double ScoreStddev, IReadOnlyDictionary<string, ulong> VerdictDistribution, IReadOnlyDictionary<string, double> SignalDrift);
public sealed record CalibrationReportResponse(uint WindowHours, ulong Samples, double Precision, double Recall, double F1, double FalsePositiveRate, double FalseNegativeRate, IReadOnlyList<string> Recommendations, string GeneratedAt);
public sealed record QueueStatusResponse(IReadOnlyList<QueueInfo> Queues);
public sealed record QueueInfo(string Name, ulong Depth, uint Consumers, double RatePerSecond);
