using Neo.L2.Batch;

namespace Neo.Plugins.L2Gateway.UnitTests;

[TestClass]
public class UT_GatewayPublicationPipeline
{
    private static UInt256 H(byte value) => new(Enumerable.Repeat(value, 32).ToArray());

    private static GatewayRootPublicationObservation Exact(GatewayProofBinding binding) => new()
    {
        GlobalMessageRoot = binding.GlobalMessageRoot,
        ProofInputHash = GatewayProofBindingSerializer.ComputeHash(binding),
    };

    [TestMethod]
    public void DelegatingProver_ConstructorRejectsInvalidConfiguration()
    {
        static ValueTask<ReadOnlyMemory<byte>> Factory(
            GatewayProofBinding binding,
            AggregatedCommitment commitment,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[] { 0x01 });

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new DelegatingGatewayProofProver(0, MerklePathRoundProver.ConstBackendId, Factory));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new DelegatingGatewayProofProver(5, MerklePathRoundProver.ConstBackendId, Factory));
        Assert.ThrowsExactly<ArgumentException>(
            () => new DelegatingGatewayProofProver(1, PassThroughRoundProver.ConstBackendId, Factory));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new DelegatingGatewayProofProver(1, MerklePathRoundProver.ConstBackendId, null!));
    }

    [TestMethod]
    public async Task DelegatingProver_ProvesOnlyExactBoundAggregate()
    {
        var (binding, aggregate) = Statement();
        GatewayProofBinding? receivedBinding = null;
        AggregatedCommitment? receivedAggregate = null;
        var prover = new DelegatingGatewayProofProver(
            1,
            MerklePathRoundProver.ConstBackendId,
            (candidateBinding, candidateAggregate, _) =>
            {
                receivedBinding = candidateBinding;
                receivedAggregate = candidateAggregate;
                return ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[] { 0xCA, 0xFE });
            });

        Assert.AreEqual(1, prover.ProofSystem);
        Assert.AreEqual(MerklePathRoundProver.ConstBackendId, prover.AggregationBackendId);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await prover.ProveAsync(null!, aggregate));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await prover.ProveAsync(binding, null!));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await prover.ProveAsync(binding, aggregate, cancelled.Token));
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await prover.ProveAsync(binding with { ProofSystem = 2 }, aggregate));
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await prover.ProveAsync(
            binding with { AggregationBackendId = MultisigRoundProver.ConstBackendId },
            aggregate));
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await prover.ProveAsync(
            binding,
            aggregate with { BackendId = MultisigRoundProver.ConstBackendId }));
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await prover.ProveAsync(
            binding with { GlobalMessageRoot = H(0x52) },
            aggregate));

        var proof = await prover.ProveAsync(binding, aggregate);

        CollectionAssert.AreEqual(new byte[] { 0xCA, 0xFE }, proof.ToArray());
        Assert.AreSame(binding, receivedBinding);
        Assert.AreSame(aggregate, receivedAggregate);
    }

    [TestMethod]
    public async Task ExistingExactPublication_IsBenignAndDoesNotSubmit()
    {
        var (binding, aggregate) = Statement();
        var submitCalls = 0;
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult<GatewayRootPublicationObservation?>(Exact(binding)),
            (_, _) =>
            {
                submitCalls++;
                return ValueTask.FromResult(H(0xF1));
            });

        var result = await publisher.PublishGlobalRootAsync(binding, aggregate, new byte[] { 0x01 });

        Assert.AreEqual(UInt256.Zero, result);
        Assert.AreEqual(0, submitCalls);
    }

    [TestMethod]
    public async Task ExistingConflict_FailsClosedWithoutSubmitting()
    {
        var (binding, aggregate) = Statement();
        var submitCalls = 0;
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult<GatewayRootPublicationObservation?>(new()
            {
                GlobalMessageRoot = H(0x52),
                ProofInputHash = H(0x62),
            }),
            (_, _) =>
            {
                submitCalls++;
                return ValueTask.FromResult(H(0xF1));
            });

        await Assert.ThrowsExactlyAsync<GatewayPublicationConflictException>(
            async () => await publisher.PublishGlobalRootAsync(binding, aggregate, new byte[] { 0x01 }));
        Assert.AreEqual(0, submitCalls);
    }

    [TestMethod]
    public async Task TimeoutAfterAcceptance_ReconcilesAsIdempotentSuccess()
    {
        var (binding, aggregate) = Statement();
        GatewayRootPublicationObservation? observed = null;
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult(observed),
            (_, _) =>
            {
                observed = Exact(binding);
                throw new TimeoutException("response lost after L1 acceptance");
            });

        var result = await publisher.PublishGlobalRootAsync(binding, aggregate, new byte[] { 0x01 });

        Assert.AreEqual(UInt256.Zero, result);
    }

    [TestMethod]
    public async Task UnacceptedFailure_RemainsRetryableError()
    {
        var (binding, aggregate) = Statement();
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult<GatewayRootPublicationObservation?>(null),
            (_, _) => throw new TimeoutException("not accepted"));

        await Assert.ThrowsExactlyAsync<TimeoutException>(
            async () => await publisher.PublishGlobalRootAsync(binding, aggregate, new byte[] { 0x01 }));
    }

    [TestMethod]
    public async Task SubmitMustReturnOnlyAfterExactConfirmation()
    {
        var (binding, aggregate) = Statement();
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult<GatewayRootPublicationObservation?>(null),
            (_, _) => ValueTask.FromResult(H(0xF1)));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await publisher.PublishGlobalRootAsync(binding, aggregate, new byte[] { 0x01 }));
    }

    [TestMethod]
    public async Task NullTransactionHashWithoutAcceptance_IsRejected()
    {
        var (binding, aggregate) = Statement();
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult<GatewayRootPublicationObservation?>(null),
            (_, _) => ValueTask.FromResult<UInt256>(null!));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await publisher.PublishGlobalRootAsync(binding, aggregate, new byte[] { 0x01 }));
    }

    [TestMethod]
    public async Task SubmitRequest_PreservesExactConstituentReferences()
    {
        var (binding, aggregate) = Statement();
        GatewayRootPublishRequest? captured = null;
        GatewayRootPublicationObservation? observed = null;
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult(observed),
            (request, _) =>
            {
                captured = request;
                observed = Exact(binding);
                return ValueTask.FromResult(H(0xF1));
            });

        await publisher.PublishGlobalRootAsync(binding, aggregate, new byte[] { 0x01 });

        Assert.IsNotNull(captured);
        CollectionAssert.AreEqual(
            GatewayFinalityReferenceSerializer.Encode(aggregate.Constituents),
            captured.ConstituentReferences.ToArray());
    }

    private static (GatewayProofBinding Binding, AggregatedCommitment Aggregate) Statement()
    {
        var constituent = new L2BatchCommitment
        {
            ChainId = 1001,
            BatchNumber = 1,
            FirstBlock = 1,
            LastBlock = 1,
            PreStateRoot = H(0x01),
            PostStateRoot = H(0x02),
            TxRoot = H(0x03),
            ReceiptRoot = H(0x04),
            WithdrawalRoot = H(0x05),
            L2ToL1MessageRoot = H(0x06),
            L2ToL2MessageRoot = H(0x07),
            DACommitment = H(0x08),
            PublicInputHash = H(0x09),
            ProofType = ProofType.Zk,
            Proof = new byte[] { 0x10 },
        };
        var constituents = new[] { constituent };
        var aggregate = new AggregatedCommitment
        {
            Constituents = constituents,
            GlobalMessageRoot = constituent.L2ToL2MessageRoot,
            ConstituentCommitmentsRoot =
                GatewayProofBindingSerializer.ComputeConstituentCommitmentsRoot(constituents),
            AggregatedProof = new byte[] { 0x20 },
            BackendId = MerklePathRoundProver.ConstBackendId,
        };
        var binding = GatewayProofBindingSerializer.Create(
            UInt160.Parse("0x" + new string('a', 40)),
            H(0xD1),
            7,
            aggregate,
            1,
            H(0xA1));
        return (binding, aggregate);
    }
}
