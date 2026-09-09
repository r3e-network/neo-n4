using Neo.Cryptography;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Persistence;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>
/// L1 broadcast and finalize helpers for <see cref="CanonicalSettlementPipeline"/>.
/// </summary>
/// <remarks>See doc.md §7.5, §15.1, and §17.</remarks>
public sealed partial class CanonicalSettlementPipeline
{
    private async ValueTask BroadcastAndPersistAsync(
        ProofWitnessArtifactV1 artifact,
        ProofResultManifest manifest,
        UInt256? replacedTransactionHash,
        CancellationToken cancellationToken)
    {
        var commitment = BuildCommitment(artifact, manifest);
        var submitStarted = System.Diagnostics.Stopwatch.StartNew();
        var useAtomicFinalize = artifact.ProofType is ProofType.Zk or ProofType.Multisig;
        var transactionHash = useAtomicFinalize
            ? await _client.SubmitAndFinalizeBatchAsync(
                commitment,
                WithForcedCount(artifact.PublicInputs, artifact.ExecutionPayload.ForcedInclusions.Count),
                cancellationToken).ConfigureAwait(false)
            : await _client.SubmitBatchAsync(
                commitment,
                WithForcedCount(artifact.PublicInputs, artifact.ExecutionPayload.ForcedInclusions.Count),
                cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(transactionHash);
        if (transactionHash.Equals(UInt256.Zero))
            throw new InvalidOperationException(
                "ISettlementClient returned a zero transaction hash");

        if (replacedTransactionHash is null)
        {
            await _store.MarkSubmittedAsync(
                artifact.ContentHash,
                transactionHash,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _store.ReplaceSubmittedTransactionAsync(
                artifact.ContentHash,
                replacedTransactionHash,
                transactionHash,
                cancellationToken).ConfigureAwait(false);
        }

        submitStarted.Stop();
        _metrics.SafeIncrementCounter(MetricNames.BatchesSubmitted);
        _metrics.SafeRecordHistogram(
            MetricNames.SubmitLatencyMs,
            submitStarted.Elapsed.TotalMilliseconds);
    }

    private static L2BatchCommitment BuildCommitment(
        ProofWitnessArtifactV1 artifact,
        ProofResultManifest manifest) => new()
        {
            ChainId = artifact.ChainId,
            BatchNumber = artifact.BatchNumber,
            FirstBlock = artifact.FirstBlock,
            LastBlock = artifact.LastBlock,
            PreStateRoot = artifact.ExecutionPayload.PreStateRoot,
            PostStateRoot = artifact.ExecutionResult.PostStateRoot,
            TxRoot = artifact.ExecutionResult.TxRoot,
            ReceiptRoot = artifact.ExecutionResult.ReceiptRoot,
            WithdrawalRoot = artifact.ExecutionResult.WithdrawalRoot,
            L2ToL1MessageRoot = artifact.ExecutionResult.L2ToL1MessageRoot,
            L2ToL2MessageRoot = artifact.ExecutionResult.L2ToL2MessageRoot,
            DACommitment = artifact.DAReceipt.Commitment,
            PublicInputHash = manifest.PublicInputHash,
            ProofType = manifest.ProofType,
            Proof = manifest.Proof.ToArray(),
        };

    private static PublicInputs WithForcedCount(PublicInputs inputs, int forcedCount)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var count = checked((uint)forcedCount);
        return inputs.ForcedInclusionCount == count
            ? inputs
            : inputs with { ForcedInclusionCount = count };
    }
}
