using System.Buffers.Binary;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.L2.Proving.Attestation;

namespace Neo.Plugins.L2Gateway;

/// <summary>
/// Production <see cref="IRoundProver"/> for chains aggregating with a prover committee.
/// Each round threshold-signs the canonical concatenation of the two child commitments;
/// the round's <see cref="RoundResult.ProofBytes"/> is the encoded
/// <see cref="MultisigProofPayload"/> over those signatures.
/// </summary>
/// <remarks>
/// <para>
/// This is real production cryptography: every signature is a Secp256r1 ECDSA signature
/// produced by an <see cref="ISignerSet"/> the operator wires (HSM, remote signer, KMS).
/// The threshold <see cref="Threshold"/> determines how many committee members must sign
/// for a round to succeed; insufficient signatures throw at <see cref="Combine"/> time
/// rather than producing a silently-weak aggregate.
/// </para>
/// <para>
/// Verification is symmetric: <see cref="VerifyRound"/> decodes the
/// <see cref="MultisigProofPayload"/>, re-derives the canonical message hash from the two
/// child commitments, and asserts that at least <see cref="Threshold"/> signatures are
/// valid against the canonical validator set.
/// </para>
/// <para>
/// Pair with <see cref="BinaryTreeAggregator"/>; replaces <see cref="PassThroughRoundProver"/>
/// for chains that want every level of the aggregation tree to carry a committee attestation
/// without needing a recursive-ZK toolchain.
/// </para>
/// </remarks>
public sealed class MultisigRoundProver : IRoundProver
{
    /// <summary>Stable backend identifier emitted in <see cref="AggregatedCommitment.BackendId"/>.</summary>
    public const byte ConstBackendId = 0xC0;

    /// <summary>Canonical message-prefix domain-separation tag (1 byte).</summary>
    private const byte DomainTag = 0x4D; // 'M' for "Multisig round"

    private readonly ISignerSet _signers;

    /// <summary>Minimum number of signatures required per round.</summary>
    public int Threshold { get; }

    /// <summary>The validator set this prover trusts (sorted, canonical order).</summary>
    public IReadOnlyList<ECPoint> ValidatorKeys => _signers.ValidatorKeys;

    /// <inheritdoc />
    public byte BackendId => ConstBackendId;

    /// <summary>Construct with a signer set + threshold.</summary>
    public MultisigRoundProver(ISignerSet signers, int threshold)
    {
        ArgumentNullException.ThrowIfNull(signers);
        if (threshold < 1)
            throw new ArgumentException($"threshold must be >= 1, got {threshold}", nameof(threshold));
        if (threshold > signers.ValidatorKeys.Count)
            throw new ArgumentException(
                $"threshold {threshold} exceeds validator count {signers.ValidatorKeys.Count}",
                nameof(threshold));
        _signers = signers;
        Threshold = threshold;
    }

    /// <inheritdoc />
    public RoundResult Combine(RoundResult left, RoundResult? right)
    {
        ArgumentNullException.ThrowIfNull(left);
        if (right is null) return left; // Merkle odd-leaf rule

        var canonicalMessage = CanonicalRoundMessage(left, right);
        // ISignerSet.SignAsync is inherently async (HSM/KMS backends). Run it on a
        // background thread and synchronously await with ConfigureAwait(false) to
        // prevent SynchronizationContext capture deadlocks. GetAwaiter().GetResult()
        // unwraps exceptions directly (unlike .Result which wraps in AggregateException).
        // TODO: make IRoundProver.Combine return ValueTask<RoundResult> so the async
        // propagates cleanly through BinaryTreeAggregator → settlement plugin.
        var sigs = Task.Run(async () => await _signers.SignAsync(canonicalMessage).ConfigureAwait(false))
            .GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("ISignerSet.SignAsync returned null");
        if (sigs.Count < Threshold)
            throw new InvalidOperationException(
                $"got {sigs.Count} signatures, threshold {Threshold} not met");

        // Canonicalize signature ordering: sort by public-key bytes so the encoded proof is
        // deterministic regardless of signer-callback order. Without this, a prover that
        // produced sigs in submission order would emit different bytes than one that
        // produced them in completion order — same signatures, different aggregate hash.
        var ordered = sigs
            .Where(s => s is not null)
            .OrderBy(s => s.PublicKey)
            .ToArray();

        var payload = new MultisigProofPayload { Signatures = ordered };
        var combinedRoot = HashPair(left.MessageRootContribution, right.MessageRootContribution);

        return new RoundResult
        {
            MessageRootContribution = combinedRoot,
            ProofBytes = payload.Encode(),
        };
    }

