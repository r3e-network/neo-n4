using Neo.Json;
using Neo.Plugins.RpcServer;

namespace Neo.Plugins.L2Rpc;

/// <summary>
/// Official <see cref="RpcServer"/> adapter for the L2 RPC surface.
/// </summary>
/// <remarks>
/// See doc.md §14.1. This type only maps stable JSON-RPC names to
/// <see cref="L2RpcMethods"/>; all validation and response construction stays in the
/// existing handler implementation.
/// </remarks>
internal sealed class L2RpcServerAdapter : IDisposable
{
    private readonly L2RpcMethods _methods;
    private int _disposed;

    internal L2RpcServerAdapter(L2RpcMethods methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        _methods = methods;
    }

    [RpcMethod(Name = "getl2batch")]
    public JToken GetL2Batch(JArray parameters) => Invoke(() => _methods.GetL2Batch(parameters));

    [RpcMethod(Name = "getl2batchstatus")]
    public JToken GetL2BatchStatus(JArray parameters) => Invoke(() => _methods.GetL2BatchStatus(parameters));

    [RpcMethod(Name = "getl2stateroot")]
    public JToken GetL2StateRoot(JArray parameters) => Invoke(() => _methods.GetL2StateRoot(parameters));

    [RpcMethod(Name = "getl2withdrawalproof")]
    public JToken GetL2WithdrawalProof(JArray parameters) => Invoke(() => _methods.GetL2WithdrawalProof(parameters));

    [RpcMethod(Name = "getl2messageproof")]
    public JToken GetL2MessageProof(JArray parameters) => Invoke(() => _methods.GetL2MessageProof(parameters));

    [RpcMethod(Name = "getl1depositstatus")]
    public JToken GetL1DepositStatus(JArray parameters) => Invoke(() => _methods.GetL1DepositStatus(parameters));

    [RpcMethod(Name = "getcanonicalasset")]
    public JToken GetCanonicalAsset(JArray parameters) => Invoke(() => _methods.GetCanonicalAsset(parameters));

    [RpcMethod(Name = "getbridgedasset")]
    public JToken GetBridgedAsset(JArray parameters) => Invoke(() => _methods.GetBridgedAsset(parameters));

    [RpcMethod(Name = "getsecuritylevel")]
    public JToken GetSecurityLevel(JArray parameters) => Invoke(() => _methods.GetSecurityLevel(parameters));

    [RpcMethod(Name = "getsecuritylabel")]
    public JToken GetSecurityLabel(JArray parameters) => Invoke(() => _methods.GetSecurityLabel(parameters));

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

    private JToken Invoke(Func<JToken?> invoke)
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(L2RpcPlugin),
                "L2 RPC registration is no longer active because the owning plugin was disposed");
        return invoke() ?? JToken.Null!;
    }
}
