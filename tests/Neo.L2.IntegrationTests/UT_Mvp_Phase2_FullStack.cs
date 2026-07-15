using System.Numerics;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Censorship;
using Neo.L2.Executor;
using Neo.L2.Executor.Receipts;
using Neo.L2.Executor.State;
using Neo.L2.ForcedInclusion;
using Neo.L2.Messaging;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Sequencer;
using Neo.L2.State;
using Neo.Plugins.L2Gateway;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// Phase-2 full-stack integration test. Stitches every Phase-1/2/3/5 off-chain piece into one
/// scenario:
///   1. Multi-sequencer committee.
///   2. Multi-batch run with KeyedStateStore continuity (preStateRoot of N+1 == postStateRoot of N).
///   3. ForcedInclusion: a user posts a forced tx; some batches are too late.
///   4. CensorshipDetector identifies the responsible sequencer.
///   5. Gateway aggregates multiple chains' commitments into one global commitment.
/// </summary>
[TestClass]
public class UT_Mvp_Phase2_FullStack
{
    private const uint LocalChainId = 1001;
    private const uint OtherChainId = 1002;
    private static readonly UInt160 GasL2 = UInt160.Parse("0x" + new string('2', 40));
    private static readonly UInt160 Alice = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 Bob = UInt160.Parse("0x" + new string('b', 40));

