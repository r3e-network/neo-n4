using Neo.Cryptography;

namespace Neo.L2.Challenge.UnitTests;

/// <summary>
/// Tests for <see cref="V3StorageProofVerifier"/> — the off-chain reference verifier
/// that demonstrates the algorithm a future on-chain re-execution-capable fraud verifier
/// would mirror. Builds small Merkle trees by hand, constructs storage proofs against
/// them, and exercises both the happy path and each rejection path.
/// </summary>
[TestClass]
public class UT_V3StorageProofVerifier
{
    private static UInt256 H(byte b)
    {
        var bytes = new byte[32];
        bytes[0] = b;
        return new UInt256(bytes);
    }

    private static UInt256 Hash256Concat(UInt256 left, UInt256 right)
    {
        var combined = new byte[64];
        left.GetSpan().CopyTo(combined.AsSpan(0, 32));
        right.GetSpan().CopyTo(combined.AsSpan(32, 32));
        return new UInt256(Crypto.Hash256(combined));
    }

    /// <summary>
    /// Build a 2-leaf Merkle tree from (key, value) pairs and return:
    ///   - The Merkle root
    ///   - Each leaf's StorageProof-style sibling list (length 1: just the other leaf)
    /// </summary>
    private static (UInt256 root, UInt256[] sibA, UInt256[] sibB) Build2LeafTree(
        ReadOnlySpan<byte> keyA, ReadOnlySpan<byte> valA,
        ReadOnlySpan<byte> keyB, ReadOnlySpan<byte> valB)
    {
        var leafA = V3StorageProofVerifier.HashEntry(keyA, valA);
        var leafB = V3StorageProofVerifier.HashEntry(keyB, valB);
        var root = Hash256Concat(leafA, leafB);
        return (root, new[] { leafB }, new[] { leafA });
    }

    [TestMethod]
    public void Verify_NotV3_ReturnsNotV3()
    {
        // v1 payload — no storage proofs.
        var p = new FraudProofPayload
        {
            PreStateRoot = H(1),
            ClaimedPostStateRoot = H(2),
            ReplayedPostStateRoot = H(3),
            DisputedTxIndex = 0,
        };
        Assert.AreEqual(V3StorageProofVerifier.Verdict.NotV3, V3StorageProofVerifier.Verify(p));
    }

    [TestMethod]
    public void Verify_NoDiscrepancy_Rejected()
    {
        // v3 payload with claimed == replayed → NoDiscrepancy. This catches a degenerate
        // challenger who submits a payload with no actual fraud claim.
        var sameRoot = H(0xAA);
        var p = new FraudProofPayload
        {
            PreStateRoot = H(1),
            ClaimedPostStateRoot = sameRoot,
            ReplayedPostStateRoot = sameRoot,
            DisputedTxIndex = 0,
            StorageProofs = new[] { Sample() },
        };
        Assert.AreEqual(V3StorageProofVerifier.Verdict.NoDiscrepancy, V3StorageProofVerifier.Verify(p));
    }

    [TestMethod]
    public void Verify_HappyPath_2LeafTree_Verified()
    {
        // 2-leaf tree:
        //   - keyA="aa", preValue="00" → leafA pre  ; keyA="aa", postValue="11" → leafA post
        //   - keyB="bb", value="22" (unchanged in pre + post) → leafB
        var keyA = new byte[] { 0xAA };
        var preValA = new byte[] { 0x00 };
        var postValA = new byte[] { 0x11 };
        var keyB = new byte[] { 0xBB };
        var valB = new byte[] { 0x22 };

        var (preRoot, sibA_pre, _) = Build2LeafTree(keyA, preValA, keyB, valB);
        var (postRoot, sibA_post, _) = Build2LeafTree(keyA, postValA, keyB, valB);

        // The challenger claims that running the disputed tx on preRoot produces postRoot.
        // The sequencer claimed something different.
        var p = new FraudProofPayload
        {
            PreStateRoot = preRoot,
            ClaimedPostStateRoot = H(0xDE),  // sequencer's wrong claim — anything != postRoot
            ReplayedPostStateRoot = postRoot,
            DisputedTxIndex = 0,
            StorageProofs = new[]
            {
                new StorageProof
                {
                    Key = keyA,
                    PreValue = preValA,
                    PostValue = postValA,
                    LeafIndex = 0,  // keyA is at position 0 in the tree
                    PreSiblings = sibA_pre,
                    PostSiblings = sibA_post,
                },
            },
        };
        Assert.AreEqual(V3StorageProofVerifier.Verdict.Verified, V3StorageProofVerifier.Verify(p));
    }

