using System.Buffers.Binary;
using Neo.Cryptography;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.State;

namespace Neo.Plugins.L2Gateway;

/// <summary>Canonical statement proven before NeoHub publishes a Gateway global root.</summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). The statement binds the MessageRouter deployment, replay domain,
/// epoch, global root, complete ordered constituent set, aggregation backend, proof system, and
/// verification key. The terminal proof verifies against
/// <see cref="GatewayProofBindingSerializer.ComputeHash"/>.
/// </remarks>
public sealed record GatewayProofBinding
{
    /// <summary>Target NeoHub.MessageRouter deployment.</summary>
    public required UInt160 MessageRouter { get; init; }

    /// <summary>Application/network replay domain pinned by MessageRouter governance.</summary>
    public required UInt256 ReplayDomain { get; init; }

    /// <summary>Gateway aggregation epoch.</summary>
    public required ulong BatchEpoch { get; init; }

    /// <summary>Global L2-to-L2 message root for the epoch.</summary>
    public required UInt256 GlobalMessageRoot { get; init; }

    /// <summary>Merkle root of the complete canonical constituent commitments.</summary>
    public required UInt256 ConstituentCommitmentsRoot { get; init; }

    /// <summary>Number of constituent commitments represented by the root.</summary>
    public required uint ConstituentCount { get; init; }

    /// <summary>Aggregation algorithm/backend discriminator.</summary>
    public required byte AggregationBackendId { get; init; }

    /// <summary>Terminal proof-system discriminator accepted by the verifier contract.</summary>
    public required byte ProofSystem { get; init; }

    /// <summary>Governance-pinned verification-key identifier.</summary>
    public required UInt256 VerificationKeyId { get; init; }
}

/// <summary>Canonical encoder and hashing helpers for <see cref="GatewayProofBinding"/>.</summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). All integers are little-endian and the final digest is Hash256.
/// Fixed layout: <c>"NEO4GWR2" || router(20) || replayDomain(32) || epochLE64 ||
/// globalRoot(32) || constituentRoot(32) || constituentCountLE32 || aggregationBackend(1) ||
/// proofSystem(1) || verificationKeyId(32)</c>.
/// </remarks>
public static class GatewayProofBindingSerializer
{
    /// <summary>Fixed encoded length.</summary>
    public const int EncodedSize = 170;

    private static ReadOnlySpan<byte> DomainTag => "NEO4GWR2"u8;

    /// <summary>Create and validate a binding from an aggregate.</summary>
    public static GatewayProofBinding Create(
        UInt160 messageRouter,
        UInt256 replayDomain,
        ulong batchEpoch,
        AggregatedCommitment commitment,
        byte proofSystem,
        UInt256 verificationKeyId)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(commitment.Constituents);
        ValidateCanonicalConstituentOrder(commitment.Constituents);
        var constituentCount = checked((uint)commitment.Constituents.Count);
        var recomputedRoot = ComputeConstituentCommitmentsRoot(commitment.Constituents);
        if (!recomputedRoot.Equals(commitment.ConstituentCommitmentsRoot))
            throw new InvalidOperationException("aggregate constituent commitments changed after aggregation");

