using System.Buffers.Binary;

namespace Neo.L2.Proving;

/// <summary>
/// Shared encode/decode helpers for proof payloads. Every proof format
/// (Multisig, Optimistic, RiscV-ZK) uses the same patterns: version byte
/// dispatch, strict length matching, length-prefixed varbytes, and
/// symmetry between encode and decode. Centralizing these eliminates
/// duplicated bounds-checking and version-validation logic.
/// </summary>
public static class ProofPayloadSerializer
{
    /// <summary>Write a version byte at the start of the buffer.</summary>
    public static void WriteVersion(Span<byte> span, byte version)
    {
        if (span.IsEmpty)
            throw new ArgumentException("Buffer too small for a version byte", nameof(span));
        span[0] = version;
    }

    /// <summary>Read and validate a version byte. Throws <see cref="InvalidDataException"/>
    /// if the version doesn't match <paramref name="expected"/>.</summary>
    public static void ReadVersion(ReadOnlySpan<byte> bytes, byte expected, string payloadName)
    {
        if (bytes.IsEmpty)
            throw new ArgumentException($"Buffer too small for {payloadName}", nameof(bytes));
        if (bytes[0] != expected)
            throw new InvalidDataException(
                $"Unsupported {payloadName} version {bytes[0]} (expected {expected})");
    }

    /// <summary>Validate that the buffer length exactly matches the expected size.</summary>
    public static void ValidateExactLength(int actual, int expected, string payloadName)
    {
        if (actual != expected)
            throw new InvalidDataException(
                $"{payloadName} buffer size {actual} != expected {expected}");
    }

    /// <summary>Validate that a variable-length payload size is within bounds.</summary>
    public static void ValidateVarBytesLength(int len, int maxBytes, string fieldName)
    {
        if (maxBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (len < 0 || len > maxBytes)
            throw new InvalidDataException(
                $"Bad {fieldName} length {len} (max {maxBytes})");
    }

    /// <summary>Write a 32-bit LE length prefix followed by the data.</summary>
    public static void WriteLengthPrefixed(Span<byte> span, ref int pos, ReadOnlySpan<byte> data)
    {
        if (pos < 0 || pos > span.Length - 4 || data.Length > span.Length - pos - 4)
            throw new ArgumentException("Buffer too small for length-prefixed data", nameof(span));
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), data.Length);
        pos += 4;
        data.CopyTo(span.Slice(pos));
        pos += data.Length;
    }

    /// <summary>Read a 32-bit LE length prefix and return the corresponding slice.</summary>
    public static ReadOnlySpan<byte> ReadLengthPrefixed(
        ReadOnlySpan<byte> bytes, ref int pos, int maxBytes, string fieldName)
    {
        if (pos < 0 || pos > bytes.Length - 4)
            throw new InvalidDataException($"Truncated {fieldName} length prefix");
        var len = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(pos, 4));
        pos += 4;
        ValidateVarBytesLength(len, maxBytes, fieldName);
        if (len > bytes.Length - pos)
            throw new InvalidDataException($"Truncated {fieldName} payload");
        var data = bytes.Slice(pos, len);
        pos += len;
        return data;
    }
}
