using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.GovernanceFraudVerifier;

/// <summary>
/// Structural fraud verifier for governance-arbitration optimistic rollups.
/// </summary>
/// <remarks>
/// <para>
/// Stage-0 verifier in the §17 mitigation #2 (invalid state root) chain. Decodes the
/// canonical <c>Neo.L2.Challenge.FraudProofPayload</c> wire format (101 bytes), checks
/// the version + length + the basic claim that the challenger's replayed root differs
/// from the sequencer's claimed root. Returns <c>true</c> when those structural checks
/// pass — at which point <c>NeoHub.OptimisticChallenge.Challenge</c> proceeds with bond
/// slashing + challenger reward.
/// </para>
/// <para>
/// This verifier does NOT re-execute the disputed transaction on L1; an L1 contract
/// can't access the L2 state needed for re-execution without execution-trace witness
/// bytes that the current <c>FraudProofPayload</c> wire format doesn't carry. So this
/// verifier is for chains running in <em>governance-arbitration mode</em> — the on-chain
/// check confirms the proof is well-formed and claims a discrepancy; a human security
/// council (via <c>NeoHub.GovernanceController</c>) is the final arbiter for whether
/// the challenger's replay was actually correct.
/// </para>
/// <para>
/// Production chains targeting full trustlessness must replace this verifier with one
/// that re-executes the disputed transaction (requires extending <c>FraudProofPayload</c>
/// with execution-trace witness bytes — see <c>IMPLEMENTATION_STATUS.md</c>'s
/// "Optimistic-challenge fraud-proof game" section). Wire by registering this
/// contract's hash with <c>OptimisticChallenge.Challenge(... fraudVerifier)</c>.
/// </para>
/// <para>
/// See doc.md §15 (optimistic challenge), §17 mitigation #2.
/// </para>
/// </remarks>
[DisplayName("NeoHub.GovernanceFraudVerifier")]
[ContractAuthor("R3E Network", "dev@r3e.network")]
[ContractDescription("Structural fraud verifier for governance-arbitration optimistic rollups.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.GovernanceFraudVerifier")]
[ContractPermission(Permission.Any, Method.Any)]
public class GovernanceFraudVerifierContract : SmartContract
{
    /// <summary>v1 wire-format payload size — must match Neo.L2.Challenge.FraudProofPayload.Size.</summary>
    public const int FraudProofPayloadSize = 1 + 32 + 32 + 32 + 4;

    /// <summary>v2 wire-format header size (= v1 size + 4-byte witness length prefix).</summary>
    public const int V2HeaderSize = FraudProofPayloadSize + 4;

    /// <summary>Cap on the v2 disputed-tx witness — must match Neo.L2.Challenge.FraudProofPayload.MaxDisputedTxBytes.</summary>
    public const int MaxDisputedTxBytes = 64 * 1024;

    /// <summary>v1 wire-format version (structural-only, fixed length).</summary>
    public const byte SupportedVersion = 1;

    /// <summary>v2 wire-format version (includes disputed-tx witness trailer).</summary>
    public const byte SupportedVersion2 = 2;

    /// <summary>Emitted on every accepted fraud-proof structural verification.</summary>
    [DisplayName("FraudProofAccepted")]
    public static event Action<uint, ulong, UInt256, UInt256> OnFraudProofAccepted = default!;

    /// <summary>Emitted on every rejected fraud-proof — reason byte distinguishes the failure mode.</summary>
    [DisplayName("FraudProofRejected")]
    public static event Action<uint, ulong, byte> OnFraudProofRejected = default!;

    /// <summary>Reject reason: payload bytes are not the canonical 101-byte length (v1) or
    /// the v2 length doesn't match the declared header + witness size.</summary>
    public const byte ReasonBadLength = 1;

    /// <summary>Reject reason: payload version byte is not 1 (v1) or 2 (v2).</summary>
    public const byte ReasonBadVersion = 2;

    /// <summary>Reject reason: claimedPostStateRoot equals replayedPostStateRoot — no discrepancy claimed.</summary>
    public const byte ReasonNoDiscrepancy = 3;

