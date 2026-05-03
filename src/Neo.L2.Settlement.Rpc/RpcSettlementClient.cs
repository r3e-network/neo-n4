using Neo.Json;
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
    /// <returns>Tx hash returned by <c>sendrawtransaction</c>.</returns>
    public delegate ValueTask<UInt256> SignAndSendAsync(
        UInt160 settlementManagerHash,
        byte[] commitmentBytes,
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
        var bytes = BatchSerializer.Encode(commitment);
        // Defensive: a buggy signAndSend that returns null UInt256 would propagate as a
        // NRE further downstream (e.g. an L1-tracker that dereferences the tx hash).
        // Same iter-171/172/173 callee-contract pattern.
        var txHash = await _signAndSend(_settlementManagerHash, bytes, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "RpcSettlementClient.SignAndSendAsync delegate returned null tx hash");
        return txHash;
    }

    /// <inheritdoc />
    public async ValueTask<UInt256> GetCanonicalStateRootAsync(uint chainId, CancellationToken cancellationToken = default)
    {
        var result = await InvokeReadAsync("getCanonicalStateRoot", new object[] { chainId }, cancellationToken).ConfigureAwait(false);
        return ParseUInt256(result, "ByteString");
    }

    /// <inheritdoc />
    public async ValueTask<BatchStatus> GetBatchStatusAsync(uint chainId, ulong batchNumber, CancellationToken cancellationToken = default)
    {
        var result = await InvokeReadAsync("getBatchStatus", new object[] { chainId, batchNumber }, cancellationToken).ConfigureAwait(false);
        var byteValue = ParseInteger(result);
        if (byteValue < 0 || byteValue > 4)
            throw new InvalidOperationException($"unexpected status byte {byteValue}");
        return (BatchStatus)byteValue;
    }

    private async ValueTask<JToken?> InvokeReadAsync(string method, object[] args, CancellationToken cancellationToken)
    {
        var paramsArray = new JArray
        {
            _settlementManagerHash.ToString(),
            method,
            BuildParamsArray(args),
        };

        var result = await _rpc.CallAsync("invokefunction", paramsArray, cancellationToken).ConfigureAwait(false);
        if (result is not JObject obj)
            throw new InvalidOperationException("invokefunction returned non-object");

        var state = obj["state"]?.AsString();
        if (state != "HALT")
            throw new InvalidOperationException($"contract call faulted (state={state})");

        if (obj["stack"] is not JArray stack || stack.Count == 0)
            throw new InvalidOperationException("contract call returned empty stack");

        return stack[0];
    }

    private static JArray BuildParamsArray(object[] args)
    {
        var arr = new JArray();
        foreach (var a in args)
        {
            var entry = new JObject();
            switch (a)
            {
                case uint u:
                    entry["type"] = "Integer";
                    entry["value"] = u.ToString();
                    break;
                case ulong ul:
                    entry["type"] = "Integer";
                    entry["value"] = ul.ToString();
                    break;
                case int i:
                    entry["type"] = "Integer";
                    entry["value"] = i.ToString();
                    break;
                case string s:
                    entry["type"] = "String";
                    entry["value"] = s;
                    break;
                case UInt160 h160:
                    entry["type"] = "Hash160";
                    entry["value"] = h160.ToString();
                    break;
                case UInt256 h256:
                    entry["type"] = "Hash256";
                    entry["value"] = h256.ToString();
                    break;
                case byte[] b:
                    entry["type"] = "ByteArray";
                    entry["value"] = Convert.ToBase64String(b);
                    break;
                default:
                    throw new ArgumentException($"Unsupported RPC param type {a?.GetType()}");
            }
            arr.Add(entry);
        }
        return arr;
    }

    private static UInt256 ParseUInt256(JToken? token, string expectedType)
    {
        if (token is not JObject obj) throw new InvalidOperationException("expected JObject for stack item");
        var value = obj["value"]?.AsString() ?? throw new InvalidOperationException("missing 'value'");
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length != 32) throw new InvalidOperationException($"expected 32 bytes, got {bytes.Length}");
        return new UInt256(bytes);
    }

    private static int ParseInteger(JToken? token)
    {
        if (token is not JObject obj) throw new InvalidOperationException("expected JObject for stack item");
        var value = obj["value"]?.AsString() ?? "0";
        if (int.TryParse(value, out var intValue)) return intValue;
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length == 0) return 0;
        // Neo encodes integers as little-endian; interpret first 4 bytes for status byte.
        return bytes[0];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _rpc.Dispose();
    }
}
