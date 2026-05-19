using System.Numerics;

namespace Neo.L2.Bridge.UnitTests;

/// <summary>
/// Tests that <see cref="DepositProcessor"/> and <see cref="WithdrawalProcessor"/> emit the
/// canonical bridge metrics on success and failure paths.
/// </summary>
[TestClass]
public class UT_Bridge_Metrics
{
    private const uint LocalChain = 1001;
    private static readonly UInt160 GasL1 = UInt160.Parse("0x" + new string('1', 40));
    private static readonly UInt160 GasL2 = UInt160.Parse("0x" + new string('2', 40));
    private static readonly UInt160 Recipient = UInt160.Parse("0x" + new string('3', 40));
    private static readonly UInt160 Sender = UInt160.Parse("0x" + new string('4', 40));

    private static AssetMapping GasMapping() => new()
    {
        L1Asset = GasL1,
        L2ChainId = LocalChain,
        L2Asset = GasL2,
        L1Decimals = 8,
        L2Decimals = 8,
        AssetType = AssetType.Gas,
        MintBurn = true,
        LockMint = true,
        Active = true,
    };

    private static AssetRegistry RegistryWithGas()
    {
        var r = new AssetRegistry();
        r.Register(GasMapping());
        return r;
    }

    [TestMethod]
    public void Deposit_Success_IncrementsProcessedCounter()
    {
        var metrics = new InMemoryMetrics();
        var proc = new DepositProcessor(LocalChain, RegistryWithGas(), metrics);
        var msg = BuildDepositMessage(nonce: 1);

        proc.Process(msg);

        Assert.AreEqual(1, metrics.GetCounter(MetricNames.DepositsProcessed));
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.DepositsRejected));
    }

    [TestMethod]
    public void Deposit_Replay_IncrementsRejectedCounter()
    {
        var metrics = new InMemoryMetrics();
        var proc = new DepositProcessor(LocalChain, RegistryWithGas(), metrics);
        var msg = BuildDepositMessage(nonce: 1);

        proc.Process(msg);
        Assert.ThrowsExactly<InvalidOperationException>(() => proc.Process(msg));

        Assert.AreEqual(1, metrics.GetCounter(MetricNames.DepositsProcessed), "first call");
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.DepositsRejected), "replay rejection");
    }

    [TestMethod]
    public void Deposit_UnknownAsset_IncrementsRejectedCounter()
    {
        var metrics = new InMemoryMetrics();
        var proc = new DepositProcessor(LocalChain, new AssetRegistry(), metrics);

        Assert.ThrowsExactly<InvalidOperationException>(() => proc.Process(BuildDepositMessage(nonce: 1)));

        Assert.AreEqual(0, metrics.GetCounter(MetricNames.DepositsProcessed));
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.DepositsRejected));
    }

    [TestMethod]
    public void Withdrawal_Success_IncrementsStagedCounter()
    {
        var metrics = new InMemoryMetrics();
        var proc = new WithdrawalProcessor(LocalChain, RegistryWithGas(), metrics);

        proc.Stage(BuildWithdrawalRequest(nonce: 1));
        proc.Stage(BuildWithdrawalRequest(nonce: 2));

        Assert.AreEqual(2, metrics.GetCounter(MetricNames.WithdrawalsStaged));
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.WithdrawalsRejected));
    }

    [TestMethod]
    public void Withdrawal_DuplicateNonce_IncrementsRejectedCounter()
    {
        var metrics = new InMemoryMetrics();
        var proc = new WithdrawalProcessor(LocalChain, RegistryWithGas(), metrics);

        proc.Stage(BuildWithdrawalRequest(nonce: 1));
        Assert.ThrowsExactly<InvalidOperationException>(() => proc.Stage(BuildWithdrawalRequest(nonce: 1)));

        Assert.AreEqual(1, metrics.GetCounter(MetricNames.WithdrawalsStaged), "first call");
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.WithdrawalsRejected), "duplicate-nonce rejection");
    }

    [TestMethod]
    public void Withdrawal_NegativeAmount_IncrementsRejectedCounter()
    {
        var metrics = new InMemoryMetrics();
        var proc = new WithdrawalProcessor(LocalChain, RegistryWithGas(), metrics);

        var req = BuildWithdrawalRequest(nonce: 1, amount: 0);
        Assert.ThrowsExactly<ArgumentException>(() => proc.Stage(req));

        Assert.AreEqual(1, metrics.GetCounter(MetricNames.WithdrawalsRejected));
    }

    [TestMethod]
    public void Constructors_DefaultToNoOp_WhenMetricsNull()
    {
        // Regression — old call sites without the new param keep working.
        var proc = new DepositProcessor(LocalChain, RegistryWithGas());
        proc.Process(BuildDepositMessage(nonce: 1)); // no-throw

        var wproc = new WithdrawalProcessor(LocalChain, RegistryWithGas());
        wproc.Stage(BuildWithdrawalRequest(nonce: 1)); // no-throw
    }

    [TestMethod]
    public void Deposit_WithMetrics_PreservesConsumedNonceState()
    {
        // Regression: previously L2BridgePlugin.WithMetrics re-constructed the processor,
        // dropping the consumed-nonce HashSet — a replay after re-wiring would slip through.
        // Now WithMetrics swaps the sink in-place, preserving state.
        var initial = new InMemoryMetrics();
        var proc = new DepositProcessor(LocalChain, RegistryWithGas(), initial);
        proc.Process(BuildDepositMessage(nonce: 1));

        var second = new InMemoryMetrics();
        proc.WithMetrics(second);

        // Replay must still be rejected — proves consumed-nonce state survived the swap.
        Assert.ThrowsExactly<InvalidOperationException>(() => proc.Process(BuildDepositMessage(nonce: 1)));
        Assert.AreEqual(1, second.GetCounter(MetricNames.DepositsRejected), "post-swap rejection emits to new sink");
        Assert.AreEqual(1, initial.GetCounter(MetricNames.DepositsProcessed), "pre-swap success was on old sink");
    }

    [TestMethod]
    public void Withdrawal_WithMetrics_PreservesNonceState()
    {
        var initial = new InMemoryMetrics();
        var proc = new WithdrawalProcessor(LocalChain, RegistryWithGas(), initial);
        proc.Stage(BuildWithdrawalRequest(nonce: 1));

        var second = new InMemoryMetrics();
        proc.WithMetrics(second);

        Assert.ThrowsExactly<InvalidOperationException>(() => proc.Stage(BuildWithdrawalRequest(nonce: 1)));
        Assert.AreEqual(1, second.GetCounter(MetricNames.WithdrawalsRejected));
    }

    private static CrossChainMessage BuildDepositMessage(ulong nonce)
    {
        var payload = new DepositPayload
        {
            L1Asset = GasL1,
            L2Recipient = Recipient,
            Amount = new BigInteger(1_000_000),
        };
        return new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = LocalChain,
            Nonce = nonce,
            Sender = Sender,
            Receiver = Recipient,
            MessageType = MessageType.Deposit,
            Payload = payload.Encode(),
            MessageHash = UInt256.Zero,
        };
    }

    private static WithdrawalRequest BuildWithdrawalRequest(ulong nonce, long amount = 100) => new()
    {
        EmittingContract = UInt160.Zero,
        L2Sender = Sender,
        L1Recipient = Recipient,
        L2Asset = GasL2,
        Amount = new BigInteger(amount),
        Nonce = nonce,
    };
}
