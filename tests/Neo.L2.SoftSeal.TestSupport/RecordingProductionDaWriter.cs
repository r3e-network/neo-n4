using System.Collections.Concurrent;
using Neo.Cryptography;

namespace Neo.L2.SoftSeal.TestSupport;

/// <summary>
/// Shared in-process <see cref="IProductionDAWriter"/> for offline Zk host tests.
/// Satisfies the production-DA marker type without funded NeoFS/L1 credentials.
/// Not a mainnet DA backend — operator production still injects NeoFS/L1 writers.
/// </summary>
/// <remarks>
/// Replaces per-file <c>StubProductionDaWriter</c> clones that either no-op-throw or
/// reimplement the same dictionary publish path. SoftSeal multi-cycle still requires
/// Multisig/Optimistic local DA (<c>SupportsLocalDaReader=true</c>).
/// </remarks>
public sealed class RecordingProductionDaWriter : IProductionDAWriter
{
    private readonly ConcurrentDictionary<UInt256, byte[]> _payloads = new();

    public RecordingProductionDaWriter(
        DAMode mode = DAMode.L1,
        DAReceiptKind receiptKind = DAReceiptKind.L1Transaction)
    {
        Mode = mode;
        ReceiptKind = receiptKind;
    }

    /// <inheritdoc />
    public DAMode Mode { get; }

    /// <inheritdoc />
    public DAReceiptKind ReceiptKind { get; }

    /// <summary>Number of successful publishes (offline test pin).</summary>
    public int PublishCount { get; private set; }

    /// <inheritdoc />
    public ValueTask<DAReceipt> PublishAsync(
        DAPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = request.Payload.ToArray();
        var commitment = new UInt256(Crypto.Hash256(payload));
        _payloads[commitment] = payload;
        PublishCount++;

        return ValueTask.FromResult(new DAReceipt
        {
            Commitment = commitment,
            Pointer = commitment.GetSpan().ToArray(),
            Evidence = new byte[] { (byte)'r', (byte)'e', (byte)'c', (byte)Mode },
            Kind = ReceiptKind,
            Layer = Mode,
        });
    }

    /// <inheritdoc />
    public ValueTask<bool> IsAvailableAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(receipt.Commitment);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_payloads.ContainsKey(receipt.Commitment));
    }

    /// <summary>Read back a published payload when present (offline assert helper).</summary>
    public bool TryGetPayload(UInt256 commitment, out ReadOnlyMemory<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        if (_payloads.TryGetValue(commitment, out var bytes))
        {
            payload = bytes;
            return true;
        }

        payload = default;
        return false;
    }
}
