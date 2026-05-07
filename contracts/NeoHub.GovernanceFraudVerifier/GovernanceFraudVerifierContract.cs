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
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Structural fraud verifier for governance-arbitration optimistic rollups.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.GovernanceFraudVerifier")]
[ContractPermission(Permission.Any, Method.Any)]
public class GovernanceFraudVerifierContract : SmartContract
{
    /// <summary>Wire-format payload size — must match Neo.L2.Challenge.FraudProofPayload.Size.</summary>
    public const int FraudProofPayloadSize = 1 + 32 + 32 + 32 + 4;

    /// <summary>Wire-format version we accept. Bump when the off-chain payload format changes.</summary>
    public const byte SupportedVersion = 1;

    /// <summary>Emitted on every accepted fraud-proof structural verification.</summary>
    [DisplayName("FraudProofAccepted")]
    public static event Action<uint, ulong, UInt256, UInt256> OnFraudProofAccepted = default!;

    /// <summary>Emitted on every rejected fraud-proof — reason byte distinguishes the failure mode.</summary>
    [DisplayName("FraudProofRejected")]
    public static event Action<uint, ulong, byte> OnFraudProofRejected = default!;

    /// <summary>Reject reason: payload bytes are not the canonical 101-byte length.</summary>
    public const byte ReasonBadLength = 1;

    /// <summary>Reject reason: payload version byte does not match the supported version.</summary>
    public const byte ReasonBadVersion = 2;

    /// <summary>Reject reason: claimedPostStateRoot equals replayedPostStateRoot — no discrepancy claimed.</summary>
    public const byte ReasonNoDiscrepancy = 3;

    /// <summary>
    /// Verify a fraud-proof payload's structural validity.
    /// </summary>
    /// <param name="chainId">L2 chain id (passed through from <c>OptimisticChallenge.Challenge</c>).</param>
    /// <param name="batchNumber">Disputed batch number (passed through).</param>
    /// <param name="payload">Canonical <c>FraudProofPayload</c> bytes (101 bytes, version=1).</param>
    /// <returns>
    /// True when the payload is well-formed AND claims a real discrepancy
    /// (claimedPostStateRoot != replayedPostStateRoot). False otherwise — the rejected
    /// event names the specific failure mode.
    /// </returns>
    [Safe]
    public static bool VerifyFraud(uint chainId, ulong batchNumber, byte[] payload)
    {
        if (payload.Length != FraudProofPayloadSize)
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
            return false;
        }
        if (payload[0] != SupportedVersion)
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonBadVersion);
            return false;
        }

        // Wire layout (matches Neo.L2.Challenge.FraudProofPayload.Encode):
        //   [0]      : version
        //   [1..32]  : preStateRoot
        //   [33..64] : claimedPostStateRoot
        //   [65..96] : replayedPostStateRoot
        //   [97..100]: disputedTxIndex (uint32 LE)
        // The discrepancy claim is: claimedPostStateRoot != replayedPostStateRoot.
        // If they're equal, the challenger has no actual fraud claim — reject.
        if (BytesEqual(payload, 33, payload, 65, 32))
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonNoDiscrepancy);
            return false;
        }

        // Structural validation passed. Pull out the two state roots for the event log
        // so the security council reviewing this dispute has them visible without
        // re-decoding the bytes.
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
