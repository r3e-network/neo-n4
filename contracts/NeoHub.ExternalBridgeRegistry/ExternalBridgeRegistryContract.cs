using System;
using System.ComponentModel;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.ExternalBridgeRegistry;

/// <summary>
/// Pluggable verifier dispatch table for cross-foreign-chain messages
/// (Ethereum / Tron / Solana / etc.). See <c>doc.md</c> §11.3 and
/// <c>docs/external-bridge-roadmap.md</c>.
/// </summary>
/// <remarks>
/// Mirrors <c>NeoHub.VerifierRegistry</c>: an owner-set + governance-proposal
/// registration path with replay protection. Distinguished from
/// <c>VerifierRegistry</c> because:
/// <list type="bullet">
/// <item><description>Indexed by <c>externalChainId</c> (uint with the
/// <c>0xE0_xx_xx_xx</c> foreign-namespace prefix), not by
/// <c>ProofType</c>.</description></item>
/// <item><description>The dispatched <c>VerifyInboundMessage</c> ABI is
/// distinct from the L2-settlement verifier ABI — it operates on a single
/// message + proof rather than a whole batch commitment.</description></item>
/// <item><description>Records each chain's <c>bridgeKind</c>
/// (1=MPC, 2=Optimistic, 3=ZK) so dApps + UIs can surface the trust
/// model their messages currently traverse.</description></item>
/// </list>
/// Phase B adds the first real verifier (<c>NeoHub.MpcCommitteeVerifier</c>);
/// Phase C swaps to <c>NeoHub.ExternalOptimisticChallenge</c>; Phase D to a
/// per-chain ZK verifier. Each transition is a single
/// <see cref="UpgradeVerifier"/> call through governance — no app rebuild.
/// </remarks>
[DisplayName("NeoHub.ExternalBridgeRegistry")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Pluggable verifier dispatch table for cross-foreign-chain messages.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeRegistry")]
[ContractPermission(Permission.Any, Method.Any)]
public class ExternalBridgeRegistryContract : SmartContract
{
    private const byte PrefixVerifier = 0x01;          // 0x01 + externalChainId(4B LE) → UInt160
    private const byte PrefixBridgeKind = 0x02;        // 0x02 + externalChainId(4B LE) → 1B
    private const byte KeyGovernanceController = 0x03;
    private const byte PrefixConsumedProposal = 0x04;  // 0x04 + proposalId(8B LE) → 1B
    private const byte KeyOwner = 0xFF;

    /// <summary>Foreign-namespace prefix all <c>externalChainId</c>s must carry —
    /// keeps the foreign keyspace disjoint from Neo L2 chainIds (which start at 1).</summary>
    public const uint ForeignNamespacePrefix = 0xE000_0000U;

    /// <summary>1 = MPC committee verifier (Phase B).</summary>
    public const byte BridgeKindMpc = 1;
    /// <summary>2 = Optimistic-challenge verifier (Phase C).</summary>
    public const byte BridgeKindOptimistic = 2;
    /// <summary>3 = ZK light-client verifier (Phase D).</summary>
    public const byte BridgeKindZk = 3;

    /// <summary>Emitted whenever a verifier is registered, replaced, or
    /// upgraded for an external chain.</summary>
    [DisplayName("ExternalVerifierRegistered")]
    public static event Action<uint, UInt160, byte> OnExternalVerifierRegistered = default!;

    /// <summary>Set the initial owner. Same shape as VerifierRegistry._deploy.</summary>
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

    /// <summary>Wire the GovernanceController contract hash that
    /// <see cref="UpgradeVerifierViaProposal"/> consults. Owner only.</summary>
    public static void SetGovernanceController(UInt160 governanceController)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(governanceController.IsValid && !governanceController.IsZero,
            "invalid governance controller");
        Storage.Put(new byte[] { KeyGovernanceController }, governanceController);
    }

    /// <summary>Look up the wired GovernanceController hash, or
    /// <see cref="UInt160.Zero"/> if not yet set.</summary>
    [Safe]
    public static UInt160 GetGovernanceController()
    {
        var raw = Storage.Get(new byte[] { KeyGovernanceController });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Bind a verifier to an <c>externalChainId</c>. Owner only —
    /// the council-veto path is <see cref="UpgradeVerifierViaProposal"/>.</summary>
    public static void RegisterVerifier(uint externalChainId, UInt160 verifier, byte bridgeKind)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        WriteVerifier(externalChainId, verifier, bridgeKind);
    }

    /// <summary>Same as <see cref="RegisterVerifier"/> but follows the
    /// governance + timelock path (council-multisig approval, replay-protected
    /// per <paramref name="proposalId"/>). Anyone can submit; the proof of
    /// authority is the proposal's approval state, not the caller's witness.</summary>
    public static void UpgradeVerifierViaProposal(
        uint externalChainId, UInt160 verifier, byte bridgeKind, ulong proposalId)
    {
        var gc = GetGovernanceController();
        ExecutionEngine.Assert(gc != UInt160.Zero,
            "governance controller not wired — owner must call SetGovernanceController first");

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

        // Bind proposal payload to (externalChainId, verifier, bridgeKind). Without this,
        // an approved proposal could route to ANY foreign chain or replace the verifier
        // with anything — the council vote becomes a one-time blank check.
        var expectedAction = BuildUpgradeVerifierAction(externalChainId, verifier, bridgeKind);
        var bound = (bool)Contract.Call(gc, "matchesProposalPayload",
            CallFlags.ReadOnly, new object[] { proposalId, expectedAction });
        ExecutionEngine.Assert(bound,
            "proposal payload does not match (externalChainId, verifier, bridgeKind) action args (council voted on different bytes)");

        Storage.Put(consumedKey, new byte[] { 1 });
        WriteVerifier(externalChainId, verifier, bridgeKind);
    }

    /// <summary>
    /// Canonical encoding for an "upgrade verifier" action. Council submits this as the
    /// proposal payload; <see cref="UpgradeVerifierViaProposal"/> rebuilds it from
    /// runtime args and asserts byte-equality. Layout:
    /// <c>"neo4-gov:upgradeVerifier" || externalChainId(4B LE) || verifier(20B) || bridgeKind(1B)</c>
    /// = 49 bytes.
    /// </summary>
    [Safe]
    public static byte[] BuildUpgradeVerifierAction(uint externalChainId, UInt160 verifier, byte bridgeKind)
    {
        var tag = ActionTagUpgradeVerifier;
        var buf = new byte[tag.Length + 4 + 20 + 1];
        for (var i = 0; i < tag.Length; i++) buf[i] = tag[i];
        var pos = tag.Length;
        buf[pos++] = (byte)externalChainId;
        buf[pos++] = (byte)(externalChainId >> 8);
        buf[pos++] = (byte)(externalChainId >> 16);
        buf[pos++] = (byte)(externalChainId >> 24);
        var vk = (byte[])verifier;
        for (var i = 0; i < 20; i++) buf[pos + i] = vk[i];
        pos += 20;
        buf[pos] = bridgeKind;
        return buf;
    }

    private static readonly byte[] ActionTagUpgradeVerifier = new byte[]
    {
        (byte)'n', (byte)'e', (byte)'o', (byte)'4', (byte)'-',
        (byte)'g', (byte)'o', (byte)'v', (byte)':',
        (byte)'u', (byte)'p', (byte)'g', (byte)'r', (byte)'a', (byte)'d', (byte)'e',
        (byte)'V', (byte)'e', (byte)'r', (byte)'i', (byte)'f', (byte)'i', (byte)'e', (byte)'r'
    };

    /// <summary>Convenience alias retained for symmetry with
    /// <c>VerifierRegistry.RegisterVerifierViaProposal</c>.</summary>
    public static void UpgradeVerifier(uint externalChainId, UInt160 verifier, byte bridgeKind)
    {
        RegisterVerifier(externalChainId, verifier, bridgeKind);
    }

    /// <summary>Read the verifier hash registered for an external chain, or
    /// <see cref="UInt160.Zero"/> if none. Callers should treat zero as
    /// "chain not supported".</summary>
    [Safe]
    public static UInt160 GetVerifier(uint externalChainId)
    {
        var raw = Storage.Get(VerifierKey(externalChainId));
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Read the bridgeKind (1=MPC, 2=Optimistic, 3=ZK) for an
    /// external chain, or <c>0</c> if not registered.</summary>
    [Safe]
    public static byte GetBridgeKind(uint externalChainId)
    {
        var raw = Storage.Get(BridgeKindKey(externalChainId));
        return raw == null ? (byte)0 : raw[0];
    }

    /// <summary>Dispatch verification to the registered verifier. The verifier
    /// contract must export <c>verifyInboundMessage(uint, byte[], byte[])</c>.
    /// Reverts if no verifier is registered for <paramref name="externalChainId"/>.</summary>
    [Safe]
    public static bool VerifyInbound(uint externalChainId, byte[] messageBytes, byte[] proofBytes)
    {
        var verifier = GetVerifier(externalChainId);
        ExecutionEngine.Assert(verifier != UInt160.Zero,
            "no verifier registered for externalChainId");
        return (bool)Contract.Call(verifier, "verifyInboundMessage",
            CallFlags.ReadOnly, new object[] { externalChainId, messageBytes, proofBytes });
    }

    private static void WriteVerifier(uint externalChainId, UInt160 verifier, byte bridgeKind)
    {
        ExecutionEngine.Assert(verifier.IsValid && !verifier.IsZero, "invalid verifier");
        ExecutionEngine.Assert(
            (externalChainId & 0xFF000000U) == ForeignNamespacePrefix,
            "externalChainId must use the 0xE0_xx_xx_xx foreign-namespace prefix");
        ExecutionEngine.Assert(
            bridgeKind == BridgeKindMpc
            || bridgeKind == BridgeKindOptimistic
            || bridgeKind == BridgeKindZk,
            "bridgeKind must be 1 (MPC), 2 (Optimistic), or 3 (ZK)");

        Storage.Put(VerifierKey(externalChainId), verifier);
        Storage.Put(BridgeKindKey(externalChainId), new byte[] { bridgeKind });
        OnExternalVerifierRegistered(externalChainId, verifier, bridgeKind);
    }

    private static byte[] VerifierKey(uint externalChainId)
    {
        var k = new byte[1 + 4];
        k[0] = PrefixVerifier;
        k[1] = (byte)externalChainId;
        k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16);
        k[4] = (byte)(externalChainId >> 24);
        return k;
    }

    private static byte[] BridgeKindKey(uint externalChainId)
    {
        var k = new byte[1 + 4];
        k[0] = PrefixBridgeKind;
        k[1] = (byte)externalChainId;
        k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16);
        k[4] = (byte)(externalChainId >> 24);
        return k;
    }
}
