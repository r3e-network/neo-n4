using System.Buffers;
using System.Buffers.Binary;
using Neo.VM.Types;
using Array = Neo.VM.Types.Array;
using Boolean = Neo.VM.Types.Boolean;
using Buffer = Neo.VM.Types.Buffer;

namespace Neo.L2.Executor.Effects;

/// <summary>Encodes Neo VM stack items using the N4 genesis <c>NEO4STK1</c> V1 format.</summary>
/// <remarks>See <c>doc.md</c> §8 and executor <c>SPEC.md</c>.</remarks>
public static class CanonicalStackStateSerializer
{
    private static ReadOnlySpan<byte> Magic => "NEO4STK1"u8;

    private const int MaximumDepth = 16;
    private const int MaximumNodes = 512;
    private const int MaximumEncodedBytes = 1024;

    /// <summary>Encode one complete recursive stack value in canonical N4 genesis V1 form.</summary>
    public static byte[] Serialize(StackItem value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var writer = new ArrayBufferWriter<byte>();
        WriteBytes(writer, Magic);
        WriteUInt16(writer, 1);
        WriteUInt16(writer, 0);
        var nodes = 0;
        WriteStackItem(writer, value, 0, ref nodes);
        if (writer.WrittenCount > MaximumEncodedBytes)
            throw new InvalidOperationException("canonical event state exceeds 1024 bytes");
        return writer.WrittenSpan.ToArray();
    }

    private static void WriteStackItem(
        IBufferWriter<byte> writer,
        StackItem value,
        int depth,
        ref int nodes)
    {
        if (depth > MaximumDepth)
            throw new InvalidOperationException("canonical stack depth exceeds 16");
        nodes = checked(nodes + 1);
        if (nodes > MaximumNodes)
            throw new InvalidOperationException("canonical stack node count exceeds 512");

        switch (value)
        {
            case Null:
                WriteByte(writer, 0x00);
                break;
            case Boolean boolean:
                WriteByte(writer, 0x20);
                WriteByte(writer, boolean.GetBoolean() ? (byte)1 : (byte)0);
                break;
            case Integer integer:
                WriteByte(writer, 0x21);
                WriteLengthPrefixedBytes(writer, NormalizeSignedLittleEndian(
                    integer.GetInteger().ToByteArray(isUnsigned: false, isBigEndian: false)));
                break;
            case ByteString byteString:
                WriteByte(writer, 0x28);
                WriteLengthPrefixedBytes(writer, byteString.GetSpan());
                break;
            case Buffer buffer:
                WriteByte(writer, 0x30);
                WriteLengthPrefixedBytes(writer, buffer.GetSpan());
                break;
            case Struct @struct:
                WriteSequence(writer, 0x41, @struct, depth, ref nodes);
                break;
            case Array array:
                WriteSequence(writer, 0x40, array, depth, ref nodes);
                break;
            case Map map:
                WriteMap(writer, map, depth, ref nodes);
                break;
            default:
                throw new InvalidOperationException(
                    $"stack type {value.Type} is not canonical-event serializable");
        }
    }

    private static void WriteSequence(
        IBufferWriter<byte> writer,
        byte tag,
        Array sequence,
        int depth,
        ref int nodes)
    {
        WriteByte(writer, tag);
        WriteUInt32(writer, checked((uint)sequence.Count));
        foreach (var item in sequence)
            WriteStackItem(writer, item, depth + 1, ref nodes);
    }

    private static void WriteMap(
        IBufferWriter<byte> writer,
        Map map,
        int depth,
        ref int nodes)
    {
        var keys = map.Keys.ToArray();
        WriteByte(writer, 0x48);
        WriteUInt32(writer, checked((uint)keys.Length));
        foreach (var key in keys)
        {
            WriteStackItem(writer, key, depth + 1, ref nodes);
            WriteStackItem(writer, map[key], depth + 1, ref nodes);
        }
    }

    private static ReadOnlySpan<byte> NormalizeSignedLittleEndian(byte[] bytes)
    {
        var length = bytes.Length;
        while (length > 1)
        {
            var last = bytes[length - 1];
            var next = bytes[length - 2];
            if ((last == 0 && (next & 0x80) == 0) || (last == 0xff && (next & 0x80) != 0))
                length--;
            else
                break;
        }
        if (length == 1 && bytes[0] == 0) length = 0;
        return bytes.AsSpan(0, length);
    }

    private static void WriteLengthPrefixedBytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        WriteUInt32(writer, checked((uint)value.Length));
        WriteBytes(writer, value);
    }

    private static void WriteUInt16(IBufferWriter<byte> writer, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(writer.GetSpan(sizeof(ushort)), value);
        writer.Advance(sizeof(ushort));
    }

    private static void WriteUInt32(IBufferWriter<byte> writer, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(writer.GetSpan(sizeof(uint)), value);
        writer.Advance(sizeof(uint));
    }

    private static void WriteByte(IBufferWriter<byte> writer, byte value)
    {
        writer.GetSpan(1)[0] = value;
        writer.Advance(1);
    }

    private static void WriteBytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        value.CopyTo(writer.GetSpan(value.Length));
        writer.Advance(value.Length);
    }
}
