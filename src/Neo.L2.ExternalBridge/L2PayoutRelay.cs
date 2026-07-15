namespace Neo.L2.ExternalBridge;

/// <summary>Exact target-L2 state observed for one source domain and nonce.</summary>
/// <remarks>See <c>doc.md</c> §11.3.</remarks>
public sealed record L2PayoutCreditObservation(UInt256 MessageHash, UInt256 TransactionHash)
{
    /// <summary>No target credit exists yet.</summary>
    public static L2PayoutCreditObservation Missing { get; } = new(UInt256.Zero, UInt256.Zero);

    /// <summary>Whether target credit exists.</summary>
    public bool IsApplied => MessageHash != UInt256.Zero;
}

/// <summary>Production seam for authenticated target-L2 credit transactions.</summary>
/// <remarks>See <c>doc.md</c> §11.3.</remarks>
public interface IL2PayoutCreditClient
{
    /// <summary>Read the exact consumed message and transaction hash for the payout nonce.</summary>
    ValueTask<L2PayoutCreditObservation> ObserveAsync(
        L2PayoutInstruction instruction,
        CancellationToken cancellationToken = default);

    /// <summary>Build and sign the exact target credit transaction without broadcasting it.</summary>
    ValueTask<ReadOnlyMemory<byte>> PrepareAsync(
        L2PayoutInstruction instruction,
        CancellationToken cancellationToken = default);

    /// <summary>Broadcast a previously persisted signed target transaction and confirm it.</summary>
    ValueTask<UInt256> BroadcastAsync(
        ReadOnlyMemory<byte> signedTransaction,
        CancellationToken cancellationToken = default);
}

/// <summary>Exact L1 adapter acknowledgement state.</summary>
/// <remarks>See <c>doc.md</c> §11.3.</remarks>
public sealed record L1PayoutAcknowledgementObservation(bool Acknowledged, UInt256 L2TransactionHash)
{
    /// <summary>No acknowledgement exists yet.</summary>
    public static L1PayoutAcknowledgementObservation Missing { get; } =
        new(false, UInt256.Zero);
}

/// <summary>Production seam for authenticated L1 adapter acknowledgement transactions.</summary>
/// <remarks>See <c>doc.md</c> §11.3.</remarks>
public interface IL1PayoutAcknowledgementClient
{
    /// <summary>Read current acknowledgement state for the exact queue sequence.</summary>
    ValueTask<L1PayoutAcknowledgementObservation> ObserveAsync(
        L2PayoutInstruction instruction,
        CancellationToken cancellationToken = default);

    /// <summary>Build and sign the exact acknowledgement without broadcasting it.</summary>
    ValueTask<ReadOnlyMemory<byte>> PrepareAsync(
        L2PayoutInstruction instruction,
        UInt256 l2TransactionHash,
        CancellationToken cancellationToken = default);

    /// <summary>Broadcast a previously persisted signed acknowledgement and confirm it.</summary>
    ValueTask<UInt256> BroadcastAsync(
        ReadOnlyMemory<byte> signedTransaction,
        CancellationToken cancellationToken = default);
}

/// <summary>Crash-safe enqueue, target credit, and L1 acknowledgement coordinator.</summary>
/// <remarks>
/// See <c>doc.md</c> §11.3 and §17. This is intentionally not described as atomic cross-chain
/// settlement. It is a durable at-least-once relay whose target native endpoint and L1 adapter are
/// both exact-hash idempotent. Prepared signed transactions are write-ahead logged before broadcast.
/// </remarks>
public sealed class L2PayoutRelay : IDisposable
{
    private readonly PersistentL2PayoutOutbox _outbox;
    private readonly IL2PayoutCreditClient _creditClient;
    private readonly IL1PayoutAcknowledgementClient _acknowledgementClient;
    private readonly int _maximumRetries;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private int _disposed;

