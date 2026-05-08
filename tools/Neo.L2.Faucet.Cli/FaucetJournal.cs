using System;
using System.Buffers.Binary;
using System.Numerics;
using Neo.L2.Persistence;

namespace Neo.L2.Faucet.Cli;

/// <summary>
/// Per-recipient rate-limit + drip-amount accounting persisted to an
/// <see cref="IL2KeyValueStore"/>. Production deployments wire RocksDB so
/// the rate limit survives faucet restarts — without persistence, a malicious
/// requester can drain by bouncing the faucet process between requests.
/// </summary>
/// <remarks>
/// Wire layout per recipient (key = 20-byte UInt160 address):
/// <list type="bullet">
///   <item><c>[8B lastDripUnixSeconds LE]</c></item>
///   <item><c>[16B totalDripped (BigInteger lo + hi)]</c> — saturating total of all drips ever, signed-positive</item>
///   <item><c>[4B totalCount LE]</c></item>
/// </list>
/// 28 bytes per recipient. Total cap (both per-window and lifetime) lives in
/// <see cref="FaucetPolicy"/>; the journal just records the data the policy reads.
/// </remarks>
public sealed class FaucetJournal
{
    private readonly IL2KeyValueStore _store;

    /// <summary>Construct against the persistent backing store.</summary>
    public FaucetJournal(IL2KeyValueStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>Read the current journal entry for <paramref name="recipient"/>; null if no prior drips.</summary>
    public FaucetEntry? Get(UInt160 recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        var bytes = _store.Get(recipient.GetSpan());
        return bytes is null ? null : Decode(bytes);
    }

    /// <summary>
    /// Append a successful drip: bump <c>lastDripUnixSeconds</c>, add to
    /// <c>totalDripped</c>, increment <c>totalCount</c>.
    /// </summary>
    public void RecordDrip(UInt160 recipient, ulong nowUnixSeconds, BigInteger amount)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        if (amount <= 0)
            throw new ArgumentException("amount must be positive", nameof(amount));

        var existing = Get(recipient);
        var entry = new FaucetEntry
        {
            LastDripUnixSeconds = nowUnixSeconds,
            TotalDripped = (existing?.TotalDripped ?? BigInteger.Zero) + amount,
            TotalCount = (existing?.TotalCount ?? 0) + 1,
        };
        _store.Put(recipient.GetSpan(), Encode(entry));
    }

    private static FaucetEntry Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 28)
            throw new InvalidDataException($"faucet entry: expected 28 bytes, got {bytes.Length}");
        var lastDrip = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(0, 8));
        // Pack 16 bytes as little-endian unsigned BigInteger (saturates at 2^128).
        // This is enough for any sane faucet's lifetime drip total: 2^128 is
        // ~3.4e38 datoshi which dwarfs any real cumulative drip even if every
        // request hits the hard per-request cap (1 GAS = 100M datoshi).
        var totalDripped = new BigInteger(bytes.Slice(8, 16), isUnsigned: true, isBigEndian: false);
        var totalCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(24, 4));
        return new FaucetEntry
        {
            LastDripUnixSeconds = lastDrip,
            TotalDripped = totalDripped,
            TotalCount = totalCount,
        };
    }

    private static byte[] Encode(FaucetEntry entry)
    {
        var buf = new byte[28];
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(0, 8), entry.LastDripUnixSeconds);
        // BigInteger.TryWriteBytes truncates at the destination size — defense
        // against a corrupted-input total exceeding 2^128 by bounding writes.
        Span<byte> bigBuf = stackalloc byte[16];
        if (!entry.TotalDripped.TryWriteBytes(bigBuf, out var written, isUnsigned: true, isBigEndian: false))
            throw new InvalidOperationException(
                $"totalDripped {entry.TotalDripped} exceeds 16-byte saturating cap");
        bigBuf.CopyTo(buf.AsSpan(8, 16));
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24, 4), entry.TotalCount);
        return buf;
    }
}

/// <summary>One recipient's faucet history.</summary>
public sealed record FaucetEntry
{
    /// <summary>Unix seconds of the most recent successful drip.</summary>
    public required ulong LastDripUnixSeconds { get; init; }

    /// <summary>Sum of all amounts ever dripped to this recipient.</summary>
    public required BigInteger TotalDripped { get; init; }

    /// <summary>Total successful drips ever.</summary>
    public required uint TotalCount { get; init; }
}
