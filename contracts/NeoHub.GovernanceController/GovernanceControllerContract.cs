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
    private const byte PrefixApprovalCount = 0x09;        // 0x09 + proposalId(8B) → BigInteger count
    private const byte PrefixApprovedVerifier = 0x0A;    // 0x0A + verifierHash(20B) → 1   (§16.1 semi-permissionless gate)
    private const byte PrefixApprovedBridge = 0x0B;      // 0x0B + bridgeHash(20B) → 1     (§16.1 semi-permissionless gate)
    private const byte PrefixApprovedAt = 0x0C;           // 0x0C + proposalId(8B) → BigInteger unix-seconds when threshold first hit
    private const byte PrefixImmutableFlag = 0x0D;        // 0x0D + flagId(1B) → 1 (once set, never cleared — ZKsync PermanentRestriction equivalent)
    private const byte PrefixConsumedSetImmutable = 0x0E; // 0x0E + proposalId(8B) → 1 (replay protection for SetImmutableFlagViaProposal)
    private const byte KeyUpgradeNoticeSeconds = 0x0F;
    private const byte KeyUpgradeExecutionWindowSeconds = 0x10;
    private const byte KeyUpgradeCooldownSeconds = 0x11;
    private const byte PrefixProposalExecutedAt = 0x12;   // 0x12 + proposalId(8B) → Runtime.Time ms
    private const byte KeyOwner = 0xFF;

    public const byte StagePending = 0;
    public const byte StageNotice = 1;
    public const byte StageExecutable = 2;
    public const byte StageCooldown = 3;
    public const byte StageComplete = 4;
    public const byte StageExpired = 5;

    /// <summary>Emitted whenever a proposal is registered.</summary>
    [DisplayName("ProposalCreated")]
    public static event Action<ulong, byte[]> OnProposalCreated = default!;

    /// <summary>Emitted whenever a council member approves a proposal.</summary>
    [DisplayName("ProposalApproved")]
    public static event Action<ulong, ECPoint> OnProposalApproved = default!;

    /// <summary>Emitted the first time an immutable flag is set. Re-setting an already-set flag is a no-op and does not re-emit.</summary>
    [DisplayName("ImmutableFlagSet")]
    public static event Action<byte> OnImmutableFlagSet = default!;

    /// <summary>Emitted when staged-upgrade timing windows are changed.</summary>
    [DisplayName("UpgradeWindowsSet")]
    public static event Action<uint, uint, uint> OnUpgradeWindowsSet = default!;

    /// <summary>Emitted when a proposal is marked executed and enters cool-down.</summary>
    [DisplayName("ProposalExecuted")]
    public static event Action<ulong, ulong> OnProposalExecuted = default!;

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
        Storage.Put(new byte[] { KeyUpgradeNoticeSeconds }, (BigInteger)timelockSeconds);
        Storage.Put(new byte[] { KeyUpgradeExecutionWindowSeconds }, (BigInteger)timelockSeconds);
        Storage.Put(new byte[] { KeyUpgradeCooldownSeconds }, (BigInteger)timelockSeconds);
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
        return IncrementAndCountApprovals(proposalId);
    }

    /// <summary>Look up the encoded payload of a proposal (or empty bytes).</summary>
    [Safe]
    public static byte[] GetProposal(ulong proposalId)
    {
        var raw = Storage.Get(ProposalKey(proposalId));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    /// <summary>
    /// Increment the per-proposal approval counter by 1 and return the new value.
    /// Also records <c>approvedAt</c> on the first crossing of <see cref="GetThreshold"/>
    /// so <see cref="IsApprovedAndTimelocked"/> can enforce the council-veto window.
    /// </summary>
    /// <remarks>
    /// Bump-and-return semantics — NOT a pure read. The pure-read companion is the
    /// <see cref="GetApprovalCount"/> public method (added so wallets / dashboards can
    /// render "M of N approvals" UI without bumping the counter).
    /// </remarks>
    private static uint IncrementAndCountApprovals(ulong proposalId)
    {
        // Iteration over all members would require Find; we track a counter bumped on
        // each new approval instead. The same counter is exposed read-only via
        // GetApprovalCount for UI / observability paths.
        var counterKey = ProposalIdKey(PrefixApprovalCount, proposalId);
        var raw = Storage.Get(counterKey);
        var current = raw == null ? 0u : (uint)(BigInteger)raw;
        var next = current + 1;
        Storage.Put(counterKey, (BigInteger)next);

        // §16-council-veto: when the count first crosses the threshold, record the
        // wall-clock time so IsApprovedAndTimelocked can enforce the configured timelock.
        // Only record on the *first* threshold-reach (current < threshold && next >= threshold)
        // so a later vote past threshold can't reset the timer.
        var threshold = GetThreshold();
        if (threshold > 0 && current < threshold && next >= threshold)
        {
            var approvedAtKey = ProposalIdKey(PrefixApprovedAt, proposalId);
            if (Storage.Get(approvedAtKey) == null)
            {
                // Runtime.Time is in milliseconds since unix epoch; we store the same
                // unit so IsApprovedAndTimelocked can compare directly. Timelock seconds
                // is converted to ms there.
                Storage.Put(approvedAtKey, (BigInteger)Runtime.Time);
            }
        }
        return next;
    }

    /// <summary>
    /// Wall-clock time (Runtime.Time, ms since epoch) at which the proposal first reached
    /// <see cref="GetThreshold"/> approvals. Returns 0 if never crossed. Used by
    /// <see cref="IsApprovedAndTimelocked"/> to gate verifier / bridge upgrade execution.
    /// </summary>
    [Safe]
    public static ulong GetApprovedAt(ulong proposalId)
    {
        var raw = Storage.Get(ProposalIdKey(PrefixApprovedAt, proposalId));
        return raw == null ? 0UL : (ulong)(BigInteger)raw;
    }

    /// <summary>
    /// Current approval count for <paramref name="proposalId"/>. Returns 0 for unknown
    /// proposals. Operators / wallets / dashboards consult this to render
    /// "M of N approvals" UI without bumping the counter.
    /// </summary>
    /// <remarks>
    /// Pure read — does NOT increment. The internal <c>CountApprovals</c> private helper
    /// is misleadingly named (it bumps + returns); this <see cref="GetApprovalCount"/>
    /// is the safe public read.
    /// </remarks>
    [Safe]
    public static uint GetApprovalCount(ulong proposalId)
    {
        var raw = Storage.Get(ProposalIdKey(PrefixApprovalCount, proposalId));
        return raw == null ? 0u : (uint)(BigInteger)raw;
    }

    /// <summary>
    /// True iff the proposal has reached the M-of-N approval threshold AND the configured
    /// timelock has elapsed since first-threshold-reach. Verifier / bridge upgrades on
    /// other contracts call this via <c>Contract.Call</c> as the §16 council-veto gate.
    /// </summary>
    /// <remarks>
    /// Threshold reach without elapsed timelock returns false — that's the window where
    /// the security council can still raise an alarm and revert / re-approve.
    /// </remarks>
    [Safe]
    public static bool IsApprovedAndTimelocked(ulong proposalId)
    {
        var approvedAt = GetApprovedAt(proposalId);
        if (approvedAt == 0UL) return false;
        var timelockSeconds = GetTimelockSeconds();
        // Runtime.Time is ms; timelock is seconds → multiply to compare on the same scale.
        ulong timelockMs = (ulong)timelockSeconds * 1000UL;
        return Runtime.Time >= approvedAt + timelockMs;
    }

    /// <summary>Set staged-upgrade timing windows. Owner only.</summary>
    public static void SetUpgradeWindows(uint noticeSeconds, uint executionWindowSeconds, uint cooldownSeconds)
    {
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        ExecutionEngine.Assert(noticeSeconds > 0, "notice must be positive");
        ExecutionEngine.Assert(executionWindowSeconds > 0, "execution window must be positive");
        ExecutionEngine.Assert(cooldownSeconds > 0, "cooldown must be positive");
        Storage.Put(new byte[] { KeyUpgradeNoticeSeconds }, (BigInteger)noticeSeconds);
        Storage.Put(new byte[] { KeyUpgradeExecutionWindowSeconds }, (BigInteger)executionWindowSeconds);
        Storage.Put(new byte[] { KeyUpgradeCooldownSeconds }, (BigInteger)cooldownSeconds);
        OnUpgradeWindowsSet(noticeSeconds, executionWindowSeconds, cooldownSeconds);
    }

    /// <summary>Notice period, in seconds, after a proposal reaches threshold.</summary>
    [Safe]
    public static uint GetUpgradeNoticeSeconds()
    {
        var raw = Storage.Get(new byte[] { KeyUpgradeNoticeSeconds });
        return raw == null ? GetTimelockSeconds() : (uint)(BigInteger)raw;
    }

    /// <summary>Execution window length, in seconds, after notice completes.</summary>
    [Safe]
    public static uint GetUpgradeExecutionWindowSeconds()
    {
        var raw = Storage.Get(new byte[] { KeyUpgradeExecutionWindowSeconds });
        return raw == null ? GetTimelockSeconds() : (uint)(BigInteger)raw;
    }

    /// <summary>Post-execution cool-down window, in seconds.</summary>
    [Safe]
    public static uint GetUpgradeCooldownSeconds()
    {
        var raw = Storage.Get(new byte[] { KeyUpgradeCooldownSeconds });
        return raw == null ? GetTimelockSeconds() : (uint)(BigInteger)raw;
    }

    /// <summary>Runtime.Time ms at which the proposal was marked executed, or 0.</summary>
    [Safe]
    public static ulong GetProposalExecutedAt(ulong proposalId)
    {
        var raw = Storage.Get(ProposalIdKey(PrefixProposalExecutedAt, proposalId));
        return raw == null ? 0UL : (ulong)(BigInteger)raw;
    }

    /// <summary>
    /// Current staged-upgrade phase:
    /// 0=pending, 1=notice, 2=executable, 3=cooldown, 4=complete, 5=expired.
    /// </summary>
    [Safe]
    public static byte GetProposalStage(ulong proposalId)
    {
        var approvedAt = GetApprovedAt(proposalId);
        if (approvedAt == 0UL) return StagePending;

        var noticeEnd = approvedAt + (ulong)GetUpgradeNoticeSeconds() * 1000UL;
        if (Runtime.Time < noticeEnd) return StageNotice;

        var executedAt = GetProposalExecutedAt(proposalId);
        if (executedAt > 0UL)
        {
            var cooldownEnd = executedAt + (ulong)GetUpgradeCooldownSeconds() * 1000UL;
            return Runtime.Time < cooldownEnd ? StageCooldown : StageComplete;
        }

        var executionEnd = noticeEnd + (ulong)GetUpgradeExecutionWindowSeconds() * 1000UL;
        return Runtime.Time <= executionEnd ? StageExecutable : StageExpired;
    }

    /// <summary>True when a proposal is approved and currently inside its execution window.</summary>
    [Safe]
    public static bool IsInExecutionWindow(ulong proposalId)
    {
        return GetProposalStage(proposalId) == StageExecutable;
    }

    /// <summary>
    /// Mark a proposal executed. Consumers should call this after applying an upgrade,
    /// which starts the explicit cool-down phase and prevents duplicate execution.
    /// </summary>
    public static void MarkProposalExecuted(ulong proposalId)
    {
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        ExecutionEngine.Assert(IsInExecutionWindow(proposalId), "proposal not executable");
        var key = ProposalIdKey(PrefixProposalExecutedAt, proposalId);
        ExecutionEngine.Assert(Storage.Get(key) == null, "proposal already executed");
        Storage.Put(key, (BigInteger)Runtime.Time);
        OnProposalExecuted(proposalId, Runtime.Time);
    }

    private static byte[] ProposalIdKey(byte prefix, ulong id)
    {
        var k = new byte[1 + 8];
        k[0] = prefix;
        k[1] = (byte)id; k[2] = (byte)(id >> 8); k[3] = (byte)(id >> 16); k[4] = (byte)(id >> 24);
        k[5] = (byte)(id >> 32); k[6] = (byte)(id >> 40); k[7] = (byte)(id >> 48); k[8] = (byte)(id >> 56);
        return k;
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

    /// <summary>
    /// Set a permanent restriction flag. Once set, the flag can NEVER be cleared — this
    /// is the on-chain equivalent of ZKsync's <c>PermanentRestriction</c> mechanism. Use
    /// for invariants that must hold for the lifetime of the chain (e.g. "this chain can
    /// never switch DAMode away from Rollup" once published).
    /// </summary>
    /// <param name="flagId">Operator-defined 1-byte flag identifier. Storage layout pins
    /// it as a single byte so the namespace is exhaustively enumerable on inspection.</param>
    /// <remarks>
    /// Idempotent — calling twice is allowed (no-op the second time). Storage is
    /// write-only: there is no <c>ClearImmutableFlag</c> entry-point. Owner-witness
    /// gated; the council-veto path is <see cref="SetImmutableFlagViaProposal"/>.
    /// </remarks>
    public static void SetImmutableFlag(byte flagId)
    {
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        var key = ImmutableFlagKey(flagId);
        if (Storage.Get(key) == null)
        {
            Storage.Put(key, new byte[] { 1 });
            OnImmutableFlagSet(flagId);
        }
    }

    /// <summary>
    /// Council-veto path for setting a permanent restriction. Same shape as
    /// <see cref="RegisterCommitteeViaProposal"/> in the external-bridge stack:
    /// the proposalId must be approved + timelocked, then this call is replay-protected
    /// per proposalId. For high-stakes immutability decisions (e.g. locking the
    /// security model permanently) the council path is preferred over owner-only.
    /// </summary>
    public static void SetImmutableFlagViaProposal(byte flagId, ulong proposalId)
    {
        var consumedKey = ProposalIdKey(PrefixConsumedSetImmutable, proposalId);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "proposal already consumed");
        ExecutionEngine.Assert(IsApprovedAndTimelocked(proposalId), "proposal not approved + timelocked");
        // Bind the proposal payload to the action args. Without this, an approved
        // proposal could be executed with ANY flagId — the council vote becomes
        // a one-time blank check. The payload format is domain-separated by an
        // action-id prefix so the same byte sequence can't be repurposed across
        // *ViaProposal methods (e.g. registerVerifier proposal accidentally executed
        // as setImmutableFlag because the byte tails coincidentally match).
        AssertProposalBinding(proposalId, BuildSetImmutableFlagAction(flagId));
        Storage.Put(consumedKey, new byte[] { 1 });
        var key = ImmutableFlagKey(flagId);
        if (Storage.Get(key) == null)
        {
            Storage.Put(key, new byte[] { 1 });
            OnImmutableFlagSet(flagId);
        }
    }

    /// <summary>
    /// Canonical encoding for a "set immutable flag" action — what the council votes on
    /// when they create a proposal that <see cref="SetImmutableFlagViaProposal"/> will
    /// execute. Off-chain tooling computes this and submits it as the proposal payload
    /// via <see cref="CreateProposal"/>. The execution call then re-derives the same
    /// bytes from its args and asserts they match the stored payload — so council
    /// members can't be tricked into "approving" a payload that's actually some other
    /// action's args.
    /// </summary>
    /// <remarks>
    /// Layout: <c>"neo4-gov:setImmutableFlag\x01" || flagId(1B)</c> = 26 bytes.
    /// The "neo4-gov:" prefix prevents collisions with payloads from other *ViaProposal
    /// methods (each uses a distinct method id) and with arbitrary payloads council
    /// members might paste from elsewhere.
    /// </remarks>
    [Safe]
    public static byte[] BuildSetImmutableFlagAction(byte flagId)
    {
        // 25-byte prefix tag + 1-byte arg. Tag is the literal ASCII of
        // "neo4-gov:setImmutableFlag" (no terminating null in the contract bytes).
        // Length is fixed so equality compare is O(1) for the most common path.
        var buf = new byte[ActionTagSetImmutableFlag.Length + 1];
        for (var i = 0; i < ActionTagSetImmutableFlag.Length; i++) buf[i] = ActionTagSetImmutableFlag[i];
        buf[ActionTagSetImmutableFlag.Length] = flagId;
        return buf;
    }

    /// <summary>
    /// Read the stored payload for <paramref name="proposalId"/> and assert it equals
    /// <paramref name="expectedAction"/> byte-for-byte. Each *ViaProposal method calls
    /// this with its locally-canonicalized action bytes; mismatched length / content
    /// reverts before any state mutation happens.
    /// </summary>
    [Safe]
    public static bool MatchesProposalPayload(ulong proposalId, byte[] expectedAction)
    {
        var stored = Storage.Get(ProposalKey(proposalId));
        if (stored == null) return false;
        var bytes = (byte[])stored;
        if (bytes.Length != expectedAction.Length) return false;
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] != expectedAction[i]) return false;
        return true;
    }

    private static void AssertProposalBinding(ulong proposalId, byte[] expectedAction)
    {
        ExecutionEngine.Assert(MatchesProposalPayload(proposalId, expectedAction),
            "proposal payload does not match action args (council voted on different bytes)");
    }

    // ASCII bytes for the "neo4-gov:setImmutableFlag" action tag. Kept as a const
    // byte[] (not a string) so the on-chain bytecode is the literal byte sequence
    // and reads avoid string-encoding nuances. Length must match the comment in
    // BuildSetImmutableFlagAction's Layout note.
    private static readonly byte[] ActionTagSetImmutableFlag = new byte[]
    {
        (byte)'n', (byte)'e', (byte)'o', (byte)'4', (byte)'-',
        (byte)'g', (byte)'o', (byte)'v', (byte)':',
        (byte)'s', (byte)'e', (byte)'t', (byte)'I', (byte)'m', (byte)'m', (byte)'u', (byte)'t', (byte)'a', (byte)'b', (byte)'l', (byte)'e',
        (byte)'F', (byte)'l', (byte)'a', (byte)'g'
    };

    /// <summary>True if <paramref name="flagId"/> has been set as a permanent restriction.</summary>
    [Safe]
    public static bool IsImmutable(byte flagId)
    {
        return Storage.Get(ImmutableFlagKey(flagId)) != null;
    }

    private static byte[] ImmutableFlagKey(byte flagId)
    {
        return new byte[] { PrefixImmutableFlag, flagId };
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
