using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.GovernanceController;

/// <summary>
/// L1 governance controller. Holds the council, verifier-upgrade timelock, L2 admission
/// policy, and proposal lifecycle for cross-cutting NeoHub changes. See doc.md §16
/// (Governance) and §17 (verifier upgrade attack mitigations).
/// </summary>
[DisplayName("NeoHub.GovernanceController")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Governance controller for the Neo Elastic Network: council, timelocks, admission policy.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.GovernanceController")]
[ContractPermission(Permission.Any, Method.Any)]
public class GovernanceControllerContract : SmartContract
{
    private const byte PrefixCouncilMember = 0x01;       // 0x01 + memberKey(33B) → 1
    private const byte KeyCouncilCount = 0x02;
    private const byte KeyCouncilThreshold = 0x03;
    private const byte KeyTimelockSeconds = 0x04;
    private const byte KeyAdmissionMode = 0x05;          // 0=permissioned 1=semi-permissionless 2=permissionless
    private const byte PrefixProposal = 0x06;             // 0x06 + proposalId(8B) → encoded proposal
    private const byte PrefixApproval = 0x07;             // 0x07 + proposalId(8B) + memberKey(33B) → 1
    private const byte KeyNextProposalId = 0x08;
    private const byte PrefixApprovedVerifier = 0x0A;    // 0x0A + verifierHash(20B) → 1   (§16.1 semi-permissionless gate)
    private const byte PrefixApprovedBridge = 0x0B;      // 0x0B + bridgeHash(20B) → 1     (§16.1 semi-permissionless gate)
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted whenever a proposal is registered.</summary>
    [DisplayName("ProposalCreated")]
    public static event Action<ulong, byte[]> OnProposalCreated = default!;

    /// <summary>Emitted whenever a council member approves a proposal.</summary>
    [DisplayName("ProposalApproved")]
    public static event Action<ulong, ECPoint> OnProposalApproved = default!;

    /// <summary>Set initial council + thresholds at deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var members = (ECPoint[])arr[1];
        var threshold = (uint)(BigInteger)arr[2];
        var timelockSeconds = (uint)(BigInteger)arr[3];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(members.Length > 0, "council must be non-empty");
        ExecutionEngine.Assert(threshold > 0 && threshold <= members.Length, "bad threshold");
        // A zero timelock means proposals execute instantly — defeats the point of a
        // timelock. Surface the misconfig at deploy time.
        ExecutionEngine.Assert(timelockSeconds > 0, "timelock must be positive");

        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyCouncilCount }, (BigInteger)members.Length);
        Storage.Put(new byte[] { KeyCouncilThreshold }, (BigInteger)threshold);
        Storage.Put(new byte[] { KeyTimelockSeconds }, (BigInteger)timelockSeconds);
        Storage.Put(new byte[] { KeyAdmissionMode }, new byte[] { 0 }); // start permissioned
        Storage.Put(new byte[] { KeyNextProposalId }, (BigInteger)1);

        for (var i = 0; i < members.Length; i++)
        {
            var key = new byte[1 + 33];
            key[0] = PrefixCouncilMember;
            var pk = (byte[])members[i];
            for (var j = 0; j < 33; j++) key[1 + j] = pk[j];
            Storage.Put(key, new byte[] { 1 });
        }
    }

    /// <summary>True if the given key is a current council member.</summary>
    [Safe]
    public static bool IsCouncilMember(ECPoint memberKey)
    {
        var key = new byte[1 + 33];
        key[0] = PrefixCouncilMember;
        var pk = (byte[])memberKey;
        for (var j = 0; j < 33; j++) key[1 + j] = pk[j];
        return Storage.Get(key) != null;
    }

    /// <summary>Number of registered council members.</summary>
    [Safe]
    public static uint GetCouncilCount()
    {
        var raw = Storage.Get(new byte[] { KeyCouncilCount });
        return raw == null ? 0u : (uint)(BigInteger)raw;
    }

    /// <summary>M-of-N threshold of approvals required to execute a proposal.</summary>
    [Safe]
    public static uint GetThreshold()
    {
        var raw = Storage.Get(new byte[] { KeyCouncilThreshold });
        return raw == null ? 0u : (uint)(BigInteger)raw;
    }

    /// <summary>Timelock applied to verifier and bridge upgrades, in seconds.</summary>
    [Safe]
    public static uint GetTimelockSeconds()
    {
        var raw = Storage.Get(new byte[] { KeyTimelockSeconds });
        return raw == null ? 0u : (uint)(BigInteger)raw;
    }

    /// <summary>L2 admission policy: 0=permissioned, 1=semi-permissionless, 2=permissionless.</summary>
    [Safe]
    public static byte GetAdmissionMode()
    {
        var raw = Storage.Get(new byte[] { KeyAdmissionMode });
        return raw == null ? (byte)0 : ((byte[])raw)[0];
    }

    /// <summary>Update the admission mode. Owner only.</summary>
    public static void SetAdmissionMode(byte mode)
    {
        ExecutionEngine.Assert(mode <= 2, "invalid admission mode");
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        Storage.Put(new byte[] { KeyAdmissionMode }, new byte[] { mode });
    }

    /// <summary>Council member submits a proposal payload (opaque bytes, semantics owned by caller).</summary>
    public static ulong CreateProposal(ECPoint signer, byte[] payload)
    {
        ExecutionEngine.Assert(IsCouncilMember(signer), "not a council member");
        ExecutionEngine.Assert(Runtime.CheckWitness(signer), "no witness");
        // An empty payload is meaningless — Approve() can't tell what's being voted on.
        // Without this guard, a council member typo could waste a proposal id and
        // collect approvals for "vote on nothing".
        ExecutionEngine.Assert(payload.Length > 0, "empty proposal payload");
        var idRaw = Storage.Get(new byte[] { KeyNextProposalId });
        var id = idRaw == null ? 1UL : (ulong)(BigInteger)idRaw;
        Storage.Put(new byte[] { KeyNextProposalId }, (BigInteger)(id + 1));
        Storage.Put(ProposalKey(id), payload);
        OnProposalCreated(id, payload);
        return id;
    }

    /// <summary>Approve an existing proposal. One vote per member.</summary>
    public static uint Approve(ulong proposalId, ECPoint memberKey)
    {
        ExecutionEngine.Assert(IsCouncilMember(memberKey), "not a council member");
        ExecutionEngine.Assert(Runtime.CheckWitness(memberKey), "no witness");
        ExecutionEngine.Assert(Storage.Get(ProposalKey(proposalId)) != null, "unknown proposal");

        var aKey = ApprovalKey(proposalId, memberKey);
        ExecutionEngine.Assert(Storage.Get(aKey) == null, "already approved");
        Storage.Put(aKey, new byte[] { 1 });
        OnProposalApproved(proposalId, memberKey);
        return CountApprovals(proposalId);
    }

    /// <summary>Look up the encoded payload of a proposal (or empty bytes).</summary>
    [Safe]
    public static byte[] GetProposal(ulong proposalId)
    {
        var raw = Storage.Get(ProposalKey(proposalId));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    private static uint CountApprovals(ulong proposalId)
    {
        // Iteration over all members would require Find; for MVP we just track a counter
        // bumped on each new approval. Production deploys add a dedicated index.
        var counterKey = new byte[1 + 8];
        counterKey[0] = 0x09;
        counterKey[1] = (byte)proposalId; counterKey[2] = (byte)(proposalId >> 8); counterKey[3] = (byte)(proposalId >> 16); counterKey[4] = (byte)(proposalId >> 24);
        counterKey[5] = (byte)(proposalId >> 32); counterKey[6] = (byte)(proposalId >> 40); counterKey[7] = (byte)(proposalId >> 48); counterKey[8] = (byte)(proposalId >> 56);
        var raw = Storage.Get(counterKey);
        var current = raw == null ? 0u : (uint)(BigInteger)raw;
        var next = current + 1;
        Storage.Put(counterKey, (BigInteger)next);
        return next;
    }

    private static byte[] ProposalKey(ulong id)
    {
        var k = new byte[1 + 8];
        k[0] = PrefixProposal;
        k[1] = (byte)id; k[2] = (byte)(id >> 8); k[3] = (byte)(id >> 16); k[4] = (byte)(id >> 24);
        k[5] = (byte)(id >> 32); k[6] = (byte)(id >> 40); k[7] = (byte)(id >> 48); k[8] = (byte)(id >> 56);
        return k;
    }

    private static byte[] ApprovalKey(ulong id, ECPoint memberKey)
    {
        var k = new byte[1 + 8 + 33];
        k[0] = PrefixApproval;
        k[1] = (byte)id; k[2] = (byte)(id >> 8); k[3] = (byte)(id >> 16); k[4] = (byte)(id >> 24);
        k[5] = (byte)(id >> 32); k[6] = (byte)(id >> 40); k[7] = (byte)(id >> 48); k[8] = (byte)(id >> 56);
        var pk = (byte[])memberKey;
        for (var j = 0; j < 33; j++) k[9 + j] = pk[j];
        return k;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §16.1 semi-permissionless admission gate: approved verifier + bridge sets
    //
    // ChainRegistry.RegisterChainPublic consults these sets when admission mode
    // is 1 (semi-permissionless). An L2 can register without owner approval if
    // its declared verifier + bridgeAdapter both appear in the approved sets.
    // The owner curates the sets — this is the operational lever that lets
    // governance say "we trust these verifier implementations and these bridge
    // adapters, anyone using them is welcome."
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Approve a verifier contract for §16.1 semi-permissionless admission. Owner only.</summary>
    public static void ApproveVerifier(UInt160 verifier)
    {
        ExecutionEngine.Assert(verifier.IsValid && !verifier.IsZero, "invalid verifier");
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        Storage.Put(VerifierKey(verifier), new byte[] { 1 });
    }

    /// <summary>Revoke a previously-approved verifier. Owner only. Does not affect chains
    /// already registered under this verifier — only future <c>RegisterChainPublic</c> calls.</summary>
    public static void RevokeVerifier(UInt160 verifier)
    {
        ExecutionEngine.Assert(verifier.IsValid && !verifier.IsZero, "invalid verifier");
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        Storage.Delete(VerifierKey(verifier));
    }

    /// <summary>True if <paramref name="verifier"/> is in the approved-verifier set.</summary>
    [Safe]
    public static bool IsApprovedVerifier(UInt160 verifier)
    {
        return Storage.Get(VerifierKey(verifier)) != null;
    }

    /// <summary>Approve a bridge-adapter contract for §16.1 semi-permissionless admission. Owner only.</summary>
    public static void ApproveBridgeAdapter(UInt160 bridge)
    {
        ExecutionEngine.Assert(bridge.IsValid && !bridge.IsZero, "invalid bridge");
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        Storage.Put(BridgeKey(bridge), new byte[] { 1 });
    }

    /// <summary>Revoke a previously-approved bridge adapter. Owner only.</summary>
    public static void RevokeBridgeAdapter(UInt160 bridge)
    {
        ExecutionEngine.Assert(bridge.IsValid && !bridge.IsZero, "invalid bridge");
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        Storage.Delete(BridgeKey(bridge));
    }

    /// <summary>True if <paramref name="bridge"/> is in the approved-bridge-adapter set.</summary>
    [Safe]
    public static bool IsApprovedBridgeAdapter(UInt160 bridge)
    {
        return Storage.Get(BridgeKey(bridge)) != null;
    }

    private static byte[] VerifierKey(UInt160 verifier)
    {
        var k = new byte[1 + 20];
        k[0] = PrefixApprovedVerifier;
        var b = (byte[])verifier;
        for (var i = 0; i < 20; i++) k[1 + i] = b[i];
        return k;
    }

    private static byte[] BridgeKey(UInt160 bridge)
    {
        var k = new byte[1 + 20];
        k[0] = PrefixApprovedBridge;
        var b = (byte[])bridge;
        for (var i = 0; i < 20; i++) k[1 + i] = b[i];
        return k;
    }
}
