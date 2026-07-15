using System.Buffers.Binary;
using Neo.L2;
using Neo.L2.Batch;

namespace Neo.Plugins.L2Gateway;

/// <summary>Canonical request framing for the dedicated Gateway SP1 recursive prover.</summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). This framing does not replace existing encoders. It contains the
/// exact 170-byte <see cref="GatewayProofBindingSerializer"/> output followed by the exact
/// <see cref="BatchSerializer.Encode(L2BatchCommitment)"/> output for every canonical constituent.
/// Layout: <c>"NEO4GWP1" || binding[170] || countLE32 || (lengthLE32 || commitmentBytes)*</c>.
/// The daemon resolves compressed child-proof sidecars by the canonical constituent identity and
/// pins the batch guest verification key in its Gateway guest; no witness-supplied program key or
/// filesystem path is trusted.
/// </remarks>
public static class GatewaySp1RequestSerializer
{
    /// <summary>Fixed bytes before the first constituent.</summary>
    public const int HeaderSize = 8 + GatewayProofBindingSerializer.EncodedSize + 4;

    /// <summary>Maximum constituent count accepted by one recursive proof request.</summary>
    public const int MaxConstituents = 4096;

    /// <summary>Maximum encoded request size.</summary>
    public const int MaxRequestBytes = 64 * 1024 * 1024;

    private static ReadOnlySpan<byte> Magic => "NEO4GWP1"u8;

    /// <summary>Encode one exact Gateway statement and its canonical constituents.</summary>
    public static byte[] Encode(
        GatewayProofBinding binding,
        IReadOnlyList<L2BatchCommitment> constituents)
    {
        GatewayProofBindingSerializer.Validate(binding);
        ArgumentNullException.ThrowIfNull(constituents);
        if (constituents.Count == 0 || constituents.Count > MaxConstituents)
        {
            throw new ArgumentOutOfRangeException(
                nameof(constituents),
                $"constituent count must be in [1, {MaxConstituents}]");
        }
        if (binding.ConstituentCount != (uint)constituents.Count)
            throw new ArgumentException("binding constituent count does not match request", nameof(binding));
        GatewayProofBindingSerializer.ValidateCanonicalConstituentOrder(constituents);
        if (!GatewayProofBindingSerializer.ComputeConstituentCommitmentsRoot(constituents)
            .Equals(binding.ConstituentCommitmentsRoot))
        {
            throw new ArgumentException("binding constituent root does not match request", nameof(binding));
        }
        if (!Sp1RecursiveRoundProver.ComputeGlobalMessageRoot(constituents)
            .Equals(binding.GlobalMessageRoot))
        {
            throw new ArgumentException("binding global message root does not match request", nameof(binding));
        }

        var encoded = new byte[constituents.Count][];
        var totalLength = HeaderSize;
        for (var index = 0; index < constituents.Count; index++)
        {
            var constituent = constituents[index]
                ?? throw new ArgumentException($"constituents[{index}] is null", nameof(constituents));
            if (constituent.ProofType != ProofType.Zk || constituent.Proof.IsEmpty)
            {
                throw new ArgumentException(
                    $"constituents[{index}] must carry a non-empty ZK proof",
                    nameof(constituents));
            }
            encoded[index] = BatchSerializer.Encode(constituent);
            totalLength = checked(totalLength + 4 + encoded[index].Length);
            if (totalLength > MaxRequestBytes)
                throw new ArgumentException(
                    $"Gateway SP1 request exceeds {MaxRequestBytes} bytes",
                    nameof(constituents));
        }

        var request = new byte[totalLength];
        var span = request.AsSpan();
        var position = 0;
        Magic.CopyTo(span);
        position += Magic.Length;
        var bindingBytes = GatewayProofBindingSerializer.Encode(binding);
        bindingBytes.CopyTo(span.Slice(position, GatewayProofBindingSerializer.EncodedSize));
        position += GatewayProofBindingSerializer.EncodedSize;
        BinaryPrimitives.WriteUInt32LittleEndian(
            span.Slice(position, 4),
            checked((uint)constituents.Count));
        position += 4;
        foreach (var bytes in encoded)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                span.Slice(position, 4),
                checked((uint)bytes.Length));
            position += 4;
            bytes.CopyTo(span.Slice(position, bytes.Length));
            position += bytes.Length;
        }
        if (position != request.Length)
            throw new InvalidOperationException("Gateway SP1 request length mismatch");
        return request;
    }
}
