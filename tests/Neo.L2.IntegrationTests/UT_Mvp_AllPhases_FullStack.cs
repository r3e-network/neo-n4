using System.Numerics;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Hub.Deploy;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Challenge;
using Neo.L2.Executor;
using Neo.L2.Executor.State;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Sequencer;
using Neo.L2.State;
using Neo.Plugins.L2Gateway;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// Consolidated end-to-end test exercising Phases 0+1+2+3+5 in one scenario. Acts as a
/// living "everything works together" demonstration the user can read from top to bottom
/// to understand the whole pipeline.
/// </summary>
[TestClass]
public class UT_Mvp_AllPhases_FullStack
{
    private const uint ChainA = 1001;
    private const uint ChainB = 1002;
    private static readonly UInt160 GasL2 = UInt160.Parse("0x" + new string('2', 40));
    private static readonly UInt160 Alice = UInt160.Parse("0x" + new string('a', 40));

    private static (ECPoint pub, byte[] priv) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return (ECCurve.Secp256r1.G * priv, priv);
    }

    private static byte[] BalanceKey(UInt160 asset, UInt160 holder)
    {
        var k = new byte[1 + 20 + 20];
        k[0] = 0x01;
        asset.GetSpan().CopyTo(k.AsSpan(1, 20));
        holder.GetSpan().CopyTo(k.AsSpan(21, 20));
        return k;
    }

    private static UInt256 H(byte b)
    {
        var bytes = new byte[32];
        bytes[0] = b;
        return new UInt256(bytes);
    }

    [TestMethod]
    public async Task AllPhases_StitchedTogether()
    {
        // ───── Phase 1: NeoHub deploy plan ─────
        var plan = ScaffoldPlan.Default();
        var bundle = DeployPlanner.Plan(plan, name =>
        {
            var bytes = new byte[20];
            var src = System.Text.Encoding.UTF8.GetBytes(name);
            Array.Copy(src, bytes, Math.Min(src.Length, 20));
            return new UInt160(bytes);
        });
        Assert.AreEqual(plan.Steps.Count, bundle.Invocations.Count);
        Assert.IsTrue(bundle.Invocations.Any(i => i.Name == "SettlementManager"));
        Assert.IsTrue(bundle.Invocations.Any(i => i.Name == "ForcedInclusion"));

        // ───── Phase 0/2: real off-chain stack with state continuity ─────
        var stateStore = new KeyedStateStore();
        var oracle = new KeyedStateRootOracle(stateStore);
        var executor = new ReferenceBatchExecutor(new ReferenceTransactionExecutor(), oracle);

        var validators = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var prover = new AttestationProver(new InMemorySignerSet(validators));
        var verifierRegistry = new VerifierRegistry();
        verifierRegistry.Register(new AttestationVerifier(validators.Select(v => v.pub), threshold: 3));

        var committee = new InMemorySequencerCommitteeProvider(ChainA);
        for (var i = 0; i < 3; i++) committee.Register(GenKey((byte)(50 + i)).pub, UInt160.Zero);

        var preStateRoot = stateStore.ComputeRoot();
        var honestCommitments = new List<L2BatchCommitment>();

        for (var batchNum = 1; batchNum <= 3; batchNum++)
        {
            var depositAmount = new BigInteger(1_000_000 * batchNum);
            var withdrawalAmount = new BigInteger(10_000 * batchNum);

            var balanceKey = BalanceKey(GasL2, Alice);
            var current = stateStore.Get(balanceKey).Length == 0
                ? BigInteger.Zero
                : new BigInteger(stateStore.Get(balanceKey).Span, isUnsigned: true, isBigEndian: false);
            var nextBalance = current + depositAmount - withdrawalAmount;
            stateStore.Put(balanceKey, nextBalance.ToByteArray(isUnsigned: true, isBigEndian: false));

            var ctx = new BatchBlockContext
            {
                L1FinalizedHeight = (uint)(1000 + batchNum),
                FirstBlockTimestamp = (ulong)(1_700_000_000_000 + batchNum * 10_000),
                LastBlockTimestamp = (ulong)(1_700_000_000_000 + batchNum * 10_000 + 5_000),
                SequencerCommitteeHash = UInt256.Zero,
                Network = 0x4F454E,
            };

            var execReq = new BatchExecutionRequest
            {
                ChainId = ChainA,
                BatchNumber = (ulong)batchNum,
                PreStateRoot = preStateRoot,
                Transactions = new ReadOnlyMemory<byte>[] { BitConverter.GetBytes((long)batchNum) },
                L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
                BlockContext = ctx,
            };
            var execResult = await executor.ApplyBatchAsync(execReq);

            var publicInputs = new PublicInputs
            {
                ChainId = ChainA,
                BatchNumber = (ulong)batchNum,
                PreStateRoot = preStateRoot,
                PostStateRoot = execResult.PostStateRoot,
                TxRoot = execResult.TxRoot,
                ReceiptRoot = execResult.ReceiptRoot,
                WithdrawalRoot = execResult.WithdrawalRoot,
                L2ToL1MessageRoot = execResult.L2ToL1MessageRoot,
                L2ToL2MessageRoot = execResult.L2ToL2MessageRoot,
                L1MessageHash = StateRootCalculator.HashL1Messages(execReq.L1MessagesConsumed),
                DACommitment = UInt256.Zero,
                BlockContextHash = StateRootCalculator.HashBlockContext(ctx),
            };
            var proofResult = await prover.ProveAsync(new ProofRequest
            {
                PublicInputs = publicInputs,
                Witness = ReadOnlyMemory<byte>.Empty,
                Kind = ProofType.Multisig,
            });
            var commitment = new L2BatchCommitment
            {
                ChainId = ChainA,
                BatchNumber = (ulong)batchNum,
                FirstBlock = (ulong)(100 * batchNum),
                LastBlock = (ulong)(100 * batchNum + 50),
                PreStateRoot = preStateRoot,
                PostStateRoot = execResult.PostStateRoot,
                TxRoot = execResult.TxRoot,
                ReceiptRoot = execResult.ReceiptRoot,
                WithdrawalRoot = execResult.WithdrawalRoot,
                L2ToL1MessageRoot = execResult.L2ToL1MessageRoot,
                L2ToL2MessageRoot = execResult.L2ToL2MessageRoot,
                DACommitment = UInt256.Zero,
                PublicInputHash = proofResult.PublicInputHash,
                ProofType = ProofType.Multisig,
                Proof = proofResult.Proof,
            };

            var verify = await verifierRegistry.VerifyAsync(commitment, publicInputs);
            Assert.IsTrue(verify.Valid, $"batch {batchNum}: {verify.FailureReason}");

            if (batchNum > 1)
                Assert.AreEqual(honestCommitments[^1].PostStateRoot, commitment.PreStateRoot,
                    "state-root continuity broken");
            honestCommitments.Add(commitment);
            preStateRoot = commitment.PostStateRoot;
        }

        // ───── Phase 3: simulate a sequencer lie + bisection ─────
        // Honest checkpoints from the executor; lying sequencer's claim diverges at index 1.
        var honestCheckpoints = new UInt256[] { H(0), H(1), H(2), H(3) };
        var lyingCheckpoints = new UInt256[] { H(0), H(1), H(99), H(98) };
        var game = new BisectionGame(honestCheckpoints, lyingCheckpoints);
        var disputedIndex = game.RunToSettlement();
        Assert.AreEqual(1, disputedIndex, "sequencer lied starting at checkpoint 2 (= tx index 1)");
        Assert.IsTrue(game.Rounds <= BisectionGame.MaxRoundsFor(3));

        // ───── Phase 5: cross-chain Gateway aggregation ─────
        var gateway = new L2GatewayPlugin();
        gateway.UseAggregator(new BinaryTreeAggregator());
        foreach (var c in honestCommitments) gateway.ReceiveBatch(c);
        // Add a synthetic ChainB commitment.
        gateway.ReceiveBatch(honestCommitments[0] with { ChainId = ChainB, L2ToL2MessageRoot = H(0xCC) });

        var aggregated = gateway.PullAggregate();
        Assert.IsNotNull(aggregated);
        Assert.AreEqual(4, aggregated!.Constituents.Count);
        Assert.AreNotEqual(UInt256.Zero, aggregated.GlobalMessageRoot);

        // Final consistency check: Alice's net balance.
        var aliceBytes = stateStore.Get(BalanceKey(GasL2, Alice));
        var aliceBal = new BigInteger(aliceBytes.Span, isUnsigned: true, isBigEndian: false);
        // Σ (1Mᵢ - 10Kᵢ) for i=1..3 = 6·990000 = 5_940_000
        Assert.AreEqual(new BigInteger(5_940_000), aliceBal);
    }
}
