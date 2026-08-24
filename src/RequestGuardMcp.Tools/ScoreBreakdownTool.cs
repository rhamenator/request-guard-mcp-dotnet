using System.Text.Json;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Engines;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Models.Signals;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

/// <summary>The <c>score_breakdown</c> MCP tool. Ports src/tools/score_breakdown.rs.</summary>
public sealed class ScoreBreakdownTool : IMcpTool
{

    public string Name => "score_breakdown";

    public string Description => "Detailed score breakdown";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.ParamsOrDefault<ScoreBreakdownRequest>(parameters);

        var signals = new SignalSet();
        if (request.Signals is not null)
        {
            try
            {
                var classifyRequest = JsonSerializer.Deserialize<ClassifyRequest>(request.Signals, McpJson.Options);
                if (classifyRequest is not null)
                {
                    signals = RuleEngine.Evaluate(classifyRequest);
                }
            }
            catch (JsonException)
            {
                // Fall through with an empty signal set, matching the Rust tool's behavior.
            }
        }

        var result = Scorer.Score(signals);
        var breakdown = signals.Signals
            .Select(s => new ScoreComponent("rule_engine", s.Value, s.Weight, s.Value * s.Weight, [s.Name]))
            .ToList();

        var response = new ScoreBreakdownResponse(
            request.RequestId,
            result.Score,
            breakdown,
            Scorer.ThresholdAllow,
            Scorer.ThresholdBlock);

        return Task.FromResult(JsonSerializer.SerializeToNode(response, McpJson.Options));
    }
}
