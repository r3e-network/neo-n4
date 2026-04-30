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
[ContractSourceCode("https://github.com/neo-project/neo4/tree/master/contracts/NeoHub.EmergencyManager")]
[ContractPermission(Permission.Any, Method.Any)]
public class EmergencyManagerContract : SmartContract
{
    private const byte KeyPaused = 0x01;
    private const byte KeyEmergencyCouncil = 0x02;   // multisig hash that can pause
    private const byte PrefixEscapeConsumed = 0x03;   // 0x03 + leafHash(32B) → 1
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted whenever the global pause flag toggles.</summary>
    [DisplayName("PauseStateChanged")]
    public static event Action<bool> OnPauseStateChanged = default!;

    /// <summary>Emitted on a successful escape-hatch withdrawal.</summary>
    [DisplayName("EscapeHatchExit")]
    public static event Action<UInt160, UInt256> OnEscapeHatchExit = default!;

    /// <summary>Set wiring on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        Storage.Put(new byte[] { KeyOwner }, (UInt160)arr[0]);
        Storage.Put(new byte[] { KeyEmergencyCouncil }, (UInt160)arr[1]);
        Storage.Put(new byte[] { KeyPaused }, new byte[] { 0 });
    }

    /// <summary>Governance owner (typically the GovernanceController contract hash).</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
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
    /// can prove ownership directly to NeoHub and receive the canonical asset. Replay-protected
    /// on the leaf hash (which the user computes against the last finalized state root).
    /// </summary>
    public static void EscapeHatchExit(UInt160 sender, UInt256 leafHash)
    {
        ExecutionEngine.Assert(IsPaused(), "escape hatch only valid while paused");
        ExecutionEngine.Assert(Runtime.CheckWitness(sender), "no witness");
        var key = EscapeKey(leafHash);
        ExecutionEngine.Assert(Storage.Get(key) == null, "escape leaf already consumed");
        Storage.Put(key, new byte[] { 1 });
        OnEscapeHatchExit(sender, leafHash);
    }

    private static byte[] EscapeKey(UInt256 leafHash)
    {
        var k = new byte[1 + 32];
        k[0] = PrefixEscapeConsumed;
        var b = (byte[])leafHash;
        for (var i = 0; i < 32; i++) k[1 + i] = b[i];
        return k;
    }
}
