using System.Collections.Concurrent;
using Neo.Cryptography;
using Neo.L2;

namespace Neo.Plugins.L2;

/// <summary>
/// Content-addressed in-process DA writer that mimics NeoFS object semantics: per-chain
/// container, object id = SHA256(payload), retrievable by id. Useful as a model for the real
/// NeoFS client and as a deterministic test backend that survives restarts via the optional
/// snapshot file.
/// </summary>
/// <remarks>
/// Production deployments register a custom <see cref="IDAWriter"/> wired to a NeoFS SDK
/// via <c>L2DAPlugin.WithWriter</c>.
/// The on-chain commitment recorded in <c>NeoHub.DARegistry</c> is the object id (32 bytes),
/// matching what the real NeoFS implementation will produce. See doc.md §12.2 (NeoFS DA).
/// </remarks>
public sealed class NeoFsLikeDAWriter : IDAWriter
{
    private readonly ConcurrentDictionary<(uint chainId, UInt256 id), byte[]> _store = new();

    /// <inheritdoc />
    public DAMode Mode => DAMode.NeoFS;

    /// <summary>Number of objects retained across all chains (test inspection helper).</summary>
    public int ObjectCount => _store.Count;

    /// <inheritdoc />
    public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var objectId = new UInt256(Crypto.Hash256(request.Payload.Span));
        // Idempotent: same payload → same id; multiple writes are no-ops.
        _store[(request.ChainId, objectId)] = request.Payload.ToArray();

        // Pointer encodes (4B chainId LE) + 32B objectId — what the real NeoFS would surface
        // as a "container/object" composite address.
        var pointer = new byte[4 + 32];
        pointer[0] = (byte)request.ChainId;
        pointer[1] = (byte)(request.ChainId >> 8);
        pointer[2] = (byte)(request.ChainId >> 16);
        pointer[3] = (byte)(request.ChainId >> 24);
        objectId.GetSpan().CopyTo(pointer.AsSpan(4));

        return new ValueTask<DAReceipt>(new DAReceipt
        {
            Commitment = objectId,
            Pointer = pointer,
            Layer = Mode,
        });
    }

    /// <inheritdoc />
    public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(receipt.Commitment);

        if (receipt.Pointer.Length != 36)
            return new ValueTask<bool>(false);

        var span = receipt.Pointer.Span;
        var chainId = (uint)span[0]
            | ((uint)span[1] << 8)
            | ((uint)span[2] << 16)
            | ((uint)span[3] << 24);
        return new ValueTask<bool>(_store.ContainsKey((chainId, receipt.Commitment)));
    }

    /// <summary>Retrieve a previously published payload by chain + object id (test/debug helper).</summary>
    /// <remarks>
    /// Defensive copy: the store retains the original <c>byte[]</c> reference; without
    /// the per-call clone, a debug consumer that mutated the returned bytes would
    /// silently corrupt the store. Same iter-176 pattern as
    /// <c>KeyedStateStore.EnumerateSorted</c>.
    /// </remarks>
    public ReadOnlyMemory<byte>? TryGet(uint chainId, UInt256 objectId)
    {
        ArgumentNullException.ThrowIfNull(objectId);
        return _store.TryGetValue((chainId, objectId), out var bytes)
            ? (ReadOnlyMemory<byte>?)(byte[])bytes.Clone()
            : null;
    }

    /// <summary>Drop everything (test-only convenience).</summary>
    public void Clear() => _store.Clear();
}
