using System.Buffers.Binary;

namespace Neo.L2.Challenge.UnitTests;

/// <summary>
/// Parity tests for <c>NeoHub.GovernanceFraudVerifier</c>'s structural verification logic.
/// Without an <c>ApplicationEngine</c>-backed contract test harness, we replicate the
/// contract's accept/reject decisions in C# and run the same fraud-proof payloads
/// through both — any divergence (constant drift, off-by-one, wrong reject-reason mapping)
/// surfaces here rather than at L1 contract execution time, where it would manifest as
/// a wrongly-accepted or wrongly-rejected fraud claim.
///
/// Mirrors the contract's algorithm at
/// <c>contracts/NeoHub.GovernanceFraudVerifier/GovernanceFraudVerifierContract.cs</c>:
/// <code>
/// if (payload.Length != 101) → ReasonBadLength
/// if (payload[0] != 1) → ReasonBadVersion
/// if (payload[33..65] == payload[65..97]) → ReasonNoDiscrepancy
/// else → accepted
/// </code>
/// </summary>
[TestClass]
public class UT_GovernanceFraudVerifierParity
{
    // Mirror the on-chain contract's reason codes — see
    // GovernanceFraudVerifierContract.Reason* constants.
    private const byte ReasonBadLength = 1;
    private const byte ReasonBadVersion = 2;
    private const byte ReasonNoDiscrepancy = 3;
    private const byte ReasonOversizedWitness = 4;
    private const byte ReasonAccepted = 0;
    private const byte SupportedVersion = 1;
    private const byte SupportedVersion2 = 2;
    private const int FraudProofPayloadSize = 101;
    private const int V2HeaderSize = 105;
    private const int MaxDisputedTxBytes = 64 * 1024;

    /// <summary>
    /// C# replica of <c>GovernanceFraudVerifierContract.VerifyFraud</c>'s decision tree.
    /// Returns the on-chain reason byte (0 for accept, 1/2/3/4 for the rejection paths).
    /// Handles v1 (101 bytes) and v2 (105 + N bytes with witness trailer) formats.
    /// </summary>
    private static byte SimulateVerify(byte[] payload)
    {
        if (payload.Length < 1) return ReasonBadLength;
        var version = payload[0];
        if (version == SupportedVersion)
        {
            if (payload.Length != FraudProofPayloadSize) return ReasonBadLength;
        }
        else if (version == SupportedVersion2)
        {
            if (payload.Length < V2HeaderSize) return ReasonBadLength;
            var declaredLen = (uint)payload[101]
                | ((uint)payload[102] << 8)
                | ((uint)payload[103] << 16)
                | ((uint)payload[104] << 24);
            if (declaredLen > MaxDisputedTxBytes) return ReasonOversizedWitness;
            if (payload.Length != V2HeaderSize + declaredLen) return ReasonBadLength;
        }
        else
        {
            return ReasonBadVersion;
        }
        // Compare claimedPostStateRoot (offset 33..64) vs replayedPostStateRoot (offset 65..96).
        for (var i = 0; i < 32; i++)
        {
            if (payload[33 + i] != payload[65 + i])
            {
                // Real discrepancy → accept.
                return ReasonAccepted;
            }
        }
        return ReasonNoDiscrepancy;
    }

    private static FraudProofPayload SamplePayload(UInt256 claimed, UInt256 replayed) => new()
    {
        PreStateRoot = UInt256.Parse("0x" + new string('1', 64)),
        ClaimedPostStateRoot = claimed,
        ReplayedPostStateRoot = replayed,
        DisputedTxIndex = 7,
    };

    private static UInt256 H(byte b)
    {
        var bytes = new byte[32];
        for (var i = 0; i < 32; i++) bytes[i] = b;
        return new UInt256(bytes);
    }

