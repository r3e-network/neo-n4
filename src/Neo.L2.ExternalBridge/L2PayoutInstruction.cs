using Neo.L2.Bridge.External;
using Neo.L2.Messaging;

namespace Neo.L2.ExternalBridge;

/// <summary>Exact durable payout instruction emitted by one immutable L1 adapter.</summary>
/// <remarks>See <c>doc.md</c> §11.3.</remarks>
public sealed record L2PayoutInstruction
{
    /// <summary>Monotonic adapter queue sequence.</summary>
    public required ulong Sequence { get; init; }

    /// <summary>Authenticated L1 adapter contract hash that emitted the instruction.</summary>
    public required UInt160 Adapter { get; init; }

    /// <summary>Mapped target-L2 asset selected by the immutable escrow route.</summary>
    public required UInt160 NeoAsset { get; init; }

    /// <summary>Decoded canonical signed message, including its recomputed <c>Hash256</c>.</summary>
    public required ExternalCrossChainMessage Message { get; init; }

    /// <summary>Foreign asset decoded from the canonical asset-transfer payload.</summary>
    public required ExternalAssetId ForeignAsset { get; init; }

    /// <summary>Amount decoded from the canonical asset-transfer payload.</summary>
    public required System.Numerics.BigInteger Amount { get; init; }

    /// <summary>Exact signed bytes, without any relay-local envelope.</summary>
    public required ReadOnlyMemory<byte> CanonicalMessageBytes { get; init; }

    /// <summary>Construct and fully validate an instruction read from an adapter event.</summary>
    public static L2PayoutInstruction Decode(
        ulong sequence,
        UInt160 adapter,
        UInt160 neoAsset,
        UInt256 eventMessageHash,
        ReadOnlyMemory<byte> canonicalMessageBytes,
        uint expectedNeoChainId)
    {
        if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(neoAsset);
        ArgumentNullException.ThrowIfNull(eventMessageHash);
        if (adapter == UInt160.Zero)
            throw new ArgumentException("Adapter must not be zero.", nameof(adapter));
        if (neoAsset == UInt160.Zero)
            throw new ArgumentException("Mapped Neo asset must not be zero.", nameof(neoAsset));
        if (expectedNeoChainId == 0)
            throw new ArgumentOutOfRangeException(nameof(expectedNeoChainId));

        var message = ExternalMessageHasher.DecodeCanonical(canonicalMessageBytes.Span);
        if (message.MessageHash != eventMessageHash)
            throw new InvalidDataException("Adapter event message hash does not match canonical bytes.");
        if (message.NeoChainId != expectedNeoChainId)
            throw new InvalidDataException(
                $"Payout targets Neo chain {message.NeoChainId}, expected {expectedNeoChainId}.");
        if (message.Direction != ExternalBridgeDirection.ForeignToNeo)
            throw new InvalidDataException("Payout direction must be foreign-to-Neo.");
        if (message.MessageType != ExternalMessageType.AssetTransfer)
            throw new InvalidDataException("L2 payout relay supports asset-transfer messages only.");
        if (message.Recipient == UInt160.Zero)
            throw new InvalidDataException("Payout recipient must not be zero.");
        if (message.SourceTxRef == UInt256.Zero)
            throw new InvalidDataException("Payout source transaction must not be zero.");

        ExternalAssetTransferPayload payload;
        try
        {
            payload = ExternalAssetTransferPayload.Decode(message.Payload.Span);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("Payout asset payload is not canonical.", exception);
        }

        return new L2PayoutInstruction
        {
            Sequence = sequence,
            Adapter = adapter,
            NeoAsset = neoAsset,
            Message = message,
            ForeignAsset = payload.ForeignAsset,
            Amount = payload.Amount,
            CanonicalMessageBytes = canonicalMessageBytes.ToArray(),
        };
    }

    /// <inheritdoc />
    public bool Equals(L2PayoutInstruction? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Sequence == other.Sequence
            && Adapter == other.Adapter
            && NeoAsset == other.NeoAsset
            && Message == other.Message
            && ForeignAsset == other.ForeignAsset
            && Amount == other.Amount
            && CanonicalMessageBytes.Span.SequenceEqual(other.CanonicalMessageBytes.Span);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Sequence);
        hash.Add(Adapter);
        hash.Add(NeoAsset);
        hash.Add(Message);
        hash.Add(ForeignAsset);
        hash.Add(Amount);
        hash.AddBytes(CanonicalMessageBytes.Span);
        return hash.ToHashCode();
    }
}
