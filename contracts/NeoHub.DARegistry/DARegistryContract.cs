using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.DARegistry;

/// <summary>
/// Records per-batch DA commitments and the DA mode each chain was running when it submitted.
/// See doc.md §3.2 (DARegistry) and §12 (Data Availability tiers).
/// </summary>
[DisplayName("NeoHub.DARegistry")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("DA commitment registry for Neo Elastic Network L2 batches.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.DARegistry")]
[ContractPermission(Permission.Any, Method.Any)]
public class DARegistryContract : SmartContract
{
    private const byte PrefixCommitment = 0x01;   // 0x01 + chainId(4B) + batchNum(8B) → 32B commitment
    private const byte PrefixMode = 0x02;         // 0x02 + chainId(4B) + batchNum(8B) → 1B DAMode
    private const byte PrefixSettlementManager = 0xFD;
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted whenever a DA commitment is recorded.</summary>
    [DisplayName("CommitmentRecorded")]
    public static event Action<uint, ulong, UInt256, byte> OnCommitmentRecorded = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    /// <summary>Set wiring on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var settlementManager = (UInt160)arr[1];
        // Surface a typo'd zero / invalid hash here, not at first use.
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

    /// <summary>SettlementManager calls this when sealing a batch to record its DA tuple.</summary>
    public static void Record(uint chainId, ulong batchNumber, UInt256 commitment, byte daMode)
    {
        var sm = (UInt160)(Storage.Get(new byte[] { PrefixSettlementManager }) ?? throw new Exception("sm unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(sm) || Runtime.CallingScriptHash.Equals(sm), "not settlement manager");
        // DAMode enum: L1=0, NeoFS=1, External=2, DAC=3 (matches Neo.L2.DAMode).
        // Without this range guard a buggy SettlementManager refactor could write
        // daMode=99 here, which any later GetMode reader would interpret as garbage.
        ExecutionEngine.Assert(daMode <= 3, "daMode must be 0..3 (L1/NeoFS/External/DAC)");
        Storage.Put(Key(PrefixCommitment, chainId, batchNumber), (byte[])commitment);
        Storage.Put(Key(PrefixMode, chainId, batchNumber), new byte[] { daMode });
        OnCommitmentRecorded(chainId, batchNumber, commitment, daMode);
    }

    /// <summary>Look up a recorded DA commitment.</summary>
    [Safe]
    public static UInt256 GetCommitment(uint chainId, ulong batchNumber)
    {
        var raw = Storage.Get(Key(PrefixCommitment, chainId, batchNumber));
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    /// <summary>Look up the DA mode that was active when a batch was sealed.</summary>
    [Safe]
    public static byte GetMode(uint chainId, ulong batchNumber)
    {
        var raw = Storage.Get(Key(PrefixMode, chainId, batchNumber));
        return raw == null ? (byte)0 : ((byte[])raw)[0];
    }

    private static byte[] Key(byte prefix, uint chainId, ulong batchNumber)
    {
        var k = new byte[13];
        k[0] = prefix;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        k[5] = (byte)batchNumber; k[6] = (byte)(batchNumber >> 8); k[7] = (byte)(batchNumber >> 16); k[8] = (byte)(batchNumber >> 24);
        k[9] = (byte)(batchNumber >> 32); k[10] = (byte)(batchNumber >> 40); k[11] = (byte)(batchNumber >> 48); k[12] = (byte)(batchNumber >> 56);
        return k;
    }
}
