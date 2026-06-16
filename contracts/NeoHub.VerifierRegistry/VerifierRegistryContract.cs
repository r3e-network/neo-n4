using System;
using System.ComponentModel;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.VerifierRegistry;

/// <summary>
/// Registers verifier contracts per <c>ProofType</c> and dispatches commitment verification
/// requests from <see cref="NeoHub.SettlementManager"/>. See doc.md §3.2 (VerifierRegistry).
/// </summary>
[DisplayName("NeoHub.VerifierRegistry")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Pluggable proof verifier dispatch table for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.VerifierRegistry")]
[ContractPermission(Permission.Any, Method.Any)]
public class VerifierRegistryContract : SmartContract
{
    private const byte PrefixVerifier = 0x01;   // 0x01 + proofType(1B) → UInt160 verifier
    private const byte KeyGovernanceController = 0x02;
    private const byte PrefixConsumedProposal = 0x03;  // 0x03 + proposalId(8B) → 1 (replay protection)
    private const byte KeyGovernanceLocked = 0x04;     // once set → instant owner RegisterVerifier is disabled (one-way)
    private const byte KeyOwner = 0xFF;

    /// <summary>
    /// Offset of the proofType byte inside a canonical L2BatchCommitment encoding.
    /// Matches Neo.L2.Batch.BatchSerializer: 4+8+8+8 + 9*32 = 316.
    /// </summary>
    public const int ProofTypeOffset = 316;

    /// <summary>Emitted whenever a verifier is registered or replaced.</summary>
    [DisplayName("VerifierRegistered")]
    public static event Action<byte, UInt160> OnVerifierRegistered = default!;

    /// <summary>Emitted when the GovernanceController address is changed.</summary>
    [DisplayName("GovernanceControllerChanged")]
    public static event Action<UInt160> OnGovernanceControllerChanged = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    /// <summary>Emitted the first time governance is locked. Re-locking is a no-op and does not re-emit.</summary>
    [DisplayName("GovernanceLocked")]
    public static event Action OnGovernanceLocked = default!;

    /// <summary>Set the initial owner.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var owner = (UInt160)data;
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        Storage.Put(new byte[] { KeyOwner }, owner);
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Transfer governance ownership. Owner only.</summary>
    public static void SetOwner(UInt160 newOwner)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "invalid new owner");
        var oldOwner = GetOwner();
        Storage.Put(new byte[] { KeyOwner }, newOwner);
        OnOwnerChanged(oldOwner, newOwner);
    }

    /// <summary>
    /// Bind a verifier contract to a <c>ProofType</c>. Owner only — the instant bootstrap path.
    /// <para>
    /// This instant owner-witness path has NO timelock and NO council-veto. It exists so a
    /// genuine §16 council-veto path (<see cref="RegisterVerifierViaProposal"/>) coexists with the instant path
    /// only during bring-up. At production launch the operator MUST call
    /// <see cref="LockGovernance"/> — once governance is locked this instant path reverts and
    /// every verifier swap is forced through the council multisig + timelock
    /// (<see cref="RegisterVerifierViaProposal"/>), closing the rogue-owner "swap to a
    /// return-true verifier" hole. Lock is one-way (mirrors
    /// <c>ContractZkVerifier.disableEnvelopeOnlyPermanently</c>).
    /// </para>
    /// </summary>
    public static void RegisterVerifier(byte proofType, UInt160 verifier)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(!IsGovernanceLocked(),
            "governance locked — instant owner path disabled; use RegisterVerifierViaProposal");
        WriteVerifier(proofType, verifier);
    }

    /// <summary>
    /// Permanently disable the instant owner-only <see cref="RegisterVerifier"/> path so all
    /// verifier swaps must go through the council multisig + timelock
    /// (<see cref="RegisterVerifierViaProposal"/>). Owner only; one-way (there is no unlock).
    /// Idempotent — re-locking is a no-op. The GovernanceController must be wired first
    /// (<see cref="SetGovernanceController"/>) so the proposal path is actually usable once
    /// the instant path is closed.
    /// </summary>
    public static void LockGovernance()
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(GetGovernanceController() != UInt160.Zero,
            "wire GovernanceController before locking — else no verifier could ever be registered");
        var key = new byte[] { KeyGovernanceLocked };
        if (Storage.Get(key) == null)
        {
            Storage.Put(key, new byte[] { 1 });
            OnGovernanceLocked();
        }
    }

    /// <summary>True once <see cref="LockGovernance"/> has been called — the instant owner
    /// <see cref="RegisterVerifier"/> path is then permanently disabled.</summary>
    [Safe]
    public static bool IsGovernanceLocked()
    {
        return Storage.Get(new byte[] { KeyGovernanceLocked }) != null;
    }

    /// <summary>Wire the GovernanceController contract hash that
    /// <see cref="RegisterVerifierViaProposal"/> consults for the §16 council-veto +
    /// timelock check. Owner only.</summary>
    public static void SetGovernanceController(UInt160 governanceController)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(governanceController.IsValid && !governanceController.IsZero,
            "invalid governance controller");
        Storage.Put(new byte[] { KeyGovernanceController }, governanceController);
        OnGovernanceControllerChanged(governanceController);
    }

    /// <summary>Look up the wired GovernanceController hash, or <see cref="UInt160.Zero"/>
    /// if not yet set.</summary>
    [Safe]
    public static UInt160 GetGovernanceController()
    {
        var raw = Storage.Get(new byte[] { KeyGovernanceController });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Apply a verifier registration that has been approved by the GovernanceController
    /// council and has cleared the configured timelock — the §16 council-veto path.
    /// Anyone may submit; the proof of authority is the proposal's approval state, not
    /// the caller's witness.
    /// </summary>
    /// <remarks>
    /// Replay-protected per <paramref name="proposalId"/>: a single approved proposal can
    /// be applied at most once. The (proofType, verifier) pair is canonically encoded into
    /// the proposal payload via <see cref="BuildRegisterVerifierAction"/> and bound at
    /// execution time — council members vote on the EXACT args this call uses, not on
    /// opaque bytes that could be repurposed.
    /// </remarks>
    public static void RegisterVerifierViaProposal(byte proofType, UInt160 verifier, ulong proposalId)
    {
        var gc = GetGovernanceController();
        ExecutionEngine.Assert(gc != UInt160.Zero,
            "governance controller not wired — owner must call SetGovernanceController first");

        // Replay protection.
        var consumedKey = new byte[1 + 8];
        consumedKey[0] = PrefixConsumedProposal;
        consumedKey[1] = (byte)proposalId; consumedKey[2] = (byte)(proposalId >> 8);
        consumedKey[3] = (byte)(proposalId >> 16); consumedKey[4] = (byte)(proposalId >> 24);
        consumedKey[5] = (byte)(proposalId >> 32); consumedKey[6] = (byte)(proposalId >> 40);
        consumedKey[7] = (byte)(proposalId >> 48); consumedKey[8] = (byte)(proposalId >> 56);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "proposal already consumed");

        var ok = (bool)Contract.Call(gc, "isApprovedAndTimelocked",
            CallFlags.ReadOnly, new object[] { proposalId });
        ExecutionEngine.Assert(ok,
            "proposal not approved + timelocked (council multisig + timelock not satisfied)");

        // Bind the proposal payload to the action args. Without this, an approved
        // proposal could be applied with ANY (proofType, verifier) — the council
        // vote becomes a one-time blank check at the verifier-dispatch level.
        var expectedAction = BuildRegisterVerifierAction(proofType, verifier);
        var bound = (bool)Contract.Call(gc, "matchesProposalPayload",
            CallFlags.ReadOnly, new object[] { proposalId, expectedAction });
        ExecutionEngine.Assert(bound,
            "proposal payload does not match (proofType, verifier) action args (council voted on different bytes)");

        Storage.Put(consumedKey, new byte[] { 1 });
        WriteVerifier(proofType, verifier);
    }

    /// <summary>
    /// Canonical encoding for a "register verifier" action. The council submits this as
    /// the proposal payload via <see cref="NeoHub.GovernanceController"/>
    /// <c>CreateProposal</c>; <see cref="RegisterVerifierViaProposal"/> re-derives it
    /// from the runtime args and asserts byte-equality. Layout:
    /// <c>"neo4-gov:registerVerifier" || proofType(1B) || verifier(20B)</c> = 46 bytes.
    /// </summary>
    [Safe]
    public static byte[] BuildRegisterVerifierAction(byte proofType, UInt160 verifier)
    {
        var tag = ActionTagRegisterVerifier;
        var buf = new byte[tag.Length + 1 + 20];
        for (var i = 0; i < tag.Length; i++) buf[i] = tag[i];
        buf[tag.Length] = proofType;
        var vk = (byte[])verifier;
        for (var i = 0; i < 20; i++) buf[tag.Length + 1 + i] = vk[i];
        return buf;
    }

    private static readonly byte[] ActionTagRegisterVerifier = new byte[]
    {
        (byte)'n', (byte)'e', (byte)'o', (byte)'4', (byte)'-',
        (byte)'g', (byte)'o', (byte)'v', (byte)':',
        (byte)'r', (byte)'e', (byte)'g', (byte)'i', (byte)'s', (byte)'t', (byte)'e', (byte)'r',
        (byte)'V', (byte)'e', (byte)'r', (byte)'i', (byte)'f', (byte)'i', (byte)'e', (byte)'r'
    };

    private static void WriteVerifier(byte proofType, UInt160 verifier)
    {
        ExecutionEngine.Assert(verifier.IsValid && !verifier.IsZero, "invalid verifier");
        // ProofType range: None(0)/Multisig(1)/Optimistic(2)/Zk(3). Reject 0 because
        // "no proof" can't be verified by anything; reject >3 because off-chain code
        // would reject the same ProofType byte at decode time, so accepting it here
        // would let an operator wire a verifier that's effectively unreachable —
        // confusing later when batches with that ProofType are rejected upstream.
        ExecutionEngine.Assert(proofType >= 1 && proofType <= 3, "proofType must be 1..3 (Multisig/Optimistic/Zk)");
        Storage.Put(new byte[] { PrefixVerifier, proofType }, verifier);
        OnVerifierRegistered(proofType, verifier);
    }

    /// <summary>Look up the registered verifier for a <c>ProofType</c>.</summary>
    [Safe]
    public static UInt160 GetVerifier(byte proofType)
    {
        var raw = Storage.Get(new byte[] { PrefixVerifier, proofType });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Dispatch verification by reading the proofType byte from the commitment and forwarding
    /// to the registered verifier. Returns the verifier's bool result. Used by SettlementManager.
    /// </summary>
    [Safe]
    public static bool VerifyCommitment(byte[] commitmentBytes)
    {
        ExecutionEngine.Assert(commitmentBytes.Length > ProofTypeOffset, "commitment too small");
        var proofType = commitmentBytes[ProofTypeOffset];
        var verifier = GetVerifier(proofType);
        ExecutionEngine.Assert(!verifier.IsZero, "no verifier for proof type");
        return (bool)Contract.Call(verifier, "verify", CallFlags.ReadOnly, new object[] { commitmentBytes });
    }
}
