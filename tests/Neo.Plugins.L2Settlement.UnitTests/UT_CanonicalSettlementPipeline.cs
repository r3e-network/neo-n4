using Neo.Cryptography;
using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Executor.Receipts;
using Neo.L2.Persistence;
using Neo.L2.Proving.RiscVZk;
using Neo.L2.State;

namespace Neo.Plugins.L2Settlement.UnitTests;

[TestClass]
public class UT_CanonicalSettlementPipeline
{
    private const uint ChainId = 1001;
    private static readonly UInt256 SafeSemantic =
        ExecutionSemanticIds.FromName("neo4.test.authenticated-execution.v1");
    private static readonly UInt256 OtherSafeSemantic =
        ExecutionSemanticIds.FromName("neo4.test.other-authenticated-execution.v1");
    private static readonly UInt256 VerificationKeyId = H(0x71);

    [TestMethod]
    public void ZkProfile_RejectsMockProver()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var prover = new RecordingZkProver(SafeSemantic)
        {
            ProducesCryptographicProof = false,
        };

        Assert.ThrowsExactly<ArgumentException>(() => new CanonicalSettlementPipeline(
            new TestExecutor(SafeSemantic),
            new ProductionDaWriter(),
            store,
            prover,
            new RecordingSettlementClient(),
            ZkProfile()));
    }

    [TestMethod]
    public async Task ZkProfile_RejectsMismatchedExecutionSemantic()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var pipeline = CreateZkPipeline(
            store,
            new TestExecutor(OtherSafeSemantic),
            new ProductionDaWriter(),
            new RecordingZkProver(SafeSemantic),
            new RecordingSettlementClient());

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await pipeline.PersistAsync(BuildBatch()));
    }

    [TestMethod]
    public async Task ZkProfile_RejectsUnauthenticatedOrEmptyWitness()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var unauthenticated = CreateZkPipeline(
            store,
            new TestExecutor(SafeSemantic, authenticated: false),
            new ProductionDaWriter(),
            new RecordingZkProver(SafeSemantic),
            new RecordingSettlementClient());

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await unauthenticated.PersistAsync(BuildBatch()));

        using var emptyBackend = new InMemoryKeyValueStore();
        using var emptyStore = new KeyValueProofWitnessStore(emptyBackend);
        using var empty = CreateZkPipeline(
            emptyStore,
            new TestExecutor(SafeSemantic, returnEmptyStateWitness: true),
            new ProductionDaWriter(),
            new RecordingZkProver(SafeSemantic),
            new RecordingSettlementClient());

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await empty.PersistAsync(BuildBatch()));
    }

    [TestMethod]
    public async Task ZkProfile_RejectsPreviewAndLegacyExecutionSemantics()
    {
        foreach (var semantic in new[]
        {
            ExecutionSemanticIds.ReferenceNoOpV1,
            ExecutionSemanticIds.NeoN3ApplicationEngineV1,
            ExecutionSemanticIds.NeoVm2PolkaVmStatelessPreviewV1,
            ExecutionSemanticIds.Sp1LegacyNeoN3GuestV1,
        })
        {
            using var backend = new InMemoryKeyValueStore();
            using var store = new KeyValueProofWitnessStore(backend);
            using var pipeline = CreateZkPipeline(
                store,
                new TestExecutor(semantic),
                new ProductionDaWriter(),
                new RecordingZkProver(semantic),
                new RecordingSettlementClient());

            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                async () => await pipeline.PersistAsync(BuildBatch()),
                $"semantic {semantic} must fail closed");
        }
    }

    [TestMethod]
    public async Task PersistAndProve_CommitsFullArtifactBeforeNonEmptyProverWitness()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var writer = new ProductionDaWriter();
        var prover = new RecordingZkProver(SafeSemantic) { Store = store };
        var client = new RecordingSettlementClient();
        using var pipeline = CreateZkPipeline(
            store, new TestExecutor(SafeSemantic), writer, prover, client);
        var batch = BuildBatch();

        await pipeline.PersistAsync(batch);
        var artifact = await store.GetAsync(ChainId, batch.BatchNumber);
        Assert.IsNotNull(artifact);
        Assert.AreEqual(
            StateRootCalculator.HashL1Messages(batch.L1Messages),
            artifact.PublicInputs.L1MessageHash);
        Assert.AreEqual(
            StateRootCalculator.HashBlockContext(batch.BlockContext),
            artifact.PublicInputs.BlockContextHash);
        Assert.AreEqual(
            ExecutionPayloadSerializer.ComputeCommitment(artifact.ExecutionPayload),
            artifact.DAReceipt.Commitment);

        await pipeline.ReconcileAsync();
        await pipeline.ReconcileAsync();

        Assert.IsFalse(prover.LastWitness.IsEmpty);
        var provedArtifact = ProofWitnessArtifactSerializer.Decode(prover.LastWitness.Span);
        Assert.AreEqual(artifact.ContentHash, provedArtifact.ContentHash);
        var manifest = await store.GetProofAsync(artifact.ContentHash);
        Assert.IsNotNull(manifest);
        Assert.AreEqual(artifact.ContentHash, manifest.ArtifactContentHash);
        Assert.IsTrue(manifest.SettlementObserved);
        Assert.AreEqual(1, client.SubmitCount);
    }

    [TestMethod]
    public async Task Persist_RejectsTamperedDaReceiptOrUnavailablePayload()
    {
        using var corruptBackend = new InMemoryKeyValueStore();
        using var corruptStore = new KeyValueProofWitnessStore(corruptBackend);
        using var corruptPipeline = CreateZkPipeline(
            corruptStore,
            new TestExecutor(SafeSemantic),
            new ProductionDaWriter { CorruptCommitment = true },
            new RecordingZkProver(SafeSemantic),
            new RecordingSettlementClient());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await corruptPipeline.PersistAsync(BuildBatch()));

        using var unavailableBackend = new InMemoryKeyValueStore();
        using var unavailableStore = new KeyValueProofWitnessStore(unavailableBackend);
        using var unavailablePipeline = CreateZkPipeline(
            unavailableStore,
            new TestExecutor(SafeSemantic),
            new ProductionDaWriter { Available = false },
            new RecordingZkProver(SafeSemantic),
            new RecordingSettlementClient());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await unavailablePipeline.PersistAsync(BuildBatch()));
    }

    [TestMethod]
    public async Task ProveFailure_PreservesCommittedArtifactForRetry()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var prover = new RecordingZkProver(SafeSemantic) { FailNextProof = true };
        var client = new RecordingSettlementClient();
        using var pipeline = CreateZkPipeline(
            store,
            new TestExecutor(SafeSemantic),
            new ProductionDaWriter(),
            prover,
            client);
        var batch = BuildBatch();
        await pipeline.PersistAsync(batch);
        var artifact = await store.GetAsync(ChainId, batch.BatchNumber);
        Assert.IsNotNull(artifact);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await pipeline.ReconcileAsync());
        Assert.IsNull(await store.GetProofAsync(artifact.ContentHash));
        Assert.AreEqual(1, await pipeline.GetPendingCountAsync());

        await pipeline.ReconcileAsync();
        await pipeline.ReconcileAsync();
        Assert.AreEqual(2, prover.ProveCount);
        Assert.AreEqual(1, client.SubmitCount);
        Assert.AreEqual(0, await pipeline.GetPendingCountAsync());
    }

    [TestMethod]
    public async Task SubmitFailure_PreservesProofManifestForRetry()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var prover = new RecordingZkProver(SafeSemantic);
        var client = new RecordingSettlementClient { FailNextSubmitBeforeRecord = true };
        using var pipeline = CreateZkPipeline(
            store,
            new TestExecutor(SafeSemantic),
            new ProductionDaWriter(),
            prover,
            client);
        var batch = BuildBatch();
        await pipeline.PersistAsync(batch);
        var artifact = await store.GetAsync(ChainId, batch.BatchNumber);
        Assert.IsNotNull(artifact);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await pipeline.ReconcileAsync());
        var manifest = await store.GetProofAsync(artifact.ContentHash);
        Assert.IsNotNull(manifest);
        Assert.IsFalse(manifest.SettlementObserved);
        Assert.AreEqual(1, prover.ProveCount);

        await pipeline.ReconcileAsync();
        await pipeline.ReconcileAsync();
        Assert.AreEqual(1, prover.ProveCount, "persisted proof must be reused");
        Assert.AreEqual(2, client.SubmitCount);
        Assert.AreEqual(0, await pipeline.GetPendingCountAsync());
    }

    [TestMethod]
    public async Task RestartReconcile_AfterSubmitCrash_DoesNotSubmitTwice()
    {
        using var backend = new InMemoryKeyValueStore();
        var writer = new ProductionDaWriter();
        var prover = new RecordingZkProver(SafeSemantic);
        var client = new RecordingSettlementClient { FailNextSubmitAfterRecord = true };
        UInt256 contentHash;

        using (var firstStore = new KeyValueProofWitnessStore(backend))
        using (var firstPipeline = CreateZkPipeline(
            firstStore,
            new TestExecutor(SafeSemantic),
            writer,
            prover,
            client))
        {
            var batch = BuildBatch();
            await firstPipeline.PersistAsync(batch);
            contentHash = (await firstStore.GetAsync(ChainId, batch.BatchNumber))!.ContentHash;
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await firstPipeline.ReconcileAsync());
            Assert.IsFalse((await firstStore.GetProofAsync(contentHash))!.SettlementObserved);
        }

        using var restartedStore = new KeyValueProofWitnessStore(backend);
        using var restartedPipeline = CreateZkPipeline(
            restartedStore,
            new TestExecutor(SafeSemantic),
            writer,
            prover,
            client);
        await restartedPipeline.ReconcileAsync();
        await restartedPipeline.ReconcileAsync();

        var reconciled = await restartedStore.GetProofAsync(contentHash);
        Assert.IsNotNull(reconciled);
        Assert.IsTrue(reconciled.SettlementObserved);
        Assert.IsNull(reconciled.L1TransactionHash);
        Assert.AreEqual(1, client.SubmitCount);
        Assert.AreEqual(0, await restartedPipeline.GetPendingCountAsync());
    }

    [TestMethod]
    public async Task SuccessfulReconcile_SubmitsExactlyOnce()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var client = new RecordingSettlementClient();
        using var pipeline = CreateZkPipeline(
            store,
            new TestExecutor(SafeSemantic),
            new ProductionDaWriter(),
            new RecordingZkProver(SafeSemantic),
            client);
        await pipeline.PersistAsync(BuildBatch());

        await pipeline.ReconcileAsync();
        await pipeline.ReconcileAsync();

        Assert.AreEqual(1, client.SubmitCount);
        Assert.AreEqual(0, await pipeline.GetPendingCountAsync());
    }

    [TestMethod]
    public async Task Reconcile_DurableArtifacts_SubmitsInBatchOrder()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var client = new RecordingSettlementClient();
        using var pipeline = CreateZkPipeline(
            store,
            new TestExecutor(SafeSemantic),
            new ProductionDaWriter(),
            new RecordingZkProver(SafeSemantic),
            client);
        await pipeline.PersistAsync(BuildBatch(1));
        await pipeline.PersistAsync(BuildBatch(2));

        await pipeline.ReconcileAsync();
        Assert.IsNull((await store.GetAsync(ChainId, 2)) is { } second
            ? await store.GetProofAsync(second.ContentHash)
            : null);
        await pipeline.ReconcileAsync();
        await pipeline.ReconcileAsync();

        CollectionAssert.AreEqual(new ulong[] { 1, 2 }, client.SubmittedBatchNumbers.ToArray());
        Assert.AreEqual(0, await pipeline.GetPendingCountAsync());
    }

    [TestMethod]
    public async Task RevertedStatus_RemainsPendingAndIsNeverResubmittedSilently()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var client = new RecordingSettlementClient();
        using var pipeline = CreateZkPipeline(
            store,
            new TestExecutor(SafeSemantic),
            new ProductionDaWriter(),
            new RecordingZkProver(SafeSemantic),
            client);
        var batch = BuildBatch();
        await pipeline.PersistAsync(batch);
        var artifact = await store.GetAsync(ChainId, batch.BatchNumber);
        Assert.IsNotNull(artifact);
        client.SetStatus(ChainId, batch.BatchNumber, BatchStatus.Reverted);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await pipeline.ReconcileAsync());

        var manifest = await store.GetProofAsync(artifact.ContentHash);
        Assert.IsNotNull(manifest);
        Assert.IsFalse(manifest.SettlementObserved);
        Assert.AreEqual(0, client.SubmitCount);
        Assert.AreEqual(1, await pipeline.GetPendingCountAsync());
    }

    [TestMethod]
    public async Task Dispose_DuringInFlightReconcile_DoesNotFaultCompletedSubmission()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var client = new BlockingSettlementClient();
        var pipeline = CreateZkPipeline(
            store,
            new TestExecutor(SafeSemantic),
            new ProductionDaWriter(),
            new RecordingZkProver(SafeSemantic),
            client);
        await pipeline.PersistAsync(BuildBatch());

        var reconcile = pipeline.ReconcileAsync();
        await client.SubmitEntered.Task;
        pipeline.Dispose();
        client.AllowSubmit.SetResult();

        await reconcile;
        Assert.AreEqual(1, client.SubmitCount);
    }

    [TestMethod]
    public async Task ForcedInclusion_FinalizesConsumeOnlyAfterFinalityAndRecoversRetry()
    {
        using var backend = new InMemoryKeyValueStore();
        var writer = new ProductionDaWriter();
        var prover = new RecordingZkProver(SafeSemantic);
        var client = new RecordingSettlementClient();
        var finalizer = new RecordingForcedInclusionFinalizer { FailNextConsume = true };
        UInt256 contentHash;
        UInt256 txRoot;

        using (var firstStore = new KeyValueProofWitnessStore(backend))
        using (var firstPipeline = CreateZkPipeline(
            firstStore,
            new TestExecutor(SafeSemantic),
            writer,
            prover,
            client,
            finalizer))
        {
            var batch = BuildForcedBatch();
            await firstPipeline.PersistAsync(batch);
            var artifact = await firstStore.GetAsync(ChainId, batch.BatchNumber);
            Assert.IsNotNull(artifact);
            contentHash = artifact.ContentHash;
            txRoot = artifact.ExecutionResult.TxRoot;
            CollectionAssert.AreEqual(
                new ulong[] { 44 },
                (await firstPipeline.GetTrackedForcedInclusionNoncesAsync()).ToArray());
            Assert.AreEqual(1, artifact.ExecutionPayload.ForcedInclusions.Count);
            Assert.AreEqual(0U, artifact.ExecutionPayload.ForcedInclusions[0].LeafIndex);

            await firstPipeline.ReconcileAsync();
            Assert.AreEqual(0, finalizer.ConsumeCount, "pending settlement must not consume");
            Assert.AreEqual(1, await firstPipeline.GetPendingCountAsync());

            client.SetStatus(ChainId, batch.BatchNumber, BatchStatus.Finalized);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await firstPipeline.ReconcileAsync());
            Assert.AreEqual(1, finalizer.ConsumeCount);
            Assert.IsFalse((await firstStore.GetProofAsync(contentHash))!.ForcedInclusionFinalized);
        }

        using var restartedStore = new KeyValueProofWitnessStore(backend);
        using var restartedPipeline = CreateZkPipeline(
            restartedStore,
            new TestExecutor(SafeSemantic),
            writer,
            prover,
            client,
            finalizer);
        await restartedPipeline.ReconcileAsync();
        await restartedPipeline.ReconcileAsync();

        var finalized = await restartedStore.GetProofAsync(contentHash);
        Assert.IsNotNull(finalized);
        Assert.IsTrue(finalized.ForcedInclusionFinalized);
        Assert.AreEqual(2, finalizer.ConsumeCount, "failed consume retries once after restart");
        Assert.AreEqual(0, await restartedPipeline.GetPendingCountAsync());
        CollectionAssert.AreEqual(
            new ulong[] { 44 },
            (await restartedPipeline.GetTrackedForcedInclusionNoncesAsync()).ToArray());
        var proof = finalizer.LastProofs.Single();
        Assert.IsTrue(new Neo.L2.State.MerkleProof
        {
            Leaf = proof.TxHash,
            LeafIndex = checked((int)proof.LeafIndex),
            Siblings = proof.Siblings,
            PathBitmap = proof.LeafIndex,
        }.Verify(txRoot));
    }

    [TestMethod]
    public async Task Restart_BeforeProof_ReprovesDurableArtifact()
    {
        using var backend = new InMemoryKeyValueStore();
        var writer = new ProductionDaWriter();
        var prover = new RecordingZkProver(SafeSemantic);
        var client = new RecordingSettlementClient();
        UInt256 contentHash;

        using (var store = new KeyValueProofWitnessStore(backend))
        using (var pipeline = CreateZkPipeline(
            store, new TestExecutor(SafeSemantic), writer, prover, client))
        {
            var batch = BuildBatch();
            await pipeline.PersistAsync(batch);
            contentHash = (await store.GetAsync(ChainId, batch.BatchNumber))!.ContentHash;
            Assert.IsNull(await store.GetProofAsync(contentHash));
        }

        using var restartedStore = new KeyValueProofWitnessStore(backend);
        using var restarted = CreateZkPipeline(
            restartedStore, new TestExecutor(SafeSemantic), writer, prover, client);
        await restarted.ReconcileAsync();
        await restarted.ReconcileAsync();

        Assert.AreEqual(1, prover.ProveCount);
        Assert.IsTrue((await restartedStore.GetProofAsync(contentHash))!.SettlementObserved);
        Assert.AreEqual(1, client.SubmitCount);
    }

    [TestMethod]
    public async Task Restart_AfterProof_ReusesManifestAfterSubmitFailure()
    {
        using var backend = new InMemoryKeyValueStore();
        var writer = new ProductionDaWriter();
        var prover = new RecordingZkProver(SafeSemantic);
        var client = new RecordingSettlementClient { FailNextSubmitBeforeRecord = true };
        UInt256 contentHash;

        using (var store = new KeyValueProofWitnessStore(backend))
        using (var pipeline = CreateZkPipeline(
            store, new TestExecutor(SafeSemantic), writer, prover, client))
        {
            var batch = BuildBatch();
            await pipeline.PersistAsync(batch);
            contentHash = (await store.GetAsync(ChainId, batch.BatchNumber))!.ContentHash;
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await pipeline.ReconcileAsync());
            Assert.AreEqual(
                ProofSubmissionState.ProofReady,
                (await store.GetProofAsync(contentHash))!.SubmissionState);
        }

        using var restartedStore = new KeyValueProofWitnessStore(backend);
        using var restarted = CreateZkPipeline(
            restartedStore, new TestExecutor(SafeSemantic), writer, prover, client);
        await restarted.ReconcileAsync();
        await restarted.ReconcileAsync();

        Assert.AreEqual(1, prover.ProveCount, "restart must reuse the durable proof");
        Assert.AreEqual(2, client.SubmitCount);
        Assert.IsTrue((await restartedStore.GetProofAsync(contentHash))!.SettlementObserved);
    }

    [TestMethod]
    public async Task Restart_SubmittedButUnobserved_UsesTransactionStatusWithoutDuplicate()
    {
        using var backend = new InMemoryKeyValueStore();
        var writer = new ProductionDaWriter();
        var prover = new RecordingZkProver(SafeSemantic);
        var client = new RecordingSettlementClient();
        UInt256 contentHash;
        UInt256 transactionHash;

        using (var store = new KeyValueProofWitnessStore(backend))
        using (var pipeline = CreateZkPipeline(
            store, new TestExecutor(SafeSemantic), writer, prover, client))
        {
            var batch = BuildBatch();
            await pipeline.PersistAsync(batch);
            contentHash = (await store.GetAsync(ChainId, batch.BatchNumber))!.ContentHash;
            await pipeline.ReconcileAsync();
            var submitted = (await store.GetProofAsync(contentHash))!;
            Assert.AreEqual(ProofSubmissionState.Submitted, submitted.SubmissionState);
            Assert.IsFalse(submitted.SettlementObserved);
            transactionHash = submitted.L1TransactionHash!;
            client.SetStatus(ChainId, batch.BatchNumber, BatchStatus.Unknown);
            client.SetTransactionStatus(transactionHash, SettlementTransactionStatus.Pending);
        }

        using var restartedStore = new KeyValueProofWitnessStore(backend);
        using var restarted = CreateZkPipeline(
            restartedStore, new TestExecutor(SafeSemantic), writer, prover, client);
        await restarted.ReconcileAsync();

        Assert.AreEqual(1, client.SubmitCount);
        Assert.AreEqual(1, client.TransactionStatusReadCount);
        Assert.AreEqual(
            ProofSubmissionState.Submitted,
            (await restartedStore.GetProofAsync(contentHash))!.SubmissionState);
        client.SetStatus(ChainId, 1, BatchStatus.Pending);
        await restarted.ReconcileAsync();
        Assert.IsTrue((await restartedStore.GetProofAsync(contentHash))!.SettlementObserved);
    }

    [TestMethod]
    public async Task Restart_SubmittedDropped_ReplacesOnlyAfterExplicitTransactionStatus()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var client = new RecordingSettlementClient();
        using var pipeline = CreateZkPipeline(
            store,
            new TestExecutor(SafeSemantic),
            new ProductionDaWriter(),
            new RecordingZkProver(SafeSemantic),
            client);
        var batch = BuildBatch();
        await pipeline.PersistAsync(batch);
        var contentHash = (await store.GetAsync(ChainId, batch.BatchNumber))!.ContentHash;
        await pipeline.ReconcileAsync();
        var first = (await store.GetProofAsync(contentHash))!;
        var firstTransactionHash = first.L1TransactionHash!;
        client.SetStatus(ChainId, batch.BatchNumber, BatchStatus.Unknown);
        client.SetTransactionStatus(firstTransactionHash, SettlementTransactionStatus.Dropped);

        await pipeline.ReconcileAsync();

        var replacement = (await store.GetProofAsync(contentHash))!;
        Assert.AreEqual(ProofSubmissionState.Submitted, replacement.SubmissionState);
        Assert.AreNotEqual(firstTransactionHash, replacement.L1TransactionHash);
        Assert.AreEqual(2, client.SubmitCount);
        Assert.AreEqual(1, client.TransactionStatusReadCount);
    }

    [TestMethod]
    public async Task Restart_SubmittedUnknown_FailsClosedWithoutDuplicate()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var client = new RecordingSettlementClient();
        using var pipeline = CreateZkPipeline(
            store,
            new TestExecutor(SafeSemantic),
            new ProductionDaWriter(),
            new RecordingZkProver(SafeSemantic),
            client);
        var batch = BuildBatch();
        await pipeline.PersistAsync(batch);
        var contentHash = (await store.GetAsync(ChainId, batch.BatchNumber))!.ContentHash;
        await pipeline.ReconcileAsync();
        client.SetStatus(ChainId, batch.BatchNumber, BatchStatus.Unknown);
        client.SetTransactionStatus(
            (await store.GetProofAsync(contentHash))!.L1TransactionHash!,
            SettlementTransactionStatus.Unknown);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await pipeline.ReconcileAsync());

        Assert.AreEqual(1, client.SubmitCount);
        Assert.AreEqual(ProofSubmissionState.Submitted,
            (await store.GetProofAsync(contentHash))!.SubmissionState);
    }

    [TestMethod]
    public async Task Restart_Observed_PerformsNoSettlementCalls()
    {
        using var backend = new InMemoryKeyValueStore();
        var writer = new ProductionDaWriter();
        var prover = new RecordingZkProver(SafeSemantic);
        var client = new RecordingSettlementClient();

        using (var store = new KeyValueProofWitnessStore(backend))
        using (var pipeline = CreateZkPipeline(
            store, new TestExecutor(SafeSemantic), writer, prover, client))
        {
            await pipeline.PersistAsync(BuildBatch());
            await pipeline.ReconcileAsync();
            await pipeline.ReconcileAsync();
        }
        var submitCount = client.SubmitCount;
        var statusReads = client.BatchStatusReadCount;

        using var restartedStore = new KeyValueProofWitnessStore(backend);
        using var restarted = CreateZkPipeline(
            restartedStore, new TestExecutor(SafeSemantic), writer, prover, client);
        await restarted.ReconcileAsync();

        Assert.AreEqual(submitCount, client.SubmitCount);
        Assert.AreEqual(statusReads, client.BatchStatusReadCount);
    }

    [TestMethod]
    public async Task Restart_FinalizedBeforeConsume_ConsumesPersistedProofs()
    {
        using var backend = new InMemoryKeyValueStore();
        var writer = new ProductionDaWriter();
        var prover = new RecordingZkProver(SafeSemantic);
        var client = new RecordingSettlementClient();
        var finalizer = new RecordingForcedInclusionFinalizer();
        UInt256 contentHash;

        using (var store = new KeyValueProofWitnessStore(backend))
        using (var pipeline = CreateZkPipeline(
            store, new TestExecutor(SafeSemantic), writer, prover, client, finalizer))
        {
            var batch = BuildForcedBatch();
            await pipeline.PersistAsync(batch);
            contentHash = (await store.GetAsync(ChainId, batch.BatchNumber))!.ContentHash;
            await pipeline.ReconcileAsync();
            client.SetStatus(ChainId, batch.BatchNumber, BatchStatus.Finalized);
        }

        using var restartedStore = new KeyValueProofWitnessStore(backend);
        using var restarted = CreateZkPipeline(
            restartedStore,
            new TestExecutor(SafeSemantic),
            writer,
            prover,
            client,
            finalizer);
        await restarted.ReconcileAsync();

        Assert.AreEqual(1, finalizer.ConsumeCount);
        Assert.IsTrue((await restartedStore.GetProofAsync(contentHash))!.ForcedInclusionFinalized);
    }

    [TestMethod]
    public async Task Restart_ConsumeBeforeLocalMark_RetriesIdempotentFinalizer()
    {
        using var backend = new InMemoryKeyValueStore();
        var writer = new ProductionDaWriter();
        var prover = new RecordingZkProver(SafeSemantic);
        var client = new RecordingSettlementClient();
        var finalizer = new RecordingForcedInclusionFinalizer();
        UInt256 contentHash;

        using (var innerStore = new KeyValueProofWitnessStore(backend))
        using (var failingStore = new FailForcedFinalizationMarkStore(innerStore))
        using (var pipeline = CreateZkPipeline(
            failingStore,
            new TestExecutor(SafeSemantic),
            writer,
            prover,
            client,
            finalizer))
        {
            var batch = BuildForcedBatch();
            await pipeline.PersistAsync(batch);
            contentHash = (await innerStore.GetAsync(ChainId, batch.BatchNumber))!.ContentHash;
            await pipeline.ReconcileAsync();
            client.SetStatus(ChainId, batch.BatchNumber, BatchStatus.Finalized);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await pipeline.ReconcileAsync());
            Assert.AreEqual(1, finalizer.ConsumeCount);
            Assert.IsFalse((await innerStore.GetProofAsync(contentHash))!.ForcedInclusionFinalized);
        }

        using var restartedStore = new KeyValueProofWitnessStore(backend);
        using var restarted = CreateZkPipeline(
            restartedStore,
            new TestExecutor(SafeSemantic),
            writer,
            prover,
            client,
            finalizer);
        await restarted.ReconcileAsync();

        Assert.AreEqual(2, finalizer.ConsumeCount);
        Assert.IsTrue((await restartedStore.GetProofAsync(contentHash))!.ForcedInclusionFinalized);
    }

    [TestMethod]
    public async Task Restart_Reverted_RetainsProofAndNeverResubmits()
    {
        using var backend = new InMemoryKeyValueStore();
        var writer = new ProductionDaWriter();
        var prover = new RecordingZkProver(SafeSemantic);
        var client = new RecordingSettlementClient();
        UInt256 contentHash;

        using (var store = new KeyValueProofWitnessStore(backend))
        using (var pipeline = CreateZkPipeline(
            store, new TestExecutor(SafeSemantic), writer, prover, client))
        {
            var batch = BuildBatch();
            await pipeline.PersistAsync(batch);
            contentHash = (await store.GetAsync(ChainId, batch.BatchNumber))!.ContentHash;
            client.SetStatus(ChainId, batch.BatchNumber, BatchStatus.Reverted);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await pipeline.ReconcileAsync());
        }

        using var restartedStore = new KeyValueProofWitnessStore(backend);
        using var restarted = CreateZkPipeline(
            restartedStore, new TestExecutor(SafeSemantic), writer, prover, client);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await restarted.ReconcileAsync());

        Assert.AreEqual(0, client.SubmitCount);
        Assert.AreEqual(ProofSubmissionState.ProofReady,
            (await restartedStore.GetProofAsync(contentHash))!.SubmissionState);
        Assert.AreEqual(1, await restarted.GetPendingCountAsync());
    }

    [TestMethod]
    public async Task LegacyMultisigProfile_RemainsExplicitlyCompatible()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var client = new RecordingSettlementClient();
        using var pipeline = new CanonicalSettlementPipeline(
            new TestExecutor(ExecutionSemanticIds.ReferenceNoOpV1, authenticated: false),
            new ProductionDaWriter(),
            store,
            new LegacyProver(),
            client,
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig));

        await pipeline.PersistAsync(BuildBatch());
        await pipeline.ReconcileAsync();

        Assert.AreEqual(1, client.SubmitCount);
        Assert.AreEqual(ProofType.Multisig, client.LastCommitment!.ProofType);
    }

    private static CanonicalSettlementPipeline CreateZkPipeline(
        IProofWitnessStore store,
        IProofWitnessBatchExecutor executor,
        IDAWriter writer,
        IL2Prover prover,
        ISettlementClient client,
        IForcedInclusionFinalizationClient? forcedInclusionFinalizer = null) => new(
            executor,
            writer,
            store,
            prover,
            client,
            ZkProfile(),
            forcedInclusionFinalizer: forcedInclusionFinalizer);

    private static ProofWitnessPipelineProfile ZkProfile() =>
        ProofWitnessPipelineProfile.Zk(
            ChainId,
            WitnessProofSystem.Sp1,
            VerificationKeyId);

    private static SealedBatch BuildBatch(ulong batchNumber = 1)
    {
        var message = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = ChainId,
            Nonce = 7,
            Sender = new UInt160(Enumerable.Repeat((byte)0x11, 20).ToArray()),
            Receiver = new UInt160(Enumerable.Repeat((byte)0x22, 20).ToArray()),
            MessageType = MessageType.Deposit,
            Payload = new byte[] { 0x31, 0x32 },
            MessageHash = UInt256.Zero,
        };
        message = message with { MessageHash = MessageHasher.HashMessage(message) };
        return new SealedBatch(
            ChainId,
            batchNumber,
            firstBlock: batchNumber * 10,
            lastBlock: batchNumber * 10,
            preStateRoot: H(1),
            transactions: new ReadOnlyMemory<byte>[]
            {
                new byte[] { 1, 2, 3, checked((byte)batchNumber) },
            },
            l1Messages: new[] { message },
            blockContext: new BatchBlockContext
            {
                L1FinalizedHeight = 900,
                FirstBlockTimestamp = 1_700_000_000,
                LastBlockTimestamp = 1_700_000_001,
                SequencerCommitteeHash = H(2),
                Network = 860833102,
            });
    }

    private static SealedBatch BuildForcedBatch()
    {
        var forcedTransaction = new byte[] { 0xF1, 0xF2, 0xF3 };
        var builder = new BatchBuilder(ChainId, 1, 10, H(1))
            .AddBlock(10)
            .AddForcedTransaction(
                44,
                new UInt256(Crypto.Hash256(forcedTransaction)),
                forcedTransaction)
            .AddTransaction(new byte[] { 1, 2, 3, 4 })
            .WithBlockContext(new BatchBlockContext
            {
                L1FinalizedHeight = 900,
                FirstBlockTimestamp = 1_700_000_000,
                LastBlockTimestamp = 1_700_000_001,
                SequencerCommitteeHash = H(2),
                Network = 860833102,
            });
        return builder.SealArtifact();
    }

    private static UInt256 H(byte value)
    {
        var bytes = new byte[32];
        bytes[0] = value;
        return new UInt256(bytes);
    }

    private sealed class TestExecutor : IProofWitnessBatchExecutor
    {
        private readonly UInt256 _semantic;
        private readonly bool _authenticated;
        private readonly bool _returnEmptyStateWitness;

        public TestExecutor(
            UInt256 semantic,
            bool authenticated = true,
            bool returnEmptyStateWitness = false)
        {
            _semantic = semantic;
            _authenticated = authenticated;
            _returnEmptyStateWitness = returnEmptyStateWitness;
        }

        public ValueTask<BatchExecutionResult> ApplyBatchAsync(
            BatchExecutionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException(
                "canonical settlement must use ApplyBatchWithWitnessAsync");

        public ValueTask<ProofWitnessExecutionResult> ApplyBatchWithWitnessAsync(
            SealedBatch batch,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var txHashes = new UInt256[batch.Transactions.Count];
            var receiptHashes = new UInt256[batch.Transactions.Count];
            long gasConsumed = 0;
            for (var index = 0; index < batch.Transactions.Count; index++)
            {
                var txHash = new UInt256(Crypto.Hash256(batch.Transactions[index].Span));
                var receipt = new Receipt
                {
                    TxHash = txHash,
                    Success = true,
                    GasConsumed = 17 + index,
                    StorageDeltaHash = H(checked((byte)(0x41 + index))),
                    EventsHash = H(checked((byte)(0x51 + index))),
                };
                txHashes[index] = txHash;
                receiptHashes[index] = receipt.Hash();
                gasConsumed += receipt.GasConsumed;
            }
            var executionResult = new BatchExecutionResult
            {
                PostStateRoot = H(3),
                TxRoot = StateRootCalculator.ComputeTxRoot(txHashes),
                ReceiptRoot = StateRootCalculator.ComputeReceiptRoot(receiptHashes),
                WithdrawalRoot = UInt256.Zero,
                L2ToL1MessageRoot = UInt256.Zero,
                L2ToL2MessageRoot = UInt256.Zero,
                GasConsumed = gasConsumed,
            };
            var stateWitness = _returnEmptyStateWitness
                ? ReadOnlyMemory<byte>.Empty
                : new byte[] { 0x4e, 0x45, 0x4f, 0x34, 0x53, 0x54, 0x57, 0x31 };
            return ValueTask.FromResult(new ProofWitnessExecutionResult
            {
                ExecutionResult = executionResult,
                ExecutionSemanticId = _semantic,
                WitnessAuthenticated = _authenticated,
                StateWitness = stateWitness,
                Effects = new byte[] { 0x4e, 0x45, 0x4f, 0x34, 0x45, 0x46, 0x58, 0x31 },
            });
        }
    }

    private sealed class ProductionDaWriter : IProductionDAWriter
    {
        public DAMode Mode => DAMode.External;
        public DAReceiptKind ReceiptKind => DAReceiptKind.ExternalPublication;
        public bool CorruptCommitment { get; init; }
        public bool Available { get; set; } = true;

        public ValueTask<DAReceipt> PublishAsync(
            DAPublishRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var commitment = ExecutionPayloadSerializer.ComputeCommitment(request.Payload.Span);
            if (CorruptCommitment) commitment = H(0xEE);
            return ValueTask.FromResult(new DAReceipt
            {
                Commitment = commitment,
                Pointer = new byte[] { 0x70, 0x74, 0x72 },
                Evidence = new byte[] { 0x65, 0x76, 0x69, 0x64 },
                Kind = ReceiptKind,
                Layer = Mode,
            });
        }

        public ValueTask<bool> IsAvailableAsync(
            DAReceipt receipt,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Available);
    }

    private sealed class RecordingZkProver : IZkExecutionProver
    {
        public RecordingZkProver(UInt256 executionSemanticId)
        {
            ExecutionSemanticId = executionSemanticId;
        }

        public ProofType Kind => ProofType.Zk;
        public UInt256 ExecutionSemanticId { get; }
        public WitnessProofSystem WitnessProofSystem => WitnessProofSystem.Sp1;
        public UInt256 VerificationKeyId => UT_CanonicalSettlementPipeline.VerificationKeyId;
        public bool ProducesCryptographicProof { get; set; } = true;
        public IProofWitnessStore? Store { get; init; }
        public bool FailNextProof { get; set; }
        public int ProveCount { get; private set; }
        public ReadOnlyMemory<byte> LastWitness { get; private set; }

        public async ValueTask<ProofResult> ProveAsync(
            ProofRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProveCount++;
            LastWitness = request.Witness.ToArray();
            if (FailNextProof)
            {
                FailNextProof = false;
                throw new InvalidOperationException("prover unavailable");
            }
            Assert.IsFalse(request.Witness.IsEmpty);
            var artifact = ProofWitnessArtifactSerializer.Decode(request.Witness.Span);
            if (Store is not null)
            {
                var persisted = await Store.GetAsync(artifact.ChainId, artifact.BatchNumber, cancellationToken);
                Assert.IsNotNull(persisted, "artifact must be committed before proof generation");
                Assert.AreEqual(artifact.ContentHash, persisted.ContentHash);
            }
            return new ProofResult
            {
                Proof = new byte[] { 0x91, 0x92, 0x93 },
                Kind = Kind,
                PublicInputHash = StateRootCalculator.HashPublicInputs(request.PublicInputs),
            };
        }
    }

    private sealed class LegacyProver : IL2Prover
    {
        public ProofType Kind => ProofType.Multisig;

        public ValueTask<ProofResult> ProveAsync(
            ProofRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ProofResult
            {
                Proof = new byte[] { 0x81 },
                Kind = Kind,
                PublicInputHash = StateRootCalculator.HashPublicInputs(request.PublicInputs),
            });
    }

    private sealed class RecordingSettlementClient
        : ISettlementClient, ISettlementTransactionStatusClient
    {
        private readonly Dictionary<(uint ChainId, ulong BatchNumber), BatchStatus> _statuses = new();
        private readonly Dictionary<UInt256, SettlementTransactionStatus> _transactionStatuses = new();

        public int SubmitCount { get; private set; }
        public int BatchStatusReadCount { get; private set; }
        public int TransactionStatusReadCount { get; private set; }
        public bool FailNextSubmitBeforeRecord { get; set; }
        public bool FailNextSubmitAfterRecord { get; set; }
        public L2BatchCommitment? LastCommitment { get; private set; }
        public List<ulong> SubmittedBatchNumbers { get; } = new();

        public void SetStatus(uint chainId, ulong batchNumber, BatchStatus status)
            => _statuses[(chainId, batchNumber)] = status;

        public void SetTransactionStatus(
            UInt256 transactionHash,
            SettlementTransactionStatus status)
            => _transactionStatuses[transactionHash] = status;

        public ValueTask<UInt256> SubmitBatchAsync(
            L2BatchCommitment commitment,
            PublicInputs publicInputs,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SubmitCount++;
            SubmittedBatchNumbers.Add(commitment.BatchNumber);
            LastCommitment = commitment;
            if (FailNextSubmitBeforeRecord)
            {
                FailNextSubmitBeforeRecord = false;
                throw new InvalidOperationException("submit failed before L1 acceptance");
            }
            var transactionHash = H(checked((byte)(0xA0 + SubmitCount)));
            _statuses[(commitment.ChainId, commitment.BatchNumber)] = BatchStatus.Pending;
            _transactionStatuses[transactionHash] = SettlementTransactionStatus.Pending;
            if (FailNextSubmitAfterRecord)
            {
                FailNextSubmitAfterRecord = false;
                throw new InvalidOperationException("process stopped after L1 acceptance");
            }
            return ValueTask.FromResult(transactionHash);
        }

        public ValueTask<UInt256> GetCanonicalStateRootAsync(
            uint chainId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(UInt256.Zero);

        public ValueTask<BatchStatus> GetBatchStatusAsync(
            uint chainId,
            ulong batchNumber,
            CancellationToken cancellationToken = default)
        {
            BatchStatusReadCount++;
            return ValueTask.FromResult(
                _statuses.GetValueOrDefault((chainId, batchNumber), BatchStatus.Unknown));
        }

        public ValueTask<SettlementTransactionStatus> GetTransactionStatusAsync(
            UInt256 transactionHash,
            CancellationToken cancellationToken = default)
        {
            TransactionStatusReadCount++;
            return ValueTask.FromResult(
                _transactionStatuses.GetValueOrDefault(
                    transactionHash, SettlementTransactionStatus.Unknown));
        }
    }

    private sealed class FailForcedFinalizationMarkStore(IProofWitnessStore inner)
        : IProofWitnessStore
    {
        private bool _failNext = true;

        public ValueTask CommitAsync(
            ProofWitnessArtifactV1 artifact,
            CancellationToken cancellationToken = default)
            => inner.CommitAsync(artifact, cancellationToken);

        public ValueTask<ProofWitnessArtifactV1?> GetAsync(
            uint chainId,
            ulong batchNumber,
            CancellationToken cancellationToken = default)
            => inner.GetAsync(chainId, batchNumber, cancellationToken);

        public IAsyncEnumerable<ProofWitnessArtifactV1> EnumerateCommittedAsync(
            uint chainId,
            CancellationToken cancellationToken = default)
            => inner.EnumerateCommittedAsync(chainId, cancellationToken);

        public ValueTask PutProofAsync(
            ProofResultManifest manifest,
            CancellationToken cancellationToken = default)
            => inner.PutProofAsync(manifest, cancellationToken);

        public ValueTask<ProofResultManifest?> GetProofAsync(
            UInt256 artifactContentHash,
            CancellationToken cancellationToken = default)
            => inner.GetProofAsync(artifactContentHash, cancellationToken);

        public ValueTask MarkSubmittedAsync(
            UInt256 artifactContentHash,
            UInt256 l1TransactionHash,
            CancellationToken cancellationToken = default)
            => inner.MarkSubmittedAsync(
                artifactContentHash, l1TransactionHash, cancellationToken);

        public ValueTask ReplaceSubmittedTransactionAsync(
            UInt256 artifactContentHash,
            UInt256 expectedTransactionHash,
            UInt256 replacementTransactionHash,
            CancellationToken cancellationToken = default)
            => inner.ReplaceSubmittedTransactionAsync(
                artifactContentHash,
                expectedTransactionHash,
                replacementTransactionHash,
                cancellationToken);

        public ValueTask MarkSubmissionObservedAsync(
            UInt256 artifactContentHash,
            CancellationToken cancellationToken = default)
            => inner.MarkSubmissionObservedAsync(artifactContentHash, cancellationToken);

        public ValueTask MarkForcedInclusionFinalizedAsync(
            UInt256 artifactContentHash,
            CancellationToken cancellationToken = default)
        {
            if (_failNext)
            {
                _failNext = false;
                throw new InvalidOperationException(
                    "process stopped before forced finalization state was persisted");
            }
            return inner.MarkForcedInclusionFinalizedAsync(
                artifactContentHash, cancellationToken);
        }

        public ValueTask<IReadOnlyCollection<ulong>> GetTrackedForcedInclusionNoncesAsync(
            uint chainId,
            CancellationToken cancellationToken = default)
            => inner.GetTrackedForcedInclusionNoncesAsync(chainId, cancellationToken);

        public void Dispose()
        {
        }
    }

    private sealed class BlockingSettlementClient : ISettlementClient
    {
        public TaskCompletionSource SubmitEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowSubmit { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SubmitCount { get; private set; }

        public async ValueTask<UInt256> SubmitBatchAsync(
            L2BatchCommitment commitment,
            PublicInputs publicInputs,
            CancellationToken cancellationToken = default)
        {
            SubmitCount++;
            SubmitEntered.SetResult();
            await AllowSubmit.Task.WaitAsync(cancellationToken);
            return H(0xA2);
        }

        public ValueTask<UInt256> GetCanonicalStateRootAsync(
            uint chainId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(UInt256.Zero);

        public ValueTask<BatchStatus> GetBatchStatusAsync(
            uint chainId,
            ulong batchNumber,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(BatchStatus.Unknown);
    }

    private sealed class RecordingForcedInclusionFinalizer
        : IForcedInclusionFinalizationClient
    {
        public bool FailNextConsume { get; set; }
        public int ConsumeCount { get; private set; }
        public IReadOnlyList<ForcedInclusionConsumptionProof> LastProofs { get; private set; }
            = Array.Empty<ForcedInclusionConsumptionProof>();

        public ValueTask ConsumeAndConfirmAsync(
            uint chainId,
            ulong batchNumber,
            IReadOnlyList<ForcedInclusionConsumptionProof> proofs,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConsumeCount++;
            LastProofs = proofs.ToArray();
            if (FailNextConsume)
            {
                FailNextConsume = false;
                throw new InvalidOperationException("forced consume confirmation unavailable");
            }
            return ValueTask.CompletedTask;
        }
    }
}
