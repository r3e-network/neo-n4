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
    private const byte PrefixConsumedSetAdmissionMode = 0x13; // 0x13 + proposalId(8B) → 1 (replay protection for SetAdmissionModeViaProposal)
    private const byte PrefixProposalVetoed = 0x14;        // 0x14 + proposalId(8B) → 1 (council/owner brake; IsApprovedAndTimelocked returns false once set)
    private const byte KeyCouncilEpoch = 0x15;
    private const byte PrefixProposalEpoch = 0x16;         // 0x16 + proposalId(8B) → council epoch at creation
    private const byte PrefixConsumedRotateCouncil = 0x17; // 0x17 + proposalId(8B) → 1
    private const byte KeyOwner = 0xFF;

    private const uint MaxCouncilMembers = 64;
    private const ulong InitialCouncilEpoch = 1;

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

    /// <summary>Emitted the first time a proposal is vetoed via <c>CancelProposal</c>. Re-vetoing is a no-op and does not re-emit.</summary>
    [DisplayName("ProposalVetoed")]
    public static event Action<ulong> OnProposalVetoed = default!;

    /// <summary>Emitted after an atomic council rotation advances the governance epoch.</summary>
    [DisplayName("CouncilRotated")]
    public static event Action<ulong, ulong, ulong, uint, uint> OnCouncilRotated = default!;

    /// <summary>Emitted when the admission mode changes.</summary>
    [DisplayName("AdmissionModeChanged")]
    public static event Action<byte> OnAdmissionModeChanged = default!;

    /// <summary>Emitted when a verifier is approved for semi-permissionless admission.</summary>
    [DisplayName("VerifierApproved")]
    public static event Action<UInt160> OnVerifierApproved = default!;

    /// <summary>Emitted when a verifier is revoked from the semi-permissionless set.</summary>
    [DisplayName("VerifierRevoked")]
    public static event Action<UInt160> OnVerifierRevoked = default!;

    /// <summary>Emitted when a bridge adapter is approved for semi-permissionless admission.</summary>
    [DisplayName("BridgeAdapterApproved")]
    public static event Action<UInt160> OnBridgeAdapterApproved = default!;

    /// <summary>Emitted when a bridge adapter is revoked from the semi-permissionless set.</summary>
    [DisplayName("BridgeAdapterRevoked")]
    public static event Action<UInt160> OnBridgeAdapterRevoked = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    /// <summary>
    /// Set the initial council, threshold, timelock, and governance epoch. Council replacement is
    /// available only through the epoch-bound, approved, timelocked <see cref="RotateCouncil"/>
    /// operation. Timelock remains deployment-fixed.
    /// </summary>
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
        ExecutionEngine.Assert(members.Length <= MaxCouncilMembers, "council exceeds maximum size");
        ExecutionEngine.Assert(threshold > 0, "threshold must be positive");
        ExecutionEngine.Assert(threshold <= members.Length, "threshold exceeds committee size");
        // A zero timelock means proposals execute instantly — defeats the point of a
        // timelock. Surface the misconfig at deploy time.
        ExecutionEngine.Assert(timelockSeconds > 0, "timelock must be positive");
        AssertDistinctMembers(members);

        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyCouncilCount }, (BigInteger)members.Length);
        Storage.Put(new byte[] { KeyCouncilThreshold }, (BigInteger)threshold);
        Storage.Put(new byte[] { KeyTimelockSeconds }, (BigInteger)timelockSeconds);
        Storage.Put(new byte[] { KeyUpgradeNoticeSeconds }, (BigInteger)timelockSeconds);
        Storage.Put(new byte[] { KeyUpgradeExecutionWindowSeconds }, (BigInteger)timelockSeconds);
        Storage.Put(new byte[] { KeyUpgradeCooldownSeconds }, (BigInteger)timelockSeconds);
        Storage.Put(new byte[] { KeyAdmissionMode }, new byte[] { 0 }); // start permissioned
        Storage.Put(new byte[] { KeyNextProposalId }, (BigInteger)1);
        Storage.Put(new byte[] { KeyCouncilEpoch }, (BigInteger)InitialCouncilEpoch);

        for (var i = 0; i < members.Length; i++)
            Storage.Put(CouncilMemberKey(members[i]), new byte[] { 1 });
    }

    /// <summary>Owner — controls admission policy, upgrade windows, and allowlisted verifier/bridge sets.</summary>
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

    /// <summary>True if the given key is a current council member.</summary>
    [Safe]
    public static bool IsCouncilMember(ECPoint memberKey)
    {
        return Storage.Get(CouncilMemberKey(memberKey)) != null;
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

    /// <summary>Monotonic council generation. Every successful rotation increments it once.</summary>
    [Safe]
    public static ulong GetCouncilEpoch()
    {
        var raw = Storage.Get(new byte[] { KeyCouncilEpoch });
        return raw == null ? InitialCouncilEpoch : (ulong)(BigInteger)raw;
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

    /// <summary>
    /// Tighten the admission mode. Owner only. The instant owner-witness path may only make
    /// admission MORE restrictive (lower or equal numeric mode: 2=permissionless &gt;
    /// 1=semi-permissionless &gt; 0=permissioned) — a compromised or rogue owner key cannot
    /// instantly open the network up. Loosening admission (e.g. switching to permissionless)
    /// is security-relevant and must go through the council-gated, timelocked
    /// <see cref="SetAdmissionModeViaProposal"/> path, consistent with the treatment of
    /// other security-relevant upgrades (verifier swaps, immutable-flag locks).
    /// </summary>
    public static void SetAdmissionMode(byte mode)
    {
        ExecutionEngine.Assert(mode <= 2, "invalid admission mode");
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        // Only allow tightening (toward permissioned). Loosening must go through the
        // approved + timelocked proposal path so opening admission is council-gated + delayed.
        ExecutionEngine.Assert(mode <= GetAdmissionMode(),
            "instant SetAdmissionMode may only tighten admission; use SetAdmissionModeViaProposal to loosen");
        Storage.Put(new byte[] { KeyAdmissionMode }, new byte[] { mode });
        OnAdmissionModeChanged(mode);
    }

    /// <summary>
    /// Council-veto path for changing the admission mode (the only way to LOOSEN admission,
    /// e.g. switch to semi-permissionless or permissionless). The proposalId must be
    /// approved by the council multisig AND have cleared the timelock; the call is then
    /// replay-protected per proposalId and bound to the exact target mode so council
    /// members vote on the precise transition, not opaque bytes.
    /// </summary>
    public static void SetAdmissionModeViaProposal(byte mode, ulong proposalId)
    {
        ExecutionEngine.Assert(mode <= 2, "invalid admission mode");
        var consumedKey = ProposalIdKey(PrefixConsumedSetAdmissionMode, proposalId);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "proposal already consumed");
        ExecutionEngine.Assert(IsApprovedAndTimelocked(proposalId), "proposal not approved + timelocked");
        // Bind the proposal payload to the target mode. Without this, an approved proposal
        // could be applied with ANY mode — the council vote becomes a one-time blank check.
        AssertProposalBinding(proposalId, BuildSetAdmissionModeAction(mode));
        Storage.Put(consumedKey, new byte[] { 1 });
        Storage.Put(new byte[] { KeyAdmissionMode }, new byte[] { mode });
        OnAdmissionModeChanged(mode);
    }

    /// <summary>
    /// Canonical encoding for a "set admission mode" action — what the council votes on when
    /// they create the proposal that <see cref="SetAdmissionModeViaProposal"/> executes.
    /// Off-chain tooling computes this and submits it as the proposal payload via
    /// <see cref="CreateProposal"/>; the execution call re-derives the same bytes from its
    /// args and asserts byte-equality. Layout:
    /// <c>"neo4-gov:setAdmissionMode" || mode(1B)</c> = 26 bytes. The "neo4-gov:" prefix +
    /// distinct method id prevents cross-method payload reuse.
    /// </summary>
    [Safe]
    public static byte[] BuildSetAdmissionModeAction(byte mode)
    {
        var buf = new byte[ActionTagSetAdmissionMode.Length + 1];
        for (var i = 0; i < ActionTagSetAdmissionMode.Length; i++) buf[i] = ActionTagSetAdmissionMode[i];
        buf[ActionTagSetAdmissionMode.Length] = mode;
        return buf;
    }

    // ASCII bytes for the "neo4-gov:setAdmissionMode" action tag (25 bytes). Kept as a const
    // byte[] (not a string) so the on-chain bytecode is the literal byte sequence — same
    // idiom as ActionTagSetImmutableFlag.
    private static readonly byte[] ActionTagSetAdmissionMode = new byte[]
    {
        (byte)'n', (byte)'e', (byte)'o', (byte)'4', (byte)'-',
        (byte)'g', (byte)'o', (byte)'v', (byte)':',
        (byte)'s', (byte)'e', (byte)'t', (byte)'A', (byte)'d', (byte)'m', (byte)'i', (byte)'s', (byte)'s', (byte)'i', (byte)'o', (byte)'n',
        (byte)'M', (byte)'o', (byte)'d', (byte)'e'
    };

    private static readonly byte[] ActionTagRotateCouncil = new byte[]
    {
        (byte)'n', (byte)'e', (byte)'o', (byte)'4', (byte)'-',
        (byte)'g', (byte)'o', (byte)'v', (byte)':',
        (byte)'r', (byte)'o', (byte)'t', (byte)'a', (byte)'t', (byte)'e',
        (byte)'C', (byte)'o', (byte)'u', (byte)'n', (byte)'c', (byte)'i', (byte)'l',
        (byte)':', (byte)'v', (byte)'1'
    };

    /// <summary>
    /// Atomically replace the complete council set and threshold after the current council's
    /// epoch-bound proposal has reached threshold and cleared the timelock.
    /// </summary>
    public static void RotateCouncil(
        ECPoint[] oldMembers,
        ECPoint[] newMembers,
        uint newThreshold,
        ulong proposalId)
    {
        var consumedKey = ProposalIdKey(PrefixConsumedRotateCouncil, proposalId);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "proposal already consumed");

        var currentCount = GetCouncilCount();
        ExecutionEngine.Assert(oldMembers.Length == currentCount,
            "oldMembers must be the complete current council");
        ExecutionEngine.Assert(oldMembers.Length > 0, "old council must be non-empty");
        ExecutionEngine.Assert(oldMembers.Length <= MaxCouncilMembers, "old council exceeds maximum size");
        AssertDistinctMembers(oldMembers);
        for (var i = 0; i < oldMembers.Length; i++)
            ExecutionEngine.Assert(IsCouncilMember(oldMembers[i]), "oldMembers is not the current council");

        ExecutionEngine.Assert(newMembers.Length > 0, "new council must be non-empty");
        ExecutionEngine.Assert(newMembers.Length <= MaxCouncilMembers, "new council exceeds maximum size");
        ExecutionEngine.Assert(newThreshold > 0, "threshold must be positive");
        ExecutionEngine.Assert(newThreshold <= newMembers.Length, "threshold exceeds new council size");
        AssertDistinctMembers(newMembers);

        var currentEpoch = GetCouncilEpoch();
        ExecutionEngine.Assert(currentEpoch < ulong.MaxValue, "council epoch exhausted");
        ExecutionEngine.Assert(GetProposalEpoch(proposalId) == currentEpoch, "proposal council epoch expired");
        ExecutionEngine.Assert(IsApprovedAndTimelocked(proposalId), "proposal not approved + timelocked");
        AssertProposalBinding(proposalId, BuildRotateCouncilAction(newMembers, newThreshold));

        Storage.Put(consumedKey, new byte[] { 1 });
        for (var i = 0; i < oldMembers.Length; i++)
            Storage.Delete(CouncilMemberKey(oldMembers[i]));
        for (var i = 0; i < newMembers.Length; i++)
            Storage.Put(CouncilMemberKey(newMembers[i]), new byte[] { 1 });

        var newEpoch = currentEpoch + 1;
        Storage.Put(new byte[] { KeyCouncilCount }, (BigInteger)newMembers.Length);
        Storage.Put(new byte[] { KeyCouncilThreshold }, (BigInteger)newThreshold);
        Storage.Put(new byte[] { KeyCouncilEpoch }, (BigInteger)newEpoch);
        OnCouncilRotated(proposalId, currentEpoch, newEpoch, (uint)newMembers.Length, newThreshold);
    }

    /// <summary>
    /// Build the canonical versioned rotation action for the current epoch. Layout:
    /// <c>tag || epoch(8B LE) || threshold(4B LE) || count(4B LE) || ordered member keys</c>.
    /// </summary>
    [Safe]
    public static byte[] BuildRotateCouncilAction(ECPoint[] newMembers, uint newThreshold)
    {
        ExecutionEngine.Assert(newMembers.Length > 0, "new council must be non-empty");
        ExecutionEngine.Assert(newMembers.Length <= MaxCouncilMembers, "new council exceeds maximum size");
        ExecutionEngine.Assert(newThreshold > 0, "threshold must be positive");
        ExecutionEngine.Assert(newThreshold <= newMembers.Length, "threshold exceeds new council size");
        AssertDistinctMembers(newMembers);

        var buf = new byte[ActionTagRotateCouncil.Length + 8 + 4 + 4 + newMembers.Length * 33];
        var pos = 0;
        for (var i = 0; i < ActionTagRotateCouncil.Length; i++) buf[pos++] = ActionTagRotateCouncil[i];

        var epoch = GetCouncilEpoch();
        WriteUInt64LittleEndian(buf, pos, epoch);
        pos += 8;
        WriteUInt32LittleEndian(buf, pos, newThreshold);
        pos += 4;
        WriteUInt32LittleEndian(buf, pos, (uint)newMembers.Length);
        pos += 4;

        for (var i = 0; i < newMembers.Length; i++)
        {
            var member = (byte[])newMembers[i];
            for (var j = 0; j < 33; j++) buf[pos++] = member[j];
        }
        return buf;
    }

    /// <summary>Council member submits a proposal payload (opaque bytes, semantics owned by caller).</summary>
    public static ulong CreateProposal(ECPoint signer, byte[] payload)
    {
        ExecutionEngine.Assert(IsCouncilMember(signer), "not a council member");
        ExecutionEngine.Assert(Runtime.CheckWitness(signer), "not authorized");
        // An empty payload is meaningless — Approve() can't tell what's being voted on.
        // Without this guard, a council member typo could waste a proposal id and
        // collect approvals for "vote on nothing".
        ExecutionEngine.Assert(payload.Length > 0, "empty proposal payload");
        var idRaw = Storage.Get(new byte[] { KeyNextProposalId });
        var id = idRaw == null ? 1UL : (ulong)(BigInteger)idRaw;
        ExecutionEngine.Assert(id < ulong.MaxValue, "proposal id exhausted");
        Storage.Put(new byte[] { KeyNextProposalId }, (BigInteger)(id + 1));
        Storage.Put(ProposalKey(id), payload);
        Storage.Put(ProposalIdKey(PrefixProposalEpoch, id), (BigInteger)GetCouncilEpoch());
        OnProposalCreated(id, payload);
        return id;
    }

    /// <summary>Approve an existing proposal. One vote per member.</summary>
    public static uint Approve(ulong proposalId, ECPoint memberKey)
    {
        ExecutionEngine.Assert(Storage.Get(ProposalKey(proposalId)) != null, "unknown proposal");
        ExecutionEngine.Assert(GetProposalEpoch(proposalId) == GetCouncilEpoch(), "proposal council epoch expired");
        ExecutionEngine.Assert(IsCouncilMember(memberKey), "not a council member");
        ExecutionEngine.Assert(Runtime.CheckWitness(memberKey), "not authorized");

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

    /// <summary>Council epoch captured when the proposal was created, or zero if unknown.</summary>
    [Safe]
    public static ulong GetProposalEpoch(ulong proposalId)
    {
        var raw = Storage.Get(ProposalIdKey(PrefixProposalEpoch, proposalId));
        return raw == null ? 0UL : (ulong)(BigInteger)raw;
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
    /// timelock has elapsed since first-threshold-reach AND it has not been vetoed. Verifier /
    /// bridge upgrades on other contracts call this via <c>Contract.Call</c> as the §16
    /// council-veto gate.
    /// </summary>
    /// <remarks>
    /// Threshold reach without elapsed timelock returns false — that's the delay window. The
    /// timelock is a pure delay, but it is NOT inert: during (and after) it the governance
    /// owner can call <see cref="CancelProposal"/> to veto an approved-but-malicious proposal,
    /// which permanently flips this method to false for that proposalId. So the window is the
    /// on-chain brake the security council uses to stop a bad-but-approved proposal before it
    /// executes.
    /// </remarks>
    [Safe]
    public static bool IsApprovedAndTimelocked(ulong proposalId)
    {
        if (GetProposalEpoch(proposalId) != GetCouncilEpoch()) return false;
        if (IsVetoed(proposalId)) return false;
        var approvedAt = GetApprovedAt(proposalId);
        if (approvedAt == 0UL) return false;
        var timelockSeconds = GetTimelockSeconds();
        // Runtime.Time is ms; timelock is seconds → multiply to compare on the same scale.
        ulong timelockMs = (ulong)timelockSeconds * 1000UL;
        return Runtime.Time >= approvedAt + timelockMs;
    }

    /// <summary>
    /// Veto / cancel an approved-but-not-yet-executed proposal. Governance owner only — the
    /// on-chain brake referenced by <see cref="IsApprovedAndTimelocked"/>. Once vetoed, a
    /// proposalId can never satisfy the timelock gate, so any *ViaProposal consumer that
    /// re-checks <c>isApprovedAndTimelocked</c> (verifier swap, immutable-flag lock, admission
    /// loosening, external bridge upgrades) will refuse to apply it. The veto is permanent and
    /// replay-safe: re-vetoing is a no-op. A vetoed proposal cannot be revived; the council
    /// must create a fresh proposal to re-attempt the action.
    /// </summary>
    public static void CancelProposal(ulong proposalId)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(Storage.Get(ProposalKey(proposalId)) != null, "unknown proposal");
        // Refuse to veto a proposal that already executed — the cool-down / completed state
        // is terminal and a late veto would be misleading.
        ExecutionEngine.Assert(GetProposalExecutedAt(proposalId) == 0UL, "proposal already executed");
        var key = ProposalIdKey(PrefixProposalVetoed, proposalId);
        if (Storage.Get(key) == null)
        {
            Storage.Put(key, new byte[] { 1 });
            OnProposalVetoed(proposalId);
        }
    }

    /// <summary>True if <paramref name="proposalId"/> has been vetoed via <see cref="CancelProposal"/>.</summary>
    [Safe]
    public static bool IsVetoed(ulong proposalId)
    {
        return Storage.Get(ProposalIdKey(PrefixProposalVetoed, proposalId)) != null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Staged-upgrade lifecycle (notice → execution → cooldown).
    //
    // ADVISORY / OBSERVABILITY ONLY. The methods below model a richer proposal
    // lifecycle (notice window, bounded execution window, post-execution cooldown,
    // single-execution marker) for off-chain dashboards / operator runbooks. They
    // are NOT part of the enforced execution gate: every *ViaProposal consumer in the
    // system (RegisterVerifierViaProposal, SetImmutableFlagViaProposal,
    // SetAdmissionModeViaProposal, and the external bridge stack) gates SOLELY on
    // isApprovedAndTimelocked + matchesProposalPayload + its own per-proposalId
    // consumed/replay flag (plus the CancelProposal veto). A consumer does NOT call
    // IsInExecutionWindow or MarkProposalExecuted, so approvals do not "expire" at the
    // end of the execution window and the cooldown does not block re-execution at the
    // protocol level — replay is prevented per-consumer instead. Treat these as
    // operator-facing scheduling hints, not authorization.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Set staged-upgrade timing windows (advisory; see the section note above). Owner only.</summary>
    public static void SetUpgradeWindows(uint noticeSeconds, uint executionWindowSeconds, uint cooldownSeconds)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
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
        if (Storage.Get(ProposalKey(proposalId)) == null) return StagePending;
        if (GetProposalEpoch(proposalId) != GetCouncilEpoch()) return StageExpired;
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
    /// Advisory marker: record that a proposal was executed so the staged-upgrade stage view
    /// (<see cref="GetProposalStage"/>) reports cooldown/complete for dashboards. Owner only.
    /// <para>
    /// This is NOT a hard authorization step — current *ViaProposal consumers enforce
    /// single-execution via their own per-proposalId consumed flag and do not require this
    /// marker (see the staged-upgrade section note above). It is kept so operators running a
    /// notice/execution/cooldown runbook can pin the explicit cool-down start on-chain.
    /// </para>
    /// </summary>
    public static void MarkProposalExecuted(ulong proposalId)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
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
        WriteUInt64LittleEndian(k, 1, id);
        return k;
    }

    private static byte[] ProposalKey(ulong id)
    {
        return ProposalIdKey(PrefixProposal, id);
    }

    private static byte[] ApprovalKey(ulong id, ECPoint memberKey)
    {
        var k = new byte[1 + 8 + 33];
        k[0] = PrefixApproval;
        WriteUInt64LittleEndian(k, 1, id);
        var pk = (byte[])memberKey;
        for (var j = 0; j < 33; j++) k[9 + j] = pk[j];
        return k;
    }

    private static byte[] CouncilMemberKey(ECPoint memberKey)
    {
        var key = new byte[1 + 33];
        key[0] = PrefixCouncilMember;
        var member = (byte[])memberKey;
        ExecutionEngine.Assert(member.Length == 33, "council member key must be compressed");
        for (var i = 0; i < 33; i++) key[i + 1] = member[i];
        return key;
    }

    private static void AssertDistinctMembers(ECPoint[] members)
    {
        for (var i = 0; i < members.Length; i++)
        {
            var left = (byte[])members[i];
            ExecutionEngine.Assert(left.Length == 33, "council member key must be compressed");
            for (var j = i + 1; j < members.Length; j++)
            {
                var right = (byte[])members[j];
                ExecutionEngine.Assert(right.Length == 33, "council member key must be compressed");
                ExecutionEngine.Assert(!MemberKeysEqual(left, right), "duplicate council member");
            }
        }
    }

    private static bool MemberKeysEqual(byte[] left, byte[] right)
    {
        for (var i = 0; i < 33; i++)
            if (left[i] != right[i]) return false;
        return true;
    }

    private static void WriteUInt32LittleEndian(byte[] destination, int offset, uint value)
    {
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
        destination[offset + 2] = (byte)(value >> 16);
        destination[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteUInt64LittleEndian(byte[] destination, int offset, ulong value)
    {
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
        destination[offset + 2] = (byte)(value >> 16);
        destination[offset + 3] = (byte)(value >> 24);
        destination[offset + 4] = (byte)(value >> 32);
        destination[offset + 5] = (byte)(value >> 40);
        destination[offset + 6] = (byte)(value >> 48);
        destination[offset + 7] = (byte)(value >> 56);
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
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Put(VerifierKey(verifier), new byte[] { 1 });
        OnVerifierApproved(verifier);
    }

    /// <summary>Revoke a previously-approved verifier. Owner only. Does not affect chains
    /// already registered under this verifier — only future <c>RegisterChainPublic</c> calls.</summary>
    public static void RevokeVerifier(UInt160 verifier)
    {
        ExecutionEngine.Assert(verifier.IsValid && !verifier.IsZero, "invalid verifier");
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Delete(VerifierKey(verifier));
        OnVerifierRevoked(verifier);
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
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Put(BridgeKey(bridge), new byte[] { 1 });
        OnBridgeAdapterApproved(bridge);
    }

    /// <summary>Revoke a previously-approved bridge adapter. Owner only.</summary>
    public static void RevokeBridgeAdapter(UInt160 bridge)
    {
        ExecutionEngine.Assert(bridge.IsValid && !bridge.IsZero, "invalid bridge");
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Delete(BridgeKey(bridge));
        OnBridgeAdapterRevoked(bridge);
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
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
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
