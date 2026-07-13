using Neo.L2.Executor.Effects;
using Neo.L2.Executor.Receipts;
using Neo.SmartContract;
using Neo.VM;
using Neo.VM.Types;
using Array = Neo.VM.Types.Array;

namespace Neo.L2.Executor.UnitTests;

/// <summary>Conformance tests for versioned V1 execution-effect hashing.</summary>
[TestClass]
public class UT_CanonicalExecutionEffects
{
    [TestMethod]
    public void EmptyEffects_MapBothHashesToUInt256Zero()
    {
        Assert.AreEqual(UInt256.Zero, CanonicalExecutionEffects.Empty.StorageHash);
        Assert.AreEqual(UInt256.Zero, CanonicalExecutionEffects.Empty.EventsHash);
    }

    [TestMethod]
    public void StorageHash_SortsByFullKeyAndBindsEveryTransitionField()
    {
        var first = Change([0x02], CanonicalStorageOperation.Put, [0x20], [0x21]);
        var second = Change([0x01], CanonicalStorageOperation.Delete, [0x10], null);

        var forward = CanonicalEffectsHasher.HashStorage([first, second]);
        var reversed = CanonicalEffectsHasher.HashStorage([second, first]);

        Assert.AreEqual(forward, reversed);
        Assert.AreNotEqual(forward, CanonicalEffectsHasher.HashStorage(
            [Change([0x02], CanonicalStorageOperation.Put, [0x20], [0x22]), second]));
        Assert.AreNotEqual(forward, CanonicalEffectsHasher.HashStorage(
            [Change([0x03], CanonicalStorageOperation.Put, [0x20], [0x21]), second]));
        Assert.AreNotEqual(forward, CanonicalEffectsHasher.HashStorage(
            [Change([0x02], CanonicalStorageOperation.Put, [0x19], [0x21]), second]));
    }

    [TestMethod]
    public void StorageHash_RejectsDuplicateFullKeysAndInvalidDelete()
    {
        var duplicate = Change([0x01], CanonicalStorageOperation.Put, null, [0x01]);
        Assert.ThrowsExactly<ArgumentException>(
            () => CanonicalEffectsHasher.SerializeStorage([duplicate, duplicate]));
        Assert.ThrowsExactly<ArgumentException>(
            () => CanonicalEffectsHasher.SerializeStorage(
                [Change([0x02], CanonicalStorageOperation.Delete, [0x01], [0x02])]));
    }

    [TestMethod]
    public void EventsHash_BindsOrderScriptNameAndFullCanonicalState()
    {
        var hashA = UInt160.Parse("0x1111111111111111111111111111111111111111");
        var hashB = UInt160.Parse("0x2222222222222222222222222222222222222222");
        var first = Event(hashA, "Changed", new Array(null, [new Integer(1), new ByteString(new byte[] { 0xAA })]));
        var second = Event(hashB, "Changed", new Array(null, [new Integer(2), new ByteString(new byte[] { 0xBB })]));

        var ordered = CanonicalExecutionEffects.Create([], [first, second]);
        var reversed = CanonicalExecutionEffects.Create([], [second, first]);
        var changedArgument = CanonicalExecutionEffects.Create(
            [],
            [Event(hashA, "Changed", new Array(null, [new Integer(9), new ByteString(new byte[] { 0xAA })])), second]);
        var changedName = CanonicalExecutionEffects.Create(
            [],
            [Event(hashA, "Other", new Array(null, [new Integer(1), new ByteString(new byte[] { 0xAA })])), second]);

        Assert.AreNotEqual(ordered.EventsHash, reversed.EventsHash);
        Assert.AreNotEqual(ordered.EventsHash, changedArgument.EventsHash);
        Assert.AreNotEqual(ordered.EventsHash, changedName.EventsHash);
        CollectionAssert.AreEqual(
            BinarySerializer.Serialize(
                first.State,
                ApplicationEngine.MaxNotificationSize,
                ExecutionEngineLimits.Default.MaxStackSize),
            ordered.Events[0].State.ToArray());
    }

    [TestMethod]
    public void ReceiptEncoding_RemainsExactly105Bytes()
    {
        var receipt = new Receipt
        {
            TxHash = UInt256.Zero,
            Success = true,
            GasConsumed = 123,
            StorageDeltaHash = UInt256.Zero,
            EventsHash = UInt256.Zero,
        };

        Assert.AreEqual(105, Receipt.ReceiptHashSize);
        Assert.AreEqual(105, receipt.EncodeHashData().Length);
    }

    private static CanonicalStorageChange Change(
        byte[] key,
        CanonicalStorageOperation operation,
        byte[]? oldValue,
        byte[]? newValue)
        => new()
        {
            Key = key,
            Operation = operation,
            OldValue = Optional(oldValue),
            NewValue = Optional(newValue),
        };

    private static ReadOnlyMemory<byte>? Optional(byte[]? value)
    {
        if (value is null) return null;
        return new ReadOnlyMemory<byte>(value);
    }

    private static NotifyEventArgs Event(UInt160 hash, string name, Array state)
        => new(null, hash, name, state);
}