    /// <summary>Construct the coordinator over caller-owned clients and durable outbox.</summary>
    public L2PayoutRelay(
        PersistentL2PayoutOutbox outbox,
        IL2PayoutCreditClient creditClient,
        IL1PayoutAcknowledgementClient acknowledgementClient,
        int maximumRetries = 10)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(creditClient);
        ArgumentNullException.ThrowIfNull(acknowledgementClient);
        if (maximumRetries <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRetries));
        _outbox = outbox;
        _creditClient = creditClient;
        _acknowledgementClient = acknowledgementClient;
        _maximumRetries = maximumRetries;
    }

    /// <summary>Process up to <paramref name="maximumItems"/> durable payouts once.</summary>
    public async ValueTask<int> ProcessAsync(
        int maximumItems = 100,
        CancellationToken cancellationToken = default)
    {
        if (maximumItems <= 0) throw new ArgumentOutOfRangeException(nameof(maximumItems));
        ThrowIfDisposed();
        await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var completed = 0;
            foreach (var checkpoint in _outbox.LoadPending().Take(maximumItems))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (checkpoint.State == L2PayoutRelayState.Poisoned) continue;
                try
                {
                    var updated = await ProcessOneAsync(checkpoint, cancellationToken)
                        .ConfigureAwait(false);
                    if (updated.State == L2PayoutRelayState.Acknowledged) completed++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _outbox.RecordFailure(checkpoint, exception, _maximumRetries);
                }
            }
            return completed;
        }
        finally
        {
            _runGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _runGate.Dispose();
    }

    private async ValueTask<L2PayoutRelayCheckpoint> ProcessOneAsync(
        L2PayoutRelayCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        if (checkpoint.State is L2PayoutRelayState.Enqueued or L2PayoutRelayState.CreditPrepared)
            checkpoint = await EnsureCreditedAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        if (checkpoint.State is L2PayoutRelayState.Credited
            or L2PayoutRelayState.AcknowledgementPrepared)
        {
            checkpoint = await EnsureAcknowledgedAsync(checkpoint, cancellationToken)
                .ConfigureAwait(false);
        }
        return checkpoint;
    }

    private async ValueTask<L2PayoutRelayCheckpoint> EnsureCreditedAsync(
        L2PayoutRelayCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var instruction = checkpoint.Instruction;
        var observed = await _creditClient.ObserveAsync(instruction, cancellationToken)
            .ConfigureAwait(false);
        if (observed.IsApplied)
        {
            AssertExactCredit(instruction, observed);
            return _outbox.MarkCredited(checkpoint, observed.TransactionHash);
        }

        if (checkpoint.State == L2PayoutRelayState.Enqueued)
        {
            var transaction = await _creditClient.PrepareAsync(instruction, cancellationToken)
                .ConfigureAwait(false);
            checkpoint = _outbox.SavePreparedCredit(checkpoint, transaction);
        }

        await _creditClient.BroadcastAsync(
            checkpoint.PreparedCreditTransaction, cancellationToken).ConfigureAwait(false);
        observed = await _creditClient.ObserveAsync(instruction, cancellationToken)
            .ConfigureAwait(false);
        AssertExactCredit(instruction, observed);
        return _outbox.MarkCredited(checkpoint, observed.TransactionHash);
    }

    private async ValueTask<L2PayoutRelayCheckpoint> EnsureAcknowledgedAsync(
        L2PayoutRelayCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var instruction = checkpoint.Instruction;
        var observed = await _acknowledgementClient.ObserveAsync(instruction, cancellationToken)
            .ConfigureAwait(false);
        if (observed.Acknowledged)
        {
            AssertExactAcknowledgement(checkpoint, observed);
            return _outbox.MarkAcknowledged(checkpoint);
        }

        if (checkpoint.State == L2PayoutRelayState.Credited)
        {
            var transaction = await _acknowledgementClient.PrepareAsync(
                instruction, checkpoint.L2TransactionHash, cancellationToken).ConfigureAwait(false);
            checkpoint = _outbox.SavePreparedAcknowledgement(checkpoint, transaction);
        }

        await _acknowledgementClient.BroadcastAsync(
            checkpoint.PreparedAcknowledgementTransaction, cancellationToken).ConfigureAwait(false);
        observed = await _acknowledgementClient.ObserveAsync(instruction, cancellationToken)
            .ConfigureAwait(false);
        AssertExactAcknowledgement(checkpoint, observed);
        return _outbox.MarkAcknowledged(checkpoint);
    }

    private static void AssertExactCredit(
        L2PayoutInstruction instruction,
        L2PayoutCreditObservation observation)
    {
        if (!observation.IsApplied)
            throw new InvalidOperationException("L2 credit transaction did not consume the payout.");
        if (observation.MessageHash != instruction.Message.MessageHash)
            throw new InvalidDataException("L2 nonce is consumed by a different message hash.");
        if (observation.TransactionHash == UInt256.Zero)
            throw new InvalidDataException("L2 credit has no transaction hash.");
    }

    private static void AssertExactAcknowledgement(
        L2PayoutRelayCheckpoint checkpoint,
        L1PayoutAcknowledgementObservation observation)
    {
        if (!observation.Acknowledged)
            throw new InvalidOperationException("L1 adapter acknowledgement was not persisted.");
        if (observation.L2TransactionHash != checkpoint.L2TransactionHash)
            throw new InvalidDataException("L1 adapter acknowledges a different L2 transaction.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
