using Neo.L2.Batch;

namespace Neo.L2.Settlement.Rpc;

/// <summary>
/// <see cref="ISettlementClient"/> implementation that calls NeoHub on L1 over JSON-RPC.
/// </summary>
/// <remarks>
/// Read-only methods (<see cref="GetCanonicalStateRootAsync"/>, <see cref="GetBatchStatusAsync"/>)
/// are wired via Neo's standard <c>invokefunction</c> RPC. <see cref="SubmitBatchAsync"/>
/// requires building + signing a Neo transaction and sending via <c>sendrawtransaction</c>;
/// this class accepts a delegate to do that step so production code can plug in its own
/// signer / wallet without forcing a particular signing dependency on this library.
/// </remarks>
public sealed class RpcSettlementClient : ISettlementClient, IDisposable
{
    /// <summary>Delegate for signing+sending the SubmitBatch transaction.</summary>
    /// <remarks>
    /// The on-chain <c>SettlementManager.SubmitBatch</c> takes three arguments —
    /// <paramref name="commitmentBytes"/> plus <paramref name="l1MessageHash"/> and
    /// <paramref name="blockContextHash"/> — because the contract recomputes and binds the
    /// commitment's <c>publicInputHash</c> from them (these two public inputs are not carried in
    /// the commitment header). The signer MUST forward all three as the contract-call arguments.
    /// </remarks>
    /// <returns>Tx hash returned by <c>sendrawtransaction</c>.</returns>
    public delegate ValueTask<UInt256> SignAndSendAsync(
        UInt160 settlementManagerHash,
        byte[] commitmentBytes,
        byte[] l1MessageHash,
        byte[] blockContextHash,
        CancellationToken cancellationToken);

    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _settlementManagerHash;
    private readonly SignAndSendAsync _signAndSend;
    private bool _disposed;

    /// <summary>Construct.</summary>
    public RpcSettlementClient(
        JsonRpcClient rpc,
        UInt160 settlementManagerHash,
        SignAndSendAsync signAndSend)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(signAndSend);
        _rpc = rpc;
        _settlementManagerHash = settlementManagerHash;
        _signAndSend = signAndSend;
    }

    /// <inheritdoc />
    public async ValueTask<UInt256> SubmitBatchAsync(L2BatchCommitment commitment, PublicInputs publicInputs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(publicInputs);
        var bytes = BatchSerializer.Encode(commitment);
        // The contract binds the commitment's publicInputHash to these two public inputs, which
        // are not part of the commitment header — forward exactly the values used to compute it.
        var l1MessageHash = publicInputs.L1MessageHash.GetSpan().ToArray();
        var blockContextHash = publicInputs.BlockContextHash.GetSpan().ToArray();
        // Defensive: a buggy signAndSend that returns null UInt256 would propagate as a
        // NRE further downstream (e.g. an L1-tracker that dereferences the tx hash).
        // Same iter-171/172/173 callee-contract pattern.
        var txHash = await _signAndSend(_settlementManagerHash, bytes, l1MessageHash, blockContextHash, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "RpcSettlementClient.SignAndSendAsync delegate returned null tx hash");
        return txHash;
    }

    /// <inheritdoc />
    public async ValueTask<UInt256> GetCanonicalStateRootAsync(uint chainId, CancellationToken cancellationToken = default)
    {
        var result = await RpcContractReader.InvokeReadAsync(_rpc, _settlementManagerHash, "getCanonicalStateRoot", new object[] { chainId }, cancellationToken).ConfigureAwait(false);
        return RpcContractReader.ParseUInt256(result);
    }

    /// <inheritdoc />
    public async ValueTask<BatchStatus> GetBatchStatusAsync(uint chainId, ulong batchNumber, CancellationToken cancellationToken = default)
    {
        var result = await RpcContractReader.InvokeReadAsync(_rpc, _settlementManagerHash, "getBatchStatus", new object[] { chainId, batchNumber }, cancellationToken).ConfigureAwait(false);
        var byteValue = RpcContractReader.ParseInteger(result);
        if (byteValue < 0 || byteValue > 4)
            throw new InvalidOperationException($"unexpected status byte {byteValue}");
        return (BatchStatus)byteValue;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _rpc.Dispose();
    }
}
