using Neo.L2.Gateway.Rpc;
using Neo.Plugins.L2Gateway;
using Neo.VM;
using Neo.Wallets;

namespace Neo.L2.Gateway.Rpc.UnitTests;

/// <summary>
/// Unit tests for <see cref="RpcGlobalRootPublisher"/> — the production IGlobalRootPublisher that
/// signs + sends the L1 PublishGlobalRoot transaction. The L1 transport + signing is delegated, so
/// these tests assert the publisher's input validation + argument-forwarding contract, not the L1
/// round-trip itself (the delegate captures the forwarded args and returns a fixed tx hash).
/// </summary>
[TestClass]
public class UT_RpcGlobalRootPublisher
{
    private static readonly UInt160 MessageRouter = UInt160.Parse("0x" + new string('a', 40));
    private static readonly ReadOnlyMemory<byte> SampleVkId = new byte[32] {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20,
    };
    private static readonly UInt256 SampleRoot = UInt256.Parse("0x" + new string('7', 64));

    /// <summary>Build a minimal AggregatedCommitment with the given proof + global root.</summary>
    private static AggregatedCommitment Commitment(UInt256 root, byte[] proof) => new()
    {
        Constituents = Array.Empty<L2BatchCommitment>(),
        GlobalMessageRoot = root,
        ConstituentCommitmentsRoot = UInt256.Zero,
        AggregatedProof = proof,
        BackendId = 1,
    };

    // ---------------------------------------------------------------------------------------------
    // Construction
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Constructor_RejectsZeroRouter_AndNullSigner()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new RpcGlobalRootPublisher(UInt160.Zero, (_, _, _, _, _, _) => ValueTask.FromResult(UInt256.Zero)),
            "zero MessageRouter hash must be rejected");

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new RpcGlobalRootPublisher(MessageRouter, null!),
            "null signer delegate must be rejected");
    }

    // ---------------------------------------------------------------------------------------------
    // Argument forwarding — the core contract. The publisher must forward epoch, globalRoot,
    // verificationKeyId, and aggregatedProof UNCHANGED to the signer, and return the signer's tx hash.
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task PublishGlobalRootAsync_ForwardsAllArgs_AndReturnsTxHash()
    {
        UInt160? capturedRouter = null;
        ulong? capturedEpoch = null;
        UInt256? capturedRoot = null;
        ReadOnlyMemory<byte>? capturedVkId = null;
        ReadOnlyMemory<byte>? capturedProof = null;
        var expectedTx = UInt256.Parse("0x" + new string('f', 64));

        var publisher = new RpcGlobalRootPublisher(MessageRouter,
            (router, epoch, root, vkId, proof, ct) =>
            {
                capturedRouter = router;
                capturedEpoch = epoch;
                capturedRoot = root;
                capturedVkId = vkId;
                capturedProof = proof;
                return ValueTask.FromResult(expectedTx);
            });

        var proof = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var commitment = Commitment(SampleRoot, proof);
        var tx = await publisher.PublishGlobalRootAsync(batchEpoch: 42, commitment, SampleVkId);

        Assert.AreEqual(expectedTx, tx, "publisher must return the signer's tx hash");
        Assert.AreEqual(MessageRouter, capturedRouter, "messageRouterHash forwarded");
        Assert.AreEqual(42UL, capturedEpoch, "epoch forwarded");
        Assert.AreEqual(SampleRoot, capturedRoot, "global root forwarded");
        Assert.AreEqual(32, capturedVkId!.Value.Length, "vk id length forwarded");
        Assert.IsTrue(capturedVkId!.Value.Span.SequenceEqual(SampleVkId.Span), "vk id bytes forwarded");
        Assert.IsTrue(capturedProof!.Value.Span.SequenceEqual(proof), "aggregated proof bytes forwarded");
    }

    // ---------------------------------------------------------------------------------------------
    // Input validation — surfaced at the boundary before the L1 round-trip.
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task PublishGlobalRootAsync_RejectsNullCommitment()
    {
        var publisher = new RpcGlobalRootPublisher(MessageRouter,
            (_, _, _, _, _, _) => ValueTask.FromResult(UInt256.Zero));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await publisher.PublishGlobalRootAsync(1, null!, SampleVkId));
    }

    [TestMethod]
    public async Task PublishGlobalRootAsync_RejectsWrongVkIdLength()
    {
        var publisher = new RpcGlobalRootPublisher(MessageRouter,
            (_, _, _, _, _, _) => ValueTask.FromResult(UInt256.Zero));
        var commitment = Commitment(SampleRoot, new byte[] { 0x01 });
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await publisher.PublishGlobalRootAsync(1, commitment, new byte[31]),
            "31-byte vk id rejected before L1 round-trip");
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await publisher.PublishGlobalRootAsync(1, commitment, new byte[33]),
            "33-byte vk id rejected before L1 round-trip");
    }

    [TestMethod]
    public async Task PublishGlobalRootAsync_RejectsEmptyProof()
    {
        // Empty proof would be rejected by the on-chain contract anyway when a verifier is wired;
        // surface it here to avoid the L1 round-trip and give the operator a clearer error.
        var publisher = new RpcGlobalRootPublisher(MessageRouter,
            (_, _, _, _, _, _) => ValueTask.FromResult(UInt256.Zero));
        var commitment = Commitment(SampleRoot, Array.Empty<byte>());
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await publisher.PublishGlobalRootAsync(1, commitment, SampleVkId),
            "empty aggregated proof rejected before L1 round-trip");
    }

    [TestMethod]
    public async Task PublishGlobalRootAsync_PropagatesBenignAlreadyPublishedZero()
    {
        // The signer delegate MAY return UInt256.Zero to signal a benign already-published retry
        // (the on-chain publish-once-per-epoch guard surfaces as such). The publisher must
        // propagate it unchanged, not convert it to a fault.
        var publisher = new RpcGlobalRootPublisher(MessageRouter,
            (_, _, _, _, _, _) => ValueTask.FromResult(UInt256.Zero));
        var commitment = Commitment(SampleRoot, new byte[] { 0x01 });
        var tx = await publisher.PublishGlobalRootAsync(1, commitment, SampleVkId);
        Assert.AreEqual(UInt256.Zero, tx, "benign already-published UInt256.Zero propagated");
    }

    [TestMethod]
    public async Task PublishGlobalRootAsync_ForwardsCancellation()
    {
        var publisher = new RpcGlobalRootPublisher(MessageRouter,
            (_, _, _, _, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult(UInt256.Zero);
            });
        var commitment = Commitment(SampleRoot, new byte[] { 0x01 });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await publisher.PublishGlobalRootAsync(1, commitment, SampleVkId, cts.Token),
            "cancellation token forwarded to the signer");
    }
}
