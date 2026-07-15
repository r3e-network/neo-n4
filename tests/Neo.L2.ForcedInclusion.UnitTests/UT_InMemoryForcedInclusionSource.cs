namespace Neo.L2.ForcedInclusion.UnitTests;

[TestClass]
public class UT_InMemoryForcedInclusionSource
{
    private static ForcedInclusionEntry MkEntry(ulong nonce, uint deadlineSec) => new()
    {
        Nonce = nonce,
        Sender = UInt160.Parse("0x" + new string('a', 40)),
        TxHash = UInt256.Parse("0x" + new string('1', 64)),
        SerializedTx = new byte[] { (byte)nonce, 0xCC },
        DeadlineUnixSeconds = deadlineSec,
    };

    [TestMethod]
    public async Task DrainPreservesNonceOrder()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(3, 1_700_010_000));
        src.Enqueue(MkEntry(1, 1_700_005_000));
        src.Enqueue(MkEntry(2, 1_700_007_000));

        var drained = await src.DrainAsync(10);
        Assert.AreEqual(3, drained.Count);
        Assert.AreEqual(1UL, drained[0].Nonce);
        Assert.AreEqual(2UL, drained[1].Nonce);
        Assert.AreEqual(3UL, drained[2].Nonce);
    }

    [TestMethod]
    public async Task MarkConsumedRemovesFromPending()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_005_000));
        src.Enqueue(MkEntry(2, 1_700_007_000));

        await src.ConfirmConsumedAsync(1);
        Assert.AreEqual(1, src.PendingCount);

        var drained = await src.DrainAsync(10);
        Assert.AreEqual(1, drained.Count);
        Assert.AreEqual(2UL, drained[0].Nonce);
    }

    [TestMethod]
    public void EnqueueDuplicateNonceThrows()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_005_000));
        Assert.ThrowsExactly<InvalidOperationException>(() => src.Enqueue(MkEntry(1, 1_700_006_000)));
    }

    [TestMethod]
    public async Task EnqueueAfterConsumeThrows()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_005_000));
        await src.ConfirmConsumedAsync(1);
        Assert.ThrowsExactly<InvalidOperationException>(() => src.Enqueue(MkEntry(1, 1_700_006_000)));
    }

    [TestMethod]
    public async Task HasOverdueDetectsLatePast()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_000_000));
        Assert.IsFalse(await src.HasOverdueEntryAsync(1_699_999_999));
        Assert.IsTrue(await src.HasOverdueEntryAsync(1_700_000_000));
        Assert.IsTrue(await src.HasOverdueEntryAsync(1_700_000_001));
    }

    [TestMethod]
    public async Task DrainCapsAtMax()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        for (ulong i = 1; i <= 10; i++) src.Enqueue(MkEntry(i, 1_700_000_000));
        var drained = await src.DrainAsync(3);
        Assert.AreEqual(3, drained.Count);
    }

    [TestMethod]
    public async Task DrainEmptyReturnsEmpty()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        var drained = await src.DrainAsync(10);
        Assert.AreEqual(0, drained.Count);
    }

    [TestMethod]
    public async Task MarkConsumedNonexistentThrows()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await src.ConfirmConsumedAsync(99));
    }

    [TestMethod]
    public void Enqueue_RejectsNullEntry()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        Assert.ThrowsExactly<ArgumentNullException>(() => src.Enqueue(null!));
    }

    [TestMethod]
    public async Task DrainAsync_ZeroOrNegativeMax_ReturnsEmpty()
    {
        // Boundary: max <= 0 short-circuits to empty before lock acquire.
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_000_000));
        Assert.AreEqual(0, (await src.DrainAsync(0)).Count);
        Assert.AreEqual(0, (await src.DrainAsync(-5)).Count);
        Assert.AreEqual(1, src.PendingCount, "DrainAsync with max<=0 must not consume");
    }

    [TestMethod]
    public async Task HasOverdueEntryAsync_EmptyQueue_ReturnsFalse()
    {
        // Boundary: empty pending → no overdue regardless of clock.
        var src = new InMemoryForcedInclusionSource(1001);
        Assert.IsFalse(await src.HasOverdueEntryAsync(uint.MaxValue));
        Assert.IsFalse(await src.HasOverdueEntryAsync(0));
    }
}
