namespace Neo.L2;

/// <summary>
/// A single cross-chain message routed through Neo Connect.
/// </summary>
/// <remarks>
/// See doc.md §3.2 (MessageRouter) and §10 (Neo Connect).
/// </remarks>
public sealed record CrossChainMessage
{
    /// <summary>Identifier of the chain that produced the message. <c>0</c> denotes Neo L1.</summary>
    public required uint SourceChainId { get; init; }

    /// <summary>Identifier of the chain that should consume the message. <c>0</c> denotes Neo L1.</summary>
    public required uint TargetChainId { get; init; }

    /// <summary>Per-source-chain monotonic nonce — replay protection lives here.</summary>
    public required ulong Nonce { get; init; }

    /// <summary>Address that emitted the message on the source chain.</summary>
    public required UInt160 Sender { get; init; }

    /// <summary>Address that should receive the message on the target chain.</summary>
    public required UInt160 Receiver { get; init; }

    /// <summary>Message kind (deposit / withdraw / call / event / governance).</summary>
    public required MessageType MessageType { get; init; }

    /// <summary>Message body. Interpretation depends on <see cref="MessageType"/>.</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>Hash committing to all the other fields, used for inclusion proofs.</summary>
    public required UInt256 MessageHash { get; init; }

    /// <inheritdoc />
    public bool Equals(CrossChainMessage? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SourceChainId == other.SourceChainId
            && TargetChainId == other.TargetChainId
            && Nonce == other.Nonce
            && Sender.Equals(other.Sender)
            && Receiver.Equals(other.Receiver)
            && MessageType == other.MessageType
            && Payload.Span.SequenceEqual(other.Payload.Span)
            && MessageHash.Equals(other.MessageHash);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SourceChainId);
        hash.Add(TargetChainId);
        hash.Add(Nonce);
        hash.Add(Sender);
        hash.Add(Receiver);
        hash.Add(MessageType);
        hash.AddBytes(Payload.Span);
        hash.Add(MessageHash);
        return hash.ToHashCode();
    }
}
