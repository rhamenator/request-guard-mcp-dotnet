namespace RequestGuardMcp.Core.Models.Request;

/// <summary>Ports src/models/request.rs's <c>BatchClassifyRequest</c>.</summary>
public sealed class BatchClassifyRequest
{
    public List<ClassifyRequest> Items { get; set; } = [];
    public BatchOptions? Options { get; set; }
}

/// <summary>Ports src/models/request.rs's <c>BatchOptions</c>.</summary>
public sealed class BatchOptions
{
    public bool FailFast { get; set; }
    public bool IncludeDetails { get; set; }
}
