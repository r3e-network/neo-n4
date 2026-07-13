using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.MessageRouter;

/// <summary>
/// L1-side router for L1↔L2 and L2↔L2 cross-chain messages. Maintains the L1→L2 outbound
/// queue per target chain and the L2→L1 / L2→L2 message-root registry per finalized batch.
/// See doc.md §3.2 (MessageRouter) and §10 (Neo Connect).
/// </summary>
[DisplayName("NeoHub.MessageRouter")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Cross-chain message queue + message-root registry for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.MessageRouter")]
[ContractPermission(Permission.Any, Method.Any)]
public class MessageRouterContract : SmartContract
{
    private const byte PrefixL1ToL2Nonce = 0x01;       // 0x01 + targetChainId(4B) → next nonce
    private const byte PrefixL1ToL2Msg = 0x02;         // 0x02 + targetChainId(4B) + nonce(8B) → encoded msg
    private const byte PrefixL2ToL1Root = 0x03;        // 0x03 + chainId(4B) + batchNum(8B) → root
    private const byte PrefixL2ToL2Root = 0x04;        // 0x04 + chainId(4B) + batchNum(8B) → root
    private const byte PrefixGlobalRoot = 0x05;        // 0x05 + batchEpoch(8B) → 32B global aggregated message root (Phase-5 Neo Gateway commitment)
    private const byte PrefixConsumed = 0x06;          // 0x06 + msgHash(32B) → 1
    private const byte PrefixL1TxFilter = 0x07;        // 0x07 + targetChainId(4B) → filter contract hash
    private const byte KeyGlobalRootVerifier = 0x08;   // 20B terminal verifier/router hash
    private const byte KeyGlobalRootProofSystem = 0x09;// 1B terminal proof-system tag (1=SP1, …)
    private const byte KeyGlobalRootVerificationKey = 0x0A; // pinned 32B verification-key id
    private const byte KeyGlobalRootReplayDomain = 0x0B;     // pinned 32B replay domain
    private const byte PrefixGlobalRootProofInput = 0x0C;    // 0x0C + epoch(8B) → accepted proof-input hash
    private const byte KeyGlobalRootAggregationBackend = 0x0D; // pinned non-pass-through backend
    private const byte KeyGovernanceController = 0x0E;
    private const byte PrefixConsumedGatewayProposal = 0x0F; // 0x0F + proposalId(8B) → 1
    private const byte KeyGlobalRootGovernanceLocked = 0x10;
    private const byte PrefixSettlementManager = 0xFD;
    private const byte KeyOwner = 0xFF;
    private const int MaxAggregatedProofBytes = 1 * 1024 * 1024;
    private const int MaxMessageProofDepth = 64;
    private const byte PassThroughRoundBackend = 0xFE;
    private const byte PassThroughAggregateBackend = 0xFF;

    /// <summary>Emitted when a new L1→L2 message is enqueued.</summary>
    [DisplayName("L1ToL2Enqueued")]
    public static event Action<uint, ulong, UInt160, UInt160> OnL1ToL2Enqueued = default!;

    /// <summary>Emitted when an L2→L1 message is consumed on L1.</summary>
    [DisplayName("L2ToL1Consumed")]
    public static event Action<uint, UInt256> OnL2ToL1Consumed = default!;

    /// <summary>Emitted when the Neo Gateway publishes a global aggregated message root for an epoch.</summary>
    [DisplayName("GlobalRootPublished")]
    public static event Action<ulong, UInt256> OnGlobalRootPublished = default!;

    /// <summary>Emitted when a per-chain L1→L2 filter is installed or cleared.</summary>
    [DisplayName("L1TxFilterSet")]
    public static event Action<uint, UInt160> OnL1TxFilterSet = default!;

    /// <summary>Emitted when message roots are published for a finalized batch.</summary>
    [DisplayName("MessageRootsPublished")]
    public static event Action<uint, ulong, UInt256, UInt256> OnMessageRootsPublished = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    /// <summary>Emitted when governance installs a fail-closed Gateway proof profile.</summary>
    [DisplayName("GlobalRootVerifierSet")]
    public static event Action<UInt160, byte, byte, UInt256, UInt256> OnGlobalRootVerifierSet = default!;

    /// <summary>Emitted with the canonical proof statement accepted for an epoch.</summary>
    [DisplayName("GlobalRootProofAccepted")]
    public static event Action<ulong, UInt256, UInt256> OnGlobalRootProofAccepted = default!;

    /// <summary>Emitted when the governance controller is configured during bootstrap.</summary>
    [DisplayName("GovernanceControllerChanged")]
    public static event Action<UInt160> OnGovernanceControllerChanged = default!;

    /// <summary>Emitted once the owner bootstrap path is permanently disabled.</summary>
    [DisplayName("GlobalRootGovernanceLocked")]
    public static event Action OnGlobalRootGovernanceLocked = default!;

    /// <summary>Set wiring on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var settlementManager = (UInt160)arr[1];
        // Without these guards a typo'd zero settlementManager would deploy successfully
        // but every Route call would later fail mysteriously when verifying message
        // proofs against a non-existent contract.
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(settlementManager.IsValid && !settlementManager.IsZero, "invalid settlement manager");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { PrefixSettlementManager }, settlementManager);
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
    /// Enqueue an L1 → L2 message. Anyone may call. The L2 watches its inbox and consumes
    /// the message in its next batch; replay protection lives on the L2 side via the
    /// (sourceChain, nonce) bitmap.
    /// </summary>
    public static ulong EnqueueL1ToL2(uint targetChainId, UInt160 receiver, byte messageType, byte[] payload)
    {
        ExecutionEngine.Assert(receiver.IsValid && !receiver.IsZero, "invalid receiver");
        // chainId 0 is the L1 sentinel — without this guard anyone could enqueue
        // L1→L2 messages bound for chainId 0 that no L2 would ever consume,
        // bloating L1 storage with no-op entries.
        ExecutionEngine.Assert(targetChainId > 0, "targetChainId 0 is reserved for L1");

        var sender = Runtime.CallingScriptHash;
        ApplyL1TxFilter(targetChainId, sender, receiver, messageType, payload);

        var nonceKey = NonceKey(targetChainId);
        var raw = Storage.Get(nonceKey);
        var nonce = raw == null ? 1UL : (ulong)(BigInteger)raw + 1UL;
        Storage.Put(nonceKey, (BigInteger)nonce);

        var encoded = EncodeMessage(0u, targetChainId, nonce, sender, receiver, messageType, payload);
        Storage.Put(MessageKey(targetChainId, nonce), encoded);

        OnL1ToL2Enqueued(targetChainId, nonce, sender, receiver);
        return nonce;
    }

    /// <summary>Read a previously enqueued L1→L2 message by (chainId, nonce).</summary>
    [Safe]
    public static byte[] GetL1ToL2(uint chainId, ulong nonce)
    {
        var raw = Storage.Get(MessageKey(chainId, nonce));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    /// <summary>Install or replace a per-target-chain L1→L2 filter. Owner only.</summary>
    public static void SetL1TxFilter(uint targetChainId, UInt160 filter)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(targetChainId > 0, "targetChainId 0 is reserved for L1");
        ExecutionEngine.Assert(filter.IsValid && !filter.IsZero, "invalid filter");
        Storage.Put(L1TxFilterKey(targetChainId), filter);
        OnL1TxFilterSet(targetChainId, filter);
    }

    /// <summary>Clear the filter for a target chain. Owner only.</summary>
    public static void ClearL1TxFilter(uint targetChainId)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(targetChainId > 0, "targetChainId 0 is reserved for L1");
        Storage.Delete(L1TxFilterKey(targetChainId));
        OnL1TxFilterSet(targetChainId, UInt160.Zero);
    }

    /// <summary>Read the filter for a target chain, or zero if none is configured.</summary>
    [Safe]
    public static UInt160 GetL1TxFilter(uint targetChainId)
    {
        var raw = Storage.Get(L1TxFilterKey(targetChainId));
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Settlement manager calls this when finalizing a batch to publish the L2→L1 and L2→L2
    /// message roots so they can be used for inclusion-proof verification.
    /// </summary>
    public static void PublishMessageRoots(uint chainId, ulong batchNumber, UInt256 l2ToL1Root, UInt256 l2ToL2Root)
    {
        var sm = (UInt160)(Storage.Get(new byte[] { PrefixSettlementManager }) ?? throw new Exception("sm unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(sm), "not settlement manager");
        Storage.Put(BuildKey(PrefixL2ToL1Root, chainId, batchNumber), (byte[])l2ToL1Root);
        Storage.Put(BuildKey(PrefixL2ToL2Root, chainId, batchNumber), (byte[])l2ToL2Root);
        OnMessageRootsPublished(chainId, batchNumber, l2ToL1Root, l2ToL2Root);
    }

    /// <summary>Get the L2→L1 message root committed for a finalized batch.</summary>
    [Safe]
    public static UInt256 GetL2ToL1Root(uint chainId, ulong batchNumber)
    {
        var raw = Storage.Get(BuildKey(PrefixL2ToL1Root, chainId, batchNumber));
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    /// <summary>Get the L2→L2 message root committed for a finalized batch.</summary>
    [Safe]
    public static UInt256 GetL2ToL2Root(uint chainId, ulong batchNumber)
    {
        var raw = Storage.Get(BuildKey(PrefixL2ToL2Root, chainId, batchNumber));
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    /// <summary>
    /// Verify and publish a fully-bound Gateway global root. Returns <c>true</c> for a new
    /// publication and <c>false</c> for an exact idempotent replay of confirmed state.
    /// </summary>
    /// <remarks>
    /// See doc.md §4 (Neo Gateway). The terminal verifier checks Hash256 over
    /// <c>"NEO4GWR2" || executingScriptHash || replayDomain || epoch || globalRoot ||
    /// constituentCommitmentsRoot || constituentCount || aggregationBackend || proofSystem ||
    /// verificationKeyId</c>. A settlement-manager witness can submit the transaction but cannot
    /// replace proof verification. New publications are disabled until governance is locked.
    /// </remarks>
    public static bool PublishGlobalRoot(
        ulong batchEpoch,
        UInt256 globalRoot,
        UInt256 constituentCommitmentsRoot,
        uint constituentCount,
        byte aggregationBackendId,
        byte proofSystem,
        UInt256 verificationKeyId,
        UInt256 replayDomain,
        byte[] aggregatedProof)
    {
        var sm = (UInt160)(Storage.Get(new byte[] { PrefixSettlementManager }) ?? throw new Exception("sm unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(sm), "not settlement manager");
        ExecutionEngine.Assert(!globalRoot.Equals(UInt256.Zero), "global root must be non-zero");
        ExecutionEngine.Assert(!constituentCommitmentsRoot.Equals(UInt256.Zero),
            "constituent commitments root must be non-zero");
        ExecutionEngine.Assert(constituentCount > 0, "constituent count must be positive");
        ExecutionEngine.Assert(IsProductionAggregationBackend(aggregationBackendId),
            "pass-through/reserved aggregation backend is not publishable");
        ExecutionEngine.Assert(proofSystem >= 1 && proofSystem <= 4, "proofSystem must be 1..4");
        ExecutionEngine.Assert(!verificationKeyId.Equals(UInt256.Zero),
            "verification key id must be non-zero");
        ExecutionEngine.Assert(!replayDomain.Equals(UInt256.Zero), "replay domain must be non-zero");

        var proofInputHash = BuildGlobalRootProofInputHash(
            batchEpoch,
            globalRoot,
            constituentCommitmentsRoot,
            constituentCount,
            aggregationBackendId,
            proofSystem,
            verificationKeyId,
            replayDomain);
        var key = GlobalRootKey(batchEpoch);
        var existingRoot = Storage.Get(key);
        if (existingRoot != null)
        {
            var existingInput = Storage.Get(GlobalRootProofInputKey(batchEpoch));
            ExecutionEngine.Assert(
                ((UInt256)existingRoot).Equals(globalRoot)
                && existingInput != null
                && ((UInt256)existingInput).Equals(proofInputHash),
                "epoch already bound to a different global root statement");
            return false;
        }

        ExecutionEngine.Assert(IsGlobalRootGovernanceLocked(),
            "global root governance not locked");
        ExecutionEngine.Assert(aggregationBackendId == GetGlobalRootAggregationBackend(),
            "global root aggregation backend mismatch");
        ExecutionEngine.Assert(proofSystem == GetGlobalRootProofSystem(),
            "global root proof system mismatch");
        ExecutionEngine.Assert(verificationKeyId.Equals(GetGlobalRootVerificationKeyId()),
            "global root verification key mismatch");
        ExecutionEngine.Assert(replayDomain.Equals(GetGlobalRootReplayDomain()),
            "global root replay domain mismatch");
        ExecutionEngine.Assert(aggregatedProof.Length > 0, "aggregated proof required");
        ExecutionEngine.Assert(aggregatedProof.Length <= MaxAggregatedProofBytes,
            "aggregated proof too large");

        var verifier = GetGlobalRootVerifier();
        ExecutionEngine.Assert(verifier.IsValid && !verifier.IsZero,
            "global root verifier not configured");
        var verified = (bool)Contract.Call(
            verifier,
            "verifyZkProof",
            CallFlags.ReadOnly,
            new object[]
            {
                (BigInteger)proofSystem,
                (byte[])verificationKeyId,
                (byte[])proofInputHash,
                aggregatedProof
            });
        ExecutionEngine.Assert(verified, "gateway aggregate proof rejected");

        Storage.Put(key, (byte[])globalRoot);
        Storage.Put(GlobalRootProofInputKey(batchEpoch), (byte[])proofInputHash);
        OnGlobalRootPublished(batchEpoch, globalRoot);
        OnGlobalRootProofAccepted(batchEpoch, constituentCommitmentsRoot, proofInputHash);
        return true;
    }

    /// <summary>Bootstrap the fail-closed Gateway proof profile. Owner only before lock.</summary>
    public static void SetGlobalRootVerifier(
        UInt160 verifier,
        byte proofSystem,
        byte aggregationBackendId,
        UInt256 verificationKeyId,
        UInt256 replayDomain)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(!IsGlobalRootGovernanceLocked(),
            "governance locked — use SetGlobalRootVerifierViaProposal");
        WriteGlobalRootVerifier(
            verifier,
            proofSystem,
            aggregationBackendId,
            verificationKeyId,
            replayDomain);
    }

    /// <summary>Configure the GovernanceController used for post-lock profile rotation.</summary>
    public static void SetGovernanceController(UInt160 governanceController)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(!IsGlobalRootGovernanceLocked(),
            "global root governance already locked");
        ExecutionEngine.Assert(governanceController.IsValid && !governanceController.IsZero,
            "invalid governance controller");
        Storage.Put(new byte[] { KeyGovernanceController }, governanceController);
        OnGovernanceControllerChanged(governanceController);
    }

    /// <summary>Permanently disable owner-only proof-profile mutation.</summary>
    public static void LockGlobalRootGovernance()
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(GetGovernanceController() != UInt160.Zero,
            "wire GovernanceController before locking");
        AssertGlobalRootVerifierConfigured();
        var key = new byte[] { KeyGlobalRootGovernanceLocked };
        if (Storage.Get(key) == null)
        {
            Storage.Put(key, new byte[] { 1 });
            OnGlobalRootGovernanceLocked();
        }
    }

    /// <summary>Apply an exact council-approved and timelocked proof-profile rotation.</summary>
    public static void SetGlobalRootVerifierViaProposal(
        UInt160 verifier,
        byte proofSystem,
        byte aggregationBackendId,
        UInt256 verificationKeyId,
        UInt256 replayDomain,
        ulong proposalId)
    {
        ExecutionEngine.Assert(IsGlobalRootGovernanceLocked(),
            "lock global root governance before using proposal path");
        var governanceController = GetGovernanceController();
        ExecutionEngine.Assert(governanceController != UInt160.Zero,
            "governance controller not wired");
        var consumedKey = GatewayProposalKey(proposalId);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "proposal already consumed");
        var approved = (bool)Contract.Call(
            governanceController,
            "isApprovedAndTimelocked",
            CallFlags.ReadOnly,
            new object[] { proposalId });
        ExecutionEngine.Assert(approved, "proposal not approved and timelocked");
        var action = BuildSetGlobalRootVerifierAction(
            verifier,
            proofSystem,
            aggregationBackendId,
            verificationKeyId,
            replayDomain);
        var matches = (bool)Contract.Call(
            governanceController,
            "matchesProposalPayload",
            CallFlags.ReadOnly,
            new object[] { proposalId, action });
        ExecutionEngine.Assert(matches, "proposal payload does not match Gateway verifier action");
        Storage.Put(consumedKey, new byte[] { 1 });
        WriteGlobalRootVerifier(
            verifier,
            proofSystem,
            aggregationBackendId,
            verificationKeyId,
            replayDomain);
    }

    /// <summary>
    /// Canonical governance payload for an exact, MessageRouter-bound proof-profile rotation.
    /// </summary>
    [Safe]
    public static byte[] BuildSetGlobalRootVerifierAction(
        UInt160 verifier,
        byte proofSystem,
        byte aggregationBackendId,
        UInt256 verificationKeyId,
        UInt256 replayDomain)
    {
        var tag = SetGlobalRootVerifierActionTag;
        var buffer = new byte[tag.Length + 20 + 20 + 1 + 1 + 32 + 32];
        var position = 0;
        for (var i = 0; i < tag.Length; i++) buffer[position++] = tag[i];
        var messageRouterBytes = (byte[])Runtime.ExecutingScriptHash;
        for (var i = 0; i < 20; i++) buffer[position++] = messageRouterBytes[i];
        var verifierBytes = (byte[])verifier;
        for (var i = 0; i < 20; i++) buffer[position++] = verifierBytes[i];
        buffer[position++] = proofSystem;
        buffer[position++] = aggregationBackendId;
        var verificationKeyBytes = (byte[])verificationKeyId;
        for (var i = 0; i < 32; i++) buffer[position++] = verificationKeyBytes[i];
        var replayDomainBytes = (byte[])replayDomain;
        for (var i = 0; i < 32; i++) buffer[position++] = replayDomainBytes[i];
        return buffer;
    }

    /// <summary>Build the exact Hash256 public input checked by <see cref="PublishGlobalRoot"/>.</summary>
    [Safe]
    public static UInt256 BuildGlobalRootProofInputHash(
        ulong batchEpoch,
        UInt256 globalRoot,
        UInt256 constituentCommitmentsRoot,
        uint constituentCount,
        byte aggregationBackendId,
        byte proofSystem,
        UInt256 verificationKeyId,
        UInt256 replayDomain)
    {
        var buffer = new byte[170];
        var tag = GlobalRootProofDomainTag;
        for (var i = 0; i < tag.Length; i++) buffer[i] = tag[i];
        var router = (byte[])Runtime.ExecutingScriptHash;
        for (var i = 0; i < 20; i++) buffer[8 + i] = router[i];
        CopyUInt256(buffer, 28, replayDomain);
        WriteUInt64(buffer, 60, batchEpoch);
        CopyUInt256(buffer, 68, globalRoot);
        CopyUInt256(buffer, 100, constituentCommitmentsRoot);
        WriteUInt32(buffer, 132, constituentCount);
        buffer[136] = aggregationBackendId;
        buffer[137] = proofSystem;
        CopyUInt256(buffer, 138, verificationKeyId);
        var first = CryptoLib.Sha256((ByteString)buffer);
        return (UInt256)(byte[])CryptoLib.Sha256(first);
    }

    /// <summary>Configured terminal verifier/router, or zero before bootstrap.</summary>
    [Safe]
    public static UInt160 GetGlobalRootVerifier()
    {
        var raw = Storage.Get(new byte[] { KeyGlobalRootVerifier });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Configured terminal proof-system tag, or zero before bootstrap.</summary>
    [Safe]
    public static byte GetGlobalRootProofSystem()
    {
        var raw = Storage.Get(new byte[] { KeyGlobalRootProofSystem });
        return raw == null ? (byte)0 : ((byte[])raw)[0];
    }

    /// <summary>Configured aggregation-backend tag, or zero before bootstrap.</summary>
    [Safe]
    public static byte GetGlobalRootAggregationBackend()
    {
        var raw = Storage.Get(new byte[] { KeyGlobalRootAggregationBackend });
        return raw == null ? (byte)0 : ((byte[])raw)[0];
    }

    /// <summary>Configured verification-key id, or zero before bootstrap.</summary>
    [Safe]
    public static UInt256 GetGlobalRootVerificationKeyId()
    {
        var raw = Storage.Get(new byte[] { KeyGlobalRootVerificationKey });
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    /// <summary>Configured replay domain, or zero before bootstrap.</summary>
    [Safe]
    public static UInt256 GetGlobalRootReplayDomain()
    {
        var raw = Storage.Get(new byte[] { KeyGlobalRootReplayDomain });
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    /// <summary>Configured GovernanceController, or zero before bootstrap.</summary>
    [Safe]
    public static UInt160 GetGovernanceController()
    {
        var raw = Storage.Get(new byte[] { KeyGovernanceController });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>True once owner-only Gateway proof-profile mutation is disabled.</summary>
    [Safe]
    public static bool IsGlobalRootGovernanceLocked()
    {
        return Storage.Get(new byte[] { KeyGlobalRootGovernanceLocked }) != null;
    }

    /// <summary>Accepted canonical proof-input hash for an epoch, or zero when unpublished.</summary>
    [Safe]
    public static UInt256 GetGlobalRootProofInputHash(ulong batchEpoch)
    {
        var raw = Storage.Get(GlobalRootProofInputKey(batchEpoch));
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    /// <summary>
    /// Get the global aggregated message root committed for a Phase-5 Gateway epoch,
    /// or <see cref="UInt256.Zero"/> if no root has been published for that epoch.
    /// </summary>
    [Safe]
    public static UInt256 GetGlobalRoot(ulong batchEpoch)
    {
        var raw = Storage.Get(GlobalRootKey(batchEpoch));
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    /// <summary>
    /// Permissionlessly consume an L2→L1 message after proving that its canonical message hash is
    /// included in a finalized batch's L2→L1 message root. Replay-protected.
    /// </summary>
    /// <remarks>
    /// See doc.md §3.2 (MessageRouter) and §10 (Neo Connect). The root is read from
    /// SettlementManager's finalized canonical batch header; a settlement witness cannot replace
    /// the Merkle proof. Siblings are ordered from leaf to root and use Neo Hash256 over
    /// <c>left || right</c>. Bit i of <paramref name="leafIndex"/> selects the leaf's side at
    /// level i.
    /// </remarks>
    public static void ConsumeL2ToL1(
        uint sourceChainId,
        ulong batchNumber,
        UInt256 messageHash,
        byte[][] siblings,
        ulong leafIndex)
    {
        ExecutionEngine.Assert(sourceChainId > 0, "sourceChainId 0 is reserved for L1");
        ExecutionEngine.Assert(!messageHash.Equals(UInt256.Zero), "message hash must be non-zero");
        ExecutionEngine.Assert(siblings != null, "siblings required");
        var proofSiblings = siblings!;
        ExecutionEngine.Assert(proofSiblings.Length <= MaxMessageProofDepth, "proof too deep");

        var sm = (UInt160)(Storage.Get(new byte[] { PrefixSettlementManager }) ?? throw new Exception("sm unset"));
        var root = (UInt256)Contract.Call(
            sm,
            "getL2ToL1MessageRoot",
            CallFlags.ReadOnly,
            new object[] { sourceChainId, batchNumber });
        ExecutionEngine.Assert(!root.Equals(UInt256.Zero), "batch is not finalized or has no L2-to-L1 messages");
        ExecutionEngine.Assert(VerifyMerkleProof(messageHash, root, proofSiblings, leafIndex),
            "invalid L2-to-L1 message proof");

        var key = ConsumedKey(messageHash);
        ExecutionEngine.Assert(Storage.Get(key) == null, "already consumed");
        Storage.Put(key, new byte[] { 1 });
        OnL2ToL1Consumed(sourceChainId, messageHash);
    }

    /// <summary>True if a message hash has been consumed.</summary>
    [Safe]
    public static bool IsConsumed(UInt256 messageHash)
    {
        return Storage.Get(ConsumedKey(messageHash)) != null;
    }

    private static bool VerifyMerkleProof(
        UInt256 leafHash,
        UInt256 expectedRoot,
        byte[][] siblings,
        ulong leafIndex)
    {
        var current = (byte[])leafHash;
        var index = leafIndex;
        for (var level = 0; level < siblings.Length; level++)
        {
            var sibling = siblings[level];
            ExecutionEngine.Assert(sibling != null && sibling.Length == 32,
                "sibling must be 32 bytes");
            var combined = new byte[64];
            if ((index & 1UL) == 0UL)
            {
                for (var i = 0; i < 32; i++) combined[i] = current[i];
                for (var i = 0; i < 32; i++) combined[32 + i] = sibling[i];
            }
            else
            {
                for (var i = 0; i < 32; i++) combined[i] = sibling[i];
                for (var i = 0; i < 32; i++) combined[32 + i] = current[i];
            }

            var first = CryptoLib.Sha256((ByteString)combined);
            current = (byte[])CryptoLib.Sha256(first);
            index >>= 1;
        }

        return index == 0 && expectedRoot.Equals((UInt256)current);
    }

    private static byte[] NonceKey(uint chainId)
    {
        var k = new byte[5];
        k[0] = PrefixL1ToL2Nonce;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        return k;
    }

    private static byte[] MessageKey(uint chainId, ulong nonce) =>
        BuildKey(PrefixL1ToL2Msg, chainId, nonce);

    private static byte[] ConsumedKey(UInt256 hash)
    {
        var k = new byte[1 + 32];
        k[0] = PrefixConsumed;
        var b = (byte[])hash;
        for (var i = 0; i < 32; i++) k[1 + i] = b[i];
        return k;
    }

    private static void ApplyL1TxFilter(
        uint targetChainId,
        UInt160 sender,
        UInt160 receiver,
        byte messageType,
        byte[] payload)
    {
        var filter = GetL1TxFilter(targetChainId);
        if (filter.IsZero) return;
        var accepted = (bool)Contract.Call(
            filter,
            "acceptL1ToL2",
            CallFlags.ReadOnly,
            new object[] { targetChainId, sender, receiver, messageType, payload });
        ExecutionEngine.Assert(accepted, "l1 to l2 message rejected by filter");
    }

    private static byte[] L1TxFilterKey(uint chainId)
    {
        var k = new byte[5];
        k[0] = PrefixL1TxFilter;
        k[1] = (byte)chainId;
        k[2] = (byte)(chainId >> 8);
        k[3] = (byte)(chainId >> 16);
        k[4] = (byte)(chainId >> 24);
        return k;
    }

    private static byte[] GlobalRootKey(ulong batchEpoch)
    {
        var k = new byte[1 + 8];
        k[0] = PrefixGlobalRoot;
        k[1] = (byte)batchEpoch; k[2] = (byte)(batchEpoch >> 8);
        k[3] = (byte)(batchEpoch >> 16); k[4] = (byte)(batchEpoch >> 24);
        k[5] = (byte)(batchEpoch >> 32); k[6] = (byte)(batchEpoch >> 40);
        k[7] = (byte)(batchEpoch >> 48); k[8] = (byte)(batchEpoch >> 56);
        return k;
    }

    private static byte[] GlobalRootProofInputKey(ulong batchEpoch)
    {
        var key = GlobalRootKey(batchEpoch);
        key[0] = PrefixGlobalRootProofInput;
        return key;
    }

    private static byte[] GatewayProposalKey(ulong proposalId)
    {
        var key = new byte[9];
        key[0] = PrefixConsumedGatewayProposal;
        WriteUInt64(key, 1, proposalId);
        return key;
    }

    private static void WriteGlobalRootVerifier(
        UInt160 verifier,
        byte proofSystem,
        byte aggregationBackendId,
        UInt256 verificationKeyId,
        UInt256 replayDomain)
    {
        ExecutionEngine.Assert(verifier.IsValid && !verifier.IsZero,
            "invalid global root verifier");
        ExecutionEngine.Assert(proofSystem >= 1 && proofSystem <= 4,
            "proofSystem must be 1..4");
        ExecutionEngine.Assert(IsProductionAggregationBackend(aggregationBackendId),
            "pass-through/reserved aggregation backend is not publishable");
        ExecutionEngine.Assert(!verificationKeyId.Equals(UInt256.Zero),
            "verification key id must be non-zero");
        ExecutionEngine.Assert(!replayDomain.Equals(UInt256.Zero),
            "replay domain must be non-zero");
        Storage.Put(new byte[] { KeyGlobalRootVerifier }, verifier);
        Storage.Put(new byte[] { KeyGlobalRootProofSystem }, new byte[] { proofSystem });
        Storage.Put(new byte[] { KeyGlobalRootAggregationBackend }, new byte[] { aggregationBackendId });
        Storage.Put(new byte[] { KeyGlobalRootVerificationKey }, (byte[])verificationKeyId);
        Storage.Put(new byte[] { KeyGlobalRootReplayDomain }, (byte[])replayDomain);
        OnGlobalRootVerifierSet(
            verifier,
            proofSystem,
            aggregationBackendId,
            verificationKeyId,
            replayDomain);
    }

    private static void AssertGlobalRootVerifierConfigured()
    {
        ExecutionEngine.Assert(GetGlobalRootVerifier() != UInt160.Zero,
            "global root verifier not configured");
        ExecutionEngine.Assert(GetGlobalRootProofSystem() >= 1,
            "global root proof system not configured");
        ExecutionEngine.Assert(
            IsProductionAggregationBackend(GetGlobalRootAggregationBackend()),
            "global root aggregation backend not configured");
        ExecutionEngine.Assert(GetGlobalRootVerificationKeyId() != UInt256.Zero,
            "global root verification key not configured");
        ExecutionEngine.Assert(GetGlobalRootReplayDomain() != UInt256.Zero,
            "global root replay domain not configured");
    }

    private static bool IsProductionAggregationBackend(byte aggregationBackendId)
    {
        return aggregationBackendId != 0
            && aggregationBackendId != PassThroughRoundBackend
            && aggregationBackendId != PassThroughAggregateBackend;
    }

    private static void CopyUInt256(byte[] target, int offset, UInt256 value)
    {
        var bytes = (byte[])value;
        for (var i = 0; i < 32; i++) target[offset + i] = bytes[i];
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteUInt64(byte[] target, int offset, ulong value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
        target[offset + 4] = (byte)(value >> 32);
        target[offset + 5] = (byte)(value >> 40);
        target[offset + 6] = (byte)(value >> 48);
        target[offset + 7] = (byte)(value >> 56);
    }

    private static readonly byte[] GlobalRootProofDomainTag = new byte[]
    {
        (byte)'N', (byte)'E', (byte)'O', (byte)'4',
        (byte)'G', (byte)'W', (byte)'R', (byte)'2'
    };

    private static readonly byte[] SetGlobalRootVerifierActionTag = new byte[]
    {
        (byte)'n', (byte)'e', (byte)'o', (byte)'4', (byte)'-',
        (byte)'g', (byte)'o', (byte)'v', (byte)':',
        (byte)'s', (byte)'e', (byte)'t', (byte)'G', (byte)'l', (byte)'o', (byte)'b', (byte)'a', (byte)'l',
        (byte)'R', (byte)'o', (byte)'o', (byte)'t', (byte)'V', (byte)'e', (byte)'r', (byte)'i', (byte)'f', (byte)'i', (byte)'e', (byte)'r'
    };

    private static byte[] BuildKey(byte prefix, uint chainId, ulong number)
    {
        var k = new byte[13];
        k[0] = prefix;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        k[5] = (byte)number; k[6] = (byte)(number >> 8); k[7] = (byte)(number >> 16); k[8] = (byte)(number >> 24);
        k[9] = (byte)(number >> 32); k[10] = (byte)(number >> 40); k[11] = (byte)(number >> 48); k[12] = (byte)(number >> 56);
        return k;
    }

    private static byte[] EncodeMessage(uint sourceChainId, uint targetChainId, ulong nonce, UInt160 sender, UInt160 receiver, byte messageType, byte[] payload)
    {
        // 4 + 4 + 8 + 20 + 20 + 1 + 4 + payload.Length
        var size = 4 + 4 + 8 + 20 + 20 + 1 + 4 + payload.Length;
        var buf = new byte[size];
        var pos = 0;
        buf[pos++] = (byte)sourceChainId; buf[pos++] = (byte)(sourceChainId >> 8);
        buf[pos++] = (byte)(sourceChainId >> 16); buf[pos++] = (byte)(sourceChainId >> 24);
        buf[pos++] = (byte)targetChainId; buf[pos++] = (byte)(targetChainId >> 8);
        buf[pos++] = (byte)(targetChainId >> 16); buf[pos++] = (byte)(targetChainId >> 24);
        buf[pos++] = (byte)nonce; buf[pos++] = (byte)(nonce >> 8); buf[pos++] = (byte)(nonce >> 16); buf[pos++] = (byte)(nonce >> 24);
        buf[pos++] = (byte)(nonce >> 32); buf[pos++] = (byte)(nonce >> 40); buf[pos++] = (byte)(nonce >> 48); buf[pos++] = (byte)(nonce >> 56);
        var senderBytes = (byte[])sender;
        for (var i = 0; i < 20; i++) buf[pos + i] = senderBytes[i];
        pos += 20;
        var receiverBytes = (byte[])receiver;
        for (var i = 0; i < 20; i++) buf[pos + i] = receiverBytes[i];
        pos += 20;
        buf[pos++] = messageType;
        var len = payload.Length;
        buf[pos++] = (byte)len; buf[pos++] = (byte)(len >> 8); buf[pos++] = (byte)(len >> 16); buf[pos++] = (byte)(len >> 24);
        for (var i = 0; i < len; i++) buf[pos + i] = payload[i];
        return buf;
    }
}
