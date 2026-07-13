using System.Buffers.Binary;
using Neo.L2;

namespace Neo.Plugins.L2Gateway;

/// <summary>Identifies one exact L2 batch finalized by NeoHub.SettlementManager.</summary>
/// <remarks>See doc.md §4 (Neo Gateway).</remarks>
public readonly record struct GatewayFinalityReference(uint ChainId, ulong BatchNumber);

/// <summary>Canonical packed encoding for Gateway constituent finality references.</summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). Each entry is exactly
/// <c>chainId:uint32 LE || batchNumber:uint64 LE</c>. Entries are strictly ordered by that tuple
/// and the complete payload is bounded to 4096 constituents so it fits a Neo transaction script.
/// </remarks>
public static class GatewayFinalityReferenceSerializer
{
    /// <summary>Bytes in one canonical reference.</summary>
    public const int EntrySize = 12;

    /// <summary>Maximum constituents accepted by the on-chain streaming Merkle verifier.</summary>
    public const int MaxConstituents = 4096;

    /// <summary>Encode the exact constituent identities committed by an aggregate.</summary>
    public static byte[] Encode(IReadOnlyList<L2BatchCommitment> constituents)
    {
        ArgumentNullException.ThrowIfNull(constituents);
        GatewayProofBindingSerializer.ValidateCanonicalConstituentOrder(constituents);
        if (constituents.Count > MaxConstituents)
            throw new ArgumentException("at most 4096 Gateway constituents are supported", nameof(constituents));

        var result = new byte[checked(constituents.Count * EntrySize)];
        for (var index = 0; index < constituents.Count; index++)
        {
            var constituent = constituents[index];
            var entry = result.AsSpan(index * EntrySize, EntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, constituent.ChainId);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[4..], constituent.BatchNumber);
        }
        return result;
    }

    /// <summary>Decode and validate a canonical packed reference sequence.</summary>
    public static IReadOnlyList<GatewayFinalityReference> Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length % EntrySize != 0)
            throw new InvalidDataException("Gateway finality references must contain complete entries");
        var count = bytes.Length / EntrySize;
        if (count > MaxConstituents)
            throw new InvalidDataException("at most 4096 Gateway constituents are supported");

        var references = new GatewayFinalityReference[count];
        for (var index = 0; index < count; index++)
        {
            var entry = bytes.Slice(index * EntrySize, EntrySize);
            var current = new GatewayFinalityReference(
                BinaryPrimitives.ReadUInt32LittleEndian(entry),
                BinaryPrimitives.ReadUInt64LittleEndian(entry[4..]));
            if (current.ChainId == 0)
                throw new InvalidDataException("Gateway chainId 0 is reserved for L1");
            if (index > 0)
            {
                var previous = references[index - 1];
                if (previous.ChainId > current.ChainId
                    || (previous.ChainId == current.ChainId
                        && previous.BatchNumber >= current.BatchNumber))
                {
                    throw new InvalidDataException(
                        "Gateway finality references must be strictly ordered");
                }
            }
            references[index] = current;
        }
        return references;
    }
}