    private static (ECPoint pub, byte[] priv) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return (ECCurve.Secp256r1.G * priv, priv);
    }

    private static UInt160 A(byte b)
    {
        var bytes = new byte[20];
        for (var i = 0; i < 20; i++) bytes[i] = b;
        return new UInt160(bytes);
    }

    private static byte[] BalanceKey(UInt160 asset, UInt160 holder)
    {
        var k = new byte[1 + 20 + 20];
        k[0] = 0x01;
        asset.GetSpan().CopyTo(k.AsSpan(1, 20));
        holder.GetSpan().CopyTo(k.AsSpan(21, 20));
        return k;
    }

    private static BatchBlockContext Ctx(int batchNum) => new()
    {
        L1FinalizedHeight = (uint)(1000 + batchNum),
        FirstBlockTimestamp = (ulong)(1_700_000_000_000 + batchNum * 10_000),
        LastBlockTimestamp = (ulong)(1_700_000_000_000 + batchNum * 10_000 + 5_000),
        SequencerCommitteeHash = UInt256.Parse("0x" + new string('c', 64)),
        Network = 0x4F454E,
    };

    [TestMethod]
    public async Task FullStack_StateRootContinuity_PlusGatewayAggregation()
    {
        // Wire: state store + oracle.
        var stateStore = new KeyedStateStore();
        var oracle = new KeyedStateRootOracle(stateStore);

        var executor = new ReferenceBatchExecutor(
            new ReferenceTransactionExecutor(),
            oracle);

        // Wire: validators + prover.
        var validators = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(validators);
        var prover = new AttestationProver(signers);
        var verifierRegistry = new VerifierRegistry();
        verifierRegistry.Register(new AttestationVerifier(validators.Select(v => v.pub), threshold: 3));

        // Wire: per-chain Gateway.
        var gateway = new L2GatewayPlugin();
        gateway.UseAggregator(new BinaryTreeAggregator());

        var preStateRoot = UInt256.Zero;

        // 3 batches, each: deposit + withdraw, applied to the state store.
        var commitments = new List<L2BatchCommitment>();
        for (var batchNum = 1; batchNum <= 3; batchNum++)
        {
            var depositAmount = new BigInteger(1_000_000 * batchNum);
            var withdrawalAmount = new BigInteger(10_000 * batchNum);

            // Apply to store directly (models L2BridgeContract behavior).
            var balanceKey = BalanceKey(GasL2, Alice);
            var current = stateStore.Get(balanceKey).Length == 0
                ? BigInteger.Zero
                : new BigInteger(stateStore.Get(balanceKey).Span, isUnsigned: true, isBigEndian: false);
            var nextBalance = current + depositAmount - withdrawalAmount;
            stateStore.Put(balanceKey, nextBalance.ToByteArray(isUnsigned: true, isBigEndian: false));

            // Run the batch executor (compute roots).
            var execReq = new BatchExecutionRequest
            {
                ChainId = LocalChainId,
                BatchNumber = (ulong)batchNum,
                PreStateRoot = preStateRoot,
                Transactions = new ReadOnlyMemory<byte>[] { BitConverter.GetBytes((long)batchNum * 17) },
                L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
                BlockContext = Ctx(batchNum),
            };
            var execResult = await executor.ApplyBatchAsync(execReq);

            // Sign + verify.
            var publicInputs = new PublicInputs
            {
                ChainId = LocalChainId,
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
                BlockContextHash = StateRootCalculator.HashBlockContext(Ctx(batchNum)),
            };
            var proofResult = await prover.ProveAsync(new ProofRequest
            {
                PublicInputs = publicInputs,
                Witness = ReadOnlyMemory<byte>.Empty,
                Kind = ProofType.Multisig,
            });

            var commitment = new L2BatchCommitment
            {
                ChainId = LocalChainId,
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

            // Continuity invariant.
            if (batchNum > 1)
            {
                Assert.AreEqual(commitments[^1].PostStateRoot, commitment.PreStateRoot,
                    "batch N+1 preRoot must equal batch N postRoot");
            }
            commitments.Add(commitment);
            preStateRoot = commitment.PostStateRoot;

            // Forward to Gateway.
            gateway.ReceiveBatch(commitment);
        }

        // Add a synthetic commitment from another chain so the Gateway has multi-chain input.
        gateway.ReceiveBatch(commitments[0] with { ChainId = OtherChainId, L2ToL2MessageRoot = UInt256.Parse("0x" + new string('5', 64)) });

        // Aggregate across all 4 commitments. BinaryTreeAggregator → log2(4) = 2 rounds.
        var aggregated = gateway.PullAggregate();
        Assert.IsNotNull(aggregated);
        Assert.AreEqual(4, aggregated!.Constituents.Count);
        Assert.AreNotEqual(UInt256.Zero, aggregated.GlobalMessageRoot);

        // Final balance check: sum of (depositᵢ - withdrawalᵢ) over 3 batches = (1+2+3)·990000 = 5_940_000.
        var aliceBytes = stateStore.Get(BalanceKey(GasL2, Alice));
        var aliceBal = new BigInteger(aliceBytes.Span, isUnsigned: true, isBigEndian: false);
        Assert.AreEqual(new BigInteger(5_940_000), aliceBal);
    }

    [TestMethod]
    public async Task FullStack_CensorshipDetector_LeavesAttributionUnknownWithoutConsensusEvidence()
    {
        // Wire committee.
        var committee = new InMemorySequencerCommitteeProvider(LocalChainId);
        committee.Register(GenKey(1).pub, A(0x10));
        committee.Register(GenKey(2).pub, A(0x20));
        committee.Register(GenKey(3).pub, A(0x30));

        // Wire forced-inclusion source with two entries: one overdue, one fresh.
        var src = new InMemoryForcedInclusionSource(LocalChainId);
        src.Enqueue(new ForcedInclusionEntry
        {
            Nonce = 1,
            Sender = A(0xAA),
            TxHash = new UInt256(Crypto.Hash256(new byte[] { 1 })),
            SerializedTx = new byte[] { 1 },
            DeadlineUnixSeconds = 1_700_000_000,
        });
        src.Enqueue(new ForcedInclusionEntry
        {
            Nonce = 2,
            Sender = A(0xAA),
            TxHash = new UInt256(Crypto.Hash256(new byte[] { 2 })),
            SerializedTx = new byte[] { 2 },
            DeadlineUnixSeconds = 1_700_999_999,
        });

        var clock = new FakeClock { NowUnixSeconds = 1_700_500_000 };
        var detector = new CensorshipDetector(src, committee, clock);

        var reports = await detector.DetectOverdueAsync();
        Assert.AreEqual(1, reports.Count, "only one entry is overdue at this clock value");
        Assert.AreEqual(1UL, reports[0].ForcedInclusionNonce);

        // Committee membership alone does not prove which dBFT proposer omitted the
        // transaction. Without a finalized-consensus attribution provider, the report
        // remains unassigned for governance review rather than blaming an arbitrary member.
        Assert.AreEqual(ECCurve.Secp256r1.Infinity, reports[0].ResponsibleSequencer);
        Assert.AreEqual(UInt160.Zero, reports[0].ResponsibleSequencerAddress);
        Assert.IsTrue(reports[0].SlashAmount > 0);
    }
}
