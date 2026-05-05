using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace L2Native.L2BatchInfoContract;

/// <summary>
/// L2-side native contract that exposes the chain identifier, current sealed batch number,
/// and L1 finalized height to other on-L2 contracts. See doc.md §13.1 (L2BatchInfoContract).
/// </summary>
[DisplayName("L2Native.L2BatchInfoContract")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Exposes batch context (chainId, batchNumber, L1 height) to L2 contracts.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/neo-project/neo4/tree/master/contracts/L2Native.L2BatchInfoContract")]
[ContractPermission(Permission.Any, Method.Any)]
public class L2BatchInfoContract : SmartContract
{
    private const byte KeyChainId = 0x01;
    private const byte KeyBatchNumber = 0x02;
    private const byte KeyL1FinalizedHeight = 0x03;
    private const byte KeySystemAccount = 0xFE;
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted by the system at every batch seal.</summary>
    [DisplayName("BatchAdvanced")]
    public static event Action<uint, ulong, uint> OnBatchAdvanced = default!;

    /// <summary>Set the chainId once at deploy time.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var systemAccount = (UInt160)arr[1];
        var chainId = (uint)(BigInteger)arr[2];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(systemAccount.IsValid && !systemAccount.IsZero, "invalid system account");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeySystemAccount }, systemAccount);
        Storage.Put(new byte[] { KeyChainId }, (BigInteger)chainId);
    }

    /// <summary>The L2 chain identifier.</summary>
    [Safe]
    public static uint GetChainId()
    {
        var raw = Storage.Get(new byte[] { KeyChainId });
        return raw == null ? 0u : (uint)(BigInteger)raw;
    }

    /// <summary>The current sealed batch number on this L2.</summary>
    [Safe]
    public static ulong GetBatchNumber()
    {
        var raw = Storage.Get(new byte[] { KeyBatchNumber });
        return raw == null ? 0UL : (ulong)(BigInteger)raw;
    }

    /// <summary>The L1 block height that was finalized at the start of the current batch.</summary>
    [Safe]
    public static uint GetL1FinalizedHeight()
    {
        var raw = Storage.Get(new byte[] { KeyL1FinalizedHeight });
        return raw == null ? 0u : (uint)(BigInteger)raw;
    }

    /// <summary>
    /// Called by the L2 sequencer system account when sealing a batch. Updates the
    /// batch number and L1 finalized height.
    /// </summary>
    public static void Advance(ulong newBatchNumber, uint newL1Height)
    {
        var systemAccount = (UInt160)(Storage.Get(new byte[] { KeySystemAccount }) ?? throw new Exception("system account unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(systemAccount), "not system");

        var current = GetBatchNumber();
        ExecutionEngine.Assert(newBatchNumber == current + 1, "batch number out of sequence");

        // L1 finalized height must monotonically increase (or stay equal — same L1 block
        // can finalize multiple consecutive L2 batches when batch interval < L1 block
        // time). Without this guard a sequencer mistake that passes a stale L1 height
        // would silently break apps relying on L1FinalizedHeight as a "no-deeper-rollback"
        // confirmation signal.
        var currentL1 = GetL1FinalizedHeight();
        ExecutionEngine.Assert(newL1Height >= currentL1, "L1 finalized height must not decrease");

        Storage.Put(new byte[] { KeyBatchNumber }, (BigInteger)newBatchNumber);
        Storage.Put(new byte[] { KeyL1FinalizedHeight }, (BigInteger)newL1Height);

        OnBatchAdvanced(GetChainId(), newBatchNumber, newL1Height);
    }
}
