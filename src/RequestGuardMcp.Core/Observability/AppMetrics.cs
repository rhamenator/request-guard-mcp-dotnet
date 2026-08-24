using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace RequestGuardMcp.Core.Observability;

/// <summary>Thread-safe MCP counters and a Prometheus text renderer without a runtime exporter dependency.</summary>
public sealed class AppMetrics
{
    private static readonly double[] BucketBounds = [0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];
    private readonly ConcurrentDictionary<(string Tool, string Status), long> _requests = new();
    private readonly ConcurrentDictionary<(string Tool, string Code), long> _errors = new();
    private readonly ConcurrentDictionary<string, DurationMetric> _durations = new(StringComparer.Ordinal);
    private long _activeConnections;

    public void ConnectionOpened() => Interlocked.Increment(ref _activeConnections);
    public void ConnectionClosed() => Interlocked.Decrement(ref _activeConnections);

    public void Record(string tool, string status, double durationSeconds, string? errorCode = null)
    {
        _requests.AddOrUpdate((tool, status), 1, static (_, current) => current + 1);
        if (errorCode is not null)
        {
            _errors.AddOrUpdate((tool, errorCode), 1, static (_, current) => current + 1);
        }

        _durations.GetOrAdd(tool, static _ => new DurationMetric()).Record(durationSeconds);
    }

    public string RenderPrometheus()
    {
        var output = new StringBuilder();
        output.AppendLine("# HELP mcp_requests_total Total number of MCP tool requests");
        output.AppendLine("# TYPE mcp_requests_total counter");
        foreach (var item in _requests.OrderBy(item => item.Key.Tool, StringComparer.Ordinal).ThenBy(item => item.Key.Status, StringComparer.Ordinal))
        {
            output.Append("mcp_requests_total{tool=\"").Append(Escape(item.Key.Tool)).Append("\",status=\"").Append(Escape(item.Key.Status)).Append("\"} ").AppendLine(item.Value.ToString(CultureInfo.InvariantCulture));
        }

        output.AppendLine("# HELP mcp_request_duration_seconds MCP tool request duration in seconds");
        output.AppendLine("# TYPE mcp_request_duration_seconds histogram");
        foreach (var item in _durations.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            item.Value.AppendPrometheus(output, item.Key);
        }

        output.AppendLine("# HELP mcp_active_connections Number of active WebSocket connections");
        output.AppendLine("# TYPE mcp_active_connections gauge");
        output.Append("mcp_active_connections ").AppendLine(Interlocked.Read(ref _activeConnections).ToString(CultureInfo.InvariantCulture));
        output.AppendLine("# HELP mcp_tool_errors_total Total number of tool errors by type");
        output.AppendLine("# TYPE mcp_tool_errors_total counter");
        foreach (var item in _errors.OrderBy(item => item.Key.Tool, StringComparer.Ordinal).ThenBy(item => item.Key.Code, StringComparer.Ordinal))
        {
            output.Append("mcp_tool_errors_total{tool=\"").Append(Escape(item.Key.Tool)).Append("\",error_code=\"").Append(Escape(item.Key.Code)).Append("\"} ").AppendLine(item.Value.ToString(CultureInfo.InvariantCulture));
        }

        return output.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private sealed class DurationMetric
    {
        private readonly long[] _buckets = new long[BucketBounds.Length];
        private long _count;
        private double _sum;

        public void Record(double seconds)
        {
            Interlocked.Increment(ref _count);
            AtomicAdd(ref _sum, seconds);
            for (var index = 0; index < BucketBounds.Length; index++)
            {
                if (seconds <= BucketBounds[index])
                {
                    Interlocked.Increment(ref _buckets[index]);
                }
            }
        }

        public void AppendPrometheus(StringBuilder output, string tool)
        {
            var escaped = Escape(tool);
            for (var index = 0; index < BucketBounds.Length; index++)
            {
                output.Append("mcp_request_duration_seconds_bucket{tool=\"").Append(escaped).Append("\",le=\"")
                    .Append(BucketBounds[index].ToString(CultureInfo.InvariantCulture)).Append("\"} ")
                    .AppendLine(Interlocked.Read(ref _buckets[index]).ToString(CultureInfo.InvariantCulture));
            }

            output.Append("mcp_request_duration_seconds_bucket{tool=\"").Append(escaped).Append("\",le=\"+Inf\"} ").AppendLine(Interlocked.Read(ref _count).ToString(CultureInfo.InvariantCulture));
            output.Append("mcp_request_duration_seconds_sum{tool=\"").Append(escaped).Append("\"} ").AppendLine(Volatile.Read(ref _sum).ToString(CultureInfo.InvariantCulture));
            output.Append("mcp_request_duration_seconds_count{tool=\"").Append(escaped).Append("\"} ").AppendLine(Interlocked.Read(ref _count).ToString(CultureInfo.InvariantCulture));
        }

        private static void AtomicAdd(ref double location, double value)
        {
            double current;
            do
            {
                current = location;
            }
            while (Interlocked.CompareExchange(ref location, current + value, current) != current);
        }
    }
}
