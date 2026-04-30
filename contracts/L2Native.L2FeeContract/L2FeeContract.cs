using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace L2Native.L2FeeContract;

/// <summary>
/// L2-side fee accounting. Splits collected fees among sequencer, prover, and DA layer
/// recipients per a configurable basis-point split. See doc.md §13.1 (L2FeeContract).
/// </summary>
[DisplayName("L2Native.L2FeeContract")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("L2 fee splitter: sequencer / prover / DA shares.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/neo-project/neo4/tree/master/contracts/L2Native.L2FeeContract")]
[ContractPermission(Permission.Any, Method.Any)]
public class L2FeeContract : SmartContract
{
    private const byte KeySequencerBps = 0x01;
    private const byte KeyProverBps = 0x02;
    private const byte KeyDABps = 0x03;
    private const byte KeySequencerAddress = 0x04;
    private const byte KeyProverAddress = 0x05;
    private const byte KeyDAAddress = 0x06;
    private const byte KeyFeeAsset = 0x07;
    private const byte KeyOwner = 0xFF;

    /// <summary>Total basis points; sequencer + prover + DA must sum to this.</summary>
    public const ushort BasisPointsTotal = 10_000;

    /// <summary>Emitted on every Distribute call.</summary>
    [DisplayName("FeesDistributed")]
    public static event Action<BigInteger, BigInteger, BigInteger, BigInteger> OnFeesDistributed = default!;

    /// <summary>Set initial split + recipients on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        Storage.Put(new byte[] { KeyOwner }, (UInt160)arr[0]);
        Storage.Put(new byte[] { KeyFeeAsset }, (UInt160)arr[1]);
        Storage.Put(new byte[] { KeySequencerAddress }, (UInt160)arr[2]);
        Storage.Put(new byte[] { KeyProverAddress }, (UInt160)arr[3]);
        Storage.Put(new byte[] { KeyDAAddress }, (UInt160)arr[4]);
        Storage.Put(new byte[] { KeySequencerBps }, (BigInteger)(uint)(BigInteger)arr[5]);
        Storage.Put(new byte[] { KeyProverBps }, (BigInteger)(uint)(BigInteger)arr[6]);
        Storage.Put(new byte[] { KeyDABps }, (BigInteger)(uint)(BigInteger)arr[7]);
    }

    /// <summary>Update the split (owner only). Must sum to <see cref="BasisPointsTotal"/>.</summary>
    public static void SetBps(uint sequencerBps, uint proverBps, uint daBps)
    {
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        ExecutionEngine.Assert(sequencerBps + proverBps + daBps == BasisPointsTotal, "bps must sum to 10000");
        Storage.Put(new byte[] { KeySequencerBps }, (BigInteger)sequencerBps);
        Storage.Put(new byte[] { KeyProverBps }, (BigInteger)proverBps);
        Storage.Put(new byte[] { KeyDABps }, (BigInteger)daBps);
    }

    /// <summary>Read the current split.</summary>
    [Safe]
    public static uint[] GetBps()
    {
        var sRaw = Storage.Get(new byte[] { KeySequencerBps });
        var pRaw = Storage.Get(new byte[] { KeyProverBps });
        var dRaw = Storage.Get(new byte[] { KeyDABps });
        var s = sRaw == null ? 0u : (uint)(BigInteger)sRaw;
        var p = pRaw == null ? 0u : (uint)(BigInteger)pRaw;
        var d = dRaw == null ? 0u : (uint)(BigInteger)dRaw;
        return new uint[] { s, p, d };
    }

    /// <summary>
    /// Distribute <paramref name="amount"/> fee tokens (held in escrow at this contract) among
    /// the three recipients per current bps. Caller must already have transferred the funds in.
    /// </summary>
    public static void Distribute(BigInteger amount)
    {
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");

        var bps = GetBps();
        var sequencerShare = (amount * bps[0]) / BasisPointsTotal;
        var proverShare = (amount * bps[1]) / BasisPointsTotal;
        var daShare = amount - sequencerShare - proverShare;

        var asset = (UInt160)(Storage.Get(new byte[] { KeyFeeAsset }) ?? throw new Exception("fee asset unset"));
        var sequencerAddr = (UInt160)(Storage.Get(new byte[] { KeySequencerAddress }) ?? UInt160.Zero);
        var proverAddr = (UInt160)(Storage.Get(new byte[] { KeyProverAddress }) ?? UInt160.Zero);
        var daAddr = (UInt160)(Storage.Get(new byte[] { KeyDAAddress }) ?? UInt160.Zero);

        if (sequencerShare > 0)
            Contract.Call(asset, "transfer", CallFlags.All, new object[] { Runtime.ExecutingScriptHash, sequencerAddr, sequencerShare, null! });
        if (proverShare > 0)
            Contract.Call(asset, "transfer", CallFlags.All, new object[] { Runtime.ExecutingScriptHash, proverAddr, proverShare, null! });
        if (daShare > 0)
            Contract.Call(asset, "transfer", CallFlags.All, new object[] { Runtime.ExecutingScriptHash, daAddr, daShare, null! });

        OnFeesDistributed(amount, sequencerShare, proverShare, daShare);
    }
}
