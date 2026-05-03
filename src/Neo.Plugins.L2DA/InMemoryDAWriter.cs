using System.Collections.Concurrent;
using Neo.Cryptography;
using Neo.L2;

namespace Neo.Plugins.L2;

/// <summary>
/// Default in-process <see cref="IDAWriter"/> for tests and devnet boot. Hashes the payload
/// to produce the commitment; retains the bytes in a concurrent dictionary so
/// <see cref="IsAvailableAsync"/> can answer truthfully.
/// </summary>
/// <remarks>
/// NOT for production. Production deployments register one of:
/// <see cref="L1DAWriter"/>, <see cref="NeoFSDAWriter"/>, <see cref="ExternalDAWriter"/>,
/// or <see cref="DACDAWriter"/>.
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

/// <summary>L1 DA: publishes batch payload directly to the Neo N3 chain. Highest cost, highest security.</summary>
public sealed class L1DAWriter : IDAWriter
{
    /// <inheritdoc />
    public DAMode Mode => DAMode.L1;

    /// <inheritdoc />
    public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("L1DAWriter requires an RpcClient and signed L1 transaction; wire in production.");

    /// <inheritdoc />
    public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("L1DAWriter requires an RpcClient lookup against L1.");
}

/// <summary>NeoFS DA: stores batch payload in NeoFS, records the object commitment on L1.</summary>
public sealed class NeoFSDAWriter : IDAWriter
{
    /// <inheritdoc />
    public DAMode Mode => DAMode.NeoFS;

    /// <inheritdoc />
    public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("NeoFSDAWriter requires NeoFS client; wire in production.");

    /// <inheritdoc />
    public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("NeoFSDAWriter requires NeoFS object lookup.");
}

/// <summary>External DA: Celestia, EigenDA, or any layer that exposes blob commitments.</summary>
public sealed class ExternalDAWriter : IDAWriter
{
    /// <inheritdoc />
    public DAMode Mode => DAMode.External;

    /// <inheritdoc />
    public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("ExternalDAWriter requires a client to the chosen DA layer.");

    /// <inheritdoc />
    public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("ExternalDAWriter requires DA-layer-specific availability check.");
}

/// <summary>DAC: a fixed set of signers attest to availability. Lowest cost, highest risk.</summary>
public sealed class DACDAWriter : IDAWriter
{
    /// <inheritdoc />
    public DAMode Mode => DAMode.DAC;

    /// <inheritdoc />
    public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("DACDAWriter requires committee signing protocol; wire in production.");

    /// <inheritdoc />
    public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("DACDAWriter requires committee attestation lookup.");
}
