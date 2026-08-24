namespace RequestGuardMcp.Core.Models.Signals;

/// <summary>Aggregated signal set from all engines for one request. Ports src/models/signals.rs's <c>SignalSet</c>.</summary>
public sealed class SignalSet
{
    private readonly List<Signal> _signals = [];

    public void Add(Signal signal) => _signals.Add(signal);

    public IReadOnlyList<Signal> Signals => _signals;

    /// <summary>Weighted sum of every signal's contribution, clamped to [0, 1].</summary>
    public double AggregateScore()
    {
        var totalWeight = _signals.Sum(s => s.Weight);
        if (totalWeight == 0.0)
        {
            return 0.0;
        }

        var sum = _signals.Sum(s => s.Contribution);
        return Math.Clamp(sum / totalWeight, 0.0, 1.0);
    }
}
