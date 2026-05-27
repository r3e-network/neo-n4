namespace Neo.L2.Telemetry;

/// <summary>
/// Lightweight circuit breaker for L1 RPC calls. Prevents cascading failures
/// when the L1 RPC endpoint is degraded — after the configurable failure threshold
/// consecutive failures, the circuit opens and subsequent calls fail fast
/// without touching the network. After the open timeout elapses, a single
/// probe call is allowed; if it succeeds, the circuit closes.
/// </summary>
/// <remarks>
/// For production, consider replacing this with Polly's
/// <c>CircuitBreakerPolicy</c> which provides more sophisticated
/// half-open handling, duration-based sampling, and integration with
/// <c>IServiceCollection</c>. This implementation is zero-dependency.
/// </remarks>
public sealed class CircuitBreaker
{
    private readonly Lock _gate = new();
    private readonly string _name;
    private readonly int _failureThreshold;
    private readonly TimeSpan _openTimeout;
    private int _failureCount;
    private DateTime _openedAt = DateTime.MinValue;

    /// <summary>Current state of the circuit.</summary>
    public CircuitState State
    {
        get
        {
            lock (_gate)
            {
                if (_failureCount >= _failureThreshold)
                {
                    if (DateTime.UtcNow - _openedAt > _openTimeout)
                        return CircuitState.HalfOpen;
                    return CircuitState.Open;
                }
                return CircuitState.Closed;
            }
        }
    }

    /// <summary>Diagnostic name for logging/metrics.</summary>
    public string Name => _name;

    /// <summary>Consecutive failures since last success.</summary>
    public int FailureCount { get { lock (_gate) return _failureCount; } }

    /// <summary>Construct.</summary>
    /// <param name="name">Diagnostic name (e.g. "L1-settlement-RPC").</param>
    /// <param name="failureThreshold">Consecutive failures before opening (default 5).</param>
    /// <param name="openTimeout">How long the circuit stays open (default 30s).</param>
    public CircuitBreaker(string name, int failureThreshold = 5, TimeSpan? openTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (failureThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(failureThreshold),
                "failureThreshold must be >= 1");
        _name = name;
        _failureThreshold = failureThreshold;
        _openTimeout = openTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Called before an operation. Returns <c>true</c> if the call may proceed.
    /// When the circuit is open, returns <c>false</c> immediately (fail-fast).
    /// When half-open, allows exactly one probe call.
    /// </summary>
    public bool TryEnter()
    {
        lock (_gate)
        {
            if (_failureCount >= _failureThreshold)
            {
                if (DateTime.UtcNow - _openedAt > _openTimeout)
                {
                    // Half-open: allow one probe call
                    return true;
                }
                return false; // Open: fail fast
            }
            return true; // Closed: allow
        }
    }

    /// <summary>Record a successful call. Resets the circuit.</summary>
    public void RecordSuccess()
    {
        lock (_gate)
        {
            _failureCount = 0;
            _openedAt = DateTime.MinValue;
        }
    }

    /// <summary>Record a failed call. May open the circuit.</summary>
    public void RecordFailure()
    {
        lock (_gate)
        {
            _failureCount++;
            if (_failureCount >= _failureThreshold)
                _openedAt = DateTime.UtcNow;
        }
    }
}

/// <summary>Circuit breaker state.</summary>
public enum CircuitState
{
    /// <summary>Normal operation — calls proceed.</summary>
    Closed,
    /// <summary>Circuit is open — calls fail fast.</summary>
    Open,
    /// <summary>Single probe call allowed to test recovery.</summary>
    HalfOpen,
}
