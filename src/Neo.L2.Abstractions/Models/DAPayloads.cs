namespace Neo.L2;

/// <summary>
/// Bytes the DA writer is asked to publish. The writer wraps these in whatever envelope the
/// chosen DA layer expects.
/// </summary>
public sealed record DAPublishRequest
{
    /// <summary>L2 chain identifier.</summary>
    public required uint ChainId { get; init; }

    /// <summary>Batch number this payload belongs to.</summary>
    public required ulong BatchNumber { get; init; }

    /// <summary>Compressed batch payload (typically the ordered transaction blob).</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }

    /// <inheritdoc />
    public bool Equals(DAPublishRequest? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ChainId == other.ChainId
            && BatchNumber == other.BatchNumber
            && Payload.Span.SequenceEqual(other.Payload.Span);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ChainId);
        hash.Add(BatchNumber);
        hash.AddBytes(Payload.Span);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Receipt returned by <see cref="IDAWriter.PublishAsync"/>. Whatever DA layer is in use must
/// produce a content commitment that fits in <c>UInt256</c>; longer pointers go in
/// <see cref="Pointer"/>.
/// </summary>
public sealed record DAReceipt
{
    /// <summary>Commitment placed in <see cref="L2BatchCommitment.DACommitment"/>.</summary>
    public required UInt256 Commitment { get; init; }

    /// <summary>Layer-specific pointer / locator (e.g. NeoFS object id, Celestia commitment).</summary>
    public required ReadOnlyMemory<byte> Pointer { get; init; }

    /// <summary>The DA layer that was actually used.</summary>
    public required DAMode Layer { get; init; }

    /// <inheritdoc />
    public bool Equals(DAReceipt? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Layer == other.Layer
            && Commitment.Equals(other.Commitment)
            && Pointer.Span.SequenceEqual(other.Pointer.Span);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Layer);
        hash.Add(Commitment);
        hash.AddBytes(Pointer.Span);
        return hash.ToHashCode();
    }
}
