using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace L2Native.L2SystemConfigContract;

/// <summary>
/// L2-side cache of NeoHub-managed config (bridge, message router, verifier, system addresses,
/// owner). Synced via governance messages from L1; consulted by every L2 native contract
/// that needs to authenticate "is this caller really the system?". See doc.md §13.1
/// (L2SystemConfigContract).
/// </summary>
[DisplayName("L2Native.L2SystemConfigContract")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("L2-side cache of NeoHub-pushed system configuration.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/L2Native.L2SystemConfigContract")]
[ContractPermission(Permission.Any, Method.Any)]
public class L2SystemConfigContract : SmartContract
{
    private const byte KeySystemAccount = 0x01;          // canonical "system" sender on L2
    private const byte KeyL1MessageContract = 0x02;       // L2-side hash that authoritatively delivers L1→L2 msgs
    private const byte KeyBridgeContract = 0x03;
    private const byte KeyMessageContract = 0x04;
    private const byte KeyBatchInfoContract = 0x05;
    private const byte KeyFeeContract = 0x06;
    private const byte KeyPaymasterContract = 0x07;
    private const byte KeyChainId = 0x08;
    private const byte KeySettingsBlob = 0x09;            // arbitrary opaque config blob synced from NeoHub
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted whenever a config slot is updated.</summary>
    [DisplayName("ConfigUpdated")]
    public static event Action<byte, byte[]> OnConfigUpdated = default!;

    /// <summary>Set wiring on deploy. <c>data</c> = [owner, systemAccount, chainId].</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var systemAccount = (UInt160)arr[1];
        var chainId = (uint)(BigInteger)arr[2];
        // Surface typo'd zero hashes here. chainId == 0 is the L1 sentinel (see
        // L2Outbox.L1ChainId) — an L2 with chainId=0 would misroute every L2→L2
        // message as L2→L1.
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(systemAccount.IsValid && !systemAccount.IsZero, "invalid system account");
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeySystemAccount }, systemAccount);
        Storage.Put(new byte[] { KeyChainId }, (BigInteger)chainId);
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Canonical L2 system account.</summary>
    [Safe]
    public static UInt160 GetSystemAccount()
    {
        var raw = Storage.Get(new byte[] { KeySystemAccount });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Local L2 chain id.</summary>
    [Safe]
    public static uint GetChainId()
    {
        var raw = Storage.Get(new byte[] { KeyChainId });
        return raw == null ? 0u : (uint)(BigInteger)raw;
    }

    /// <summary>Update a single config slot. Owner-gated.</summary>
    public static void SetSlot(byte slot, byte[] value)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(slot >= 0x02 && slot <= 0x09, "slot out of range");
        Storage.Put(new byte[] { slot }, value);
        OnConfigUpdated(slot, value);
    }

    /// <summary>Read a config slot's raw bytes (or empty if unset).</summary>
    [Safe]
    public static byte[] GetSlot(byte slot)
    {
        var raw = Storage.Get(new byte[] { slot });
        return raw == null ? new byte[0] : (byte[])raw;
    }

    /// <summary>Convenience: read a slot as UInt160.</summary>
    [Safe]
    public static UInt160 GetAddressSlot(byte slot)
    {
        var raw = Storage.Get(new byte[] { slot });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }
}
