namespace RequestGuardMcp.Core.Models.Enums;

/// <summary>Classification verdict for a request. Ports src/models/enums.rs's <c>Verdict</c>.</summary>
public enum Verdict
{
    Allow,
    Block,
    Challenge,
    Flag,
    Unknown,
}
