using System.Collections.Concurrent;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.L2;

namespace Neo.Plugins.L2;

/// <summary>
/// Data Availability Committee (DAC) writer — N committee signers attest to availability
/// by signing the payload commitment hash. The aggregated signatures are stored as the
/// receipt <c>Pointer</c>; <see cref="IsAvailableAsync"/> verifies all N signatures
/// against the committee public keys before answering true.
/// </summary>
/// <remarks>
/// In-process backing for tests, devnets, and single-node demos. Production replaces the
/// in-memory store with networked distribution while keeping the cryptographic shape:
/// one 64-byte secp256r1 signature per committee member, all signing the same Hash256
/// commitment. The receipt is L1-recoverable — `NeoHub.DARegistry` can verify the same
/// signatures on-chain by re-running the loop here against the committee declared in
/// `ChainRegistry`.
/// <para>
/// Threshold-DAC (k-of-N) is layered above this by trimming the signature set to the
/// chosen quorum before submission and tracking which seats signed; the on-chain
/// verifier then accepts any k valid signatures from the committee. This base writer
/// is full-N for simplicity.
/// </para>
/// <para>
/// Wire via <c>L2DAPlugin.WithWriter(new CommitteeAttestedDAWriter(committee, signFn))</c>
/// before <c>L2DAPlugin.Configure</c> runs. The <c>sign</c> callback is intentionally
/// injected (not a held private-key set) so production deployments can back it with HSMs,
/// remote signers, or threshold-signing protocols without touching this class.
/// </para>
/// </remarks>
public sealed class CommitteeAttestedDAWriter : IDAWriter
{
    private readonly ConcurrentDictionary<UInt256, byte[]> _store = new();
    private readonly IReadOnlyList<ECPoint> _committee;
    private readonly Func<UInt256, IReadOnlyList<byte[]>> _sign;

    /// <inheritdoc />
    public DAMode Mode => DAMode.DAC;

    /// <summary>The committee public keys (canonicalized order matters for verification).</summary>
    public IReadOnlyList<ECPoint> Committee => _committee;

    /// <summary>
    /// Construct with the committee public keys (in fixed canonical order) and a signing
    /// callback that returns one 64-byte signature per committee member, in the same order.
    /// </summary>
    public CommitteeAttestedDAWriter(
        IEnumerable<ECPoint> committee,
        Func<UInt256, IReadOnlyList<byte[]>> sign)
    {
        ArgumentNullException.ThrowIfNull(committee);
        ArgumentNullException.ThrowIfNull(sign);
        var list = committee.ToList();
        if (list.Count == 0)
            throw new ArgumentException("DAC committee must have at least one signer", nameof(committee));
        // Per-entry null-guard: ECPoint is reference-typed and a null entry would NRE
        // inside Crypto.VerifySignature deep in IsAvailableAsync. Same iter-179 / iter-188
        // per-entry pattern.
        for (var i = 0; i < list.Count; i++)
            if (list[i] is null)
                throw new ArgumentException($"committee[{i}] is null", nameof(committee));
        _committee = list;
        _sign = sign;
    }

    /// <inheritdoc />
    public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var commitment = new UInt256(Crypto.Hash256(request.Payload.Span));
        // Defensive copy of the payload so a caller who reuses a scratch buffer can't
        // silently corrupt the stored bytes. Same iter-167 pattern as RecordWithdrawalProof.
        _store[commitment] = request.Payload.ToArray();

        // The signing callback returns one 64-byte signature per committee member, in the
        // same order as Committee. Validate the callee contract — same iter-171 callee-
        // contract pattern: a buggy signing callback that returns null / wrong-count /
        // wrong-length would otherwise NRE inside the CopyTo below or fail far downstream
        // at IsAvailableAsync with a confusing "verification failed" rather than a clear
        // contract violation message.
        var sigs = _sign(commitment) ?? throw new InvalidOperationException(
            "DAC sign callback returned null IReadOnlyList");
        if (sigs.Count != _committee.Count)
            throw new InvalidOperationException(
                $"DAC sign callback returned {sigs.Count} signatures but committee is {_committee.Count}");

        var pointer = new byte[sigs.Count * 64];
        for (var i = 0; i < sigs.Count; i++)
        {
            var sig = sigs[i];
            if (sig is null)
                throw new InvalidOperationException($"DAC sign callback returned null signature[{i}]");
            if (sig.Length != 64)
                throw new InvalidOperationException(
                    $"DAC sign callback returned signature[{i}] of {sig.Length} bytes (must be exactly 64)");
            sig.AsSpan(0, 64).CopyTo(pointer.AsSpan(i * 64, 64));
        }

        return new ValueTask<DAReceipt>(new DAReceipt
        {
            Commitment = commitment,
            Pointer = pointer,
            Layer = Mode,
        });
    }

    /// <inheritdoc />
    public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(receipt);
        // UInt256 reference-typed; null would NRE in ContainsKey deep below. Same iter-148
        // boundary pattern.
        ArgumentNullException.ThrowIfNull(receipt.Commitment);

        // Reject quickly if we never saw the data or the receipt's signature shape is
        // inconsistent with the committee.
        if (!_store.ContainsKey(receipt.Commitment)) return new ValueTask<bool>(false);
        if (receipt.Pointer.Length != _committee.Count * 64) return new ValueTask<bool>(false);

        var msg = receipt.Commitment.GetSpan().ToArray();
        for (var i = 0; i < _committee.Count; i++)
        {
            var sigSpan = receipt.Pointer.Span.Slice(i * 64, 64);
            if (!Crypto.VerifySignature(msg, sigSpan, _committee[i]))
                return new ValueTask<bool>(false);
        }
        return new ValueTask<bool>(true);
    }
}
