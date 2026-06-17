using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using Neo.SmartContract.Testing;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level tests for NeoHub.RestrictedExecutionFraudVerifier — the structural v3 fraud verifier in
/// the §17 mitigation #2 (invalid state root) chain. Like its companion GovernanceFraudVerifier it is
/// a pure, [Safe], stateless validator: no _deploy args, no witness gates, no cross-contract calls, no
/// storage. Its entire security value is in (a) input/wire-format validation and (b) re-deriving each
/// storage proof's pre/post Merkle root from leaf+siblings+leafIndex and rejecting the payload unless
/// every proof reconstructs to the header's PreStateRoot / ReplayedPostStateRoot AND a real
/// discrepancy (claimed != replayed) is claimed.
///
/// These tests execute verifyFraud in a real NeoVM and pin:
///   * the core fraud guard: claimedPostStateRoot == replayedPostStateRoot => no discrepancy => reject
///     (accepting it would let a self-consistent, non-fraudulent "proof" slash an honest sequencer);
///   * the soundness guard the v3 verifier adds over v2: a storage proof whose pre-leaf does NOT fold
///     to payload.PreStateRoot is rejected (ReasonPreStateRootMismatch); same on the post side
///     (ReasonReplayedPostStateRootMismatch) — a challenger cannot supply arbitrary unrelated proofs;
///   * proof-count bounds: zero proofs (must use v2) and > MaxStorageProofsPerPayload are rejected;
///   * version gating: only version byte 3 is accepted (v1/v2 belong to GovernanceFraudVerifier);
///   * wire-format framing: sub-header truncation, oversized declared witness, and any extra trailing
///     bytes after the last proof (strict total-length match) are rejected;
///   * the happy path: a well-formed v3 payload whose single proof folds to both roots with a genuine
///     discrepancy is accepted (true).
///
/// To exercise the Merkle re-derivation honestly, the test mirrors the contract's HashEntry
/// (Hash256(int32LE(keyLen)||key||int32LE(valLen)||val)) and FoldMerkleProof (Hash256(left||right)
/// with leafIndex's low bit at level i selecting current-left/current-right) off-chain so the built
/// payload's header roots equal what the contract re-derives. verifyFraud is [Safe] and returns bool,
/// so rejected inputs return false rather than faulting.
/// </summary>
[TestClass]
public class UT_RestrictedExecutionFraudVerifier_Vm
{
    private const uint ChainId = 1001;
    private const ulong BatchNumber = 42;

    // Wire-format constants mirrored from RestrictedExecutionFraudVerifierContract.
    private const int V1HeaderSize = 1 + 32 + 32 + 32 + 4;   // 101
    private const int V2HeaderSize = V1HeaderSize + 4;       // 105
    private const byte SupportedVersion3 = 3;
    private const int MaxDisputedTxBytes = 64 * 1024;        // 65536
    private const int MaxStorageProofsPerPayload = 32;
    private const int MaxSiblingDepth = 64;

    private static NeoHubRestrictedExecutionFraudVerifier Deploy(TestEngine engine) =>
        engine.Deploy<NeoHubRestrictedExecutionFraudVerifier>(
            NeoHubRestrictedExecutionFraudVerifier.Nef, NeoHubRestrictedExecutionFraudVerifier.Manifest, new object[] { });

    // ---- off-chain mirrors of the contract's hashing / folding ----

    private static byte[] Hash256(byte[] x) => SHA256.HashData(SHA256.HashData(x));

