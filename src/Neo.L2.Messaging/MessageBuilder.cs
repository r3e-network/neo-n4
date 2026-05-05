using Neo.L2.State;

namespace Neo.L2.Messaging;

/// <summary>
/// Helper for building <see cref="CrossChainMessage"/> instances with the canonical
/// <see cref="CrossChainMessage.MessageHash"/> filled in.
/// </summary>
public static class MessageBuilder
{
    /// <summary>Build a <see cref="CrossChainMessage"/> and compute its canonical hash.</summary>
    public static CrossChainMessage Build(
        uint sourceChainId,
        uint targetChainId,
        ulong nonce,
        UInt160 sender,
        UInt160 receiver,
        MessageType messageType,
        ReadOnlyMemory<byte> payload)
    {
        // UInt160 is a reference type in Neo's library; null would crash inside
        // MessageHasher.HashMessage's GetSpan() with no link back to the bad caller.
        // Reject at the boundary so the operator sees which argument was wrong.
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(receiver);
        // Self-routed messages have no transport: there is no L1→L1 or L2→L2 route
        // when source == target. Surface the misconfig at build time, not later when
        // the gateway silently drops the message.
        if (sourceChainId == targetChainId)
            throw new ArgumentException(
                $"sourceChainId == targetChainId == {sourceChainId} — self-routed messages have no cross-chain transport",
                nameof(targetChainId));
        // Build a temporary message with placeholder hash, then compute the real hash.
        var unhashed = new CrossChainMessage
        {
            SourceChainId = sourceChainId,
            TargetChainId = targetChainId,
            Nonce = nonce,
            Sender = sender,
            Receiver = receiver,
            MessageType = messageType,
            Payload = payload,
            MessageHash = UInt256.Zero,
        };
        var hash = MessageHasher.HashMessage(unhashed);
        return unhashed with { MessageHash = hash };
    }
}
