using System.Text.Json;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Engines;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

/// <summary>The <c>explain</c> MCP tool. Ports src/tools/explain.rs.</summary>
public sealed class ExplainTool : IMcpTool
{

    public string Name => "explain";

    public string Description => "Explain a classification decision";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.RequiredParams<ExplainRequest>(parameters);

        // Attempt to extract a classify request from the provided classification value.
        ClassifyRequest? classifyRequest = null;
        if (request.Classification is not null)
        {
            try
            {
                classifyRequest = JsonSerializer.Deserialize<ClassifyRequest>(request.Classification, McpJson.Options);
            }
            catch (JsonException)
            {
                classifyRequest = null;
            }
        }

        var requestId = classifyRequest?.RequestId ?? Guid.NewGuid().ToString();

        ExplainResponse response;
        if (classifyRequest is not null)
        {
            var signals = RuleEngine.Evaluate(classifyRequest);
            var result = Scorer.Score(signals);
            response = ExplainEngine.ExplainSignals(requestId, signals, result.Score, result.Verdict.ToString().ToLowerInvariant());
        }
        else
        {
            response = new ExplainResponse(
                requestId,
                "Explanation generated from pre-computed classification result.",
                [],
                ["Provide a full classify request for detailed explanation."]);
        }

        return Task.FromResult(JsonSerializer.SerializeToNode(response, McpJson.Options));
    }
}
