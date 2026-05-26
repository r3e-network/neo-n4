using System.Collections.Generic;

namespace Neo.L2;

/// <summary>
/// Lexicographic byte-array comparer. Single canonical implementation used by
/// state trees, persistence adapters, and execution engines. Using this shared
/// instance guarantees that sort order is consistent across the entire L2 stack.
/// </summary>
public sealed class LexicographicByteArrayComparer : IComparer<byte[]>
{
    /// <summary>Singleton.</summary>
    public static readonly LexicographicByteArrayComparer Instance = new();

    private LexicographicByteArrayComparer() { }

    /// <inheritdoc />
    public int Compare(byte[]? x, byte[]? y)
    {
        if (x is null) return y is null ? 0 : -1;
        if (y is null) return 1;
        return x.AsSpan().SequenceCompareTo(y);
    }
}
