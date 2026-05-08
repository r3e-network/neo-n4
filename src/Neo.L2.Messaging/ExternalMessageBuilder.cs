namespace Neo.L2.Messaging;

/// <summary>
/// Helper for building <see cref="ExternalCrossChainMessage"/> instances with the
/// canonical <see cref="ExternalCrossChainMessage.MessageHash"/> filled in. Mirrors
/// <see cref="MessageBuilder"/> for internal Neo cross-chain messages.
/// </summary>
public static class ExternalMessageBuilder
{
    /// <summary>Build an <see cref="ExternalCrossChainMessage"/> and populate
    /// <see cref="ExternalCrossChainMessage.MessageHash"/> with the canonical
    /// hash from <see cref="ExternalMessageHasher"/>.</summary>
    public static ExternalCrossChainMessage Build(
        uint externalChainId,
        uint neoChainId,
        ulong nonce,
        ExternalBridgeDirection direction,
        UInt160 sender,
        UInt160 recipient,
        ulong deadlineUnixSeconds,
        UInt256 sourceTxRef,
        ExternalMessageType messageType,
        ReadOnlyMemory<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(sourceTxRef);

        // The 0xE0 prefix is the foreign-namespace gate. A caller passing a
        // non-prefixed externalChainId is almost certainly mixing it up with a
        // Neo L2 chainId — surface the misconfig now rather than letting it
        // silently route to the wrong verifier.
        if ((externalChainId & 0xFF00_0000U) != 0xE000_0000U)
            throw new ArgumentException(
                $"externalChainId 0x{externalChainId:X8} must use the 0xE0_xx_xx_xx foreign-namespace prefix " +
                "(see Neo.L2.ExternalCrossChainMessage docs for the assigned ranges)",
                nameof(externalChainId));

        // Direction must be one of the two enum values; reject e.g. (byte)0
        // which would otherwise produce an unhashable record.
        if (direction is not (ExternalBridgeDirection.NeoToForeign or ExternalBridgeDirection.ForeignToNeo))
            throw new ArgumentException(
                $"direction must be NeoToForeign(1) or ForeignToNeo(2), got {(byte)direction}",
                nameof(direction));

        var unhashed = new ExternalCrossChainMessage
        {
            ExternalChainId = externalChainId,
            NeoChainId = neoChainId,
            Nonce = nonce,
            Direction = direction,
            Sender = sender,
            Recipient = recipient,
            DeadlineUnixSeconds = deadlineUnixSeconds,
            SourceTxRef = sourceTxRef,
            MessageType = messageType,
            Payload = payload,
            MessageHash = UInt256.Zero,
        };
        var hash = ExternalMessageHasher.HashMessage(unhashed);
        return unhashed with { MessageHash = hash };
    }
}
