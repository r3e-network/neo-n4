using System.Buffers.Binary;
using Neo.Cryptography.ECC;
using Neo.L2.Batch;
using Neo.L2.Executor;
using Neo.L2.Executor.State;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.State;
using Sample.CounterChainExecutor;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// Full-stack integration test for a CUSTOM <see cref="ITransactionExecutor"/>. Proves
/// the framework's seam ("an L2 brings its own executor") works end-to-end: a custom
/// executor + the standard <c>ReferenceBatchExecutor</c> + <c>KeyedStateRootOracle</c>
/// + multisig prover + verifier registry produce well-formed
/// <c>L2BatchCommitment</c>s that pass verification, with state-root continuity
/// preserved across batches.
/// </summary>
/// <remarks>
/// <para>
/// The fixture is the <see cref="CounterChainExecutor"/> sample, wired through the
/// adapter <see cref="KeyedStateStoreAdapter"/> so its writes land in the same
/// <c>KeyedStateStore</c> the post-state-root oracle hashes. This means
/// <c>PostStateRoot</c> changes if and only if the executor's actual state mutations
/// changed — a sanity check the synthetic XOR oracle in
/// <see cref="DerivedPostStateRootOracle"/> can't provide.
/// </para>
/// <para>
/// What this test pins:
/// </para>
/// <list type="bullet">
///   <item><description>Custom executor + framework pipeline = consistent commitments.</description></item>
///   <item><description>State-root continuity (<c>preStateRoot[N+1] == postStateRoot[N]</c>).</description></item>
///   <item><description>Per-batch state-root reflects the executor's writes (counter went up, root changed).</description></item>
///   <item><description>Withdrawal + message side-effects flow through to <c>WithdrawalRoot</c> + outbox roots.</description></item>
///   <item><description>Multisig prover / verifier round-trip on commitments built from custom-executor batches.</description></item>
/// </list>
/// </remarks>
[TestClass]
public class UT_E2E_CustomExecutor_FullStack
{
    private const uint ChainId = 1100;
    private const uint OtherChainId = 1200;

    private static readonly UInt160 EmittingContract = UInt160.Parse("0x" + new string('e', 40));

    private static UInt160 Addr(byte b)
    {
        var bytes = new byte[20];
        bytes[0] = b;
        return new UInt160(bytes);
    }