    /// <summary>Reject reason (v2 only): declared disputed-tx witness length exceeds MaxDisputedTxBytes.</summary>
    public const byte ReasonOversizedWitness = 4;

    /// <summary>
    /// Verify a fraud-proof payload's structural validity. Accepts both v1 (101-byte
    /// fixed) and v2 (105 + N-byte variable, with disputed-tx witness) wire formats.
    /// </summary>
    /// <param name="chainId">L2 chain id (passed through from <c>OptimisticChallenge.Challenge</c>).</param>
    /// <param name="batchNumber">Disputed batch number (passed through).</param>
    /// <param name="payload">Canonical <c>FraudProofPayload</c> bytes (v1 or v2).</param>
    /// <returns>
    /// True when the payload is well-formed AND claims a real discrepancy
    /// (claimedPostStateRoot != replayedPostStateRoot). False otherwise — the rejected
    /// event names the specific failure mode.
    /// </returns>
    // NOT [Safe]: this method emits diagnostic events (Runtime.Notify), which requires the
    // AllowNotify call flag. A [Safe] method is invoked read-only (ReadStates|AllowCall), so the
    // Notify would FAULT — OptimisticChallenge.Challenge dispatches here with
    // CallFlags.ReadOnly|AllowNotify expressly so these reason-coded events can fire.
    public static bool VerifyFraud(uint chainId, ulong batchNumber, byte[] payload)
    {
        // Need at least the 1-byte version prefix to dispatch.
        if (payload.Length < 1)
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
            return false;
        }
        var version = payload[0];

        if (version == SupportedVersion)
        {
            // v1: fixed 101 bytes, no witness trailer.
            if (payload.Length != FraudProofPayloadSize)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
        }
        else if (version == SupportedVersion2)
        {
            // v2: at least 105 bytes for the header + declared witness length.
            if (payload.Length < V2HeaderSize)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
            // Read uint32 LE at offset 101..104 = declared disputed-tx witness length.
            var declaredLen = (uint)payload[101]
                | ((uint)payload[102] << 8)
                | ((uint)payload[103] << 16)
                | ((uint)payload[104] << 24);
            if (declaredLen > MaxDisputedTxBytes)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonOversizedWitness);
                return false;
            }
            // Strict length match — extra trailing bytes or a truncated witness are
            // both malformed. Same iter discipline as the off-chain Decode.
            if (payload.Length != V2HeaderSize + declaredLen)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
        }
        else
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonBadVersion);
            return false;
        }

        // Wire layout (matches Neo.L2.Challenge.FraudProofPayload.Encode):
        //   [0]      : version (1 or 2)
        //   [1..32]  : preStateRoot
        //   [33..64] : claimedPostStateRoot
        //   [65..96] : replayedPostStateRoot
        //   [97..100]: disputedTxIndex (uint32 LE)
        //   v2 only:
        //   [101..104]: disputedTxLen (uint32 LE)
        //   [105..]   : disputedTxBytes
        // The discrepancy claim is: claimedPostStateRoot != replayedPostStateRoot.
        // If they're equal, the challenger has no actual fraud claim — reject.
        if (BytesEqual(payload, 33, payload, 65, 32))
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonNoDiscrepancy);
            return false;
        }

        // Structural validation passed. Pull out the two state roots for the event log
        // so the security council reviewing this dispute has them visible without
        // re-decoding the bytes. v2 witness bytes are NOT in the event — operators
        // who want them re-decode the original payload off-chain.
        var claimedRoot = SliceUInt256(payload, 33);
        var replayedRoot = SliceUInt256(payload, 65);
        OnFraudProofAccepted(chainId, batchNumber, claimedRoot, replayedRoot);
        return true;
    }

    private static UInt256 SliceUInt256(byte[] src, int offset)
    {
        var buf = new byte[32];
        for (var i = 0; i < 32; i++) buf[i] = src[offset + i];
        return (UInt256)buf;
    }

    /// <summary>Constant-time-ish byte-range equality (NeoVM has no SequenceEqual primitive).</summary>
    private static bool BytesEqual(byte[] a, int aOffset, byte[] b, int bOffset, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (a[aOffset + i] != b[bOffset + i]) return false;
        }
        return true;
    }
}
