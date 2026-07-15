using System.Buffers.Binary;
using System.Numerics;
using Neo;
using Neo.Cryptography;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>VM proof for the authenticated, exact-field-bound L2 payout queue.</summary>
[TestClass]
public sealed class UT_L2PayoutAdapter_Vm
{
    private const uint ExternalChainId = 0xE0000001;
    private const uint NeoChainId = 1099;
    private const ulong Nonce = 17;
    private static readonly UInt160 Escrow = U160('1');
    private static readonly UInt160 Relay = U160('2');
    private static readonly UInt160 ForeignSender = U160('3');
    private static readonly UInt160 ForeignAsset = U160('4');
    private static readonly UInt160 NeoAsset = U160('5');
    private static readonly UInt160 Recipient = U160('6');
    private static readonly UInt160 Stranger = U160('7');
    private static readonly UInt256 SourceTransaction = U256('8');
    private static readonly UInt256 L2Transaction = U256('9');

    [TestMethod]
    public void Payout_AuthenticatesBindsPersistsAndAcknowledgesIdempotently()
    {
        var engine = new TestEngine(true);
        var adapter = engine.Deploy<NeoHubL2PayoutAdapter>(
            NeoHubL2PayoutAdapter.Nef,
            NeoHubL2PayoutAdapter.Manifest,
            new object[] { engine.Sender, Escrow, (BigInteger)NeoChainId, Relay });
        var message = BuildMessage(amount: 250);
        var messageHash = new UInt256(Crypto.Hash256(message));

        Assert.AreEqual((BigInteger)1, adapter.PayoutVersion);
        Assert.AreEqual(Escrow, adapter.Escrow);
        Assert.AreEqual((BigInteger)NeoChainId, adapter.NeoChainId);
        Assert.AreEqual(Relay, adapter.RelayAccount);
        Assert.ThrowsExactly<TestException>(() => adapter.Payout(
            ExternalChainId, NeoChainId, Nonce, ForeignAsset, NeoAsset, Recipient,
            250, 0, SourceTransaction, message));

        engine.OnGetCallingScriptHash = (_, _) => Escrow;
        try
        {
            Assert.IsTrue(adapter.Payout(
                ExternalChainId, NeoChainId, Nonce, ForeignAsset, NeoAsset, Recipient,
                250, 0, SourceTransaction, message));
            Assert.AreEqual((BigInteger)1, adapter.LastSequence);
            Assert.AreEqual((BigInteger)1, adapter.GetPayoutStatus(1));
            Assert.AreEqual(messageHash, adapter.GetPayoutMessageHash(1));
            Assert.AreEqual(NeoAsset, adapter.GetPayoutNeoAsset(1));
            CollectionAssert.AreEqual(message, adapter.GetPayoutMessage(1));

            Assert.IsTrue(adapter.Payout(
                ExternalChainId, NeoChainId, Nonce, ForeignAsset, NeoAsset, Recipient,
                250, 0, SourceTransaction, message));
            Assert.AreEqual((BigInteger)1, adapter.LastSequence,
                "exact queue replay must not allocate another sequence");
            Assert.ThrowsExactly<TestException>(() => adapter.Payout(
                ExternalChainId, NeoChainId, Nonce, ForeignAsset, NeoAsset, Stranger,
                250, 0, SourceTransaction, message));
        }
        finally
        {
            engine.OnGetCallingScriptHash = null;
        }

        engine.SetTransactionSigners(Stranger);
        Assert.ThrowsExactly<TestException>(() => adapter.Acknowledge(1, messageHash, L2Transaction));
        engine.SetTransactionSigners(Relay);
        Assert.IsTrue(adapter.Acknowledge(1, messageHash, L2Transaction));
        Assert.AreEqual((BigInteger)2, adapter.GetPayoutStatus(1));
        Assert.AreEqual(L2Transaction, adapter.GetPayoutL2TransactionHash(1));
        Assert.IsTrue(adapter.Acknowledge(1, messageHash, L2Transaction));
        Assert.ThrowsExactly<TestException>(() => adapter.Acknowledge(1, messageHash, U256('a')));
    }

    private static byte[] BuildMessage(BigInteger amount)
    {
        var amountBytes = amount.ToByteArray(isUnsigned: true, isBigEndian: false);
        var payloadLength = 24 + amountBytes.Length;
        var bytes = new byte[102 + payloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), ExternalChainId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), NeoChainId);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8, 8), Nonce);
        bytes[16] = 2;
        ForeignSender.GetSpan().CopyTo(bytes.AsSpan(17, UInt160.Length));
        Recipient.GetSpan().CopyTo(bytes.AsSpan(37, UInt160.Length));
        SourceTransaction.GetSpan().CopyTo(bytes.AsSpan(65, UInt256.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(98, 4), (uint)payloadLength);
        ForeignAsset.GetSpan().CopyTo(bytes.AsSpan(102, UInt160.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(122, 4), (uint)amountBytes.Length);
        amountBytes.CopyTo(bytes, 126);
        return bytes;
    }

    private static UInt160 U160(char value) => UInt160.Parse("0x" + new string(value, 40));

    private static UInt256 U256(char value) => UInt256.Parse("0x" + new string(value, 64));
}
