using Neo.Plugins.L2Gateway;
using Neo.L2.Persistence;

namespace Neo.L2.IntegrationTests;

/// <summary>End-to-end Gateway aggregation, terminal proving, reconciliation, and tamper tests.</summary>
[TestClass]
public class UT_GatewayProofPublication
{
    private static UInt256 H(byte value) => new(Enumerable.Repeat(value, 32).ToArray());

    private static L2BatchCommitment Batch(uint chainId, byte proof) => new()
    {
        ChainId = chainId,
        BatchNumber = 1,
        FirstBlock = 1,
        LastBlock = 1,
        PreStateRoot = H(0x01),
        PostStateRoot = H(0x02),
        TxRoot = H(0x03),
        ReceiptRoot = H(0x04),
        WithdrawalRoot = H(0x05),
        L2ToL1MessageRoot = H(0x06),
        L2ToL2MessageRoot = H((byte)(chainId & 0xFF)),
        DACommitment = H(0x08),
        PublicInputHash = H(0x09),
        ProofType = ProofType.Zk,
        Proof = new byte[] { proof },
    };

    private static L2GatewayPlugin CreatePlugin(
        IProofBoundGlobalRootPublisher publisher,
        IGatewayAggregator? aggregator = null)
    {
        var plugin = new L2GatewayPlugin();
        plugin.UseAggregator(aggregator ?? new BinaryTreeAggregator(new MerklePathRoundProver()));
        plugin.UsePersistentOutbox(new PersistentGatewayOutbox(
            new InMemoryKeyValueStore(),
            ownsStore: true), ownsOutbox: true);
        var prover = new DelegatingGatewayProofProver(
            proofSystem: 1,
            aggregationBackendId: MerklePathRoundProver.ConstBackendId,
            (binding, _, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadOnlyMemory<byte> proof = GatewayProofBindingSerializer.ComputeHash(binding)
                    .GetSpan().ToArray();
                return ValueTask.FromResult(proof);
            });
        plugin.ConfigureGlobalRootPublication(
            prover,
            publisher,
            UInt160.Parse("0x" + new string('a', 40)),
            H(0xD1),
            H(0xA1));
        return plugin;
    }

    [TestMethod]
    public async Task ValidAggregate_IsProofBoundPublishedAndConfirmed()
    {
        GatewayRootPublicationObservation? observed = null;
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult(observed),
            (request, _) =>
            {
                Assert.IsTrue(request.AggregatedProof.Span.SequenceEqual(
                    request.ProofInputHash.GetSpan()));
                observed = new GatewayRootPublicationObservation
                {
                    GlobalMessageRoot = request.Binding.GlobalMessageRoot,
                    ProofInputHash = request.ProofInputHash,
                };
                return ValueTask.FromResult(H(0xF1));
            });
        using var plugin = CreatePlugin(publisher);
        plugin.ReceiveBatch(Batch(2002, 0x22));
        plugin.ReceiveBatch(Batch(1001, 0x11));

        var transactionHash = await plugin.PublishAggregateAsync(77);

        Assert.AreEqual(H(0xF1), transactionHash);
        Assert.IsNotNull(observed);
        Assert.IsFalse(observed.GlobalMessageRoot.Equals(UInt256.Zero));
        Assert.IsFalse(plugin.HasPendingPublication);
    }

    [TestMethod]
    public async Task TamperedConstituent_IsRejectedBeforeProofOrPublication()
    {
        var first = Batch(1001, 0x11);
        var second = Batch(2002, 0x22);
        var constituents = new[] { first, second };
        var aggregate = new AggregatedCommitment
        {
            Constituents = constituents,
            GlobalMessageRoot = H(0x51),
            ConstituentCommitmentsRoot =
                GatewayProofBindingSerializer.ComputeConstituentCommitmentsRoot(constituents),
            AggregatedProof = new byte[] { 0x01 },
            BackendId = MerklePathRoundProver.ConstBackendId,
        };
        constituents[1] = second with { Proof = new byte[] { 0x23 } };
        var publisherCalls = 0;
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult<GatewayRootPublicationObservation?>(null),
            (_, _) =>
            {
                publisherCalls++;
                return ValueTask.FromResult(H(0xF1));
            });
        using var plugin = CreatePlugin(publisher, new SingleAggregate(aggregate));
        plugin.ReceiveBatch(first);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await plugin.PublishAggregateAsync(77));

        Assert.AreEqual(0, publisherCalls);
        Assert.IsFalse(plugin.HasPendingPublication);
        Assert.AreEqual(1, plugin.OutboxStatus.QueueDepth,
            "sealed input remains durable even when a malicious aggregate is rejected");
    }

    private sealed class SingleAggregate : IGatewayAggregator
    {
        private AggregatedCommitment? _aggregate;
        private bool _ready;

        public SingleAggregate(AggregatedCommitment aggregate)
        {
            _aggregate = aggregate;
            BackendId = aggregate.BackendId;
        }

        public byte BackendId { get; }
        public int PendingCount => _ready && _aggregate is not null ? 1 : 0;
        public void Submit(L2BatchCommitment commitment) => _ready = true;
        public AggregatedCommitment? Aggregate() =>
            _ready ? Interlocked.Exchange(ref _aggregate, null) : null;
    }
}