    private static (ECPoint pub, byte[] priv) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return (ECCurve.Secp256r1.G * priv, priv);
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
    public async Task CustomExecutor_FullPipeline_ProducesValidCommitmentsWithContinuity()
    {
        // Wire: state store + oracle + adapter so the executor's writes participate in
        // the post-state root.
        var stateStore = new KeyedStateStore();
        var oracle = new KeyedStateRootOracle(stateStore);
        var adapter = new KeyedStateStoreAdapter(stateStore);

        // Wire: custom executor.
        var customExec = new Sample.CounterChainExecutor.CounterChainExecutor(
            chainId: ChainId,
            state: adapter,
            emittingContract: EmittingContract);
        var batchExecutor = new ReferenceBatchExecutor(customExec, oracle);

        // Wire: validators + multisig prover/verifier.
        var validators = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var prover = new AttestationProver(new InMemorySignerSet(validators));
        var verifierRegistry = new VerifierRegistry();
        verifierRegistry.Register(new AttestationVerifier(validators.Select(v => v.pub), threshold: 3));

        var preStateRoot = stateStore.ComputeRoot();  // empty store → zero
        Assert.AreEqual(UInt256.Zero, preStateRoot, "empty store starts at zero root");
        var rootSeq = new List<UInt256> { preStateRoot };
        var commitments = new List<L2BatchCommitment>();

        // 3 batches, each: 2 increments, 1 withdrawal, 1 message → all 4 root channels exercised.
        for (var batchNum = 1; batchNum <= 3; batchNum++)
        {
            var alice = Addr((byte)(0xA0 + batchNum));
            var bob = Addr((byte)(0xB0 + batchNum));

            ReadOnlyMemory<byte>[] txs =
            {
                CounterTxBuilder.IncrementCounter(alice, (ulong)(100 * batchNum)),
                CounterTxBuilder.IncrementCounter(bob, (ulong)(50 * batchNum)),
                CounterTxBuilder.EmitWithdrawal(alice, Addr(0xCC), (ulong)(7 * batchNum)),
                CounterTxBuilder.EmitMessage(OtherChainId, new byte[] { (byte)batchNum }),
            };

            var execReq = new BatchExecutionRequest
            {
                ChainId = ChainId,
                BatchNumber = (ulong)batchNum,
                PreStateRoot = preStateRoot,
                Transactions = txs,
                L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
                BlockContext = Ctx(batchNum),
            };
            var execResult = await batchExecutor.ApplyBatchAsync(execReq);

            // The executor mutates state on every IncrementCounter — so postStateRoot
            // MUST advance, and MUST NOT match preStateRoot. This is the sanity check
            // that the adapter actually plumbs writes through to the oracle's store.
            Assert.AreNotEqual(preStateRoot, execResult.PostStateRoot,
                $"batch {batchNum}: postStateRoot must change because IncrementCounter wrote");

            // All four roots should be non-zero: 4 txs → non-trivial txRoot + receiptRoot;
            // EmitWithdrawal → non-zero withdrawalRoot; EmitMessage → non-zero L2ToL2 root.
            Assert.AreNotEqual(UInt256.Zero, execResult.TxRoot, $"batch {batchNum}: txRoot");
            Assert.AreNotEqual(UInt256.Zero, execResult.ReceiptRoot, $"batch {batchNum}: receiptRoot");
            Assert.AreNotEqual(UInt256.Zero, execResult.WithdrawalRoot, $"batch {batchNum}: withdrawalRoot");
            Assert.AreNotEqual(UInt256.Zero, execResult.L2ToL2MessageRoot, $"batch {batchNum}: L2ToL2MessageRoot");

            // Per-opcode gas: 2 × 100 (IncrementCounter) + 500 (EmitWithdrawal) + 200 (EmitMessage) = 900.
            Assert.AreEqual(
                Sample.CounterChainExecutor.CounterChainExecutor.GasIncrementCounter * 2
                + Sample.CounterChainExecutor.CounterChainExecutor.GasEmitWithdrawal
                + Sample.CounterChainExecutor.CounterChainExecutor.GasEmitMessage,
                execResult.GasConsumed,
                $"batch {batchNum}: total gas must match the per-opcode schedule");

            // Build PublicInputs + ProofResult + L2BatchCommitment exactly as the production
            // batch sealer does — no special-casing for custom executors.
            var publicInputs = new PublicInputs
            {
                ChainId = ChainId,
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
                ChainId = ChainId,
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

            // Multisig verifier MUST accept commitments built from custom-executor batches.
            // Pin so a future seam change can't subtly invalidate the commitment shape.
            var verify = await verifierRegistry.VerifyAsync(commitment, publicInputs);
            Assert.IsTrue(verify.Valid,
                $"batch {batchNum}: commitment from custom executor must verify ({verify.FailureReason})");

            // BatchSerializer must round-trip the commitment cleanly. Tests at the unit
            // level pin this; here we re-pin the cross-component invariant on a
            // commitment built from genuinely custom-executor data.
            var serialized = BatchSerializer.Encode(commitment);
            var roundTripped = BatchSerializer.Decode(serialized);
            Assert.AreEqual(commitment, roundTripped,
                $"batch {batchNum}: BatchSerializer round-trip must be identity");

            // Continuity invariant.
            if (batchNum > 1)
            {
                Assert.AreEqual(commitments[^1].PostStateRoot, commitment.PreStateRoot,
                    $"batch {batchNum}: preRoot must equal previous postRoot");
            }
            commitments.Add(commitment);
            rootSeq.Add(execResult.PostStateRoot);
            preStateRoot = execResult.PostStateRoot;
        }

        // Every postStateRoot in the sequence must be distinct: each batch wrote new
        // counter entries for distinct senders, so no two stores can hash to the same
        // root. Pin so a non-deterministic state-root-or-store-bug doesn't go unnoticed.
        for (var i = 0; i < rootSeq.Count; i++)
            for (var j = i + 1; j < rootSeq.Count; j++)
                Assert.AreNotEqual(rootSeq[i], rootSeq[j],
                    $"roots at positions {i} and {j} collided (state should have been distinct)");

        // Final state check: after 3 batches each writing two counters, the store should
        // hold 6 distinct counter entries.
        var finalEntries = stateStore.EnumerateSorted().ToList();
        Assert.AreEqual(6, finalEntries.Count,
            "store should hold one counter entry per (sender, batch) — 2 senders × 3 batches");

        // Spot-check Alice's counter for the last batch matches what the executor wrote.
        var aliceLast = Addr((byte)(0xA0 + 3));
        var aliceKey = BuildCounterKey(aliceLast);
        var aliceVal = stateStore.Get(aliceKey);
        Assert.AreEqual(8, aliceVal.Length, "counter value is 8 bytes (u64 LE)");
        Assert.AreEqual((ulong)(100 * 3), BinaryPrimitives.ReadUInt64LittleEndian(aliceVal.Span),
            "Alice's batch-3 counter should equal the IncrementCounter amount");
    }

    [TestMethod]
    public async Task CustomExecutor_FailedTxsDoNotPolluteRoots()
    {
        // Same wiring as the happy path, but the batch contains a deliberately malformed
        // tx in addition to good ones. The malformed tx should produce a Failed receipt
        // (success=false, gas charged), and its withdrawals/messages must NOT flow through
        // to the per-batch trees — same defense-in-depth ReferenceBatchExecutor enforces
        // for any executor.
        var stateStore = new KeyedStateStore();
        var oracle = new KeyedStateRootOracle(stateStore);
        var adapter = new KeyedStateStoreAdapter(stateStore);
        var customExec = new Sample.CounterChainExecutor.CounterChainExecutor(
            chainId: ChainId, state: adapter, emittingContract: EmittingContract);
        var batchExecutor = new ReferenceBatchExecutor(customExec, oracle);

        var alice = Addr(0xAA);
        ReadOnlyMemory<byte>[] txs =
        {
            CounterTxBuilder.IncrementCounter(alice, 100),  // good
            new byte[] { 0xFF, 0xFF, 0xFF },                 // unknown opcode → Failed
            CounterTxBuilder.IncrementCounter(alice, 50),   // good
        };
        var execReq = new BatchExecutionRequest
        {
            ChainId = ChainId,
            BatchNumber = 1,
            PreStateRoot = UInt256.Zero,
            Transactions = txs,
            L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
            BlockContext = Ctx(1),
        };
        var execResult = await batchExecutor.ApplyBatchAsync(execReq);

        // Batch produces 3 receipts (one per tx). State has alice's counter at 150.
        // Verify total gas: 100 (good) + 0 (unknown opcode charges 0) + 100 (good) = 200.
        Assert.AreEqual(
            Sample.CounterChainExecutor.CounterChainExecutor.GasIncrementCounter * 2,
            execResult.GasConsumed);

        // Batch root channels: txRoot + receiptRoot non-zero (3 txs total). withdrawalRoot
        // and message roots SHOULD be zero (no successful tx emitted any).
        Assert.AreNotEqual(UInt256.Zero, execResult.TxRoot);
        Assert.AreNotEqual(UInt256.Zero, execResult.ReceiptRoot);
        Assert.AreEqual(UInt256.Zero, execResult.WithdrawalRoot,
            "no successful tx emitted a withdrawal — root must be zero");
        Assert.AreEqual(UInt256.Zero, execResult.L2ToL1MessageRoot);
        Assert.AreEqual(UInt256.Zero, execResult.L2ToL2MessageRoot);

        // State got 150 from the two good increments.
        var aliceKey = BuildCounterKey(alice);
        var aliceVal = stateStore.Get(aliceKey);
        Assert.AreEqual(150UL, BinaryPrimitives.ReadUInt64LittleEndian(aliceVal.Span));
    }

    private static byte[] BuildCounterKey(UInt160 sender)
    {
        var senderBytes = sender.GetSpan().ToArray();
        var prefix = Sample.CounterChainExecutor.CounterChainExecutor.CounterKeyPrefix;
        var key = new byte[prefix.Length + senderBytes.Length];
        prefix.CopyTo(key, 0);
        senderBytes.CopyTo(key, prefix.Length);
        return key;
    }
}