    /// <summary>
    /// Verify that <paramref name="result"/>'s proof bytes contain at least <paramref name="threshold"/>
    /// valid signatures by <paramref name="validators"/> over the canonical encoding of
    /// <paramref name="left"/> + <paramref name="right"/>.
    /// </summary>
    public static bool VerifyRound(
        RoundResult result,
        RoundResult left,
        RoundResult right,
        IReadOnlyList<ECPoint> validators,
        int threshold)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(validators);
        if (threshold < 1) return false;

        // Re-derive the canonical message + the expected combined root.
        var canonical = CanonicalRoundMessage(left, right);
        var expectedRoot = HashPair(left.MessageRootContribution, right.MessageRootContribution);
        if (result.MessageRootContribution != expectedRoot) return false;

        MultisigProofPayload payload;
        try { payload = MultisigProofPayload.Decode(result.ProofBytes.Span); }
        catch (InvalidDataException) { return false; }
        catch (ArgumentException) { return false; }

        var validatorSet = new HashSet<ECPoint>(validators);
        var validCount = 0;
        var seen = new HashSet<ECPoint>();
        foreach (var sig in payload.Signatures)
        {
            if (!validatorSet.Contains(sig.PublicKey)) continue; // not a known validator
            if (!seen.Add(sig.PublicKey)) continue; // duplicate signer counts once
            if (sig.Signature.Length != 64) continue;
            try
            {
                if (Crypto.VerifySignature(canonical, sig.Signature.Span, sig.PublicKey))
                    validCount++;
            }
            catch (ArgumentException)
            {
                // Malformed pubkey or signature bytes — treat as invalid signature.
                continue;
            }
            catch (FormatException)
            {
                // Malformed ECPoint encoding — treat as invalid signature.
                continue;
            }
        }
        return validCount >= threshold;
    }

    /// <summary>
    /// Canonical round message = <c>[domainTag][backendId][leftRoot][leftProofLen LE32][leftProofBytes][rightRoot][rightProofLen LE32][rightProofBytes]</c>.
    /// Length-prefixing the proof byte segments ensures the message has a unique parse — a
    /// prover that omitted lengths would expose the canonical encoding to length-extension /
    /// concatenation ambiguity attacks.
    /// </summary>
    public static byte[] CanonicalRoundMessage(RoundResult left, RoundResult right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var leftProofLen = left.ProofBytes.Length;
        var rightProofLen = right.ProofBytes.Length;
        var size = checked(1 + 1 + 32 + 4 + leftProofLen + 32 + 4 + rightProofLen);
        var buf = new byte[size];
        var span = buf.AsSpan();
        span[0] = DomainTag;
        span[1] = ConstBackendId;
        var pos = 2;
        left.MessageRootContribution.GetSpan().CopyTo(span.Slice(pos, 32));
        pos += 32;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), leftProofLen);
        pos += 4;
        left.ProofBytes.Span.CopyTo(span.Slice(pos, leftProofLen));
        pos += leftProofLen;
        right.MessageRootContribution.GetSpan().CopyTo(span.Slice(pos, 32));
        pos += 32;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), rightProofLen);
        pos += 4;
        right.ProofBytes.Span.CopyTo(span.Slice(pos, rightProofLen));
        pos += rightProofLen;
        if (pos != size) throw new InvalidOperationException("CanonicalRoundMessage size mismatch");
        return buf;
    }

    private static UInt256 HashPair(UInt256 left, UInt256 right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Span<byte> buf = stackalloc byte[64];
        left.GetSpan().CopyTo(buf);
        right.GetSpan().CopyTo(buf[32..]);
        return new UInt256(Crypto.Hash256(buf));
    }
}
