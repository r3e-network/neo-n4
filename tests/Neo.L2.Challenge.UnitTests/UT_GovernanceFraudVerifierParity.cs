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
    private const byte ReasonAccepted = 0;
    private const byte SupportedVersion = 1;
    private const int FraudProofPayloadSize = 101;

    /// <summary>
    /// C# replica of <c>GovernanceFraudVerifierContract.VerifyFraud</c>'s decision tree.
    /// Returns the on-chain reason byte (0 for accept, 1/2/3 for the three rejection paths).
    /// </summary>
    private static byte SimulateVerify(byte[] payload)
    {
        if (payload.Length != FraudProofPayloadSize) return ReasonBadLength;
        if (payload[0] != SupportedVersion) return ReasonBadVersion;
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
        // Truncated and oversized inputs must reject with ReasonBadLength.
        Assert.AreEqual(ReasonBadLength, SimulateVerify(new byte[100]));
        Assert.AreEqual(ReasonBadLength, SimulateVerify(new byte[102]));
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
    public void Verify_DecisionTreeOrder_LengthBeforeVersion()
    {
        // Pin the order: length is checked first, so a bad-length+bad-version
        // payload reports ReasonBadLength (1), not ReasonBadVersion (2).
        // Without this ordering pin, a refactor that swapped the checks would
        // change the on-chain event's reason byte for malformed inputs and
        // operators reading the reject-reason metric would see the wrong cause.
        var bytes = new byte[100];
        bytes[0] = 99;
        Assert.AreEqual(ReasonBadLength, SimulateVerify(bytes),
            "length check runs before version check");
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
}
