using System.Buffers.Binary;
using System.Numerics;
using Neo.Cryptography;

namespace Neo.L2.State;

/// <summary>
/// Canonical leaf hashing for cross-chain messages and withdrawal records. The output is
/// what gets inserted into <see cref="MessageTree"/> / <see cref="WithdrawalTree"/>, and what
/// NeoHub verifies inclusion proofs against.
/// </summary>
/// <remarks>
/// All multi-byte integers are little-endian. Variable-length payloads are length-prefixed
/// (32-bit LE). The final hash is <c>Hash256</c> (double-SHA256) for consistency with Neo's
/// existing Merkle / TX hashing conventions.
/// </remarks>
public static class MessageHasher
{
    /// <summary>Maximum variable message payload accepted by the canonical decoder.</summary>
    public const int MaxMessagePayloadBytes = 1024 * 1024;

    /// <summary>Compute the leaf hash for a <see cref="CrossChainMessage"/>.</summary>
    public static UInt256 HashMessage(CrossChainMessage message)
        => new(Crypto.Hash256(EncodeMessage(message)));

    /// <summary>Encode the canonical message-hash preimage.</summary>
    public static byte[] EncodeMessage(CrossChainMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        // Defense at the cryptographic-primitive boundary: CrossChainMessage's UInt160
        // fields are reference types and `required` only forces "must be set," not
        // "non-null." Callers like MessageBuilder.Build already validate (iter 146),
        // but HashMessage is a static utility that anything can call directly — guard
        // here too so a null field can't reach GetSpan().
        ArgumentNullException.ThrowIfNull(message.Sender);
        ArgumentNullException.ThrowIfNull(message.Receiver);
        // checked: payload is unbounded — a near-int.MaxValue payload would wrap.
        var size = checked(4 + 4 + 8 + 20 + 20 + 1 + 4 + message.Payload.Length);
        var buffer = new byte[size];
        var span = buffer.AsSpan();

        var pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), message.SourceChainId); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), message.TargetChainId); pos += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(pos, 8), message.Nonce); pos += 8;
        message.Sender.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        message.Receiver.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        span[pos++] = (byte)message.MessageType;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), message.Payload.Length); pos += 4;
        message.Payload.Span.CopyTo(span.Slice(pos));
        pos += message.Payload.Length;

        if (pos != size) throw new InvalidOperationException("HashMessage internal length mismatch");

        return buffer;
    }

    /// <summary>Decode a canonical message preimage and recompute its message hash.</summary>
    public static CrossChainMessage DecodeMessage(ReadOnlySpan<byte> bytes)
    {
        const int fixedSize = 4 + 4 + 8 + 20 + 20 + 1 + 4;
        if (bytes.Length < fixedSize)
            throw new InvalidDataException("message buffer is truncated");
        var position = 0;
        var sourceChainId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position, 4)); position += 4;
        var targetChainId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position, 4)); position += 4;
        var nonce = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(position, 8)); position += 8;
        var sender = new UInt160(bytes.Slice(position, 20)); position += 20;
        var receiver = new UInt160(bytes.Slice(position, 20)); position += 20;
        var messageTypeByte = bytes[position++];
        if (!Enum.IsDefined((MessageType)messageTypeByte))
            throw new InvalidDataException($"unknown message type {messageTypeByte}");
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(position, 4)); position += 4;
        if (payloadLength < 0
            || payloadLength > MaxMessagePayloadBytes
            || position + payloadLength != bytes.Length)
            throw new InvalidDataException($"invalid message payload length {payloadLength}");
        var message = new CrossChainMessage
        {
            SourceChainId = sourceChainId,
            TargetChainId = targetChainId,
            Nonce = nonce,
            Sender = sender,
            Receiver = receiver,
            MessageType = (MessageType)messageTypeByte,
            Payload = bytes.Slice(position, payloadLength).ToArray(),
            MessageHash = UInt256.Zero,
        };
        return message with { MessageHash = HashMessage(message) };
    }

    /// <summary>Compute the leaf hash for a <see cref="WithdrawalRequest"/>.</summary>
    /// <remarks>
    /// Wire format MUST match <c>NeoHub.SharedBridge.ComputeWithdrawalLeafHash</c>:
    /// <c>4B chainId LE | 20B emittingContract | 20B l2Sender | 20B l1Recipient |
    /// 20B l2Asset | 4B amountLen LE | N amountBytes LE | 8B nonce LE</c>, then
    /// Hash256 (double-SHA256). The chainId domain-separator is load-bearing
    /// against cross-L2 inclusion-proof replay — see
    /// <see cref="WithdrawalRequest.ChainId"/>.
    /// </remarks>
    public static UInt256 HashWithdrawal(WithdrawalRequest withdrawal)
        => new(Crypto.Hash256(EncodeWithdrawal(withdrawal)));

    /// <summary>Encode the canonical withdrawal-hash preimage.</summary>
    public static byte[] EncodeWithdrawal(WithdrawalRequest withdrawal)
    {
        ArgumentNullException.ThrowIfNull(withdrawal);
        // Defense at the cryptographic-primitive boundary: WithdrawalRequest's UInt160
        // fields are reference types and `required` only forces "must be set," not
        // "non-null." Callers like WithdrawalProcessor.Stage already validate (iter 147),
        // but HashWithdrawal is a static utility that anything can call directly — guard
        // here too so a null field can't reach GetSpan().
        ArgumentNullException.ThrowIfNull(withdrawal.EmittingContract);
        ArgumentNullException.ThrowIfNull(withdrawal.L2Sender);
        ArgumentNullException.ThrowIfNull(withdrawal.L1Recipient);
        ArgumentNullException.ThrowIfNull(withdrawal.L2Asset);
        var amountBytes = withdrawal.Amount.ToByteArray(isUnsigned: true, isBigEndian: false);
        if (amountBytes.Length > 64)
            throw new ArgumentException("Withdrawal amount exceeds 64 bytes", nameof(withdrawal));

        var size = 4 + 20 + 20 + 20 + 20 + 4 + amountBytes.Length + 8;
        var buffer = new byte[size];
        var span = buffer.AsSpan();

        var pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), withdrawal.ChainId); pos += 4;
        withdrawal.EmittingContract.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        withdrawal.L2Sender.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        withdrawal.L1Recipient.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        withdrawal.L2Asset.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), amountBytes.Length); pos += 4;
        amountBytes.AsSpan().CopyTo(span.Slice(pos, amountBytes.Length)); pos += amountBytes.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(pos, 8), withdrawal.Nonce); pos += 8;

        if (pos != size) throw new InvalidOperationException("HashWithdrawal internal length mismatch");

        return buffer;
    }

    /// <summary>Decode a canonical withdrawal-hash preimage.</summary>
    public static WithdrawalRequest DecodeWithdrawal(ReadOnlySpan<byte> bytes)
    {
        const int fixedSize = 4 + 20 + 20 + 20 + 20 + 4 + 8;
        if (bytes.Length < fixedSize)
            throw new InvalidDataException("withdrawal buffer is truncated");
        var position = 0;
        var chainId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position, 4)); position += 4;
        var emittingContract = new UInt160(bytes.Slice(position, 20)); position += 20;
        var l2Sender = new UInt160(bytes.Slice(position, 20)); position += 20;
        var l1Recipient = new UInt160(bytes.Slice(position, 20)); position += 20;
        var l2Asset = new UInt160(bytes.Slice(position, 20)); position += 20;
        var amountLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(position, 4)); position += 4;
        if (amountLength < 0 || amountLength > 64 || position + amountLength + 8 != bytes.Length)
            throw new InvalidDataException($"invalid withdrawal amount length {amountLength}");
        var amount = new BigInteger(
            bytes.Slice(position, amountLength), isUnsigned: true, isBigEndian: false);
        position += amountLength;
        var nonce = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(position, 8));
        return new WithdrawalRequest
        {
            ChainId = chainId,
            EmittingContract = emittingContract,
            L2Sender = l2Sender,
            L1Recipient = l1Recipient,
            L2Asset = l2Asset,
            Amount = amount,
            Nonce = nonce,
        };
    }
}
