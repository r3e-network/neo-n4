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
    /// <summary>Canonical fixed prefix before the message payload.</summary>
    public const int FixedPrefixSize = 102;

    /// <summary>Maximum payload accepted by the NeoHub external-bridge contracts.</summary>
    public const int MaxPayloadSize = 64 * 1024;

    /// <summary>
    /// Encode the exact canonical bytes signed by external-bridge committees and consumed by
    /// <c>NeoHub.ExternalBridgeEscrow</c>.
    /// </summary>
    /// <remarks>See <c>doc.md</c> §11.3.3–§11.3.4.</remarks>
    public static byte[] EncodeCanonical(ExternalCrossChainMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.Sender);
        ArgumentNullException.ThrowIfNull(message.Recipient);
        ArgumentNullException.ThrowIfNull(message.SourceTxRef);
        if (message.Payload.Length > MaxPayloadSize)
            throw new ArgumentException(
                $"External bridge payload exceeds {MaxPayloadSize} bytes.", nameof(message));

        var size = checked(FixedPrefixSize + message.Payload.Length);
        var buffer = new byte[size];
        var span = buffer.AsSpan();

        var pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), message.ExternalChainId); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), message.NeoChainId); pos += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(pos, 8), message.Nonce); pos += 8;
        span[pos++] = (byte)message.Direction;
        message.Sender.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        message.Recipient.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(pos, 8), message.DeadlineUnixSeconds); pos += 8;
        message.SourceTxRef.GetSpan().CopyTo(span.Slice(pos, 32)); pos += 32;
        span[pos++] = (byte)message.MessageType;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), message.Payload.Length); pos += 4;
        message.Payload.Span.CopyTo(span.Slice(pos));
        return buffer;
    }

    /// <summary>Decode canonical signed bytes and recompute their <c>Hash256</c>.</summary>
    /// <remarks>See <c>doc.md</c> §11.3.3–§11.3.4.</remarks>
    public static ExternalCrossChainMessage DecodeCanonical(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedPrefixSize)
            throw new ArgumentException(
                $"Canonical external message is shorter than {FixedPrefixSize} bytes.", nameof(bytes));

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(98, 4));
        if (payloadLength < 0 || payloadLength > MaxPayloadSize)
            throw new ArgumentException(
                $"External bridge payload length {payloadLength} is outside [0, {MaxPayloadSize}].",
                nameof(bytes));
        if (bytes.Length != FixedPrefixSize + payloadLength)
            throw new ArgumentException(
                "Canonical external message length does not match its payload length.", nameof(bytes));

        var direction = (ExternalBridgeDirection)bytes[16];
        if (direction is not (ExternalBridgeDirection.NeoToForeign or ExternalBridgeDirection.ForeignToNeo))
            throw new ArgumentException("Canonical external message has an invalid direction.", nameof(bytes));
        var messageType = (ExternalMessageType)bytes[97];
        if (messageType is not (ExternalMessageType.AssetTransfer
            or ExternalMessageType.Call
            or ExternalMessageType.AssetAndCall))
        {
            throw new ArgumentException("Canonical external message has an invalid message type.", nameof(bytes));
        }

        var unhashed = new ExternalCrossChainMessage
        {
            ExternalChainId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0, 4)),
            NeoChainId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)),
            Nonce = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8, 8)),
            Direction = direction,
            Sender = new UInt160(bytes.Slice(17, UInt160.Length)),
            Recipient = new UInt160(bytes.Slice(37, UInt160.Length)),
            DeadlineUnixSeconds = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(57, 8)),
            SourceTxRef = new UInt256(bytes.Slice(65, UInt256.Length)),
            MessageType = messageType,
            Payload = bytes.Slice(FixedPrefixSize, payloadLength).ToArray(),
            MessageHash = UInt256.Zero,
        };
        return unhashed with { MessageHash = HashMessage(unhashed) };
    }

    /// <summary>Compute the canonical hash for an
    /// <see cref="ExternalCrossChainMessage"/>.</summary>
    public static UInt256 HashMessage(ExternalCrossChainMessage message)
    {
        return new UInt256(Crypto.Hash256(EncodeCanonical(message)));
    }
}
