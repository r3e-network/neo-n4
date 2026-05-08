using System;
using Neo;
using Neo.L2;
using Neo.L2.Batch;

namespace Neo.L2.Explore.UnitTests;

/// <summary>
/// Test helper: builds <see cref="L2BatchCommitment"/> instances with
/// state-root continuity by default — each batch's preStateRoot equals the
/// previous batch's postStateRoot. Tests that want to inject a discontinuity
/// override the preStateRoot explicitly.
/// </summary>
internal static class BatchFactory
{
    /// <summary>Create a continuous batch from <paramref name="prev"/>.</summary>
    public static L2BatchCommitment Continuous(uint chainId, ulong batchNumber, L2BatchCommitment? prev = null)
    {
        var pre = prev?.PostStateRoot ?? UInt256.Zero;
        var post = MakeRoot(batchNumber);
        return Build(chainId, batchNumber, pre, post);
    }

    /// <summary>Create a batch with explicit preStateRoot — used to inject discontinuities.</summary>
    public static L2BatchCommitment WithExplicitPre(uint chainId, ulong batchNumber, UInt256 preStateRoot)
    {
        var post = MakeRoot(batchNumber);
        return Build(chainId, batchNumber, preStateRoot, post);
    }

    private static L2BatchCommitment Build(uint chainId, ulong batchNumber, UInt256 pre, UInt256 post)
    {
        return new L2BatchCommitment
        {
            ChainId = chainId,
            BatchNumber = batchNumber,
            FirstBlock = batchNumber * 100,
            LastBlock = batchNumber * 100 + 99,
            PreStateRoot = pre,
            PostStateRoot = post,
            TxRoot = MakeRoot(batchNumber + 1000),
            ReceiptRoot = MakeRoot(batchNumber + 2000),
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = MakeRoot(batchNumber + 3000),
            PublicInputHash = MakeRoot(batchNumber + 4000),
            ProofType = ProofType.Multisig,
            Proof = new byte[] { 0x01, 0x02 },
        };
    }

    private static UInt256 MakeRoot(ulong seed)
    {
        // Deterministic per-seed UInt256 — the test reads back what it wrote.
        var bytes = new byte[32];
        BitConverter.GetBytes(seed).CopyTo(bytes, 0);
        return new UInt256(bytes);
    }
}
