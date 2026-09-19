using Neo.L2;

namespace Neo.Plugins.L2Gateway;

/// <summary>Retries timeout failures from an idempotent Gateway prover with bounded exponential backoff.</summary>
/// <remarks>
/// See doc.md §4. The caller must supply an idempotent prover: IGatewayProofProver itself does
/// not guarantee idempotency. Sp1GatewayProofProver reuses exact content-addressed requests;
/// retrying it extends result waiting and does not restart or cancel a daemon job.
/// Protocol, validation, and cancellation exceptions propagate without retry.
/// This decorator is not automatically wired into a production host.
/// </remarks>
public sealed class RetryingGatewayProofProver : IGatewayProofProver
{
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(1);
    private readonly IGatewayProofProver _inner;
    private readonly int _maxAttempts;
    private readonly TimeSpan _initialBackoff;
    private readonly double _backoffMultiplier;

    /// <summary>Wrap an idempotent prover; attempts include the initial call and backoff is capped at one minute.</summary>
    public RetryingGatewayProofProver(
        IGatewayProofProver inner,
        int maxAttempts = 3,
        TimeSpan? initialBackoff = null,
        double backoffMultiplier = 2.0)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "maxAttempts must be at least 1");
        var backoff = initialBackoff ?? TimeSpan.FromSeconds(5);
        if (backoff <= TimeSpan.Zero || backoff > MaximumBackoff)
            throw new ArgumentOutOfRangeException(nameof(initialBackoff), "initialBackoff must be in (0, 1 minute]");
        if (!double.IsFinite(backoffMultiplier) || backoffMultiplier < 1.0)
            throw new ArgumentOutOfRangeException(nameof(backoffMultiplier), "backoffMultiplier must be finite and >= 1.0");

        _inner = inner;
        _maxAttempts = maxAttempts;
        _initialBackoff = backoff;
        _backoffMultiplier = backoffMultiplier;
    }

    /// <inheritdoc />
    public byte ProofSystem => _inner.ProofSystem;

    /// <inheritdoc />
    public byte AggregationBackendId => _inner.AggregationBackendId;

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>> ProveAsync(
        GatewayProofBinding binding,
        AggregatedCommitment commitment,
        CancellationToken cancellationToken = default)
    {
        var backoff = _initialBackoff;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await _inner.ProveAsync(binding, commitment, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException) when (attempt < _maxAttempts)
            {
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                backoff = TimeSpan.FromTicks((long)Math.Min(
                    MaximumBackoff.Ticks, backoff.Ticks * _backoffMultiplier));
            }
        }
    }
}
