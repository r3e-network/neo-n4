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

/// <summary>Identifies the backend-specific evidence carried by a <see cref="DAReceipt"/>.</summary>
/// <remarks>
/// See doc.md §7.4 and §12. Receipt kind is deliberately separate from
/// <see cref="DAMode"/> so local durability and semantic simulations cannot be mistaken
/// for public DA publication evidence.
/// </remarks>
public enum DAReceiptKind : byte
{
    /// <summary>Legacy or malformed receipt with no declared evidence format.</summary>
    Unspecified = 0,

    /// <summary>Node-local content-addressed persistence receipt.</summary>
    LocalPersistence = 1,

    /// <summary>Development-only in-process model of a public DA backend.</summary>
    SemanticSimulation = 2,

    /// <summary>Confirmed signed L1 transaction publication.</summary>
    L1Transaction = 3,

    /// <summary>NeoFS container/object locator validated through an independent client.</summary>
    NeoFSObject = 4,

    /// <summary>Provider-specific external DA locator and publication proof.</summary>
    ExternalPublication = 5,

    /// <summary>DAC data locator plus committee availability attestation.</summary>
    DACAttestation = 6,
}

/// <summary>
/// Receipt returned by <see cref="IDAWriter.PublishAsync"/>. Whatever DA layer is in use must
/// produce a content commitment that fits in <c>UInt256</c>; backend locators go in
/// <see cref="Pointer"/> and independently checkable publication metadata goes in
/// <see cref="Evidence"/>.
/// </summary>
public sealed record DAReceipt
{
    /// <summary>Commitment placed in <see cref="L2BatchCommitment.DACommitment"/>.</summary>
    public required UInt256 Commitment { get; init; }

    /// <summary>Layer-specific pointer / locator (e.g. NeoFS object id, Celestia commitment).</summary>
    public required ReadOnlyMemory<byte> Pointer { get; init; }

    /// <summary>
    /// Backend-specific publication metadata or proof. Public DA writers must never emit
    /// an empty value; semantic/local writers use a versioned marker that readers validate.
    /// </summary>
    public ReadOnlyMemory<byte> Evidence { get; init; } = ReadOnlyMemory<byte>.Empty;

    /// <summary>The evidence format carried by <see cref="Pointer"/> and <see cref="Evidence"/>.</summary>
    public DAReceiptKind Kind { get; init; } = DAReceiptKind.Unspecified;

    /// <summary>The DA layer that was actually used.</summary>
    public required DAMode Layer { get; init; }

    /// <summary>
    /// Return whether the receipt has the expected security label, evidence kind, and
    /// non-empty locator/proof metadata. Backend readers still verify the metadata bytes.
    /// </summary>
    public bool HasRequiredMetadata(DAMode expectedLayer, DAReceiptKind expectedKind)
        => Commitment is not null
            && Layer == expectedLayer
            && Kind == expectedKind
            && !Pointer.IsEmpty
            && !Evidence.IsEmpty;

    /// <inheritdoc />
    public bool Equals(DAReceipt? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Layer == other.Layer
            && Kind == other.Kind
            && Commitment.Equals(other.Commitment)
            && Pointer.Span.SequenceEqual(other.Pointer.Span)
            && Evidence.Span.SequenceEqual(other.Evidence.Span);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Layer);
        hash.Add(Kind);
        hash.Add(Commitment);
        hash.AddBytes(Pointer.Span);
        hash.AddBytes(Evidence.Span);
        return hash.ToHashCode();
    }
}
