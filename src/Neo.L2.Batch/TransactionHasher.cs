using Neo.Extensions;
using Neo.Network.P2P.Payloads;

namespace Neo.L2.Batch;

/// <summary>
/// Canonical transaction identifier for Neo L2 batches: the double-SHA256 of the unsigned
/// transaction preimage — version through script, everything before the witness section.
/// </summary>
/// <remarks>
/// See doc.md §7.5 and §8. This is the value Neo N3's <c>Transaction.Hash</c> computes
/// (<c>IVerifiable.SerializeUnsigned</c>) and the value
/// <c>neo-execution-core::transaction::parse_transaction</c> derives, so batch tx roots, receipts,
/// and forced-inclusion leaves stay identical across the native executor, the SP1 guest, and L1
/// consumers. Hashing the full serialized bytes instead produces a different digest for any
/// transaction carrying signatures and silently forks those consumers apart.
/// </remarks>
public static class TransactionHasher
{
    /// <summary>
    /// Hash <paramref name="serializedTx"/> to its canonical transaction id.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The bytes are not a canonical Neo transaction. A transaction the Neo parser rejects is
    /// also rejected by the guest parser, so no canonical id exists for it.
    /// </exception>
    public static UInt256 Hash(ReadOnlyMemory<byte> serializedTx)
    {
        try
        {
            return serializedTx.ToArray().AsSerializable<Transaction>().Hash;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            throw new ArgumentException(
                "transaction bytes are not a canonical Neo transaction", ex);
        }
    }
}
