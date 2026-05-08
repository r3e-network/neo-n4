using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Neo;

namespace Neo.L2.Bridge.Cli.Commands;

/// <summary>
/// Decodes the canonical Merkle-proof byte format emitted by
/// <c>Neo.L2.State.MerkleProofSerializer</c> + served via the
/// <c>getl2withdrawalproof</c> RPC method into the per-level sibling list the
/// <c>SharedBridge.FinalizeWithdrawalWithProof</c> contract expects.
/// </summary>
/// <remarks>
/// Wire layout (matches <c>Neo.L2.State.MerkleProofSerializer.Encode</c>):
/// <list type="bullet">
///   <item><c>[32B leaf]</c></item>
///   <item><c>[4B leafIndex LE]</c></item>
///   <item><c>[8B pathBitmap LE]</c></item>
///   <item><c>[4B siblingCount LE]</c></item>
///   <item><c>[32B*N siblings]</c> in leaf-to-root order (siblings[0] pairs with the leaf)</item>
/// </list>
/// Total size = 48 + 32×N bytes. Sibling order is leaf-to-root because the on-chain
/// <c>SettlementManager.VerifyWithdrawalLeafWithProof</c> consumes them in that order
/// (walks <c>leafIndex</c>'s bit at each level low-to-high).
/// </remarks>
internal static class MerkleProofDecoder
{
    /// <summary>Header byte size before the sibling array (32 + 4 + 8 + 4 = 48).</summary>
    public const int HeaderSize = 32 + 4 + 8 + 4;

    /// <summary>Decode the proof bytes into the canonical sibling list (leaf-to-root order).</summary>
    public static IReadOnlyList<UInt256> Decode(ReadOnlyMemory<byte> proofBytes)
    {
        var span = proofBytes.Span;
        if (span.Length < HeaderSize)
            throw new InvalidDataException($"merkle proof bytes too short ({span.Length} < {HeaderSize})");
        var siblingCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(44, 4));
        var expected = HeaderSize + siblingCount * 32;
        if (span.Length != expected)
            throw new InvalidDataException(
                $"merkle proof byte size {span.Length} != expected {expected} for {siblingCount} siblings");

        var siblings = new UInt256[siblingCount];
        for (var i = 0; i < siblingCount; i++)
        {
            siblings[i] = new UInt256(span.Slice(HeaderSize + i * 32, 32).ToArray());
        }
        return siblings;
    }
}
