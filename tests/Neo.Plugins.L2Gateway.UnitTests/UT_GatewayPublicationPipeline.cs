namespace Neo.Plugins.L2Gateway.UnitTests;

[TestClass]
public class UT_GatewayPublicationPipeline
{
    private static UInt256 H(byte value) => new(Enumerable.Repeat(value, 32).ToArray());

    private static GatewayProofBinding Binding(ulong epoch = 7) => new()
    {
        MessageRouter = UInt160.Parse("0x" + new string('a', 40)),
        ReplayDomain = H(0xD1),
        BatchEpoch = epoch,
        GlobalMessageRoot = H(0x51),
        ConstituentCommitmentsRoot = H(0x61),
        ConstituentCount = 2,
        AggregationBackendId = MerklePathRoundProver.ConstBackendId,
        ProofSystem = 1,
        VerificationKeyId = H(0xA1),
    };

    private static GatewayRootPublicationObservation Exact(GatewayProofBinding binding) => new()
    {
        GlobalMessageRoot = binding.GlobalMessageRoot,
        ProofInputHash = GatewayProofBindingSerializer.ComputeHash(binding),
    };

    [TestMethod]
    public async Task ExistingExactPublication_IsBenignAndDoesNotSubmit()
    {
        var binding = Binding();
        var submitCalls = 0;
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult<GatewayRootPublicationObservation?>(Exact(binding)),
            (_, _) =>
            {
                submitCalls++;
                return ValueTask.FromResult(H(0xF1));
            });

        var result = await publisher.PublishGlobalRootAsync(binding, new byte[] { 0x01 });

        Assert.AreEqual(UInt256.Zero, result);
        Assert.AreEqual(0, submitCalls);
    }

    [TestMethod]
    public async Task ExistingConflict_FailsClosedWithoutSubmitting()
    {
        var binding = Binding();
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
            async () => await publisher.PublishGlobalRootAsync(binding, new byte[] { 0x01 }));
        Assert.AreEqual(0, submitCalls);
    }

    [TestMethod]
    public async Task TimeoutAfterAcceptance_ReconcilesAsIdempotentSuccess()
    {
        var binding = Binding();
        GatewayRootPublicationObservation? observed = null;
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult(observed),
            (_, _) =>
            {
                observed = Exact(binding);
                throw new TimeoutException("response lost after L1 acceptance");
            });

        var result = await publisher.PublishGlobalRootAsync(binding, new byte[] { 0x01 });

        Assert.AreEqual(UInt256.Zero, result);
    }

    [TestMethod]
    public async Task UnacceptedFailure_RemainsRetryableError()
    {
        var binding = Binding();
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult<GatewayRootPublicationObservation?>(null),
            (_, _) => throw new TimeoutException("not accepted"));

        await Assert.ThrowsExactlyAsync<TimeoutException>(
            async () => await publisher.PublishGlobalRootAsync(binding, new byte[] { 0x01 }));
    }

    [TestMethod]
    public async Task SubmitMustReturnOnlyAfterExactConfirmation()
    {
        var binding = Binding();
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult<GatewayRootPublicationObservation?>(null),
            (_, _) => ValueTask.FromResult(H(0xF1)));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await publisher.PublishGlobalRootAsync(binding, new byte[] { 0x01 }));
    }

    [TestMethod]
    public async Task NullTransactionHashWithoutAcceptance_IsRejected()
    {
        var binding = Binding();
        var publisher = new ReconciledGlobalRootPublisher(
            (_, _, _) => ValueTask.FromResult<GatewayRootPublicationObservation?>(null),
            (_, _) => ValueTask.FromResult<UInt256>(null!));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await publisher.PublishGlobalRootAsync(binding, new byte[] { 0x01 }));
    }
}
