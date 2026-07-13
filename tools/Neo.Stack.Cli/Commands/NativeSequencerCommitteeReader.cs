using Neo.Cryptography.ECC;
using Neo.Json;
using Neo.L2.Sequencer;
using Neo.L2.Settlement.Rpc;
using Neo.SmartContract.Native;

namespace Neo.Stack.Cli.Commands;

internal interface INativeSequencerCommitteeReader
{
    Task<IReadOnlyList<ECPoint>> ReadAsync(
        string rpcEndpoint,
        int expectedCount,
        CancellationToken cancellationToken);
}

internal sealed class RpcNativeSequencerCommitteeReader : INativeSequencerCommitteeReader
{
    public async Task<IReadOnlyList<ECPoint>> ReadAsync(
        string rpcEndpoint,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        using var rpc = new JsonRpcClient(rpcEndpoint);
        var stackItem = await RpcContractReader.InvokeReadAsync(
            rpc,
            NativeContract.L2SystemConfig.Hash,
            "getSequencerValidators",
            Array.Empty<object>(),
            cancellationToken).ConfigureAwait(false);
        if (stackItem is not JObject item
            || !string.Equals(item["type"]?.AsString(), "Array", StringComparison.Ordinal)
            || item["value"] is not JArray values)
        {
            throw new InvalidOperationException("getSequencerValidators returned a non-array stack item.");
        }

        var validators = values
            .Select(value => ECPoint.DecodePoint(
                RpcContractReader.ParseByteArray(value),
                ECCurve.Secp256r1))
            .ToArray();
        return SequencerCommitteeTransactionBuilder.Normalize(validators, expectedCount);
    }
}
