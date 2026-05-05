using System.Collections.Concurrent;
using Neo.Cryptography;
using Neo.L2;

namespace Neo.Plugins.L2;

/// <summary>
/// Default in-process <see cref="IDAWriter"/> for tests, devnets, and single-node demos.
/// Hashes the payload to produce the commitment; retains the bytes in a concurrent
/// dictionary so <see cref="IsAvailableAsync"/> can answer truthfully.
/// </summary>
/// <remarks>
/// Wired by <see cref="L2DAPlugin"/> when <see cref="DAMode.External"/> is configured
/// without an operator-provided override. Production with real distributed DA registers
/// a custom <see cref="IDAWriter"/> via <c>L2DAPlugin.WithWriter</c> (e.g. an L1-RPC-backed
/// implementation, a real NeoFS SDK adapter, or a DAC committee-attestation writer).
/// </remarks>
public sealed class InMemoryDAWriter : IDAWriter
{
    private readonly ConcurrentDictionary<UInt256, byte[]> _store = new();

    /// <inheritdoc />
    public DAMode Mode => DAMode.External;

    /// <inheritdoc />
    public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var commitment = new UInt256(Crypto.Hash256(request.Payload.Span));
        _store[commitment] = request.Payload.ToArray();

        return new ValueTask<DAReceipt>(new DAReceipt
        {
            Commitment = commitment,
            Pointer = ReadOnlyMemory<byte>.Empty,
            Layer = Mode,
        });
    }

    /// <inheritdoc />
    public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(receipt);
        // Commitment is UInt256 (reference type); `required` doesn't prevent null.
        // Without this guard, ContainsKey(null) throws ArgumentNullException with a
        // generic "key" message. Same iter-148/183/184 pattern.
        ArgumentNullException.ThrowIfNull(receipt.Commitment);
        return new ValueTask<bool>(_store.ContainsKey(receipt.Commitment));
    }
}

