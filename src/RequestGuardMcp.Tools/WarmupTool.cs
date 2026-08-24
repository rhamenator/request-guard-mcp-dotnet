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

/// <summary>The <c>warmup</c> MCP tool. Ports src/tools/warmup.rs.</summary>
public sealed class WarmupTool : IMcpTool
{
    public string Name => "warmup";

    public string Description => "Warm up caches";

    public async Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.ParamsOrDefault<WarmupRequest>(parameters);
        var stopwatch = Stopwatch.StartNew();
        var target = request.Target ?? "all";

        var warmed = new List<string>();
        var skipped = new List<string>();
        var targets = target == "all" ? ["rule_engine", "scorer", "cache"] : new[] { target };

        foreach (var t in targets)
        {
            switch (t)
            {
                case "rule_engine" or "scorer":
                    // Pre-run a dummy classify to JIT the regex patterns / prime the pipeline.
                    var dummy = new ClassifyRequest
                    {
                        Ip = "127.0.0.1",
                        UserAgent = "warmup",
                        Path = "/warmup",
                        Method = "GET",
                        RequestId = "warmup",
                    };
                    try
                    {
                        await ClassifyService.RunEphemeralAsync(state, dummy, cancellationToken).ConfigureAwait(false);
                        warmed.Add(t);
                    }
                    catch (AppErrorException)
                    {
                        skipped.Add(t);
                    }

                    break;

                case "cache":
                    await state.Cache.SetAsync("__warmup__", JsonNode.Parse("""{"ok":true}""")!, cancellationToken).ConfigureAwait(false);
                    warmed.Add(t);
                    break;

                default:
                    skipped.Add(t);
                    break;
            }
        }

        var response = new WarmupResponse(warmed, skipped, (ulong)stopwatch.ElapsedMilliseconds);
        return JsonSerializer.SerializeToNode(response, McpJson.Options);
    }
}
