using System.Buffers.Binary;
using Neo;

namespace Sample.CounterChainExecutor;

/// <summary>
/// Helpers to build canonical Counter Chain transaction bytes. Mirrors the wire format
/// the executor decodes — keeping encode + decode in one repo means a future format
/// change must update both sides at once.
/// </summary>
public static class CounterTxBuilder
{
    /// <summary>
    /// Build an <see cref="CounterChainExecutor.Opcode.IncrementCounter"/> transaction:
    /// <c>[0x01][20B sender][8B u64 amount LE]</c>.
    /// </summary>
    public static byte[] IncrementCounter(UInt160 sender, ulong amount)
    {
        ArgumentNullException.ThrowIfNull(sender);
        var buf = new byte[1 + 20 + 8];
        buf[0] = (byte)CounterChainExecutor.Opcode.IncrementCounter;
        sender.GetSpan().CopyTo(buf.AsSpan(1, 20));
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(21, 8), amount);
        return buf;
    }

    /// <summary>
    /// Build an <see cref="CounterChainExecutor.Opcode.EmitWithdrawal"/> transaction:
    /// <c>[0x02][20B recipient][20B token][8B u64 amount LE]</c>.
    /// </summary>
    public static byte[] EmitWithdrawal(UInt160 recipient, UInt160 token, ulong amount)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(token);
        var buf = new byte[1 + 20 + 20 + 8];
        buf[0] = (byte)CounterChainExecutor.Opcode.EmitWithdrawal;
        recipient.GetSpan().CopyTo(buf.AsSpan(1, 20));
        token.GetSpan().CopyTo(buf.AsSpan(21, 20));
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(41, 8), amount);
        return buf;
    }

    /// <summary>
    /// Build an <see cref="CounterChainExecutor.Opcode.EmitMessage"/> transaction:
    /// <c>[0x03][4B destChainId LE][2B msgLen LE][N bytes msg]</c>.
    /// </summary>
    public static byte[] EmitMessage(uint destChainId, ReadOnlySpan<byte> body)
    {
        if (body.Length > CounterChainExecutor.MaxMessageBytes)
            throw new ArgumentException(
                $"message body {body.Length} exceeds MaxMessageBytes ({CounterChainExecutor.MaxMessageBytes})",
                nameof(body));
        var buf = new byte[1 + 4 + 2 + body.Length];
        buf[0] = (byte)CounterChainExecutor.Opcode.EmitMessage;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1, 4), destChainId);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(5, 2), (ushort)body.Length);
        body.CopyTo(buf.AsSpan(7, body.Length));
        return buf;
    }
}
