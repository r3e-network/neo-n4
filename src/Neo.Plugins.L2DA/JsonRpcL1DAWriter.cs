using Neo.Json;
using Neo.L2;
using Neo.L2.Settlement.Rpc;

namespace Neo.Plugins.L2DA;

/// <summary>
/// <see cref="IDAWriter"/> implementation that publishes batch payloads to an L1 contract
/// via JSON-RPC <c>sendrawtransaction</c>. Used when <c>DAMode.L1</c> is selected — the
/// L2 batch's <c>daCommitment</c> becomes the SHA256 of the published payload, and the
/// pointer carries the L1 transaction hash for off-chain re-fetch.
/// </summary>
/// <remarks>
/// Same shape as <see cref="RpcSettlementClient"/>: takes a <see cref="JsonRpcClient"/>,
/// a target contract hash, and a sign-and-send delegate so the operator can plug in their
/// own wallet (NEP-6, Ledger, etc.) without forcing a particular signer dependency on
/// this library. The DA target contract is operator-supplied — typically a thin wrapper
/// that records (chainId, batchNumber, commitment, pointer) on L1; an obvious candidate
/// is a method on <c>NeoHub.DARegistry</c> or a sibling contract that emits a
/// "blob published" event.
/// </remarks>
public sealed class JsonRpcL1DAWriter : IDAWriter, IDisposable
{
    /// <summary>
    /// Sign-and-send a transaction calling <paramref name="daContractHash"/> with the publish
    /// payload, returning the <c>sendrawtransaction</c> tx hash. Mirrors
    /// <see cref="RpcSettlementClient.SignAndSendAsync"/> so a single signing pipeline can
    /// serve both settlement submission + DA publishing.
    /// </summary>
    public delegate ValueTask<UInt256> SignAndSendAsync(
        UInt160 daContractHash,
        DAPublishRequest request,
        CancellationToken cancellationToken);

    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _daContractHash;
    private readonly SignAndSendAsync _signAndSend;
    private readonly string _isAvailableRpcMethod;
    private bool _disposed;

    /// <summary>Construct.</summary>
    /// <param name="rpc">Connected JSON-RPC client to L1.</param>
    /// <param name="daContractHash">L1 contract that records DA publishes for this L2.</param>
    /// <param name="signAndSend">Operator-supplied callback that builds + signs + sends the
    /// publish transaction; returns the resulting tx hash.</param>
    /// <param name="isAvailableRpcMethod">L1 contract method name to call (read-only) to
    /// confirm a previously-published commitment is still queryable. Defaults to
    /// <c>"isAvailable"</c>; operators with a different DA contract can override.</param>
    public JsonRpcL1DAWriter(
        JsonRpcClient rpc,
        UInt160 daContractHash,
        SignAndSendAsync signAndSend,
        string isAvailableRpcMethod = "isAvailable")
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(signAndSend);
        ArgumentException.ThrowIfNullOrEmpty(isAvailableRpcMethod);
        _rpc = rpc;
        _daContractHash = daContractHash;
        _signAndSend = signAndSend;
        _isAvailableRpcMethod = isAvailableRpcMethod;
    }

    /// <inheritdoc />
    public DAMode Mode => DAMode.L1;

    /// <inheritdoc />
    public async ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Same defensive cap as RpcSettlementClient: a buggy delegate that returns a null
        // tx hash would otherwise propagate as a deep NRE. Surface it here.
        var txHash = await _signAndSend(_daContractHash, request, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "JsonRpcL1DAWriter.SignAndSendAsync delegate returned null tx hash");

        // Commitment = canonical Hash256 of the published payload — same convention as
        // InMemoryDAWriter / NeoFsLikeDAWriter so the off-chain audit
        // (DAAvailabilityCheck) can compare against the L2BatchCommitment.DACommitment
        // unchanged across DA tiers.
        var commitment = new UInt256(Neo.Cryptography.Crypto.Hash256(request.Payload.Span));

        // Pointer = the L1 tx hash, so off-chain consumers can fetch the original payload
        // via getrawtransaction. 32 bytes; preserves UInt256.GetSpan() round-trip.
        var pointer = txHash.GetSpan().ToArray().AsMemory();

        return new DAReceipt
        {
            Commitment = commitment,
            Pointer = pointer,
            Layer = DAMode.L1,
        };
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Skip the round-trip when the receipt is the "no DA" sentinel that some
        // higher-tier paths use (DACommitment = UInt256.Zero on a None-mode batch).
        // Matches the off-chain DAAvailabilityCheck convention.
        if (receipt.Commitment.Equals(UInt256.Zero)) return true;

        var paramsArray = new JArray
        {
            _daContractHash.ToString(),
            _isAvailableRpcMethod,
            BuildIsAvailableParams(receipt.Commitment),
        };
        var result = await _rpc.CallAsync("invokefunction", paramsArray, cancellationToken).ConfigureAwait(false);
        if (result is not JObject obj) return false;

        var state = obj["state"]?.AsString();
        if (state != "HALT") return false;

        if (obj["stack"] is not JArray stack || stack.Count == 0) return false;
        var stackItem = stack[0];
        if (stackItem is not JObject stackObj) return false;

        // Boolean-typed stack item — Neo serializes as type:"Boolean" value:"true"|"false".
        var typeStr = stackObj["type"]?.AsString();
        if (typeStr != "Boolean") return false;
        return stackObj["value"]?.AsString() == "true";
    }

    private static JArray BuildIsAvailableParams(UInt256 commitment)
    {
        var arr = new JArray();
        var entry = new JObject();
        entry["type"] = "Hash256";
        entry["value"] = commitment.ToString();
        arr.Add(entry);
        return arr;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _rpc.Dispose();
    }
}
