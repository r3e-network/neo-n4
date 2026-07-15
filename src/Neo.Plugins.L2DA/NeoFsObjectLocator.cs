using System.Text;
using Neo.Cryptography;

namespace Neo.Plugins.L2;

/// <summary>
/// Canonical NeoFS object address containing the Base58 container and object identifiers.
/// </summary>
/// <remarks>
/// See doc.md §7.4 and §12. The protocol form is exactly
/// <c>{containerId}/{objectId}</c>, matching the NeoFS SDK object-address encoding.
/// </remarks>
public sealed record NeoFsObjectLocator
{
    private const int IdentifierBytes = 32;
    private const int MaxIdentifierCharacters = 44;
    private const int MaxProtocolAddressBytes = 128;
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    /// <summary>Construct a validated NeoFS object address.</summary>
    public NeoFsObjectLocator(string containerId, string objectId)
    {
        ContainerId = ValidateIdentifier(containerId, nameof(containerId));
        ObjectId = ValidateIdentifier(objectId, nameof(objectId));
    }

    /// <summary>NeoFS container identifier in canonical Base58 form.</summary>
    public string ContainerId { get; }

    /// <summary>NeoFS object identifier in canonical Base58 form.</summary>
    public string ObjectId { get; }

    /// <summary>NeoFS protocol address in <c>container/object</c> form.</summary>
    public string ProtocolAddress => $"{ContainerId}/{ObjectId}";

    /// <summary>Encode the protocol address for <see cref="Neo.L2.DAReceipt.Pointer"/>.</summary>
    public byte[] ToPointer() => Encoding.UTF8.GetBytes(ProtocolAddress);

    /// <summary>Decode and validate a protocol address from a DA receipt pointer.</summary>
    public static bool TryParsePointer(ReadOnlySpan<byte> pointer, out NeoFsObjectLocator? locator)
    {
        locator = null;
        if (pointer.IsEmpty || pointer.Length > MaxProtocolAddressBytes) return false;

        string address;
        try
        {
            address = s_strictUtf8.GetString(pointer);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var delimiter = address.IndexOf('/');
        if (delimiter <= 0 || delimiter != address.LastIndexOf('/') || delimiter == address.Length - 1)
            return false;

        try
        {
            locator = new NeoFsObjectLocator(address[..delimiter], address[(delimiter + 1)..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static string ValidateContainerId(string containerId)
        => ValidateIdentifier(containerId, nameof(containerId));

    private static string ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaxIdentifierCharacters)
            throw new ArgumentException("NeoFS identifier is longer than a 32-byte Base58 value", parameterName);

        byte[] decoded;
        try
        {
            decoded = Base58.Decode(value);
        }
        catch (FormatException error)
        {
            throw new ArgumentException("NeoFS identifier must use canonical Base58 encoding", parameterName, error);
        }

        if (decoded.Length != IdentifierBytes || decoded.AsSpan().IndexOfAnyExcept((byte)0) < 0)
            throw new ArgumentException("NeoFS identifier must encode a non-zero 32-byte value", parameterName);
        if (!string.Equals(Base58.Encode(decoded), value, StringComparison.Ordinal))
            throw new ArgumentException("NeoFS identifier is not canonically encoded", parameterName);
        return value;
    }
}
