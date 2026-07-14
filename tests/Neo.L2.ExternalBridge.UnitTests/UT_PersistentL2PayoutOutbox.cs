using System.Numerics;
using Neo.L2.Bridge.External;
using Neo.L2.Messaging;
using Neo.L2.Persistence;

using static Neo.L2.ExternalBridge.UnitTests.PayoutTestData;

namespace Neo.L2.ExternalBridge.UnitTests;

[TestClass]
public sealed class UT_PersistentL2PayoutOutbox
{
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

    [TestMethod]
    public void Instruction_ValueSemanticsAndHashCodeBindCanonicalBytes()
    {
        var instruction = Instruction();
        var equal = Instruction();

        Assert.AreEqual(instruction, equal);
        Assert.AreEqual(instruction.GetHashCode(), equal.GetHashCode());
        Assert.AreNotEqual(instruction, null);
        Assert.AreNotEqual(instruction, instruction with { Sequence = 2 });
        Assert.AreNotEqual(instruction, instruction with { Adapter = H160(0x81) });
        Assert.AreNotEqual(instruction, instruction with { NeoAsset = H160(0x82) });
        Assert.AreNotEqual(instruction, instruction with
        {
            Message = instruction.Message with { Nonce = instruction.Message.Nonce + 1 },
        });
        Assert.AreNotEqual(instruction, instruction with
        {
            ForeignAsset = ExternalAssetId.Parse("223344556677889900aabbccddeeff0011223344"),
        });
        Assert.AreNotEqual(instruction, instruction with { Amount = instruction.Amount + 1 });
        Assert.AreNotEqual(instruction, instruction with { CanonicalMessageBytes = new byte[] { 1 } });
    }

    [TestMethod]
    public void Instruction_DecodeRejectsUnsafeEnvelopeFields()
    {
        var instruction = Instruction();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => L2PayoutInstruction.Decode(
            0, Adapter, NeoAsset, instruction.Message.MessageHash,
            instruction.CanonicalMessageBytes, NeoChainId));
        Assert.ThrowsExactly<ArgumentException>(() => L2PayoutInstruction.Decode(
            1, UInt160.Zero, NeoAsset, instruction.Message.MessageHash,
            instruction.CanonicalMessageBytes, NeoChainId));
        Assert.ThrowsExactly<ArgumentException>(() => L2PayoutInstruction.Decode(
            1, Adapter, UInt160.Zero, instruction.Message.MessageHash,
            instruction.CanonicalMessageBytes, NeoChainId));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => L2PayoutInstruction.Decode(
            1, Adapter, NeoAsset, instruction.Message.MessageHash,
            instruction.CanonicalMessageBytes, 0));
        Assert.ThrowsExactly<InvalidDataException>(() => L2PayoutInstruction.Decode(
            1, Adapter, NeoAsset, H256(0x83), instruction.CanonicalMessageBytes, NeoChainId));
        Assert.ThrowsExactly<InvalidDataException>(() => L2PayoutInstruction.Decode(
            1, Adapter, NeoAsset, instruction.Message.MessageHash,
            instruction.CanonicalMessageBytes, NeoChainId + 1));
    }

}
