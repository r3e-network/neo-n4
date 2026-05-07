using System.Buffers.Binary;
using Neo.Cryptography;

namespace Neo.L2.Challenge.UnitTests;

/// <summary>
/// Parity tests for <c>NeoHub.RestrictedExecutionFraudVerifier</c>'s on-chain v3
/// verification logic. We replicate the contract's accept/reject decisions in C# and
/// run identical v3 fraud-proof payloads through both — drift between the on-chain
/// algorithm and the canonical wire format / off-chain
/// <see cref="V3StorageProofVerifier"/> surfaces here rather than at L1 contract
/// execution time.
/// </summary>
/// <remarks>
/// Mirrors the contract at
/// <c>contracts/NeoHub.RestrictedExecutionFraudVerifier/RestrictedExecutionFraudVerifierContract.cs</c>.
/// The on-chain decision tree (from VerifyFraud):
/// <code>
/// 1. version != 3 → ReasonBadVersion
/// 2. payload.Length &lt; V2HeaderSize → ReasonBadLength
/// 3. declaredTxLen > MaxDisputedTxBytes → ReasonOversizedWitness
/// 4. claimed == replayed → ReasonNoDiscrepancy
/// 5. numProofs == 0 || > MaxStorageProofsPerPayload → ReasonProofCountInvalid
/// 6. for each proof:
///    a. cap violations → ReasonInvalidStorageProof
///    b. truncation → ReasonBadLength
///    c. pre-derived root != PreStateRoot → ReasonPreStateRootMismatch
///    d. post-derived root != ReplayedPostStateRoot → ReasonReplayedPostStateRootMismatch
/// 7. trailing bytes → ReasonBadLength
/// 8. else → ReasonAccepted
/// </code>
/// </remarks>
[TestClass]
public class UT_RestrictedExecutionFraudVerifierParity
{
    // Mirror the on-chain contract's reason codes.
    private const byte ReasonAccepted = 0;
    private const byte ReasonBadLength = 1;
    private const byte ReasonBadVersion = 2;
    private const byte ReasonNoDiscrepancy = 3;
    private const byte ReasonOversizedWitness = 4;
    private const byte ReasonInvalidStorageProof = 5;
    private const byte ReasonProofCountInvalid = 6;
    private const byte ReasonPreStateRootMismatch = 7;
    private const byte ReasonReplayedPostStateRootMismatch = 8;

    private const byte SupportedVersion3 = 3;
    private const int V1HeaderSize = 101;
    private const int V2HeaderSize = 105;
    private const int MaxDisputedTxBytes = 64 * 1024;
    private const int MaxStorageProofsPerPayload = 32;
    private const int MaxKeyBytes = 256;
    private const int MaxValueBytes = 4096;
    private const int MaxSiblingDepth = 64;

    /// <summary>
    /// C# replica of <c>RestrictedExecutionFraudVerifierContract.VerifyFraud</c>'s
    /// decision tree. Returns the on-chain reason byte (0 for accept, 1..8 for the
    /// rejection paths). Mirrors the contract's wire-format parsing + Merkle fold
    /// 1:1.
    /// </summary>
    private static byte SimulateVerify(byte[] payload)
    {
        if (payload.Length < 1) return ReasonBadLength;
        if (payload[0] != SupportedVersion3) return ReasonBadVersion;
        if (payload.Length < V2HeaderSize) return ReasonBadLength;

        var declaredTxLen = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(101, 4));
        if (declaredTxLen > MaxDisputedTxBytes) return ReasonOversizedWitness;
        var pos = (int)(V2HeaderSize + declaredTxLen);
        if (payload.Length < pos + 4) return ReasonBadLength;

        // Discrepancy claim: claimed (33..64) != replayed (65..96).
        var sameRoots = true;
        for (var i = 0; i < 32; i++) if (payload[33 + i] != payload[65 + i]) { sameRoots = false; break; }
        if (sameRoots) return ReasonNoDiscrepancy;

