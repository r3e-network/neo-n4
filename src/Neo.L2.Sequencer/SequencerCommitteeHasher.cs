using Neo.Cryptography;
using Neo.Cryptography.ECC;
using NeoECPoint = Neo.Cryptography.ECC.ECPoint;

namespace Neo.L2.Sequencer;

/// <summary>
/// Canonical <c>SequencerCommitteeHash</c> for <see cref="BatchBlockContext"/>: Hash256 over
/// compressed secp256r1 public keys of the active committee, sorted lexicographically.
/// </summary>
/// <remarks>
/// See doc.md §7.1 / §8.3. Matches the encoding used by the in-process devnet so production
/// seals and devnet seals share one trust anchor for committee identity.
/// </remarks>
public static class SequencerCommitteeHasher
{
    /// <summary>
    /// Hash an ordered or unordered set of committee public keys (empty → <see cref="UInt256.Zero"/>).
    /// </summary>
    public static UInt256 Compute(IEnumerable<NeoECPoint> publicKeys)
    {
        ArgumentNullException.ThrowIfNull(publicKeys);
        var encoded = publicKeys
            .Select(key =>
            {
                ArgumentNullException.ThrowIfNull(key);
                return key.EncodePoint(true);
            })
            .OrderBy(bytes => bytes, ByteArrayComparer.Instance)
            .ToArray();
        if (encoded.Length == 0)
            return UInt256.Zero;

        var total = 0;
        foreach (var key in encoded)
            total = checked(total + key.Length);
        var buffer = new byte[total];
        var offset = 0;
        foreach (var key in encoded)
        {
            key.CopyTo(buffer.AsSpan(offset));
            offset += key.Length;
        }
        return new UInt256(Crypto.Hash256(buffer));
    }

    /// <summary>
    /// Hash active <see cref="CommitteeMember"/> public keys (status is not re-checked here —
    /// callers should pass the snapshot from <see cref="ISequencerCommitteeProvider.GetActiveCommitteeAsync"/>).
    /// </summary>
    public static UInt256 Compute(IReadOnlyList<CommitteeMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        return Compute(members.Select(member => member.PublicKey));
    }

    /// <summary>
    /// Synchronous <c>Func&lt;UInt256&gt;</c> for seal / WireProduction that snapshots the active
    /// committee via <paramref name="provider"/> and returns <see cref="Compute(IReadOnlyList{CommitteeMember})"/>.
    /// </summary>
    public static Func<UInt256> CreateSyncProvider(ISequencerCommitteeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return () =>
        {
            var members = provider.GetActiveCommitteeAsync(CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            return Compute(members);
        };
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();

        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            var len = Math.Min(x.Length, y.Length);
            for (var i = 0; i < len; i++)
            {
                var c = x[i].CompareTo(y[i]);
                if (c != 0) return c;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
