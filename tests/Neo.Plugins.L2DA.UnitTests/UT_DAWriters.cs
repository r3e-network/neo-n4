using Neo.Cryptography;

namespace Neo.Plugins.L2DA.UnitTests;

[TestClass]
public class UT_DAWriters
{
    [TestMethod]
    public async Task InMemory_PublishHashesPayload()
    {
        var w = new InMemoryDAWriter();
        var payload = new byte[] { 1, 2, 3, 4 };
        var receipt = await w.PublishAsync(new DAPublishRequest
        {
            ChainId = 1001,
            BatchNumber = 1,
            Payload = payload,
        });

        Assert.AreEqual(DAMode.External, receipt.Layer);
        Assert.AreEqual(new UInt256(Crypto.Hash256(payload)), receipt.Commitment);
        Assert.IsTrue(await w.IsAvailableAsync(receipt));
    }

    [TestMethod]
    public async Task InMemory_AvailabilityFalseForUnknownReceipt()
    {
        var w = new InMemoryDAWriter();
        var fake = new DAReceipt
        {
            Commitment = UInt256.Parse("0x" + new string('f', 64)),
            Pointer = ReadOnlyMemory<byte>.Empty,
            Layer = DAMode.External,
        };
        Assert.IsFalse(await w.IsAvailableAsync(fake));
    }

    [TestMethod]
    public async Task NeoFsLike_PublishProducesPointer()
    {
        var w = new NeoFsLikeDAWriter();
        var receipt = await w.PublishAsync(new DAPublishRequest
        {
            ChainId = 1001,
            BatchNumber = 5,
            Payload = new byte[] { 0xAA, 0xBB },
        });

        Assert.AreEqual(DAMode.NeoFS, receipt.Layer);
        Assert.AreEqual(36, receipt.Pointer.Length);            // 4B chainId + 32B objectId
        Assert.AreEqual(0xE9, receipt.Pointer.Span[0]);          // 1001 = 0x000003E9 LE → low byte 0xE9
        Assert.AreEqual(0x03, receipt.Pointer.Span[1]);
        Assert.IsTrue(await w.IsAvailableAsync(receipt));
        Assert.AreEqual(1, w.ObjectCount);
    }

    [TestMethod]
    public async Task NeoFsLike_IdempotentPublish()
    {
        var w = new NeoFsLikeDAWriter();
        var payload = new byte[] { 0x10, 0x20 };

        var r1 = await w.PublishAsync(new DAPublishRequest { ChainId = 1001, BatchNumber = 1, Payload = payload });
        var r2 = await w.PublishAsync(new DAPublishRequest { ChainId = 1001, BatchNumber = 2, Payload = payload });

        Assert.AreEqual(r1.Commitment, r2.Commitment);          // content-addressed
        Assert.AreEqual(1, w.ObjectCount);                       // dedup'd
    }

    [TestMethod]
    public async Task NeoFsLike_PerChainIsolation()
    {
        var w = new NeoFsLikeDAWriter();
        var payload = new byte[] { 0x99 };

        var r1 = await w.PublishAsync(new DAPublishRequest { ChainId = 1001, BatchNumber = 1, Payload = payload });
        var r2 = await w.PublishAsync(new DAPublishRequest { ChainId = 1002, BatchNumber = 1, Payload = payload });

        Assert.AreEqual(r1.Commitment, r2.Commitment);          // same content
        Assert.AreEqual(2, w.ObjectCount);                       // separate per-chain entries
        Assert.IsNotNull(w.TryGet(1001, r1.Commitment));
        Assert.IsNotNull(w.TryGet(1002, r2.Commitment));
    }

    [TestMethod]
    public async Task NeoFsLike_AvailabilityFalseForBadPointer()
    {
        var w = new NeoFsLikeDAWriter();
        var bad = new DAReceipt
        {
            Commitment = UInt256.Zero,
            Pointer = new byte[10], // wrong length
            Layer = DAMode.NeoFS,
        };
        Assert.IsFalse(await w.IsAvailableAsync(bad));
    }

    [TestMethod]
    public async Task NeoFsLike_AvailabilityFalseForUnknownObject()
    {
        var w = new NeoFsLikeDAWriter();
        await w.PublishAsync(new DAPublishRequest { ChainId = 1001, BatchNumber = 1, Payload = new byte[] { 1 } });

        var pointer = new byte[36];
        pointer[0] = 0xE9;
        pointer[1] = 3; // chainId 1001 differs (still LE-encoded fake)
        var fake = new DAReceipt
        {
            Commitment = UInt256.Parse("0x" + new string('f', 64)),
            Pointer = pointer,
            Layer = DAMode.NeoFS,
        };
        Assert.IsFalse(await w.IsAvailableAsync(fake));
    }
}
