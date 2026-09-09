using Neo.L2.Batch;
using Neo.Json;

namespace Neo.L2.Settlement.Rpc;

/// <summary>
/// <see cref="ISettlementClient"/> implementation that calls NeoHub RollupHub on L1 over JSON-RPC.
/// </summary>
/// <remarks>
/// Read-only methods (<see cref="GetCanonicalStateRootAsync"/>, <see cref="GetBatchStatusAsync"/>)
/// are wired via Neo's standard <c>invokefunction</c> RPC. Submit methods require building + signing
/// a Neo transaction and sending via <c>sendrawtransaction</c>; this class accepts a delegate so
/// production code can plug in its own signer / wallet.
/// See doc.md §3.2 (RollupHub / former SettlementManager) and §15.1.
/// </remarks>
public sealed class RpcSettlementClient
    : ISettlementClient, ISettlementTransactionStatusClient, IDisposable
{
    /// <summary>Delegate for signing+sending a RollupHub settlement transaction.</summary>
    /// <remarks>
    /// <paramref name="method"/> is <c>submitBatch</c> or <c>submitAndFinalizeBatch</c>.
    /// The contract binds <c>publicInputHash</c> from the commitment plus
    /// <paramref name="l1MessageHash"/>, <paramref name="blockContextHash"/>, and
    /// <paramref name="forcedInclusionCount"/>.
    /// </remarks>
    public delegate ValueTask<UInt256> SignAndSendAsync(
        UInt160 rollupHubHash,
        string method,
        byte[] commitmentBytes,
        byte[] l1MessageHash,
        byte[] blockContextHash,
        uint forcedInclusionCount,
        CancellationToken cancellationToken);

    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _rollupHubHash;
    private readonly SignAndSendAsync _signAndSend;
    private int _disposed;

    /// <summary>Construct.</summary>
    public RpcSettlementClient(
        JsonRpcClient rpc,
        UInt160 rollupHubHash,
        SignAndSendAsync signAndSend)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(signAndSend);
        _rpc = rpc;
        _rollupHubHash = rollupHubHash;
        _signAndSend = signAndSend;
    }

    /// <inheritdoc />
    public ValueTask<UInt256> SubmitBatchAsync(
        L2BatchCommitment commitment,
        PublicInputs publicInputs,
        CancellationToken cancellationToken = default)
        => SubmitAsync("submitBatch", commitment, publicInputs, cancellationToken);

    /// <inheritdoc />
    public ValueTask<UInt256> SubmitAndFinalizeBatchAsync(
        L2BatchCommitment commitment,
        PublicInputs publicInputs,
        CancellationToken cancellationToken = default)
        => SubmitAsync("submitAndFinalizeBatch", commitment, publicInputs, cancellationToken);

    private async ValueTask<UInt256> SubmitAsync(
        string method,
        L2BatchCommitment commitment,
        PublicInputs publicInputs,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(publicInputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var bytes = BatchSerializer.Encode(commitment);
        var l1MessageHash = publicInputs.L1MessageHash.GetSpan().ToArray();
        var blockContextHash = publicInputs.BlockContextHash.GetSpan().ToArray();
        var txHash = await _signAndSend(
                _rollupHubHash,
                method,
                bytes,
                l1MessageHash,
                blockContextHash,
                publicInputs.ForcedInclusionCount,
                cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "RpcSettlementClient.SignAndSendAsync delegate returned null tx hash");
        return txHash;
    }

    /// <inheritdoc />
    public async ValueTask<UInt256> GetCanonicalStateRootAsync(uint chainId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var result = await RpcContractReader.InvokeReadAsync(_rpc, _rollupHubHash, "getCanonicalStateRoot", new object[] { chainId }, cancellationToken).ConfigureAwait(false);
        return RpcContractReader.ParseUInt256(result);
    }

    /// <inheritdoc />
    public async ValueTask<BatchStatus> GetBatchStatusAsync(uint chainId, ulong batchNumber, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var result = await RpcContractReader.InvokeReadAsync(_rpc, _rollupHubHash, "getBatchStatus", new object[] { chainId, batchNumber }, cancellationToken).ConfigureAwait(false);
        var byteValue = RpcContractReader.ParseInteger(result);
        if (byteValue < 0 || byteValue > 4)
            throw new InvalidOperationException($"unexpected status byte {byteValue}");
        return (BatchStatus)byteValue;
    }

    /// <inheritdoc />
    public async ValueTask<SettlementTransactionStatus> GetTransactionStatusAsync(
        UInt256 transactionHash,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(transactionHash);
        try
        {
            var result = await _rpc.CallAsync(
                "getrawtransaction",
                new JArray { transactionHash.ToString(), true },
                cancellationToken).ConfigureAwait(false);
            if (result is not JObject transaction)
                throw new InvalidOperationException(
                    "getrawtransaction returned a non-object verbose result");
            return transaction["blockhash"] is JString blockHash
                && !string.IsNullOrWhiteSpace(blockHash.AsString())
                    ? SettlementTransactionStatus.Confirmed
                    : SettlementTransactionStatus.Pending;
        }
        catch (JsonRpcException exception) when (IsUnknownTransaction(exception))
        {
            return SettlementTransactionStatus.Unknown;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _rpc.Dispose();
    }

    private static bool IsUnknownTransaction(JsonRpcException exception)
        => exception.Code is -100 or -105
            || exception.Message.Contains(
                "Unknown transaction", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
