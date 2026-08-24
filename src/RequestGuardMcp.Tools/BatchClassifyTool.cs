using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Classification;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

/// <summary>The <c>batch_classify</c> MCP tool. Ports src/tools/batch_classify.rs.</summary>
public sealed class BatchClassifyTool : IMcpTool
{
    public string Name => "batch_classify";

    public string Description => "Classify multiple requests";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken) =>
        CallScopedAsync(state, parameters, "internal", cancellationToken);

    public async Task<JsonNode?> CallScopedAsync(AppState state, JsonNode? parameters, string callerScope, CancellationToken cancellationToken)
    {
        if (!state.Config.Features.EnableBatch)
        {
            throw AppErrorException.IntegrationUnavailable("batch_classify is disabled by configuration");
        }

        var request = McpJson.RequiredParams<BatchClassifyRequest>(parameters);

        var stopwatch = Stopwatch.StartNew();
        var max = state.Config.Limits.MaxBatchSize;
        var got = request.Items.Count;

        if (got > max)
        {
            throw AppErrorException.BatchTooLarge(max, got);
        }

        var results = new List<BatchItemResult>(got);
        var errorCount = 0;
        var failFast = request.Options?.FailFast ?? false;

        for (var index = 0; index < request.Items.Count; index++)
        {
            try
            {
                var result = await ClassifyService.RunAsync(state, request.Items[index], callerScope, cancellationToken).ConfigureAwait(false);
                results.Add(new BatchItemResult(index, result, null));
            }
            catch (AppErrorException error)
            {
                errorCount++;
                if (failFast)
                {
                    throw;
                }

                results.Add(new BatchItemResult(index, null, error.Code));
            }
        }

        var response = new BatchClassifyResponse(
            Results: results,
            Total: got,
            Processed: results.Count - errorCount,
            Errors: errorCount,
            LatencyMs: (ulong)stopwatch.ElapsedMilliseconds);

        return JsonSerializer.SerializeToNode(response, McpJson.Options);
    }
}
