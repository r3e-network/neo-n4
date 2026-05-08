namespace Neo.L2;

/// <summary>
/// A cross-chain message bound for or arriving from a foreign chain
/// (Ethereum / Tron / Solana / etc.) — the input to <c>NeoHub.ExternalBridge</c>.
/// </summary>
/// <remarks>
/// Distinct from <see cref="CrossChainMessage"/>, which routes between Neo L1
/// and Neo L2s (or between Neo L2s) — those share Neo's finality model. This
/// record is for messages that cross into a foreign chain's finality model and
/// therefore require an explicit verifier (MPC committee / optimistic / ZK
/// light client). See <c>doc.md</c> §11.3 and
/// <c>docs/external-bridge-roadmap.md</c>.
/// </remarks>
public sealed record ExternalCrossChainMessage
{
    /// <summary>
    /// Foreign chain identifier. The <c>0xE0</c> high-byte prefix reserves a
    /// namespace disjoint from Neo L2 chainIds (which start at 1):
    /// <list type="bullet">
    /// <item><description><c>0xE0_00_00_01</c> — Ethereum mainnet</description></item>
    /// <item><description><c>0xE0_00_00_02</c> — Ethereum Sepolia</description></item>
    /// <item><description><c>0xE0_00_00_10</c> — Tron mainnet</description></item>
    /// <item><description><c>0xE0_00_00_20</c> — Solana mainnet-beta</description></item>
    /// </list>
    /// </summary>
    public required uint ExternalChainId { get; init; }

    /// <summary>Target Neo L2 chainId (or <c>0</c> for Neo L1).</summary>
    public required uint NeoChainId { get; init; }

    /// <summary>Per-(externalChainId, direction) monotonic nonce — replay protection.</summary>
    public required ulong Nonce { get; init; }

    /// <summary>Direction the message flows: 1 = Neo→Foreign, 2 = Foreign→Neo.</summary>
    public required ExternalBridgeDirection Direction { get; init; }

    /// <summary>Sender on the source chain (UInt160 on Neo side; for foreign-side
    /// senders, last 20 bytes of the foreign address — natural for Eth/Tron,
    /// packed for Solana).</summary>
    public required UInt160 Sender { get; init; }

    /// <summary>Recipient on the destination chain (same encoding as
    /// <see cref="Sender"/>).</summary>
    public required UInt160 Recipient { get; init; }

    /// <summary>Unix-seconds deadline; <c>0</c> = no deadline.</summary>
    public required ulong DeadlineUnixSeconds { get; init; }

    /// <summary>Source-chain transaction reference (Eth tx hash / Tron tx hash /
    /// Solana signature truncated). Stored as <see cref="UInt256"/> regardless of
    /// the foreign chain's native hash type — verifiers know how to interpret
    /// the bytes for their target.</summary>
    public required UInt256 SourceTxRef { get; init; }

    /// <summary>What the payload represents — asset transfer, contract call, or
    /// both.</summary>
    public required ExternalMessageType MessageType { get; init; }

    /// <summary>Message body. Interpretation depends on
    /// <see cref="MessageType"/>.</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>Hash committing to all other fields. Computed by
    /// <c>ExternalMessageHasher.HashMessage</c>.</summary>
    public required UInt256 MessageHash { get; init; }

    /// <inheritdoc />
    public bool Equals(ExternalCrossChainMessage? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ExternalChainId == other.ExternalChainId
               && NeoChainId == other.NeoChainId
               && Nonce == other.Nonce
               && Direction == other.Direction
               && Sender.Equals(other.Sender)
               && Recipient.Equals(other.Recipient)
               && DeadlineUnixSeconds == other.DeadlineUnixSeconds
               && SourceTxRef.Equals(other.SourceTxRef)
               && MessageType == other.MessageType
               && Payload.Span.SequenceEqual(other.Payload.Span)
               && MessageHash.Equals(other.MessageHash);
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        ExternalChainId, NeoChainId, Nonce, (byte)Direction, Sender, Recipient, MessageHash);
}

/// <summary>Direction of an <see cref="ExternalCrossChainMessage"/>.</summary>
public enum ExternalBridgeDirection : byte
{
    /// <summary>Message originated on a Neo chain and is bound for a foreign chain.</summary>
    NeoToForeign = 1,

    /// <summary>Message originated on a foreign chain and is bound for a Neo chain.</summary>
    ForeignToNeo = 2,
}

/// <summary>Type of payload carried by an <see cref="ExternalCrossChainMessage"/>.</summary>
public enum ExternalMessageType : byte
{
    /// <summary>Pure asset transfer: payload is an
    /// <c>ExternalAssetTransferPayload</c>.</summary>
    AssetTransfer = 0,

    /// <summary>Pure contract call: payload is an arbitrary calldata blob the
    /// recipient handles.</summary>
    Call = 1,

    /// <summary>Asset transfer plus a follow-up call. Payload concatenates the
    /// asset-transfer header followed by calldata.</summary>
    AssetAndCall = 2,
}
