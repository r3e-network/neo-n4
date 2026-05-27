using System;
using System.ComponentModel;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.EmergencyManager;

/// <summary>
/// Provides the global pause flag and the escape-hatch withdrawal path. See doc.md §3.2
/// (EmergencyManager) and §15.5 (Emergency Exit). Other NeoHub contracts consult
/// <see cref="IsPaused"/> before mutating state.
/// </summary>
[DisplayName("NeoHub.EmergencyManager")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Emergency pause + escape hatch for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.EmergencyManager")]
[ContractPermission(Permission.Any, Method.Any)]
public class EmergencyManagerContract : SmartContract
{
    private const byte KeyPaused = 0x01;
    private const byte KeyEmergencyCouncil = 0x02;   // multisig hash that can pause
    private const byte PrefixEscapeConsumed = 0x03;   // 0x03 + chainId(4B) + leafHash(32B) → 1
    private const byte KeySettlementManager = 0x04;
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted whenever the global pause flag toggles.</summary>
    [DisplayName("PauseStateChanged")]
    public static event Action<bool> OnPauseStateChanged = default!;

    /// <summary>Emitted on a successful escape-hatch exit. Includes chainId so off-chain
    /// indexers can attribute the exit to the right L2.</summary>
    [DisplayName("EscapeHatchExit")]
    public static event Action<uint, UInt160, UInt256> OnEscapeHatchExit = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    /// <summary>Emitted when the emergency council is changed.</summary>
    [DisplayName("CouncilChanged")]
    public static event Action<UInt160> OnCouncilChanged = default!;

    /// <summary>Set wiring on deploy. Args: (owner, emergencyCouncil, settlementManager).</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var council = (UInt160)arr[1];
        var settlementManager = (UInt160)arr[2];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(council.IsValid && !council.IsZero, "invalid council");
        ExecutionEngine.Assert(settlementManager.IsValid && !settlementManager.IsZero, "invalid settlement manager");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyEmergencyCouncil }, council);
        Storage.Put(new byte[] { KeySettlementManager }, settlementManager);
        Storage.Put(new byte[] { KeyPaused }, new byte[] { 0 });
    }

    /// <summary>Hash of the SettlementManager contract whose finalized state roots the
    /// escape hatch verifies against.</summary>
    [Safe]
    public static UInt160 GetSettlementManager()
    {
        var raw = Storage.Get(new byte[] { KeySettlementManager });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Governance owner (typically the GovernanceController contract hash).</summary>
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

    /// <summary>Set the emergency council multisig hash. Owner only.</summary>
    public static void SetEmergencyCouncil(UInt160 council)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(council.IsValid && !council.IsZero, "invalid council");
        Storage.Put(new byte[] { KeyEmergencyCouncil }, council);
        OnCouncilChanged(council);
    }

    /// <summary>True when the network is currently paused.</summary>
    [Safe]
    public static bool IsPaused()
    {
        var raw = Storage.Get(new byte[] { KeyPaused });
        return raw != null && ((byte[])raw)[0] == 1;
    }

    /// <summary>Pause the network. Emergency council multisig only.</summary>
    public static void Pause()
    {
        var council = (UInt160)(Storage.Get(new byte[] { KeyEmergencyCouncil }) ?? throw new Exception("council unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(council), "not council");
        Storage.Put(new byte[] { KeyPaused }, new byte[] { 1 });
        OnPauseStateChanged(true);
    }

    /// <summary>Resume the network. Owner / governance only.</summary>
    public static void Resume()
    {
        var owner = GetOwner();
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        Storage.Put(new byte[] { KeyPaused }, new byte[] { 0 });
        OnPauseStateChanged(false);
    }

    /// <summary>
    /// Escape-hatch withdrawal: when an L2 has stalled or its sequencer is malicious, a user
    /// can prove ownership directly to NeoHub and receive the canonical asset. The leaf hash
    /// is verified against the chain's latest finalized state root via SettlementManager —
    /// without that check, a malicious user could submit a leaf for any state, valid or not.
    /// Replay-protected per (chainId, leafHash) so the same proof can't drain twice and so a
    /// leaf valid on one L2 can't be replayed against another.
    /// </summary>
    public static void EscapeHatchExit(uint chainId, UInt160 sender, UInt256 leafHash)
    {
        ExecutionEngine.Assert(IsPaused(), "escape hatch only valid while paused");
        ExecutionEngine.Assert(Runtime.CheckWitness(sender), "not authorized");
        var key = EscapeKey(chainId, leafHash);
        ExecutionEngine.Assert(Storage.Get(key) == null, "escape leaf already consumed");

        // Single-entry-tree fast path: the user supplies the entire state root as the
        // leaf hash. Works only when the L2's canonical state collapses to a single
        // entry (root == leaf) — typically genesis or a deliberately-pruned escape
        // configuration. Multi-entry state trees MUST use EscapeHatchExitWithProof,
        // which takes a Merkle inclusion proof against the canonical state root and
        // is the standard production path.
        var sm = GetSettlementManager();
        ExecutionEngine.Assert(sm != UInt160.Zero, "settlement manager unset");
        var canonicalRoot = (UInt256)Contract.Call(sm, "getCanonicalStateRoot",
            CallFlags.ReadOnly, new object[] { chainId });
        ExecutionEngine.Assert(canonicalRoot.Equals(leafHash),
            "leaf does not match latest finalized state root");

        Storage.Put(key, new byte[] { 1 });
        OnEscapeHatchExit(chainId, sender, leafHash);
    }

    /// <summary>
    /// Production-shape escape hatch: prove ownership of a specific state-tree leaf via
    /// a Merkle inclusion proof against the chain's canonical state root. Lets users
    /// exit individual balance / ownership entries from a multi-entry state tree —
    /// <see cref="EscapeHatchExit"/> only works when the entire state collapses to a
    /// single root-equal-to-leaf shape.
    /// </summary>
    /// <remarks>
    /// The leaf hash is computed off-chain via
    /// <c>Neo.L2.Executor.State.KeyedStateStore.HashEntry(key, value)</c>. The siblings
    /// array + leafIndex come from a Merkle proof generated against the canonical state
    /// tree (built with <c>Neo.L2.State.MerkleTree</c> over all state entries in lex-key
    /// order). Verification is delegated to
    /// <c>SettlementManager.VerifyStateLeafWithProof</c>.
    /// </remarks>
    public static void EscapeHatchExitWithProof(
        uint chainId,
        UInt160 sender,
        UInt256 leafHash,
        byte[][] siblings,
        ulong leafIndex)
    {
        ExecutionEngine.Assert(IsPaused(), "escape hatch only valid while paused");
        ExecutionEngine.Assert(Runtime.CheckWitness(sender), "not authorized");
        var key = EscapeKey(chainId, leafHash);
        ExecutionEngine.Assert(Storage.Get(key) == null, "escape leaf already consumed");

        var sm = GetSettlementManager();
        ExecutionEngine.Assert(sm != UInt160.Zero, "settlement manager unset");
        var verified = (bool)Contract.Call(sm, "verifyStateLeafWithProof",
            CallFlags.ReadOnly,
            new object[] { chainId, leafHash, siblings, leafIndex });
        ExecutionEngine.Assert(verified,
            "leaf does not Merkle-verify against latest finalized state root");

        Storage.Put(key, new byte[] { 1 });
        OnEscapeHatchExit(chainId, sender, leafHash);
    }

    private static byte[] EscapeKey(uint chainId, UInt256 leafHash)
    {
        var k = new byte[1 + 4 + 32];
        k[0] = PrefixEscapeConsumed;
        k[1] = (byte)(chainId & 0xFF);
        k[2] = (byte)((chainId >> 8) & 0xFF);
        k[3] = (byte)((chainId >> 16) & 0xFF);
        k[4] = (byte)((chainId >> 24) & 0xFF);
        var b = (byte[])leafHash;
        for (var i = 0; i < 32; i++) k[1 + 4 + i] = b[i];
        return k;
    }
}
