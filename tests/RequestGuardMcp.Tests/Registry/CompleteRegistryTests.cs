using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Tools;

namespace RequestGuardMcp.Tests.Registry;

public sealed class CompleteRegistryTests
{
    [Fact]
    public void FactoryRegistersAllTwentyThreeContracts()
    {
        var registry = ToolRegistryFactory.Create();

        Assert.Equal(23, registry.List().Count);
        Assert.Contains("calibration_report", registry.List());
        Assert.Contains("threat_lookup", registry.List());
    }

    [Fact]
    public void MissingRequiredFeedbackFieldsAreInvalidRequest()
    {
        var error = Assert.Throws<AppErrorException>(() => McpJson.RequiredParams<FeedbackRequest>(JsonNode.Parse("{}")));

        Assert.Equal(AppErrorKind.InvalidRequest, error.Kind);
    }

    [Fact]
    public async Task BackendDependentToolFailsTruthfullyWhenDisabled()
    {
        var config = new AppConfig { Auth = { Enabled = false } };
        await using var state = new AppState(config);
        var tool = ToolRegistryFactory.Create().Get("threat_lookup");

        var error = await Assert.ThrowsAsync<AppErrorException>(() => tool!.CallAsync(state, JsonNode.Parse("""{"indicator":"example.com"}"""), CancellationToken.None));

        Assert.Equal(AppErrorKind.IntegrationUnavailable, error.Kind);
    }

    [Fact]
    public void MetricsRenderPrometheusFamilies()
    {
        var config = new AppConfig { Auth = { Enabled = false } };
        var state = new AppState(config);
        state.Metrics.Record("classify", "ok", 0.01);

        var text = state.Metrics.RenderPrometheus();

        Assert.Contains("mcp_requests_total{tool=\"classify\",status=\"ok\"} 1", text, StringComparison.Ordinal);
        Assert.Contains("mcp_request_duration_seconds_count{tool=\"classify\"} 1", text, StringComparison.Ordinal);
    }
}
