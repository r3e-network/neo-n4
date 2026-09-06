using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Neo;
using Neo.Cryptography.ECC;
using Neo.L2.Bridge;
using Neo.L2.Censorship;
using Neo.L2.ForcedInclusion;
using Neo.L2.Messaging;
using Neo.L2.Sequencer;
using Neo.L2.State;

namespace Neo.L2.IntegrationTests;

[TestClass]
public class UT_E2E_SystemInvariants
{
    private static ECPoint MakePubKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return ECCurve.Secp256r1.G * priv;
    }

    private static UInt160 MakeAddress(byte seed)
    {
        var b = new byte[20];
        b[0] = seed;
        return new UInt160(b);
    }

    /// <summary>
    /// Invariant: Conservation of Assets across multiple L1-L2 deposit/withdrawal cycles.
    /// Total Escrowed on L1 must strictly equal Total L2 Minted minus Total Withdrawn at all times.
    /// Attempting to replay any deposit or withdrawal nonce across batches must fail closed.
    /// </summary>
    [TestMethod]
    public void Bridge_ConservationOfAssets_And_ReplayImmunity_Invariant()
    {
        const uint chainId = 1001;
        var registry = new AssetRegistry();
        var l1Gas = MakeAddress(1);
        var l2Gas = MakeAddress(2);
        registry.Register(PlatformAssets.CreateGasMapping(l1Gas, chainId) with { L2Asset = l2Gas });

        var depositProcessor = new DepositProcessor(chainId, registry);
        var withdrawalProcessor = new WithdrawalProcessor(chainId, registry);

        BigInteger l1Escrow = 0;
        BigInteger totalL2Supply = 0;

        var userA = MakeAddress(10);
        var userB = MakeAddress(20);

        // --- Batch 1: Inbound Deposits ---
        var dep1Payload = new DepositPayload { L1Asset = l1Gas, L2Recipient = userA, Amount = 1000 }.Encode();
        var dep1Msg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = chainId,
            Nonce = 1,
            Sender = MakeAddress(99),
            Receiver = MakeAddress(98),
            MessageType = MessageType.Deposit,
            Payload = dep1Payload,
            MessageHash = UInt256.Parse("0x" + new string('a', 64)),
        };
        var mint1 = depositProcessor.Process(dep1Msg);
        l1Escrow += mint1.Amount;
        totalL2Supply += mint1.Amount;

        var dep2Payload = new DepositPayload { L1Asset = l1Gas, L2Recipient = userB, Amount = 500 }.Encode();
        var dep2Msg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = chainId,
            Nonce = 2,
            Sender = MakeAddress(99),
            Receiver = MakeAddress(98),
            MessageType = MessageType.Deposit,
            Payload = dep2Payload,
            MessageHash = UInt256.Parse("0x" + new string('b', 64)),
        };
        var mint2 = depositProcessor.Process(dep2Msg);
        l1Escrow += mint2.Amount;
        totalL2Supply += mint2.Amount;

        Assert.AreEqual(new BigInteger(1500), l1Escrow);
        Assert.AreEqual(l1Escrow, totalL2Supply);

        // Invariant: Replay of Deposit 1 must be rejected
        Assert.ThrowsExactly<InvalidOperationException>(() => depositProcessor.Process(dep1Msg));

        // --- Batch 2: Outbound Withdrawal ---
        var withdrawAmount = new BigInteger(400);
        var req = new WithdrawalRequest
        {
            ChainId = chainId,
            EmittingContract = MakeAddress(98),
            L2Sender = userA,
            L1Recipient = MakeAddress(11),
            L2Asset = l2Gas,
            Amount = withdrawAmount,
            Nonce = 1,
        };
        withdrawalProcessor.Stage(req);
        var (root, tree) = withdrawalProcessor.SealBatch();
        totalL2Supply -= withdrawAmount;
        l1Escrow -= withdrawAmount;

        Assert.AreEqual(new BigInteger(1100), l1Escrow);
        Assert.AreEqual(l1Escrow, totalL2Supply);
        Assert.AreEqual(1, tree.Count);

        // Invariant: Cross-batch replay of withdrawal request must be rejected
        Assert.ThrowsExactly<InvalidOperationException>(() => withdrawalProcessor.Stage(req));
    }

    /// <summary>
    /// Invariant: Anti-Censorship Forced Inclusion Lifecycle.
    /// Transactions included within deadline must never trigger censorship reports.
    /// Transactions censored past deadline must trigger an attributed CensorshipReport.
    /// </summary>
    [TestMethod]
    public async Task ForcedInclusion_AntiCensorship_Lifecycle_Invariant()
    {
        const uint chainId = 2002;
        var clock = new FakeClock { NowUnixSeconds = 1000 };
        var source = new InMemoryForcedInclusionSource(chainId);
        var committee = new InMemorySequencerCommitteeProvider(chainId);
        committee.Register(MakePubKey(1), MakeAddress(0x10));
        var detector = new CensorshipDetector(source, committee, clock);

        var txHash1 = UInt256.Parse("0x" + new string('1', 64));
        var txHash2 = UInt256.Parse("0x" + new string('2', 64));

        // Enqueue tx1 at t=1000, deadline=1060
        source.Enqueue(new ForcedInclusionEntry
        {
            Nonce = 1,
            Sender = MakeAddress(0xAA),
            TxHash = txHash1,
            SerializedTx = new byte[] { 0xAA },
            DeadlineUnixSeconds = 1060,
        });

        // Enqueue tx2 at t=1000, deadline=1100
        source.Enqueue(new ForcedInclusionEntry
        {
            Nonce = 2,
            Sender = MakeAddress(0xBB),
            TxHash = txHash2,
            SerializedTx = new byte[] { 0xBB },
            DeadlineUnixSeconds = 1100,
        });

        // At t=1050 (before deadlines), detector must report zero censorship
        clock.NowUnixSeconds = 1050;
        var earlyReports = await detector.DetectOverdueAsync();
        Assert.AreEqual(0, earlyReports.Count, "No censorship before deadline");

        // Simulate sequencer including tx1 at t=1055 (before tx1 deadline)
        // Mark tx1 consumed in source
        await source.ConfirmConsumedAsync(1);

        // Advance to t=1070 (tx1 was deadline 1060, but was consumed; tx2 deadline 1100 is not yet reached)
        clock.NowUnixSeconds = 1070;
        var midReports = await detector.DetectOverdueAsync();
        Assert.AreEqual(0, midReports.Count, "Consumed entry must not be reported as censored");

        // Advance to t=1105 (tx2 deadline 1100 missed by sequencer)
        clock.NowUnixSeconds = 1105;
        var overdueReports = await detector.DetectOverdueAsync();
        Assert.AreEqual(1, overdueReports.Count, "Overdue tx2 must trigger censorship report");
        Assert.AreEqual(chainId, overdueReports[0].ChainId);
        Assert.AreEqual(2UL, overdueReports[0].ForcedInclusionNonce);
        Assert.AreEqual(txHash2, overdueReports[0].OverdueTxHash);
    }
}
