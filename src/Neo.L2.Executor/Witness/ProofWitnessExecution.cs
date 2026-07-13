using Neo.L2.Batch;

namespace Neo.L2.Executor.ProofWitness;

/// <summary>Proof-ready canonical outputs returned by <see cref="IProofWitnessBatchExecutor"/>.</summary>
/// <remarks>
/// See doc.md §7.5 and §8. The state and effects fields are opaque to the settlement pipeline;
/// their canonical encodings are owned by the matching execution profile.
/// </remarks>
public sealed record ProofWitnessExecutionResult
{
    /// <summary>Canonical execution roots and gas.</summary>
    public required BatchExecutionResult ExecutionResult { get; init; }

    /// <summary>Exact VM/state-transition semantic executed.</summary>
    public required UInt256 ExecutionSemanticId { get; init; }

    /// <summary>True only when the executor authenticates the supplied state witness.</summary>
    public required bool WitnessAuthenticated { get; init; }

    /// <summary>Canonical versioned VM/state witness bytes.</summary>
    public required ReadOnlyMemory<byte> StateWitness { get; init; }

    /// <summary>Canonical versioned receipt/effects bytes.</summary>
    public required ReadOnlyMemory<byte> Effects { get; init; }

    /// <inheritdoc />
    public bool Equals(ProofWitnessExecutionResult? other)
        => other is not null
            && ExecutionResult.Equals(other.ExecutionResult)
            && ExecutionSemanticId.Equals(other.ExecutionSemanticId)
            && WitnessAuthenticated == other.WitnessAuthenticated
            && StateWitness.Span.SequenceEqual(other.StateWitness.Span)
            && Effects.Span.SequenceEqual(other.Effects.Span);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ExecutionResult);
        hash.Add(ExecutionSemanticId);
        hash.Add(WitnessAuthenticated);
        hash.AddBytes(StateWitness.Span);
        hash.AddBytes(Effects.Span);
        return hash.ToHashCode();
    }
}

/// <summary>Batch executor seam that returns complete canonical proof material.</summary>
/// <remarks>See doc.md §8.1–§8.4.</remarks>
public interface IProofWitnessBatchExecutor : IL2BatchExecutor
{
    /// <summary>Execute one immutable batch and retain the exact canonical proof outputs.</summary>
    ValueTask<ProofWitnessExecutionResult> ApplyBatchWithWitnessAsync(
        SealedBatch batch,
        CancellationToken cancellationToken = default);
}
