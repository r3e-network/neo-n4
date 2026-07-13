using System.Buffers.Binary;
using Neo.Cryptography;
using Neo.L2.Batch;
using Neo.L2.State;

namespace Neo.L2.Persistence.UnitTests;

[TestClass]
public class UT_ProofWitnessStore
{
    [TestMethod]
    public async Task CommitGet_IsIdempotentAndUsesCanonicalKeyLayout()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var artifact = SampleArtifact();

        await store.CommitAsync(artifact);
        await store.CommitAsync(artifact);

        Assert.AreEqual(artifact, await store.GetAsync(artifact.ChainId, artifact.BatchNumber));
        Assert.AreEqual(1L, backend.Count);
        var (key, value) = backend.EnumeratePrefix("PWIT"u8).Single();
        CollectionAssert.AreEqual("PWIT"u8.ToArray(), key[..4]);
        Assert.AreEqual(artifact.ChainId, BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(4, 4)));
        Assert.AreEqual(artifact.BatchNumber, BinaryPrimitives.ReadUInt64LittleEndian(key.AsSpan(8, 8)));
        Assert.AreEqual(artifact, ProofWitnessArtifactSerializer.Decode(value));
    }

    [TestMethod]
    public async Task Commit_ConflictingContentFailsClosed()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var original = SampleArtifact();
        var conflicting = Rebuild(original, new byte[] { 0xde, 0xad });

        await store.CommitAsync(original);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.CommitAsync(conflicting).AsTask());
        Assert.AreEqual(original, await store.GetAsync(original.ChainId, original.BatchNumber));
    }

    [TestMethod]
    public async Task Commit_ConcurrentConflictPersistsExactlyOneArtifact()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var first = SampleArtifact();
        var second = Rebuild(first, new byte[] { 0xfa, 0xce });
        var outcomes = await Task.WhenAll(
            TryCommitAsync(store, first),
            TryCommitAsync(store, second));

        Assert.AreEqual(1, outcomes.Count(static result => result));
        Assert.AreEqual(1, outcomes.Count(static result => !result));
        var persisted = await store.GetAsync(first.ChainId, first.BatchNumber);
        Assert.IsNotNull(persisted);
        Assert.IsTrue(
            persisted.ContentHash.Equals(first.ContentHash)
            || persisted.ContentHash.Equals(second.ContentHash));
    }

    [TestMethod]
    public async Task EnumerateCommitted_SortsLittleEndianKeysByBatchNumber()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        await store.CommitAsync(SampleArtifact(256));
        await store.CommitAsync(SampleArtifact(2));
        await store.CommitAsync(SampleArtifact(1));

        var batches = new List<ulong>();
        await foreach (var artifact in store.EnumerateCommittedAsync(SampleChainId))
            batches.Add(artifact.BatchNumber);
        CollectionAssert.AreEqual(new ulong[] { 1, 2, 256 }, batches);
    }

    [TestMethod]
    public async Task ProofManifest_IsBoundIdempotentAndSubmissionIsCompareExchanged()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var artifact = SampleArtifact();
        var manifest = SampleManifest(artifact);
        var l1TransactionHash = H(0x70);

        await store.CommitAsync(artifact);
        await store.PutProofAsync(manifest);
        await store.PutProofAsync(manifest);
        Assert.AreEqual(manifest, await store.GetProofAsync(artifact.ContentHash));

        await store.MarkSubmittedAsync(artifact.ContentHash, l1TransactionHash);
        await store.MarkSubmittedAsync(artifact.ContentHash, l1TransactionHash);
        var submitted = await store.GetProofAsync(artifact.ContentHash);
        Assert.IsNotNull(submitted);
        Assert.AreEqual(l1TransactionHash, submitted.L1TransactionHash);
        Assert.AreEqual(ProofSubmissionState.Submitted, submitted.SubmissionState);
        Assert.IsTrue(submitted.Submitted);
        Assert.IsFalse(submitted.SettlementObserved);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.MarkSubmittedAsync(artifact.ContentHash, H(0x71)).AsTask());
        var conflictingManifest = manifest with { Proof = new byte[] { 0xff } };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.PutProofAsync(conflictingManifest).AsTask());
    }

    [TestMethod]
    public async Task SubmittedTransaction_ReplacementRequiresExpectedHashAndUnobservedState()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var artifact = SampleArtifact();
        var first = H(0x72);
        var replacement = H(0x73);
        await store.CommitAsync(artifact);
        await store.PutProofAsync(SampleManifest(artifact));
        await store.MarkSubmittedAsync(artifact.ContentHash, first);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.ReplaceSubmittedTransactionAsync(
                artifact.ContentHash, H(0x74), replacement).AsTask());
        await store.ReplaceSubmittedTransactionAsync(
            artifact.ContentHash, first, replacement);
        Assert.AreEqual(
            replacement,
            (await store.GetProofAsync(artifact.ContentHash))!.L1TransactionHash);

        await store.MarkSubmissionObservedAsync(artifact.ContentHash);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.ReplaceSubmittedTransactionAsync(
                artifact.ContentHash, replacement, H(0x75)).AsTask());
    }

    [TestMethod]
    public async Task ProofManifest_RejectsUnknownArtifactAndBindingMismatch()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var artifact = SampleArtifact();
        var manifest = SampleManifest(artifact);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.PutProofAsync(manifest).AsTask());

        await store.CommitAsync(artifact);
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.PutProofAsync(manifest with { PublicInputHash = H(0x99) }).AsTask());
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.PutProofAsync(manifest with { ArtifactContentHash = H(0x97) }).AsTask());
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.PutProofAsync(manifest with { VerificationKeyId = H(0x98) }).AsTask());
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.PutProofAsync(manifest with { ProofSystem = WitnessProofSystem.Halo2 }).AsTask());
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.PutProofAsync(manifest with { ProofType = ProofType.Optimistic }).AsTask());
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.PutProofAsync(manifest with { ExecutionSemanticId = H(0x96) }).AsTask());
    }

    [TestMethod]
    public async Task MarkSubmissionObserved_RoundTripsWithoutInventingTransactionHash()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var artifact = SampleArtifact();
        await store.CommitAsync(artifact);
        await store.PutProofAsync(SampleManifest(artifact));

        await store.MarkSubmissionObservedAsync(artifact.ContentHash);
        await store.MarkSubmissionObservedAsync(artifact.ContentHash);

        var observed = await store.GetProofAsync(artifact.ContentHash);
        Assert.IsNotNull(observed);
        Assert.IsTrue(observed.SettlementObserved);
        Assert.IsNull(observed.L1TransactionHash);
    }

    [TestMethod]
    public async Task ForcedInclusionReservation_PersistsUntilConfirmedFinalization()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var artifact = SampleArtifact(forcedInclusion: true);
        var manifest = SampleManifest(artifact);
        await store.CommitAsync(artifact);

        CollectionAssert.AreEqual(
            new ulong[] { 42 },
            (await store.GetTrackedForcedInclusionNoncesAsync(artifact.ChainId)).ToArray());
        await store.PutProofAsync(manifest);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.MarkForcedInclusionFinalizedAsync(artifact.ContentHash).AsTask());
        await store.MarkSubmittedAsync(artifact.ContentHash, H(0x75));
        CollectionAssert.AreEqual(
            new ulong[] { 42 },
            (await store.GetTrackedForcedInclusionNoncesAsync(artifact.ChainId)).ToArray());

        await store.MarkSubmissionObservedAsync(artifact.ContentHash);
        await store.MarkForcedInclusionFinalizedAsync(artifact.ContentHash);
        await store.MarkForcedInclusionFinalizedAsync(artifact.ContentHash);

        CollectionAssert.AreEqual(
            new ulong[] { 42 },
            (await store.GetTrackedForcedInclusionNoncesAsync(artifact.ChainId)).ToArray());
        Assert.IsTrue((await store.GetProofAsync(artifact.ContentHash))!.ForcedInclusionFinalized);
    }

    [TestMethod]
    public async Task CorruptedArtifactFailsClosedOnRead()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        var artifact = SampleArtifact();
        await store.CommitAsync(artifact);
        var (key, value) = backend.EnumeratePrefix("PWIT"u8).Single();
        value[16] ^= 0x80;
        backend.Put(key, value);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => store.GetAsync(artifact.ChainId, artifact.BatchNumber).AsTask());
    }

    [TestMethod]
    public async Task SettlementRecovery_RestartPersistsPoisonResetAndClearWithoutArtifactLoss()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "neo-l2-settlement-recovery-" + Guid.NewGuid().ToString("N"));
        var artifact = SampleArtifact();
        try
        {
            using (var backend = new RocksDbKeyValueStore(directory))
            using (var store = new KeyValueProofWitnessStore(backend))
            {
                await store.CommitAsync(artifact);
                var first = await store.RecordSettlementFailureAsync(
                    artifact.ChainId,
                    artifact.BatchNumber,
                    artifact.ContentHash,
                    "first failure",
                    maxAutomaticRetries: 3,
                    failedAtUnixMilliseconds: 1000);
                var second = await store.RecordSettlementFailureAsync(
                    artifact.ChainId,
                    artifact.BatchNumber,
                    artifact.ContentHash,
                    "second failure",
                    maxAutomaticRetries: 3,
                    failedAtUnixMilliseconds: 2000);
                Assert.AreEqual(SettlementRecoveryState.Retrying, first.State);
                Assert.AreEqual(2, second.RetryCount);
            }

            using (var backend = new RocksDbKeyValueStore(directory))
            using (var store = new KeyValueProofWitnessStore(backend))
            {
                var recovered = await store.GetSettlementRecoveryAsync(artifact.ContentHash);
                Assert.IsNotNull(recovered);
                Assert.AreEqual(2, recovered.RetryCount);
                var poisoned = await store.RecordSettlementFailureAsync(
                    artifact.ChainId,
                    artifact.BatchNumber,
                    artifact.ContentHash,
                    "permanent failure",
                    maxAutomaticRetries: 3,
                    failedAtUnixMilliseconds: 3000);
                Assert.AreEqual(SettlementRecoveryState.Poisoned, poisoned.State);
                Assert.AreEqual(3, poisoned.RetryCount);
                Assert.AreEqual("permanent failure", poisoned.LastError);
            }

            using (var backend = new RocksDbKeyValueStore(directory))
            using (var store = new KeyValueProofWitnessStore(backend))
            {
                var poisoned = await store.GetSettlementRecoveryAsync(artifact.ContentHash);
                Assert.IsNotNull(poisoned);
                Assert.AreEqual(SettlementRecoveryState.Poisoned, poisoned.State);
                Assert.AreEqual(artifact, await store.GetAsync(
                    artifact.ChainId, artifact.BatchNumber));

                await store.ResetSettlementRecoveryAsync(
                    artifact.ChainId, artifact.BatchNumber, artifact.ContentHash);
                var reset = await store.GetSettlementRecoveryAsync(artifact.ContentHash);
                Assert.IsNotNull(reset);
                Assert.AreEqual(SettlementRecoveryState.Retrying, reset.State);
                Assert.AreEqual(0, reset.RetryCount);
                Assert.IsNull(reset.LastError);

                await store.ClearSettlementRecoveryAsync(
                    artifact.ChainId, artifact.BatchNumber, artifact.ContentHash);
                Assert.IsNull(await store.GetSettlementRecoveryAsync(artifact.ContentHash));
                Assert.AreEqual(artifact, await store.GetAsync(
                    artifact.ChainId, artifact.BatchNumber));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task RocksDb_ReopenRecoversArtifactProofAndSubmission()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "neo-l2-proof-witness-" + Guid.NewGuid().ToString("N"));
        var artifact = SampleArtifact();
        var manifest = SampleManifest(artifact);
        var l1TransactionHash = H(0x77);
        try
        {
            using (var backend = new RocksDbKeyValueStore(directory))
            using (var store = new KeyValueProofWitnessStore(backend))
            {
                await store.CommitAsync(artifact);
                await store.PutProofAsync(manifest);
                await store.MarkSubmittedAsync(artifact.ContentHash, l1TransactionHash);
            }

            using (var backend = new RocksDbKeyValueStore(directory))
            using (var store = new KeyValueProofWitnessStore(backend))
            {
                Assert.AreEqual(
                    artifact,
                    await store.GetAsync(artifact.ChainId, artifact.BatchNumber));
                var recoveredProof = await store.GetProofAsync(artifact.ContentHash);
                Assert.IsNotNull(recoveredProof);
                Assert.AreEqual(l1TransactionHash, recoveredProof.L1TransactionHash);
                Assert.AreEqual(ProofSubmissionState.Submitted, recoveredProof.SubmissionState);
                Assert.IsFalse(recoveredProof.SettlementObserved);
                var recovered = new List<ProofWitnessArtifactV1>();
                await foreach (var item in store.EnumerateCommittedAsync(artifact.ChainId))
                    recovered.Add(item);
                Assert.AreEqual(1, recovered.Count);
                Assert.AreEqual(artifact, recovered[0]);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void ProofResultManifest_RoundTripsAndRejectsMalformedInput()
    {
        var artifact = SampleArtifact();
        var manifest = SampleManifest(artifact);
        var canonical = ProofResultManifestSerializer.Encode(manifest);
        Assert.AreEqual(manifest, ProofResultManifestSerializer.Decode(canonical));

        for (var index = 0; index < canonical.Length; index++)
        {
            var tampered = canonical.ToArray();
            tampered[index] ^= 0x01;
            Assert.ThrowsExactly<InvalidDataException>(
                () => ProofResultManifestSerializer.Decode(tampered),
                $"tamper at byte {index} must be rejected");
        }

        var magic = canonical.ToArray();
        magic[0] = 0;
        Assert.ThrowsExactly<InvalidDataException>(() => ProofResultManifestSerializer.Decode(magic));

        var version = canonical.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(version.AsSpan(8, 2), 3);
        Assert.ThrowsExactly<InvalidDataException>(() => ProofResultManifestSerializer.Decode(version));

        var flags = canonical.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(flags.AsSpan(10, 2), 0x8000);
        Assert.ThrowsExactly<InvalidDataException>(() => ProofResultManifestSerializer.Decode(flags));

        var proofSystem = canonical.ToArray();
        proofSystem[13] = 0xff;
        Assert.ThrowsExactly<InvalidDataException>(
            () => ProofResultManifestSerializer.Decode(proofSystem));

        var reserved = canonical.ToArray();
        reserved[14] = 1;
        Assert.ThrowsExactly<InvalidDataException>(() => ProofResultManifestSerializer.Decode(reserved));

        Assert.ThrowsExactly<InvalidDataException>(
            () => ProofResultManifestSerializer.Decode([.. canonical, 0x00]));

        var badLength = canonical.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(badLength.AsSpan(156, 4), uint.MaxValue);
        Assert.ThrowsExactly<InvalidDataException>(() => ProofResultManifestSerializer.Decode(badLength));
    }

    [TestMethod]
    public void ProofResultManifest_V1BroadcastStateMigratesToSubmittedNotObserved()
    {
        var artifact = SampleArtifact();
        var transactionHash = H(0x79);
        var submitted = SampleManifest(artifact) with
        {
            SubmissionState = ProofSubmissionState.Submitted,
            L1TransactionHash = transactionHash,
        };
        var legacy = ProofResultManifestSerializer.Encode(submitted);
        BinaryPrimitives.WriteUInt16LittleEndian(legacy.AsSpan(8, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(legacy.AsSpan(10, 2), 3);
        var body = legacy.AsSpan(0, legacy.Length - UInt256.Length);
        var bound = new byte["neo-n4/proof-result/v1\0"u8.Length + body.Length];
        "neo-n4/proof-result/v1\0"u8.CopyTo(bound);
        body.CopyTo(bound.AsSpan("neo-n4/proof-result/v1\0"u8.Length));
        Crypto.Hash256(bound).CopyTo(legacy.AsSpan(body.Length));

        var migrated = ProofResultManifestSerializer.Decode(legacy);

        Assert.AreEqual(ProofSubmissionState.Submitted, migrated.SubmissionState);
        Assert.AreEqual(transactionHash, migrated.L1TransactionHash);
        Assert.IsFalse(migrated.SettlementObserved);
    }

    private const uint SampleChainId = 0x1122_3344;

    private static async Task<bool> TryCommitAsync(
        IProofWitnessStore store,
        ProofWitnessArtifactV1 artifact)
    {
        try
        {
            await store.CommitAsync(artifact);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static ProofWitnessArtifactV1 Rebuild(
        ProofWitnessArtifactV1 artifact,
        ReadOnlyMemory<byte> stateWitness)
        => ProofWitnessArtifactV1.Create(
            artifact.ProofType,
            artifact.ProofSystem,
            artifact.VerificationKeyId,
            artifact.ExecutionSemanticId,
            artifact.ExecutionWitnessAuthenticated,
            artifact.ChainId,
            artifact.BatchNumber,
            artifact.FirstBlock,
            artifact.LastBlock,
            artifact.ExecutionPayload,
            stateWitness,
            artifact.ExecutionResult,
            artifact.Effects,
            artifact.DAReceipt,
            artifact.PublicInputs);

    private static ProofResultManifest SampleManifest(ProofWitnessArtifactV1 artifact)
        => new()
        {
            ProofType = artifact.ProofType,
            ChainId = artifact.ChainId,
            BatchNumber = artifact.BatchNumber,
            ArtifactContentHash = artifact.ContentHash,
            PublicInputHash = new UInt256(
                Crypto.Hash256(BatchSerializer.EncodePublicInputs(artifact.PublicInputs))),
            VerificationKeyId = artifact.VerificationKeyId,
            ProofSystem = artifact.ProofSystem,
            ExecutionSemanticId = artifact.ExecutionSemanticId,
            Proof = new byte[] { 0x01, 0x02, 0x03 },
            PublicValues = new byte[] { 0x04, 0x05 },
            SubmissionState = ProofSubmissionState.ProofReady,
        };

    private static ProofWitnessArtifactV1 SampleArtifact(
        ulong batchNumber = 257,
        bool forcedInclusion = false)
    {
        ReadOnlyMemory<byte> transaction = new byte[] { 0x01, 0x02, 0x03 };
        var transactionHash = new UInt256(Crypto.Hash256(transaction.Span));
        var payload = new ExecutionPayloadV1
        {
            ChainId = SampleChainId,
            BatchNumber = batchNumber,
            FirstBlock = 10,
            LastBlock = 11,
            PreStateRoot = H(0x10),
            BlockContext = new BatchBlockContext
            {
                L1FinalizedHeight = 99,
                FirstBlockTimestamp = 1_700_000_000,
                LastBlockTimestamp = 1_700_000_001,
                SequencerCommitteeHash = H(0x20),
                Network = 860_833_102,
            },
            L1Messages = [],
            ForcedInclusions = forcedInclusion
                ? new ForcedInclusionConsumptionProof[]
                {
                    new()
                    {
                        Nonce = 42,
                        LeafIndex = 0,
                        TxHash = transactionHash,
                        Siblings = Array.Empty<UInt256>(),
                    },
                }
                : Array.Empty<ForcedInclusionConsumptionProof>(),
            Transactions = [transaction],
        };
        var result = new BatchExecutionResult
        {
            PostStateRoot = H(0x31),
            TxRoot = H(0x32),
            ReceiptRoot = H(0x33),
            WithdrawalRoot = H(0x34),
            L2ToL1MessageRoot = H(0x35),
            L2ToL2MessageRoot = H(0x36),
            GasConsumed = 1_234,
        };
        var daReceipt = new DAReceipt
        {
            Layer = DAMode.NeoFS,
            Commitment = ExecutionPayloadSerializer.ComputeCommitment(payload),
            Pointer = new byte[] { 0xa1 },
            Kind = DAReceiptKind.NeoFSObject,
            Evidence = new byte[] { 0xa2 },
        };
        var inputs = new PublicInputs
        {
            ChainId = payload.ChainId,
            BatchNumber = payload.BatchNumber,
            PreStateRoot = payload.PreStateRoot,
            PostStateRoot = result.PostStateRoot,
            TxRoot = result.TxRoot,
            ReceiptRoot = result.ReceiptRoot,
            WithdrawalRoot = result.WithdrawalRoot,
            L2ToL1MessageRoot = result.L2ToL1MessageRoot,
            L2ToL2MessageRoot = result.L2ToL2MessageRoot,
            L1MessageHash = UInt256.Zero,
            DACommitment = daReceipt.Commitment,
            BlockContextHash = StateRootCalculator.HashBlockContext(payload.BlockContext),
        };
        return ProofWitnessArtifactV1.Create(
            ProofType.Zk,
            WitnessProofSystem.Sp1,
            H(0x41),
            H(0x42),
            true,
            payload.ChainId,
            payload.BatchNumber,
            payload.FirstBlock,
            payload.LastBlock,
            payload,
            new byte[] { 0xb1, 0xb2 },
            result,
            new byte[] { 0xc1 },
            daReceipt,
            inputs);
    }

    private static UInt256 H(byte value)
        => new(Enumerable.Repeat(value, UInt256.Length).ToArray());
}
