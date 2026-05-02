using System.Buffers.Binary;
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
    /// <summary>Compute the leaf hash for a <see cref="CrossChainMessage"/>.</summary>
    public static UInt256 HashMessage(CrossChainMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        // checked: payload is unbounded — a near-int.MaxValue payload would wrap.
        var size = checked(4 + 4 + 8 + 20 + 20 + 1 + 4 + message.Payload.Length);
        var buffer = size <= 256 ? stackalloc byte[size] : new byte[size];

        var pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(pos, 4), message.SourceChainId); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(pos, 4), message.TargetChainId); pos += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(pos, 8), message.Nonce); pos += 8;
        message.Sender.GetSpan().CopyTo(buffer.Slice(pos, 20)); pos += 20;
        message.Receiver.GetSpan().CopyTo(buffer.Slice(pos, 20)); pos += 20;
        buffer[pos++] = (byte)message.MessageType;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(pos, 4), message.Payload.Length); pos += 4;
        message.Payload.Span.CopyTo(buffer.Slice(pos));
        pos += message.Payload.Length;

        if (pos != size) throw new InvalidOperationException("HashMessage internal length mismatch");

        return new UInt256(Crypto.Hash256(buffer));
    }

    /// <summary>Compute the leaf hash for a <see cref="WithdrawalRequest"/>.</summary>
    public static UInt256 HashWithdrawal(WithdrawalRequest withdrawal)
    {
        ArgumentNullException.ThrowIfNull(withdrawal);
        var amountBytes = withdrawal.Amount.ToByteArray(isUnsigned: true, isBigEndian: false);
        if (amountBytes.Length > 64)
            throw new ArgumentException("Withdrawal amount exceeds 64 bytes", nameof(withdrawal));

        var size = 20 + 20 + 20 + 20 + 4 + amountBytes.Length + 8;
        Span<byte> buffer = size <= 256 ? stackalloc byte[size] : new byte[size];

        var pos = 0;
        withdrawal.EmittingContract.GetSpan().CopyTo(buffer.Slice(pos, 20)); pos += 20;
        withdrawal.L2Sender.GetSpan().CopyTo(buffer.Slice(pos, 20)); pos += 20;
        withdrawal.L1Recipient.GetSpan().CopyTo(buffer.Slice(pos, 20)); pos += 20;
        withdrawal.L2Asset.GetSpan().CopyTo(buffer.Slice(pos, 20)); pos += 20;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(pos, 4), amountBytes.Length); pos += 4;
        amountBytes.AsSpan().CopyTo(buffer.Slice(pos, amountBytes.Length)); pos += amountBytes.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(pos, 8), withdrawal.Nonce); pos += 8;

        if (pos != size) throw new InvalidOperationException("HashWithdrawal internal length mismatch");

        return new UInt256(Crypto.Hash256(buffer));
    }
}
