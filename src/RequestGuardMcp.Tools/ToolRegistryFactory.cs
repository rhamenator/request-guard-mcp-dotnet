using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

/// <summary>Creates the complete, wire-compatible 23-tool registry.</summary>
public static class ToolRegistryFactory
{
    public static ToolRegistry Create()
    {
        var registry = new ToolRegistry();
        registry.Register(new HealthTool());
        registry.Register(new ModelInfoTool(registry));
        registry.Register(new ClassifyTool());
        registry.Register(new BatchClassifyTool());
        registry.Register(new ExplainTool());
        registry.Register(new ScoreBreakdownTool());
        registry.Register(new ValidatePayloadTool());
        registry.Register(new FeatureFlagsTool());
        registry.Register(new RedactPreviewTool());
        registry.Register(new ConfigSnapshotTool());
        registry.Register(new SelfTestTool());
        registry.Register(new WarmupTool());
        registry.Register(new FeedbackTool());
        registry.Register(new ReplayDecisionTool());
        registry.Register(new EnrichIpTool());
        registry.Register(new EnrichAsnTool());
        registry.Register(new EnrichUaTool());
        registry.Register(new ThreatLookupTool());
        registry.Register(new CanaryEvalTool());
        registry.Register(new AbusePatternMatchTool());
        registry.Register(new DriftReportTool());
        registry.Register(new CalibrationReportTool());
        registry.Register(new QueueStatusTool());
        return registry;
    }
}
