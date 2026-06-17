using System.Numerics;
using Neo.SmartContract.Testing;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level tests for NeoHub.GovernanceFraudVerifier — the stage-0 structural fraud verifier in the
/// §17 mitigation #2 (invalid state root) chain. The contract is a pure, [Safe], stateless validator:
/// no _deploy args, no witness gates, no cross-contract calls, no storage. Its security value is
/// entirely in input validation and the discrepancy claim, so these tests execute verifyFraud in a
/// real NeoVM and pin:
///   * the v1 wire format is accepted ONLY at the canonical 101-byte length;
///   * the v2 wire format is accepted ONLY when the declared witness length matches the trailer
///     exactly AND does not exceed the 64KB cap (no oversized / truncated / padded witness);
///   * unknown version bytes and sub-1-byte payloads are rejected;
///   * the core fraud guard: a proof whose claimedPostStateRoot == replayedPostStateRoot claims NO
///     discrepancy and MUST be rejected (false) — accepting it would let a self-consistent,
///     non-fraudulent "proof" trigger bond slashing in OptimisticChallenge;
///   * a well-formed proof claiming a real discrepancy is accepted (true).
/// Because verifyFraud is [Safe] and returns bool, rejected inputs return false rather than faulting.
/// </summary>
[TestClass]
public class UT_GovernanceFraudVerifier_Vm
{
    private const uint ChainId = 1001;
    private const ulong BatchNumber = 42;

    // Wire-format constants mirrored from GovernanceFraudVerifierContract.
    private const int V1Size = 1 + 32 + 32 + 32 + 4;      // 101
    private const int V2HeaderSize = V1Size + 4;          // 105
    private const int MaxDisputedTxBytes = 64 * 1024;     // 65536
    private const byte VersionV1 = 1;
    private const byte VersionV2 = 2;

    private static NeoHubGovernanceFraudVerifier Deploy(TestEngine engine) =>
        engine.Deploy<NeoHubGovernanceFraudVerifier>(
            NeoHubGovernanceFraudVerifier.Nef, NeoHubGovernanceFraudVerifier.Manifest, new object[] { });

    /// <summary>Build a canonical v1 payload. Bytes [33..64]=claimedRoot, [65..96]=replayedRoot.
    /// By default the two roots differ (a real discrepancy claim).</summary>
    private static byte[] BuildV1(byte claimedFill = 0xAA, byte replayedFill = 0xBB, uint disputedTxIndex = 7)
    {
        var p = new byte[V1Size];
        p[0] = VersionV1;
        // preStateRoot [1..32] left as-is.
        for (var i = 0; i < 32; i++) p[33 + i] = claimedFill;
        for (var i = 0; i < 32; i++) p[65 + i] = replayedFill;
        WriteUInt32LE(p, 97, disputedTxIndex);
        return p;
    }

    /// <summary>Build a v2 payload of total length 105 + witnessLen, with the declared witness length
    /// prefix set to <paramref name="declaredLen"/> (defaults to the actual witness length).</summary>
    private static byte[] BuildV2(int witnessLen, uint? declaredLen = null,
        byte claimedFill = 0xAA, byte replayedFill = 0xBB)
    {
        var p = new byte[V2HeaderSize + witnessLen];
        p[0] = VersionV2;
        for (var i = 0; i < 32; i++) p[33 + i] = claimedFill;
        for (var i = 0; i < 32; i++) p[65 + i] = replayedFill;
        WriteUInt32LE(p, 97, 7);                                  // disputedTxIndex
        WriteUInt32LE(p, 101, declaredLen ?? (uint)witnessLen);   // declared witness length
        return p;
    }

    private static void WriteUInt32LE(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    [TestMethod]
    public void VerifyFraud_V1_WellFormedWithDiscrepancy_Accepts()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Claimed root != replayed root => a genuine fraud discrepancy claim.
        Assert.IsTrue(fv.VerifyFraud(ChainId, BatchNumber, BuildV1(0xAA, 0xBB))!,
            "well-formed v1 payload claiming a real root discrepancy must be accepted");
    }

