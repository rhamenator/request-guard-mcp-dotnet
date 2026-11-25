namespace RequestGuardMcp.Core.Runtime;

/// <summary>
/// Aggregate in-flight byte budget for request bodies and WebSocket messages (issue 81). A request reserves its
/// worst-case memory after authentication and before its body is read; when the budget is exhausted the request is
/// refused immediately instead of queueing, so total buffered input cannot exceed the deployment's memory envelope no
/// matter how many authenticated callers send maximum-size messages at once. Reservation is lock-free and never blocks.
/// </summary>
public sealed class RequestMemoryBudget(long capacityBytes)
{
    /// <summary>
    /// Worst-case bytes of memory held per byte of message: a growing <c>MemoryStream</c> can hold up to twice the message length
    /// as capacity, and the decoded UTF-16 string holds up to twice the message length again. JSON parsing and responses are not counted.
    /// </summary>
    public const int ExpansionFactor = 4;

    private long _used;

    public long Capacity => capacityBytes;
    public long Used => Interlocked.Read(ref _used);

    /// <summary>Reserve <paramref name="messageBytes"/> of message (times <see cref="ExpansionFactor"/>); null when it does not fit.</summary>
    public Lease? TryReserve(long messageBytes)
    {
        var lease = new Lease(this);
        return lease.TryGrow(messageBytes) ? lease : null;
    }

    private bool TryAdd(long bytes)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _used);
            if (current + bytes > capacityBytes)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _used, current + bytes, current) == current)
            {
                return true;
            }
        }
    }

    private void Release(long bytes) => Interlocked.Add(ref _used, -bytes);

    /// <summary>A reservation that can grow as a message arrives; disposing it returns everything it holds.</summary>
    public sealed class Lease : IDisposable
    {
        private readonly RequestMemoryBudget _budget;
        private long _held;

        internal Lease(RequestMemoryBudget budget) => _budget = budget;

        public long HeldBytes => Interlocked.Read(ref _held);

        /// <summary>Reserve <paramref name="messageBytes"/> more message bytes; false (nothing changes) when the budget is exhausted.</summary>
        public bool TryGrow(long messageBytes)
        {
            var bytes = checked(messageBytes * ExpansionFactor);
            if (!_budget.TryAdd(bytes))
            {
                return false;
            }

            Interlocked.Add(ref _held, bytes);
            return true;
        }

        public void Dispose()
        {
            var held = Interlocked.Exchange(ref _held, 0);
            if (held > 0)
            {
                _budget.Release(held);
            }
        }
    }
}