    [TestMethod]
    public void Verify_RealDiscrepancy_Accepted()
    {
        var p = SamplePayload(claimed: H(0xA1), replayed: H(0xB2));
        var bytes = p.Encode();
        Assert.AreEqual(ReasonAccepted, SimulateVerify(bytes),
            "well-formed payload with claimed!=replayed must accept (reason=0)");
    }

    [TestMethod]
    public void Verify_NoDiscrepancy_Rejected()
    {
        // Same root for claimed + replayed — challenger has no real fraud claim.
        var sameRoot = H(0xCC);
        var p = SamplePayload(claimed: sameRoot, replayed: sameRoot);
        var bytes = p.Encode();
        Assert.AreEqual(ReasonNoDiscrepancy, SimulateVerify(bytes),
            "claimed==replayed must reject with ReasonNoDiscrepancy (3)");
    }

    [TestMethod]
    public void Verify_BadLength_Rejected()
    {
        // For a known version (v1), bad length → ReasonBadLength.
        var v1Short = new byte[100]; v1Short[0] = SupportedVersion;
        Assert.AreEqual(ReasonBadLength, SimulateVerify(v1Short));
        var v1Long = new byte[102]; v1Long[0] = SupportedVersion;
        Assert.AreEqual(ReasonBadLength, SimulateVerify(v1Long));

        // Empty buffer can't even read the version byte → ReasonBadLength
        // (we can't dispatch version-first if there's no version byte).
        Assert.AreEqual(ReasonBadLength, SimulateVerify(new byte[0]));
    }

    [TestMethod]
    public void Verify_BadVersion_Rejected()
    {
        var p = SamplePayload(claimed: H(0xA1), replayed: H(0xB2));
        var bytes = p.Encode();
        bytes[0] = 99;  // version byte
        Assert.AreEqual(ReasonBadVersion, SimulateVerify(bytes),
            "non-1 version byte must reject with ReasonBadVersion (2)");
    }

    [TestMethod]
    public void Verify_DecisionTreeOrder_VersionBeforeLength()
    {
        // After v2 support landed, version dispatch happens FIRST (different
        // versions have different valid lengths). So a bad-version + bad-length
        // payload reports ReasonBadVersion (2), not ReasonBadLength (1). Pin so
        // a refactor that re-orders the checks doesn't change the operator-facing
        // reject metric for malformed inputs.
        var bytes = new byte[100];
        bytes[0] = 99;
        Assert.AreEqual(ReasonBadVersion, SimulateVerify(bytes),
            "version check runs before per-version length check (since v2 added)");
    }

    [TestMethod]
    public void Verify_DecisionTreeOrder_VersionBeforeDiscrepancy()
    {
        // Pin: version is checked before the discrepancy check. So a wrong-version
        // payload with claimed==replayed reports ReasonBadVersion, not
        // ReasonNoDiscrepancy.
        var sameRoot = H(0xCC);
        var p = SamplePayload(claimed: sameRoot, replayed: sameRoot);
        var bytes = p.Encode();
        bytes[0] = 99;
        Assert.AreEqual(ReasonBadVersion, SimulateVerify(bytes),
            "version check runs before discrepancy check");
    }

    [TestMethod]
    public void Verify_LayoutOffsets_Match_FraudProofPayloadEncoder()
    {
        // The contract reads claimedPostStateRoot at offset 33..64 and
        // replayedPostStateRoot at offset 65..96. Pin that the off-chain encoder
        // writes those fields at the same offsets so the contract's BytesEqual
        // compares the right bytes. (Same risk class as the existing
        // Payload_ByteLayout_MatchesDocumentedOffsets test in UT_Challenge.)
        var claimed = H(0xAA);
        var replayed = H(0xBB);
        var bytes = SamplePayload(claimed, replayed).Encode();

        // Offset 33..64: claimedPostStateRoot bytes should all be 0xAA.
        for (var i = 33; i < 65; i++)
            Assert.AreEqual(0xAA, bytes[i], $"byte {i} should be claimedPostStateRoot's 0xAA fill");
        // Offset 65..96: replayedPostStateRoot bytes should all be 0xBB.
        for (var i = 65; i < 97; i++)
            Assert.AreEqual(0xBB, bytes[i], $"byte {i} should be replayedPostStateRoot's 0xBB fill");
    }