        var numProofs = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(pos, 4));
        pos += 4;
        if (numProofs == 0 || numProofs > MaxStorageProofsPerPayload) return ReasonProofCountInvalid;

        for (var i = 0u; i < numProofs; i++)
        {
            if (payload.Length < pos + 2) return ReasonBadLength;
            var keyLen = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(pos, 2)); pos += 2;
            if (keyLen > MaxKeyBytes) return ReasonInvalidStorageProof;
            if (payload.Length < pos + keyLen) return ReasonBadLength;
            var keyOffset = pos; pos += keyLen;

            if (payload.Length < pos + 4) return ReasonBadLength;
            var preLen = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(pos, 4)); pos += 4;
            if (preLen > MaxValueBytes) return ReasonInvalidStorageProof;
            if (payload.Length < pos + (int)preLen) return ReasonBadLength;
            var preOffset = pos; pos += (int)preLen;

            if (payload.Length < pos + 4) return ReasonBadLength;
            var postLen = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(pos, 4)); pos += 4;
            if (postLen > MaxValueBytes) return ReasonInvalidStorageProof;
            if (payload.Length < pos + (int)postLen) return ReasonBadLength;
            var postOffset = pos; pos += (int)postLen;

            if (payload.Length < pos + 8) return ReasonBadLength;
            var leafIndex = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(pos, 8)); pos += 8;

            if (payload.Length < pos + 1) return ReasonBadLength;
            var preSibCount = (int)payload[pos]; pos += 1;
            if (preSibCount > MaxSiblingDepth) return ReasonInvalidStorageProof;
            if (payload.Length < pos + 32 * preSibCount) return ReasonBadLength;
            var preSibOffset = pos; pos += 32 * preSibCount;

            if (payload.Length < pos + 1) return ReasonBadLength;
            var postSibCount = (int)payload[pos]; pos += 1;
            if (postSibCount > MaxSiblingDepth) return ReasonInvalidStorageProof;
            if (payload.Length < pos + 32 * postSibCount) return ReasonBadLength;
            var postSibOffset = pos; pos += 32 * postSibCount;

            // Re-derive pre-root + match.
            var preLeaf = HashEntry(payload, keyOffset, keyLen, preOffset, (int)preLen);
            var preRoot = FoldMerkleProof(preLeaf, payload, preSibOffset, preSibCount, leafIndex);
            for (var j = 0; j < 32; j++)
                if (preRoot[j] != payload[1 + j]) return ReasonPreStateRootMismatch;

            // Re-derive post-root + match.
            var postLeaf = HashEntry(payload, keyOffset, keyLen, postOffset, (int)postLen);
            var postRoot = FoldMerkleProof(postLeaf, payload, postSibOffset, postSibCount, leafIndex);
            for (var j = 0; j < 32; j++)
                if (postRoot[j] != payload[65 + j]) return ReasonReplayedPostStateRootMismatch;
        }

        if (pos != payload.Length) return ReasonBadLength;
        return ReasonAccepted;
    }

    private static byte[] HashEntry(byte[] payload, int keyOffset, int keyLen, int valOffset, int valLen)
    {
        var buf = new byte[4 + keyLen + 4 + valLen];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), keyLen);
        Buffer.BlockCopy(payload, keyOffset, buf, 4, keyLen);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4 + keyLen, 4), valLen);
        Buffer.BlockCopy(payload, valOffset, buf, 4 + keyLen + 4, valLen);
        return Crypto.Hash256(buf);
    }

    private static byte[] FoldMerkleProof(byte[] leafHash, byte[] payload, int sibOffset, int sibCount, ulong leafIndex)
    {
        var current = leafHash;
        var index = leafIndex;
        for (var i = 0; i < sibCount; i++)
        {
            var combined = new byte[64];
            var sibBase = sibOffset + 32 * i;
            if ((index & 1UL) == 0UL)
            {
                Buffer.BlockCopy(current, 0, combined, 0, 32);
                Buffer.BlockCopy(payload, sibBase, combined, 32, 32);
            }
            else
            {
                Buffer.BlockCopy(payload, sibBase, combined, 0, 32);
                Buffer.BlockCopy(current, 0, combined, 32, 32);
            }
            current = Crypto.Hash256(combined);
            index >>= 1;
        }
        return current;
    }

    private static UInt256 Hash256Concat(UInt256 left, UInt256 right)
    {
        var combined = new byte[64];
        left.GetSpan().CopyTo(combined.AsSpan(0, 32));
        right.GetSpan().CopyTo(combined.AsSpan(32, 32));
        return new UInt256(Crypto.Hash256(combined));
    }

    private static UInt256 H(byte b)
    {
        var bytes = new byte[32];
        bytes[0] = b;
        return new UInt256(bytes);
    }

    /// <summary>
    /// Build a 2-leaf Merkle tree from (keyA, valA) + (keyB, valB) and return the root +
    /// each leaf's StorageProof-style sibling list. Leaf hashes use the
    /// <c>KeyedStateStore.HashEntry</c> format
    /// (<c>Hash256(int32LE(keyLen)||key||int32LE(valueLen)||value)</c>).
    /// </summary>
    private static (UInt256 root, UInt256[] sibA, UInt256[] sibB) Build2LeafTree(
        byte[] keyA, byte[] valA, byte[] keyB, byte[] valB)
    {
        var leafA = V3StorageProofVerifier.HashEntry(keyA, valA);
        var leafB = V3StorageProofVerifier.HashEntry(keyB, valB);
        var root = Hash256Concat(leafA, leafB);
        return (root, new[] { leafB }, new[] { leafA });
    }

    [TestMethod]
    public void Verify_HappyPath_2LeafTree_Accepted()
    {
        // 2-leaf tree, keyA at index 0 with pre/post values diverging.
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
            ClaimedPostStateRoot = H(0xDE),  // sequencer's wrong claim
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
        Assert.AreEqual(SupportedVersion3, bytes[0]);
        Assert.AreEqual(ReasonAccepted, SimulateVerify(bytes),
            "well-formed v3 with reconstructing storage proofs + claimed!=replayed must accept");
    }

    [TestMethod]
    public void Verify_NotV3_Rejected()
    {
        // v1 payload (no storage proofs) → reject with BadVersion (this verifier is v3-only;
        // GovernanceFraudVerifier handles v1/v2).
        var p = new FraudProofPayload
        {
            PreStateRoot = H(1), ClaimedPostStateRoot = H(2), ReplayedPostStateRoot = H(3),
            DisputedTxIndex = 0,
        };
        var bytes = p.Encode();
        Assert.AreEqual((byte)1, bytes[0]);  // v1
        Assert.AreEqual(ReasonBadVersion, SimulateVerify(bytes));
    }

    [TestMethod]
    public void Verify_NoDiscrepancy_Rejected()
    {
        // v3 with claimed == replayed → reject NoDiscrepancy (no real fraud claim).
        // Same root so the discrepancy check fires before the per-proof verification.
        var keyA = new byte[] { 0xAA };
        var valA = new byte[] { 0x00 };
        var keyB = new byte[] { 0xBB };
        var valB = new byte[] { 0x22 };

        var (preRoot, sibA_pre, _) = Build2LeafTree(keyA, valA, keyB, valB);
        var sameRoot = H(0xCC);

        var p = new FraudProofPayload
        {
            PreStateRoot = preRoot,
            ClaimedPostStateRoot = sameRoot,
            ReplayedPostStateRoot = sameRoot,
            DisputedTxIndex = 0,
            StorageProofs = new[]
            {
                new StorageProof
                {
                    Key = keyA, PreValue = valA, PostValue = valA,
                    LeafIndex = 0, PreSiblings = sibA_pre, PostSiblings = sibA_pre,
                },
            },
        };
        Assert.AreEqual(ReasonNoDiscrepancy, SimulateVerify(p.Encode()));
    }

    [TestMethod]
    public void Verify_PreStateRootMismatch_Rejected()
    {
        // Build a real tree but plug in a wrong PreStateRoot at the header — the
        // pre-derived root from the proof's siblings won't match.
        var keyA = new byte[] { 0xAA };
        var preValA = new byte[] { 0x00 };
        var postValA = new byte[] { 0x11 };
        var keyB = new byte[] { 0xBB };
        var valB = new byte[] { 0x22 };

        var (preRoot, sibA_pre, _) = Build2LeafTree(keyA, preValA, keyB, valB);
        var (postRoot, sibA_post, _) = Build2LeafTree(keyA, postValA, keyB, valB);

        var p = new FraudProofPayload
        {
            PreStateRoot = H(0xCC),  // ← wrong
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
        Assert.AreEqual(ReasonPreStateRootMismatch, SimulateVerify(p.Encode()));
    }

    [TestMethod]
    public void Verify_ReplayedPostStateRootMismatch_Rejected()
    {
        // Build a real tree but plug in a wrong ReplayedPostStateRoot at the header.
        // The pre side reconstructs cleanly; the post side mismatches → that specific
        // reject reason fires (ordering pin: pre check happens before post check).
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
            ReplayedPostStateRoot = H(0xEF),  // ← wrong
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
        Assert.AreEqual(ReasonReplayedPostStateRootMismatch, SimulateVerify(p.Encode()));
    }

    [TestMethod]
    public void Verify_BadVersion_v2Rejected()
    {
        // v2 payload (with witness, no storage proofs) → reject BadVersion. v2 is
        // the GovernanceFraudVerifier's job; this verifier only handles v3.
        var p = new FraudProofPayload
        {
            PreStateRoot = H(1), ClaimedPostStateRoot = H(2), ReplayedPostStateRoot = H(3),
            DisputedTxIndex = 0,
            DisputedTxBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
        };
        var bytes = p.Encode();
        Assert.AreEqual((byte)2, bytes[0]);  // v2
        Assert.AreEqual(ReasonBadVersion, SimulateVerify(bytes));
    }

    [TestMethod]
    public void Verify_BadLength_TruncatedBelowV2Header_Rejected()
    {
        // 50 bytes with version=3 → too short to even contain v1+witness-len header.
        var bytes = new byte[50];
        bytes[0] = SupportedVersion3;
        Assert.AreEqual(ReasonBadLength, SimulateVerify(bytes));
    }

    [TestMethod]
    public void Verify_BadLength_TruncatedNumProofs_Rejected()
    {
        // V2HeaderSize buffer with version=3 + zero declaredTxLen → after V2HeaderSize
        // there's no room for the 4-byte numProofs prefix → BadLength.
        var bytes = new byte[V2HeaderSize];
        bytes[0] = SupportedVersion3;
        // Make claimed != replayed (offsets 33..96) so the discrepancy check passes
        // before we hit the numProofs length check. (The contract reads numProofs
        // length BEFORE the discrepancy check actually — let's verify both orderings
        // work.)
        bytes[33] = 0xAA;
        bytes[65] = 0xBB;
        Assert.AreEqual(ReasonBadLength, SimulateVerify(bytes));
    }

    [TestMethod]
    public void Verify_OversizedWitness_Rejected()
    {
        // Construct a v3 buffer with declaredTxLen > MaxDisputedTxBytes → reject before
        // length-match check.
        var bytes = new byte[V2HeaderSize + 10];
        bytes[0] = SupportedVersion3;
        var oversized = (uint)MaxDisputedTxBytes + 1;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(101, 4), oversized);
        Assert.AreEqual(ReasonOversizedWitness, SimulateVerify(bytes));
    }

    [TestMethod]
    public void Verify_ZeroProofs_Rejected()
    {
        // Manually construct a v3 buffer with declaredTxLen=0 + numProofs=0. The
        // off-chain encoder rejects this (uses v2 instead), but the on-chain decoder
        // must defensively reject too in case someone hand-rolls the bytes.
        var bytes = new byte[V2HeaderSize + 4];
        bytes[0] = SupportedVersion3;
        bytes[33] = 0xAA;  // claimed
        bytes[65] = 0xBB;  // replayed
        // declaredTxLen = 0 (101..104 zero-init)
        // numProofs at offset 105 = 0
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(105, 4), 0u);
        Assert.AreEqual(ReasonProofCountInvalid, SimulateVerify(bytes));
    }

    [TestMethod]
    public void Verify_TooManyProofs_Rejected()
    {
        // numProofs = MaxStorageProofsPerPayload + 1 → reject ProofCountInvalid before
        // the per-proof loop runs (so we don't even need real proof bytes after it).
        var bytes = new byte[V2HeaderSize + 4];
        bytes[0] = SupportedVersion3;
        bytes[33] = 0xAA; bytes[65] = 0xBB;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(105, 4), (uint)(MaxStorageProofsPerPayload + 1));
        Assert.AreEqual(ReasonProofCountInvalid, SimulateVerify(bytes));
    }

    [TestMethod]
    public void Verify_DecisionTreeOrder_VersionBeforeAll()
    {
        // Wrong version + every other thing wrong → BadVersion fires first. Pin so a
        // refactor that reorders checks doesn't change the operator-facing reject
        // reason for malformed inputs.
        var bytes = new byte[10];  // way under V2HeaderSize
        bytes[0] = 99;
        Assert.AreEqual(ReasonBadVersion, SimulateVerify(bytes));
    }

    [TestMethod]
    public void Verify_DecisionTreeOrder_DiscrepancyBeforeProofVerify()
    {
        // claimed == replayed + valid storage proof → NoDiscrepancy fires before the
        // per-proof Merkle re-derivation runs. Saves a lot of compute on degenerate
        // submissions on-chain.
        var keyA = new byte[] { 0xAA };
        var valA = new byte[] { 0x00 };
        var keyB = new byte[] { 0xBB };
        var valB = new byte[] { 0x22 };

        var (preRoot, sibA, _) = Build2LeafTree(keyA, valA, keyB, valB);
        var sameRoot = H(0xCC);  // != preRoot, but claimed == replayed wins

        var p = new FraudProofPayload
        {
            PreStateRoot = preRoot,
            ClaimedPostStateRoot = sameRoot,
            ReplayedPostStateRoot = sameRoot,
            DisputedTxIndex = 0,
            StorageProofs = new[]
            {
                new StorageProof
                {
                    Key = keyA, PreValue = valA, PostValue = valA,
                    LeafIndex = 0, PreSiblings = sibA, PostSiblings = sibA,
                },
            },
        };
        Assert.AreEqual(ReasonNoDiscrepancy, SimulateVerify(p.Encode()));
    }

    [TestMethod]
    public void Verify_LayoutOffsets_PreStateRootAt1_ReplayedAt65()
    {
        // The contract reads PreStateRoot at offset [1..32] and ReplayedPostStateRoot
        // at [65..96] when re-deriving. Pin that the encoder writes those fields at
        // the same offsets — same risk class as
        // UT_GovernanceFraudVerifierParity.Verify_LayoutOffsets_Match_FraudProofPayloadEncoder.
        var keyA = new byte[] { 0xAA };
        var preValA = new byte[] { 0x00 };
        var postValA = new byte[] { 0x11 };
        var keyB = new byte[] { 0xBB };
        var valB = new byte[] { 0x22 };

        var (preRoot, sibA_pre, _) = Build2LeafTree(keyA, preValA, keyB, valB);
        var (postRoot, sibA_post, _) = Build2LeafTree(keyA, postValA, keyB, valB);

        var p = new FraudProofPayload
        {
            PreStateRoot = H(0xAA),  // distinct sentinel for offset check
            ClaimedPostStateRoot = H(0xCC),
            ReplayedPostStateRoot = H(0xBB),  // distinct sentinel for offset check
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

        // Offset [1]: PreStateRoot's first byte must be 0xAA (H(0xAA) sets bytes[0]=0xAA).
        Assert.AreEqual(0xAA, bytes[1], "PreStateRoot[0] must be at offset 1 (per v1 header layout)");
        // Offset [65]: ReplayedPostStateRoot's first byte must be 0xBB.
        Assert.AreEqual(0xBB, bytes[65], "ReplayedPostStateRoot[0] must be at offset 65 (per v1 header layout)");
    }

    [TestMethod]
    public void Verify_RoundTripsThroughEncodeDecode()
    {
        // The contract sees on-chain bytes; the off-chain encoder produces them. Pin
        // that bytes → on-chain accept survives a full encode→decode→encode round-trip.
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
        Assert.AreEqual(ReasonAccepted, SimulateVerify(decoded.Encode()),
            "encode→decode→encode round-trip preserves on-chain acceptance");
    }
}
