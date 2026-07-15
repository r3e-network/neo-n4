using Neo.Cryptography;
using Neo.L2;

namespace Neo.Plugins.L2;

internal static class DAReceiptFormats
{
    internal static ReadOnlySpan<byte> LocalEvidence
        => "neo-n4:local-content-addressed:v1"u8;

    internal static ReadOnlySpan<byte> NeoFsSemanticEvidence
        => "neo-n4:neofs-semantic-simulation:v1"u8;

    internal static byte[] CommitmentPointer(UInt256 commitment)
        => commitment.GetSpan().ToArray();

    internal static bool IsContentAddressedPayload(
        DAReceipt receipt,
        DAMode expectedLayer,
        DAReceiptKind expectedKind,
        ReadOnlySpan<byte> expectedEvidence,
        ReadOnlySpan<byte> payload)
    {
        if (!receipt.HasRequiredMetadata(expectedLayer, expectedKind)) return false;
        if (receipt.Pointer.Length != UInt256.Length) return false;
        if (!receipt.Pointer.Span.SequenceEqual(receipt.Commitment.GetSpan())) return false;
        if (!receipt.Evidence.Span.SequenceEqual(expectedEvidence)) return false;
        return Crypto.Hash256(payload).AsSpan().SequenceEqual(receipt.Commitment.GetSpan());
    }
}
