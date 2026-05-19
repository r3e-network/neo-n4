using System.Numerics;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Hub.Deploy;
using Neo.Json;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Executor;
using Neo.L2.Executor.Receipts;
using Neo.L2.ForcedInclusion;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Proving.RiscVZk;
using Neo.L2.State;
using Neo.Plugins.L2Gateway;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// Phase-1 cross-component integration test. Walks: deploy planner → forced inclusion
/// drain → batch executor → mock RISC-V prover → multi-chain Gateway aggregation.
/// The real ZK prover lives out-of-process (`prove-batch daemon`); these in-process
/// tests pin the framework's prover seam wiring with a deterministic mock.
/// </summary>
[TestClass]
public class UT_Mvp_Phase1_Cross_Component
{
    private static (ECPoint pub, byte[] priv) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return (ECCurve.Secp256r1.G * priv, priv);
    }

    private static BatchBlockContext SampleContext() => new()
    {
        L1FinalizedHeight = 5_000,
        FirstBlockTimestamp = 1_700_000_000_000,
        LastBlockTimestamp = 1_700_000_005_000,
        SequencerCommitteeHash = UInt256.Parse("0x" + new string('c', 64)),
        Network = 0x4F454E,
    };

    [TestMethod]
    public void DeployPlanner_ResolvesCanonicalNeoHubLayout()
    {
        // Verify the canonical scaffold passes topological + placeholder resolution.
        var plan = ScaffoldPlan.Default();
        var bundle = DeployPlanner.Plan(plan, name =>
        {
            // Deterministic per-name fake hashes; each is unique.
            var bytes = new byte[20];
            var src = System.Text.Encoding.UTF8.GetBytes(name);
            Array.Copy(src, bytes, Math.Min(src.Length, 20));
            return new UInt160(bytes);
        });

        Assert.AreEqual(plan.Steps.Count, bundle.Invocations.Count);

        // Look up SettlementManager — must come AFTER ChainRegistry and VerifierRegistry.
        var smIdx = bundle.Invocations.ToList().FindIndex(i => i.Name == "SettlementManager");
        var crIdx = bundle.Invocations.ToList().FindIndex(i => i.Name == "ChainRegistry");
        var vrIdx = bundle.Invocations.ToList().FindIndex(i => i.Name == "VerifierRegistry");
        Assert.IsTrue(smIdx > crIdx, $"SettlementManager idx {smIdx} should follow ChainRegistry {crIdx}");
        Assert.IsTrue(smIdx > vrIdx, $"SettlementManager idx {smIdx} should follow VerifierRegistry {vrIdx}");

        // SharedBridge depends on SettlementManager + TokenRegistry.
        var sbIdx = bundle.Invocations.ToList().FindIndex(i => i.Name == "SharedBridge");
        var trIdx = bundle.Invocations.ToList().FindIndex(i => i.Name == "TokenRegistry");
        Assert.IsTrue(sbIdx > smIdx);
        Assert.IsTrue(sbIdx > trIdx);

        // ForcedInclusion is in the layout per Phase 1.
        Assert.IsTrue(bundle.Invocations.Any(i => i.Name == "ForcedInclusion"));
    }

    [TestMethod]
    public async Task ForcedInclusion_DrainAndBatchPrependsForcedTxs()
    {
        var src = new InMemoryForcedInclusionSource(1001);

        // Two forced txs from a censored user.
        for (ulong nonce = 1; nonce <= 2; nonce++)
        {
            var serializedTx = new byte[] { (byte)nonce, 0xCC, 0xDD };
            var txHash = new UInt256(Crypto.Hash256(serializedTx));
            src.Enqueue(new ForcedInclusionEntry
            {
                Nonce = nonce,
                Sender = UInt160.Parse("0x" + new string('a', 40)),
                TxHash = txHash,
                SerializedTx = serializedTx,
                DeadlineUnixSeconds = 1_700_010_000,
            });
        }

        Assert.AreEqual(2, src.PendingCount);
        Assert.IsFalse(await src.HasOverdueEntryAsync(1_700_005_000));

        // Drain into a batch builder.
        var builder = new BatchBuilder(1001, 1, 100, UInt256.Zero);
        var drained = await src.DrainAsync(10);
        foreach (var entry in drained)
        {
            builder.AddTransaction(entry.SerializedTx);
            await src.MarkConsumedAsync(entry.Nonce);
        }
        builder.AddBlock(100);
        builder.WithBlockContext(SampleContext());

        Assert.AreEqual(2, builder.Batch.TransactionCount);
        Assert.AreEqual(0, src.PendingCount);
    }

    [TestMethod]
    public async Task MockRiscVProver_ProveVerify_FullCycle()
    {
        // Pins the framework's RISC-V prover seam with a deterministic mock. The
        // real ZK prover lives out-of-process — operators run `prove-batch daemon`
        // which produces real SP1 proofs (see docs/launching-an-l2.md § "Prover
        // deployment"). This test only validates the in-process IL2Prover wiring
        // is sane: same prover round-trips an arbitrary public-inputs commitment.
        var vkId = UInt256.Parse("0x" + new string('e', 64));
        var prover = new MockRiscVProver(vkId);
        var verifier = new MockRiscVVerifier(vkId);

        var publicInputs = new PublicInputs
        {
            ChainId = 1001,
            BatchNumber = 1,
            PreStateRoot = UInt256.Parse("0x" + new string('1', 64)),
            PostStateRoot = UInt256.Parse("0x" + new string('2', 64)),
            TxRoot = UInt256.Parse("0x" + new string('3', 64)),
            ReceiptRoot = UInt256.Parse("0x" + new string('4', 64)),
            WithdrawalRoot = UInt256.Parse("0x" + new string('5', 64)),
            L2ToL1MessageRoot = UInt256.Parse("0x" + new string('6', 64)),
            L2ToL2MessageRoot = UInt256.Parse("0x" + new string('7', 64)),
            L1MessageHash = UInt256.Parse("0x" + new string('8', 64)),
            DACommitment = UInt256.Parse("0x" + new string('9', 64)),
            BlockContextHash = UInt256.Parse("0x" + new string('a', 64)),
        };

        var proof = await prover.ProveAsync(new ProofRequest
        {
            PublicInputs = publicInputs,
            Witness = new byte[] { 0xFF, 0xEE, 0xDD },
            Kind = ProofType.Zk,
        });

        var verify = await verifier.VerifyAsync(publicInputs, proof.Proof);
        Assert.IsTrue(verify.Valid, verify.FailureReason);
    }

    [TestMethod]
    public async Task GatewayAggregation_TwoChains_GlobalRootCommits()
    {
        var aggregator = new PassThroughAggregator();
        var plugin = new L2GatewayPlugin();
        plugin.UseAggregator(aggregator);

        L2BatchCommitment Mk(uint chainId)
        {
            var z = UInt256.Zero;
            return new L2BatchCommitment
            {
                ChainId = chainId,
                BatchNumber = 1,
                FirstBlock = 100,
                LastBlock = 200,
                PreStateRoot = z,
                PostStateRoot = UInt256.Parse("0x" + new string((char)('a' + (int)(chainId % 10)), 64)),
                TxRoot = z,
                ReceiptRoot = z,
                WithdrawalRoot = z,
                L2ToL1MessageRoot = z,
                L2ToL2MessageRoot = UInt256.Parse("0x" + new string((char)('1' + (int)(chainId % 10)), 64)),
                DACommitment = z,
                PublicInputHash = z,
                ProofType = ProofType.Multisig,
                Proof = new byte[] { (byte)chainId },
            };
        }

        plugin.ReceiveBatch(Mk(1001));
        plugin.ReceiveBatch(Mk(1002));
        Assert.AreEqual(2, aggregator.PendingCount);

        var aggregated = plugin.PullAggregate();
        Assert.IsNotNull(aggregated);
        Assert.AreEqual(2, aggregated!.Constituents.Count);
        Assert.AreNotEqual(UInt256.Zero, aggregated.GlobalMessageRoot);
        Assert.AreEqual(0, aggregator.PendingCount);

        await Task.Yield();
    }

    [TestMethod]
    public async Task EndToEnd_ForcedInclusion_Then_MockRiscV_Then_Aggregation()
    {
        // Cross-component flow: forced inclusion → batch execute → mock RISC-V
        // prove → aggregation. The mock prover stands in for the out-of-process
        // `prove-batch daemon`, which is what produces real SP1 proofs in production.
        // 1. User posts a forced tx because the sequencer was censoring.
        var src = new InMemoryForcedInclusionSource(1001);
        var forcedTx = new byte[] { 0xFE, 0xED, 0xFA, 0xCE };
        src.Enqueue(new ForcedInclusionEntry
        {
            Nonce = 1,
            Sender = UInt160.Parse("0x" + new string('f', 40)),
            TxHash = new UInt256(Crypto.Hash256(forcedTx)),
            SerializedTx = forcedTx,
            DeadlineUnixSeconds = 1_700_010_000,
        });

        // 2. Batcher drains and seals.
        var builder = new BatchBuilder(1001, 1, 100, UInt256.Zero);
        foreach (var e in await src.DrainAsync(10))
        {
            builder.AddTransaction(e.SerializedTx);
            await src.MarkConsumedAsync(e.Nonce);
        }
        builder.AddBlock(100);
        builder.WithBlockContext(SampleContext());

        var executor = new ReferenceBatchExecutor(
            new ReferenceTransactionExecutor(),
            new DerivedPostStateRootOracle());
        var execResult = await executor.ApplyBatchAsync(builder.ToExecutionRequest());

        // 3. Phase-4 prover seam — mock stands in for the out-of-process daemon.
        var vkId = UInt256.Parse("0x" + new string('f', 64));
        var prover = new MockRiscVProver(vkId);
        var publicInputs = new PublicInputs
        {
            ChainId = 1001,
            BatchNumber = 1,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = execResult.PostStateRoot,
            TxRoot = execResult.TxRoot,
            ReceiptRoot = execResult.ReceiptRoot,
            WithdrawalRoot = execResult.WithdrawalRoot,
            L2ToL1MessageRoot = execResult.L2ToL1MessageRoot,
            L2ToL2MessageRoot = execResult.L2ToL2MessageRoot,
            L1MessageHash = UInt256.Zero,
            DACommitment = UInt256.Zero,
            BlockContextHash = StateRootCalculator.HashBlockContext(SampleContext()),
        };
        var proofResult = await prover.ProveAsync(new ProofRequest
        {
            PublicInputs = publicInputs,
            Witness = ReadOnlyMemory<byte>.Empty,
            Kind = ProofType.Zk,
        });

        var commitment = builder.Seal(execResult, UInt256.Zero, proofResult.PublicInputHash, ProofType.Zk, proofResult.Proof);

        // 4. Phase-5 Gateway aggregates this batch with another chain's batch.
        var gateway = new L2GatewayPlugin();
        gateway.ReceiveBatch(commitment);
        gateway.ReceiveBatch(commitment with { ChainId = 1002, L2ToL2MessageRoot = UInt256.Parse("0x" + new string('b', 64)) });
        var aggregated = gateway.PullAggregate();

        Assert.IsNotNull(aggregated);
        Assert.AreEqual(2, aggregated!.Constituents.Count);
        Assert.AreEqual(commitment, aggregated.Constituents[0]);
        Assert.AreNotEqual(UInt256.Zero, aggregated.GlobalMessageRoot);

        // 5. Mock RISC-V verifier accepts the per-chain proof.
        var verifier = new MockRiscVVerifier(vkId);
        var verify = await verifier.VerifyAsync(publicInputs, commitment.Proof);
        Assert.IsTrue(verify.Valid, verify.FailureReason);
    }
}
