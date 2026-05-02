using Neo.Cryptography;

namespace Neo.Plugins.L2Gateway;

/// <summary>
/// Default <see cref="IRoundProver"/>. Combines children by:
/// <list type="bullet">
///   <item><description>message root: <c>Hash256(left.MessageRoot || right.MessageRoot)</c> — same convention as the binary Merkle tree.</description></item>
///   <item><description>proof bytes: <c>[4B leftLen][leftBytes][4B rightLen][rightBytes]</c>.</description></item>
/// </list>
/// When <c>right</c> is null the left child is promoted unchanged (Merkle odd-leaf rule).
/// </summary>
public sealed class PassThroughRoundProver : IRoundProver
{
    /// <summary>Constant backend id matching <see cref="PassThroughAggregator.BackendId"/>.</summary>
    public const byte ConstBackendId = 0xFE;

    /// <inheritdoc />
    public byte BackendId => ConstBackendId;

    /// <inheritdoc />
    public RoundResult Combine(RoundResult left, RoundResult? right)
    {
        ArgumentNullException.ThrowIfNull(left);

        if (right is null) return left;

        Span<byte> rootBuf = stackalloc byte[64];
        left.MessageRootContribution.GetSpan().CopyTo(rootBuf);
        right.MessageRootContribution.GetSpan().CopyTo(rootBuf[32..]);
        var combinedRoot = new UInt256(Crypto.Hash256(rootBuf));

        var lp = left.ProofBytes;
        var rp = right.ProofBytes;
        // Checked arithmetic: at log(N) tree depth with proofs doubling per level, the
        // total size could in theory exceed int.MaxValue for a pathological combination.
        // Without `checked`, the wrap would surface as a confusing OverflowException
        // from `new byte[neg]` rather than naming the offending sum.
        var combinedSize = checked(4 + lp.Length + 4 + rp.Length);
        var combinedProof = new byte[combinedSize];
        var span = combinedProof.AsSpan();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), lp.Length);
        lp.Span.CopyTo(span.Slice(4));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4 + lp.Length, 4), rp.Length);
        rp.Span.CopyTo(span.Slice(8 + lp.Length));

        return new RoundResult
        {
            MessageRootContribution = combinedRoot,
            ProofBytes = combinedProof,
        };
    }
}
