using System.Buffers.Binary;
using Neo.Cryptography;

namespace Neo.L2.Executor.Receipts;

/// <summary>
/// Per-transaction execution receipt. Goes into <see cref="BatchExecutionResult.ReceiptRoot"/>.
/// </summary>
/// <remarks>
/// A failed transaction produces a receipt with <see cref="Success"/>=false; failures still
/// participate in the receipt root (matches Neo L1 semantics) so the prover can reproduce them.
/// </remarks>
public sealed record Receipt
{
    /// <summary>Hash of the transaction this receipt is for.</summary>
    public required UInt256 TxHash { get; init; }

    /// <summary>True if execution halted normally (VMState.HALT in Neo terms).</summary>
    public required bool Success { get; init; }

    /// <summary>Total gas consumed by the transaction.</summary>
    public required long GasConsumed { get; init; }

    /// <summary>Hash committing to the executed contract's storage delta.</summary>
    public required UInt256 StorageDeltaHash { get; init; }

    /// <summary>Hash committing to all events emitted by the transaction.</summary>
    public required UInt256 EventsHash { get; init; }

    /// <summary>Compute the canonical leaf hash for this receipt.</summary>
    public UInt256 Hash()
    {
        Span<byte> buffer = stackalloc byte[32 + 1 + 8 + 32 + 32];
        var pos = 0;
        TxHash.GetSpan().CopyTo(buffer.Slice(pos, 32)); pos += 32;
        buffer[pos++] = (byte)(Success ? 1 : 0);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(pos, 8), GasConsumed); pos += 8;
        StorageDeltaHash.GetSpan().CopyTo(buffer.Slice(pos, 32)); pos += 32;
        EventsHash.GetSpan().CopyTo(buffer.Slice(pos, 32));
        return new UInt256(Crypto.Hash256(buffer));
    }
}
