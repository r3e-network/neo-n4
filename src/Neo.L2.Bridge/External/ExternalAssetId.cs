namespace Neo.L2.Bridge.External;

/// <summary>
/// Opaque 20-byte foreign-chain asset identifier in network byte order.
/// </summary>
/// <remarks>
/// See <c>doc.md</c> §11.3.3–§11.3.4. EVM addresses are copied exactly as
/// displayed on the foreign chain; Neo's little-endian <c>UInt160</c> display
/// and storage conventions must never be applied to this wire identifier.
/// </remarks>
public sealed record ExternalAssetId
{
    /// <summary>Encoded asset identifier length.</summary>
    public const int Length = 20;

    /// <summary>
    /// Canonical EVM native-asset sentinel used by the Solidity router and watcher.
    /// </summary>
    public const string NativeAssetSentinelHex = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

    private readonly byte[] _networkBytes;

    /// <summary>Canonical native EVM asset identifier.</summary>
    public static ExternalAssetId Native { get; } = Parse(NativeAssetSentinelHex);

    /// <summary>Create an identifier from exactly 20 opaque network-order bytes.</summary>
    public ExternalAssetId(ReadOnlySpan<byte> networkBytes)
    {
        if (networkBytes.Length != Length)
            throw new ArgumentException(
                $"foreign asset length {networkBytes.Length} != {Length}", nameof(networkBytes));
        if (networkBytes.IndexOfAnyExcept((byte)0) < 0)
            throw new ArgumentException("foreign asset must be non-zero", nameof(networkBytes));

        _networkBytes = networkBytes.ToArray();
    }

    /// <summary>Parse a 40-digit network-order hexadecimal asset identifier.</summary>
    public static ExternalAssetId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (hex.Length != Length * 2)
            throw new FormatException(
                $"foreign asset hex length {hex.Length} != {Length * 2}");

        return new ExternalAssetId(Convert.FromHexString(hex));
    }

    /// <summary>Whether this identifier denotes the EVM chain's native asset.</summary>
    public bool IsNative => Equals(Native);

    /// <summary>Copy the opaque network-order bytes into a destination span.</summary>
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < Length)
            throw new ArgumentException(
                $"destination length {destination.Length} < {Length}", nameof(destination));
        _networkBytes.CopyTo(destination);
    }

    /// <inheritdoc />
    public bool Equals(ExternalAssetId? other) =>
        other is not null && _networkBytes.AsSpan().SequenceEqual(other._networkBytes);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in _networkBytes)
            hash.Add(value);
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() => $"0x{Convert.ToHexStringLower(_networkBytes)}";
}