    [TestMethod]
    public void Verify_PreStateRootMismatch_Rejected()
    {
        // Build a tree, but submit a payload with a wrong PreStateRoot. The pre-derived
        // root from the proof won't match → PreStateRootMismatch.
        var keyA = new byte[] { 0xAA };
        var preValA = new byte[] { 0x00 };
        var postValA = new byte[] { 0x11 };
        var keyB = new byte[] { 0xBB };
        var valB = new byte[] { 0x22 };

        var (preRoot, sibA_pre, _) = Build2LeafTree(keyA, preValA, keyB, valB);
        var (postRoot, sibA_post, _) = Build2LeafTree(keyA, postValA, keyB, valB);

        var p = new FraudProofPayload
        {
            PreStateRoot = H(0xCC),  // ← wrong, doesn't match preRoot
            ClaimedPostStateRoot = H(0xDE),
            ReplayedPostStateRoot = postRoot,
            DisputedTxIndex = 0,
            StorageProofs = new[]
            {
                new StorageProof
                {
                    Key = keyA, PreValue = preValA, PostValue = postValA,
                    LeafIndex = 0, PreSiblings = sibA_pre, PostSiblings = sibA_post,
                },
            },
        };
        Assert.AreEqual(V3StorageProofVerifier.Verdict.PreStateRootMismatch, V3StorageProofVerifier.Verify(p));
    }

    [TestMethod]
    public void Verify_ReplayedPostStateRootMismatch_Rejected()
    {
        // Build a tree, submit a payload with a wrong ReplayedPostStateRoot. The
        // post-derived root won't match → ReplayedPostStateRootMismatch.
        var keyA = new byte[] { 0xAA };
        var preValA = new byte[] { 0x00 };
        var postValA = new byte[] { 0x11 };
        var keyB = new byte[] { 0xBB };
        var valB = new byte[] { 0x22 };

        var (preRoot, sibA_pre, _) = Build2LeafTree(keyA, preValA, keyB, valB);
        var (_, sibA_post, _) = Build2LeafTree(keyA, postValA, keyB, valB);

        var p = new FraudProofPayload
        {
            PreStateRoot = preRoot,
            ClaimedPostStateRoot = H(0xDE),
            ReplayedPostStateRoot = H(0xEF),  // ← wrong, doesn't match the actual postRoot
            DisputedTxIndex = 0,
            StorageProofs = new[]
            {
                new StorageProof
                {
                    Key = keyA, PreValue = preValA, PostValue = postValA,
                    LeafIndex = 0, PreSiblings = sibA_pre, PostSiblings = sibA_post,
                },
            },
        };
        Assert.AreEqual(V3StorageProofVerifier.Verdict.ReplayedPostStateRootMismatch, V3StorageProofVerifier.Verify(p));
    }

    [TestMethod]
    public void Verify_RoundTripsThroughFraudProofPayloadEncodeDecode()
    {
        // Round-trip the v3 payload bytes through encode/decode and verify the decoded
        // payload still produces Verified. Pin so a future encoder/decoder change can't
        // silently break the cross-component contract.
        var keyA = new byte[] { 0xAA };
        var preValA = new byte[] { 0x00 };
        var postValA = new byte[] { 0x11 };
        var keyB = new byte[] { 0xBB };
        var valB = new byte[] { 0x22 };

        var (preRoot, sibA_pre, _) = Build2LeafTree(keyA, preValA, keyB, valB);
        var (postRoot, sibA_post, _) = Build2LeafTree(keyA, postValA, keyB, valB);

        var p = new FraudProofPayload
        {
            PreStateRoot = preRoot,
            ClaimedPostStateRoot = H(0xDE),
            ReplayedPostStateRoot = postRoot,
            DisputedTxIndex = 0,
            StorageProofs = new[]
            {
                new StorageProof
                {
                    Key = keyA, PreValue = preValA, PostValue = postValA,
                    LeafIndex = 0, PreSiblings = sibA_pre, PostSiblings = sibA_post,
                },
            },
        };
        var bytes = p.Encode();
        var decoded = FraudProofPayload.Decode(bytes);
        Assert.AreEqual(V3StorageProofVerifier.Verdict.Verified, V3StorageProofVerifier.Verify(decoded));
    }

    [TestMethod]
    public void HashEntry_LayoutMatches_KeyedStateStore()
    {
        // Pin that the leaf hash composition matches Neo.L2.Executor.State.KeyedStateStore.
        // The two MUST stay in lockstep — if KeyedStateStore changes the format, this
        // verifier's pre/post leaves would no longer match what the L2 actually stored,
        // and every v3 storage proof would fail to verify.
        var key = new byte[] { 0x01, 0x02, 0x03 };
        var value = new byte[] { 0xAA, 0xBB };

        var verifierHash = V3StorageProofVerifier.HashEntry(key, value);
        var keyedStateHash = Neo.L2.Executor.State.KeyedStateStore.HashEntry(key, value);
        Assert.AreEqual(keyedStateHash, verifierHash,
            "V3StorageProofVerifier.HashEntry must stay in lockstep with KeyedStateStore.HashEntry");
    }

    private static StorageProof Sample()
    {
        return new StorageProof
        {
            Key = new byte[] { 0xAA },
            PreValue = new byte[] { 0x00 },
            PostValue = new byte[] { 0x11 },
            LeafIndex = 0,
            PreSiblings = new[] { H(1) },
            PostSiblings = new[] { H(1) },
        };
    }
}
