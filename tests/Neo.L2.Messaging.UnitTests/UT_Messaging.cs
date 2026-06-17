namespace Neo.L2.Messaging.UnitTests;

[TestClass]
public class UT_Messaging
{
    private static CrossChainMessage Build(uint target, ulong nonce, MessageType kind = MessageType.Call)
        => MessageBuilder.Build(
            sourceChainId: 1001,
            targetChainId: target,
            nonce: nonce,
            sender: UInt160.Parse("0x" + new string('a', 40)),
            receiver: UInt160.Parse("0x" + new string('b', 40)),
            messageType: kind,
            payload: new byte[] { 0xAA, 0xBB });

    [TestMethod]
    public void MessageBuilder_RejectsNullSender()
    {
        // Regression: previously a null sender slipped past the C# nullable analysis
        // (UInt160 is reference; `required` only forces "must be set," not "non-null")
        // and crashed deep in MessageHasher.HashMessage's GetSpan(). Now caught at
        // the Build boundary.
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            MessageBuilder.Build(
                sourceChainId: 1001,
                targetChainId: 0,
                nonce: 1,
                sender: null!,
                receiver: UInt160.Parse("0x" + new string('b', 40)),
                messageType: MessageType.Call,
                payload: ReadOnlyMemory<byte>.Empty));
    }

    [TestMethod]
    public void MessageBuilder_RejectsSelfRoutedMessage()
    {
        // Self-routed messages (sourceChainId == targetChainId) have no cross-chain
        // transport — gateways have no route from X→X. Pin the rejection so a
        // refactor that drops it doesn't silently emit messages that the gateway
        // would later drop without explanation.
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            MessageBuilder.Build(
                sourceChainId: 1001,
                targetChainId: 1001,
                nonce: 1,
                sender: UInt160.Parse("0x" + new string('a', 40)),
                receiver: UInt160.Parse("0x" + new string('b', 40)),
                messageType: MessageType.Call,
                payload: ReadOnlyMemory<byte>.Empty));
        StringAssert.Contains(ex.Message, "1001");
        StringAssert.Contains(ex.Message, "self-routed");
    }

    [TestMethod]
    public void MessageBuilder_RejectsZeroToZeroSelfRoute()
    {
        // Edge case: both source AND target = 0 (which would be "L1 to L1") is
        // also self-routed and should be rejected — pre-fix, the L1ChainId=0
        // sentinel lets this slip past any "non-zero" guard.
        Assert.ThrowsExactly<ArgumentException>(() =>
            MessageBuilder.Build(
                sourceChainId: 0,
                targetChainId: 0,
                nonce: 1,
                sender: UInt160.Parse("0x" + new string('a', 40)),
                receiver: UInt160.Parse("0x" + new string('b', 40)),
                messageType: MessageType.Call,
                payload: ReadOnlyMemory<byte>.Empty));
    }

    [TestMethod]
    public void MessageBuilder_RejectsNullReceiver()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            MessageBuilder.Build(
                sourceChainId: 1001,
                targetChainId: 0,
                nonce: 1,
                sender: UInt160.Parse("0x" + new string('a', 40)),
                receiver: null!,
                messageType: MessageType.Call,
                payload: ReadOnlyMemory<byte>.Empty));
    }

    [TestMethod]
    public void MessageBuilder_FillsHash()
    {
        var m = Build(0, 1);
        Assert.AreNotEqual(UInt256.Zero, m.MessageHash);

        var same = Build(0, 1);
        Assert.AreEqual(m.MessageHash, same.MessageHash);

        var diff = Build(0, 2);
        Assert.AreNotEqual(m.MessageHash, diff.MessageHash);
    }

    [TestMethod]
    public void Inbox_DequeueRespectsFifo()
    {
        var inbox = new L1MessageInbox();
        for (ulong i = 1; i <= 3; i++) inbox.Enqueue(Build(0, i));
        Assert.AreEqual(3, inbox.PendingCount);

        var taken = inbox.Dequeue(2);
        Assert.AreEqual(2, taken.Count);
        Assert.AreEqual(1UL, taken[0].Nonce);
        Assert.AreEqual(2UL, taken[1].Nonce);
        Assert.AreEqual(1, inbox.PendingCount);
        Assert.AreEqual(2, inbox.ConsumedCount);
    }

    [TestMethod]
    public void Inbox_RejectsReplay()
    {
        var inbox = new L1MessageInbox();
        inbox.Enqueue(Build(0, 1));
        inbox.Dequeue(1);
        Assert.ThrowsExactly<InvalidOperationException>(() => inbox.Enqueue(Build(0, 1)));
    }

    [TestMethod]
    public void Inbox_DequeueZero_ReturnsEmpty()
    {
        // Boundary: max=0 must return empty without modifying state. Prior behavior
        // is short-circuit before lock acquire, which we pin here.
        var inbox = new L1MessageInbox();
        inbox.Enqueue(Build(0, 1));
        var taken = inbox.Dequeue(0);
        Assert.AreEqual(0, taken.Count);
        Assert.AreEqual(1, inbox.PendingCount, "Dequeue(0) must not consume");
    }

    [TestMethod]
    public void Inbox_DequeueRejectsNegativeMax()
    {
        var inbox = new L1MessageInbox();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => inbox.Dequeue(-1));
    }

    [TestMethod]
    public void Inbox_DequeueLargerThanPending_DrainsAll()
    {
        // Boundary: requesting more than pending is fine; returns whatever's there.
        var inbox = new L1MessageInbox();
        inbox.Enqueue(Build(0, 1));
        inbox.Enqueue(Build(0, 2));
        var taken = inbox.Dequeue(100);
        Assert.AreEqual(2, taken.Count);
        Assert.AreEqual(0, inbox.PendingCount);
    }

    [TestMethod]
    public void Inbox_PartialDequeue_SelectsLowestAcrossWholeSet_RegardlessOfArrivalOrder()
    {
        // Regression guard for the determinism fix: when max < pendingCount, the lowest-ordered
        // subset (by sourceChainId, then nonce) must be selected across the ENTIRE pending set —
        // NOT the FIFO front. The old code popped the FIFO front then sorted only those, so two
        // nodes that received messages in different orders would pick different subsets and produce
        // different L1MessageHashes. Enqueue out of nonce order; assert the lowest two win anyway,
        // and that the non-selected messages remain (not dropped) and stay dequeuable in order.
        var inbox = new L1MessageInbox();
        foreach (var nonce in new ulong[] { 5, 1, 3, 2, 4 }) inbox.Enqueue(Build(0, nonce));
        Assert.AreEqual(5, inbox.PendingCount);

        var taken = inbox.Dequeue(2);
        Assert.AreEqual(2, taken.Count);
        Assert.AreEqual(1UL, taken[0].Nonce, "lowest nonce first — not the FIFO front (5)");
        Assert.AreEqual(2UL, taken[1].Nonce);
        Assert.AreEqual(3, inbox.PendingCount, "non-selected messages must remain, not be dropped");

        var rest = inbox.Dequeue(10);
        Assert.AreEqual(3, rest.Count);
        Assert.AreEqual(3UL, rest[0].Nonce);
        Assert.AreEqual(4UL, rest[1].Nonce);
        Assert.AreEqual(5UL, rest[2].Nonce);
        Assert.AreEqual(0, inbox.PendingCount);
    }

    [TestMethod]
    public void Inbox_HasConsumed_TracksPostDequeue()
    {
        // Build() hard-codes SourceChainId = 1001, so consume-tracking keys against 1001.
        var inbox = new L1MessageInbox();
        inbox.Enqueue(Build(0, 5));
        Assert.IsFalse(inbox.HasConsumed(1001, 5), "not yet consumed");
        inbox.Dequeue(1);
        Assert.IsTrue(inbox.HasConsumed(1001, 5), "consumed after dequeue");
        Assert.IsFalse(inbox.HasConsumed(1001, 99), "unrelated nonce stays false");
    }

    [TestMethod]
    public void Inbox_RejectsDuplicatePending()
    {
        var inbox = new L1MessageInbox();
        inbox.Enqueue(Build(0, 1));
        Assert.ThrowsExactly<InvalidOperationException>(() => inbox.Enqueue(Build(0, 1)));
    }

    [TestMethod]
    public void Outbox_SplitsByDestination()
    {
        var outbox = new L2Outbox();
        outbox.Add(Build(0, 1));   // L2 → L1
        outbox.Add(Build(0, 2));   // L2 → L1
        outbox.Add(Build(2002, 1)); // L2 → L2
        Assert.AreEqual(2, outbox.L2ToL1Count);
        Assert.AreEqual(1, outbox.L2ToL2Count);
        Assert.AreNotEqual(UInt256.Zero, outbox.L2ToL1Root);
        Assert.AreNotEqual(UInt256.Zero, outbox.L2ToL2Root);
    }

    [TestMethod]
    public void Outbox_Add_RejectsNull()
    {
        // Pin L2Outbox.cs:39.
        var outbox = new L2Outbox();
        Assert.ThrowsExactly<ArgumentNullException>(() => outbox.Add(null!));
    }

    [TestMethod]
    public void Inbox_Enqueue_RejectsNull()
    {
        // Pin L1MessageInbox.cs:37.
        var inbox = new L1MessageInbox();
        Assert.ThrowsExactly<ArgumentNullException>(() => inbox.Enqueue(null!));
    }

    [TestMethod]
    public async Task Router_EnqueueOutboundAsync_RejectsNull()
    {
        // Pin InMemoryMessageRouter.cs:44.
        var router = new InMemoryMessageRouter();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await router.EnqueueOutboundAsync(null!));
    }

    [TestMethod]
    public async Task Router_RoundTrips()
    {
        var router = new InMemoryMessageRouter();
        router.Inbox.Enqueue(Build(0, 1));
        router.Inbox.Enqueue(Build(0, 2));

        var taken = await router.DequeueL1MessagesAsync(1001, 10);
        Assert.AreEqual(2, taken.Count);

        await router.EnqueueOutboundAsync(new[] { Build(0, 1), Build(2002, 5) });
        Assert.AreEqual(1, router.Outbox.L2ToL1Count);
        Assert.AreEqual(1, router.Outbox.L2ToL2Count);
    }

    [TestMethod]
    public async Task Router_GetMessageProof_NullWhenNotFinalized()
    {
        var router = new InMemoryMessageRouter();
        var p = await router.GetMessageProofAsync(UInt256.Zero);
        Assert.IsNull(p);
    }

    [TestMethod]
    public void Router_RecordFinalized_AndGetMessageProof_AreThreadSafe()
    {
        // Regression: previously _finalized was a plain Dictionary<>. Concurrent
        // GetMessageProofAsync (RPC threads) + RecordFinalized (settlement thread) had
        // a small chance of corruption / NRE. Now ConcurrentDictionary; this test stress-
        // races them and asserts no exceptions plus reads see a consistent snapshot.
        var router = new InMemoryMessageRouter();
        var threadCount = 8;
        var iterations = 500;
        var threads = new Thread[threadCount];
        Exception? failure = null;

        for (var t = 0; t < threadCount; t++)
        {
            var threadIdx = t;
            threads[t] = new Thread(() =>
            {
                try
                {
                    for (var i = 0; i < iterations; i++)
                    {
                        var seedByte = (byte)((threadIdx + i) % 256);
                        var bytes = new byte[32];
                        bytes[0] = seedByte;
                        var hash = new UInt256(bytes);
                        if ((threadIdx & 1) == 0)
                            router.RecordFinalized(hash, new byte[] { seedByte, 0xFF });
                        else
                            _ = router.GetMessageProofAsync(hash).Result;
                    }
                }
                catch (Exception ex) { failure ??= ex; }
            });
            threads[t].Start();
        }
        foreach (var th in threads) th.Join();
        Assert.IsNull(failure, $"concurrent access threw: {failure}");
    }

    [TestMethod]
    public void Router_RecordFinalized_RejectsNullHash()
    {
        // Regression for iter 167: previously a null UInt256 messageHash would NRE
        // inside ConcurrentDictionary's hash lookup with no link back to the bad
        // caller. Same iter-148/149 pattern.
        var router = new InMemoryMessageRouter();
        Assert.ThrowsExactly<ArgumentNullException>(
            () => router.RecordFinalized(null!, new byte[] { 0x01 }));
    }

    [TestMethod]
    public async Task Router_GetMessageProof_RejectsNullHash()
    {
        var router = new InMemoryMessageRouter();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await router.GetMessageProofAsync(null!));
    }

    [TestMethod]
    public async Task Router_RecordFinalized_DefensiveCopyProtectsAgainstCallerMutation()
    {
        // Regression for iter 167: ReadOnlyMemory<byte>.ToArray() defensive copy means a
        // caller who reuses a scratch buffer or mutates their bytes after RecordFinalized
        // returns CANNOT silently corrupt the stored proof. Same pattern as
        // InMemoryL2RpcStore.RecordWithdrawalProof.
        var router = new InMemoryMessageRouter();
        var hashBytes = new byte[32]; hashBytes[0] = 0xAA;
        var hash = new UInt256(hashBytes);
        var scratch = new byte[] { 0x11, 0x22, 0x33 };

        router.RecordFinalized(hash, scratch);

        // Mutate the scratch buffer AFTER RecordFinalized — must not affect stored proof.
        scratch[0] = 0xFF;
        scratch[1] = 0xFF;
        scratch[2] = 0xFF;

        var stored = await router.GetMessageProofAsync(hash);
        Assert.IsNotNull(stored);
        var bytes = stored.Value.ToArray();
        Assert.AreEqual(0x11, bytes[0]);
        Assert.AreEqual(0x22, bytes[1]);
        Assert.AreEqual(0x33, bytes[2]);
    }
}
