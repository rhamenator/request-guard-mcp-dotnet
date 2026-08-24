namespace RequestGuardMcp.Core.Models.Signals;

/// <summary>Where a <see cref="Signal"/> came from. Ports src/models/signals.rs's <c>SignalSource</c>.</summary>
public enum SignalSource
{
    RuleEngine,
    Scorer,
    Heuristic,
    External,
    Cache,
}

/// <summary>A computed signal value used during classification. Ports src/models/signals.rs's <c>Signal</c>.</summary>
public sealed record Signal(string Name, double Value, double Weight, SignalSource Source, string Description)
{
    /// <summary>Weighted contribution of this signal to the final score.</summary>
    public double Contribution => Value * Weight;
}
