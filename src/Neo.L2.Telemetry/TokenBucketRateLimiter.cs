namespace Neo.L2.Telemetry;

/// <summary>
/// Thread-safe token bucket rate limiter. Tokens refill at a configurable rate
/// up to a burst capacity. When tokens are exhausted, <see cref="TryConsume"/>
/// returns false (fail-closed). Zero dependencies.
/// </summary>
/// <remarks>
/// For production with ASP.NET, consider <c>AspNetCoreRateLimit</c> or
/// <c>System.Threading.RateLimiting</c> (.NET 7+). This implementation
/// works for both ASP.NET middleware and standalone services.
/// </remarks>
public sealed class TokenBucketRateLimiter
{
    private readonly Lock _gate = new();
    private readonly double _refillRatePerSecond;
    private readonly int _burstCapacity;
    private double _tokens;
    private DateTime _lastRefillUtc = DateTime.UtcNow;

    /// <summary>Current token count (diagnostic).</summary>
    public double AvailableTokens { get { lock (_gate) { Refill(); return _tokens; } } }

    /// <summary>Maximum burst capacity.</summary>
    public int BurstCapacity => _burstCapacity;

    /// <summary>Refill rate in tokens per second.</summary>
    public double RefillRatePerSecond => _refillRatePerSecond;

    /// <summary>Construct.</summary>
    /// <param name="refillRatePerSecond">Tokens added per second (e.g. 100 = 100 req/s sustained).</param>
    /// <param name="burstCapacity">Maximum tokens that can accumulate (e.g. 200 = bursts of 200).</param>
    public TokenBucketRateLimiter(double refillRatePerSecond = 100, int burstCapacity = 200)
    {
        if (refillRatePerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(refillRatePerSecond));
        if (burstCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(burstCapacity));
        _refillRatePerSecond = refillRatePerSecond;
        _burstCapacity = burstCapacity;
        _tokens = burstCapacity; // Start full — don't penalize startup bursts
    }

    /// <summary>
    /// Try to consume one token. Returns true if allowed, false if rate-limited.
    /// </summary>
    public bool TryConsume(int count = 1)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        lock (_gate)
        {
            Refill();
            if (_tokens >= count)
            {
                _tokens -= count;
                return true;
            }
            return false;
        }
    }

    private void Refill()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastRefillUtc).TotalSeconds;
        if (elapsed <= 0) return;
        _tokens = Math.Min(_burstCapacity, _tokens + elapsed * _refillRatePerSecond);
        _lastRefillUtc = now;
    }
}
