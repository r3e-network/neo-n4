using System;

namespace Neo.L2.Sdk;

/// <summary>Base type for every <see cref="L2RpcClient"/> failure mode.</summary>
public abstract class L2RpcException : Exception
{
    /// <summary>The RPC method name the failure happened on (e.g. <c>getl2batch</c>).</summary>
    public string Method { get; }

    /// <summary>Construct.</summary>
    protected L2RpcException(string method, string message) : base($"{method}: {message}")
    {
        Method = method;
    }
}

/// <summary>HTTP-layer failure (timeout, connection refused, non-2xx).</summary>
public sealed class L2RpcTransportException : L2RpcException
{
    /// <summary>Construct.</summary>
    public L2RpcTransportException(string method, string message) : base(method, message) { }
}

/// <summary>JSON-RPC envelope or parse failure (bad shape, mismatched id).</summary>
public sealed class L2RpcProtocolException : L2RpcException
{
    /// <summary>Construct.</summary>
    public L2RpcProtocolException(string method, string message) : base(method, message) { }
}

/// <summary>Server-side error returned in the JSON-RPC <c>error</c> field.</summary>
public sealed class L2RpcServerException : L2RpcException
{
    /// <summary>JSON-RPC 2.0 error code.</summary>
    public int Code { get; }

    /// <summary>Construct.</summary>
    public L2RpcServerException(string method, int code, string message)
        : base(method, $"server error {code}: {message}")
    {
        Code = code;
    }
}

/// <summary>
/// Server returned a chainId that does NOT match the one the client was constructed with.
/// Surfaces a config error (wrong endpoint, wrong chainId) rather than letting the caller
/// silently consume cross-chain data.
/// </summary>
public sealed class L2RpcMismatchedChainIdException : L2RpcException
{
    /// <summary>The chainId the client was constructed with.</summary>
    public uint Expected { get; }

    /// <summary>The chainId the server returned in the response.</summary>
    public uint Got { get; }

    /// <summary>Construct.</summary>
    public L2RpcMismatchedChainIdException(string method, uint expected, uint got)
        : base(method, $"server returned chainId {got}, expected {expected}")
    {
        Expected = expected;
        Got = got;
    }
}
