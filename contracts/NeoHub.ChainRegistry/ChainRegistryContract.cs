using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.ChainRegistry;

/// <summary>
/// Registers Neo Elastic Network L2 chains and their configs. Anyone can read; only the
/// governance owner can mutate. See doc.md §3.2 (ChainRegistry).
/// </summary>
[DisplayName("NeoHub.ChainRegistry")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("L2 chain admission and per-chain config registry for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/neo-project/neo4/tree/master/contracts/NeoHub.ChainRegistry")]
[ContractPermission(Permission.Any, Method.Any)]
public class ChainRegistryContract : SmartContract
{
    /// <summary>Storage prefix for the per-chain config record.</summary>
    private const byte PrefixConfig = 0x01;

    /// <summary>Storage prefix for the index of all registered chain ids.</summary>
    private const byte PrefixChainIndex = 0x02;

    /// <summary>Storage key for the owner address.</summary>
    private const byte KeyOwner = 0xFF;

    /// <summary>
    /// Encoded length of an L2ChainConfig. See doc.md §3.2.
    /// 4B chainId + 4×20B (operator/verifier/bridge/message) + 5×1B (security/da/gateway/exit/active) = 89 bytes.
    /// </summary>
    public const int ConfigSize = 4 + 20 * 4 + 5;

    /// <summary>Emitted whenever a chain is registered or updated.</summary>
    [DisplayName("ChainRegistered")]
    public static event Action<uint, byte[]> OnChainRegistered = default!;

    /// <summary>Emitted whenever a chain is paused.</summary>
    [DisplayName("ChainPaused")]
    public static event Action<uint> OnChainPaused = default!;

    /// <summary>Emitted whenever a paused chain is resumed.</summary>
    [DisplayName("ChainResumed")]
    public static event Action<uint> OnChainResumed = default!;

    /// <summary>Initial owner is set on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var initialOwner = (UInt160)data;
        ExecutionEngine.Assert(initialOwner.IsValid && !initialOwner.IsZero, "invalid initial owner");
        Storage.Put(new byte[] { KeyOwner }, initialOwner);
    }

    /// <summary>Look up the governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Transfer governance ownership. Old owner only.</summary>
    public static void SetOwner(UInt160 newOwner)
    {
        ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "invalid new owner");
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Put(new byte[] { KeyOwner }, newOwner);
    }

    /// <summary>Register a new L2 chain. Owner only. Idempotent on chainId.</summary>
    public static void RegisterChain(uint chainId, byte[] configBytes)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        // chainId 0 is the L1 sentinel (see L2Outbox.L1ChainId) — registering a chain
        // with id 0 would silently break L2→L2 routing for every other chain.
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(configBytes.Length == ConfigSize, "config size mismatch");
        ExecutionEngine.Assert(ReadChainId(configBytes) == chainId, "chainId mismatch");

        var key = ConfigKey(chainId);
        var existing = Storage.Get(key);
        Storage.Put(key, configBytes);
        if (existing == null)
            Storage.Put(IndexKey(chainId), new byte[] { 1 });

        OnChainRegistered(chainId, configBytes);
    }

    /// <summary>Update an already-registered chain's config. Owner only.</summary>
    public static void UpdateChain(uint chainId, byte[] configBytes)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(configBytes.Length == ConfigSize, "config size mismatch");
        ExecutionEngine.Assert(ReadChainId(configBytes) == chainId, "chainId mismatch");
        ExecutionEngine.Assert(Storage.Get(ConfigKey(chainId)) != null, "chain not registered");

        Storage.Put(ConfigKey(chainId), configBytes);
        OnChainRegistered(chainId, configBytes);
    }

    /// <summary>Pause a chain. Owner only. Sets active=false in stored config.</summary>
    public static void PauseChain(uint chainId)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        var key = ConfigKey(chainId);
        var raw = Storage.Get(key);
        ExecutionEngine.Assert(raw != null, "chain not registered");
        var bytes = (byte[])raw!;
        // active flag is the very last byte of the encoded config.
        bytes[ConfigSize - 1] = 0;
        Storage.Put(key, bytes);
        OnChainPaused(chainId);
    }

    /// <summary>Resume a paused chain. Owner only.</summary>
    public static void ResumeChain(uint chainId)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        var key = ConfigKey(chainId);
        var raw = Storage.Get(key);
        ExecutionEngine.Assert(raw != null, "chain not registered");
        var bytes = (byte[])raw!;
        bytes[ConfigSize - 1] = 1;
        Storage.Put(key, bytes);
        OnChainResumed(chainId);
    }

    /// <summary>Read the canonical encoded L2ChainConfig. Empty bytes if not registered.</summary>
    [Safe]
    public static byte[] GetChainConfig(uint chainId)
    {
        var raw = Storage.Get(ConfigKey(chainId));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    /// <summary>True if chainId is registered AND active=1.</summary>
    [Safe]
    public static bool IsActive(uint chainId)
    {
        var raw = Storage.Get(ConfigKey(chainId));
        if (raw == null) return false;
        var bytes = (byte[])raw;
        return bytes[ConfigSize - 1] == 1;
    }

    private static byte[] ConfigKey(uint chainId)
    {
        var key = new byte[5];
        key[0] = PrefixConfig;
        key[1] = (byte)chainId;
        key[2] = (byte)(chainId >> 8);
        key[3] = (byte)(chainId >> 16);
        key[4] = (byte)(chainId >> 24);
        return key;
    }

    private static byte[] IndexKey(uint chainId)
    {
        var key = new byte[5];
        key[0] = PrefixChainIndex;
        key[1] = (byte)chainId;
        key[2] = (byte)(chainId >> 8);
        key[3] = (byte)(chainId >> 16);
        key[4] = (byte)(chainId >> 24);
        return key;
    }

    private static uint ReadChainId(byte[] bytes)
    {
        return (uint)bytes[0]
            | ((uint)bytes[1] << 8)
            | ((uint)bytes[2] << 16)
            | ((uint)bytes[3] << 24);
    }
}