        var binding = new GatewayProofBinding
        {
            MessageRouter = messageRouter,
            ReplayDomain = replayDomain,
            BatchEpoch = batchEpoch,
            GlobalMessageRoot = commitment.GlobalMessageRoot,
            ConstituentCommitmentsRoot = recomputedRoot,
            ConstituentCount = constituentCount,
            AggregationBackendId = commitment.BackendId,
            ProofSystem = proofSystem,
            VerificationKeyId = verificationKeyId,
        };
        Validate(binding);
        return binding;
    }

    /// <summary>Encode to the fixed canonical wire format.</summary>
    public static byte[] Encode(GatewayProofBinding binding)
    {
        Validate(binding);
        var bytes = new byte[EncodedSize];
        var span = bytes.AsSpan();
        DomainTag.CopyTo(span);
        binding.MessageRouter.GetSpan().CopyTo(span.Slice(8, 20));
        binding.ReplayDomain.GetSpan().CopyTo(span.Slice(28, 32));
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(60, 8), binding.BatchEpoch);
        binding.GlobalMessageRoot.GetSpan().CopyTo(span.Slice(68, 32));
        binding.ConstituentCommitmentsRoot.GetSpan().CopyTo(span.Slice(100, 32));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(132, 4), binding.ConstituentCount);
        span[136] = binding.AggregationBackendId;
        span[137] = binding.ProofSystem;
        binding.VerificationKeyId.GetSpan().CopyTo(span.Slice(138, 32));
        return bytes;
    }

    /// <summary>Decode and validate the fixed canonical wire format.</summary>
    public static GatewayProofBinding Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != EncodedSize)
            throw new InvalidDataException($"gateway proof binding must be {EncodedSize} bytes");
        if (!bytes.Slice(0, DomainTag.Length).SequenceEqual(DomainTag))
            throw new InvalidDataException("unsupported gateway proof binding domain/version");

        var binding = new GatewayProofBinding
        {
            MessageRouter = new UInt160(bytes.Slice(8, 20).ToArray()),
            ReplayDomain = new UInt256(bytes.Slice(28, 32).ToArray()),
            BatchEpoch = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(60, 8)),
            GlobalMessageRoot = new UInt256(bytes.Slice(68, 32).ToArray()),
            ConstituentCommitmentsRoot = new UInt256(bytes.Slice(100, 32).ToArray()),
            ConstituentCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(132, 4)),
            AggregationBackendId = bytes[136],
            ProofSystem = bytes[137],
            VerificationKeyId = new UInt256(bytes.Slice(138, 32).ToArray()),
        };
        try
        {
            Validate(binding);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("invalid gateway proof binding", ex);
        }
        return binding;
    }

    /// <summary>Hash the canonical encoding with Neo's Hash256 convention.</summary>
    public static UInt256 ComputeHash(GatewayProofBinding binding) =>
        new(Crypto.Hash256(Encode(binding)));

    /// <summary>Compute the ordered Merkle root of Hash256(canonical commitment) leaves.</summary>
    public static UInt256 ComputeConstituentCommitmentsRoot(
        IReadOnlyList<L2BatchCommitment> constituents)
    {
        ArgumentNullException.ThrowIfNull(constituents);
        if (constituents.Count == 0)
            throw new ArgumentException("at least one constituent is required", nameof(constituents));
        var leaves = new UInt256[constituents.Count];
        for (var i = 0; i < constituents.Count; i++)
        {
            var constituent = constituents[i]
                ?? throw new ArgumentException($"constituents[{i}] is null", nameof(constituents));
            leaves[i] = new UInt256(Crypto.Hash256(BatchSerializer.Encode(constituent)));
        }
        return Neo.L2.State.MerkleTree.ComputeRoot(leaves);
    }

    /// <summary>True only for a non-reserved, non-pass-through aggregation backend.</summary>
    public static bool IsProductionAggregationBackend(byte backendId) =>
        backendId != 0
        && backendId != PassThroughRoundProver.ConstBackendId
        && backendId != PassThroughAggregator.BackendId;

    /// <summary>Validate all fixed-field invariants.</summary>
    public static void Validate(GatewayProofBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(binding.MessageRouter);
        ArgumentNullException.ThrowIfNull(binding.ReplayDomain);
        ArgumentNullException.ThrowIfNull(binding.GlobalMessageRoot);
        ArgumentNullException.ThrowIfNull(binding.ConstituentCommitmentsRoot);
        ArgumentNullException.ThrowIfNull(binding.VerificationKeyId);
        if (binding.MessageRouter.Equals(UInt160.Zero))
            throw new ArgumentException("MessageRouter must be non-zero", nameof(binding));
        if (binding.ReplayDomain.Equals(UInt256.Zero))
            throw new ArgumentException("ReplayDomain must be non-zero", nameof(binding));
        if (binding.GlobalMessageRoot.Equals(UInt256.Zero))
            throw new ArgumentException("GlobalMessageRoot must be non-zero", nameof(binding));
        if (binding.ConstituentCommitmentsRoot.Equals(UInt256.Zero))
            throw new ArgumentException("ConstituentCommitmentsRoot must be non-zero", nameof(binding));
        if (binding.ConstituentCount is 0 or > GatewayFinalityReferenceSerializer.MaxConstituents)
            throw new ArgumentException("ConstituentCount must be 1..4096", nameof(binding));
        if (!IsProductionAggregationBackend(binding.AggregationBackendId))
            throw new ArgumentException("pass-through/reserved aggregation backend is not publishable", nameof(binding));
        if (binding.ProofSystem is < 1 or > 4)
            throw new ArgumentException("ProofSystem must be 1..4", nameof(binding));
        if (binding.VerificationKeyId.Equals(UInt256.Zero))
            throw new ArgumentException("VerificationKeyId must be non-zero", nameof(binding));
    }

    internal static void ValidateCanonicalConstituentOrder(
        IReadOnlyList<L2BatchCommitment> constituents)
    {
        if (constituents.Count == 0)
            throw new ArgumentException("at least one constituent is required", nameof(constituents));
        for (var i = 0; i < constituents.Count; i++)
        {
            var current = constituents[i]
                ?? throw new ArgumentException($"constituents[{i}] is null", nameof(constituents));
            if (current.ChainId == 0)
                throw new ArgumentException("Gateway chainId 0 is reserved for L1", nameof(constituents));
            if (i == 0) continue;
            var previous = constituents[i - 1];
            if (previous.ChainId > current.ChainId
                || (previous.ChainId == current.ChainId && previous.BatchNumber >= current.BatchNumber))
            {
                throw new ArgumentException(
                    "constituents must be strictly ordered by (chainId, batchNumber)",
                    nameof(constituents));
            }
        }
    }
}