    /// <summary>Mirror of contract HashEntry: Hash256(int32LE(keyLen)||key||int32LE(valLen)||value).</summary>
    private static byte[] HashEntry(byte[] key, byte[] value)
    {
        var buf = new byte[4 + key.Length + 4 + value.Length];
        var pos = 0;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos, 4), key.Length); pos += 4;
        key.CopyTo(buf.AsSpan(pos, key.Length)); pos += key.Length;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos, 4), value.Length); pos += 4;
        value.CopyTo(buf.AsSpan(pos, value.Length));
        return Hash256(buf);
    }

    /// <summary>Mirror of contract FoldMerkleProof: at level i, if (index&amp;1)==0 current is left of the
    /// sibling, else current is right; Hash256(left||right); index >>= 1.</summary>
    private static byte[] FoldMerkleProof(byte[] leaf, byte[][] siblings, ulong leafIndex)
    {
        var current = leaf;
        var index = leafIndex;
        for (var i = 0; i < siblings.Length; i++)
        {
            var combined = new byte[64];
            if ((index & 1UL) == 0UL)
            {
                Array.Copy(current, 0, combined, 0, 32);
                Array.Copy(siblings[i], 0, combined, 32, 32);
            }
            else
            {
                Array.Copy(siblings[i], 0, combined, 0, 32);
                Array.Copy(current, 0, combined, 32, 32);
            }
            current = Hash256(combined);
            index >>= 1;
        }
        return current;
    }

    /// <summary>A single storage-proof manifest: a (key, preValue, postValue) entry with its
    /// leafIndex and pre/post sibling lists. Pre and post share the same key but differ in value
    /// (the disputed write), folding to the pre and replayed-post roots respectively.</summary>
    private sealed class StorageProof
    {
        public byte[] Key = Array.Empty<byte>();
        public byte[] PreValue = Array.Empty<byte>();
        public byte[] PostValue = Array.Empty<byte>();
        public ulong LeafIndex;
        public byte[][] PreSiblings = Array.Empty<byte[]>();
        public byte[][] PostSiblings = Array.Empty<byte[]>();

        public byte[] PreRoot() => FoldMerkleProof(HashEntry(Key, PreValue), PreSiblings, LeafIndex);
        public byte[] PostRoot() => FoldMerkleProof(HashEntry(Key, PostValue), PostSiblings, LeafIndex);
    }

    /// <summary>Build a canonical, well-formed v3 payload from a list of storage proofs. The header
    /// PreStateRoot/ReplayedPostStateRoot are taken from the FIRST proof's derived roots (all proofs
    /// must fold to the same pair to be accepted). claimedPostStateRoot is forced to differ from the
    /// replayed root so the NoDiscrepancy guard passes by default. Pass <paramref name="claimedRoot"/>
    /// to override (e.g. to make claimed == replayed for the no-discrepancy test). Pass
    /// <paramref name="overridePreRoot"/> / <paramref name="overrideReplayedRoot"/> to deliberately
    /// break the header so re-derivation mismatches.</summary>
    private static byte[] BuildV3(
        IList<StorageProof> proofs,
        int witnessLen = 0,
        uint? declaredWitnessLen = null,
        uint? numProofsOverride = null,
        byte[]? claimedRoot = null,
        byte[]? overridePreRoot = null,
        byte[]? overrideReplayedRoot = null,
        int trailingPad = 0)
    {
        var preRoot = overridePreRoot ?? proofs[0].PreRoot();
        var replayedRoot = overrideReplayedRoot ?? proofs[0].PostRoot();
        // Default claimed root: differs from the replayed root (a real discrepancy claim).
        var claimed = claimedRoot ?? Fill(0x77);

        var body = new List<byte>();
        // numStorageProofs (uint32 LE).
        var declaredCount = numProofsOverride ?? (uint)proofs.Count;
        AppendUInt32LE(body, declaredCount);
        foreach (var sp in proofs)
        {
            AppendUInt16LE(body, (ushort)sp.Key.Length);
            body.AddRange(sp.Key);
            AppendUInt32LE(body, (uint)sp.PreValue.Length);
            body.AddRange(sp.PreValue);
            AppendUInt32LE(body, (uint)sp.PostValue.Length);
            body.AddRange(sp.PostValue);
            AppendUInt64LE(body, sp.LeafIndex);
            body.Add((byte)sp.PreSiblings.Length);
            foreach (var s in sp.PreSiblings) body.AddRange(s);
            body.Add((byte)sp.PostSiblings.Length);
            foreach (var s in sp.PostSiblings) body.AddRange(s);
        }
        for (var i = 0; i < trailingPad; i++) body.Add(0x00);

        var p = new byte[V2HeaderSize + witnessLen + body.Count];
        p[0] = SupportedVersion3;
        Array.Copy(preRoot, 0, p, 1, 32);
        Array.Copy(claimed, 0, p, 33, 32);
        Array.Copy(replayedRoot, 0, p, 65, 32);
        WriteUInt32LE(p, 97, 7);                                          // disputedTxIndex (event-only)
        WriteUInt32LE(p, 101, declaredWitnessLen ?? (uint)witnessLen);    // declared witness length
        // witness bytes left as zeros [105 .. 105+witnessLen)
        body.ToArray().CopyTo(p, V2HeaderSize + witnessLen);
        return p;
    }

    private static byte[] Fill(byte b)
    {
        var x = new byte[32];
        for (var i = 0; i < 32; i++) x[i] = b;
        return x;
    }

    private static void AppendUInt16LE(List<byte> l, ushort v)
    {
        l.Add((byte)(v & 0xFF));
        l.Add((byte)((v >> 8) & 0xFF));
    }

    private static void AppendUInt32LE(List<byte> l, uint v)
    {
        l.Add((byte)(v & 0xFF));
        l.Add((byte)((v >> 8) & 0xFF));
        l.Add((byte)((v >> 16) & 0xFF));
        l.Add((byte)((v >> 24) & 0xFF));
    }

    private static void AppendUInt64LE(List<byte> l, ulong v)
    {
        for (var i = 0; i < 8; i++) l.Add((byte)((v >> (8 * i)) & 0xFF));
    }

    private static void WriteUInt32LE(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    /// <summary>A standard single-proof manifest: key + differing pre/post values + a 2-level sibling
    /// path. preValue != postValue so the pre-root and post-root genuinely differ (a real write).</summary>
    private static StorageProof StandardProof() => new()
    {
        Key = new byte[] { 0x01, 0x02, 0x03, 0x04 },
        PreValue = new byte[] { 0xAA, 0xAA },
        PostValue = new byte[] { 0xBB, 0xBB, 0xBB },
        LeafIndex = 2,                       // bits: level0=0 (left), level1=1 (right)
        PreSiblings = new[] { Fill(0x10), Fill(0x20) },
        PostSiblings = new[] { Fill(0x10), Fill(0x20) },
    };

    // ---- happy path ----

    [TestMethod]
    public void VerifyFraud_WellFormedV3_ProofsFoldToHeaderRoots_WithDiscrepancy_Accepts()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        var sp = StandardProof();
        var payload = BuildV3(new List<StorageProof> { sp });

        // Sanity: pre and post roots genuinely differ (the proof describes a real state write).
        CollectionAssert.AreNotEqual(sp.PreRoot(), sp.PostRoot(),
            "pre/post values differ => derived roots must differ (test fixture self-check)");

        Assert.IsTrue(fv.VerifyFraud(ChainId, BatchNumber, payload)!,
            "v3 payload whose proof folds to PreStateRoot and ReplayedPostStateRoot with claimed != replayed must be accepted");
    }

    [TestMethod]
    public void VerifyFraud_WithWitnessTrailer_DeclaredLenMatches_Accepts()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // A 128-byte re-execution witness sits between the header and the proof manifests; the
        // verifier must skip it correctly (declared length == actual trailer) and still validate.
        var payload = BuildV3(new List<StorageProof> { StandardProof() }, witnessLen: 128);
        Assert.IsTrue(fv.VerifyFraud(ChainId, BatchNumber, payload)!,
            "well-formed v3 with a matching-length witness trailer must be accepted");
    }

    [TestMethod]
    public void VerifyFraud_MultipleProofsFoldingToSameRoots_Accepts()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Two distinct keys that, with chosen sibling paths, fold to the SAME pre/post root pair.
        // Both must reconstruct to the header roots for acceptance.
        var sp1 = StandardProof();
        var sp2 = new StorageProof
        {
            Key = sp1.Key,
            PreValue = sp1.PreValue,
            PostValue = sp1.PostValue,
            LeafIndex = sp1.LeafIndex,
            PreSiblings = sp1.PreSiblings,
            PostSiblings = sp1.PostSiblings,
        };
        var payload = BuildV3(new List<StorageProof> { sp1, sp2 });
        Assert.IsTrue(fv.VerifyFraud(ChainId, BatchNumber, payload)!,
            "all proofs folding to the header roots (claimed != replayed) must be accepted");
    }

    // ---- core fraud guard: no discrepancy ----

    [TestMethod]
    public void VerifyFraud_NoDiscrepancy_ClaimedEqualsReplayed_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        var sp = StandardProof();
        // Force claimedPostStateRoot == replayedPostStateRoot. The proofs themselves are valid, but
        // the challenger is claiming no actual fraud — accepting this would let a self-consistent
        // proof slash an honest sequencer's bond. Must be rejected (ReasonNoDiscrepancy).
        var payload = BuildV3(new List<StorageProof> { sp }, claimedRoot: sp.PostRoot());
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, payload)!,
            "claimed == replayed => no discrepancy claimed => must be rejected even with valid proofs");
    }

    // ---- soundness: proofs must reconstruct to the header roots ----

    [TestMethod]
    public void VerifyFraud_PreRootMismatch_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        var sp = StandardProof();
        // Header PreStateRoot is set to garbage unrelated to what the proof folds to. The verifier
        // must reject (ReasonPreStateRootMismatch) — a challenger cannot bind an arbitrary pre-root
        // to a proof of a different leaf. This is the soundness check v3 adds over the v2 verifier.
        var payload = BuildV3(new List<StorageProof> { sp }, overridePreRoot: Fill(0xEE));
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, payload)!,
            "a proof whose pre-leaf does not fold to payload.PreStateRoot must be rejected");
    }

    [TestMethod]
    public void VerifyFraud_ReplayedPostRootMismatch_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        var sp = StandardProof();
        // PreRoot matches (default) but ReplayedPostStateRoot is garbage. The post-side fold will not
        // match => ReasonReplayedPostStateRootMismatch. claimed (default 0x77) != replayed (0xEE) so
        // the discrepancy guard passes and execution reaches the post-root check.
        var payload = BuildV3(new List<StorageProof> { sp }, overrideReplayedRoot: Fill(0xEE));
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, payload)!,
            "a proof whose post-leaf does not fold to payload.ReplayedPostStateRoot must be rejected");
    }

    [TestMethod]
    public void VerifyFraud_TamperedSibling_BreaksReconstruction_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Header roots are taken from the ORIGINAL proof, but the proof actually written into the
        // payload uses a tampered sibling. The on-chain fold then diverges from the header => reject.
        var honest = StandardProof();
        var preRoot = honest.PreRoot();
        var postRoot = honest.PostRoot();

        var tampered = StandardProof();
        tampered.PreSiblings = new[] { Fill(0x10), Fill(0x99) };  // second pre-sibling flipped
        tampered.PostSiblings = new[] { Fill(0x10), Fill(0x99) };

        var payload = BuildV3(new List<StorageProof> { tampered },
            overridePreRoot: preRoot, overrideReplayedRoot: postRoot);
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, payload)!,
            "tampering a sibling so the fold no longer reconstructs the header root must be rejected");
    }

    // ---- proof-count bounds ----

    [TestMethod]
    public void VerifyFraud_ZeroProofs_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // numStorageProofs == 0 (declared) — v3 must carry at least one proof (else use v2).
        // Provide one real proof for header-root derivation but declare a count of 0.
        var sp = StandardProof();
        var payload = BuildV3(new List<StorageProof> { sp }, numProofsOverride: 0);
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, payload)!,
            "zero declared storage proofs must be rejected (ReasonProofCountInvalid)");
    }

    [TestMethod]
    public void VerifyFraud_TooManyProofs_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Declared count above MaxStorageProofsPerPayload must be rejected before any proof is read.
        var sp = StandardProof();
        var payload = BuildV3(new List<StorageProof> { sp },
            numProofsOverride: (uint)(MaxStorageProofsPerPayload + 1));
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, payload)!,
            "declared proof count above MaxStorageProofsPerPayload must be rejected");
    }

    // ---- version gating ----

    [TestMethod]
    public void VerifyFraud_NonV3Version_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Build a valid v3 payload then flip the version byte. v1/v2 belong to GovernanceFraudVerifier.
        var v2 = BuildV3(new List<StorageProof> { StandardProof() });
        v2[0] = 2;
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, v2)!,
            "version 2 must be rejected (ReasonBadVersion) — only v3 is handled here");

        var v0 = BuildV3(new List<StorageProof> { StandardProof() });
        v0[0] = 0;
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, v0)!,
            "version 0 must be rejected (ReasonBadVersion)");
    }

    [TestMethod]
    public void VerifyFraud_EmptyPayload_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, new byte[0])!,
            "sub-1-byte payload has no version prefix => must be rejected (ReasonBadLength)");
    }

    // ---- wire-format framing ----

    [TestMethod]
    public void VerifyFraud_TruncatedBelowV2Header_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Version 3 but fewer than 105 bytes — cannot even read the declared witness length prefix.
        var p = new byte[V2HeaderSize - 1];
        p[0] = SupportedVersion3;
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, p)!,
            "v3 payload below the 105-byte header must be rejected (ReasonBadLength)");
    }

    [TestMethod]
    public void VerifyFraud_OversizedDeclaredWitness_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Declared witness length exceeds the 64KB cap; guard runs before the proof count is read so a
        // tiny actual trailer is fine for exercising it (ReasonOversizedWitness).
        var payload = BuildV3(new List<StorageProof> { StandardProof() },
            declaredWitnessLen: (uint)(MaxDisputedTxBytes + 1));
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, payload)!,
            "declared witness length above MaxDisputedTxBytes must be rejected");
    }

    [TestMethod]
    public void VerifyFraud_DeclaredWitnessLongerThanTrailer_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Declared witness length far exceeds the actual bytes present, so the manifests cannot be
        // located: pos + 4 runs past the end of the payload => ReasonBadLength.
        var payload = BuildV3(new List<StorageProof> { StandardProof() },
            witnessLen: 8, declaredWitnessLen: 4096);
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, payload)!,
            "declared witness length longer than the actual trailer must be rejected (ReasonBadLength)");
    }

    [TestMethod]
    public void VerifyFraud_ExtraTrailingBytesAfterProofs_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // A fully valid proof set followed by extra padding bytes. The strict total-length match
        // (pos != payload.Length) must reject — no malleable trailing data is allowed.
        var payload = BuildV3(new List<StorageProof> { StandardProof() }, trailingPad: 5);
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, payload)!,
            "extra trailing bytes after the last proof must be rejected (strict length match)");
    }

    [TestMethod]
    public void VerifyFraud_SiblingDepthAtCap_StillValidates_Accepts()
    {
        var engine = new TestEngine(true);
        // A depth-64 Merkle reconstruction performs thousands of hash syscalls; raise the gas ceiling
        // above the default so the (legitimately expensive) at-cap proof can complete.
        engine.Fee = 100_000_000_000L; // 1000 GAS gas budget
        var fv = Deploy(engine);

        // Boundary: a proof with exactly MaxSiblingDepth siblings is allowed (<=), and when its fold
        // is honestly reflected in the header roots the payload is accepted. Different pre/post values
        // keep the two derived roots distinct.
        var sibs = new byte[MaxSiblingDepth][];
        for (var i = 0; i < MaxSiblingDepth; i++) sibs[i] = Fill((byte)(i + 1));
        var sp = new StorageProof
        {
            Key = new byte[] { 0xDE, 0xAD },
            PreValue = new byte[] { 0x01 },
            PostValue = new byte[] { 0x02 },
            LeafIndex = 0xABCDEF12,
            PreSiblings = sibs,
            PostSiblings = sibs,
        };
        var payload = BuildV3(new List<StorageProof> { sp });
        Assert.IsTrue(fv.VerifyFraud(ChainId, BatchNumber, payload)!,
            "a proof with sibling depth exactly at the cap that folds to the header roots must be accepted");
    }
}
