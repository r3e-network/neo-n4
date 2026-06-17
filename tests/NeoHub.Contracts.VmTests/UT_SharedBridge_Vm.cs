using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using System.Security.Cryptography;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>Minimal NEP-17 surface so the bridge's asset transfers can be mocked.</summary>
public abstract class MockNep17(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("transfer")]
    public abstract bool? Transfer(UInt160? from, UInt160? to, BigInteger? amount, object? data);
}

/// <summary>
/// VM-level tests for NeoHub.SharedBridge — the canonical asset escrow. With the TokenRegistry,
/// SettlementManager, and the NEP-17 asset replaced by mocks, these execute the deposit/withdrawal
/// paths in a real NeoVM and pin the C1 per-chain escrow accounting: a chain's withdrawals can never
/// exceed its own deposits, so a (forged) withdrawal for a chain with no escrow cannot drain assets
/// deposited for a different chain.
/// </summary>
[TestClass]
public class UT_SharedBridge_Vm
{
    private static readonly UInt160 AssetHash = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 L2Asset = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 SmHash = UInt160.Parse("0x" + new string('5', 40));
    private static readonly UInt160 TrHash = UInt160.Parse("0x" + new string('6', 40));
    private const uint ChainA = 1001;
    private const uint ChainB = 2002;

    private static byte[] Hash256(byte[] x) => SHA256.HashData(SHA256.HashData(x));

    /// <summary>Mirror SharedBridge.ComputeWithdrawalLeafHash: chainId(4 LE) ‖ emittingContract ‖
    /// l2Sender ‖ l1Recipient ‖ l2Asset ‖ amountLen(4 LE) ‖ amount(minimal unsigned LE) ‖ nonce(8 LE),
    /// then double-SHA256.</summary>
    private static UInt256 LeafHash(uint chainId, UInt160 emitting, UInt160 l2Sender, UInt160 recipient,
        UInt160 l2Asset, BigInteger amount, ulong nonce)
    {
        var amt = amount.ToByteArray(isUnsigned: true, isBigEndian: false);
        var buf = new byte[4 + 20 + 20 + 20 + 20 + 4 + amt.Length + 8];
        var pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), chainId); pos += 4;
        emitting.GetSpan().CopyTo(buf.AsSpan(pos, 20)); pos += 20;
        l2Sender.GetSpan().CopyTo(buf.AsSpan(pos, 20)); pos += 20;
        recipient.GetSpan().CopyTo(buf.AsSpan(pos, 20)); pos += 20;
        l2Asset.GetSpan().CopyTo(buf.AsSpan(pos, 20)); pos += 20;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), (uint)amt.Length); pos += 4;
        amt.CopyTo(buf.AsSpan(pos, amt.Length)); pos += amt.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(pos, 8), nonce); pos += 8;
        return new UInt256(Hash256(buf));
    }

    private static NeoHubSharedBridge Deploy(TestEngine engine)
    {
        var owner = engine.Sender;
        engine.FromHash<NeoHubTokenRegistry>(TrHash, m =>
        {
            m.Setup(c => c.GetL2Asset(It.IsAny<UInt160?>(), It.IsAny<BigInteger?>())).Returns(L2Asset);
            m.Setup(c => c.IsActive(It.IsAny<UInt160?>(), It.IsAny<BigInteger?>())).Returns(true);
        }, checkExistence: false);
        engine.FromHash<MockNep17>(AssetHash, m =>
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.IsAny<UInt160?>(), It.IsAny<BigInteger?>(), It.IsAny<object?>())).Returns(true),
            checkExistence: false);
        engine.FromHash<NeoHubSettlementManager>(SmHash, m =>
            m.Setup(c => c.VerifyWithdrawalLeafWithProof(It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>(),
                It.IsAny<UInt256?>(), It.IsAny<IList<object>?>(), It.IsAny<BigInteger?>())).Returns(true),
            checkExistence: false);

        return engine.Deploy<NeoHubSharedBridge>(
            NeoHubSharedBridge.Nef, NeoHubSharedBridge.Manifest, new object[] { owner, SmHash, TrHash });
    }

    [TestMethod]
    public void Deposit_CreditsPerChainEscrowLedger()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);
        var recipient = UInt160.Parse("0x" + new string('c', 40));

        Assert.AreEqual((BigInteger)0, sb.GetLockedBalance(ChainA, AssetHash));
        sb.Deposit(AssetHash, 1000, ChainA, recipient);
        Assert.AreEqual((BigInteger)1000, sb.GetLockedBalance(ChainA, AssetHash), "deposit must credit chain A's escrow");
        sb.Deposit(AssetHash, 500, ChainA, recipient);
        Assert.AreEqual((BigInteger)1500, sb.GetLockedBalance(ChainA, AssetHash), "second deposit accumulates");
        // Chain B got nothing.
        Assert.AreEqual((BigInteger)0, sb.GetLockedBalance(ChainB, AssetHash));
    }

    [TestMethod]
    public void Withdrawal_CannotDrainAnotherChainsEscrow_C1Isolation()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);
        var emitting = UInt160.Parse("0x" + new string('d', 40));
        var l2Sender = UInt160.Parse("0x" + new string('e', 40));
        var recipient = UInt160.Parse("0x" + new string('c', 40));

        // Fund ONLY chain A.
        sb.Deposit(AssetHash, 1000, ChainA, recipient);

        // A legitimate withdrawal from chain A succeeds and debits A's escrow.
        var leafA = LeafHash(ChainA, emitting, l2Sender, recipient, L2Asset, 600, nonce: 1);
        sb.FinalizeWithdrawalWithProof(ChainA, 1, leafA, new List<object>(), 0,
            emitting, l2Sender, L2Asset, 1, AssetHash, recipient, 600);
        Assert.AreEqual((BigInteger)400, sb.GetLockedBalance(ChainA, AssetHash), "withdrawal debits chain A's escrow");

        // A withdrawal for chain B (which has zero escrow) MUST fail at the per-chain cap — it cannot
        // draw from chain A's deposits. This is the core C1 isolation guarantee.
        var leafB = LeafHash(ChainB, emitting, l2Sender, recipient, L2Asset, 500, nonce: 1);
        Assert.ThrowsExactly<TestException>(() =>
            sb.FinalizeWithdrawalWithProof(ChainB, 1, leafB, new List<object>(), 0,
                emitting, l2Sender, L2Asset, 1, AssetHash, recipient, 500),
            "chain B has no escrow — must not be able to drain chain A's funds");
        // Chain A's balance is untouched by the failed cross-chain attempt.
        Assert.AreEqual((BigInteger)400, sb.GetLockedBalance(ChainA, AssetHash));
    }

    [TestMethod]
    public void Withdrawal_ExceedingChainsOwnEscrow_Fails()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);
        var emitting = UInt160.Parse("0x" + new string('d', 40));
        var l2Sender = UInt160.Parse("0x" + new string('e', 40));
        var recipient = UInt160.Parse("0x" + new string('c', 40));

        sb.Deposit(AssetHash, 100, ChainA, recipient);
        var leaf = LeafHash(ChainA, emitting, l2Sender, recipient, L2Asset, 101, nonce: 1);
        Assert.ThrowsExactly<TestException>(() =>
            sb.FinalizeWithdrawalWithProof(ChainA, 1, leaf, new List<object>(), 0,
                emitting, l2Sender, L2Asset, 1, AssetHash, recipient, 101),
            "withdrawing more than the chain's own escrow must fail");
    }
}
