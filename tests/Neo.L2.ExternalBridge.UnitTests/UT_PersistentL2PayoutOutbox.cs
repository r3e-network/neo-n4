using System.Numerics;
using Neo.L2.Bridge.External;
using Neo.L2.Messaging;
using Neo.L2.Persistence;

namespace Neo.L2.ExternalBridge.UnitTests;

[TestClass]
public sealed class UT_PersistentL2PayoutOutbox
{
    private const uint ExternalChainId = 0xE0000038;
    private const uint NeoChainId = 1099;
    private static readonly UInt160 Adapter = H160(0x11);
    private static readonly UInt160 ForeignSender = H160(0x22);
    private static readonly ExternalAssetId ForeignAsset =
        ExternalAssetId.Parse("11223344556677889900aabbccddeeff00112233");
    private static readonly UInt160 NeoAsset = H160(0x44);
    private static readonly UInt160 Recipient = H160(0x55);
    private static readonly UInt256 SourceTransaction = H256(0x66);

    [TestMethod]
    public void Enqueue_ExactReplayIsIdempotentAndMappedAssetConflictFailsClosed()
    {
        using var store = new InMemoryKeyValueStore();
        using var outbox = new PersistentL2PayoutOutbox(store);
        var instruction = Instruction();

        Assert.IsTrue(outbox.Enqueue(instruction));
        Assert.IsFalse(outbox.Enqueue(instruction));
        Assert.ThrowsExactly<InvalidDataException>(() => outbox.Enqueue(
            instruction with { NeoAsset = H160(0x77) }));
        Assert.AreEqual(instruction, outbox.LoadPending().Single().Instruction);
    }

    [TestMethod]
    public void RecordFailure_ReloadsPreparedStateAndPoisonsAtBound()
    {
        using var store = new InMemoryKeyValueStore();
        using var outbox = new PersistentL2PayoutOutbox(store);
        var instruction = Instruction();
        outbox.Enqueue(instruction);
        var enqueued = outbox.LoadPending().Single();
        var prepared = outbox.SavePreparedCredit(enqueued, new byte[] { 1, 2, 3 });

        var retrying = outbox.RecordFailure(
            enqueued, new IOException("ambiguous broadcast"), maximumRetries: 2);
        Assert.AreEqual(L2PayoutRelayState.CreditPrepared, retrying.State);
        Assert.AreEqual(1, retrying.RetryCount);
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 3 }, retrying.PreparedCreditTransaction.ToArray());

        var poisoned = outbox.RecordFailure(
            prepared, new IOException("still unavailable"), maximumRetries: 2);
        Assert.AreEqual(L2PayoutRelayState.Poisoned, poisoned.State);
        Assert.AreEqual(2, poisoned.RetryCount);
        StringAssert.Contains(poisoned.LastError, "still unavailable");
    }

    [TestMethod]
    public void Instruction_DecodePreservesAllSignedPayoutFields()
    {
        var instruction = Instruction(amount: 250);

        Assert.AreEqual(ExternalChainId, instruction.Message.ExternalChainId);
        Assert.AreEqual(NeoChainId, instruction.Message.NeoChainId);
        Assert.AreEqual(7ul, instruction.Message.Nonce);
        Assert.AreEqual(ForeignSender, instruction.Message.Sender);
        Assert.AreEqual(ForeignAsset, instruction.ForeignAsset);
        Assert.AreEqual(NeoAsset, instruction.NeoAsset);
        Assert.AreEqual(Recipient, instruction.Message.Recipient);
        Assert.AreEqual(new BigInteger(250), instruction.Amount);
        Assert.AreEqual(1_900_000_000ul, instruction.Message.DeadlineUnixSeconds);
        Assert.AreEqual(SourceTransaction, instruction.Message.SourceTxRef);
        Assert.AreEqual(
            instruction.Message.MessageHash,
            ExternalMessageHasher.DecodeCanonical(instruction.CanonicalMessageBytes.Span).MessageHash);
    }

    private static L2PayoutInstruction Instruction(BigInteger? amount = null)
    {
        var payload = new ExternalAssetTransferPayload
        {
            ForeignAsset = ForeignAsset,
            Amount = amount ?? 25,
        }.Encode();
        var message = ExternalMessageBuilder.Build(
            ExternalChainId,
            NeoChainId,
            nonce: 7,
            ExternalBridgeDirection.ForeignToNeo,
            ForeignSender,
            Recipient,
            deadlineUnixSeconds: 1_900_000_000,
            SourceTransaction,
            ExternalMessageType.AssetTransfer,
            payload);
        var canonical = ExternalMessageHasher.EncodeCanonical(message);
        return L2PayoutInstruction.Decode(
            sequence: 1,
            Adapter,
            NeoAsset,
            message.MessageHash,
            canonical,
            NeoChainId);
    }

    private static UInt160 H160(byte value) =>
        new(Enumerable.Repeat(value, UInt160.Length).ToArray());

    private static UInt256 H256(byte value) =>
        new(Enumerable.Repeat(value, UInt256.Length).ToArray());
}
