using System.Buffers.Binary;
using Neo.Cryptography;

namespace Neo.L2.Messaging;

/// <summary>
/// Canonical leaf hashing for <see cref="ExternalCrossChainMessage"/>. The output is
/// what the on-chain <c>NeoHub.ExternalBridgeRegistry</c> verifier checks against, and
/// what off-chain watchers / committee signers attest over.
/// </summary>
/// <remarks>
/// Same conventions as <see cref="Neo.L2.State.MessageHasher"/>: little-endian
/// integers, length-prefixed payload, <c>Hash256</c> (double-SHA256) for parity with
/// Neo's existing tx + Merkle hashing. The two hashers are intentionally distinct
/// — they serve disjoint message universes (internal Neo vs. cross-foreign), so a
/// future change to one cannot silently shift the other's commitment.
/// </remarks>
public static class ExternalMessageHasher
{
    /// <summary>Compute the canonical hash for an
    /// <see cref="ExternalCrossChainMessage"/>.</summary>
    public static UInt256 HashMessage(ExternalCrossChainMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        // UInt160 / UInt256 are reference types in Neo's lib; `required` enforces
        // "set" but not "non-null". Catch null at the cryptographic boundary so
        // GetSpan() doesn't surface a NullReferenceException with no breadcrumb.
        ArgumentNullException.ThrowIfNull(message.Sender);
        ArgumentNullException.ThrowIfNull(message.Recipient);
        ArgumentNullException.ThrowIfNull(message.SourceTxRef);

        // Layout:
        //   4B  externalChainId   |  4B  neoChainId    |  8B  nonce
        //   1B  direction         | 20B  sender        | 20B  recipient
        //   8B  deadline          | 32B  sourceTxRef   |  1B  messageType
        //   4B  payloadLen        |  N   payload bytes
        // Total fixed-prefix = 102 bytes.
        var size = checked(102 + message.Payload.Length);
        var buffer = size <= 256 ? stackalloc byte[size] : new byte[size];

        var pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(pos, 4), message.ExternalChainId); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(pos, 4), message.NeoChainId); pos += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(pos, 8), message.Nonce); pos += 8;
        buffer[pos++] = (byte)message.Direction;
        message.Sender.GetSpan().CopyTo(buffer.Slice(pos, 20)); pos += 20;
        message.Recipient.GetSpan().CopyTo(buffer.Slice(pos, 20)); pos += 20;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(pos, 8), message.DeadlineUnixSeconds); pos += 8;
        message.SourceTxRef.GetSpan().CopyTo(buffer.Slice(pos, 32)); pos += 32;
        buffer[pos++] = (byte)message.MessageType;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(pos, 4), message.Payload.Length); pos += 4;
        message.Payload.Span.CopyTo(buffer.Slice(pos));
        pos += message.Payload.Length;

        if (pos != size)
            throw new InvalidOperationException("HashMessage internal length mismatch");

        return new UInt256(Crypto.Hash256(buffer));
    }
}