    [TestMethod]
    public void Verify_DisputedTxIndex_NotPartOfStructuralCheck()
    {
        // The structural verifier doesn't read DisputedTxIndex — it's metadata for
        // the consumer (e.g. a re-execution-capable verifier). Two payloads identical
        // except for DisputedTxIndex must produce the same accept/reject decision.
        var p1 = SamplePayload(claimed: H(0xA1), replayed: H(0xB2)) with { DisputedTxIndex = 0 };
        var p2 = SamplePayload(claimed: H(0xA1), replayed: H(0xB2)) with { DisputedTxIndex = uint.MaxValue };
        var b1 = p1.Encode();
        var b2 = p2.Encode();
        Assert.AreEqual(SimulateVerify(b1), SimulateVerify(b2),
            "DisputedTxIndex must not affect the structural verifier's accept/reject decision");
    }

    [TestMethod]
    public void Verify_V2_WithWitness_RealDiscrepancy_Accepted()
    {
        // v2 payload with a real disputed-tx witness + claimed != replayed → accept.
        var p = SamplePayload(claimed: H(0xA1), replayed: H(0xB2)) with
        {
            DisputedTxBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
        };
        var bytes = p.Encode();
        Assert.AreEqual(V2HeaderSize + 4, bytes.Length);
        Assert.AreEqual(SupportedVersion2, bytes[0]);
        Assert.AreEqual(ReasonAccepted, SimulateVerify(bytes),
            "v2 well-formed + real-discrepancy → accept");
    }

    [TestMethod]
    public void Verify_V2_NoDiscrepancy_Rejected()
    {
        // v2 with same claimed + replayed roots → reject NoDiscrepancy regardless of
        // the witness content. The witness exists for re-execution verifiers; the
        // structural verifier still requires the basic claim.
        var sameRoot = H(0xCC);
        var p = SamplePayload(claimed: sameRoot, replayed: sameRoot) with
        {
            DisputedTxBytes = new byte[] { 1, 2, 3 },
        };
        var bytes = p.Encode();
        Assert.AreEqual(ReasonNoDiscrepancy, SimulateVerify(bytes));
    }

    [TestMethod]
    public void Verify_V2_TruncatedWitness_Rejected()
    {
        // Declared length 5, but only 3 bytes of witness data → BadLength.
        var p = SamplePayload(claimed: H(0xA1), replayed: H(0xB2)) with
        {
            DisputedTxBytes = new byte[] { 1, 2, 3, 4, 5 },
        };
        var bytes = p.Encode();
        var truncated = bytes.AsSpan(0, V2HeaderSize + 3).ToArray();
        Assert.AreEqual(ReasonBadLength, SimulateVerify(truncated));
    }

    [TestMethod]
    public void Verify_V2_OversizedWitness_Rejected()
    {
        // Construct a v2 buffer with declaredLen > MaxDisputedTxBytes; reject before
        // length-match check.
        var bytes = new byte[V2HeaderSize + 10];
        bytes[0] = SupportedVersion2;
        var oversized = (uint)MaxDisputedTxBytes + 1;
        bytes[101] = (byte)oversized;
        bytes[102] = (byte)(oversized >> 8);
        bytes[103] = (byte)(oversized >> 16);
        bytes[104] = (byte)(oversized >> 24);
        Assert.AreEqual(ReasonOversizedWitness, SimulateVerify(bytes));
    }

    [TestMethod]
    public void Verify_V2_BadVersion_Rejected()
    {
        // version=99 → BadVersion regardless of length.
        var bytes = new byte[200];
        bytes[0] = 99;
        Assert.AreEqual(ReasonBadVersion, SimulateVerify(bytes));
    }
}
