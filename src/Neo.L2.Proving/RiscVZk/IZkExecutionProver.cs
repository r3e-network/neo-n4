using Neo.L2.Batch;

namespace Neo.L2.Proving.RiscVZk;

/// <summary>Fail-closed metadata required from a production execution-specific ZK prover.</summary>
/// <remarks>See doc.md §7.5 and §8.1–§8.4.</remarks>
public interface IZkExecutionProver : IL2Prover
{
    /// <summary>Exact VM/state-transition semantic executed by the proof guest.</summary>
    UInt256 ExecutionSemanticId { get; }

    /// <summary>Proof backend encoded in the canonical witness artifact.</summary>
    WitnessProofSystem WitnessProofSystem { get; }

    /// <summary>Verification-key identifier bound by the proof envelope.</summary>
    UInt256 VerificationKeyId { get; }

    /// <summary>True only for a cryptographic prover, never a mock or preview envelope.</summary>
    bool ProducesCryptographicProof { get; }
}