    [TestMethod]
    public void VerifyFraud_V1_NoDiscrepancy_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // claimedPostStateRoot == replayedPostStateRoot => the challenger claims no fraud.
        // Accepting this would let a self-consistent proof slash an honest sequencer's bond.
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, BuildV1(0xCC, 0xCC))!,
            "equal claimed/replayed roots = no discrepancy => must be rejected");
    }

    [TestMethod]
    public void VerifyFraud_V1_WrongLength_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        var tooShort = new byte[V1Size - 1];
        tooShort[0] = VersionV1;
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, tooShort)!,
            "v1 payload shorter than 101 bytes must be rejected (ReasonBadLength)");

        var tooLong = new byte[V1Size + 1];
        tooLong[0] = VersionV1;
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, tooLong)!,
            "v1 payload longer than 101 bytes must be rejected (ReasonBadLength)");
    }

    [TestMethod]
    public void VerifyFraud_EmptyPayload_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, new byte[0])!,
            "sub-1-byte payload has no version prefix => must be rejected");
    }

    [TestMethod]
    public void VerifyFraud_UnknownVersion_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Version 0 and 3 are unsupported. Use a 101-byte buffer so length isn't the failing guard.
        var v0 = BuildV1(0xAA, 0xBB);
        v0[0] = 0;
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, v0)!,
            "version 0 is unsupported => ReasonBadVersion");

        var v3 = BuildV1(0xAA, 0xBB);
        v3[0] = 3;
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, v3)!,
            "version 3 is unsupported => ReasonBadVersion");
    }

    [TestMethod]
    public void VerifyFraud_V2_WellFormedWithWitness_Accepts()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // 105 header + 256-byte witness, declared length matches actual trailer, roots differ.
        Assert.IsTrue(fv.VerifyFraud(ChainId, BatchNumber, BuildV2(256))!,
            "well-formed v2 payload with matching witness length and a discrepancy must be accepted");
    }

    [TestMethod]
    public void VerifyFraud_V2_ZeroWitness_HeaderOnly_Accepts()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Minimal v2: exactly the 105-byte header, declared witness length 0.
        Assert.IsTrue(fv.VerifyFraud(ChainId, BatchNumber, BuildV2(0))!,
            "v2 header-only payload (declared witness 0) with a discrepancy must be accepted");
    }

    [TestMethod]
    public void VerifyFraud_V2_NoDiscrepancy_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Even a perfectly framed v2 payload must be rejected if it claims no root discrepancy.
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, BuildV2(128, claimedFill: 0x11, replayedFill: 0x11))!,
            "v2 with equal claimed/replayed roots claims no fraud => must be rejected");
    }

    [TestMethod]
    public void VerifyFraud_V2_TruncatedBelowHeader_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Version 2 but fewer than 105 bytes — cannot even read the declared length prefix.
        var p = new byte[V2HeaderSize - 1];
        p[0] = VersionV2;
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, p)!,
            "v2 payload below the 105-byte header must be rejected (ReasonBadLength)");
    }

    [TestMethod]
    public void VerifyFraud_V2_DeclaredLenMismatch_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Trailer is 100 bytes but the declared length says 200 (truncated witness). Strict match fails.
        var truncated = BuildV2(100, declaredLen: 200);
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, truncated)!,
            "v2 declared witness length > actual trailer must be rejected (ReasonBadLength)");

        // Trailer is 200 bytes but declared length says 100 (extra trailing bytes). Strict match fails.
        var padded = BuildV2(200, declaredLen: 100);
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, padded)!,
            "v2 declared witness length < actual trailer (padded) must be rejected (ReasonBadLength)");
    }

    [TestMethod]
    public void VerifyFraud_V2_OversizedWitness_Rejected()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Declared witness length exceeds the 64KB cap. Use a small actual trailer so we exercise the
        // oversized-witness guard (which runs before the strict length match) without allocating 64KB.
        var oversized = BuildV2(16, declaredLen: (uint)(MaxDisputedTxBytes + 1));
        Assert.IsFalse(fv.VerifyFraud(ChainId, BatchNumber, oversized)!,
            "declared witness length above MaxDisputedTxBytes must be rejected (ReasonOversizedWitness)");
    }

    [TestMethod]
    public void VerifyFraud_V2_WitnessAtCap_DoesNotRejectOnSizeGuard()
    {
        var engine = new TestEngine(true);
        var fv = Deploy(engine);

        // Declared length exactly at the cap is allowed by the size guard; combined with a matching
        // actual trailer this is a fully well-formed payload and must be accepted (boundary check).
        Assert.IsTrue(fv.VerifyFraud(ChainId, BatchNumber, BuildV2(MaxDisputedTxBytes))!,
            "declared witness length equal to the cap is allowed (<=) and a matching trailer accepts");
    }
}
